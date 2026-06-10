using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using TodoSnap.Helpers;
using Windows.Media.Ocr;

namespace TodoSnap.Services;

/// <summary>Outcome of an analysis run.</summary>
public record AnalysisResult(string Text, bool Online);

/// <summary>
/// Turns a pasted screenshot into a one-line (or multi-line) to-do list.
/// Strategy: if an api key is configured AND the request succeeds within the
/// timeout, use the online Vision model; otherwise fall back to the offline
/// Windows OCR engine. OCR always works with no network and no dependencies.
/// </summary>
public class AnalysisService
{
    private const int MaxLineLen = 60;
    private const string DefaultEndpoint = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-4o-mini";

    // v1.1.1: task extraction + inference prompt.
    private const string Prompt =
        "你是一个任务提取与推断助手。请分析上传的图片（如微信聊天截图、群发通知截图、Word/PPT/Excel 文件截图等），完成以下任务：\n\n" +
        "1. **识别明确指令**：如果图中包含\"请做\"\"需要你\"\"麻烦你\"等直接要求的语句，直接转化为待办事项。\n" +
        "2. **推断隐含任务**：如果图中只有问题、异议、错误或待确认的信息（例如\"这个PPT里的数据不对\"\"报名截止时间好像写错了\"\"请看一下这个截图\"），请**合理推断**用户可能需要执行的动作，如：修改某个文件的具体问题、核实某个信息、补充某份材料、回复某个人等。\n" +
        "3. **处理模糊情况**：如果完全无法推断具体动作，使用通用但信息尽可能完整的格式：\"处理图中关于 [核心主题] 的事项\" 或 \"核实图中 [可识别的对象] 相关信息\"。\n\n" +
        "**输出要求：**\n" +
        "- 每条待办事项用**行动导向**的语言（以动词开头，如\"修改\"\"确认\"\"回复\"\"补充\"\"核对\"\"提交\"）。\n" +
        "- 尽量让信息**稍详细一点**，例如\"修改第3页PPT的成本数据\"而非\"修改PPT\"。\n" +
        "- 按**重要性（或图中出现的先后顺序）** 排序，每行一条。\n" +
        "- 如果图中没有发现任何可转化为待办的信息（无指令、无问题、无异常），输出\"无待办事项\"。\n\n" +
        "**示例：**\n" +
        "- 微信里有人说：\"这个方案的第二段数据好像有问题。\" → `修改方案第二段数据错误`\n" +
        "- 群发通知写着：\"请各位在下午5点前提交周报。\" → `提交周报至指定邮箱`\n" +
        "- Word截图里有一句话：\"合同里的甲方名称写错了。\" → `更正合同中的甲方名称`\n" +
        "- 模糊场景：一张系统报错截图，没有其他说明 → `处理图中系统报错信息`\n\n" +
        "直接逐行输出待办事项，不要加任何前后说明、标号或装饰符号。";

    private readonly string _primaryKeyPath;
    private readonly string _legacyKeyPath;

    private bool _onlineAvailable;
    private string _lastError = "";

    /// <summary>Fires whenever the AI reachability changes. Subscribers can update UI indicators.</summary>
    public event Action<bool>? OnlineStatusChanged;

    /// <summary>Fires when the AI error message changes (empty string = no error).</summary>
    public event Action<string>? LastErrorChanged;

    /// <summary>The most recent AI-side error message, or empty if the last call succeeded.</summary>
    public string LastError => _lastError;

    public AnalysisService(string dataDir)
    {
        _primaryKeyPath = Path.Combine(dataDir, "apikey.txt");
        _legacyKeyPath = Path.Combine(AppContext.BaseDirectory, "apikey.txt");
        _onlineAvailable = false; // optimistic value is set after the first probe
    }

    /// <summary>True when an api key file with content exists.</summary>
    public bool OnlineConfigured => TryReadApiKey(out _, out _, out _);

    /// <summary>True when the AI is both configured AND the last probe succeeded.</summary>
    public bool OnlineAvailable => _onlineAvailable && OnlineConfigured;

    /// <summary>
    /// Persist the api key (+ optional endpoint/model) to the settings-managed file.
    /// An empty key deletes the file, reverting to offline-only.
    /// </summary>
    public void SaveApiKey(string key, string? endpoint, string? model)
    {
        try
        {
            key = key.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                if (File.Exists(_primaryKeyPath)) File.Delete(_primaryKeyPath);
                SetOnline(false);
                return;
            }

            var lines = new List<string> { key };
            if (!string.IsNullOrWhiteSpace(endpoint)) lines.Add(endpoint!.Trim());
            // Model only makes sense if an endpoint line precedes it (positional format).
            if (!string.IsNullOrWhiteSpace(model))
            {
                if (lines.Count == 1) lines.Add(DefaultEndpoint);
                lines.Add(model!.Trim());
            }
            File.WriteAllLines(_primaryKeyPath, lines);
        }
        catch
        {
            // Non-fatal: a read-only profile just keeps the app offline.
        }
    }

    /// <summary>Read back the currently saved key/endpoint/model for the settings UI.</summary>
    public (string Key, string Endpoint, string Model) ReadApiKey()
    {
        TryReadApiKey(out string key, out string endpoint, out string model);
        return (key, endpoint, model);
    }

    /// <summary>Analyze the image, preferring the online model and falling back to OCR.</summary>
    public async Task<AnalysisResult> AnalyzeAsync(BitmapSource image)
    {
        if (TryReadApiKey(out string key, out string endpoint, out string model))
        {
            try
            {
                string online = await AnalyzeOnlineAsync(image, key, endpoint, model);
                if (!string.IsNullOrWhiteSpace(online))
                {
                    SetOnline(true);
                    SetError("");
                    return new AnalysisResult(CleanMultiLine(online), Online: true);
                }
                SetError("AI 返回为空，已回退本地 OCR");
                SetOnline(false);
            }
            catch (Exception ex)
            {
                // Capture the real failure (404/401/timeout/bad model …) so the UI can
                // surface it instead of silently falling back.
                SetError(ShortMessage(ex));
                SetOnline(false);
            }
        }
        else
        {
            SetOnline(false);
        }

        string ocr = await AnalyzeOcrAsync(image);
        return new AnalysisResult(ocr, Online: false);
    }

    /// <summary>
    /// End-to-end validation: send a minimal chat request to verify the endpoint URL,
    /// the API key, AND the model name in one shot. Costs ≤ 1 output token. Updates
    /// <see cref="OnlineAvailable"/> and <see cref="LastError"/>.
    /// </summary>
    public async Task<bool> CheckConnectivityAsync()
    {
        if (!TryReadApiKey(out string key, out string endpoint, out string model))
        {
            SetError("");
            SetOnline(false);
            return false;
        }
        try
        {
            await PingAsync(key, endpoint, model);
            SetError("");
            SetOnline(true);
            return true;
        }
        catch (Exception ex)
        {
            SetError(ShortMessage(ex));
            SetOnline(false);
            return false;
        }
    }

    private void SetOnline(bool ok)
    {
        if (_onlineAvailable == ok) return;
        _onlineAvailable = ok;
        OnlineStatusChanged?.Invoke(ok);
    }

    private void SetError(string msg)
    {
        if (_lastError == msg) return;
        _lastError = msg;
        LastErrorChanged?.Invoke(msg);
    }

    /// <summary>Trim the noisy HttpRequestException wrapper down to the part a user can act on.</summary>
    private static string ShortMessage(Exception ex)
    {
        string m = ex.Message ?? ex.GetType().Name;
        // HttpClient timeouts show up as "TaskCanceledException" — translate to plain text.
        if (ex is TaskCanceledException) return "请求超时";
        // Long aggregate-exception chains aren't useful in a settings line.
        int nl = m.IndexOf('\n');
        if (nl > 0) m = m[..nl];
        if (m.Length > 160) m = m[..160] + "…";
        return m;
    }

    // ------------------------------------------------------------- offline OCR

    private static async Task<string> AnalyzeOcrAsync(BitmapSource image)
    {
        // Windows OCR needs a SoftwareBitmap in a supported pixel format (Bgra8 here).
        using var bitmap = await ImageHelper.ToSoftwareBitmapAsync(image);

        // Try the user's profile languages first; fall back to any installed OCR language.
        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
        {
            foreach (var lang in OcrEngine.AvailableRecognizerLanguages)
            {
                engine = OcrEngine.TryCreateFromLanguage(lang);
                if (engine != null) break;
            }
        }
        if (engine == null)
            return "未识别到待办（系统缺少 OCR 语言包）";

        var result = await engine.RecognizeAsync(bitmap);
        string text = CleanSingleLine(result.Text);
        return string.IsNullOrWhiteSpace(text) ? "未识别到待办" : text;
    }

    // -------------------------------------------------------------- online API

    private static async Task<string> AnalyzeOnlineAsync(
        BitmapSource image, string apiKey, string endpoint, string model)
    {
        byte[] png = ImageHelper.ToPngBytes(image);
        string base64 = Convert.ToBase64String(png);

        var payload = new
        {
            model,
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = Prompt },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:image/png;base64,{base64}" }
                        }
                    }
                }
            },
            // The new prompt asks for multiple action-oriented lines (10-20 chars each)
            // ordered by importance. Bumped from 50 so the model can return a real list.
            max_tokens = 400,
            temperature = 0.3
        };

        return await PostChatAsync(apiKey, endpoint, payload, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Cheap end-to-end probe: POST a 1-token, text-only chat request to the configured
    /// endpoint. Verifies endpoint URL, auth, and that the model name is accepted by
    /// the server. Throws on any failure with a descriptive message.
    /// </summary>
    private static async Task PingAsync(string apiKey, string endpoint, string model)
    {
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "user", content = "ping" }
            },
            max_tokens = 1,
            temperature = 0.0
        };

        _ = await PostChatAsync(apiKey, endpoint, payload, TimeSpan.FromSeconds(8));
    }

    private static async Task<string> PostChatAsync(
        string apiKey, string endpoint, object payload, TimeSpan timeout)
    {
        string url = NormalizeEndpoint(endpoint);

        using var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        string body = JsonSerializer.Serialize(payload);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");

        using var resp = await http.PostAsync(url, content);
        string respBody = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            // Surface the server's own error reason ({"error": {"message": "..."}})
            // rather than the generic .NET HTTP status text.
            string detail = ExtractServerError(respBody) ?? respBody;
            if (detail.Length > 200) detail = detail[..200] + "…";
            throw new HttpRequestException(
                $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {detail}",
                inner: null,
                statusCode: resp.StatusCode);
        }

        using var doc = JsonDocument.Parse(respBody);
        string? text = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return text ?? "";
    }

    /// <summary>
    /// Accept several common endpoint shapes so the user doesn't have to remember the
    /// full path: bare host, host/v1, or already-complete /chat/completions URL.
    /// </summary>
    internal static string NormalizeEndpoint(string ep)
    {
        if (string.IsNullOrWhiteSpace(ep)) return DefaultEndpoint;
        string s = ep.Trim().TrimEnd('/');
        if (s.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return s;
        if (s.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return s + "/chat/completions";
        return s + "/v1/chat/completions";
    }

    /// <summary>Pull a `{ "error": { "message": "..." } }` field out of an OpenAI-shaped error body.</summary>
    private static string? ExtractServerError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String) return err.GetString();
                if (err.ValueKind == JsonValueKind.Object &&
                    err.TryGetProperty("message", out var msg) &&
                    msg.ValueKind == JsonValueKind.String)
                    return msg.GetString();
            }
            if (doc.RootElement.TryGetProperty("message", out var topMsg) &&
                topMsg.ValueKind == JsonValueKind.String)
                return topMsg.GetString();
        }
        catch
        {
            // Body isn't JSON — let the caller fall back to the raw text.
        }
        return null;
    }

    // ----------------------------------------------------------------- helpers

    /// <summary>
    /// apikey.txt format (one item per line):
    ///   line 1: API key (required)
    ///   line 2: endpoint URL  (optional, defaults to OpenAI)
    ///   line 3: model name    (optional, defaults to gpt-4o-mini)
    /// </summary>
    private bool TryReadApiKey(out string key, out string endpoint, out string model)
    {
        key = "";
        endpoint = DefaultEndpoint;
        model = DefaultModel;

        try
        {
            string path = File.Exists(_primaryKeyPath) ? _primaryKeyPath
                        : File.Exists(_legacyKeyPath) ? _legacyKeyPath
                        : "";
            if (path.Length == 0) return false;

            string[] lines = File.ReadAllLines(path);
            if (lines.Length == 0) return false;

            key = lines[0].Trim();
            if (string.IsNullOrWhiteSpace(key)) return false;

            if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
                endpoint = lines[1].Trim();
            if (lines.Length > 2 && !string.IsNullOrWhiteSpace(lines[2]))
                model = lines[2].Trim();

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>OCR fallback: collapse whitespace, single line, clamped length.</summary>
    private static string CleanSingleLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        string text = Regex.Replace(raw, @"\s+", " ").Trim();
        if (text.Length > MaxLineLen) text = text[..MaxLineLen];
        return text;
    }

    /// <summary>AI output: split into lines, strip bullets/numbering, keep only the
    /// first (most important) line — one todo per image.</summary>
    private static string CleanMultiLine(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // Strip common list decorations: "- ", "* ", "1. ", "1、", "•", backticks, etc.
            string l = line.Trim();
            l = Regex.Replace(l, @"^[\s\-\*••·]+", "");
            l = Regex.Replace(l, @"^\d+\s*[\.\)、:：]\s*", "");
            l = l.Trim('`', ' ', '\t');
            if (l.Length == 0) continue;
            if (l.Length > MaxLineLen) l = l[..MaxLineLen];
            return l; // keep only the first meaningful line
        }
        return "";
    }
}

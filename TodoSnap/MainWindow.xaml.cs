using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TodoSnap.Models;
using TodoSnap.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;

namespace TodoSnap;

/// <summary>
/// The paste-and-distill window. The user pastes a screenshot (Ctrl+V) or drops an
/// image file; it is analyzed into a list of one-line to-dos which the user can edit
/// and add. AI-generated lines are prefixed with "AI:" so they're distinguishable
/// from offline OCR output at a glance.
/// </summary>
public partial class MainWindow : Window
{
    private const string AiPrefix = "AI: ";

    // Status dot palette.
    private static readonly Brush DotGrey = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly Brush DotGreen = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

    private readonly DataService _data;
    private readonly AnalysisService _analysis;
    private readonly FloatingWindow _floating;

    private BitmapSource? _current;

    /// <summary>Raised when the window's HWND is ready (used to host the global hotkey).</summary>
    public event Action<IntPtr>? HandleReady;

    /// <summary>Raised when the user clicks the settings gear.</summary>
    public event Action? SettingsRequested;

    public MainWindow(DataService data, AnalysisService analysis, FloatingWindow floating)
    {
        _data = data;
        _analysis = analysis;
        _floating = floating;
        InitializeComponent();
        ApplyTransparency(_data.Config.BackgroundOpacity);

        // Wire status updates from the analysis service and kick off an initial check.
        _analysis.OnlineStatusChanged += b => Dispatcher.Invoke(() => RefreshAiStatus(b));
        RefreshAiStatus(_analysis.OnlineAvailable);
        _ = _analysis.CheckConnectivityAsync(); // fire-and-forget reachability probe
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HandleReady?.Invoke(new System.Windows.Interop.WindowInteropHelper(this).Handle);
    }

    /// <summary>Put keyboard focus in the editor (called when the window is shown).</summary>
    public void FocusEditor() => TxtDesc.Focus();

    /// <summary>Fade only the background layer (0.6–1.0); text stays fully opaque.</summary>
    public void ApplyTransparency(double opacity) => BgLayer.Opacity = opacity;

    /// <summary>Re-probe AI connectivity (called when the settings window saves a new key).</summary>
    public void RecheckAi() => _ = _analysis.CheckConnectivityAsync();

    private void RefreshAiStatus(bool online)
    {
        if (online)
        {
            AiStatusDot.Fill = DotGreen;
            AiStatusDot.ToolTip = "AI 在线，可正常调用";
            AiStatusLabel.Text = "AI 在线";
        }
        else
        {
            AiStatusDot.Fill = DotGrey;
            AiStatusDot.ToolTip = _analysis.OnlineConfigured
                ? "AI 已配置但当前不可达，将使用本地 OCR"
                : "未配置 AI，将使用本地 OCR";
            AiStatusLabel.Text = "离线 OCR";
        }
    }

    // ------------------------------------------------------------- drag to move

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Only start a drag when the press is on the background, not on a control.
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;
        if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase) return;
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* ignore re-entrant drag */ }
        }
    }

    // ------------------------------------------------------------- paste / drop

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (Clipboard.ContainsImage())
            {
                PasteFromClipboard();
                e.Handled = true; // stop the TextBox from pasting raw text
            }
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void PasteFromClipboard()
    {
        try
        {
            BitmapSource? img = Clipboard.GetImage();
            if (img != null)
                _ = LoadAndAnalyze(img);
        }
        catch
        {
            TxtStatus.Text = "无法读取剪贴板图片";
            TxtStatus.Visibility = Visibility.Visible;
        }
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(files[0]);
                bmp.EndInit();
                bmp.Freeze();
                _ = LoadAndAnalyze(bmp);
            }
            else if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource src)
            {
                _ = LoadAndAnalyze(src);
            }
        }
        catch
        {
            TxtStatus.Text = "无法读取拖入的图片";
            TxtStatus.Visibility = Visibility.Visible;
        }
    }

    // ---------------------------------------------------------------- analysis

    private async Task LoadAndAnalyze(BitmapSource image)
    {
        _current = image;
        ImgPreview.Source = image;
        ImgPreview.Visibility = Visibility.Visible;
        TxtHint.Visibility = Visibility.Collapsed;

        BtnAdd.IsEnabled = false;
        ShowStatus(_analysis.OnlineConfigured ? "AI 分析中…" : "OCR 识别中…");

        try
        {
            AnalysisResult result = await _analysis.AnalyzeAsync(image);
            TxtDesc.Text = FormatForEditor(result);
            TxtDesc.CaretIndex = TxtDesc.Text.Length;
            TxtDesc.Focus();

            // If AI was configured but the call failed, surface the reason instead of
            // silently dropping into OCR — otherwise the user has no way to know why.
            if (!result.Online && _analysis.OnlineConfigured &&
                !string.IsNullOrEmpty(_analysis.LastError))
                ShowStatus("AI 失败，已用本地 OCR：" + _analysis.LastError);
            else
                HideStatus();
        }
        catch (Exception ex)
        {
            TxtDesc.Text = "";
            ShowStatus("分析失败：" + ex.Message);
        }
        finally
        {
            BtnAdd.IsEnabled = true;
        }
    }

    /// <summary>Tag each AI-produced line with the "AI:" marker so the user can tell at a glance.</summary>
    private static string FormatForEditor(AnalysisResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Text)) return "";
        if (!result.Online) return result.Text;

        var lines = result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string s = lines[i].Trim();
            lines[i] = s.StartsWith(AiPrefix, StringComparison.Ordinal) ? s : AiPrefix + s;
        }
        return string.Join("\n", lines);
    }

    private void ShowStatus(string message)
    {
        TxtStatus.Text = message;
        TxtStatus.Visibility = Visibility.Visible;
    }

    private void HideStatus() => TxtStatus.Visibility = Visibility.Collapsed;

    // ------------------------------------------------------------------ actions

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        string raw = TxtDesc.Text;
        if (string.IsNullOrWhiteSpace(raw)) return;

        // One task per non-empty line — the new AI prompt naturally produces multi-line output.
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int added = 0;
        foreach (var line in lines)
        {
            string text = line.Trim();
            if (text.Length == 0) continue;
            _floating.AddTask(new TaskItem { Description = text });
            added++;
        }
        if (added == 0) return;

        ResetForm();
        Hide(); // tuck back into the tray after adding
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => ResetForm();

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void ResetForm()
    {
        _current = null;
        TxtDesc.Text = "";
        ImgPreview.Source = null;
        ImgPreview.Visibility = Visibility.Collapsed;
        TxtHint.Visibility = Visibility.Visible;
        TxtStatus.Visibility = Visibility.Collapsed;
    }
}

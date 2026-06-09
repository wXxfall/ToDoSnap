using System.IO;
using System.Text.Json;
using TodoSnap.Models;

namespace TodoSnap.Services;

/// <summary>
/// Reads and writes the local JSON store under %AppData%\TodoSnap.
/// All file access is guarded by a process-wide lock; writes use
/// FileShare.None so a second instance can't corrupt the file mid-write.
/// </summary>
public class DataService
{
    private static readonly object _gate = new();

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        // Keep non-ASCII (Chinese) readable in the file instead of \uXXXX escapes.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public string Directory { get; }
    public string TasksPath { get; }
    public string ConfigPath { get; }

    /// <summary>In-memory config, loaded once at startup.</summary>
    public AppConfig Config { get; private set; }

    public DataService()
    {
        Directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TodoSnap");
        System.IO.Directory.CreateDirectory(Directory);

        TasksPath = Path.Combine(Directory, "tasks.json");
        ConfigPath = Path.Combine(Directory, "config.json");

        Config = LoadConfig();
    }

    // ----------------------------------------------------------------- tasks

    public List<TaskItem> LoadTasks()
    {
        lock (_gate)
        {
            if (!File.Exists(TasksPath)) return new List<TaskItem>();
            try
            {
                string json = File.ReadAllText(TasksPath);
                return JsonSerializer.Deserialize<List<TaskItem>>(json) ?? new List<TaskItem>();
            }
            catch
            {
                // Corrupt or partially-written file → start clean rather than crash.
                return new List<TaskItem>();
            }
        }
    }

    public void SaveTasks(IEnumerable<TaskItem> tasks)
    {
        lock (_gate)
        {
            string json = JsonSerializer.Serialize(tasks, _json);
            WriteAllTextExclusive(TasksPath, json);
        }
    }

    // ---------------------------------------------------------------- config

    public AppConfig LoadConfig()
    {
        lock (_gate)
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();
            try
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }
    }

    public void SaveConfig()
    {
        lock (_gate)
        {
            string json = JsonSerializer.Serialize(Config, _json);
            WriteAllTextExclusive(ConfigPath, json);
        }
    }

    /// <summary>Write with an exclusive handle so concurrent writers fail fast instead of interleaving.</summary>
    private static void WriteAllTextExclusive(string path, string contents)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sw = new StreamWriter(fs);
        sw.Write(contents);
    }
}

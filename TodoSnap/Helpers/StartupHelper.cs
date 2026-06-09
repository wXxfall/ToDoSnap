using System.IO;
using Microsoft.Win32;

namespace TodoSnap.Helpers;

/// <summary>
/// Toggles "run at login" via the per-user Run registry key. This is lighter and
/// more reliable than dropping a .lnk in the Startup folder and needs no COM.
/// </summary>
public static class StartupHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TodoSnap";

    // Environment.ProcessPath is the real exe path even in a single-file app;
    // the BaseDirectory fallback keeps it valid if ProcessPath is ever null.
    private static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TodoSnap.exe");

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string v && !string.IsNullOrEmpty(v);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;

            if (enabled)
                key.SetValue(ValueName, $"\"{ExePath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // Non-fatal: a locked-down machine may forbid writing the Run key.
        }
    }
}

using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace TodoSnap.Helpers;

/// <summary>
/// Registers a single system-wide hotkey via Win32 <c>RegisterHotKey</c> and
/// raises <see cref="Triggered"/> when it fires. The hotkey is bound to a host
/// window's HWND (we use MainWindow's — its handle survives <c>Hide()</c>), so
/// it keeps working while the app sits in the tray.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xB001;

    // fsModifiers flags for RegisterHotKey.
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004,
                       MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private IntPtr _hwnd;
    private HwndSource? _source;
    private bool _registered;

    /// <summary>Raised on the UI thread when the registered hotkey is pressed.</summary>
    public event Action? Triggered;

    /// <summary>
    /// Attach to a host window's message loop. Call once the window's HWND exists
    /// (e.g. from <c>OnSourceInitialized</c>). Idempotent.
    /// </summary>
    public void Attach(IntPtr hwnd)
    {
        if (_source != null || hwnd == IntPtr.Zero) return;
        _hwnd = hwnd;
        _source = HwndSource.FromHwnd(hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>
    /// (Re)register the hotkey. Returns false if the combo is invalid or already
    /// taken by another app (so the settings UI can warn about the conflict).
    /// </summary>
    public bool TryRegister(ModifierKeys mods, Key key)
    {
        if (_hwnd == IntPtr.Zero) return false;
        if (key == Key.None) return false;

        Unregister();

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return false;

        uint fs = MOD_NOREPEAT;
        if (mods.HasFlag(ModifierKeys.Alt)) fs |= MOD_ALT;
        if (mods.HasFlag(ModifierKeys.Control)) fs |= MOD_CONTROL;
        if (mods.HasFlag(ModifierKeys.Shift)) fs |= MOD_SHIFT;
        if (mods.HasFlag(ModifierKeys.Windows)) fs |= MOD_WIN;

        _registered = RegisterHotKey(_hwnd, HotkeyId, fs, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered && _hwnd != IntPtr.Zero)
            UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Triggered?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    // ------------------------------------------------------------ config <-> keys

    /// <summary>Parse the persisted ("Control+Shift", "OemTilde") form into key objects.</summary>
    public static (ModifierKeys Mods, Key Key) Parse(string? mods, string? key)
    {
        ModifierKeys m = ModifierKeys.None;
        foreach (var part in (mods ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Enum.TryParse<ModifierKeys>(part, true, out var mk)) m |= mk;

        Key k = Enum.TryParse<Key>(key, true, out var parsed) ? parsed : Key.None;
        return (m, k);
    }

    /// <summary>Serialize key objects into the persisted form for config.json.</summary>
    public static (string Mods, string Key) ToConfig(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Control");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");
        return (string.Join("+", parts), key.ToString());
    }

    /// <summary>Human-friendly label, e.g. "Alt + `" or "Ctrl + Shift + T".</summary>
    public static string Describe(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (key != Key.None) parts.Add(KeyLabel(key));
        return parts.Count == 0 ? "未设置" : string.Join(" + ", parts);
    }

    private static string KeyLabel(Key key) => key switch
    {
        Key.OemTilde => "`",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Space => "Space",
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        _ => key.ToString()
    };
}

namespace TodoSnap.Models;

/// <summary>User settings persisted to config.json.</summary>
public class AppConfig
{
    /// <summary>Saved floating-bar position. -1 means "not set yet" → use default position.</summary>
    public double FloatingLeft { get; set; } = -1;
    public double FloatingTop { get; set; } = -1;

    /// <summary>User-chosen floating-bar size (expanded state).</summary>
    public double FloatingWidth { get; set; } = 260;
    public double FloatingHeight { get; set; } = 320;

    /// <summary>Whether the floating bar is shown.</summary>
    public bool FloatingVisible { get; set; } = true;

    /// <summary>Whether the floating bar is currently collapsed into an edge strip.</summary>
    public bool FloatingCollapsed { get; set; } = false;

    /// <summary>Edge the bar is docked to when collapsed: None / Left / Right / Top / Bottom.</summary>
    public string DockEdge { get; set; } = "None";

    /// <summary>Allow falling forward to the online Vision API when an api key is present.</summary>
    public bool EnableOnline { get; set; } = true;

    // ----------------------------------------------------------------- appearance

    /// <summary>Active theme id: "dark" or "light".</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Opacity of the window background layer only (0.6–1.0). Text stays fully opaque.</summary>
    public double BackgroundOpacity { get; set; } = 0.9;

    // ------------------------------------------------------------------- hotkey

    /// <summary>Global hotkey modifiers as a "+"-joined ModifierKeys string, e.g. "Alt" or "Control+Shift".</summary>
    public string HotkeyMods { get; set; } = "Alt";

    /// <summary>Global hotkey key as a System.Windows.Input.Key name, e.g. "OemTilde".</summary>
    public string HotkeyKey { get; set; } = "OemTilde";
}

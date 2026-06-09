using System.Windows;

namespace TodoSnap.Helpers;

/// <summary>
/// Swaps the active theme <see cref="ResourceDictionary"/> in
/// <c>Application.Current.Resources</c>. Because every window color is a
/// <c>DynamicResource</c> pointing at a token key (e.g. <c>Brush.WindowBg</c>),
/// replacing the dictionary re-skins the whole app live, with no restart.
/// </summary>
public static class ThemeManager
{
    /// <summary>Currently applied theme id ("dark" / "light").</summary>
    public static string Current { get; private set; } = "dark";

    private static Uri UriFor(string theme) =>
        new($"pack://application:,,,/Themes/{(theme == "light" ? "Light" : "Dark")}.xaml",
            UriKind.Absolute);

    /// <summary>Apply the theme by its id ("dark" or "light").</summary>
    public static void Apply(string? theme)
    {
        theme = theme == "light" ? "light" : "dark";
        var app = Application.Current;
        if (app == null) return;

        var dict = new ResourceDictionary { Source = UriFor(theme) };

        // Drop any previously-added theme dictionary (identified by its /Themes/ source).
        var merged = app.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source;
            if (src != null && src.OriginalString.Contains("/Themes/", StringComparison.OrdinalIgnoreCase))
                merged.RemoveAt(i);
        }

        merged.Add(dict);
        Current = theme;
    }
}

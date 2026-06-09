using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace TodoSnap.Helpers;

/// <summary>
/// Builds the tray icon at runtime (a green circle with a white check), so the
/// app needs no binary .ico asset to function. If Resources\app.ico exists next
/// to the exe it is used instead, giving a crisper multi-resolution icon.
/// </summary>
public static class TrayIconFactory
{
    public static Icon Create()
    {
        string icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app.ico");
        if (File.Exists(icoPath))
        {
            try { return new Icon(icoPath); }
            catch { /* fall through to drawn icon */ }
        }
        return Draw();
    }

    private static Icon Draw()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var fill = new SolidBrush(Color.FromArgb(0x4C, 0xAF, 0x50));
            g.FillEllipse(fill, 1, 1, 30, 30);

            using var pen = new Pen(Color.White, 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawLines(pen, new[]
            {
                new Point(9, 16),
                new Point(14, 22),
                new Point(23, 10)
            });
        }

        // Icon.FromHandle requires the HICON to outlive the Icon; the tray keeps it
        // for the whole app lifetime, so leaking it here is acceptable.
        return Icon.FromHandle(bmp.GetHicon());
    }
}

"""Generate Resources/app.ico (green circle + white check) at several sizes.

Run once:  python Resources/gen_icon.py
Requires Pillow (pip install pillow). The .exe and tray icon pick this up
automatically; if it's missing the app draws a fallback icon at runtime.
"""
import os
from PIL import Image, ImageDraw

GREEN = (76, 175, 80, 255)
WHITE = (255, 255, 255, 255)


def render(size: int) -> Image.Image:
    # Supersample for smooth edges, then downscale.
    scale = 8
    s = size * scale
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = int(s * 0.04)
    d.ellipse([pad, pad, s - pad, s - pad], fill=GREEN)

    # Check mark.
    w = max(2, int(s * 0.09))
    pts = [(s * 0.28, s * 0.52), (s * 0.44, s * 0.68), (s * 0.72, s * 0.32)]
    d.line(pts, fill=WHITE, width=w, joint="curve")
    # Round the line caps.
    r = w // 2
    for (x, y) in (pts[0], pts[2]):
        d.ellipse([x - r, y - r, x + r, y + r], fill=WHITE)

    return img.resize((size, size), Image.LANCZOS)


def main() -> None:
    out = os.path.join(os.path.dirname(__file__), "app.ico")
    sizes = [16, 24, 32, 48, 64, 128, 256]
    base = render(256)
    base.save(out, format="ICO", sizes=[(n, n) for n in sizes])
    print("wrote", out)


if __name__ == "__main__":
    main()

"""Draws the Spence app icon (same design as spence-ai-web/public/favicon.svg) into SpenceAI.WebApi/spence.ico.

Run from the repo root when the logo changes:  python packaging/make-icon.py   (needs Pillow)
"""
from pathlib import Path

from PIL import Image, ImageDraw

S = 32 * 32  # draw at 1024 px, then downsample for smooth edges
k = S / 32


def box(x, y, w, h):
    return [x * k, y * k, (x + w) * k, (y + h) * k]


img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle(box(0, 0, 32, 32), radius=8 * k, fill="#1E5A46")
d.rounded_rectangle(box(6, 19, 7, 6), radius=1.5 * k, fill="#C99A2E")
d.rounded_rectangle(box(14, 19, 5, 6), radius=1.5 * k, fill="#EEF0E9")
d.rounded_rectangle(box(20, 19, 6, 6), radius=1.5 * k, fill="#7FB89F")

# The curve "M8 13.5 c0-2.5 2.2-4 5-4 h11": a cubic from (8,13.5) to (13,9.5), then a line to (24,9.5).
p0, p1, p2, p3 = (8, 13.5), (8, 11), (10.2, 9.5), (13, 9.5)
def curve(t):
    u = 1 - t
    return (u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
            u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1])


# Stamp round dots densely along the path: a smooth stroke with round caps.
pts = [curve(i / 600) for i in range(601)] + [(13 + 11 * i / 600, 9.5) for i in range(601)]
r = 1.2 * k
for x, y in pts:
    d.ellipse([x * k - r, y * k - r, x * k + r, y * k + r], fill="#EEF0E9")

out = Path(__file__).resolve().parent.parent / "SpenceAI.WebApi" / "spence.ico"
img.resize((256, 256), Image.LANCZOS).save(out, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)])
print(f"Wrote {out}")

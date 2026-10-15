"""Buttons for the character sheet, drawn smooth at the canvas's own resolution (one pixel = one canvas unit, so they
are imported at 100 pixels per unit), in the sheet's ink rather than as boxed pixel frames:

  Ink tag.png    a price tag: a pointed left end with a punched hole, an inked outline and a white face (Buy).
                 9-sliced: left 26, right 12, top and bottom 14.
  Ink stamp.png  a rubber-stamp border: a double inked rectangle with worn, broken edges, white so the Image tints it
                 (Put down, Refund all). 9-sliced at 16.

    python -I Tools/meta_sheet_buttons.py Assets/Game/UI/Sprites/Meta
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter

out = sys.argv[1]
SS = 4                                  # drawn this many times larger, then brought down (smooth edges)
INK = (43, 39, 36)
rng = np.random.default_rng(23)


def down(img, w, h):
    return img.resize((w, h), Image.LANCZOS)


# ── Ink tag (72 x 48) ────────────────────────────────────────────────
W, H = 72, 48
big = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
d = ImageDraw.Draw(big)
s = SS
body = [(2 * s, 24 * s), (18 * s, 4 * s), (W * s - 8 * s, 4 * s), (W * s - 3 * s, 9 * s), (W * s - 3 * s, 39 * s), (W * s - 8 * s, 44 * s), (18 * s, 44 * s)]
d.polygon(body, fill=(255, 255, 255, 255))
d.line(body + [body[0]], fill=INK + (255,), width=int(2.6 * s), joint="curve")
d.ellipse([11 * s, 20 * s, 18 * s, 27 * s], fill=(0, 0, 0, 0), outline=INK + (255,), width=int(1.6 * s))
tag = down(big, W, H)
a = np.asarray(tag).astype(np.float32)
# a hole really cut through
yy, xx = np.mgrid[0:H, 0:W]
hole = (xx - 14.5) ** 2 + (yy - 23.5) ** 2 < 2.2 ** 2
a[hole, 3] = 0
Image.fromarray(a.clip(0, 255).astype(np.uint8)).save(os.path.join(out, "Ink tag.png"))

# ── Ink stamp (56 x 56) ──────────────────────────────────────────────
W = H = 56
big = Image.new("L", (W * SS, H * SS), 0)
d = ImageDraw.Draw(big)
d.rectangle([3 * s, 3 * s, W * s - 4 * s, H * s - 4 * s], outline=255, width=int(3 * s))
d.rectangle([8 * s, 8 * s, W * s - 9 * s, H * s - 9 * s], outline=255, width=int(1.2 * s))
mask = np.asarray(down(big.convert("RGB"), W, H).convert("L")).astype(np.float32) / 255
# worn: the ink breaks up where the paper's tooth didn't take it
wear = gaussian_filter(rng.normal(0, 1, (H, W)), .8)
mask *= np.clip(1.2 + wear * .7, 0, 1)
rgba = np.zeros((H, W, 4), np.uint8)
rgba[..., :3] = 255
rgba[..., 3] = (mask * 255).clip(0, 255).astype(np.uint8)
Image.fromarray(rgba).save(os.path.join(out, "Ink stamp.png"))
print("wrote Ink tag.png, Ink stamp.png")

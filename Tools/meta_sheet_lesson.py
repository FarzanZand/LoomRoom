"""The character sheet's first lesson (train the lesson skill first): the shapes of its tutorial window and highlight,
drawn smooth like the sheet's other shapes.

  Marker stroke.png  a highlighter drawn along a row in three passes: uneven edges, ragged ends, streaks along the
                     stroke. White with alpha, so the Image tints it. 1110 x 84, one pixel = one canvas unit.
  Tip tail.png       the tutorial window's pointer: a triangle in the window's fill with the pen's line down its two
                     sides, open at its base (the base lies over the window's frame line). 72 x 48, drawn at twice
                     its canvas size (200 pixels per unit).
  Tip shadow.png     a soft shadow under the window. 96 x 96, 9-sliced at 40.

    python -I Tools/meta_sheet_lesson.py Assets/Game/UI/Sprites/Meta
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, gaussian_filter1d

out = sys.argv[1]
rng = np.random.default_rng(41)

# ── Marker stroke (1110 x 84) ────────────────────────────────────────
W, H = 1110, 84
yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
alpha = np.zeros((H, W), np.float32)
# three passes of a chisel tip, each a little off line and ending where the hand stopped
passes = [(15.0, 30.0, 10, W - 26), (41.0, 31.0, 2, W - 8), (67.0, 29.0, 18, W - 40)]
for centre, width, x0, x1 in passes:
    drift = gaussian_filter1d(rng.normal(0, 1, W), 120) * 14            # the line wanders by a pixel or two
    top = centre - width / 2 + drift + gaussian_filter1d(rng.normal(0, 1, W), 9) * 1.6
    bottom = centre + width / 2 + drift + gaussian_filter1d(rng.normal(0, 1, W), 9) * 1.6
    band = np.clip(yy - top[None, :] + .5, 0, 1) * np.clip(bottom[None, :] - yy + .5, 0, 1)
    # ragged ends: each row of the tip lifts off at its own x
    start = x0 + gaussian_filter1d(rng.normal(0, 1, H), 2.5) * 16
    end = x1 + gaussian_filter1d(rng.normal(0, 1, H), 2.5) * 22
    band *= np.clip((xx - start[:, None]) / 3, 0, 1) * np.clip((end[:, None] - xx) / 5, 0, 1)
    # streaks along the stroke where the felt ran dry
    streak = gaussian_filter(rng.normal(0, 1, (H, W)), (.7, 60))
    streak = streak / (np.abs(streak).max() + 1e-6)
    band *= np.clip(.86 + streak * .55, .45, 1)
    alpha = alpha + band * (1 - alpha * .55)                             # overlaps are a little denser, as ink is
alpha = np.clip(alpha, 0, 1)
tooth = gaussian_filter(rng.normal(0, 1, (H, W)), .9)                    # the paper's tooth
alpha *= np.clip(.95 + tooth * .22, .7, 1)
rgba = np.zeros((H, W, 4), np.uint8)
rgba[..., :3] = 255
rgba[..., 3] = (alpha * 255).clip(0, 255).astype(np.uint8)
Image.fromarray(rgba).save(os.path.join(out, "Marker stroke.png"))

# ── Tip tail (36 x 24 canvas units, drawn at 2x) ─────────────────────
SS = 4
W, H = 72, 48
FILL = (251, 249, 244)
PEN = (148, 41, 31)
big = Image.new("RGBA", (W * SS, H * SS), (0, 0, 0, 0))
d = ImageDraw.Draw(big)
apex = (W * SS / 2, 5 * SS)
left = (5 * SS, H * SS + 4 * SS)
right = (W * SS - 5 * SS, H * SS + 4 * SS)
d.polygon([left, apex, right], fill=FILL + (255,))
d.line([left, apex, right], fill=PEN + (255,), width=int(4.4 * SS), joint="curve")
Image.fromarray(np.asarray(big.resize((W, H), Image.LANCZOS))).save(os.path.join(out, "Tip tail.png"))

# ── Tip shadow (96 x 96, 9-sliced at 40) ─────────────────────────────
W = H = 96
m = np.zeros((H, W), np.float32)
m[22:74, 22:74] = 1
m = gaussian_filter(m, 9)
rgba = np.zeros((H, W, 4), np.uint8)
rgba[..., 3] = (m / m.max() * 255).astype(np.uint8)
Image.fromarray(rgba).save(os.path.join(out, "Tip shadow.png"))
print("wrote Marker stroke.png, Tip tail.png, Tip shadow.png")

"""The cobweb card (Levels/Shared/Materials/Cobweb.png, on Levels/Shared/Props/Furnishing/Cobweb: a 1.5 x 0.75 m card) at
the dungeon's 128 texels a metre, 192 x 96 (the old 48 x 24 drew each thread four times as thick as a texel of the walls
round it). Hung from its top edge: threads fan down from where it is caught, joined by sagging rings, one texel wide, in
dusty greys that vary texel to texel, a few threads broken off, dust caught in clumps. Alpha is cut (the material clips
at .3), so a texel is thread or nothing.
    python Tools/cobweb_texture.py"""
import math
import numpy as np
from PIL import Image

W, H = 192, 96
OUT = "Assets/Game/Levels/Shared/Materials/Cobweb.png"
rng = np.random.default_rng(23)
img = np.zeros((H, W, 4), np.uint8)


def put(x, y, shade=0):
    x, y = int(round(x)), int(round(y))
    if 0 <= x < W and 0 <= y < H:
        v = int(np.clip(196 + rng.integers(-18, 18) + shade, 120, 232))
        img[y, x] = (v, v - 4, v - 10, 255)


def line(x0, y0, x1, y1, gap=0.0):
    n = int(max(abs(x1 - x0), abs(y1 - y0))) + 1
    for i in range(n):
        if gap and rng.random() < gap: continue
        t = i / max(1, n - 1)
        put(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t)


# Where it is caught: along the top edge, the middle a little in.
cx, cy = W / 2, 2
spokes = [math.radians(a) for a in np.linspace(14, 166, 11) + rng.uniform(-5, 5, 11)]
ends = []
for a in spokes:
    reach = rng.uniform(.8, 1.0)
    ex, ey = cx + math.cos(a) * (W / 2 - 3) * reach, cy + math.sin(a) * (H - 6) * reach
    ends.append((ex, ey))
    line(cx, cy, ex, ey, gap=.02 if rng.random() < .3 else 0)
# A few threads up to the edge it hangs from, either side.
for sx in (-1, 1):
    for k in range(2):
        line(cx + sx * rng.uniform(10, 30), 0, cx + sx * rng.uniform(50, 90), rng.uniform(4, 14))
# Rings, sagging between the spokes, wider apart further out; some broken off.
for r in np.linspace(.12, .95, 8) + rng.uniform(-.02, .02, 8):
    for i in range(len(spokes) - 1):
        if rng.random() < .1: continue
        (ax, ay), (bx, by) = ends[i], ends[i + 1]
        p0 = (cx + (ax - cx) * r, cy + (ay - cy) * r); p1 = (cx + (bx - cx) * r, cy + (by - cy) * r)
        mid = ((p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2 + 2 + 3 * r)
        line(*p0, *mid, gap=.03); line(*mid, *p1, gap=.03)
# Dust caught in it: small clumps, darker.
ys, xs = np.nonzero(img[..., 3])
for k in rng.choice(len(ys), 14, replace=False):
    for dy in (0, 1):
        for dx in (0, 1):
            if rng.random() < .7: put(xs[k] + dx, ys[k] + dy, -60)
Image.fromarray(img, "RGBA").save(OUT)
print(OUT, W, "x", H)

"""Night sky for the bedroom's gothic window (the mood pass): a pixel picture at the bedroom's 6.4 texels a unit, cut to
the window's inner arch (transparent outside it). Deep blue sky lighter toward the horizon, a few stars, a pale moon
with a soft halo, a far hill line and a treeline in near-black blue, ivy hanging in from the top.
python Tools/night_window.py <out.png>"""
import math, random, sys
import numpy as np
from PIL import Image

out = sys.argv[1]
TPU = 6.4
W_OUT, H_OUT, FR = 34.0, 56.0, 2.2           # the window (Tools/gothic_window.py)
iw, ih = W_OUT - 2 * FR, H_OUT - 2 * FR       # inner opening, units
PX, PY = int(round(iw * TPU)), int(round(ih * TPU))
rng = random.Random(5)

def inside_arch(x, y):
    """x in units from the middle, y in units from the bottom of the inner opening; the inner opening is the outer arch
    scaled (as Tools/gothic_window.py makes it), so the test maps back to the outer arch"""
    X, Y = x * W_OUT / iw, y * H_OUT / ih
    r, spring = W_OUT * .62, H_OUT - W_OUT * .62
    if abs(X) > W_OUT / 2 or Y < 0: return False
    if Y <= spring: return True
    return (X - (-W_OUT / 2 + r)) ** 2 + (Y - spring) ** 2 <= r * r and (X - (W_OUT / 2 - r)) ** 2 + (Y - spring) ** 2 <= r * r

img = np.zeros((PY, PX, 4), np.float32)
top, hor = np.array([.035, .05, .12]), np.array([.16, .24, .42])
moon = (PX * .25, PY * .5); mr = PX * .07
for j in range(PY):
    t = j / (PY - 1)                           # 0 top, 1 bottom
    base = top * (1 - t ** 1.4) + hor * t ** 1.4
    for i in range(PX):
        x = (i + .5) / TPU - iw / 2; y = ih - (j + .5) / TPU
        if not inside_arch(x, y): continue
        c = base.copy()
        d = math.hypot(i - moon[0], j - moon[1])
        c += np.array([.35, .42, .55]) * max(0.0, 1 - d / (mr * 4.5)) ** 2 * .55           # halo
        if d < mr: c = np.array([.86, .88, .9]) * (.93 + .07 * ((i + j) % 3 == 0))         # the moon, a little grain
        if d < mr and (i - moon[0] + mr * .35) ** 2 + (j - moon[1] + mr * .1) ** 2 < (mr * .3) ** 2: c *= .85   # a mare
        c *= .94 + .12 * rng.random()          # texel to texel variation
        img[j, i, :3] = c; img[j, i, 3] = 1
# stars
for _ in range(int(PX * PY * .004)):
    i, j = rng.randrange(PX), rng.randrange(int(PY * .55))
    if img[j, i, 3] > 0 and math.hypot(i - moon[0], j - moon[1]) > mr * 2.2:
        img[j, i, :3] = np.array([.75, .8, .95]) * rng.uniform(.6, 1)
# far hills, then the treeline (pointed firs), both blue-black
hill = [PY * .74 + PY * .04 * math.sin(i / PX * 5.1 + 1) + PY * .02 * math.sin(i / PX * 13) for i in range(PX)]
tree = [PY * .84 for _ in range(PX)]
i = 0
while i < PX:
    w = rng.randrange(12, 22); h = rng.uniform(PY * .05, PY * .11)
    for k in range(w):
        if i + k < PX: tree[i + k] = min(tree[i + k], PY * .86 - h * (1 - abs(k - w / 2) / (w / 2)))
    i += rng.randrange(7, 14)
for i in range(PX):
    for j in range(PY):
        if img[j, i, 3] == 0: continue
        if j >= tree[i]: img[j, i, :3] = np.array([.02, .03, .06]) * (.9 + .2 * rng.random())
        elif j >= hill[i]: img[j, i, :3] = np.array([.06, .08, .16]) * (.9 + .2 * rng.random())
# ivy hanging in from the top corners
for cx in (int(PX * .12), int(PX * .88), int(PX * .5)):
    for s in range(rng.randrange(3, 6)):
        x = cx + rng.randrange(-12, 13); length = rng.randrange(int(PY * .1), int(PY * .3))
        for j in range(length):
            x += rng.choice((-1, 0, 0, 1))
            for dx in range(-1, 2):
                xx = x + dx
                if 0 <= xx < PX and img[j, xx, 3] > 0 and (dx == 0 or rng.random() < .35):
                    img[j, xx, :3] = np.array([.03, .06, .05]) * (.8 + .4 * rng.random())
Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), 'RGBA').save(out)
print('night window', PX, 'x', PY)

"""Texture for the Dungeon Master's die (Tools/dm_die.py lays out its UVs: a 3 x 2 atlas of cells a little larger than
the face's SIZE * 128 texels). Old yellowed bone in the house style: blotchy colour varying texel to texel in
small clusters, grime worked into the rounded edges where they meet the face, worn pale spots and chips on the edges,
hairline cracks, and dark dished pips with a dark crease round their rim and a lit far side inside.
python dm_die_texture.py out.png"""
import sys
import numpy as np
from PIL import Image

SIZE, TEXELS = .8, 128          # as Tools/dm_die.py
K = SIZE / .6
CELL = int(SIZE * TEXELS) + 4
W, H = 3 * CELL, 2 * CELL
HALF, BEVEL, PIP_R, SPREAD = SIZE / 2, .065 * K, .052 * K, .155 * K
rng = np.random.default_rng(6)

def pips(value):
    s = SPREAD
    return {1: [(0, 0)], 2: [(-s, s), (s, -s)], 3: [(-s, s), (0, 0), (s, -s)], 4: [(-s, s), (s, s), (-s, -s), (s, -s)],
            5: [(-s, s), (s, s), (0, 0), (-s, -s), (s, -s)], 6: [(-s, s), (-s, 0), (-s, -s), (s, s), (s, 0), (s, -s)]}[value]

def noise(shape, cells, seed):
    """Value noise: a coarse random grid blown up with a smooth step, so blotches have soft but uneven shapes."""
    r = np.random.default_rng(seed)
    g = r.random((cells + 2, cells + 2))
    ys, xs = np.mgrid[0:shape[0], 0:shape[1]]
    fy, fx = ys / shape[0] * cells, xs / shape[1] * cells
    iy, ix = fy.astype(int), fx.astype(int)
    ty, tx = fy - iy, fx - ix
    ty, tx = ty * ty * (3 - 2 * ty), tx * tx * (3 - 2 * tx)
    a = g[iy, ix] * (1 - tx) + g[iy, ix + 1] * tx
    b = g[iy + 1, ix] * (1 - tx) + g[iy + 1, ix + 1] * tx
    return a * (1 - ty) + b * ty

out = sys.argv[1]
img = np.zeros((H, W, 3))
blot = noise((H, W), round(9 * K), 1) * .6 + noise((H, W), round(22 * K), 2) * .4
stain = noise((H, W), round(6 * K), 3)
grain = rng.random((H, W))
fine = noise((H, W), round(40 * K), 4) * .7 + rng.random((H, W)) * .3   # small clusters a few texels across

bone_light = np.array([.83, .78, .63])
bone_mid = np.array([.78, .72, .57])
bone_dark = np.array([.70, .63, .48])
grime = np.array([.36, .29, .20])
pip_dark = np.array([.11, .08, .06])
pip_lit = np.array([.26, .19, .13])

for value in range(1, 7):
    col, row = (value - 1) % 3, (value - 1) // 3
    for py in range(row * CELL, row * CELL + CELL):
        for px in range(col * CELL, col * CELL + CELL):
            a = (px + .5 - col * CELL - CELL / 2) / TEXELS
            b = -(py + .5 - row * CELL - CELL / 2) / TEXELS
            # bone: three tones laid by the blotch field in clusters, nudged per texel
            t = blot[py, px] + (fine[py, px] - .5) * .22
            c = bone_light if t > .55 else bone_mid if t > .3 else bone_dark
            c = c * (1 + (grain[py, px] - .5) * .035)
            # old yellow-brown stains
            if stain[py, px] + (fine[py, px] - .5) * .12 > .7: c = c * np.array([.97, .93, .85])
            # the rounded edge: worn pale on its crest, grime in the crease where it meets the face
            e = HALF - max(abs(a), abs(b))
            if e < BEVEL * .55:
                c = c * (1.06 if fine[py, px] > .38 else .84)
            elif e < BEVEL * 1.15:
                k = 1 - abs(e - BEVEL * .9) / (BEVEL * .4)
                if k > 0 and fine[py, px] < .3 + .4 * k: c = c * .72 + grime * .28
            # pips: dark dish, a crease of grime round the rim, the far side of the dish lit
            for (qa, qb) in pips(value):
                d = np.hypot(a - qa, b - qb)
                if d < PIP_R:
                    lit = (a - qa) * .7 - (b - qb) * .7   # lower right side faces the light inside the dish
                    c = pip_lit if lit > PIP_R * .35 and d > PIP_R * .45 else pip_dark
                    c = c * (1 + (grain[py, px] - .5) * .25)
                elif d < PIP_R + 1.6 / TEXELS:
                    c = c * .62 + grime * .38
            img[py, px] = c

# chips and cracks: a few dark nicks on the edges and thin random cracks running in from them
for value in range(1, 7):
    col, row = (value - 1) % 3, (value - 1) // 3
    x0, y0 = col * CELL, row * CELL
    for _ in range(5):
        side = rng.integers(4); t = rng.integers(10, CELL - 10)
        x, y = [(x0 + t, y0 + 3), (x0 + t, y0 + CELL - 4), (x0 + 3, y0 + t), (x0 + CELL - 4, y0 + t)][side]
        for dy in range(-1, 2):
            for dx in range(-1, 2):
                if rng.random() < .6: img[y + dy, x + dx] *= .7
    for _ in range(2):
        side = rng.integers(4); t = rng.integers(14, CELL - 14)
        x, y = [(x0 + t, y0 + 4), (x0 + t, y0 + CELL - 5), (x0 + 4, y0 + t), (x0 + CELL - 5, y0 + t)][side]
        inward = [(0, 1), (0, -1), (1, 0), (-1, 0)][side]
        for step in range(rng.integers(6, 14)):
            if x0 < x < x0 + CELL - 1 and y0 < y < y0 + CELL - 1: img[y, x] = img[y, x] * .62 + grime * .38
            x += inward[0] + (rng.integers(-1, 2) if inward[0] == 0 else 0)
            y += inward[1] + (rng.integers(-1, 2) if inward[1] == 0 else 0)

Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(out)
print('wrote', out, W, H)

"""The frozen enemy's particles (Combat/Spells/Frost Lance/Frost/, used by FrostCoat on the Frost Lance Chill effect):
Frost breath.png, four ragged puffs of cold breath side by side (16 x 16 texels each), and Rime flake.png, four small
ice flakes (8 x 8 each). Pixel art in the house style: hard-edged shapes, alpha in three steps (no airbrushed fade),
value varying in small clusters, a lit upper-left edge. White-ish, so the particle colour tints them.

    python Tools/frost_particles.py "<out folder>"
"""
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1]
rng = np.random.default_rng(23)


def puff(n=16):
    yy, xx = np.indices((n, n)) + .5
    a = np.zeros((n, n))
    # three to four overlapping lumps make a ragged cloud
    for _ in range(rng.integers(3, 5)):
        cx, cy = rng.uniform(6, 10, 2); r = rng.uniform(2.4, 3.6)
        a = np.maximum(a, 1 - np.hypot(xx - cx, yy - cy) / r)
    a = np.where(a > 0, a + rng.uniform(-.1, .1, (n, n)), 0)
    alpha = np.select([a > .55, a > .3, a > .08], [1., .62, .3], 0.)
    v = .82 + .1 * rng.choice([-1, 0, 0, 1], size=(n // 2 + 1, n // 2 + 1)).repeat(2, 0).repeat(2, 1)[:n, :n]
    # lit from the upper left: texels whose upper-left neighbour is empty are lighter, lower-right ones darker
    up = np.roll(np.roll(alpha, 1, 0), 1, 1); dn = np.roll(np.roll(alpha, -1, 0), -1, 1)
    v = np.where((alpha > 0) & (up == 0), 1., v); v = np.where((alpha > 0) & (dn == 0), .7, v)
    return np.dstack([v * .96, v * .98, v, alpha])


def flake(kind, n=8):
    img = np.zeros((n, n, 4))
    shapes = {
        0: [(3, 3, 1), (4, 3, .85), (3, 4, .85), (4, 4, .7), (2, 3, .6), (5, 4, .55), (3, 2, .6), (4, 5, .5)],
        1: [(3, 3, 1), (3, 2, .8), (3, 4, .8), (2, 3, .8), (4, 3, .8), (3, 1, .45), (3, 5, .45), (1, 3, .45), (5, 3, .45)],
        2: [(3, 3, 1), (4, 4, .8), (2, 2, .75), (5, 5, .5), (4, 3, .6)],
        3: [(3, 3, .9), (4, 3, 1), (3, 4, .7), (4, 4, .8)],
    }
    for x, y, v in shapes[kind]:
        img[y, x] = (.9 * v + .1, .95 * v + .05, 1 * v, 1. if v > .6 else .62)
    return img


def save(tiles, name):
    sheet = np.concatenate(tiles, axis=1)
    Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8), 'RGBA').save(os.path.join(out, name))


os.makedirs(out, exist_ok=True)
save([puff() for _ in range(4)], 'Frost breath.png')
save([flake(k) for k in range(4)], 'Rime flake.png')
print('written', out)

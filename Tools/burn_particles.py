"""The burning enemy's particles (Combat/Spells/Fireball/Burn/, used by BurnCoat on the Fireball Burn effect):
Flame.png, a flame in four frames side by side (12 x 16 texels each), played over each flame's life as it licks up and
dies; Ember.png, two small embers (4 x 4 each). Pixel art in the house style: hard-edged shapes, a dark red rim, orange
body, yellow core and a pale tip, no soft fade (alpha only 0 or 1).

    python Tools/burn_particles.py "<out folder>"
"""
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1]
rng = np.random.default_rng(31)
RIM, BODY, HOT, CORE = (150, 34, 14), (232, 96, 22), (255, 176, 44), (255, 236, 150)


def flame(frame, w=12, h=16):
    """A teardrop of fire, wide at the foot and licking to a point; each frame the tip sways and the flame shrinks a
    little, the last a dying wisp."""
    img = np.zeros((h, w, 4), np.uint8)
    grow = [1.0, .95, .78, .5][frame]
    sway = [0, 1, -1, 1][frame]
    base_y = h - 1
    height = (h - 2) * grow
    for y in range(h):
        up = (base_y - y) / max(height, 1)            # 0 at the foot, 1 at the tip
        if up < 0 or up > 1: continue
        half = (w / 2 - 1) * (1 - up) ** .8 * (1 if up > .12 else .4 + 5 * up)
        cx = w / 2 - .5 + sway * up * 1.6 + (rng.random() - .5) * .6 * up
        for x in range(w):
            d = abs(x - cx)
            if d > half + .35: continue
            r = d / max(half, .5)
            colour = RIM if r > .72 or up > .9 else BODY if r > .42 or up > .7 else HOT if r > .18 or up > .45 else CORE
            img[y, x] = (*colour, 255)
    # a loose spark or two off the tip
    if frame < 3:
        tip = int(base_y - height) - 1 - rng.integers(0, 2)
        if 0 <= tip < h: img[tip, int(w / 2 + sway * 2) % w] = (*HOT, 255)
    return img


def ember(kind):
    img = np.zeros((4, 4, 4), np.uint8)
    pts = [[(1, 1, CORE), (2, 1, HOT), (1, 2, HOT), (2, 2, BODY)], [(1, 1, HOT), (2, 2, BODY), (2, 1, BODY)]][kind]
    for x, y, c in pts: img[y, x] = (*c, 255)
    return img


os.makedirs(out, exist_ok=True)
Image.fromarray(np.concatenate([flame(f) for f in range(4)], axis=1), 'RGBA').save(os.path.join(out, 'Flame.png'))
Image.fromarray(np.concatenate([ember(k) for k in range(2)], axis=1), 'RGBA').save(os.path.join(out, 'Ember.png'))
print('written', out)

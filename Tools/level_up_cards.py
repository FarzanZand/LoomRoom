"""Art for the level-up panels (LevelUpCardsUI): a dark tarot card edged in gold. Drawn at half the card's UI size (one texel = two UI units, 150 x 230 for a
300 x 460 card) so it sits with the pixel UI; dirty, the colour moving texel to texel in hard-edged clusters, dark at the
edges and lit on the rim. Writes Tarot face, divider and key to <out dir>.
Run: python -I Tools/level_up_cards.py "Assets/Game/UI/Sprites/Level up cards"
"""
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
W, H = 150, 230
rng = np.random.default_rng(11)


def noise(w, h, scale, seed):
    """Smooth value noise in 0..1."""
    r = np.random.default_rng(seed)
    gw, gh = int(w / scale) + 2, int(h / scale) + 2
    g = r.random((gh, gw))
    ys, xs = np.mgrid[0:h, 0:w] / scale
    x0, y0 = xs.astype(int), ys.astype(int)
    fx, fy = xs - x0, ys - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a, b = g[y0, x0], g[y0, x0 + 1]
    c, d = g[y0 + 1, x0], g[y0 + 1, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def grain(w, h, seed, amount):
    """Texel-to-texel colour movement plus larger blotches: the house style's dirt."""
    # The blotches are cut into a few flat tones with hard edges (clusters, never an airbrushed gradient).
    blotch = np.floor(noise(w, h, 7, seed + 1) * 4) / 3 - .5
    return (np.random.default_rng(seed).random((h, w)) - .5) * amount + blotch * amount * .9


def rgba(rgb, alpha):
    return Image.fromarray(np.dstack([np.clip(rgb, 0, 255), np.clip(alpha, 0, 255)]).astype(np.uint8), "RGBA")


def rounded_mask(w, h, r):
    ys, xs = np.mgrid[0:h, 0:w]
    m = np.ones((h, w), bool)
    for cx, cy in ((r, r), (w - 1 - r, r), (r, h - 1 - r), (w - 1 - r, h - 1 - r)):
        corner = ((xs < r) if cx == r else (xs > w - 1 - r)) & ((ys < r) if cy == r else (ys > h - 1 - r))
        m &= ~(corner & ((xs - cx) ** 2 + (ys - cy) ** 2 > r * r))
    return m


def edge_distance(mask):
    """Texels from the nearest outside texel (a few steps is all the shading needs)."""
    d = np.where(mask, 99, 0).astype(float)
    for _ in range(12):
        p = np.pad(d, 1, constant_values=0)
        d = np.minimum(d, np.minimum.reduce([p[:-2, 1:-1], p[2:, 1:-1], p[1:-1, :-2], p[1:-1, 2:]]) + 1)
    return d


def shade(base, mask, seed, dirt, edge_dark, rim_light):
    """Base colour with grain, darker toward the edge (worn, handled) and a lit rim on the top-left."""
    h, w = mask.shape
    col = np.zeros((h, w, 3)) + np.array(base, float)
    col += grain(w, h, seed, dirt)[..., None]
    d = edge_distance(mask)
    col *= (1 - edge_dark * np.clip(1 - d / 7, 0, 1) ** 2)[..., None]
    ys, xs = np.mgrid[0:h, 0:w]
    lit = (d == 2) & ((xs < w * .5) | (ys < h * .3))
    col[lit] += rim_light
    col[d == 1] *= .45
    return col


def frame(col, mask, inset, colour, width=1, gap_colour=None):
    """An inked or gilded line inset from the edge, following the corners."""
    d = edge_distance(mask)
    line = (d >= inset) & (d < inset + width)
    c = np.array(colour, float)
    jitter = grain(mask.shape[1], mask.shape[0], int(inset * 7 + 3), 26)[..., None]
    col[line] = (c + jitter)[line]
    if gap_colour is not None:
        col[(d >= inset + width) & (d < inset + width + 1)] *= gap_colour
    return col


def corner_marks(col, mask, inset, colour):
    """A small gold diamond knot in each corner, inside the inner line."""
    h, w = mask.shape
    c = np.array(colour, float)
    for sx, sy in ((inset, inset), (w - 1 - inset, inset), (inset, h - 1 - inset), (w - 1 - inset, h - 1 - inset)):
        cx, cy = sx + (4 if sx < w / 2 else -4), sy + (4 if sy < h / 2 else -4)
        for yy in range(-3, 4):
            for xx in range(-3, 4):
                if abs(xx) + abs(yy) <= 3 and 0 <= cx + xx < w and 0 <= cy + yy < h:
                    col[cy + yy, cx + xx] = c * (.8 if abs(xx) + abs(yy) == 3 else 1.05)
    return col


def save(img, name):
    img.save(os.path.join(out, name + ".png"))
    print("wrote", name)


def divider(colour, length=110, dark=None, seed=5):
    h = 5
    col = np.zeros((h, length, 3)) + np.array(colour, float)
    col += grain(length, h, seed, 30)[..., None]
    a = np.zeros((h, length))
    xs = np.arange(length)
    taper = np.clip(np.minimum(xs, length - 1 - xs) / 18, 0, 1)
    a[2] = 255 * taper
    a[1] = 120 * (taper > .6)
    mid = length // 2
    for yy in range(5):
        for xx in range(-3, 4):
            if abs(xx) + abs(yy - 2) <= 2:
                a[yy, mid + xx] = 255
    if dark is not None:
        col[1] = np.array(dark, float)
    return rgba(col, a)


def key_frame(fill, line, seed):
    w = h = 14
    m = rounded_mask(w, h, 2)
    col = shade(fill, m, seed, 18, .3, 10)
    col = frame(col, m, 1, line)
    return rgba(col, m * 255)


# ── Tarot: a dark card edged in gold; the back the DM's two slit eyes ───────────────────────────────────────────
def tarot():
    m = rounded_mask(W, H, 4)
    dark = (34, 30, 44)
    gold = (214, 168, 74)
    col = shade(dark, m, 31, 16, .4, 14)
    col = frame(col, m, 3, gold, 2, gap_colour=.6)
    col = frame(col, m, 7, (150, 112, 50))
    col = corner_marks(col, m, 7, gold)
    face = rgba(col, m * 255)
    save(face, "Tarot face")
    save(divider(gold, seed=35), "Tarot divider")
    save(key_frame((40, 34, 50), gold, 36), "Tarot key")


tarot()

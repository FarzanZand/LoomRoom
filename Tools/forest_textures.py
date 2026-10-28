"""The Forest1 biome's ground textures (Levels/Dungeon1/Forest/Textures), at the dungeon world's 128 texels a metre and
tiling seamlessly:

    Forest floor.png  leaf litter on dark earth (the pack's multicoloured leaves at 3 m, quilted to one 2 m cell, 256 px)
    Forest floor fall.png  fallen gold leaves (the autumn wood, Forest2; the pack's second multicoloured leaves, as above)
    Forest path.png   trodden leaves, darker (the pack's plain leaves, 2.5 m -> 320 px)
    Forest moss.png   the banks' tops: moss gone dark under the trees (the pack's moss, 2 m -> 256 px)
    Earth bank.png    the banks' faces: earth in uneven layers with stones and roots through it (drawn here, 2 m x 3 m -> 256 x 384)
    Pond bed.png      black mud under the water (the pack's mud, 2 m -> 256 px)

Each source is box-filtered down (every texel the average of its block), pulled toward the demo scene's palette (muted,
cooler in the shadows) and varied texel to texel; the earth is drawn texel by texel with periodic shapes so it tiles.

    python Tools/forest_textures.py
"""
import math
import random

import os
import sys

import numpy as np
from PIL import Image, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import style_tiles as st  # noqa: E402

SRC = "Assets/Synty/PolygonNatureBiomes/PNB_Enchanted_Forest/Textures/"
OUT = "Assets/Game/Levels/Dungeon1/Forest/Textures/"


def box(name, size):
    """The pack's texture box-filtered to size x size (it tiles, so its blocks tile too)."""
    # PIL's box filter averages the whole source (a crop to a whole multiple would break the tiling).
    return np.asarray(Image.open(SRC + name).convert("RGB").resize((size, size), Image.BOX)).astype(float) / 255


def grade(im, seed, mute=.15, dark=1.0, cool=.0, jitter=.07):
    rng = np.random.default_rng(seed)
    grey = im.mean(axis=2, keepdims=True)
    im = im * (1 - mute) + grey * mute
    # Shadows lean blue (the forest's cool ambient), lights keep their warmth.
    lum = grey
    im = im + np.array([-.012, 0, .02]) * cool * (1 - lum)
    im = im * dark * (1 + (rng.random(im.shape[:2])[..., None] - .5) * jitter)
    return np.clip(im, 0, 1)


def drawn(im, colours=12, outline=.12, seed=0):
    """Redrawn the way the cellar styles are drawn: clusters of a few hard-edged tones (mode-filtered, then a small
    palette), the darker side of every strong edge inked one texel dark. Filtered on a 3 x 3 copy of the tile and cut
    back to the middle, so it still tiles."""
    h, w = im.shape[:2]
    big = np.tile(im, (3, 3, 1))
    pil = Image.fromarray((np.clip(big, 0, 1) * 255).astype("uint8"))
    pil = pil.filter(ImageFilter.ModeFilter(3)).filter(ImageFilter.ModeFilter(3))
    pil = pil.quantize(colours, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    big = np.asarray(pil).astype(float) / 255
    out = big[h:2 * h, w:2 * w].copy()
    lum = big.mean(axis=2)
    mid = lum[h:2 * h, w:2 * w]
    ink = np.zeros_like(mid, dtype=bool)
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        nb = lum[h + dy:2 * h + dy, w + dx:2 * w + dx]
        ink |= (nb - mid) > outline
    out[ink] *= .55
    return out


# Brought up to the cellar floors' brightness (Cellar3 floor's mean is 69 of 255; these came out about 47 and read too dark
# in game): each base ground lifted by its own share. Variants are drawn from the saved bases, so they come out lifted already.
LIFT = {"Forest floor.png": 1.5, "Forest floor fall.png": 1.3, "Forest path.png": 1.6, "Forest moss.png": 1.5, "Earth bank.png": 1.4, "Pond bed.png": 1.3}


def save(im, name, lift=None):
    lift = LIFT.get(name, 1.0) if lift is None else lift
    im = np.clip(im * lift, 0, 1)
    # The editor may be reading the file as it reimports it: try again a few times.
    import time
    for attempt in range(20):
        try:
            Image.fromarray((im * 255 + .5).astype("uint8")).save(OUT + name)
            break
        except OSError:
            time.sleep(.5)
    else:
        raise OSError(f"could not write {name}")
    print(name, im.shape[1], "px")


def earth(width=256, height=384, seed=7):
    """Earth in layers: wavy bands of brown, stones bedded in them, roots hanging through, dark in the cracks. One 2 m
    wall cell wide (variants are picked per cell) and one 3 m tile high."""
    rng = random.Random(seed)
    nrng = np.random.default_rng(seed)
    y, x = np.mgrid[0:height, 0:width].astype(float)
    t = 2 * math.pi / width
    # Layer boundaries wander with whole periods across the tile, so the tile's edges meet.
    wave = (np.sin(x * t * 1 + 1.3) * 5 + np.sin(x * t * 3 + .4) * 2.2 + np.sin(x * t * 7 + 2.1) * 1.1)
    band = (y + wave) / height * 7  # seven layers a tile
    layer = np.floor(band) % 7
    within = band - np.floor(band)
    tones = np.array([[.27, .19, .14], [.24, .17, .125], [.29, .205, .145], [.22, .155, .115], [.25, .18, .13], [.28, .2, .15], [.23, .165, .125]])
    col = tones[layer.astype(int)]
    # Each layer darker at its foot (the one above overhangs it) and lighter near its top.
    col = col * (1.05 - .12 * within[..., None])
    # Lumpy grain: blocky noise at 2 and 4 texel sizes.
    for s, a in ((2, .08), (4, .1), (8, .07)):
        g = nrng.random((height // s, width // s))
        g = np.kron(g, np.ones((s, s)))
        col = col * (1 + (g[..., None] - .5) * a * 2)
    col = np.clip(col, 0, 1)

    def put(px, py, c):
        col[py % height, px % width] = c

    # Stones: small rounded lumps, lit from the upper left, with a dark seat under them.
    for _ in range(93):  # 140 on the old 3 m square
        cx, cy = rng.randrange(width), rng.randrange(height)
        r = rng.choice([1.5, 2, 2.5, 3, 3.5, 4.5])
        base = np.array(rng.choice([[.42, .4, .38], [.36, .35, .34], [.46, .42, .36], [.3, .3, .31]])) * rng.uniform(.85, 1.1)
        for dy in range(-int(r) - 2, int(r) + 3):
            for dx in range(-int(r) - 2, int(r) + 3):
                d = math.hypot(dx / 1.25, dy)
                if d <= r:
                    light = 1.18 if (dx + dy) < -r * .6 else .78 if (dx + dy) > r * .7 else 1.0
                    put(cx + dx, cy + dy, np.clip(base * light * rng.uniform(.93, 1.07), 0, 1))
                elif d <= r + 1.1 and dy >= 0:
                    put(cx + dx, cy + dy, col[(cy + dy) % height, (cx + dx) % width] * .62)
    # Roots: thin dark lines wandering down through the earth, a pale edge on their left.
    for _ in range(18):
        px, py = rng.uniform(0, width), rng.uniform(0, height)
        ang = math.pi / 2 + rng.uniform(-.6, .6)
        w = rng.choice([1, 1, 1, 2])
        for _ in range(rng.randrange(30, 120)):
            for k in range(w):
                put(int(px) + k, int(py), np.array([.13, .09, .07]) * rng.uniform(.8, 1.15))
            put(int(px) - 1, int(py), np.clip(col[int(py) % height, (int(px) - 1) % width] * 1.25, 0, 1))
            ang += rng.uniform(-.35, .35)
            ang = min(max(ang, .3), math.pi - .3)
            px += math.cos(ang)
            py += math.sin(ang)
            if rng.random() < .03:
                w = max(1, w - 1)
    # Cracks: short dark runs along the layer lines.
    for _ in range(40):
        cx, cy = rng.randrange(width), rng.randrange(height)
        for k in range(rng.randrange(4, 14)):
            put(cx + k, cy + int(math.sin(k * .7) * 1.2), col[cy % height, (cx + k) % width] * .55)
    return col


def periodic_noise(shape, seed, waves=6, low=2, high=7):
    """Smooth noise that tiles: a sum of sine waves with whole periods across the tile (shape: height, width)."""
    rng = np.random.default_rng(seed)
    h, w = shape
    y, x = np.mgrid[0:h, 0:w].astype(float)
    y, x = y / h * 2 * math.pi, x / w * 2 * math.pi
    n = np.zeros((h, w))
    for _ in range(waves):
        fx, fy = rng.integers(low, high, 2) * rng.choice([-1, 1], 2)
        n += np.sin(x * fx + y * fy + rng.uniform(0, 6.3)) * rng.uniform(.5, 1)
    return (n - n.min()) / (n.max() - n.min())


def patches(base, over, seed, cover=.3, edge=.06):
    """`over` laid on `base` in soft-edged blobs covering about `cover` of the tile, ragged at the texel."""
    h, w = base.shape[:2]
    over = np.asarray(Image.fromarray((over * 255).astype("uint8")).resize((w, h), Image.BOX)).astype(float) / 255
    n = periodic_noise((h, w), seed) + (np.random.default_rng(seed + 1).random((h, w)) - .5) * .08
    t = np.quantile(n, 1 - cover)
    k = np.clip((n - t) / edge, 0, 1)[..., None]
    return base * (1 - k) + over * k


def lines(img, seed, count, colour, width=(1, 2), length=(30, 110)):
    """Thin roots across the texture, wrapping round its edges."""
    img = img.copy(); H, W = img.shape[:2]; rng = random.Random(seed)
    for _ in range(count):
        px, py = rng.uniform(0, W), rng.uniform(0, H); ang = rng.uniform(0, 2 * math.pi); w = rng.choice(range(width[0], width[1] + 1))
        for _ in range(rng.randrange(*length)):
            for k in range(w):
                img[int(py) % H, (int(px) + k) % W] = np.array(colour) * rng.uniform(.8, 1.15)
            img[(int(py) - 1) % H, int(px) % W] = np.clip(img[(int(py) - 1) % H, int(px) % W] * 1.2, 0, 1)
            ang += rng.uniform(-.4, .4); px += math.cos(ang); py += math.sin(ang)
    return img


def stones(img, seed, count, sizes=(1.5, 4.5)):
    img = img.copy(); H, W = img.shape[:2]; rng = random.Random(seed)
    for _ in range(count):
        cx, cy = rng.randrange(W), rng.randrange(H); r = rng.uniform(*sizes)
        base = np.array(rng.choice([[.42, .4, .38], [.36, .35, .34], [.46, .42, .36], [.3, .3, .31]])) * rng.uniform(.8, 1.05)
        for dy in range(-int(r) - 2, int(r) + 3):
            for dx in range(-int(r) - 2, int(r) + 3):
                d = math.hypot(dx / 1.2, dy)
                if d <= r:
                    light = 1.18 if (dx + dy) < -r * .6 else .78 if (dx + dy) > r * .7 else 1.0
                    img[(cy + dy) % H, (cx + dx) % W] = np.clip(base * light, 0, 1)
                elif d <= r + 1.1 and dy >= 0:
                    img[(cy + dy) % H, (cx + dx) % W] *= .62
    return img


def on_cell(variant, base, floor, seed, width=28):
    """The variant inside its cell and the base near the cell's edges (raggedly, by noise): variants are picked per 2 m
    cell, so whatever reached an edge was cut off there in a straight line against the plain tile beside it."""
    h, w = base.shape[:2]
    g = np.clip(np.minimum(np.arange(w) + .5, w - .5 - np.arange(w)) / width, 0, 1)[None, :].repeat(h, 0)
    if floor:
        g = g * np.clip(np.minimum(np.arange(h) + .5, h - .5 - np.arange(h)) / width, 0, 1)[:, None]
    keep = g > .15 + .7 * periodic_noise((h, w), seed, 8, 6, 14)
    return np.where(keep[..., None], variant, base)


def variants():
    """Wall and floor variants the room styles scatter per cell: close to their base in tone, so they read as patches
    of ground, not as squares, and giving way to the base near the cell's edges (on_cell)."""
    def load(n):
        # The drawn base as saved, its lift taken off again (variants are drawn from it and lifted the same).
        return np.asarray(Image.open(OUT + n).convert("RGB")).astype(float) / 255 / LIFT[n]

    def sized(im, like):
        return np.asarray(Image.fromarray((np.clip(im, 0, 1) * 255).astype("uint8")).resize((like.shape[1], like.shape[0]), Image.NEAREST)).astype(float) / 255

    floor, fall, moss, earth = load("Forest floor.png"), load("Forest floor fall.png"), load("Forest moss.png"), load("Earth bank.png")
    mossF, mossE = sized(moss, floor), sized(moss, earth)

    def put(im, base, base_name, name, floor_cell, seed, *args):
        save(on_cell(drawn(im, *args), base, floor_cell, seed) * LIFT[base_name], name, lift=1)

    put(patches(floor, mossF * 1.05, 11, .28), floor, "Forest floor.png", "Forest floor mossy.png", True, 31)
    put(stones(lines(floor, 12, 12, [.16, .11, .08]), 13, 20, (1, 2.5)), floor, "Forest floor.png", "Forest floor roots.png", True, 32)
    damp = floor * np.array([.78, .82, .9]); damp = patches(floor * .95, damp, 14, .55, .12)
    put(damp, floor, "Forest floor.png", "Forest floor damp.png", True, 33)
    red = fall * np.array([1.12, .82, .78])
    put(patches(fall, np.clip(red, 0, 1), 15, .45, .1), fall, "Forest floor fall.png", "Forest floor fall red.png", True, 34)
    put(patches(fall, mossF * 1.05, 16, .22), fall, "Forest floor fall.png", "Forest floor fall mossy.png", True, 35)
    put(lines(earth, 21, 27, [.13, .09, .07], (1, 3), (60, 200)), earth, "Earth bank.png", "Earth bank roots.png", False, 36, 10, .1)
    put(stones(earth, 22, 73, (2.5, 6)), earth, "Earth bank.png", "Earth bank stony.png", False, 37, 10, .1)
    put(patches(earth, mossE * .95, 23, .22, .05), earth, "Earth bank.png", "Earth bank mossy.png", False, 38, 10, .1)

if __name__ == "__main__":
    import os
    os.makedirs(OUT, exist_ok=True)
    # Every texture repeats a whole number of times per 6 m (board and floor UVs wrap there): the path covers 3 m (384 px),
    # the moss and the pond bed 1.5 m (192 px); all 128 texels a metre. Surfaces with variants (picked per 2 m cell)
    # repeat once per cell, so a variant can give way to the base at its own edges: the leaf floors are quilted to
    # 256 x 256 from the pack's leaves brought to 3 m (a leaf keeps its size), the earth is drawn 256 wide by 3 m high.
    def cell_floor(name, seed, **kw):
        src = grade(box(name, 384), seed, **kw)
        return np.asarray(st.quilt(src, (256, 256), 80, 16, np.random.default_rng(seed))).astype(float)  # a 64 step fills 256 exactly

    save(drawn(cell_floor("Leaves_Multicoloured_Texture_01.png", 1, mute=.18, dark=1.15, cool=1)), "Forest floor.png")
    save(drawn(cell_floor("Leaves_Multicoloured_Texture_02.png", 6, mute=.25, dark=.92, cool=0)), "Forest floor fall.png")
    save(drawn(grade(box("Leaves_Texture_01.png", 384), 2, mute=.2, dark=1.0, cool=1)), "Forest path.png")
    moss = box("Moss_Enchanted_Texture_01.png", 192)
    save(drawn(grade(moss * np.array([.62, .78, .8]), 3, mute=.3, dark=.82, cool=1.5), 8), "Forest moss.png")
    save(drawn(grade(earth(), 4, mute=.05, dark=1.0, cool=.6, jitter=.05), 10, .1), "Earth bank.png")
    save(drawn(grade(box("Mud_Enchanted_01.png", 192), 5, mute=.2, dark=.45, cool=1, jitter=.12), 6), "Pond bed.png")
    variants()

"""The Forest1 biome's ground textures (Levels/Dungeon1/Forest/Textures), at the dungeon world's 128 texels a metre and
tiling seamlessly:

    Forest floor.png  leaf litter on dark earth (the pack's multicoloured leaves, 2.5 m a repeat -> 320 px)
    Forest floor fall.png  fallen gold leaves (the autumn wood, Forest2; the pack's second multicoloured leaves, 2.5 m -> 320 px)
    Forest path.png   trodden leaves, darker (the pack's plain leaves, 2.5 m -> 320 px)
    Forest moss.png   the banks' tops: moss gone dark under the trees (the pack's moss, 2 m -> 256 px)
    Earth bank.png    the banks' faces: earth in uneven layers with stones and roots through it (drawn here, 3 m -> 384 px)
    Pond bed.png      black mud under the water (the pack's mud, 2 m -> 256 px)

Each source is box-filtered down (every texel the average of its block), pulled toward the demo scene's palette (muted,
cooler in the shadows) and varied texel to texel; the earth is drawn texel by texel with periodic shapes so it tiles.

    python Tools/forest_textures.py
"""
import math
import random

import numpy as np
from PIL import Image, ImageFilter

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
    size = im.shape[0]
    big = np.tile(im, (3, 3, 1))
    pil = Image.fromarray((np.clip(big, 0, 1) * 255).astype("uint8"))
    pil = pil.filter(ImageFilter.ModeFilter(3)).filter(ImageFilter.ModeFilter(3))
    pil = pil.quantize(colours, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
    big = np.asarray(pil).astype(float) / 255
    out = big[size:2 * size, size:2 * size].copy()
    lum = big.mean(axis=2)
    mid = lum[size:2 * size, size:2 * size]
    ink = np.zeros_like(mid, dtype=bool)
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        nb = lum[size + dy:2 * size + dy, size + dx:2 * size + dx]
        ink |= (nb - mid) > outline
    out[ink] *= .55
    return out


# Brought up to the cellar floors' brightness (Cellar3 floor's mean is 69 of 255; these came out about 47 and read too dark
# in game): each base ground lifted by its own share. Variants are drawn from the saved bases, so they come out lifted already.
LIFT = {"Forest floor.png": 1.5, "Forest floor fall.png": 1.3, "Forest path.png": 1.6, "Forest moss.png": 1.5, "Earth bank.png": 1.4, "Pond bed.png": 1.3}


def save(im, name):
    lift = LIFT.get(name, 1.0)
    im = np.clip(im * lift, 0, 1)
    Image.fromarray((im * 255 + .5).astype("uint8")).save(OUT + name)
    print(name, im.shape[1], "px")


def earth(size=384, seed=7):
    """Earth in layers: wavy bands of brown, stones bedded in them, roots hanging through, dark in the cracks."""
    rng = random.Random(seed)
    nrng = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size].astype(float)
    t = 2 * math.pi / size
    # Layer boundaries wander with whole periods across the tile, so the tile's edges meet.
    wave = (np.sin(x * t * 2 + 1.3) * 5 + np.sin(x * t * 5 + .4) * 2.2 + np.sin(x * t * 11 + 2.1) * 1.1)
    band = (y + wave) / size * 7  # seven layers a tile
    layer = np.floor(band) % 7
    within = band - np.floor(band)
    tones = np.array([[.27, .19, .14], [.24, .17, .125], [.29, .205, .145], [.22, .155, .115], [.25, .18, .13], [.28, .2, .15], [.23, .165, .125]])
    col = tones[layer.astype(int)]
    # Each layer darker at its foot (the one above overhangs it) and lighter near its top.
    col = col * (1.05 - .12 * within[..., None])
    # Lumpy grain: blocky noise at 2 and 4 texel sizes.
    for s, a in ((2, .08), (4, .1), (8, .07)):
        g = nrng.random((size // s, size // s))
        g = np.kron(g, np.ones((s, s)))
        col = col * (1 + (g[..., None] - .5) * a * 2)
    col = np.clip(col, 0, 1)

    def put(px, py, c):
        col[py % size, px % size] = c

    # Stones: small rounded lumps, lit from the upper left, with a dark seat under them.
    for _ in range(140):
        cx, cy = rng.randrange(size), rng.randrange(size)
        r = rng.choice([1.5, 2, 2.5, 3, 3.5, 4.5])
        base = np.array(rng.choice([[.42, .4, .38], [.36, .35, .34], [.46, .42, .36], [.3, .3, .31]])) * rng.uniform(.85, 1.1)
        for dy in range(-int(r) - 2, int(r) + 3):
            for dx in range(-int(r) - 2, int(r) + 3):
                d = math.hypot(dx / 1.25, dy)
                if d <= r:
                    light = 1.18 if (dx + dy) < -r * .6 else .78 if (dx + dy) > r * .7 else 1.0
                    put(cx + dx, cy + dy, np.clip(base * light * rng.uniform(.93, 1.07), 0, 1))
                elif d <= r + 1.1 and dy >= 0:
                    put(cx + dx, cy + dy, col[(cy + dy) % size, (cx + dx) % size] * .62)
    # Roots: thin dark lines wandering down through the earth, a pale edge on their left.
    for _ in range(26):
        px, py = rng.uniform(0, size), rng.uniform(0, size)
        ang = math.pi / 2 + rng.uniform(-.6, .6)
        w = rng.choice([1, 1, 1, 2])
        for _ in range(rng.randrange(30, 120)):
            for k in range(w):
                put(int(px) + k, int(py), np.array([.13, .09, .07]) * rng.uniform(.8, 1.15))
            put(int(px) - 1, int(py), np.clip(col[int(py) % size, (int(px) - 1) % size] * 1.25, 0, 1))
            ang += rng.uniform(-.35, .35)
            ang = min(max(ang, .3), math.pi - .3)
            px += math.cos(ang)
            py += math.sin(ang)
            if rng.random() < .03:
                w = max(1, w - 1)
    # Cracks: short dark runs along the layer lines.
    for _ in range(60):
        cx, cy = rng.randrange(size), rng.randrange(size)
        for k in range(rng.randrange(4, 14)):
            put(cx + k, cy + int(math.sin(k * .7) * 1.2), col[cy % size, (cx + k) % size] * .55)
    return col


def periodic_noise(size, seed, waves=6, low=2, high=7):
    """Smooth noise that tiles: a sum of sine waves with whole periods across the tile."""
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size].astype(float) / size * 2 * math.pi
    n = np.zeros((size, size))
    for _ in range(waves):
        fx, fy = rng.integers(low, high, 2) * rng.choice([-1, 1], 2)
        n += np.sin(x * fx + y * fy + rng.uniform(0, 6.3)) * rng.uniform(.5, 1)
    return (n - n.min()) / (n.max() - n.min())


def patches(base, over, seed, cover=.3, edge=.06):
    """`over` laid on `base` in soft-edged blobs covering about `cover` of the tile, ragged at the texel."""
    size = base.shape[0]
    over = np.asarray(Image.fromarray((over * 255).astype("uint8")).resize((size, size), Image.BOX)).astype(float) / 255
    n = periodic_noise(size, seed) + (np.random.default_rng(seed + 1).random((size, size)) - .5) * .08
    t = np.quantile(n, 1 - cover)
    k = np.clip((n - t) / edge, 0, 1)[..., None]
    return base * (1 - k) + over * k


def lines(img, seed, count, colour, width=(1, 2), length=(30, 110)):
    """Thin roots across the texture, wrapping round its edges."""
    img = img.copy(); size = img.shape[0]; rng = random.Random(seed)
    for _ in range(count):
        px, py = rng.uniform(0, size), rng.uniform(0, size); ang = rng.uniform(0, 2 * math.pi); w = rng.choice(range(width[0], width[1] + 1))
        for _ in range(rng.randrange(*length)):
            for k in range(w):
                img[int(py) % size, (int(px) + k) % size] = np.array(colour) * rng.uniform(.8, 1.15)
            img[(int(py) - 1) % size, int(px) % size] = np.clip(img[(int(py) - 1) % size, int(px) % size] * 1.2, 0, 1)
            ang += rng.uniform(-.4, .4); px += math.cos(ang); py += math.sin(ang)
    return img


def stones(img, seed, count, sizes=(1.5, 4.5)):
    img = img.copy(); size = img.shape[0]; rng = random.Random(seed)
    for _ in range(count):
        cx, cy = rng.randrange(size), rng.randrange(size); r = rng.uniform(*sizes)
        base = np.array(rng.choice([[.42, .4, .38], [.36, .35, .34], [.46, .42, .36], [.3, .3, .31]])) * rng.uniform(.8, 1.05)
        for dy in range(-int(r) - 2, int(r) + 3):
            for dx in range(-int(r) - 2, int(r) + 3):
                d = math.hypot(dx / 1.2, dy)
                if d <= r:
                    light = 1.18 if (dx + dy) < -r * .6 else .78 if (dx + dy) > r * .7 else 1.0
                    img[(cy + dy) % size, (cx + dx) % size] = np.clip(base * light, 0, 1)
                elif d <= r + 1.1 and dy >= 0:
                    img[(cy + dy) % size, (cx + dx) % size] *= .62
    return img


def variants():
    """Wall and floor variants the room styles scatter per cell: close to their base in tone, so they read as patches
    of ground, not as squares."""
    load = lambda n: np.asarray(Image.open(OUT + n).convert("RGB")).astype(float) / 255
    floor, fall, moss, earth = load("Forest floor.png"), load("Forest floor fall.png"), load("Forest moss.png"), load("Earth bank.png")
    moss = np.asarray(Image.fromarray((moss * 255).astype("uint8")).resize((floor.shape[0], floor.shape[0]), Image.NEAREST)).astype(float) / 255
    save(drawn(patches(floor, moss * 1.05, 11, .28)), "Forest floor mossy.png")
    save(drawn(stones(lines(floor, 12, 18, [.16, .11, .08]), 13, 30, (1, 2.5))), "Forest floor roots.png")
    damp = floor * np.array([.78, .82, .9]); damp = patches(floor * .95, damp, 14, .55, .12)
    save(drawn(damp), "Forest floor damp.png")
    red = fall * np.array([1.12, .82, .78])
    save(drawn(patches(fall, np.clip(red, 0, 1), 15, .45, .1)), "Forest floor fall red.png")
    save(drawn(patches(fall, moss * 1.05, 16, .22)), "Forest floor fall mossy.png")
    mossE = np.asarray(Image.fromarray((moss * 255).astype("uint8")).resize((earth.shape[0], earth.shape[0]), Image.NEAREST)).astype(float) / 255
    save(drawn(lines(earth, 21, 40, [.13, .09, .07], (1, 3), (60, 200)), 10, .1), "Earth bank roots.png")
    save(drawn(stones(earth, 22, 110, (2.5, 6)), 10, .1), "Earth bank stony.png")
    save(drawn(patches(earth, mossE * .95, 23, .22, .05), 10, .1), "Earth bank mossy.png")


if __name__ == "__main__":
    import os
    os.makedirs(OUT, exist_ok=True)
    # Every texture repeats a whole number of times per 3 m architecture tile (board and floor UVs wrap there): the leaf
    # floors and the earth cover 3 m (384 px), the moss and the pond bed 1.5 m (192 px); all 128 texels a metre.
    save(drawn(grade(box("Leaves_Multicoloured_Texture_01.png", 384), 1, mute=.18, dark=1.15, cool=1)), "Forest floor.png")
    save(drawn(grade(box("Leaves_Multicoloured_Texture_02.png", 384), 6, mute=.25, dark=.92, cool=0)), "Forest floor fall.png")
    save(drawn(grade(box("Leaves_Texture_01.png", 384), 2, mute=.2, dark=1.0, cool=1)), "Forest path.png")
    moss = box("Moss_Enchanted_Texture_01.png", 192)
    save(drawn(grade(moss * np.array([.62, .78, .8]), 3, mute=.3, dark=.82, cool=1.5), 8), "Forest moss.png")
    save(drawn(grade(earth(), 4, mute=.05, dark=1.0, cool=.6, jitter=.05), 10, .1), "Earth bank.png")
    save(drawn(grade(box("Mud_Enchanted_01.png", 192), 5, mute=.2, dark=.45, cool=1, jitter=.12), 6), "Pond bed.png")
    variants()

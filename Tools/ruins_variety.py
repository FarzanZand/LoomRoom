"""Gives the Ruins styles colour (they read as one flat colour per room): every stone in a stone texture is found (the
light shapes between the dark mortar lines) and gets a tint of its own from the ruin's palette (cooler, warmer, a
moss-stained or a rust-stained stone, a darker or lighter one), so a wall is many stones, not one colour. The drawing
is untouched: only each stone's colour and value move, mortar and cracks keep theirs. Also writes new variants laid
now and then among the base tiles (DungeonRoomStyle wallVariants / floorVariants, set by Tools/ruins_setup.eval.cs):
    <RuinsN> stone wall moss    green moss in the mortar and over the stones, thickest at the foot
    <RuinsN> stone wall soot    soot rising from the foot, a few stones blackened
    <RuinsN> stone floor moss   moss in the joints and patches over the slabs
    <RuinsN> stone floor ochre  a patch of warm, rust-stained slabs
Works from '<name> base.png' (the texture as it was before this ran, kept the first time), so it can run again.
Tiles stay seamless: stones are found on a 3 x 3 copy of the tile and coloured by where they sit modulo the tile.
    python Tools/ruins_variety.py"""
import os
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = "Assets/Game/Levels/Shared/Room styles/Ruins"
# Per ruin: the tints a stone may take (multipliers on its colour) with weights.
PALETTES = {
    1: [((1.0, 1.0, 1.0), 4), ((.92, .98, 1.08), 2), ((1.08, 1.0, .9), 2), ((.94, 1.06, .9), 1.5), ((1.1, .96, .86), 1), ((.82, .84, .86), 1.2), ((1.14, 1.12, 1.08), 1)],
    2: [((1.0, 1.0, 1.0), 4), ((1.06, 1.0, .9), 2), ((.95, .97, 1.02), 2), ((1.1, 1.0, .86), 1.2), ((.96, 1.04, .92), 1), ((.84, .82, .8), 1.2), ((1.12, 1.1, 1.06), 1.2)],
    3: [((1.0, 1.0, 1.0), 4), ((.9, .96, 1.1), 2), ((1.04, 1.0, .94), 1.5), ((.92, 1.06, 1.0), 1.5), ((1.12, 1.0, .9), .8), ((.8, .8, .84), 1.5), ((1.16, 1.16, 1.2), 1)],
}
MOSS = np.array([[44, 54, 32], [54, 66, 38], [66, 78, 44]], float)
SOOT = np.array([22, 20, 19], float)
RUST = np.array([1.12, .99, .84])


def load(path):
    return np.asarray(Image.open(path).convert("RGB")).astype(float)


def save(a, path):
    Image.fromarray(np.clip(a, 0, 255).astype("uint8")).save(path)


def base_of(path):
    base = path[:-4] + " base.png"
    if not os.path.exists(base):
        Image.open(path).save(base)
    return load(base)


def stones(img):
    """Label image of the stones (0 = mortar), found on a 3 x 3 tiling; each label's key is its centre modulo the tile,
    so a stone crossing the tile's edge is one stone with one colour."""
    h, w = img.shape[:2]
    big = np.tile(img, (3, 3, 1))
    lum = big.mean(-1)
    # Mortar: clearly darker than its neighbourhood.
    local = ndimage.uniform_filter(lum, 25)
    mortar = lum < local * .72
    mortar = ndimage.binary_dilation(mortar, iterations=1)
    lab, n = ndimage.label(~mortar)
    cy, cx = np.array(ndimage.center_of_mass(np.ones_like(lum), lab, range(1, n + 1))).T
    keys = (np.round(cy).astype(int) % h) * 100003 + (np.round(cx).astype(int) % w)
    mid = lab[h:2 * h, w:2 * w]
    return mid, keys, lum[h:2 * h, w:2 * w]


def pick(rng, palette):
    tints, weights = zip(*palette)
    weights = np.array(weights) / sum(weights)
    return np.array(tints[rng.choice(len(tints), p=weights)])


def tinted(img, n):
    lab, keys, _ = stones(img)
    out = img.copy()
    for label in np.unique(lab):
        if label == 0:
            continue
        rng = np.random.default_rng(int(keys[label - 1]) * 7 + n)
        mask = lab == label
        if mask.sum() < 12:
            continue
        out[mask] = img[mask] * pick(rng, PALETTES[n])
    return out


def field(h, w, scale, seed):
    """A tileable smooth 0-1 field with features about scale texels across."""
    rng = np.random.default_rng(seed)
    g = rng.random((max(2, h // scale), max(2, w // scale)))
    big = np.tile(g, (3, 3))
    up = np.asarray(Image.fromarray((big * 255).astype("uint8")).resize((w * 3, h * 3), Image.BICUBIC)).astype(float) / 255
    return up[h:2 * h, w:2 * w]


def mossy(img, seed, foot):
    """Moss in the mortar and over stones; foot: thickest at the bottom of the tile (a wall's foot)."""
    h, w = img.shape[:2]
    lab, _, lum = stones(img)
    f = field(h, w, 24, seed) * .7 + field(h, w, 8, seed + 1) * .3
    if foot:
        f = f * .55 + np.linspace(0, .6, h)[:, None]
    out = img.copy()
    in_mortar = (lab == 0) & (f > .45)
    on_stone = (lab > 0) & (f > .7)
    tone = np.clip(((f - .45) * 6).astype(int), 0, 2)
    shade = np.clip(lum / max(lum.mean(), 1), .6, 1.3)[..., None]
    for m in (in_mortar, on_stone):
        out[m] = MOSS[tone[m]] * shade[m]
    return out


def sooty(img, seed):
    h, w = img.shape[:2]
    lab, keys, _ = stones(img)
    f = field(h, w, 30, seed) * .5 + np.linspace(0, .7, h)[:, None]
    # In hard steps (the guide has no airbrushed gradients): clean, light, heavy, black.
    k = np.asarray([0, .25, .45, .65])[np.clip(((f - .45) * 5).astype(int), 0, 3)][..., None]
    out = img * (1 - k) + SOOT * k
    for label in np.unique(lab):
        if label == 0:
            continue
        if np.random.default_rng(int(keys[label - 1]) + seed).random() < .12:
            out[lab == label] *= .55
    return out


def ochre(img, seed):
    h, w = img.shape[:2]
    lab, keys, _ = stones(img)
    f = field(h, w, 40, seed)
    out = img.copy()
    for label in np.unique(lab):
        if label == 0:
            continue
        m = lab == label
        if f[m].mean() > .55:
            out[m] = img[m] * RUST
    return out


def palette_of(img, ref):
    """img redrawn in ref's colours: each texel takes ref's mean colour at the same brightness rank, so the drawing and
    its contrast stay and the palette becomes ref's (Ruins3's ceiling was red-brown under grey basalt walls)."""
    lw, lc = ref.mean(-1), img.mean(-1)
    bands = np.linspace(lw.min(), lw.max(), 17)
    idx = np.clip(np.digitize(lw, bands) - 1, 0, 15)
    pal = np.array([ref[idx == i].mean(0) if (idx == i).any() else ref.mean((0, 1)) for i in range(16)])
    rank = np.argsort(np.argsort(lc.ravel())).reshape(lc.shape) / lc.size
    wq = np.quantile(lw, rank)
    cidx = np.clip(np.digitize(wq, bands) - 1, 0, 15)
    return pal[cidx] * (wq / np.maximum(pal[cidx].mean(-1), 1))[..., None]


# Surfaces drawn in another surface's palette before they are tinted.
REPALETTE = {(3, "stone ceiling"): "stone wall"}


for n in (1, 2, 3):
    d = f"{ROOT}/Ruins{n}"
    for surface in ("stone wall", "stone wall b", "stone floor", "stone floor b", "stone ceiling", "timber upper", "timber upper b"):
        p = f"{d}/Ruins{n} {surface}.png"
        img = base_of(p)
        if (n, surface) in REPALETTE:
            img = palette_of(img, base_of(f"{d}/Ruins{n} {REPALETTE[(n, surface)]}.png"))
        out = tinted(img, n)
        save(out, p)
        print("tinted", p)
    wall = load(f"{d}/Ruins{n} stone wall.png")
    floor = load(f"{d}/Ruins{n} stone floor.png")
    save(mossy(wall, 10 + n, True), f"{d}/Ruins{n} stone wall moss.png")
    save(sooty(wall, 20 + n), f"{d}/Ruins{n} stone wall soot.png")
    save(mossy(floor, 30 + n, False), f"{d}/Ruins{n} stone floor moss.png")
    save(ochre(floor, 40 + n), f"{d}/Ruins{n} stone floor ochre.png")
    print("variants", n)

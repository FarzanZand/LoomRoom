"""Gives the Ruins styles colour (they read as one flat colour per room): every stone in a stone texture is found (the
light shapes between the dark mortar lines) and gets a tint of its own from the ruin's palette (cooler, warmer, a
moss-stained or a rust-stained stone, a darker or lighter one), so a wall is many stones, not one colour. The drawing
is untouched: only each stone's colour and value move, mortar and cracks keep theirs. Also writes new variants laid
now and then among the base tiles (DungeonRoomStyle wallVariants / floorVariants, set by Tools/ruins_setup.eval.cs):
    <RuinsN> stone wall moss    green moss in the mortar and over the stones, thickest at the foot
    <RuinsN> stone wall soot    soot rising from the foot, a few stones blackened
    <RuinsN> stone floor moss   moss in the joints and patches over the slabs (and 'stone floor b moss')
    <RuinsN> stone floor ochre  a patch of warm, rust-stained slabs (and 'stone floor b ochre')
    <surface> shift 1, shift 2  the wall's and floors' stones coloured again, laid often
Walls and floors are laid again to repeat once per 2 m cell (walls 256 x 384, floors 256 x 256; material tiling 1.5,
offset .5, as the Cellar styles): variants are picked per cell, so on the old 3 m repeat a mossy tile was cut off a third
of the way through its drawing, a hard line against the plain tile beside it. Each course keeps whole stones (a run of
its own about 256 wide, a few columns taken out of the widest to close it, turned by its own offset so no joint runs up
the cell's edge); a floor keeps six courses with a few rows taken out of each. Moss, soot and stains fade out toward the
cell's edges (raggedly, by the noise they are drawn from) and no stone touching an edge is blackened, stained or
coloured differently, so any two tiles meet without a line. Check with Tools/tile_seams.py.
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
    # The editor may be reading the file as it reimports the last one: try again a few times.
    import time
    for attempt in range(20):
        try:
            Image.fromarray(np.clip(a, 0, 255).astype("uint8")).save(path)
            return
        except OSError:
            time.sleep(.5)
    raise OSError(f"could not write {path}")


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


def tinted(img, n, shift=0, floor=False):
    """Each stone its own tint; shift > 0 draws them again for a copy laid among the base tiles, except the stones
    crossing the cell's edge, which keep the base's (their other half may be on the base tile)."""
    lab, keys, _ = stones(img)
    out = img.copy()
    for label in np.unique(lab):
        if label == 0:
            continue
        mask = lab == label
        salt = shift * 1000 if shift and not touches_edge(mask, floor) else 0
        rng = np.random.default_rng(int(keys[label - 1]) * 7 + n + salt)
        if mask.sum() < 12:
            continue
        out[mask] = img[mask] * pick(rng, PALETTES[n])
    return out


CELL = 256  # texels across one 2 m cell at 128 a metre


def joint_groups(profile, cut):
    """Centres of the runs of joint positions in a cyclic profile (above cut = joint)."""
    n = len(profile)
    j = profile > cut
    if j.all() or not j.any():
        return []
    start = int(np.argmin(j))  # a stone texel: no run wraps past it
    centres, run = [], []
    for i in range(n):
        k = (start + i) % n
        if j[k]:
            run.append(start + i)
        elif run:
            centres.append(int(round(np.mean(run))) % n)
            run = []
    if run:
        centres.append(int(round(np.mean(run))) % n)
    return sorted(centres)


def take_out(n, length, edge=6):
    """n positions spread through [edge, length - edge), to take out of a stone or a course."""
    inner = length - 2 * edge
    if n <= 0 or inner <= 0:
        return []
    return [edge + int((i + .5) * inner / n) for i in range(n)]


def relay_band(band, mortar_cols, size, rng):
    """One course (rows x columns x 3) laid again `size` wide from a cyclic run of its own whole stones, a few columns
    taken out of the stones (at most 15% of each) to close it, then turned by its own offset."""
    w = band.shape[1]
    centres = joint_groups(mortar_cols, .7)
    if len(centres) < 2:
        return np.roll(band[:, :size], int(rng.integers(size)), axis=1)
    segs = [(c, (centres[(i + 1) % len(centres)] - c) % w or w) for i, c in enumerate(centres)]
    best = None
    for i in range(len(segs)):
        total, run = 0, []
        for k in range(len(segs)):
            s = segs[(i + k) % len(segs)]
            run.append(s)
            total += s[1]
            if total >= size:
                break
        extra = total - size
        room = sum(int(s[1] * .15) for s in run)
        if 0 <= extra <= room and (best is None or extra < best[0]):
            best = (extra, run)
    if best is None:
        return np.roll(band[:, :size], int(rng.integers(size)), axis=1)
    extra, run = best
    widths = [s[1] for s in run]
    cut = [0] * len(run)
    for _ in range(extra):  # a column at a time from the stone that has lost the least of its width
        k = int(np.argmax([(wd - c) / wd if c < wd * .15 else -1 for wd, c in zip(widths, cut)]))
        cut[k] += 1
    cols = []
    for (c, wd), n in zip(run, cut):
        drop = set(take_out(n, wd))
        cols += [(c + i) % w for i in range(wd) if i not in drop]
    return np.roll(band[:, cols], int(rng.integers(size)), axis=1)


def relay(img, floor, seed):
    """img (a square of courses running along x) laid again to repeat once per cell: CELL wide; a floor also CELL tall
    (six of its courses, a few rows taken out of each)."""
    h, w = img.shape[:2]
    lab, _, _ = stones(img)
    mortar = lab == 0
    rows = joint_groups(mortar.mean(1), .8)
    rng = np.random.default_rng(seed)
    bands = []
    for i, r0 in enumerate(rows):
        r1 = rows[i + 1] if i + 1 < len(rows) else rows[0] + h
        idx = [r % h for r in range(r0, r1)]
        inner = [r % h for r in range(r0 + 6, r1 - 6)]
        bands.append(relay_band(img[idx], mortar[inner].mean(0), CELL, rng))
    if floor:
        bands = bands[:6]
        extra = sum(b.shape[0] for b in bands) - CELL
        each = [extra // 6 + (1 if i < extra % 6 else 0) for i in range(6)]
        bands = [np.delete(b, take_out(n, b.shape[0]), axis=0) for b, n in zip(bands, each)]
    return np.concatenate(bands, 0)


def edge_fade(h, w, floor, width=36):
    """1 in the middle of a cell, falling to 0 at its edges (x; and y on a floor)."""
    g = np.clip(np.minimum(np.arange(w) + .5, w - .5 - np.arange(w)) / width, 0, 1)[None, :].repeat(h, 0)
    if floor:
        g = g * np.clip(np.minimum(np.arange(h) + .5, h - .5 - np.arange(h)) / width, 0, 1)[:, None]
    return g


def touches_edge(mask, floor, margin=3):
    """A stone that crosses the cell's edge (half of it shows in the next cell, which may be another tile)."""
    if mask[:, :margin].any() or mask[:, -margin:].any():
        return True
    return floor and (mask[:margin].any() or mask[-margin:].any())


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
    f = f * edge_fade(h, w, not foot)
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
    f = (field(h, w, 30, seed) * .5 + np.linspace(0, .7, h)[:, None]) * edge_fade(h, w, False, 48)
    # In hard steps (the guide has no airbrushed gradients): clean, light, heavy, black.
    k = np.asarray([0, .25, .45, .65])[np.clip(((f - .45) * 5).astype(int), 0, 3)][..., None]
    out = img * (1 - k) + SOOT * k
    for label in np.unique(lab):
        if label == 0:
            continue
        m = lab == label
        if np.random.default_rng(int(keys[label - 1]) + seed).random() < .12 and not touches_edge(m, False):
            out[m] *= .55
    return out


def ochre(img, seed):
    h, w = img.shape[:2]
    lab, keys, _ = stones(img)
    f = field(h, w, 40, seed) * edge_fade(h, w, True, 48)
    out = img.copy()
    for label in np.unique(lab):
        if label == 0:
            continue
        m = lab == label
        if f[m].mean() > .55 and not touches_edge(m, True):
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


# Surfaces with variants picked per cell: laid again to repeat once per 2 m cell (True: a floor) before they are tinted.
PER_CELL = {"stone wall": False, "stone floor": True, "stone floor b": True}


def main():
    for n in (1, 2, 3):
        d = f"{ROOT}/Ruins{n}"
        for surface in ("stone wall", "stone wall b", "stone floor", "stone floor b", "stone ceiling"):   # the timber styles are deleted (2026-10-30)
            p = f"{d}/Ruins{n} {surface}.png"
            img = base_of(p)
            if (n, surface) in REPALETTE:
                img = palette_of(img, base_of(f"{d}/Ruins{n} {REPALETTE[(n, surface)]}.png"))
            if surface in PER_CELL:
                img = relay(img, PER_CELL[surface], 50 + n)
                # Two copies with the stones coloured again, laid often, so the 2 m repeat doesn't show.
                for k in (1, 2):
                    save(tinted(img, n, k, PER_CELL[surface]), f"{d}/Ruins{n} {surface} shift {k}.png")
            out = tinted(img, n)
            save(out, p)
            print("tinted", p, out.shape[1], "x", out.shape[0])
        wall = load(f"{d}/Ruins{n} stone wall.png")
        save(mossy(wall, 10 + n, True), f"{d}/Ruins{n} stone wall moss.png")
        save(sooty(wall, 20 + n), f"{d}/Ruins{n} stone wall soot.png")
        for b in ("", " b"):
            floor = load(f"{d}/Ruins{n} stone floor{b}.png")
            save(mossy(floor, 30 + n, False), f"{d}/Ruins{n} stone floor{b} moss.png")
            save(ochre(floor, 40 + n), f"{d}/Ruins{n} stone floor{b} ochre.png")
        print("variants", n)


if __name__ == "__main__":
    main()

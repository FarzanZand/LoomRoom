"""The Realms room styles (Dungeon1's floors 10-12), drawn in the house style at the dungeon's 128 texels a metre.

    python Tools/realms_style.py <out root>      writes <out root>/Realms1..3/RealmsN <surface>.png

Their look follows the POLYGON Dungeon Realms demo scenes the floors are made from:
  Realms1  The Deep Halls  the dwarf halls: big sandstone ashlar in courses, square-inset floor slabs, warm torchlight
  Realms2  The Forge       dark basalt blocks under soot, iron-plated floors with glowing seams near the furnaces
  Realms3  The Pit         black volcanic rubble, cracked obsidian floors with lava veins, the red of the hell scene

Each surface is drawn the way the Cellar and Ruins styles are: every stone outlined, its top and left edges lit, its
bottom and right inked dark, its face a few flat tones laid in clusters (no airbrushing, no flat fills), chips at the
corners and cracks across it. Walls are 256 x 384 (2 m x 3 m) and floors 256 x 256 (2 m): a texture with variants
repeats once per 2 m cell (tiling 1.5, offset .5 in the material), and both wrap left to right and top to bottom.
A variant is its base with stones changed only where they don't touch the cell's edge, so any two cells meet without a
line (Tools/tile_seams.py). Ceiling and trim are 384 x 384 (one 3 m tile).
"""
import os, sys
import numpy as np
from PIL import Image

RNG = np.random.default_rng

PALETTES = {
    # stone (light, mid, dark), mortar, highlight, ink, accents (stain colours a variant may lay on whole stones)
    1: dict(stone=[(128, 114, 92), (114, 101, 80), (98, 87, 70)], tint=[(134, 118, 94), (108, 99, 85), (122, 104, 78), (94, 86, 73)],
            mortar=(36, 30, 25), light=(160, 146, 116), ink=(70, 58, 44), soot=(58, 50, 42), accent=(196, 150, 60),
            floor=[(124, 112, 92), (108, 98, 80), (96, 86, 72)], ceiling=[(92, 80, 64), (78, 68, 56), (66, 58, 48)]),
    2: dict(stone=[(70, 66, 64), (58, 55, 54), (47, 45, 45)], tint=[(76, 70, 66), (54, 52, 54), (68, 58, 50), (44, 42, 42)],
            mortar=(20, 16, 14), light=(110, 96, 84), ink=(34, 30, 30), soot=(30, 26, 24), accent=(232, 112, 32),
            floor=[(64, 60, 58), (54, 50, 48), (44, 41, 40)], ceiling=[(52, 46, 44), (42, 38, 36), (34, 30, 30)]),
    3: dict(stone=[(70, 50, 48), (56, 40, 40), (44, 32, 32)], tint=[(78, 54, 50), (52, 40, 42), (66, 44, 38), (40, 30, 32)],
            mortar=(22, 12, 12), light=(96, 64, 56), ink=(26, 16, 16), soot=(24, 16, 16), accent=(255, 104, 24),
            floor=[(60, 44, 44), (48, 36, 36), (38, 28, 30)], ceiling=[(40, 28, 28), (32, 22, 24), (26, 18, 20)]),
}


def wrap_noise(h, w, cell, seed):
    """Value noise that wraps both ways (cell divides h and w)."""
    rng = RNG(seed)
    gh, gw = max(1, h // cell), max(1, w // cell)
    g = rng.random((gh, gw))
    ys = np.arange(h) / cell; xs = np.arange(w) / cell
    y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
    fy = ys - y0; fx = xs - x0
    fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
    a = g[y0 % gh][:, x0 % gw]; b = g[y0 % gh][:, (x0 + 1) % gw]
    c = g[(y0 + 1) % gh][:, x0 % gw]; d = g[(y0 + 1) % gh][:, (x0 + 1) % gw]
    top = a + (b - a) * fx[None, :]; bot = c + (d - c) * fx[None, :]
    return top + (bot - top) * fy[:, None]


def tones(h, w, seed):
    """Clusters of flat tones: wrapped noise at three scales (stains, clusters, grain), cut into four steps."""
    n = wrap_noise(h, w, 32, seed) * .4 + wrap_noise(h, w, 8, seed + 1) * .35 + wrap_noise(h, w, 2, seed + 2) * .25
    return np.digitize(n, [.36, .48, .6])  # 0..3


def course_layout(h, w, course, seed, min_w=40, max_w=96):
    """Stones laid in courses: a label map (wraps both ways) and each stone's box."""
    rng = RNG(seed)
    lab = np.zeros((h, w), np.int32); boxes = {}
    sid = 1
    for row in range(h // course):
        y0 = row * course
        start = int(rng.integers(0, w))
        x = 0; widths = []
        while x < w:
            ww = int(rng.integers(min_w, max_w))
            if w - x - ww < min_w: ww = w - x
            widths.append(ww); x += ww
        x = start
        for ww in widths:
            cols = (np.arange(x, x + ww)) % w
            lab[y0:y0 + course, cols] = sid
            boxes[sid] = (y0, course, x, ww)
            sid += 1; x += ww
    return lab, boxes


def slab_layout(h, w, size, seed):
    """Square floor slabs, now and then split in two."""
    rng = RNG(seed)
    lab = np.zeros((h, w), np.int32); boxes = {}; sid = 1
    for r in range(h // size):
        for c in range(w // size):
            y0, x0 = r * size, c * size
            split = rng.random()
            parts = [(y0, size, x0, size)]
            if split < .25: parts = [(y0, size // 2, x0, size), (y0 + size // 2, size // 2, x0, size)]
            elif split < .45: parts = [(y0, size, x0, size // 2), (y0, size, x0 + size // 2, size // 2)]
            for (py, ph, px, pw) in parts:
                lab[py:py + ph, px:px + pw] = sid; boxes[sid] = (py, ph, px, pw); sid += 1
    return lab, boxes


def rubble_layout(h, w, seed, n=46):
    """Irregular stones: a wrapped Voronoi of jittered seeds (the hell rock)."""
    rng = RNG(seed)
    pts = rng.random((n, 2)) * [h, w]
    yy, xx = np.mgrid[0:h, 0:w]
    best = np.full((h, w), 1e9); lab = np.zeros((h, w), np.int32)
    for i, (py, px) in enumerate(pts):
        for oy in (-h, 0, h):
            for ox in (-w, 0, w):
                d = (yy - py - oy) ** 2 * 1.3 + (xx - px - ox) ** 2
                m = d < best; best[m] = d[m]; lab[m] = i + 1
    boxes = {}
    for i in range(1, n + 1):
        ys, xs = np.nonzero(lab == i)
        if len(ys): boxes[i] = (ys.min(), ys.max() - ys.min() + 1, xs.min(), xs.max() - xs.min() + 1)
    return lab, boxes


def edges(lab):
    """Texels on a stone's boundary, by side (wrapping)."""
    up = lab != np.roll(lab, 1, 0); down = lab != np.roll(lab, -1, 0)
    left = lab != np.roll(lab, 1, 1); right = lab != np.roll(lab, -1, 1)
    return up, down, left, right


def grow(mask, n):
    out = mask.copy()
    for _ in range(n):
        out = out | np.roll(out, 1, 0) | np.roll(out, -1, 0) | np.roll(out, 1, 1) | np.roll(out, -1, 1)
    return out


def paint(lab, pal, seed, faces, joint=3, inset=False, cracks=1.0, edge=1.0):
    """Draw the stones of a label map: per-stone colour, clustered tones, lit and inked edges, mortar, cracks, chips."""
    h, w = lab.shape; rng = RNG(seed)
    img = np.zeros((h, w, 3), np.float32)
    t = tones(h, w, seed + 5)
    ids = np.unique(lab)
    stone_col = {}
    for i in ids:
        base = np.array(faces[int(rng.integers(0, len(faces)))], np.float32)
        base *= rng.uniform(.9, 1.08)
        stone_col[i] = base
    col = np.zeros((h, w, 3), np.float32)
    for i in ids: col[lab == i] = stone_col[i]
    shade = np.array([.78, .92, 1.0, 1.1], np.float32)[t]
    img = col * shade[..., None]
    # Dark stains in clusters (dirt, damp), never a smooth gradient.
    stain = wrap_noise(h, w, 24, seed + 9) > .68
    img[stain] *= .82
    up, down, left, right = edges(lab)
    mortar = grow(up | left, joint // 2) | grow(down | right, joint - joint // 2 - 1)
    # Rounded corners: the texels nearest each corner join the mortar.
    corner = grow((up | down) & (left | right), 1)
    mortar = mortar | (corner & grow(mortar, 1))
    # Lit top edge (two texels) and left edge (one) just inside the joint, inked bottom and right (two).
    lit = (grow(up, joint // 2 + 2) | grow(left, joint // 2 + 1)) & ~mortar
    inked = (grow(down, joint + 1) | grow(right, joint)) & ~mortar & ~lit
    img[lit] = img[lit] * (1 - .6 * edge) + np.array(pal["light"], np.float32) * .6 * edge
    img[inked] = img[inked] * (1 - .55 * edge) + np.array(pal["ink"], np.float32) * .55 * edge
    if inset:
        # A carved inner square on big floor slabs (the dwarf floors): a dark line set a few texels in, lit inside.
        for i in ids:
            m = lab == i
            ys, xs = np.nonzero(m)
            if len(ys) < 3000: continue
            y0, y1, x0, x1 = ys.min() + 14, ys.max() - 14, xs.min() + 14, xs.max() - 14
            ring = np.zeros_like(m); ring[y0:y1 + 1, x0] = ring[y0:y1 + 1, x1] = True; ring[y0, x0:x1 + 1] = ring[y1, x0:x1 + 1] = True
            img[ring & m] = img[ring & m] * .7 + np.array(pal["ink"], np.float32) * .3
            r2 = np.zeros_like(m); r2[y0 + 1:y1, x0 + 1] = True; r2[y0 + 1, x0 + 1:x1] = True
            img[r2 & m] = img[r2 & m] * .85 + np.array(pal["light"], np.float32) * .15
    # Cracks: wedges from a stone's top edge down into it, two texels wide where they start, with a branch, inked.
    for i in ids:
        m = lab == i
        ys, xs = np.nonzero(m & grow(up, joint + 2))
        if len(ys) < 20 or rng.random() > .7 * cracks: continue
        for _ in range(1 + int(rng.random() < .4)):
            k = int(rng.integers(0, len(ys))); y, x = int(ys[k]), int(xs[k])
            dx = rng.choice([-1, 0, 1]); length = int(rng.integers(10, 30))
            for step in range(length):
                if not m[y % h, x % w]: break
                img[y % h, x % w] = np.array(pal["mortar"], np.float32) * 1.1
                if step < length // 3 and m[y % h, (x + 1) % w]: img[y % h, (x + 1) % w] = np.array(pal["mortar"], np.float32) * 1.2
                if rng.random() < .4: dx = int(np.clip(dx + rng.choice([-1, 1]), -1, 1))
                y += 1; x += dx
                if step == length // 2 and rng.random() < .5:
                    by, bx = y, x
                    for _ in range(int(rng.integers(4, 10))):
                        if not m[by % h, bx % w]: break
                        img[by % h, bx % w] = np.array(pal["ink"], np.float32)
                        by += 1; bx += 1 if dx <= 0 else -1
    # Chips: a few corners knocked off into the mortar.
    corners = (up | down) & (left | right)
    cy, cx = np.nonzero(corners)
    for k in rng.choice(len(cy), size=min(len(cy), 40), replace=False) if len(cy) else []:
        y, x = cy[k], cx[k]
        r = int(rng.integers(2, 4))
        img[max(0, y - r):y + r, max(0, x - r):x + r] = pal["mortar"]
    img[mortar] = np.array(pal["mortar"], np.float32) * np.array([.9, 1.0, 1.08], np.float32)[np.clip(t[mortar], 0, 2)][:, None]
    return img, mortar


def touching_edge(lab, floor):
    """Stones that touch the cell's edge (left/right for walls; all four for floors): variants leave them alone."""
    edge = set(np.unique(lab[:, 0])) | set(np.unique(lab[:, -1]))
    if floor: edge |= set(np.unique(lab[0, :])) | set(np.unique(lab[-1, :]))
    return edge


def variant(img, lab, pal, floor, seed, kind, share=.45):
    """A copy with whole inner stones changed: recoloured (shift), sooted, ember-veined or rune-marked."""
    rng = RNG(seed); out = img.copy(); h, w = lab.shape
    keep = touching_edge(lab, floor)
    ids = [i for i in np.unique(lab) if i not in keep]
    for i in ids:
        if rng.random() > share: continue
        m = lab == i
        if kind == "shift":
            tint = np.array(pal["tint"][int(rng.integers(0, len(pal["tint"])))], np.float32)
            face = m & (out.sum(-1) > np.array(pal["mortar"]).sum() + 20)
            out[face] = out[face] * .55 + tint * .45 * (out[face].mean(-1, keepdims=True) / max(1, tint.mean()))
        elif kind == "soot":
            n = wrap_noise(h, w, 8, seed + int(i)) > .5
            s = m & n
            out[s] = out[s] * .55 + np.array(pal["soot"], np.float32) * .45
        elif kind == "ember":
            # A glowing seam across the stone: the forge's and the pit's lava showing through the cracks.
            ys, xs = np.nonzero(m)
            if len(ys) < 500: continue
            k = int(rng.integers(0, len(ys))); y, x = int(ys[k]), int(xs[k]); dy = rng.choice([-1, 0, 1])
            for _ in range(int(rng.integers(20, 50))):
                if not m[y % h, x % w]: break
                out[y % h, x % w] = np.array(pal["accent"], np.float32) * 1.1
                for oy, k2 in ((1, .6), (-1, .45), (2, .25)):
                    yy, xx = (y + oy) % h, x % w
                    if m[yy, xx]: out[yy, xx] = out[yy, xx] * (1 - k2) + np.array(pal["accent"], np.float32) * k2
                if rng.random() < .4: dy = int(np.clip(dy + rng.choice([-1, 1]), -1, 1))
                y += dy; x += 1
        elif kind == "rune":
            # A gold rune cut in the stone (the dwarf halls' gold symbols): a small angular glyph, lit edge below.
            ys, xs = np.nonzero(m)
            if len(ys) < 1200: continue
            cy, cx = int(ys.mean()), int(xs.mean())
            g = rng.integers(0, 2, (5, 4)); g[:, 1] = 1; g[0, :] = 1
            for gy in range(5):
                for gx in range(4):
                    if g[gy, gx]:
                        yy, xx = cy - 7 + gy * 3, cx - 6 + gx * 3
                        out[yy:yy + 3, xx:xx + 3] = np.array(pal["accent"], np.float32) * (.85 + .15 * (gx % 2))
                        out[yy + 3, xx:xx + 3] = np.array(pal["ink"], np.float32)
    return out


def save(img, path):
    Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(path)


def build(root, n):
    pal = PALETTES[n]; name = f"Realms{n}"
    out = os.path.join(root, name); os.makedirs(out, exist_ok=True)
    P = lambda s: os.path.join(out, f"{name} {s}.png")
    seed = 100 * n
    # Walls: courses of 48 texels (8 a tile) in the halls and the forge, rubble in the pit.
    if n == 3: wl, _ = rubble_layout(384, 256, seed + 1, n=52)
    else: wl, _ = course_layout(384, 256, 48 if n == 1 else 64, seed + 1, min_w=56 if n == 1 else 48, max_w=120 if n == 1 else 96)
    wall, _ = paint(wl, pal, seed + 2, pal["stone"], joint=3, edge=.55 if n == 3 else 1.0)
    save(wall, P("wall"))
    save(variant(wall, wl, pal, False, seed + 3, "shift"), P("wall shift 1"))
    save(variant(wall, wl, pal, False, seed + 4, "shift"), P("wall shift 2"))
    save(variant(wall, wl, pal, False, seed + 5, "soot", .5), P("wall soot"))
    if n == 1: save(variant(wall, wl, pal, False, seed + 6, "rune", .2), P("wall rune"))
    else: save(variant(wall, wl, pal, False, seed + 6, "ember", .35), P("wall ember"))
    # Floors: inset slabs in the halls, smaller plates in the forge, cracked obsidian in the pit.
    if n == 1: fl, _ = slab_layout(256, 256, 128, seed + 7)
    elif n == 2: fl, _ = slab_layout(256, 256, 64, seed + 7)
    else: fl, _ = rubble_layout(256, 256, seed + 7, n=34)
    floor, _ = paint(fl, pal, seed + 8, pal["floor"], joint=3, inset=(n == 1), edge=.6 if n == 3 else 1.0)
    save(floor, P("floor"))
    save(variant(floor, fl, pal, True, seed + 9, "shift"), P("floor shift 1"))
    save(variant(floor, fl, pal, True, seed + 10, "shift"), P("floor shift 2"))
    save(variant(floor, fl, pal, True, seed + 11, "soot", .5), P("floor soot"))
    if n != 1: save(variant(floor, fl, pal, True, seed + 12, "ember", .4), P("floor ember"))
    # Ceiling: dark slabs; trim: one course of long dressed stones.
    # Ceiling: long irregular stones in staggered courses, dim edges (seen from below in torchlight, a strong lit edge read
    # as a tiled grid).
    if n == 3: cl, _ = rubble_layout(384, 384, seed + 13, n=40)
    else: cl, _ = course_layout(384, 384, 64, seed + 13, min_w=80, max_w=170)
    ceil, _ = paint(cl, pal, seed + 14, pal["ceiling"], joint=3, cracks=.6, edge=.35)
    save(ceil, P("ceiling"))
    tl, _ = course_layout(384, 384, 96, seed + 15, min_w=120, max_w=200)
    trim, _ = paint(tl, pal, seed + 16, pal["stone"], joint=3, cracks=.3)
    save(trim, P("trim"))
    print(name, "done")


if __name__ == "__main__":
    root = sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/Levels/Shared/Room styles/Realms"
    for n in (1, 2, 3): build(root, n)

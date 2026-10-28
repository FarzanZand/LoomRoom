"""Bedroom surfaces from Meshy paintings (Temp/Paint/Room/gen): seamless pixel tiles at the room's pixel size.

The room is built about 20 units to the metre and drawn at 128 texels per metre, 6.4 texels per world unit. Each
painting is brought down to one texel per painted pixel and recomposed by hand-written rules, never repainted:
    floorboards  planks cut out of the painting and laid again in staggered rows that wrap, so the tile repeats
                 without a visible column of plank ends
    plaster      the painting's soft blotches kept faint, cracks and stains taken out (they would repeat), seams
                 cross-faded
    tabletop     one texture for the whole top (its UVs span it once): the painting's planks laid end to end along
                 the table's length
    rug          the painting cropped to the rug, fringe cut out with alpha
    painting     retired: drawn by rules in Tools/bedroom_painting.py

    python Tools/room_surfaces.py [out_dir]
"""
import sys, random
from pathlib import Path
import numpy as np
from PIL import Image

GEN = Path("Temp/Paint/Room/gen")
OUT = Path(sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/World/Art/Bedroom")
TEXELS_PER_UNIT = 6.4


def load(name):
    return np.asarray(Image.open(GEN / name / f"{name}.png").convert("RGB")).astype(np.float32) / 255


def save(a, name):
    OUT.mkdir(parents=True, exist_ok=True)
    a = np.clip(a * 255 + .5, 0, 255).astype(np.uint8)
    Image.fromarray(a).save(OUT / f"{name}.png")
    print(name, a.shape[1], "x", a.shape[0])


def down(a, block):
    """One texel per painted pixel: each block's median colour (robust to the painter's soft edges)."""
    h, w = a.shape[0] // block, a.shape[1] // block
    b = a[:h * block, :w * block].reshape(h, block, w, block, 3)
    return np.median(b.transpose(0, 2, 1, 3, 4).reshape(h, w, block * block, 3), axis=2)


def lum(a):
    return a[..., 0] * .299 + a[..., 1] * .587 + a[..., 2] * .114


def grade(a, sat=1., bright=1., tint=(1, 1, 1)):
    g = lum(a)[..., None]
    return np.clip((g + (a - g) * sat) * bright * np.array(tint), 0, 1)


# ── Planks ────────────────────────────────────────────────────────────────────

def plank_pieces(a, along_x=True, dark=.6):
    """Cut a texel image of planks into whole planks: rows between dark gap lines, split at dark end columns.
    Returns (pieces, gap colour, plank height)."""
    if not along_x:
        a = a.transpose(1, 0, 2)
    L = lum(a)
    row_dark = L.mean(1) < dark * np.median(L.mean(1))
    rows, start = [], None
    for y, d in enumerate(list(row_dark) + [True]):
        if not d and start is None:
            start = y
        elif d and start is not None:
            if y - start >= 4:
                rows.append((start, y))
            start = None
    gap = np.median(a[row_dark].reshape(-1, 3), axis=0)
    pieces = []
    for y0, y1 in rows:
        band = a[y0:y1]
        col_dark = lum(band).mean(0) < dark * np.median(lum(band))
        x, segs, s = 0, [], None
        for x, d in enumerate(list(col_dark) + [True]):
            if not d and s is None:
                s = x
            elif d and s is not None:
                segs.append((s, x)); s = None
        # Pieces touching the painting's edge were cut by it: kept only when long.
        for s0, s1 in segs:
            if s1 - s0 >= 12:
                pc = band[:, s0:s1].copy()
                # Knots and holes (much darker blobs) would repeat as a pattern: each such texel takes the plank's
                # colour from its own column a few rows away.
                L2 = lum(pc); med = np.median(L2)
                # Cup rings and pale scratches (much lighter) likewise.
                odd = (L2 < med * .72) | (L2 > med * 1.3)
                for y, x in zip(*np.nonzero(odd)):
                    for dy in (3, -3, 5, -5, 1, -1):
                        yy = y + dy
                        if 0 <= yy < pc.shape[0] and not odd[yy, x]:
                            pc[y, x] = pc[yy, x]; break
                pieces.append(pc)
    height = int(np.median([p.shape[0] for p in pieces]))
    return pieces, gap, height


def fit_height(p, h):
    """A plank brought to height h: middle rows dropped or repeated (its lit and shaded edges stay)."""
    if p.shape[0] == h:
        return p
    idx = np.round(np.linspace(0, p.shape[0] - 1, h)).astype(int)
    return p[idx]


def lay_rows(pieces, gap, plank_h, width, rows, rng, min_len=None, max_len=None, tone=.0):
    """Rows of planks, each row exactly `width` long and wrapping, a one-texel gap between planks and rows.
    A plank shorter than min_len is made of several painted pieces joined with no gap (the grain runs on), and each
    plank gets a slight tone of its own (boards are never all one colour)."""
    def piece():
        p = fit_height(pieces[rng.randrange(len(pieces))], plank_h)
        if rng.random() < .5:
            p = p[:, ::-1]
        if rng.random() < .3:
            p = p[::-1]
        return p
    out = np.zeros((rows * (plank_h + 1), width, 3), np.float32)
    for r in range(rows):
        row = np.zeros((plank_h, width, 3), np.float32); x = 0
        while x < width:
            target = rng.randint(min_len, max_len) if min_len else None
            # Never leave a sliver at the row's end: the last plank takes the rest.
            if target and width - x - target - 1 < min_len // 2:
                target = width - x
            p = piece()
            while target and p.shape[1] < target:
                q = piece()
                # Trim the joined piece's own end shading so the join reads as one board.
                p = np.concatenate([p[:, :-2], q[:, 2:]], axis=1)
            if target:
                p = p[:, :target]
            elif max_len and p.shape[1] > max_len:
                s0 = rng.randrange(p.shape[1] - max_len + 1); p = p[:, s0:s0 + max_len]
            if tone:
                p = p * (1 + (rng.random() - .5) * 2 * tone)
            n = min(p.shape[1], width - x)
            row[:, x:x + n] = p[:, :n]; x += n
            if x < width:
                row[:, x] = gap; x += 1
        # The row's end is a plank end too: a gap there, then the row is turned so it lands anywhere.
        row[:, width - 1] = gap
        row = np.roll(row, rng.randrange(width), axis=1)
        y = r * (plank_h + 1)
        out[y:y + plank_h] = row
        out[y + plank_h] = gap
    return out


def floorboards(rng):
    a = grade(down(load("Floorboards"), 5), sat=.8, bright=.9, tint=(1., .97, .92))
    pieces, gap, h = plank_pieces(a)
    gap = gap * .8 + np.array([.16, .11, .07]) * .2
    # 256 texels = 40 units = 2 m across; rows fill about the same height.
    rows = round(256 / (h + 1))
    # Boards 1 to 2.5 m long (128 to 320 texels): the tile is 256 wide, so most rows have one or two plank ends.
    save(lay_rows(pieces, gap, h, 256, rows, rng, min_len=128, max_len=300, tone=.07), "Floorboards")


def tabletop(rng):
    a = grade(down(load("Table_top"), 8), sat=.92, bright=.95)
    pieces, gap, h = plank_pieces(a)
    # The top is 82.5 x 110.5 units: planks run along its length (the texture's height), so lay them as rows of the
    # transposed image.
    W, H = round(110.5 * TEXELS_PER_UNIT), round(82.5 * TEXELS_PER_UNIT)
    rows = int(np.ceil(H / (h + 1)))
    t = lay_rows(pieces, gap, h, W, rows, rng, min_len=260, max_len=707, tone=.06)[:H]
    save(t.transpose(1, 0, 2)[::-1], "Tabletop")


# ── Plaster ───────────────────────────────────────────────────────────────────

def plaster(rng, name="Plaster", src="Plaster_chipped", tint=(1, 1, 1), bright=1., crop=0, marks=6, size=512):
    """A plaster tile from a painting: its soft blotches make the base, and its flaked patches and cracks are cut out
    as separate marks and scattered a few at a time over a tile `size` texels wide (4 m at 512), so the wall does not
    show one painting repeating every 2 m. One texel per painted pixel."""
    from scipy import ndimage
    a = load(src)
    if crop:
        a = a[crop:-crop, crop:-crop]
    a = np.asarray(Image.fromarray((a * 255).astype(np.uint8)).resize((1024, 1024), Image.BOX)).astype(np.float32) / 255
    a = down(a, 4)                                  # 256 x 256
    smooth = ndimage.median_filter(a, size=(9, 9, 1), mode="wrap")
    sat = lambda c: (c.max(2) - c.min(2)) / (c.max(2) + 1e-6)
    med_sat = np.median(sat(a))
    odd = (np.abs(lum(a) - lum(smooth)) > .045) | (sat(a) < med_sat * .6) | (np.abs(lum(a) - np.median(lum(a))) > .09)
    odd = ndimage.binary_fill_holes(ndimage.binary_closing(odd, iterations=2))
    odd = ndimage.binary_dilation(odd, iterations=2)
    lab, n = ndimage.label(odd)
    # The base: every mark filled with the plaster around it, the broad drift taken out.
    idx = ndimage.distance_transform_edt(odd, return_distances=False, return_indices=True)
    base = a[idx[0], idx[1]]
    base = ndimage.gaussian_filter(base, sigma=(1, 1, 0), mode="wrap") * .5 + base * .5
    drift = ndimage.gaussian_filter(base, sigma=(40, 40, 0), mode="wrap")
    med = np.median(base.reshape(-1, 3), axis=0)
    base = base - drift + med
    # Seamless base, then four copies turned and mirrored so its blotches don't line up.
    m = base.shape[0]; band = 32
    for axis in (0, 1):
        b = np.moveaxis(base, axis, 0).copy()
        for i in range(band):
            w = .5 * (1 - i / band)
            top, bot = b[i].copy(), b[m - 1 - i].copy()
            b[i] = top * (1 - w) + bot * w; b[m - 1 - i] = bot * (1 - w) + top * w
        base = np.moveaxis(b, 0, axis)
    reps = size // m
    out = np.zeros((size, size, 3), np.float32)
    for ty in range(reps):
        for tx in range(reps):
            out[ty * m:(ty + 1) * m, tx * m:(tx + 1) * m] = base
    # The marks: each one is the painting's own pixels, placed whole at random spots (wrapping at the edges).
    pieces = []
    for o in ndimage.find_objects(lab):
        h, w = o[0].stop - o[0].start, o[1].stop - o[1].start
        if h * w < 12:
            continue
        piece = a[o]; mask = (lab[o] > 0)
        pieces.append((piece - smooth[o] + 0, mask, piece))
    pieces.sort(key=lambda q: -q[1].sum())
    pieces = pieces[:max(1, marks * 2)]
    for _ in range(marks * reps * reps // 2 if reps > 1 else marks):
        diff, mask, piece = pieces[rng.randrange(len(pieces))]
        if rng.random() < .5:
            piece, mask = piece[:, ::-1], mask[:, ::-1]
        y0, x0 = rng.randrange(size), rng.randrange(size)
        for yy, xx in zip(*np.nonzero(mask)):
            out[(y0 + yy) % size, (x0 + xx) % size] = piece[yy, xx]
    save(grade(out, bright=bright, tint=tint), name)


# ── Rug and painting ──────────────────────────────────────────────────────────

def rug(rng):
    src = load("Rug")
    bg = np.median(np.concatenate([src[:20].reshape(-1, 3), src[-20:].reshape(-1, 3)]), axis=0)
    mask = np.abs(src - bg).sum(2) > .12
    ys, xs = np.nonzero(mask)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    a = src[y0:y1, x0:x1]; m = mask[y0:y1, x0:x1]
    # 50 x 38 units (2.5 x 1.9 m) at 6.4 texels per unit; long side along the image's height after the turn.
    W, H = round(38 * TEXELS_PER_UNIT), round(50 * TEXELS_PER_UNIT)
    img = Image.fromarray((a * 255).astype(np.uint8)).rotate(90, expand=True).resize((W, H), Image.BOX)
    alpha = Image.fromarray((m * 255).astype(np.uint8)).rotate(90, expand=True).resize((W, H), Image.BOX)
    rgba = np.dstack([np.asarray(img).astype(np.float32) / 255, (np.asarray(alpha) > 140).astype(np.float32)])
    OUT.mkdir(parents=True, exist_ok=True)
    Image.fromarray(np.clip(rgba * 255 + .5, 0, 255).astype(np.uint8), "RGBA").save(OUT / "Rug.png")
    print("Rug", W, "x", H)


def painting(*args, **kwargs):
    """Retired: the wall painting is drawn by rules now (Tools/bedroom_painting.py); the Meshy crop is not used."""
    print("painting: drawn by Tools/bedroom_painting.py, skipped"); return


def _painting_meshy_crop(rng, aspect=110.4 / 48.3, width_units=110.4):
    p = GEN / "Wall_painting" / "Wall_painting.png"
    if not p.exists():
        print("no wall painting yet"); return
    a = np.asarray(Image.open(p).convert("RGB")).astype(np.float32) / 255
    h, w = a.shape[:2]
    ch = min(h, int(w / aspect)); y0 = (h - ch) // 2
    a = a[y0:y0 + ch]
    W = round(width_units * TEXELS_PER_UNIT); H = round(W / aspect)
    img = Image.fromarray((a * 255).astype(np.uint8)).resize((W, H), Image.BOX)
    save(np.asarray(img).astype(np.float32) / 255, "Painting")


if __name__ == "__main__":
    rng = random.Random(7)
    floorboards(rng); tabletop(rng); plaster(rng)
    plaster(rng, "Ceiling", src="Ceiling_plaster", crop=64, marks=4)
    rug(rng); painting(rng)

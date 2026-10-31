"""The dice room's floor board: the Dungeon Master's gaming board, 4.2 x 2.8 m at 128 texels a metre (538 x 358), seen
from the room's south door (the top of the image is the far, north edge). Six squares, 1 2 3 along the far row and 4 5 6
along the near one, each with its pips in the top-left corner and a picture of what the face brings: 1 a skull (an
ambush), 2 a horned head (a tough enemy), 3 an empty ring (nothing), 4 a heart (a rest), 5 an iron chest, 6 a gold
chest. House style: an old painted board, the paint chipped back to the wood, colour varying texel to texel in small
clusters, dark ink round every picture with its upper-left edge lit, brass inlay between the squares, a dark oak frame,
grime in the corners and the middle scuffed pale where the die keeps landing. The frame's oak also covers the board's
sides (the mesh maps them onto the frame strip).
python dice_board.py out.png"""
import sys
import numpy as np
from PIL import Image, ImageDraw

TEXELS = 128
W, H = round(4.2 * TEXELS), round(2.8 * TEXELS)      # 538 x 358
FRAME = 16                                           # the oak frame, texels
INLAY = 3                                            # brass between the squares
rng = np.random.default_rng(11)


def noise(shape, cells, seed):
    """Value noise: a coarse random grid blown up with a smooth step (blotches with uneven soft edges)."""
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


blot = noise((H, W), 26, 1) * .6 + noise((H, W), 60, 2) * .4
fine = noise((H, W), 110, 3) * .7 + rng.random((H, W)) * .3       # clusters a few texels across
grain = rng.random((H, W))
chips = noise((H, W), 48, 4) + (rng.random((H, W)) - .5) * .25
grime_field = noise((H, W), 14, 5)


def tone(base, t, spread=.12):
    """Three tones of a colour laid by t in clusters (no gradient), nudged per texel."""
    return base * (1 + spread if t > .62 else 1 if t > .34 else 1 - spread)


# ── Wood: the frame and the paint's ground ──────────────────────────
oak = np.array([.27, .19, .12])
oak_dark = np.array([.17, .12, .08])
img = np.zeros((H, W, 3))
ys, xs = np.mgrid[0:H, 0:W]
# Planks run left to right, a dark seam every 45 texels; grain streaks along them.
streak = noise((H, W // 6 + 1), 40, 6)[:, (np.arange(W) // 6)]
for y in range(H):
    for x in range(W):
        t = blot[y, x] * .5 + streak[y, x] * .5 + (fine[y, x] - .5) * .2
        c = tone(oak, t, .16)
        if y % 45 in (0, 1): c = oak_dark * (.9 + grain[y, x] * .2)
        img[y, x] = c * (1 + (grain[y, x] - .5) * .05)
wood = img.copy()

# ── The six squares ─────────────────────────────────────────────────
iw, ih = W - 2 * FRAME, H - 2 * FRAME
cw, ch = (iw - 2 * INLAY) / 3, (ih - INLAY) / 2
oxblood = np.array([.36, .17, .13])
bone = np.array([.66, .59, .43])
squares = {}
for n in range(1, 7):
    col, row = (n - 1) % 3, (n - 1) // 3
    x0 = FRAME + col * (cw + INLAY); y0 = FRAME + row * (ch + INLAY)
    squares[n] = (int(round(x0)), int(round(y0)), int(round(x0 + cw)), int(round(y0 + ch)))
    ground = oxblood if n % 2 == 1 else bone
    a, b, c_, d = squares[n]
    for y in range(b, d):
        for x in range(a, c_):
            t = blot[y, x] * .55 + fine[y, x] * .45
            img[y, x] = tone(ground, t, .13) * (1 + (grain[y, x] - .5) * .05)

# ── Pictures: drawn as masks, then painted texel by texel ──────────
def mask(draw_fn, n):
    """A mask the size of the image with draw_fn drawing in square n's own 0..100 coordinates."""
    a, b, c_, d = squares[n]
    s = min(c_ - a, d - b) / 100.0
    ox, oy = a + (c_ - a - 100 * s) / 2 + 6 * s, b + (d - b - 100 * s) / 2 + 4 * s   # a little right of centre: the pips are top left
    m = Image.new("L", (W, H), 0)
    dr = ImageDraw.Draw(m)
    P = lambda pts: [(ox + px * s, oy + py * s) for px, py in pts]
    E = lambda x0, y0, x1, y1: [ox + x0 * s, oy + y0 * s, ox + x1 * s, oy + y1 * s]
    draw_fn(dr, P, E)
    return np.array(m) > 127


def skull(dr, P, E):
    dr.ellipse(E(24, 12, 76, 62), fill=255)
    dr.rounded_rectangle(E(33, 52, 67, 80), 6, fill=255)


def skull_holes(dr, P, E):
    dr.ellipse(E(32, 34, 47, 52), fill=255); dr.ellipse(E(53, 34, 68, 52), fill=255)
    dr.polygon(P([(50, 54), (45, 63), (55, 63)]), fill=255)
    for x in (40, 46, 52, 58): dr.line(P([(x, 70), (x, 79)]), fill=255, width=1)


def horned(dr, P, E):
    dr.ellipse(E(30, 30, 70, 82), fill=255)
    dr.polygon(P([(34, 44), (16, 26), (12, 8), (24, 22), (42, 36)]), fill=255)
    dr.polygon(P([(66, 44), (84, 26), (88, 8), (76, 22), (58, 36)]), fill=255)


def horned_eyes(dr, P, E):
    dr.polygon(P([(37, 52), (47, 55), (37, 57)]), fill=255)
    dr.polygon(P([(63, 52), (53, 55), (63, 57)]), fill=255)


def horned_mouth(dr, P, E):
    dr.line(P([(40, 70), (50, 66), (60, 70)]), fill=255, width=2)


def ring(dr, P, E):
    dr.ellipse(E(28, 22, 72, 66), fill=255)
    dr.ellipse(E(38, 32, 62, 56), fill=0)


def heart(dr, P, E):
    dr.ellipse(E(22, 22, 52, 50), fill=255); dr.ellipse(E(48, 22, 78, 50), fill=255)
    dr.polygon(P([(24, 42), (76, 42), (50, 76)]), fill=255)


def chest(dr, P, E):
    dr.rectangle(E(20, 40, 80, 74), fill=255)
    dr.pieslice(E(20, 22, 80, 58), 180, 360, fill=255)


def chest_bands(dr, P, E):
    for x in (28, 70): dr.rectangle(E(x, 26, x + 4, 74), fill=255)
    dr.rectangle(E(20, 40, 80, 43), fill=255)


def chest_lock(dr, P, E):
    dr.rectangle(E(45, 42, 55, 54), fill=255)


def chest_rivets(dr, P, E):
    for x in (24, 38, 62, 76):
        for y in (48, 66): dr.rectangle(E(x, y, x + 2, y + 2), fill=255)


ink = np.array([.09, .06, .05])
paints = {
    1: [(skull, np.array([.80, .74, .58])), (skull_holes, np.array([.10, .07, .06]))],
    2: [(horned, np.array([.42, .16, .13])), (horned_eyes, np.array([.93, .74, .25])), (horned_mouth, np.array([.12, .06, .05]))],
    3: [(ring, np.array([.55, .52, .47]))],
    4: [(heart, np.array([.54, .17, .14]))],
    5: [(chest, np.array([.36, .37, .39])), (chest_bands, np.array([.22, .22, .24])), (chest_rivets, np.array([.62, .62, .60])), (chest_lock, np.array([.18, .18, .19]))],
    6: [(chest, np.array([.78, .58, .20])), (chest_bands, np.array([.50, .34, .12])), (chest_rivets, np.array([.96, .85, .50])), (chest_lock, np.array([.32, .22, .09]))],
}


def outline(m):
    """Texels just outside a mask (four-neighbour), and the mask's own upper-left edge (to be lit)."""
    sh = lambda a, dy, dx: np.roll(np.roll(a, dy, 0), dx, 1)
    around = (sh(m, 1, 0) | sh(m, -1, 0) | sh(m, 0, 1) | sh(m, 0, -1)) & ~m
    lit = m & ~(sh(m, 1, 0) & sh(m, 0, 1))
    return around, lit


painted = np.zeros((H, W), bool)
for n, layers in paints.items():
    first = None
    for draw_fn, colour in layers:
        m = mask(draw_fn, n)
        if first is None: first = m
        painted |= m
        t = blot * .55 + fine * .45
        layer = np.where((t > .6)[..., None], colour * 1.13, np.where((t > .34)[..., None], colour, colour * .82))
        layer = layer * (1 + (grain[..., None] - .5) * .06)
        img[m] = layer[m]
    # Ink round the whole picture (thick-ish, broken here and there), its upper-left edge lit.
    around, lit = outline(first)
    keep = around & (fine > .12)
    img[keep] = ink * (1 + grain[keep][..., None] * .3)
    img[lit] = np.clip(img[lit] * 1.3 + .02, 0, 1)

# ── Pips in each square's top-left corner ──────────────────────────
for n in range(1, 7):
    a, b, _, _ = squares[n]
    s, r = 12, 4.2
    cx, cy = a + 22, b + 22
    pts = {1: [(0, 0)], 2: [(-1, -1), (1, 1)], 3: [(-1, -1), (0, 0), (1, 1)], 4: [(-1, -1), (1, -1), (-1, 1), (1, 1)],
           5: [(-1, -1), (1, -1), (0, 0), (-1, 1), (1, 1)], 6: [(-1, -1), (-1, 0), (-1, 1), (1, -1), (1, 0), (1, 1)]}[n]
    pip = np.array([.84, .78, .62]) if n % 2 == 1 else np.array([.13, .09, .07])
    for px, py in pts:
        x, y = cx + px * s, cy + py * s
        d = np.hypot(xs + .5 - x, ys + .5 - y)
        m = d < r
        painted |= d < r + 1.5
        img[m] = pip * (1 + (grain[m][..., None] - .5) * .2)
        rim = (d >= r) & (d < r + 1.1)
        img[rim] = img[rim] * .7

# ── Brass inlay between the squares ─────────────────────────────────
brass = np.array([.55, .43, .20])
inlay = np.zeros((H, W), bool)
for n in range(1, 7):
    a, b, c_, d = squares[n]
    if n % 3 != 0: inlay[b:d, c_:c_ + INLAY] = True
    if n <= 3: inlay[d:d + INLAY, a:c_] = True
inlay[FRAME - 2:H - FRAME + 2, FRAME - 2:FRAME] = True; inlay[FRAME - 2:H - FRAME + 2, W - FRAME:W - FRAME + 2] = True
inlay[FRAME - 2:FRAME, FRAME - 2:W - FRAME + 2] = True; inlay[H - FRAME:H - FRAME + 2, FRAME - 2:W - FRAME + 2] = True
t = blot + (fine - .5) * .3
bcol = np.where((t > .55)[..., None], brass * 1.25, np.where((t > .3)[..., None], brass, brass * .7))
img[inlay] = bcol[inlay] * (1 + (grain[inlay][..., None] - .5) * .1)

# ── Wear ────────────────────────────────────────────────────────────
inside = np.zeros((H, W), bool); inside[FRAME:H - FRAME, FRAME:W - FRAME] = True
# Paint chipped back to the wood, more toward the edges of the squares and the board.
edge_d = np.minimum(np.minimum(xs - FRAME, W - FRAME - xs), np.minimum(ys - FRAME, H - FRAME - ys)) / (H / 2)
small = noise((H, W), 150, 7) + (rng.random((H, W)) - .5) * .3
near_edge = 1 - np.clip(edge_d * 3, 0, 1)
chipped = inside & ~inlay & ~painted & (small > .9 - near_edge * .16)
chipped |= inside & ~inlay & painted & (small > .97)
img[chipped] = wood[chipped] * .92
# The middle scuffed pale where the die lands, in short scratches.
for _ in range(70):
    x, y = rng.normal(W / 2, W * .16), rng.normal(H / 2, H * .18)
    ang = rng.uniform(0, np.pi); ln = rng.integers(3, 9)
    for k in range(ln):
        px, py = int(x + np.cos(ang) * k), int(y + np.sin(ang) * k)
        if 0 <= px < W and 0 <= py < H and inside[py, px]: img[py, px] = np.clip(img[py, px] * 1.22 + .03, 0, 1)
# Grime: dark in the corners and along the frame, blotchy.
corner = np.clip(1 - np.minimum(np.hypot(xs - 0, ys - 0), np.minimum(np.hypot(xs - W, ys - 0), np.minimum(np.hypot(xs, ys - H), np.hypot(xs - W, ys - H)))) / 90, 0, 1)
g = (grime_field * .6 + corner * .6 + (fine - .5) * .3) > .72
img[g] = img[g] * .78
# The frame's inner edge: a dark crease where the board meets the frame.
crease = np.zeros((H, W), bool)
crease[FRAME:FRAME + 1, FRAME:W - FRAME] = True; crease[H - FRAME - 1, FRAME:W - FRAME] = True
crease[FRAME:H - FRAME, FRAME] = True; crease[FRAME:H - FRAME, W - FRAME - 1] = True
img[crease] = img[crease] * .6
# The frame's outer rim: lit along its top edge.
img[0:1, :] = np.clip(img[0:1, :] * 1.25, 0, 1); img[:, 0:1] = np.clip(img[:, 0:1] * 1.15, 0, 1)

Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(sys.argv[1])
print("wrote", sys.argv[1], W, H)

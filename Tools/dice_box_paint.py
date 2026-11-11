"""Paints the bedroom's dice box (the model and its texture layout come from Tools/dice_box.py) at the bedroom's 6.4
texels a unit, drawn the way the room's furniture is: boards with a dark line between them and a few flat tones of grain
laid in runs, brass in blotchy texels with a lit and a shaded edge, burgundy felt inside. Colours are the room's own
(Furniture/Wood mahogany, Brass, the trophy wall's felt). Light comes from the upper left, as on the room's hand-drawn
marks: a strap or plate leaves a one-texel shadow on the wood to its right and under it.

    python Tools/dice_box_paint.py "<folder with Dice box layout.json>"      writes Dice box.png there
"""
import json, math, os, sys
import numpy as np
from PIL import Image

folder = sys.argv[1]
lay = json.load(open(os.path.join(folder, 'Dice box layout.json')))
S = lay['sizes']
rng = np.random.default_rng(11)


def hexes(*cs): return np.array([[int(c[i:i + 2], 16) for i in (0, 2, 4)] for c in cs], float)


WOOD = hexes('3f1a0b', '491f0d', '572410', '5f2912', '6b3014', '753416', '874220')   # dark to light; the last is a worn edge
LINE = hexes('260f06')[0]                                                             # the drawn line between boards
# Old brass, a step deeper than the room's Brass.png: the box stands under a lamp, and on its lit top the room's own
# brass tones burn out to cream and lose their blotches.
BRASS = hexes('453214', '5f461b', '795c24', '8f6e2c', 'a37f34', 'ba943f', 'd6b458')  # tarnish to a worn bright edge
BRASS_LINE = hexes('3a2a10')[0]
FELT = hexes('5c1a1c', '6e2022', '802627', '8c2a2a', '983030')
HOLE = hexes('140c08')[0]


def runs(w, base, lo, hi, palette, spread=1, keep=.55):
    """One row of grain: runs of a few texels, most at the board's tone, the rest a step or two off it."""
    row = np.empty(w, int); x = 0
    while x < w:
        n = int(rng.integers(lo, hi + 1))
        t = base if rng.random() < keep else base + int(rng.choice([-2, -1, -1, 1, 1, 2][0 if spread > 1 else 1: 6 if spread > 1 else 5]))
        row[x:x + n] = np.clip(t, 0, len(palette) - 2); x += n
    return row


def boards(w, h, splits, butt=True, along='x'):
    """Boards lying along x, split at the given rows (each split row is the dark line between two boards)."""
    if along == 'y': return boards(h, w, splits, butt).transpose(1, 0, 2)
    idx = np.zeros((h, w), int); line = np.zeros((h, w), bool)
    edges = [-1] + list(splits) + [h]
    for a, b in zip(edges[:-1], edges[1:]):
        base = int(rng.choice([2, 3, 3, 4]))
        for y in range(a + 1, b):
            idx[y] = runs(w, base, 3, 11, WOOD)
        if b - a > 3 and a + 1 < h: idx[a + 1] = np.minimum(idx[a + 1] + 1, len(WOOD) - 2)   # the board's lit upper edge
        if butt and w > 40 and b - a > 2:                                                   # where two lengths meet
            x = int(rng.integers(w // 4, 3 * w // 4)); line[a + 1:b, x] = True
        if b < h: line[b] = True
    out = WOOD[idx]; out[line] = LINE
    return out


def darker(img, mask, steps=1, palette=WOOD):
    """Moves the masked texels down the palette (a shadow or grime in the same flat tones)."""
    flat = img.reshape(-1, 3); m = mask.reshape(-1)
    d = ((flat[m][:, None, :] - palette[None]) ** 2).sum(2).argmin(1)
    known = ((flat[m] - palette[d]) ** 2).sum(1) < 4
    new = flat[m].copy(); new[known] = palette[np.clip(d[known] - steps, 0, len(palette) - 1)]
    flat[m] = new


def brass(w, h, base=3, edges=True, rivets=()):
    idx = np.full((h, w), base)
    for _ in range(max(1, w * h // 9)):                       # blotches of one tone, a few texels each
        x, y = int(rng.integers(0, w)), int(rng.integers(0, h)); t = base + int(rng.choice([-2, -1, -1, 1]))
        idx[y:y + int(rng.integers(1, 3)), x:x + int(rng.integers(1, 4))] = t
    if edges and w >= 3 and h >= 3:
        idx[:, 0] = np.minimum(idx[:, 0] + 2, 5); idx[0, :] = np.minimum(idx[0, :] + 2, 5)         # lit left and top
        idx[:, -1] = np.maximum(idx[:, -1] - 2, 0); idx[-1, :] = np.maximum(idx[-1, :] - 2, 0)     # shaded right and foot
    out = BRASS[np.clip(idx, 0, 5)]
    for x, y in rivets:                                       # a rivet: a bright texel with its shadow down and to the right
        if 0 <= x < w - 1 and 0 <= y < h - 1:
            out[y, x] = BRASS[6]; out[y, x + 1] = BRASS[4]; out[y + 1, x] = BRASS[4]; out[y + 1, x + 1] = BRASS_LINE
    return out


def felt(w, h, crease=True):
    idx = np.full((h, w), 2)
    for _ in range(max(1, w * h // 14)):                      # the nap lying two ways: small patches a tone up or down
        x, y = int(rng.integers(0, w)), int(rng.integers(0, h)); t = 2 + int(rng.choice([-1, 1, 1]))
        idx[y:y + int(rng.integers(1, 3)), x:x + int(rng.integers(2, 5))] = t
    if crease:                                                # darker where two faces of the lining meet
        idx[-1, :] = 0; idx[:, 0] = np.minimum(idx[:, 0], 1); idx[:, -1] = np.minimum(idx[:, -1], 1)
        if h > 4: idx[-2, :] = np.minimum(idx[-2, :], 1)
    return FELT[np.clip(idx, 0, 4)]


HX, HZ, BODY, WALL = S['half_x'], S['half_z'], S['body'], S['wall']
STRAP_U = [(HX - xb, HX - xa) for xa, xb in S['straps']]     # strap columns on every face whose u runs 32 - x (or x + 32: they are symmetric)
SPLITS = (7, 14)                                              # the body's three boards, the same rows on all four sides


def strap_shadow(img, v0=0, v1=None):
    """The one-texel shadow each strap leaves on the wood to its right."""
    h, w = img.shape[:2]; m = np.zeros((h, w), bool)
    for a, b in STRAP_U:
        if b < w: m[v0:v1, b] = True
    darker(img, m, 2)


def body_side(w, front=False, stiles=False):
    img = boards(w, BODY, SPLITS)
    if stiles:                                                # an upright corner board at each end, its grain running up
        for x0 in (0, w - 4):
            img[:, x0:x0 + 4] = boards(4, BODY, (), butt=False, along='y')
        img[:, 4] = LINE; img[:, w - 5] = LINE
    else:
        m = np.zeros(img.shape[:2], bool); m[:, 0] = m[:, -1] = True; darker(img, m, 1)
    img[0, :] = LINE                                          # the gap under the lid
    m = np.zeros(img.shape[:2], bool); m[-2:, :] = True; darker(img, m, 1)   # grime along the foot
    if w == 2 * HX: strap_shadow(img)
    if front:                                                 # the lock plate's shadow: to its right and under it
        m = np.zeros(img.shape[:2], bool); m[2:17, 38] = True; m[15, 27:39] = True; darker(img, m, 2)
    return img


def ring(w, h, inner):
    """A rim seen from above: wood outside, the lining folded over its inner texel."""
    img = np.empty((h, w, 3)); img[:] = WOOD[3]              # kept dark: lit from straight above, a pale rim reads as more felt
    img[::3, :] = WOOD[2]; img[:, ::5] = WOOD[4]
    img[1:h - 1, 1:w - 1] = FELT[3]
    img[inner:h - inner, inner:w - inner] = FELT[1]
    return img


def lid_out(w, h):
    join = S['lid_join']; a0, a1 = int(round(join)), int(round(S['lid_len'] - join))
    n = 6; step = (a1 - a0) / n
    splits = [a0] + [int(round(a0 + step * i)) for i in range(1, n)] + [a1]
    img = boards(w, h, splits)
    strap_shadow(img)
    m = np.zeros((h, w), bool); m[a1 + 1:a1 + 5, 36] = True; darker(img, m, 2)     # the hasp plate's shadow on the front rail
    return img


def lid_end(w, h, mirror):
    """The lid's end: boards with their grain level, running out past the arch. No line is drawn along the arch: the
    arch cuts through those texels, and a dark stepped line there read as a jagged outline against the dark room."""
    img = boards(w, h, (4, 9), butt=False)
    m = np.zeros((h, w), bool); m[:, 0] = m[:, -1] = True; darker(img, m, 1)
    return img[:, ::-1] if mirror else img


def strap(w, h):
    back, lid = BODY, S['strap_len']
    riv = []
    for y in list(range(3, int(back) - 2, 8)) + list(range(int(back) + 3, int(back + lid) - 3, 9)) + list(range(int(back + lid) + 4, h - 3, 8)):
        riv.append((2, y))
    img = brass(w, h, rivets=riv)
    for y in (int(back) - 1, int(back), int(round(back + lid)) - 1, int(round(back + lid))):    # the two seams it crosses
        if 0 <= y < h: img[y, :] = BRASS_LINE if y in (int(back), int(round(back + lid)) - 1) else BRASS[1]
    img[0, :] = BRASS[1]; img[-1, :] = BRASS[1]
    return img


def lock_plate(w, h):
    img = brass(w, h, rivets=[(1, 1), (w - 3, 1), (1, h - 3), (w - 3, h - 3)])
    hole = np.zeros((h, w), bool)
    hole[6, 5:7] = True; hole[7:9, 4:8] = True; hole[9, 5:7] = True      # the round part
    hole[10:13, 5:7] = True                                               # the slot under it
    for y, x in zip(*np.nonzero(hole)):                                   # a bright lip on the hole's lower right, lit from the upper left
        for dy, dx in ((0, 1), (1, 0)):
            yy, xx = y + dy, x + dx
            if yy < h and xx < w and not hole[yy, xx]: img[yy, xx] = BRASS[6]
        for dy, dx in ((0, -1), (-1, 0)):
            yy, xx = y + dy, x + dx
            if yy >= 0 and xx >= 0 and not hole[yy, xx]: img[yy, xx] = BRASS[1]
    img[hole] = HOLE
    return img


def tongue(w, h):
    img = brass(w, h, base=4)
    img[1:h - 2, w // 2] = BRASS[2]                                       # the fold down its middle
    img[h - 3, w // 2] = BRASS_LINE; img[h - 3, w // 2 - 1] = BRASS[6]    # the staple it closes over
    return img


def plain(w, h, colour): img = np.empty((h, w, 3)); img[:] = colour; return img


def paint(name, w, h):
    if name == 'body front': return body_side(w, front=True, stiles=True)
    if name == 'body back': return body_side(w, stiles=True)
    if name in ('body left', 'body right'): return body_side(w)
    if name == 'body under': return boards(w, h, (12, 25), butt=False) * .8
    if name in ('body rim', 'lid rim'): return ring(w, h, WALL)
    if name == 'lid out': return lid_out(w, h)
    if name == 'lid end right': return lid_end(w, h, False)
    if name == 'lid end left': return lid_end(w, h, True)
    if name.startswith('lining') or name.startswith('lid lining'): return felt(w, h, crease='floor' not in name and name != 'lid lining')
    if name.startswith('strap edge'): return plain(w, h, BRASS[1])
    if name.startswith('strap'): return strap(w, h)
    if name == 'lock plate': return lock_plate(w, h)
    if name == 'hasp plate': return brass(w, h, rivets=[(1, 1), (w - 3, 1)])
    if name == 'hasp tongue': return tongue(w, h)
    if name == 'plate edge': return plain(w, h, BRASS[1])
    if name == 'hinge':                                                   # round its bar: knuckle lines across it, lit along its top
        img = brass(w, h, base=2, edges=False); img[2, :] = BRASS_LINE; img[4, :] = BRASS_LINE; return img
    if name == 'hinge cap': return plain(w, h, BRASS[1])
    if name == 'foot':
        img = boards(w, h, (), butt=False); img[0, :] = LINE; return img
    if name == 'foot under': return plain(w, h, WOOD[0])
    raise SystemExit('no painter for ' + name)


W, H = lay['width'], lay['height']
tex = np.zeros((H, W, 3)); done = np.zeros((H, W), bool)
for name, (x, y, w, h, _, _) in lay['panels'].items():
    img = np.clip(paint(name, w, h), 0, 255)
    if name == 'lining floor':                                            # the floor's creases run round all four sides
        m = np.zeros((h, w), bool); m[0, :] = m[-1, :] = m[:, 0] = m[:, -1] = True; img[m] = FELT[0]
    if name == 'lid lining':                                              # its two long edges meet the lid's end linings
        img[:, 0] = FELT[1]; img[:, -1] = FELT[1]
    tex[y:y + h, x:x + w] = img; done[y:y + h, x:x + w] = True
    # one texel of the panel's own edge all round it, so a face's border never samples its neighbour
    tex[y - 1, x:x + w] = img[0]; tex[y + h, x:x + w] = img[-1]; tex[y:y + h, x - 1] = img[:, 0]; tex[y:y + h, x + w] = img[:, -1]
Image.fromarray(tex.astype(np.uint8)).save(os.path.join(folder, 'Dice box.png'))
print('painted', W, H)

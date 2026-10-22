"""Body armour icons, third pass: the turned 3D form of armour2.py, built as layered plates.

Every plate is a region with an order (which lies on top), a bevel (its edge rolls over: lit on the upper left, in
shade lower right) and a short cast shadow on whatever lies under it. Metal gets a sharp hot spot, a broad sheen and a
cool reflected rim on its shadow side; leather stays matte. Details (rivets, studs, stitching, lacing, trim) are laid
out on the armour's own surface so they turn with it.
"""
import sys, math
import numpy as np
from PIL import Image
from scipy import ndimage
from armour2 import Scene, RAMP, vnoise, h, W, H, X, Y, ROT, PITCH, LIGHT

RAMP = dict(RAMP)
RAMP['steel'] = np.array([[int(c[i:i + 2], 16) / 255 for i in (1, 3, 5)] for c in
                          ('#08090e', '#12141c', '#1e212b', '#2d313b', '#40444d', '#575b62', '#727478', '#929290', '#b9b5ac', '#e6e0d3')])
RAMP['black'] = np.array([[int(c[i:i + 2], 16) / 255 for i in (1, 3, 5)] for c in
                          ('#050508', '#0b0c11', '#121319', '#1a1b22', '#24252c', '#303036', '#403f42', '#58544f', '#7a7166', '#a8998a')])
METAL = ('steel', 'black', 'brass', 'gold', 'mail')


def limb(sc, part, s0, v0, R, dx, dv, r0, r1):
    """An upper arm: a tapered tube from the shoulder (angle s0 round the body, height v0, distance R from the axis)
    out and down by (dx, dv); round in section, its end rounded off. Texels get t (0 shoulder .. 1 end) as their v."""
    a = s0 + ROT
    x0, z0 = R * math.sin(a), R * math.cos(a); y0 = v0 + PITCH * z0
    x1, y1 = x0 + dx, y0 + dv
    ex, ey = x1 - x0, y1 - y0; L2 = ex * ex + ey * ey
    traw = ((X - x0) * ex + (Y - y0) * ey) / L2
    t = np.clip(traw, 0, 1)
    px, py = x0 + t * ex, y0 + t * ey
    ox, oy = X - px, Y - py
    # distance across the arm (perpendicular to it), for a square-cut end
    L = math.sqrt(L2); ux, uy = ex / L, ey / L
    across = np.abs(-(X - x0) * uy + (Y - y0) * ux)
    r = r0 + (r1 - r0) * t
    m = (across < r) & (traw >= 0) & (traw <= 1)
    m |= (np.sqrt(ox * ox + oy * oy) < r) & (traw < 0)                 # rounded only where it meets the shoulder
    cross = np.clip(across / np.maximum(r, 1e-6), 0, 1)
    dz = np.sqrt(np.clip(1 - cross ** 2, 0, None)) * r
    side = np.sign(-(X - x0) * uy + (Y - y0) * ux)
    n = np.dstack([-uy * side * cross, ux * side * cross, np.sqrt(np.clip(1 - cross ** 2, 0, None))])
    n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
    sc.put(m, z0 + dz - .02, part, np.arcsin(np.clip(-side * cross, -1, 1)), t, n)
    # the opening at the end: seen a little from above, a dark half-ellipse just past the cut
    beyond = (traw - 1) * L
    hole = (traw > 1) & (across < r1 * .92) & (beyond < r1 * .38 * np.sqrt(np.clip(1 - (across / r1) ** 2, 0, None)))
    sc.put(hole, np.full((H, W), z0 - .01), 'sleeve_in', np.zeros((H, W)), np.ones((H, W)), np.dstack([np.zeros((H, W)), np.zeros((H, W)), np.ones((H, W))]))
    return m

class Armour:
    def __init__(self, sc):
        self.sc = sc
        self.order = np.full((H, W), -1.0)          # which plate lies on top here
        self.mat = np.full((H, W), '', object)
        self.bump = np.zeros((H, W))
        self.extra = np.zeros((H, W))                # value nudges (stains, crevices)
        self.shine = np.zeros((H, W))
        self.regions = []

    def plate(self, mask, material, order, bevel=2.0, lift=1.0, shadow=2, shine=1.0, extra=0.0):
        m = mask & (order >= self.order)
        self.order[m] = order; self.mat[m] = material; self.shine[m] = shine
        self.extra[m] = (extra if np.ndim(extra) == 0 else extra[m]) if True else 0
        self.regions.append((m, order, bevel, lift, shadow))
        return m

    def build(self):
        """Bevels and cast shadows from the final layering."""
        for m, order, bevel, lift, shadow in self.regions:
            top = m & (self.order == order)
            if not top.any(): continue
            if bevel > 0:
                d = ndimage.distance_transform_edt(np.pad(top, 1))[1:-1, 1:-1]
                self.bump[top] += (np.minimum(d, bevel) / bevel)[top] * lift
            for k in range(1, shadow + 1):
                sh = np.zeros_like(top); sh[k:, k // 2:] = top[:-k, :W - k // 2] if k // 2 else top[:-k, :]
                under = sh & (self.order < order) & (self.order >= 0)
                self.extra[under] -= .16 / k

    def light(self, ambient=.12):
        n = self.sc.n.copy()
        gy, gx = np.gradient(self.bump)
        n[..., 0] -= gx * 1.6; n[..., 1] -= gy * 1.6
        n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
        d = (n * LIGHT).sum(2)
        diff = np.clip(d, 0, 1)
        R = 2 * d[..., None] * n - LIGHT
        hot = np.clip(R[..., 2], 0, 1) ** 40
        sheen = np.clip(R[..., 2], 0, 1) ** 8
        metal = np.isin(self.mat, METAL)
        val = ambient + diff * .85
        # polished metal mirrors its surroundings: bright sky above, a dark band at the horizon, the floor below
        V3 = np.array([0., 0., 1.]); dv = (n * V3).sum(2)
        Rv = 2 * dv[..., None] * n - V3
        up = -(Rv[..., 1] * .75 + Rv[..., 0] * .66)
        env = np.interp(up, [-1, -.45, -.2, -.08, .06, .25, 1], [.26, .32, .10, .16, .52, .84, .95])
        mix = np.clip(self.shine, 0, 1) * .55
        val = np.where(metal, val * (1 - mix) + env * mix, val)
        val += metal * self.shine * (hot * .55 + sheen * .12)
        val += np.isin(self.mat, ['leather', 'leather2']) * (sheen * .16 + hot * .05)
        # reflected light: a cool rim on a metal plate's shadow side, facing away right
        rim = metal & (n[..., 0] > .55) & (diff < .25)
        val[rim] += .10
        # fall-off down and right
        val -= (Y / 1.5) * .12 + X * .08
        val += self.extra
        self.n2, self.val = n, val

    def image(self):
        img = np.zeros((H, W, 4)); idx = np.zeros((H, W), int)
        for name, ramp in RAMP.items():
            m = self.mat == name
            if not m.any(): continue
            k = np.clip(np.floor(self.val * (len(ramp) - .001)).astype(int), 0, len(ramp) - 1)
            idx[m] = k[m]; img[m, :3] = ramp[k[m]]; img[m, 3] = 1
        self.idx = idx; self.img = img
        return img

    # ── details, after image() ──
    def px(self, x, y, col):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < W and 0 <= y < H and self.img[y, x, 3] > 0: self.img[y, x, :3] = col

    def at(self, s, v, R):
        a = s + ROT
        return (R * math.sin(a) + .5) * W - .5, (v + PITCH * R * math.cos(a)) * W - .5, math.cos(a)

    def rivet(self, x, y, ramp, size=2):
        r = RAMP[ramp]; n = len(r)
        x, y = int(round(x)), int(round(y))
        if not (1 <= x < W - 2 and 1 <= y < H - 2) or self.img[y, x, 3] == 0: return
        # as lit as the plate it sits in: a rivet in shadow is a dull glint, not a bright dot
        under = self.idx[y, x] / max(1, len(RAMP.get(self.mat[y, x], RAMP['steel'])) - 1)
        off = int(round((1 - under) * 4))
        for dy, dx in ((size, 1), (size, 0), (1, size), (size, size)):
            if self.img[y + dy, x + dx, 3] > 0: self.img[y + dy, x + dx, :3] *= .55
        c = lambda k: r[int(np.clip(k - off, 0, n - 1))]
        self.img[y, x, :3] = c(n - 1); self.img[y, x + 1, :3] = c(n - 3); self.img[y + 1, x, :3] = c(n - 4); self.img[y + 1, x + 1, :3] = c(n - 6)

    def outline(self):
        img = self.img; solid = img[..., 3] > 0
        ring = ndimage.binary_dilation(solid) & ~solid
        out = img.copy()
        for y, x in zip(*np.nonzero(ring)):
            for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
                yy, xx = y + dy, x + dx
                if 0 <= yy < H and 0 <= xx < W and solid[yy, xx]: break
            lit = (dx > 0 or dy > 0) and self.idx[yy, xx] >= 4
            r = RAMP.get(self.mat[yy, xx], RAMP['steel'])
            out[y, x, :3] = r[1] if lit else (.025, .02, .025); out[y, x, 3] = 1
        self.img = out
        return out

def save(img, path):
    Image.fromarray((np.clip(img, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').resize((W * 2, H * 2), Image.NEAREST).save(path)


# ── plate ────────────────────────────────────────────────────────────────────
def plate(black):
    sc = Scene()
    main = 'black' if black else 'steel'; trim = 'gold' if black else 'brass'
    rad = lambda v: np.interp(v, [.10, .16, .26, .40, .56, .70, .78, .84, .96, 1.10, 1.24, 1.32],
                              [.15, .25, .295, .305, .27, .225, .21, .225, .255, .285, .30, .305])
    neck = lambda s: .11 + .06 * np.cos(np.clip(s, -1.5, 1.5)) ** 2 + .09 * np.clip(np.abs(s) - .95, 0, 1)
    for side in (-1, 1):
        limb(sc, 'sleeve', side * 1.38, .27, .25, side * .10, .33 if side > 0 else .31, .088, .072)
    hem = lambda s: 1.28 + .022 * np.sin(s * 3.3 + 1) + .016 * np.cos(s * 8.5)
    sc.torso('body', rad, .10, 1.32, top=neck, bottom=hem)
    for side in (-1, 1):
        lames_n = 4 if black else 3
        for j in reversed(range(lames_n)):
            big = 1.12 if black else 1.0
            sc.sphere(f'paul{j}', side * (1.22 + j * .065), .228 + j * .058, (.272 + j * .012) * (1.03 if black else 1), (.168 - j * .013) * big,
                      cut_below=.048 - j * .006, squash=.5)
    s, v, part = sc.s, sc.v, sc.part
    body = part == 'body'
    A = Armour(sc)
    hammer = vnoise(s, v, .08, .04, 3) - .5
    # mail underneath: sleeves, the sides below the arms, the hem
    mail = (part == 'sleeve') | body
    A.plate(mail, 'mail', 0, bevel=0, shadow=0, shine=.3)
    A.plate(part == 'sleeve_in', 'mail', 0, bevel=0, shadow=0, extra=-.55)
    A.extra[(part == 'sleeve') & (v > .9 + .05 * np.sin(s * 9))] -= .12
    # faulds: three lames, the lowest first so the one above lies over it; they flare with the hips
    sag = lambda s: .03 * np.cos(np.clip(s, -1.6, 1.6)) ** 2
    for k in reversed(range(3)):
        y0 = .80 + k * .1
        lame = body & (v >= y0 + sag(s)) & (v < y0 + .12 + sag(s)) & (np.abs(s) < 1.55 - k * .02)
        A.plate(lame, main, 1 + (2 - k), bevel=2.2, lift=1.4, shine=.7 if not black else .5, extra=hammer * .05)
    # cuirass: the breast, in to the waist; a raised keel down the middle
    cuir = body & (v >= neck(s) + .035) & (v < .82 + sag(s)) & (np.abs(s) < 1.30)
    A.plate(cuir, main, 5, bevel=2.5, lift=1.6, shine=1.0 if not black else .6, extra=hammer * .05)
    # plackart: a pointed plate over the belly, riveted on
    plack = cuir & (v > .55 + .2 * np.abs(s) ** 1.25) & (np.abs(s) < 1.05)
    A.plate(plack, main, 6, bevel=2, lift=1.4, shine=1.0 if not black else .6, extra=hammer * .05 + .02)
    # gorget
    gor = body & (v < neck(s) + .05)
    A.plate(gor, main, 5.5, bevel=1.5, lift=1.0, shine=.8)
    # pauldrons
    for j in range(4):
        A.plate(part == f'paul{j}', main, 8 + (3 - j), bevel=1.6, lift=1.0, shine=1.0 if not black else .6)
    # trim: a rolled edge round the neck and arm openings (brass on the bright plate, gold on the black)
    tw = .028
    trims = (cuir & (v < neck(s) + .035 + tw * 1.4)) | (cuir & (np.abs(s) > 1.30 - tw * 2.2))
    if black:
        trims |= plack & (v < .55 + .2 * np.abs(s) ** 1.25 + tw * 1.3)
        for k in range(3):
            y0 = .80 + k * .1
            trims |= body & (v >= y0 + .12 + sag(s) - tw * .6) & (v < y0 + .12 + sag(s)) & (A.order == 1 + (2 - k))
        trims |= (part == 'paul0') & (v > .235 + .028)
    A.plate(trims & (A.order >= 1), trim, 12, bevel=1.2, lift=.9, shadow=1, shine=1.2)
    # surface bumps on the armour itself
    keel = np.clip(1 - np.abs(s + .0) / .07, 0, 1) * cuir * (v > .2)
    A.bump += keel * 1.4
    A.bump += 5.0 * np.exp(-((s / .8) ** 2 + ((v - .40) / .22) ** 2)) * cuir
    if black:
        for f in (.28, .52, .76):
            for sg in (-1, 1):
                line = sg * f * (.75 + (v - .2) * .55)
                A.bump += .7 * np.clip(1 - np.abs(s - line) / .04, 0, 1) * cuir * (v > .2) * (v < .74) * ~trims
    if not black:
        rope_v = neck(s) + .075
        rope = cuir & (np.abs(v - rope_v) < .02) & (np.abs(s) < 1.2)
        A.plate(rope, main, 6.5, bevel=1.2, lift=1.4, shadow=2, shine=1.0)
        twist = ((s * 14 + (v - rope_v) * 60) % 1)
        A.bump += rope * 1.2 * (twist < .5)
    A.bump -= .8 * np.exp(-(((s + .38) / .09) ** 2 + ((v - .46) / .04) ** 2)) * cuir          # dent
    A.bump += hammer * (.06 if black else .12) * (A.order >= 1)
    # mail rings
    yi, xi = Y.astype(int) * 0 + np.arange(H)[:, None], np.arange(W)[None, :] + np.zeros((H, 1), int)
    cx = (xi + (yi // 2) % 2) % 2; cy = yi % 2
    mm = A.mat == 'mail'
    A.extra[mm & (cx == 0) & (cy == 0)] += .12          # the lit top of each ring
    A.extra[mm & (cx == 1) & (cy == 1)] -= .14          # the gap
    A.extra[mm & (cx == 1) & (cy == 0)] += .02
    A.build(); A.light(); A.image()
    rng = np.random.default_rng(11 if black else 4)
    # rivets: plackart, lame ends, pauldron rows; one gone on the bright plate
    pts = [(sv, .80 + .03 * math.cos(sv) ** 2 - .025, float(rad(.78))) for sv in (-.75, -.25, .25, .75)]
    pts += [(sg * (1.4 - k * .02), .80 + k * .1 + .06, float(rad(.86 + k * .1))) for k in range(3) for sg in (-1, 1)]
    pts += [(sv, .58 + .2 * abs(sv) ** 1.25 + .03, float(rad(.62))) for sv in (-.45, .45)]
    for i, (sv, vv, R) in enumerate(pts):
        if i == 2 and not black: continue
        x, y, c = A.at(sv, vv, R + .01)
        if c < .25: continue
        A.rivet(x, y, trim)
        if not black and rng.random() < .2:
            for k in range(2, 2 + rng.integers(2, 4)):
                if int(y) + k < H and A.mat[int(y) + k, int(round(x))] == 'steel': A.px(x, y + k, RAMP['rust'][max(0, 2 - k // 2)])
    for side in (-1, 1):
        for t in (-.45, 0, .45):
            a = side * 1.22 + ROT; R = .272
            x = (R * math.sin(a) + .5 + t * .12) * W; y = (.255 + PITCH * R * math.cos(a)) * W + abs(t) * 2.2
            if side < 0 and t > .3: continue
            A.rivet(x, y, trim)
    # scratches: short, slanted, on lit plate only
    cand = np.argwhere(np.isin(A.mat, ['steel', 'black']) & (A.idx >= 4) & (A.order >= 1) & (A.order < 12))
    for _ in range(10 if black else 16):
        y, x = cand[rng.integers(len(cand))]; dx = rng.choice([1, -1])
        for k in range(rng.integers(2, 4)):
            yy, xx = y + (k + 1) // 2, x + dx * k
            if A.mat[yy, xx] in ('steel', 'black'):
                r = RAMP[A.mat[yy, xx]]; A.img[yy, xx, :3] = r[min(A.idx[yy, xx] + 2, len(r) - 1)]
    if black:
        # an etched sigil on the breast: a ring and cross in gold, laid on the plate
        for t in np.linspace(0, 2 * math.pi, 44, endpoint=False):
            x, y, c = A.at(-.12 + .16 * math.cos(t), .36 + .052 * math.sin(t), .307)
            A.px(x, y, RAMP['gold'][6 if math.cos(t - 2.4) > 0 else 3])
        for d in np.linspace(-1, 1, 11):
            x, y, c = A.at(-.12 + d * .12, .36, .307); A.px(x, y, RAMP['gold'][6 if d < 0 else 4])
            x, y, c = A.at(-.12, .36 + d * .04, .307); A.px(x, y, RAMP['gold'][6 if d < 0 else 4])
        # blacking worn back to bright steel on the sharpest raised edges
        edge = (A.mat == 'black') & (A.idx >= 7) & (A.order >= 5)
        for y, x in zip(*np.nonzero(edge)):
            pass
    else:
        # rust in a few clumps where water sits: under rivets and along the lowest lame's lower edge
        low = np.argwhere((A.mat == 'steel') & (sc.v > 1.0) & (A.idx <= 4) & (A.order >= 1))
        for c in range(4):
            y, x = low[rng.integers(len(low))]
            for dy in range(-1, 2):
                for dx in range(-2, 3):
                    if abs(dx) + abs(dy) * 2 <= 2 and h(x + dx, y + dy, c) < .7 and A.mat[y + dy, x + dx] == 'steel':
                        A.img[y + dy, x + dx, :3] = RAMP['rust'][2 if dy < 0 or dx < 0 else 1]
    A.outline()
    return A.img


# ── leather ──────────────────────────────────────────────────────────────────
def leather():
    sc = Scene()
    rad = lambda v: np.interp(v, [.10, .16, .26, .42, .58, .72, .78, .86, 1.0, 1.16, 1.34],
                              [.15, .24, .28, .29, .26, .225, .215, .235, .27, .295, .31])
    neck = lambda s: .12 + .07 * np.cos(np.clip(s, -1.5, 1.5)) ** 2 + .09 * np.clip(np.abs(s) - .95, 0, 1)
    for side in (-1, 1):
        limb(sc, 'sleeve', side * 1.36, .26, .24, side * .095, .29 if side > 0 else .27, .084, .07)
    hem = lambda s: 1.30 + .02 * np.sin(s * 4.3 + .5) + .016 * np.sin(s * 11)
    sc.torso('body', rad, .10, 1.34, top=neck, bottom=hem)
    for side in (-1, 1):
        for j in reversed(range(2)):
            sc.sphere(f'cap{j}', side * (1.2 + j * .08), .235 + j * .07, .262 + j * .01, .155 - j * .016,
                      cut_below=.042, squash=.5)
    s, v, part = sc.s, sc.v, sc.part
    body = part == 'body'
    A = Armour(sc)
    stain = (vnoise(s, v, .32, .18, 9) - .5) * .55
    grain = vnoise(s, v, .045, .025, 2) - .5
    A.plate(part == 'sleeve', 'cloth', 0, bevel=0, shadow=0, extra=.02)
    A.plate(part == 'sleeve_in', 'cloth', 0, bevel=0, shadow=0, extra=-.5)
    cuff = (part == 'sleeve') & (v > .78)
    A.plate(cuff, 'leather2', .5, bevel=1.4, lift=1.0, shadow=1, extra=.0)
    A.plate(part == 'inside', 'leather2', 0, bevel=0, shadow=0, extra=-.42)
    belt_y = lambda s: .75 + .02 * np.cos(np.clip(s, -1.6, 1.6)) ** 2
    # side panels (the back piece wrapping round), the two front panels split by the laced opening
    A.plate(body & (v < belt_y(s)), 'leather2', 1, bevel=1.5, lift=.6, extra=stain * .12 - .07)
    for sg in (-1, 1):
        front = body & (v >= neck(s) + .02) & (v < belt_y(s)) & (sg * s > .05) & (sg * s < 1.0)
        A.plate(front, 'leather', 2, bevel=2.5, lift=1.6, extra=stain * .14 + .07)
    A.plate(body & (v < neck(s) + .04), 'leather2', 3, bevel=1.5, lift=1.0, extra=.04)
    # tassets below the belt, each overlapping the next a little (the middle ones on top)
    for i, c in sorted(enumerate(np.arange(-1.35, 1.36, .45)), key=lambda t: -abs(t[1])):
        t = body & (v >= belt_y(s)) & (np.abs(s - c) < .27)
        A.plate(t, 'leather' if i % 2 else 'leather2', 4 + (2 - abs(c)), bevel=2.6, lift=1.9, shadow=3, extra=stain * .16 + (.03 if i % 2 else 0))
    belt = body & (v >= belt_y(s) - .005) & (v < belt_y(s) + .065)
    A.plate(belt, 'leather2', 8, bevel=1.4, lift=1.0, extra=-.02)
    # a strap from the right shoulder down across the chest to the left hip (it holds nothing here; it breaks the front)
    strap_c = .20 + (s - .95) * -.30
    strap = body & (np.abs(v - strap_c) < .028) & (v > neck(s) + .02) & (v < belt_y(s))
    A.plate(strap, 'leather2', 7, bevel=1.5, lift=1.6, shadow=3, extra=-.12)
    for j in range(2):
        A.plate(part == f'cap{j}', 'leather' if j == 0 else 'leather2', 9 + (1 - j), bevel=1.4, lift=1.5, shadow=3, extra=stain * .1 + .02)
    A.bump += .6 * np.exp(-(((np.abs(s) - .45) / .3) ** 2 + ((v - .40) / .13) ** 2)) * (A.order == 2)      # moulded chest
    A.bump += grain * .35 * (A.order >= 1)
    A.build(); A.light(ambient=.16); A.image()
    rng = np.random.default_rng(21)
    rad1 = lambda vv: float(rad(vv)) + .006
    # lacing across the opening: crossed thongs and brass eyelets
    for vv in np.arange(.24, .70, .052):
        for a0, a1 in ((-.13, .13), (.13, -.13)):
            x0, y0, c0 = A.at(a0, vv, rad1(vv)); x1, y1, c1 = A.at(a1, vv + .035, rad1(vv))
            for t in np.linspace(0, 1, 9):
                A.px(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, RAMP['leather'][8 if t < .5 else 6])
        for sv in (-.13, .13):
            x, y, c = A.at(sv, vv, rad1(vv)); A.px(x, y, RAMP['brass'][7]); A.px(x, y + 1, RAMP['brass'][3])
    # stitching just inside each front panel's outer edge and along the belt
    for sg in (-1, 1):
        for k, vv in enumerate(np.arange(.25, .735, .026)):
            x, y, c = A.at(sg * .93, vv, rad1(vv))
            if c > .2: A.px(x, y, RAMP['leather'][8] if k % 2 == 0 else RAMP['leather'][1])
    for k, sv in enumerate(np.arange(-1.5, 1.5, .07)):
        x, y, c = A.at(sv, float(belt_y(sv)) + .012, rad1(.76))
        if c > .2 and k % 2 == 0: A.px(x, y, RAMP['leather2'][6])
    # studs in rows on the side panels and the shoulder caps; a couple gone
    for sv in (-1.12, 1.12):
        for vv in np.arange(.33, .72, .065):
            x, y, c = A.at(sv, vv, rad1(vv))
            if c < .3: continue
            if rng.random() < .1: A.px(x, y, RAMP['leather'][0]); continue
            A.rivet(x, y, 'brass')
    for side in (-1, 1):
        for t in (-.5, -.17, .17, .5):
            a = side * 1.2 + ROT; R = .262
            x = (R * math.sin(a) + .5 + t * .11) * W; y = (.258 + PITCH * R * math.cos(a)) * W + abs(t) * 2.5
            if side < 0 and t > .3: continue
            A.rivet(x, y, 'brass')
    for sv in np.arange(-.7, .95, .28):
        vv = .20 + (sv - .95) * -.30
        x, y, c = A.at(sv, vv, rad1(vv) + .004)
        if c > .3: A.rivet(x, y, 'brass')
    # buckle, off centre, with the strap's tongue through it and the loose end hanging
    bx, by, _ = A.at(-.3, float(belt_y(-.3)) + .03, rad1(.77) + .01)
    bx, by = int(round(bx)), int(round(by))
    br = RAMP['brass']
    for dx in range(-3, 5):
        for dy in range(-3, 6):
            if A.img[by + dy, bx + dx, 3] > 0 and (dx == 4 or dy == 5): A.img[by + dy, bx + dx, :3] *= .55
    for dx in range(-3, 4):
        for dy in range(-4, 5):
            if abs(dx) == 3 or abs(dy) == 4:
                k = 7 if (dx == -3 or dy == -4) else 3
                if (dx == -3 and dy == -4): k = 8
                if (dx == 3 and dy == 4): k = 2
                A.img[by + dy, bx + dx, :3] = br[k]
            else:
                # the strap seen through the frame, darker where the frame shades it (top and left)
                A.img[by + dy, bx + dx, :3] = RAMP['leather2'][2 if (dx == -2 or dy == -3) else 4]
    for dy in range(-3, 4): A.img[by + dy, bx, :3] = br[6 if dy < 0 else 4]
    A.img[by - 1:by + 2, bx + 1, :3] = RAMP['leather2'][1]
    for k in range(1, 10):
        yy = by + 2 + k // 2
        for w in range(3):
            if A.img[yy + w - 1, bx - 4 - k, 3] > 0 and w != 2: A.img[yy + w - 1, bx - 4 - k, :3] = RAMP['leather2'][5 - w * 2 - (k > 6)]
    # stitching one texel in from each cap's lower edge
    for j in range(2):
        cap = sc.part == f'cap{j}'
        lower = cap & ~np.roll(cap, -2, 0)
        for k, (y, x) in enumerate(sorted(zip(*np.nonzero(lower)), key=lambda p: p[1])):
            if A.img[y, x, 3] > 0 and k % 2 == 0 and A.mat[y, x] in ('leather', 'leather2'):
                r = RAMP[A.mat[y, x]]; A.img[y, x, :3] = r[min(A.idx[y, x] + 3, len(r) - 1)]
    # scuffs and pores
    for y, x in zip(*np.nonzero(np.isin(A.mat, ['leather', 'leather2']))):
        q = h(x, y, 5)
        if q < .025: A.img[y, x, :3] *= .8
        elif q > .985 and A.idx[y, x] >= 5: A.img[y, x, :3] = np.minimum(A.img[y, x, :3] * 1.15, 1)
    A.outline()
    return A.img


if __name__ == '__main__':
    out = sys.argv[1]
    save(leather(), f'{out}/Leather.png')
    save(plate(False), f'{out}/Plate.png')
    save(plate(True), f'{out}/Black plate.png')
    print('ok')

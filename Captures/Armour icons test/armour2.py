"""Body armour icons, second approach: a small 3D form, painted by pixel-art rules.

The armour is a torso (a body of revolution with its own radius per height), shoulder caps (spheres), sleeves (tubes),
turned about a quarter toward the viewer and seen a little from above, so the far side narrows, bands curve round the
body and the neck opening shows the dark inside. Every texel knows where it is on the armour (its angle round the body
and its height), so seams, plates, studs and wear are laid out on the armour itself, not on the screen. Light comes
from the upper left; shades come from hand-picked ramps whose shadows lean cool and highlights warm. Wear is uneven on
purpose: dents, rust runs, a missing rivet, scuffed edges, a torn hem.
"""
import sys, math
import numpy as np
from PIL import Image
from scipy import ndimage

W, H = 88, 132
ROT = .42           # turned toward the viewer's left: we see more of the armour's right side
PITCH = .16         # seen from a little above: nearer points sit lower on screen
LIGHT = np.array([-.62, -.58, .52]); LIGHT /= np.linalg.norm(LIGHT)

def hx(*cs): return np.array([[int(c[i:i + 2], 16) / 255 for i in (1, 3, 5)] for c in cs])
RAMP = {
    'steel': hx('#0d0e15', '#191b25', '#282c38', '#3c414c', '#555a63', '#72767b', '#959592', '#bdb8ae', '#e2dccf'),
    'black': hx('#060609', '#0e0f15', '#17181f', '#22232a', '#2f3036', '#403f42', '#5a5652', '#7d756b', '#a99d8b'),
    'leather': hx('#120809', '#22100d', '#361a12', '#4c2518', '#653320', '#7f442a', '#9c5a36', '#bc7a4c', '#d49e6c'),
    'leather2': hx('#0e0708', '#1a0d0b', '#29150f', '#3a1e15', '#4e2a1c', '#653726', '#7e4831', '#9a603f'),
    'brass': hx('#1a1007', '#33200b', '#523510', '#744f17', '#987021', '#bb9131', '#d9b34e', '#efd486', '#fbf0c4'),
    'gold': hx('#1d1206', '#3b260a', '#5f3f0f', '#856017', '#ad8421', '#d0a832', '#e9c854', '#f8e38f', '#fff6d2'),
    'mail': hx('#0b0c11', '#16181f', '#23262e', '#33363d', '#474a50', '#5f6266', '#7b7c7d'),
    'cloth': hx('#11080b', '#1f0d10', '#311515', '#451e1b', '#5b2a23', '#73392d'),
    'rust': hx('#1a0b06', '#2f140a', '#4a200e', '#652e13', '#7f3f1a'),
    'inside': hx('#050406', '#0b0a0d', '#141217'),
}
METAL = ('steel', 'black', 'brass', 'gold', 'mail')

def h(*a):
    x = 1469598103934665603
    for v in a: x = ((x ^ (int(v) & 0xffffffff)) * 1099511628211) & 0xffffffffffffffff
    return (x % 100003) / 100003

def vnoise(s, v, scale_s, scale_v, seed):
    """Smooth value noise in armour coordinates (for stains, hammer marks, uneven colour)."""
    gs, gv = s / scale_s, v / scale_v
    i0, j0 = np.floor(gs).astype(int), np.floor(gv).astype(int)
    fs, fv = gs - i0, gv - j0
    fs, fv = fs * fs * (3 - 2 * fs), fv * fv * (3 - 2 * fv)
    hv = np.vectorize(lambda a, b: h(a, b, seed))
    c00, c10, c01, c11 = hv(i0, j0), hv(i0 + 1, j0), hv(i0, j0 + 1), hv(i0 + 1, j0 + 1)
    return (c00 * (1 - fs) + c10 * fs) * (1 - fv) + (c01 * (1 - fs) + c11 * fs) * fv

PY, PX = np.mgrid[0:H, 0:W].astype(float)
X = (PX + .5) / W - .5          # screen, centred, in armour widths
Y = (PY + .5) / W               # screen y, 0 at the top

class Scene:
    """A z-buffer of primitives; each texel ends up with: part name, angle s, height v, normal, depth."""
    def __init__(self):
        self.z = np.full((H, W), -9.0); self.part = np.full((H, W), '', object)
        self.s = np.zeros((H, W)); self.v = np.zeros((H, W)); self.n = np.zeros((H, W, 3)); self.r = np.zeros((H, W))

    def put(self, m, z, part, s, v, n, r=None):
        win = m & (z > self.z)
        self.z[win] = z[win]; self.part[win] = part; self.s[win] = s[win]; self.v[win] = v[win]; self.n[win] = n[win]
        if r is not None: self.r[win] = r[win]

    def torso(self, part, radius, v0, v1, top=None, bottom=None, cx=0.0, inside=True):
        """A body of revolution: radius(v) half-width; top(s)/bottom(s) its edges as heights by angle round it."""
        # solve for v (pitch makes it depend on depth): two passes
        v = Y.copy()
        for _ in range(3):
            r = radius(np.clip(v, v0, v1))
            a = np.arcsin(np.clip((X - cx) / np.maximum(r, 1e-4), -1, 1))
            z = r * np.cos(a)
            v = Y - PITCH * z
        inside_r = np.abs(X - cx) < r
        s = a - ROT
        m = inside_r & (v >= v0) & (v <= v1)
        if top is not None: m &= v >= top(s)
        if bottom is not None: m &= v <= bottom(s)
        # normal: round in x, tilted by how the radius changes with height
        dr = (radius(np.clip(v + .01, v0, v1)) - radius(np.clip(v - .01, v0, v1))) / .02
        n = np.dstack([np.sin(a), -dr * np.cos(a) * 1.0, np.cos(a)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
        self.put(m, z, part, s, v, n, r)
        if inside and top is not None:
            # the inside of the back, seen through the opening at the top
            vb = Y.copy()
            for _ in range(3):
                rb = radius(np.clip(vb, v0, v1)); ab = np.arcsin(np.clip((X - cx) / np.maximum(rb, 1e-4), -1, 1))
                zb = -rb * np.cos(ab); vb = Y - PITCH * zb
            sb = math.pi - (ab + ROT)
            mb = (np.abs(X - cx) < rb) & (vb >= v0) & (vb >= top(np.arctan2(np.sin(sb), np.cos(sb)))) & ~m & (vb <= v0 + .25)
            self.put(mb, zb, 'inside', sb, vb, np.dstack([np.zeros_like(X), np.zeros_like(X), np.ones_like(X)]))
        return m

    def sphere(self, part, s0, v0, R, rad, cut_below=None, squash=1.0):
        """A cap: centre on the body at angle s0, height v0, distance R from the axis; radius rad."""
        a = s0 + ROT
        cx3, cz3 = R * math.sin(a), R * math.cos(a)
        cy = v0 + PITCH * cz3
        dx, dy = X - cx3, (Y - cy) / squash
        d2 = dx * dx + dy * dy
        m = d2 < rad * rad
        dz = np.sqrt(np.clip(rad * rad - d2, 0, None))
        z = cz3 + dz
        vv = Y - PITCH * z
        if cut_below is not None: m &= vv < v0 + cut_below
        n = np.dstack([dx, dy * squash, dz]); n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
        # angle round the cap, from its outward direction (for lames and rivets)
        ss = np.arctan2(dx, dz) - a
        self.put(m, z + .02, part, ss, vv, n)
        return m

    def tube(self, part, s0, v0, v1, R, rad, lean=0.0):
        """A sleeve hanging from the shoulder: a vertical tube at angle s0, distance R from the axis."""
        a = s0 + ROT
        cz3 = R * math.cos(a)
        cx3 = R * math.sin(a) + lean * (Y - v0)
        vv = Y - PITCH * cz3
        dx = X - cx3
        m = (np.abs(dx) < rad) & (vv >= v0) & (vv <= v1)
        dz = np.sqrt(np.clip(rad * rad - dx * dx, 0, None))
        n = np.dstack([dx / rad, np.zeros_like(dx), dz / rad])
        self.put(m, cz3 + dz, part, np.arcsin(np.clip(dx / rad, -1, 1)), vv, n)
        return m


class Paint:
    def __init__(self, sc):
        self.sc = sc
        self.mat = np.full((H, W), '', object); self.val = np.zeros((H, W)); self.bump = np.zeros((H, W))
        self.fixed = -np.ones((H, W), int)       # texels set by hand: an index into their ramp

    def light(self, ambient=.18, shine=1.0):
        n = self.sc.n.copy()
        # bumps (ridges, seams, dents) tilt the normal in screen space
        gy, gx = np.gradient(ndimage.gaussian_filter(self.bump, .7))
        n[..., 0] -= gx * 2.2; n[..., 1] -= gy * 2.2
        n /= np.maximum(np.linalg.norm(n, axis=2, keepdims=True), 1e-6)
        d = (n * LIGHT).sum(2)
        diff = np.clip(d, 0, 1)
        R = 2 * d[..., None] * n - LIGHT
        spec = np.clip(R[..., 2], 0, 1) ** 16
        self.diff, self.spec = diff, spec
        self.L = ambient + diff * .9

    def fill(self, m, material, extra=0.0, shine=0.0):
        self.mat[m] = material
        v = self.L + (self.spec * shine if material in METAL else 0) + extra
        self.val[m] = (v if np.ndim(v) else np.full((H, W), v))[m]

    def image(self):
        self.val = np.where(np.isin(self.mat, ['steel', 'mail']), self.val * .84 - .02, self.val)
        img = np.zeros((H, W, 4))
        idx = np.zeros((H, W), int)
        for name, ramp in RAMP.items():
            m = self.mat == name
            if not m.any(): continue
            k = np.clip(np.floor(self.val * (len(ramp) - .001)).astype(int), 0, len(ramp) - 1)
            k = np.where(self.fixed >= 0, np.clip(self.fixed, 0, len(ramp) - 1), k)
            idx[m] = k[m]
            img[m, :3] = ramp[k[m]]; img[m, 3] = 1
        self.idx = idx
        return img

    def set(self, x, y, material, k):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < W and 0 <= y < H:
            self.mat[y, x] = material; self.fixed[y, x] = k

def outline(img, mat, idx):
    """A dark line round the whole armour; on the lit (upper-left) side it is the edge colour's dark shade, not black."""
    solid = img[..., 3] > 0
    ring = ndimage.binary_dilation(solid) & ~solid
    out = img.copy()
    for y, x in zip(*np.nonzero(ring)):
        # neighbour inside
        best = None
        for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
            yy, xx = y + dy, x + dx
            if 0 <= yy < H and 0 <= xx < W and solid[yy, xx]: best = (yy, xx, dy, dx); break
        yy, xx, dy, dx = best
        lit_side = dx > 0 or dy > 0          # the armour lies right of / below it: this is the top-left edge
        r = RAMP.get(mat[yy, xx], RAMP['steel'])
        c = r[1] if lit_side and idx[yy, xx] >= 3 else np.array([.03, .025, .03])
        out[y, x, :3] = c; out[y, x, 3] = 1
    # inner contour where a part meets a much darker one (crevice) is left to the shading
    return out

def project(s, v, R):
    """Screen texel of a point on the body at angle s, height v, radius R."""
    a = s + ROT
    x = R * math.sin(a); z = R * math.cos(a)
    return (x + .5) * W - .5, (v + PITCH * z) * W - .5, z

def visible(sc, s, v, part_prefix, tol=.12):
    x, y, z = project(s, v, 0)
    return True

# ── Armours ──────────────────────────────────────────────────────────────────

def plate(black):
    sc = Scene(); P = Paint(sc)
    main = 'black' if black else 'steel'; trim = 'gold' if black else 'brass'
    rad = lambda v: np.interp(v, [.10, .17, .30, .48, .66, .80, .86, 1.0, 1.15, 1.30],
                              [.17, .26, .295, .30, .255, .225, .235, .255, .27, .28])
    neck = lambda s: .115 + .055 * np.cos(np.clip(s, -1.5, 1.5)) ** 2
    # mail sleeves first (behind the caps), then the torso
    for side in (-1, 1):
        sc.tube('sleeve', side * 1.45, .22, .56 + (.03 if side > 0 else 0), .265, .085, lean=side * .05)
    sc.torso('body', rad, .10, 1.30, top=neck,
             bottom=lambda s: 1.27 + .025 * np.sin(s * 3.1 + 1) + .02 * np.cos(s * 7))
    # pauldrons: three lames a side, the top one largest; the far (left) one is half hidden behind the body
    for side in (-1, 1):
        for j in reversed(range(3)):
            sc.sphere(f'paul{j}', side * (1.22 + j * .07), .235 + j * .062, .275 + j * .01, .165 - j * .014,
                      cut_below=.045 - j * .006, squash=.5)
    s, v, part = sc.s, sc.v, sc.part
    body = part == 'body'
    # Regions on the body (by height and angle round it).
    plack_top = .56 + .16 * np.abs(s) ** 1.3                          # the plackart's pointed top
    faulds = body & (v > .84)
    lames = np.floor((v - .84 - .02 * np.cos(s) ** 2) / .105)
    mail_hem = body & (v > 1.155 + .02 * np.sin(s * 5 + 2))
    gorget = body & (v < neck(s) + .05)
    cuir = body & ~faulds & ~gorget & (np.abs(s) < 1.35)
    plack = cuir & (v > plack_top) & (v < .84)
    mail_side = body & ~faulds & ~gorget & ~cuir
    # Bumps: the keel down the middle, the chest swell, the lames' roll, a dent and hammer marks.
    keel = np.clip(1 - np.abs(s) / .09, 0, 1) * (v > .15) * (v < .84) * .9
    P.bump = keel.astype(float)
    if black:
        for f in (.30, .55, .80):
            for sg in (-1, 1):
                line = sg * (f * (.8 + (v - .2) * .5))
                P.bump += .55 * np.clip(1 - np.abs(s - line) / .035, 0, 1) * (v > .2) * (v < .8) * cuir
    roll = np.sin(np.clip((v - .84 - .02 * np.cos(s) ** 2) / .105 % 1, 0, 1) * math.pi) * faulds
    P.bump += roll * .6
    P.bump += (gorget * .4)
    P.bump -= .9 * np.exp(-(((s + .35) / .11) ** 2 + ((v - .47) / .05) ** 2)) * cuir          # a dent, left breast
    hammer = vnoise(s, v, .09, .045, 3) - .5
    P.bump += hammer * .08 * (body & ~mail_side & ~mail_hem)
    P.light()
    metal = (cuir | plack | faulds & ~mail_hem | gorget) & body
    P.fill(metal, main, extra=hammer * .03, shine=.9 if not black else .55)
    P.val[faulds & metal] -= P.spec[faulds & metal] * (.45 if not black else .25)
    # seams: a dark crevice under each overlapping edge (plackart top, each lame's top, gorget)
    seam = np.zeros((H, W), bool)
    for m in (plack, gorget):
        seam |= m & ~ndimage.binary_erosion(m)
    P.val[seam & metal] -= .22
    for k in range(4):
        e = faulds & (lames == k) & ~ndimage.binary_erosion(faulds & (lames == k), structure=np.array([[0, 1, 0], [0, 1, 0], [0, 0, 0]]))
        P.val[e & metal] -= .25
    # mail: rings from the armour's own coordinates, so they wrap round
    mail = mail_side | mail_hem | (part == 'sleeve')
    P.fill(mail, 'mail', shine=.3)
    rs, rv = np.floor(sc.s * 26 + (np.floor(sc.v * 44) % 2) * .5), np.floor(sc.v * 44)
    ringhi = ((rs + rv) % 2 == 0)
    P.val[mail & ringhi] += .09; P.val[mail & ~ringhi] -= .07
    hemedge = mail & (v > 1.24); P.val[hemedge] -= .05
    # pauldrons
    for j in range(3):
        m = part == f'paul{j}'
        P.fill(m, main, extra=-.02 * j, shine=.9 if not black else .6)
        e = m & ~ndimage.binary_erosion(m, structure=np.array([[0, 0, 0], [0, 1, 0], [0, 1, 0]]))
        P.val[e] -= .2
    img = None
    # trim: gilded or brass edging along the neck and arm openings (and the plackart on the black plate)
    tz = (gorget & (v > neck(s) + .035)) | (cuir & (np.abs(np.abs(s) - 1.3) < .05) & (v < .84))
    if black:
        tz |= plack & (v < plack_top + .025) | (faulds & (((v - .84 - .02 * np.cos(s) ** 2) % .105) > .088))
        tz |= (part == 'paul0') & (sc.v > .235 + .03)
    P.fill(tz & (P.mat != ''), trim, extra=.08, shine=1.0)
    # rivets: on the armour's own grid, a few missing, rust running down from some on the bright plate
    pts = []
    for sv in (-1.15, -.6, 0, .6, 1.15):
        pts.append((sv, .80))
    for k in range(3):
        for sv in (-1.0, 1.0): pts.append((sv * (1 + k * .04), .84 + k * .105 + .05))
    for side in (-1, 1):
        for t in (-.35, 0, .35): pts.append(('paul', side, t))
    rng = np.random.default_rng(11 if black else 4)
    img_marks = []
    for p in pts:
        if p[0] == 'paul':
            _, side, t = p
            a = side * 1.22 + ROT; cx3 = .275 * math.sin(a); cz3 = .275 * math.cos(a)
            x = (cx3 + .5 + t * .11) * W; y = (.245 + PITCH * cz3) * W + abs(t) * 2
            if side < 0 and t > .2: continue
        else:
            sv, vv = p
            if abs(sv + ROT) > 1.35: continue
            x, y, z = project(sv, vv, float(rad(vv)) + .005)
        img_marks.append((x, y))
    P.light()
    return sc, P, img_marks, trim, black, rng


def finish_plate(sc, P, marks, trim, black, rng):
    img = P.image()
    for i, (x, y) in enumerate(marks):
        if i == 3 and not black: continue                      # a rivet lost
        xi, yi = int(round(x)), int(round(y))
        if not (0 <= xi < W - 1 and 0 <= yi < H - 1) or P.mat[yi, xi] == '': continue
        r = RAMP[trim]
        img[yi, xi, :3] = r[7]; img[yi, xi + 1, :3] = r[5]; img[yi + 1, xi, :3] = r[4]; img[yi + 1, xi + 1, :3] = r[2]
        if yi + 2 < H and P.mat[yi + 2, xi] != '': img[yi + 2, xi + 1, :3] *= .6
        if not black and rng.random() < .3:                   # a rust run down from it
            for k in range(2, 2 + rng.integers(2, 5)):
                if yi + k < H and P.mat[yi + k, xi] in ('steel',):
                    img[yi + k, xi, :3] = RAMP['rust'][max(0, 2 - k // 2)]
    # scratches on the lit plates, short and slanted, never mirrored
    cand = np.argwhere(np.isin(P.mat, ['steel', 'black']) & (P.idx >= 3))
    for _ in range(14 if black else 22):
        y, x = cand[rng.integers(len(cand))]
        L = rng.integers(2, 5); dx = rng.choice([1, -1])
        for k in range(L):
            yy, xx = y + k // 2, x + dx * k
            if 0 <= yy < H and 0 <= xx < W and P.mat[yy, xx] in ('steel', 'black'):
                r = RAMP[P.mat[yy, xx]]; img[yy, xx, :3] = r[min(P.idx[yy, xx] + 2, len(r) - 1)]
    if black:
        # the blacking worn off on raised edges: bright steel showing through
        worn = np.isin(P.mat, ['black']) & (P.idx >= 6) & ~ndimage.binary_erosion(np.isin(P.mat, ['black']), iterations=2)
        for y, x in zip(*np.nonzero(worn)):
            if h(x, y, 77) < .3: img[y, x, :3] = RAMP['steel'][min(P.idx[y, x] + 1, 8)]
        # an etched cross in a ring on the chest, laid on the armour (so it turns with it)
        for t in np.linspace(0, 2 * math.pi, 40, endpoint=False):
            x, y, z = project(-.1 + .17 * math.cos(t), .36 + .055 * math.sin(t), .305)
            xi, yi = int(round(x)), int(round(y))
            if P.mat[yi, xi] == 'black': img[yi, xi, :3] = RAMP['gold'][6 if math.cos(t - 2.4) > 0 else 3]
        for d in np.linspace(-1, 1, 9):
            for (ss, vv) in ((-.1 + d * .12, .36), (-.1, .36 + d * .04)):
                x, y, z = project(ss, vv, .305); xi, yi = int(round(x)), int(round(y))
                if P.mat[yi, xi] == 'black': img[yi, xi, :3] = RAMP['gold'][6 if d < 0 else 4]
    else:
        # rust in the lowest lame's crevices
        low = np.argwhere((P.mat == 'steel') & (sc.v > 1.0) & (P.idx <= 3))
        for y, x in low[rng.choice(len(low), min(30, len(low)), replace=False)]:
            img[y, x, :3] = RAMP['rust'][1 + rng.integers(3)]
    return img


def leather():
    sc = Scene(); P = Paint(sc)
    rad = lambda v: np.interp(v, [.10, .17, .30, .48, .66, .78, .86, 1.0, 1.15, 1.32],
                              [.16, .25, .285, .29, .25, .225, .235, .265, .285, .30])
    neck = lambda s: .12 + .07 * np.cos(np.clip(s, -1.5, 1.5)) ** 2
    for side in (-1, 1):
        sc.tube('sleeve', side * 1.45, .21, .50 + (.025 if side < 0 else 0), .255, .082, lean=side * .045)
    hem = lambda s: 1.29 + .02 * np.sin(s * 4.3 + .5) + .018 * np.sin(s * 11)
    sc.torso('body', rad, .10, 1.34, top=neck, bottom=hem)
    for side in (-1, 1):
        sc.sphere('cap', side * 1.22, .235, .27, .15, cut_below=.04, squash=.5)
    s, v, part = sc.s, sc.v, sc.part
    body = part == 'body'
    # panels: the front split down the middle and laced, side panels, a belt, tassets below it
    belt = body & (v > .745 + .015 * np.cos(s)) & (v < .815 + .015 * np.cos(s))
    skirt = body & (v >= .815 + .015 * np.cos(s))
    tasset = np.floor((s + 1.6) / .42)
    chest = body & (v < .745) & (np.abs(s) < 1.0) & (v > neck(s) + .03)
    collar = body & (v <= neck(s) + .03)
    side_p = body & ~chest & ~belt & ~skirt & ~collar
    P.bump = np.zeros((H, W))
    P.bump += .9 * np.exp(-(((np.abs(s) - .42) / .3) ** 2 + ((v - .40) / .14) ** 2)) * chest          # moulded chest
    P.bump += .5 * collar
    P.bump += np.sin(np.clip(((s + 1.6) / .42) % 1, 0, 1) * math.pi) * .45 * skirt                   # each tasset rounded
    P.bump -= .25 * (np.abs(s) < .025) * chest
    stain = vnoise(s, v, .35, .2, 9)
    P.bump += (vnoise(s, v, .05, .03, 2) - .5) * .12 * body
    P.light(ambient=.2)
    P.fill(chest, 'leather', extra=(stain - .5) * .16)
    P.fill(side_p, 'leather2', extra=(stain - .5) * .14 + .04)
    P.fill(collar, 'leather2', extra=.05)
    P.fill(belt, 'leather2', extra=-.02)
    P.fill(skirt & (tasset % 2 == 0), 'leather', extra=(stain - .5) * .18 - .03)
    P.fill(skirt & (tasset % 2 == 1), 'leather2', extra=(stain - .5) * .18 + .02)
    P.fill(part == 'sleeve', 'cloth', extra=.02)
    P.fill(part == 'cap', 'leather', extra=.03)
    P.fill(part == 'inside', 'inside', extra=-.2)
    # crevices: between tassets, under the belt, the front opening, along panel seams
    gap = skirt & ((((s + 1.6) / .42) % 1) < .07)
    P.val[gap] -= .3
    P.val[body & (np.abs(v - (.815 + .015 * np.cos(s))) < .012)] -= .25
    P.val[chest & (np.abs(s) < .03)] -= .45
    seam = body & ~skirt & (np.abs(np.abs(s) - 1.0) < .03) & (v < .745)
    P.val[seam] -= .2
    img = P.image()
    # stitching along the side seams and round the caps: light stitch, dark hole, by position on the armour
    for sv in (-1.0, 1.0):
        if abs(sv + ROT) > 1.3: continue
        for k, vv in enumerate(np.arange(.24, .73, .028)):
            x, y, z = project(sv + .05, vv, float(rad(vv)))
            xi, yi = int(round(x)), int(round(y))
            if 0 <= xi < W and 0 <= yi < H and P.mat[yi, xi] != '':
                img[yi, xi, :3] = RAMP['leather'][7 if k % 2 == 0 else 1]
    # lacing across the front opening: crossed thongs with brass eyelets
    for k, vv in enumerate(np.arange(.24, .70, .055)):
        for d in (-1, 1):
            x0, y0, _ = project(-.09, vv, float(rad(vv))); x1, y1, _ = project(.09, vv + .03, float(rad(vv)))
            if d > 0: x0, x1 = x1, x0
            for t in np.linspace(0, 1, 8):
                xi, yi = int(round(x0 + (x1 - x0) * t)), int(round(y0 + (y1 - y0) * t))
                if P.mat[yi, xi] != '': img[yi, xi, :3] = RAMP['leather'][7 - (t > .5)]
        for ss in (-.1, .1):
            x, y, _ = project(ss, vv, float(rad(vv))); xi, yi = int(round(x)), int(round(y))
            if P.mat[yi, xi] != '': img[yi, xi, :3] = RAMP['brass'][6]; img[yi + 1, xi, :3] = RAMP['brass'][2]
    # studs: brass, in rows on the side panels and the caps; a few gone, leaving a dark hole
    rng = np.random.default_rng(21)
    for sv in np.arange(-1.45, 1.46, .16):
        for vv in np.arange(.30, .72, .07):
            if abs(sv) < 1.06: continue
            if abs(sv + ROT) > 1.35: continue
            x, y, z = project(sv, vv, float(rad(vv)) + .004); xi, yi = int(round(x)), int(round(y))
            if not (0 <= xi < W - 1 and 0 <= yi < H - 1) or P.mat[yi, xi] == '' or sc.part[yi, xi] != 'body': continue
            if rng.random() < .12: img[yi, xi, :3] = RAMP['leather'][0]; continue
            img[yi, xi, :3] = RAMP['brass'][7]; img[yi, xi + 1, :3] = RAMP['brass'][4]; img[yi + 1, xi, :3] = RAMP['brass'][3]; img[yi + 1, xi + 1, :3] = RAMP['brass'][1]
    for side in (-1, 1):
        a = side * 1.22 + ROT; cx3 = .27 * math.sin(a); cz3 = .27 * math.cos(a)
        for t in (-.6, -.2, .2, .6):
            x = (cx3 + .5 + t * .1) * W; y = (.255 + PITCH * cz3) * W + abs(t) * 2
            xi, yi = int(round(x)), int(round(y))
            if 0 <= xi < W - 1 and sc.part[yi, xi] == 'cap':
                img[yi, xi, :3] = RAMP['brass'][7]; img[yi, xi + 1, :3] = RAMP['brass'][4]; img[yi + 1, xi, :3] = RAMP['brass'][2]
    # the buckle, off to one side, and the belt's loose end
    bx, by, _ = project(-.25, .78, float(rad(.78)) + .01)
    bx, by = int(round(bx)), int(round(by))
    for dx in range(-3, 4):
        for dy in range(-4, 4):
            edge = abs(dx) == 3 or dy in (-4, 3)
            if edge: img[by + dy, bx + dx, :3] = RAMP['brass'][6 if (dx < 0 or dy < 0) else 3]
    for dy in range(-3, 3): img[by + dy, bx, :3] = RAMP['brass'][5]
    for k in range(1, 9):
        if P.mat[by + 4 + k // 2, bx + 3 + k] != '': img[by + 4 + k // 2 - 1:by + 4 + k // 2 + 2, bx + 3 + k, :3] = RAMP['leather2'][3 + (k % 3 == 0)]
    # scuffs on raised places and a dark water stain low on the skirt
    for y, x in zip(*np.nonzero(np.isin(P.mat, ['leather', 'leather2']))):
        q = h(x, y, 5)
        if q < .03: img[y, x, :3] *= .78
        elif q > .975 and P.idx[y, x] >= 5: img[y, x, :3] = np.minimum(img[y, x, :3] * 1.18, 1)
    return sc, P, img


def save(img, P, path):
    out = outline(img, P.mat, P.idx)
    im = Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA')
    im.resize((W * 2, H * 2), Image.NEAREST).save(path)

if __name__ == '__main__':
    out = sys.argv[1]
    sc, P, img = leather(); save(img, P, f'{out}/Leather.png')
    for name, black in (('Plate', False), ('Black plate', True)):
        r = plate(black); img = finish_plate(*r); save(img, r[1], f'{out}/{name}.png')
    print('ok')

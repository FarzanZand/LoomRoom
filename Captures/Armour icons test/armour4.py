"""Three body armours, each its own construction and silhouette (armour3.py gave all three one body).

  * Hunter's jerkin (leather): sleeveless, a stand-up collar open at the throat, drooping shoulder flaps cut in tabs,
    laced front, a long skirt split up the front with its corners hanging in tails. Narrow, long, soft outline.
  * Breastplate over mail (steel): a short cuirass and two faulds over a long mail shirt; mail sleeves to the forearm,
    small round pauldrons, the mail skirt long and wide. Narrow at the top, wide at the bottom.
  * Full plate (blackened): very wide stacked pauldrons with neck guards standing up beside the head, plate arms in
    lames with elbow cops, a V torso and two long tassets over the thighs. Wide at the top, tapering.
"""
import sys, math
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from armour2 import Scene, vnoise, h, W, H, X, Y, ROT, PITCH
from armour3 import Armour, limb, save, RAMP

def ring(sc, part, rad, v0, v1, keep=None, top=None, bottom=None, cx=0.0, zoff=0.0):
    """Part of a body of revolution: like Scene.torso, but only where keep(s, v) holds (separate plates round the body)."""
    v = Y.copy()
    for _ in range(3):
        r = rad(np.clip(v, v0, v1)); a = np.arcsin(np.clip((X - cx) / np.maximum(r, 1e-4), -1, 1))
        z = r * np.cos(a); v = Y - PITCH * z
    s = a - ROT
    m = (np.abs(X - cx) < r) & (v >= v0) & (v <= v1)
    if top is not None: m &= v >= top(s)
    if bottom is not None: m &= v <= bottom(s)
    if keep is not None: m &= keep(s, v)
    dr = (rad(np.clip(v + .01, v0, v1)) - rad(np.clip(v - .01, v0, v1))) / .02
    n = np.dstack([np.sin(a), -dr * np.cos(a), np.cos(a)]); n /= np.linalg.norm(n, axis=2, keepdims=True)
    sc.put(m, z + zoff, part, s, v, n, r)
    return m

def flat(sc, part, pts, z, bulge=.7, tilt=(0.0, 0.0)):
    """A plate drawn as an outline on screen (x centred, y down, in armour widths), curved across its width."""
    S = 4
    im = Image.new('L', (W * S, H * S), 0)
    ImageDraw.Draw(im).polygon([((x + .5) * W * S, y * W * S) for x, y in pts], fill=255)
    m = np.asarray(im.resize((W, H), Image.BOX)) > 110
    xs = np.where(m, X, np.nan)
    with np.errstate(all='ignore'):
        lo, hi = np.nanmin(xs, 1, keepdims=True), np.nanmax(xs, 1, keepdims=True)
    u = np.nan_to_num((X - (lo + hi) / 2) / np.maximum((hi - lo) / 2, 1e-3))
    n = np.dstack([u * bulge + tilt[0], np.full((H, W), tilt[1]), np.ones((H, W))])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    sc.put(m, np.full((H, W), z) + (1 - u * u) * .02, part, u, Y, n)
    return m

def at(s, v, R):
    a = s + ROT
    return R * math.sin(a), v + PITCH * R * math.cos(a), R * math.cos(a)

def mail_rings(A, mask, strength=1.0):
    yi, xi = np.mgrid[0:H, 0:W]
    cx = (xi + (yi // 2) % 2) % 2; cy = yi % 2
    A.extra[mask & (cx == 0) & (cy == 0)] += .12 * strength
    A.extra[mask & (cx == 1) & (cy == 1)] -= .14 * strength
    A.extra[mask & (cx == 1) & (cy == 0)] += .02


# ── Hunter's jerkin ──────────────────────────────────────────────────────────
def jerkin():
    sc = Scene()
    rad = lambda v: np.interp(v, [.12, .18, .28, .44, .60, .74, .80, .95, 1.15, 1.42],
                              [.15, .215, .25, .255, .23, .205, .20, .225, .245, .255])
    neck = lambda s: .13 + .06 * np.cos(np.clip(s, -1.5, 1.5)) ** 2 + .1 * np.clip(np.abs(s) - .9, 0, 1)
    # bare arms' openings show the dark shirt beneath: short stubs of linen sleeve
    for side in (-1, 1):
        limb(sc, 'sleeve', side * 1.4, .26, .21, side * .06, .17, .07, .064)
    # the skirt: split up the front, its two front corners hanging lower in tails
    def hem(s):
        tail = .12 * np.exp(-((np.abs(s) - .25) / .2) ** 2)
        return 1.30 + tail + .012 * np.sin(s * 13)
    sc.torso('body', rad, .12, 1.46, top=neck, bottom=hem)
    # a stand-up collar, open at the throat
    # lapels: the coat's front edges folded back at the neck, two flat triangles lying on the chest
    for sg in (-1, 1):
        xa, ya, za = at(sg * .30 + .02, .135, .2)
        xb, yb, zb = at(sg * .06 + .02, .17, .22)
        xc, yc, zc = at(sg * .14 + .02, .34, .255)
        xd, yd, zd = at(sg * .48 + .02, .19, .235)
        flat(sc, 'lapel', [(xa, ya), (xb, yb), (xc, yc), (xd, yd)], max(za, zb, zc, zd) + .02, bulge=.3, tilt=(sg * -.15, -.5))
    # a hood folded down behind the neck: a soft lump showing above the shoulders, its opening dark
    hx, hy, hz = at(math.pi, .12, .12)
    hood = [(-.17, .20), (-.155, .13), (-.10, .09), (-.03, .075), (.04, .075), (.11, .09), (.155, .13), (.17, .20)]
    hood = [(x + .02, y) for x, y in hood]
    flat(sc, 'hood', hood, -.05, bulge=1.0, tilt=(0, -.6))
    # shoulder flaps: drooping leather, three tabs along the lower edge
    for side in (-1, 1):
        x0, y0, z0 = at(side * 1.15, .19, .23)
        sx = side
        pts = [(x0 - sx * .10, y0 - .02), (x0 - sx * .02, y0 - .06), (x0 + sx * .08, y0 - .045), (x0 + sx * .14, y0 + .02),
               (x0 + sx * .15, y0 + .12), (x0 + sx * .115, y0 + .155), (x0 + sx * .09, y0 + .12), (x0 + sx * .06, y0 + .165),
               (x0 + sx * .03, y0 + .13), (x0 + sx * .0, y0 + .17), (x0 - sx * .03, y0 + .12), (x0 - sx * .08, y0 + .06)]
        flat(sc, 'flap', pts, z0 + .06, bulge=.9, tilt=(sx * .25, -.35))
    s, v, part = sc.s, sc.v, sc.part
    body = part == 'body'
    A = Armour(sc)
    stain = (vnoise(s, v, .3, .18, 9) - .5) * .5
    grain = vnoise(s, v, .045, .025, 2) - .5
    A.plate(part == 'sleeve', 'cloth', 0, bevel=0, shadow=0, extra=.06)
    A.plate(part == 'sleeve_in', 'cloth', 0, bevel=0, shadow=0, extra=-.5)
    A.plate(part == 'inside', 'leather2', 0, bevel=0, shadow=0, extra=-.45)
    belt_y = lambda s: .78 + .02 * np.cos(np.clip(s, -1.6, 1.6)) ** 2
    A.plate(body, 'leather2', 1, bevel=1.5, lift=.6, extra=stain * .12 - .05)
    # the split up the front of the skirt: the dark inside showing between the two halves
    split = body & (v > belt_y(s) + .06) & (np.abs(s - .02) < .03 + (v - .84) * .16)
    for sg in (-1, 1):
        front = body & (v >= neck(s) + .01) & (sg * (s - .02) > .03) & (sg * s < 1.05) & ~split
        A.plate(front, 'leather', 2, bevel=2.2, lift=1.4, extra=stain * .14 - .05 - .05 * (v > belt_y(s)))
    A.plate(split, 'leather2', 2.5, bevel=0, shadow=0, extra=-.5)
    belt = body & (v >= belt_y(s)) & (v < belt_y(s) + .05)
    A.plate(belt, 'leather2', 8, bevel=1.3, lift=1.0, extra=-.04)
    hd = part == 'hood'
    A.plate(hd, 'leather2', .8, bevel=2.0, lift=1.8, shadow=0, extra=.06)
    opening = hd & (((X - .02) / .085) ** 2 + ((Y - .145) / .04) ** 2 < 1)
    A.extra[opening] -= .45
    A.extra[hd & ~opening & (Y > .12)] -= .1
    A.plate(part == 'lapel', 'leather2', 9, bevel=1.3, lift=1.4, shadow=3, extra=.04)
    # a darker border along the front edges, the hem and round the armholes
    edge_front = body & (np.abs(s - .02) < .03 + np.clip(v - .84, 0, 1) * .16 + .075) & ~split & (v > .14)
    edge_hem = body & (v > hem(s) - .05)
    A.plate((edge_front | edge_hem) & (A.order <= 2), 'leather2', 2.2, bevel=1.0, lift=.8, shadow=1, extra=-.08)
    A.plate(part == 'flap', 'leather2', 10, bevel=1.6, lift=1.8, shadow=3, extra=stain * .1 + .10)
    for sg in (-1, 1):
        seam = (A.order == 2) & (np.abs(s - sg * .62) < .025)
        A.extra[seam] -= .16
    A.bump += grain * .14 * (A.order >= 1)
    A.bump += .5 * np.exp(-(((np.abs(s) - .4) / .3) ** 2 + ((v - .42) / .14) ** 2)) * (A.order == 2)
    A.build(); A.light(ambient=.16); A.image()
    rad1 = lambda vv: float(rad(vv)) + .006
    R = RAMP
    # lacing up the front, crossed, between the two front halves, above the belt
    for vv in np.arange(.30, .76, .008):
        x, y, c = at(.02, vv, rad1(vv)); A.px((x + .5) * W - .5, y * W - .5, R['leather2'][0])
    for vv in np.arange(.33, .74, .05):
        for a0, a1 in ((-.10, .14), (.14, -.10)):
            x0, y0, _ = at(a0, vv, rad1(vv)); x1, y1, _ = at(a1, vv + .03, rad1(vv))
            for t in np.linspace(0, 1, 8):
                A.px((x0 + (x1 - x0) * t + .5) * W - .5, (y0 + (y1 - y0) * t) * W - .5, R['leather'][8 if t < .5 else 6])
    # stitching round the flaps' tabs and along the front edges and hem
    for name in ('flap',):
        m = sc.part == name
        edge = m & ~ndimage.binary_erosion(m, iterations=2) & ndimage.binary_erosion(m)
        for k, (y, x) in enumerate(sorted(zip(*np.nonzero(edge)), key=lambda p: (p[1], p[0]))):
            if k % 2 == 0 and A.mat[y, x] in ('leather', 'leather2'): A.img[y, x, :3] = R[A.mat[y, x]][min(A.idx[y, x] + 3, len(R[A.mat[y, x]]) - 1)]
    for sg in (-1, 1):
        for k, vv in enumerate(np.arange(.36, 1.36, .028)):
            x, y, c = at(sg * (.075 + max(0, vv - .84) * .16 + .05) + .02, vv, rad1(vv))
            if c > .2 and k % 2 == 0: A.px((x + .5) * W - .5, y * W - .5, R['leather'][8])
    for sg in (-1, 1):
        for k, vv in enumerate(np.arange(.22, 1.30, .03)):
            x, y, c = at(sg * .62 + .045, vv, rad1(vv))
            if c > .25 and k % 2 == 0: A.px((x + .5) * W - .5, y * W - .5, R['leather'][7])
    for side in (-1, 1):
        x0, y0, z0 = at(side * 1.15, .19, .23)
        if side < 0: continue
        A.rivet((x0 + .5 + side * .03) * W - .5, (y0 + .015) * W - .5, 'brass')
    # buckle on the belt, off centre; a small pouch hanging from the belt on the near side
    x, y, _ = at(-.35, float(belt_y(-.35)) + .025, rad1(.8) + .01)
    bx, by = int(round((x + .5) * W - .5)), int(round(y * W - .5))
    br = R['brass']
    for dx in range(-2, 3):
        for dy in range(-3, 4):
            if abs(dx) == 2 or abs(dy) == 3:
                A.img[by + dy, bx + dx, :3] = br[7 if (dx == -2 or dy == -3) else 3]
    for dy in range(-2, 3): A.img[by + dy, bx, :3] = br[5]
    px, py, _ = at(.75, .86, rad1(.86) + .02)
    cxp, cyp = (px + .5) * W, py * W
    for dy in range(0, 11):
        for dx in range(-5, 6):
            if abs(dx) <= 5 - (dy > 8) * (dy - 8) * 1.5:
                xx, yy = int(cxp + dx), int(cyp + dy)
                shade = 5 - (dx > 1) - (dx > 3) - (dy > 7) + (dx < -2 and dy < 4)
                A.img[yy, xx, :3] = R['leather2'][int(np.clip(shade, 0, 7))]; A.img[yy, xx, 3] = 1; A.mat[yy, xx] = 'leather2'; A.idx[yy, xx] = int(np.clip(shade, 0, 7))
    for dx in range(-5, 6): A.img[int(cyp + 2), int(cxp + dx), :3] = R['leather2'][2]
    A.img[int(cyp + 4), int(cxp), :3] = br[6]
    # scuffs and pores
    for y, x in zip(*np.nonzero(np.isin(A.mat, ['leather', 'leather2']))):
        q = h(x, y, 5)
        if q < .025: A.img[y, x, :3] *= .8
        elif q > .985 and A.idx[y, x] >= 5: A.img[y, x, :3] = np.minimum(A.img[y, x, :3] * 1.15, 1)
    A.outline()
    return A.img


# ── Breastplate over mail ────────────────────────────────────────────────────
def breastplate():
    sc = Scene()
    rad = lambda v: np.interp(v, [.12, .18, .30, .46, .62, .76, .90, 1.10, 1.30, 1.44],
                              [.15, .22, .255, .26, .235, .215, .24, .28, .315, .33])
    neck = lambda s: .13 + .05 * np.cos(np.clip(s, -1.5, 1.5)) ** 2 + .09 * np.clip(np.abs(s) - .9, 0, 1)
    # long mail sleeves, hanging out a little from the body
    for side in (-1, 1):
        limb(sc, 'sleeve', side * 1.42, .25, .23, side * .13, .52 if side > 0 else .49, .078, .066)
    hem = lambda s: 1.40 + .025 * np.sin(s * 3.1 + .4) + .014 * np.sin(s * 9.7)
    sc.torso('body', rad, .12, 1.46, top=neck, bottom=hem)
    # small round pauldrons, two lames
    for side in (-1, 1):
        for j in reversed(range(2)):
            sc.sphere(f'paul{j}', side * (1.25 + j * .07), .22 + j * .055, .245 + j * .01, .125 - j * .014, cut_below=.04, squash=.55)
    s, v, part = sc.s, sc.v, sc.part
    body = part == 'body'
    A = Armour(sc)
    hammer = vnoise(s, v, .08, .04, 3) - .5
    A.plate((part == 'sleeve') | body, 'mail', 0, bevel=0, shadow=0, shine=.3)
    A.plate(part == 'sleeve_in', 'mail', 0, bevel=0, shadow=0, extra=-.55)
    sag = lambda s: .03 * np.cos(np.clip(s, -1.6, 1.6)) ** 2
    for k in reversed(range(2)):
        y0 = .70 + k * .09
        lame = body & (v >= y0 + sag(s)) & (v < y0 + .11 + sag(s)) & (np.abs(s) < 1.25)
        A.plate(lame, 'steel', 1 + (1 - k), bevel=2.2, lift=1.4, shine=.55, extra=(v > y0 + .095 + sag(s)) * -.22)
    cuir = body & (v >= neck(s) + .03) & (v < .72 + sag(s)) & (np.abs(s) < 1.22)
    A.plate(cuir, 'steel', 5, bevel=2.5, lift=1.6, shine=1.0, extra=hammer * .05)
    # a brass-edged neckline and armholes
    tw = .026
    trims = (cuir & (v < neck(s) + .03 + tw * 1.3)) | (cuir & (np.abs(s) > 1.22 - tw * 2.2))
    A.plate(trims, 'brass', 12, bevel=1.2, lift=.9, shadow=1, shine=1.2)
    for j in range(2):
        A.plate(part == f'paul{j}', 'steel', 8 + (1 - j), bevel=1.6, lift=1.0, shine=1.0)
    A.bump += np.clip(1 - np.abs(s) / .07, 0, 1) * cuir * (v > .2) * 1.4
    A.bump += 5.0 * np.exp(-((s / .8) ** 2 + ((v - .40) / .22) ** 2)) * cuir
    A.bump -= .8 * np.exp(-(((s + .4) / .09) ** 2 + ((v - .47) / .04) ** 2)) * cuir
    A.bump += hammer * .12 * ((A.order >= 5) & (A.order < 8))
    mail = A.mat == 'mail'
    # folds: the mail skirt hangs in soft vertical folds that deepen toward the hem; the sleeves crease at the elbow
    skirt = mail & body & (v > .85)
    depth = np.clip((v - .85) / .55, 0, 1)
    fold = np.sin(s * 9 + 1.3 + np.sin(s * 3.7) * .8)
    A.bump += skirt * fold * depth * 2.2
    A.extra[skirt] -= (np.clip(-fold, 0, 1) * depth * .14)[skirt]
    slv = mail & (part == 'sleeve')
    A.bump += slv * np.sin(v * 13) * .9
    mail_rings(A, mail)
    # brass links along the hem and the sleeve ends
    hemrow = mail & body & (v > hem(s) - .028)
    cuffrow = mail & (part == 'sleeve') & (v > .945)
    A.plate(hemrow | cuffrow, 'brass', 12, bevel=0, shadow=1, shine=.9)
    belt = body & (v >= .895 + sag(s)) & (v < .935 + sag(s))
    A.plate(belt, 'leather2', 11, bevel=1.2, lift=1.0, shadow=2, extra=.06)
    mail_rings(A, hemrow | cuffrow, .8)
    A.build(); A.light(); A.image()
    rng = np.random.default_rng(4)
    rad1 = lambda vv: float(rad(vv)) + .01
    for i, sv in enumerate((-.8, -.3, .3, .8)):
        if i == 1: continue
        x, y, c = at(sv, .72 + .03 * math.cos(sv) ** 2 - .02, rad1(.70))
        if c > .25: A.rivet((x + .5) * W - .5, y * W - .5, 'brass')
    for k in range(2):
        for sg in (-1, 1):
            x, y, c = at(sg * 1.15, .70 + k * .09 + .055, rad1(.76 + k * .09))
            if c > .25: A.rivet((x + .5) * W - .5, y * W - .5, 'brass')
    for side in (-1, 1):
        for t in (-.4, .4):
            x0, y0, _ = at(side * 1.25, .22, .245)
            A.rivet((x0 + .5 + t * .09) * W - .5, (y0 + .02) * W - .5 + abs(t) * 2, 'brass')
    # a lance rest: a bracket bolted to the right breast, its arm sticking forward and up
    x0, y0, c0 = at(-.55, .47, .27)
    lx, ly = int(round((x0 + .5) * W - .5)), int(round(y0 * W - .5))
    st = RAMP['steel']
    shape = set()
    for k in range(8): shape |= {(ly, lx + k), (ly - 1, lx + k)}
    for k in range(4): shape |= {(ly - 2 - k, lx + 6), (ly - 2 - k, lx + 7)}
    for (yy, xx) in list(shape):
        for dy, dx in ((1, 0), (0, 1), (-1, 0), (0, -1), (1, 1)):
            if (yy + dy, xx + dx) not in shape: A.img[yy + dy, xx + dx, :3] = st[1]
    for (yy, xx) in shape:
        top = (yy - 1, xx) not in shape
        A.img[yy, xx, :3] = st[8] if top else st[5]
    A.rivet(lx + 1, ly - 3, 'brass'); A.rivet(lx + 3, ly + 1, 'brass')
    # the belt's small buckle
    bx, by, _ = at(.35, .915 + .03 * math.cos(.35) ** 2, float(rad(.915)) + .012)
    bx, by = int(round((bx + .5) * W - .5)), int(round(by * W - .5))
    for dx in range(-2, 3):
        for dy in range(-2, 3):
            if abs(dx) == 2 or abs(dy) == 2: A.img[by + dy, bx + dx, :3] = RAMP['brass'][7 if (dx < 0 or dy < 0) else 3]
    cand = np.argwhere((A.mat == 'steel') & (A.idx >= 4))
    for _ in range(14):
        y, x = cand[rng.integers(len(cand))]; dx = rng.choice([1, -1])
        for k in range(rng.integers(2, 4)):
            yy, xx = y + (k + 1) // 2, x + dx * k
            if A.mat[yy, xx] == 'steel': A.img[yy, xx, :3] = RAMP['steel'][min(A.idx[yy, xx] + 2, 9)]
    low = np.argwhere((A.mat == 'steel') & (sc.v > .76) & (A.idx <= 4))
    for c in range(3):
        y, x = low[rng.integers(len(low))]
        for dy in range(-1, 2):
            for dx in range(-2, 3):
                if abs(dx) + abs(dy) * 2 <= 2 and h(x + dx, y + dy, c) < .7 and A.mat[y + dy, x + dx] == 'steel':
                    A.img[y + dy, x + dx, :3] = RAMP['rust'][2 if dy < 0 or dx < 0 else 1]
    A.outline()
    return A.img


# ── Full plate ───────────────────────────────────────────────────────────────
def full_plate():
    sc = Scene()
    rad = lambda v: np.interp(v, [.12, .18, .30, .44, .58, .70, .78, .90, 1.0],
                              [.16, .26, .30, .30, .26, .215, .20, .215, .235])
    neck = lambda s: .12 + .05 * np.cos(np.clip(s, -1.5, 1.5)) ** 2 + .09 * np.clip(np.abs(s) - .9, 0, 1)
    # plate arms: upper arm lames ending in an elbow cop
    for side in (-1, 1):
        limb(sc, 'arm', side * 1.45, .27, .27, side * .10, .36 if side > 0 else .33, .07, .062)
        x0, y0, z0 = at(side * 1.45, .26, .27)
        ex, ey = x0 + side * .10, y0 + (.36 if side > 0 else .33)
        zc = z0 + .01
        sc.sphere('cop', math.atan2(ex, zc) - ROT, ey - PITCH * zc - .01, math.hypot(ex, zc), .062, squash=.7)
    sc.torso('body', rad, .12, 1.0, top=neck)
    # tassets: two long plates hanging over the thighs, each a curved piece of a wider ring
    trad = lambda v: np.interp(v, [.9, 1.35], [.25, .30])
    for c in (-.48, .44):
        ring(sc, 'tasset', trad, .92, 1.32 - abs(c) * .03, keep=lambda s, v, c=c: np.abs(s - c) < .50 - (v - .92) * .35,
             bottom=lambda s, c=c: 1.32 - abs(c) * .03 - .06 * ((s - c) / .3) ** 2, zoff=-.005)
    # mail between the tassets
    ring(sc, 'skirt', lambda v: np.interp(v, [.9, 1.3], [.22, .25]), .9, 1.22, bottom=lambda s: 1.2 + .02 * np.sin(s * 7),
         keep=lambda s, v: np.abs(s + .02) < .55, zoff=-.04)
    # very wide pauldrons: four lames, the top one broadest; a neck guard standing on each
    for side in (-1, 1):
        for j in reversed(range(4)):
            sc.sphere(f'paul{j}', side * (1.20 + j * .06), .215 + j * .055, .29 + j * .012, (.19 - j * .015), cut_below=.05, squash=.52)
        x0, y0, z0 = at(side * 1.05, .185, .25)
        sx = side
        pts = [(x0 - sx * .06, y0 + .03), (x0 + sx * .10, y0 + .045), (x0 + sx * .085, y0 - .03), (x0 + sx * .035, y0 - .10),
               (x0 - sx * .02, y0 - .165), (x0 - sx * .045, y0 - .10), (x0 - sx * .065, y0 - .03)]
        flat(sc, 'guard', pts, z0 + .09, bulge=.25, tilt=(-sx * .55, -.15))
    # a raised collar round the neck, higher at the back
    ring(sc, 'collar', lambda v: np.interp(v, [.05, .16], [.13, .17]), .05, .17,
         top=lambda s: .055 + .045 * np.cos(np.clip(s, -1.6, 1.6)) ** 2, zoff=.01)
    s, v, part = sc.s, sc.v, sc.part
    body = part == 'body'
    A = Armour(sc)
    hammer = vnoise(s, v, .08, .04, 3) - .5
    A.plate((part == 'skirt'), 'mail', 0, bevel=0, shadow=0, shine=.3)
    col = part == 'collar'
    A.plate(col, 'black', 12, bevel=1.5, lift=1.2, shadow=3, shine=.7)
    sag = lambda s: .03 * np.cos(np.clip(s, -1.6, 1.6)) ** 2
    for k in reversed(range(2)):
        y0 = .78 + k * .1
        lame = body & (v >= y0 + sag(s)) & (np.abs(s) < 1.6)
        A.plate(lame, 'black', 1 + (1 - k), bevel=2.2, lift=1.6, shadow=3, shine=.45, extra=(v > y0 + .085 + sag(s)) * -.18)
    A.plate(part == 'tasset', 'black', .5, bevel=2.2, lift=1.5, shadow=3, shine=.6)
    cuir = body & (v < .80 + sag(s))
    A.plate(cuir, 'black', 5, bevel=2.5, lift=1.6, shine=.6, extra=hammer * .04)
    plack = cuir & (v > .52 + .2 * np.abs(s) ** 1.25) & (np.abs(s) < 1.05)
    A.plate(plack, 'black', 6, bevel=2, lift=1.4, shine=.6, extra=.02)
    for j in range(4):
        A.plate(part == f'paul{j}', 'black', 8 + (3 - j), bevel=1.6, lift=1.0, shine=.6)
    A.plate(part == 'guard', 'black', 13, bevel=1.4, lift=1.4, shadow=3, shine=.7)
    A.plate(part == 'arm', 'black', 3, bevel=0, shadow=0, shine=.6)
    A.plate(part == 'cop', 'black', 4, bevel=1.2, lift=1.0, shadow=2, shine=.45)
    A.plate(part == 'sleeve_in', 'black', 3, bevel=0, shadow=0, extra=-.6)
    # gold: the neckline, the plackart's edge, the lames' and tassets' lower edges, the guards' rims
    tw = .026
    gold = (cuir & (v < neck(s) + tw * 1.3)) | (plack & (v < .52 + .2 * np.abs(s) ** 1.25 + tw * 1.2))
    for k in range(2):
        y0 = .78 + k * .1
        gold |= body & (A.order == 1 + (1 - k)) & (v >= y0 + .1 + sag(s) - tw * .7) & (v < y0 + .1 + sag(s))
    tas = part == 'tasset'
    gold |= tas & ~np.roll(tas, -2, 0)
    gold |= col & ~np.roll(col, 2, 0)
    cop = part == 'cop'; gold |= cop & ~np.roll(cop, -1, 0)
    gd = part == 'guard'; gold |= gd & ~ndimage.binary_erosion(gd, iterations=1) & (Y < np.nanmean(np.where(gd, Y, np.nan)))
    A.plate(gold & (A.order >= .5), 'gold', 14, bevel=1.1, lift=.9, shadow=1, shine=1.2)
    # arm lames: bands across the arm
    arm = part == 'arm'
    A.bump += arm * ((v * 4) % 1) * 1.6
    A.extra[arm & ((v * 4) % 1 < .14)] -= .28
    A.bump += np.clip(1 - np.abs(s) / .07, 0, 1) * cuir * (v > .2) * 1.3
    A.bump += 4.5 * np.exp(-((s / .8) ** 2 + ((v - .40) / .22) ** 2)) * cuir
    for f in (.3, .55):
        for sg in (-1, 1):
            line = sg * f * (.75 + (v - .2) * .55)
            A.bump += .7 * np.clip(1 - np.abs(s - line) / .04, 0, 1) * cuir * (v > .22) * (v < .52 + .2 * np.abs(s) ** 1.25)
    A.bump += tas * ((v - .92) / .1 % 1) * 1.6          # tasset lames, each overlapping the next
    A.extra[tas & (((v - .92) / .1) % 1 < .14)] -= .25
    A.bump += hammer * .06 * ((A.order >= 5) & (A.order < 8))
    mail_rings(A, A.mat == 'mail', .8)
    A.build(); A.light(); A.image()
    rng = np.random.default_rng(11)
    for sv in (-.7, -.25, .25, .7):
        x, y, c = at(sv, .80 + .03 * math.cos(sv) ** 2 - .025, float(rad(.78)) + .01)
        if c > .25: A.rivet((x + .5) * W - .5, y * W - .5, 'gold')
    for side in (-1, 1):
        for t in (-.45, 0, .45):
            x0, y0, _ = at(side * 1.2, .215, .29)
            if side < 0 and t > .3: continue
            A.rivet((x0 + .5 + t * .14) * W - .5, (y0 + .02) * W - .5 + abs(t) * 2.4, 'gold')
    # an etched sigil on the breast
    for t in np.linspace(0, 2 * math.pi, 44, endpoint=False):
        x, y, c = at(-.12 + .16 * math.cos(t), .36 + .05 * math.sin(t), .305)
        A.px((x + .5) * W - .5, y * W - .5, RAMP['gold'][6 if math.cos(t - 2.4) > 0 else 3])
    for d in np.linspace(-1, 1, 11):
        x, y, c = at(-.12 + d * .12, .36, .305); A.px((x + .5) * W - .5, y * W - .5, RAMP['gold'][6 if d < 0 else 4])
        x, y, c = at(-.12, .36 + d * .04, .305); A.px((x + .5) * W - .5, y * W - .5, RAMP['gold'][6 if d < 0 else 4])
    cand = np.argwhere((A.mat == 'black') & (A.idx >= 4))
    for _ in range(10):
        y, x = cand[rng.integers(len(cand))]; dx = rng.choice([1, -1])
        for k in range(rng.integers(2, 4)):
            yy, xx = y + (k + 1) // 2, x + dx * k
            if A.mat[yy, xx] == 'black': A.img[yy, xx, :3] = RAMP['black'][min(A.idx[yy, xx] + 2, 9)]
    A.outline()
    return A.img


if __name__ == '__main__':
    out = sys.argv[1]
    save(jerkin(), f'{out}/Leather.png')
    save(breastplate(), f'{out}/Plate.png')
    save(full_plate(), f'{out}/Black plate.png')
    print('ok')

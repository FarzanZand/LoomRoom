"""Redraws two old 32-texel room styles (Timber and Stone; Dungeon1 default style) at the dungeon's density of 384 texels
an architecture tile (3 m, 128 a metre), keeping what the user liked about them: their own layouts, read from the old
textures themselves (every joint pixel of the old tile is found and the layout scaled up 12 times, joints thinned to
8 texels), and their own colours (each surface's ramp taken from the old texture's pixels). Only then the detail the
higher resolution allows, kept quiet: one flat tone per stone with a step lighter or darker in a few loose clusters,
a lit top edge and a shaded bottom one (strong only where the old texture had lit rims: the slate and the rubble),
a few short cracks, chipped corners, specks. No bevel on every stone, no fine noise, no new layout.

Each style also comes in two other colours (the same drawing, other ramps). Variants are the same layout painted with
other cracks and chips, so a variant tile joins its neighbours with no seam.

    python Tools/style_redraw.py <out root>      writes <out root>/<Style name>/<surface>.png

  Timber and Stone 2 [, Pine, Black oak]: lower (timber frame round rubble), lower b, upper (slate), upper b,
                                          ceiling (plain cut sandstone), floor (the flagstone bond), floor b
  Dungeon1 stone 2 [, Limestone, Basalt]:  wall (stone bond), wall b, ceiling (vault bond), floor (flagstone bond),
                                          floor b, trim (plain cut sandstone)
"""
import colorsys
import random
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, str(Path(__file__).parent))
import tavern_style as ts  # noqa: E402  (board drawing for the timber frame)

N, SCALE, JOINT = 384, 12, 8
OLD = Path('Assets/Game/Levels/Shared')
OLD_TEX = {'timber': 'Room styles/Crypt/Timber lower.png', 'slate': 'Room styles/Crypt/Slate upper.png',
           'crypt': 'Materials/Crypt stone.png', 'flag': 'Room styles/Crypt/Flagstone.png',
           'vault': 'Room styles/Crypt/Vault.png', 'sand': 'Room styles/Crypt/Cut sandstone.png'}


def old(name):
    return np.asarray(Image.open(OLD / OLD_TEX[name]).convert('RGB')).astype(np.float32)


def vnoise(c, seed, n=N):
    rng = np.random.default_rng(seed)
    k = n // c
    lat = rng.random((k, k))
    y = np.arange(n) / c
    y0 = np.floor(y).astype(int); t = y - y0; t = t * t * (3 - 2 * t)
    a = lat[np.ix_(y0 % k, y0 % k)]; b = lat[np.ix_(y0 % k, (y0 + 1) % k)]
    cc = lat[np.ix_((y0 + 1) % k, y0 % k)]; d = lat[np.ix_((y0 + 1) % k, (y0 + 1) % k)]
    tx = t[None, :]; ty = t[:, None]
    return (a + (b - a) * tx) * (1 - ty) + (cc + (d - cc) * tx) * ty


def ramp_from(pixels, steps=5):
    """A dark-to-light ramp from an old texture's pixels: the mean colour round evenly spaced brightness quantiles."""
    p = pixels.reshape(-1, 3)
    order = np.argsort(p.mean(1)); n = len(order); out = []
    for f in np.linspace(.08, .92, steps):
        i = int(f * (n - 1)); lo, hi = max(0, i - n // 20), min(n, i + n // 20 + 1)
        out.append(tuple(int(c) for c in p[order[lo:hi]].mean(0)))
    return out


def shade_ramp(ramp, steps=7):
    """A seven-step shading ramp round a surface's middle colour: shadows toward half its brightness and a little
    cooler, lights a fifth above and a little warmer."""
    mid = np.asarray(ramp[len(ramp) // 2], np.float32)
    out = []
    for t in np.linspace(0, 1, steps):
        f = .5 + .72 * t
        c = mid * f + np.array([-6, -2, 6]) * (1 - t) + np.array([8, 5, -2]) * t
        out.append(tuple(int(max(0, min(255, x))) for x in c))
    return out


def pick(ramp, v):
    return np.asarray(ramp, np.float32)[np.clip((v * len(ramp)).astype(int), 0, len(ramp) - 1)]


# ── Layouts from the old textures ─────────────────────────────────────

def old_joints(name, frac=.62):
    a = old(name); L = a.mean(2)
    return L < np.median(L) * frac


def layout_from_old(name):
    """The old tile's stones as labels at 384 (joints -1): the old joint pixels scaled up 12 times, then thinned to
    JOINT texels by growing every stone into them evenly (on a 3 x 3 wrap, so the tile stays seamless)."""
    j = old_joints(name)
    big = np.kron(j.astype(np.uint8), np.ones((SCALE, SCALE), np.uint8)).astype(bool)
    lab3, _ = ndimage.label(np.tile(~big, (3, 3)))
    dist, ind = ndimage.distance_transform_edt(lab3 == 0, return_indices=True)
    grown = lab3[ind[0], ind[1]]
    grown[dist > (SCALE - JOINT) / 2] = 0
    return grown[N:2 * N, N:2 * N].astype(int) - 1


def slate_layout():
    """The old slate's layout, read off its pixels by hand (its joints are too faint to find by brightness): four
    courses of 8 old pixels, the vertical joints at the old x positions, each course's joint at its top."""
    lab = -np.ones((N, N), int); bid = 0
    courses = [(0, [0, 15, 20]), (8, [0]), (16, [0, 11, 18]), (24, [0, 8])]
    for y0, cuts in courses:
        edges = cuts + [32]
        for a, b in zip(edges, edges[1:]):
            ys = slice(y0 * SCALE + JOINT // 2, (y0 + 8) * SCALE - JOINT // 2)
            xs = slice(a * SCALE + JOINT // 2, b * SCALE - JOINT // 2)
            lab[ys, xs] = bid; bid += 1
    return lab


def rubble(seed, region, count, joint):
    """Rubble for the timber frame's infill (the old one is a few big round stones): jittered grid points, nearest
    point wins, joints between, each stone rounded."""
    rng = np.random.default_rng(seed)
    ys, xs = np.nonzero(region); y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    k = int(np.ceil(np.sqrt(count))); pts = []
    for gy in range(k):
        for gx in range(k):
            pts.append((y0 + (gy + rng.uniform(.0, 1.0)) * (y1 - y0) / k, x0 + (gx + rng.uniform(-.1, 1.1)) * (x1 - x0) / k))
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
    best = np.full((N, N), 1e9, np.float32); second = best.copy(); lab = np.zeros((N, N), int)
    for i, (py, px) in enumerate(pts):
        d = np.hypot((yy - py) * 1.15, (xx - px) * .9)
        closer = d < best
        second = np.where(closer, best, np.minimum(second, d)); best = np.where(closer, d, best); lab = np.where(closer, i, lab)
    lab[(second - best) < joint] = -1; lab[~region] = -1
    disk = np.hypot(*np.mgrid[-6:7, -6:7]) <= 6
    out = -np.ones_like(lab)
    for i in np.unique(lab[lab >= 0]):
        out[ndimage.binary_opening(lab == i, structure=disk)] = i
    return out


# ── Painting ──────────────────────────────────────────────────────────

def paint_stones(lab, ramp, joint, seed, rim=0.0, cracks=.3, chips=.4, specks=60, moss=None, bevel=10):
    """Every stone shaded as a raised form lit from the upper left (its face flat, its edges rounding over within
    bevel texels; rim: extra light on lit edges, the old slate and rubble had it), its own tone a little up or down,
    a fine mottle, joints in the joint colour with a dark line round each stone, a few cracks, chipped edges and specks. Tones come from the layout, the details from the seed, so
    a variant (same layout, another seed) joins its base with no seam. lab -1 = joint."""
    rng = np.random.default_rng(seed)
    n = lab.shape[0]; inside = lab >= 0
    lseed = int(np.unique(lab).size * 7919 + int(inside.sum()) % 104729)
    trng = np.random.default_rng(lseed)
    dist, ind = ndimage.distance_transform_edt(np.tile(inside, (3, 3)), return_indices=True)
    yy, xx = np.mgrid[n:2 * n, n:2 * n]
    dist = dist[n:2 * n, n:2 * n]; vy = ind[0][n:2 * n, n:2 * n] - yy; vx = ind[1][n:2 * n, n:2 * n] - xx
    # shading: each stone is a raised form, flat on its face and rounding over at its edges within `bevel` texels;
    # lit from the upper left, quantised into a seven-step ramp built round the stone's own colour (shadows down to
    # about half its brightness, lights a fifth above), with a fine dither where one band meets the next
    sr = shade_ramp(ramp)
    # each stone its own bevel (hewn by hand, never the same), an uneven face, and the height smoothed so a rubble stone
    # rounds over instead of folding along a ridge
    bev = np.full((n, n), float(bevel), np.float32)
    for b in np.unique(lab[inside]):
        bev[lab == b] = bevel * trng.uniform(.6, 1.4)
    h = np.clip(dist / bev, 0, 1); h = 1 - (1 - h) ** 2
    h = h + inside * ((vnoise(16, lseed + 7, n) - .5) * .35 + (vnoise(6, lseed + 8, n) - .5) * .15) * np.clip(dist / 3, 0, 1)
    h = ndimage.gaussian_filter(np.where(inside, h, 0), sigma=max(1.5, bevel * .22), mode='wrap')
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * .5; gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * .5
    k = bevel * .9
    nz = 1 / np.sqrt(1 + (gx * k) ** 2 + (gy * k) ** 2)
    L = np.array([-.5, -.62, .6]); L = L / np.linalg.norm(L)
    shade = (-gx * k * L[0] - gy * k * L[1] + L[2]) * nz - L[2]
    base = np.zeros((n, n), np.float32)
    for b in np.unique(lab[inside]):
        base[lab == b] = trng.choice([-.08, -.04, 0, 0, .04, .08])
    clus = vnoise(10, lseed + 1, n); fine = vnoise(2, lseed + 2, n); mot = vnoise(3, lseed + 5, n)
    v = .62 + shade * 1.6 + base + (clus - .5) * .12 + (mot - .5) * .1 + (fine - .5) * .06
    v = v + rim * np.clip(shade, 0, None) * 2                       # stronger lit edges where the old texture had them
    img = pick(sr, v)
    jc = np.asarray(joint, np.float32)
    img = np.where(inside[..., None], img, jc * np.where(vnoise(8, lseed + 3, n) > .6, 1.15, 1.0)[..., None])
    img = np.where((inside & (dist <= 1))[..., None], jc * .8, img)   # a dark line round each stone
    im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)); px = im.load()
    dark = tuple(int(c) for c in jc); light = tuple(int(c) for c in ramp[-1])
    ins = inside.tolist()
    def ok(x, y, d=3): return ins[y % n][x % n] and dist[y % n, x % n] > d
    ids = np.unique(lab[inside])
    centres = ndimage.center_of_mass(np.ones_like(lab), lab, ids) if len(ids) else []
    for (py, px0) in centres:
        if rng.random() < cracks:
            x, y = int(px0 + rng.integers(-20, 21)), int(py + rng.integers(-8, 9))
            for _ in range(int(rng.integers(6, 18))):
                if ok(x, y):
                    px[x % n, y % n] = dark
                    if ok(x + 1, y): px[(x + 1) % n, y % n] = light
                x += int(rng.choice((-1, 0, 1))); y += int(rng.choice((1, 1, 0)))
    for _ in range(int(len(ids) * chips)):
        x, y = int(rng.integers(n)), int(rng.integers(n))
        if not ins[y][x] or dist[y, x] > 2: continue
        r = int(rng.integers(2, 4))
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy <= r * r and ins[(y + dy) % n][(x + dx) % n]: px[(x + dx) % n, (y + dy) % n] = dark
    for _ in range(specks):
        x, y = int(rng.integers(n)), int(rng.integers(n))
        if ok(x, y): px[x, y] = dark if rng.random() < .6 else light
    if moss is not None:
        mn = vnoise(16, seed + 9, n); mf = vnoise(3, seed + 10, n); mr = np.asarray(moss[0], np.float32)
        for y in range(n):
            for x in range(n):
                if mn[y, x] > 1 - moss[1] and (not ins[y][x] or (dist[y, x] <= 1.5 and vy[y, x] > 0)) and mf[y, x] > .45:
                    px[x, y] = tuple(int(c) for c in mr[int(mf[y, x] * len(mr) * .999)])
    return im


def plain(ramp, joint, seed, specks=120, cracks=8):
    """A plain cut-sandstone surface (the old one has no joints): a flat tone, a step up or down in loose clusters,
    a few hairline cracks and specks."""
    rng = np.random.default_rng(seed)
    clus = vnoise(16, seed + 1, N); fine = vnoise(4, seed + 2, N); mot = vnoise(4, seed + 5, N)
    v = .5 + np.where((clus > .7) & (fine > .35), .2, 0) + np.where((clus < .25) & (fine > .35), -.2, 0)
    v = v + np.where(mot > .55, .2, 0) + np.where(mot < .1, -.2, 0)
    im = Image.fromarray(pick(ramp, v).astype(np.uint8)); px = im.load()
    dark = tuple(int(c) for c in joint); light = tuple(int(c) for c in ramp[-1])
    for _ in range(cracks):
        x, y = int(rng.integers(N)), int(rng.integers(N))
        for _ in range(int(rng.integers(10, 30))):
            px[x % N, y % N] = dark; px[(x + 1) % N, y % N] = light
            x += int(rng.choice((-1, 0, 1, 1))); y += int(rng.choice((1, 0)))
    for _ in range(specks):
        x, y = int(rng.integers(N)), int(rng.integers(N)); px[x, y] = dark if rng.random() < .6 else light
    return im


# ── The timber frame ──────────────────────────────────────────────────

POST, BEAM, SILL = 60, 72, 72   # the old frame: posts 5 old pixels each side of the tile's edge, beam and sill 6


def timber_lower(stone_ramp, joint, oak, oak_line, seed, layout_seed=11, moss=None):
    region = np.zeros((N, N), bool); region[BEAM:N - SILL, POST:N - POST] = True
    lab = rubble(layout_seed, region, 16, 5)
    img = paint_stones(lab, stone_ramp, joint, seed, rim=.12, cracks=.25, chips=.3, specks=30, moss=moss, bevel=24)
    cv = ts.Canvas(N, N); cv.img = img; cv.px = img.load()
    rng = random.Random(seed); clusters = ts.value_noise(N, N, 6, layout_seed + 9)
    for (y0, h) in ((0, BEAM), (N - SILL, SILL)):
        ts.board(cv, 0, y0, N, h // 2, oak, oak_line, rng, clusters, along_x=True, knot_chance=.6, nails=False)
        ts.board(cv, 0, y0 + h // 2, N, h - h // 2, oak, oak_line, rng, clusters, along_x=True, knot_chance=.4, nails=False)
    for x0 in (0, POST // 2, N - POST, N - POST // 2):
        ts.board(cv, x0, BEAM, POST // 2, N - BEAM - SILL, oak, oak_line, rng, clusters, along_x=False, knot_chance=.5, nails=False)
    for x in range(N):
        cv.set(x, BEAM, oak_line); cv.set(x, N - SILL - 1, oak_line)
    for y in range(BEAM, N - SILL):
        cv.set(POST, y, oak_line); cv.set(N - POST - 1, y, oak_line)
    for (px_, py) in ((POST // 4, BEAM + 10), (POST // 4, N - SILL - 10), (N - POST // 4, BEAM + 10), (N - POST // 4, N - SILL - 10)):
        cv.set(px_, py, ts.NAIL); cv.set(px_ - 1, py - 1, ts.tone(oak, .99))
    return cv.img


# ── Colours ───────────────────────────────────────────────────────────

def spread(ramp, k):
    """The ramp's steps pushed k times further from its middle colour: the old ramps are only a few levels apart (big
    pixels carried their texture), too close for a mottle, rims or cracks to read at 128 a metre."""
    mid = np.asarray(ramp[len(ramp) // 2], np.float32)
    return [tuple(int(max(0, min(255, c))) for c in mid + (np.asarray(r, np.float32) - mid) * k) for r in ramp]


def stones_of(name, wood=False, k=2.6):
    a = old(name); m = ~old_joints(name)
    if name == 'timber':
        w = a[..., 0] > a[..., 2] * 1.25
        return spread(ramp_from(a[w] if wood else a[m & ~w]), 1.0 if wood else 1.6)
    return spread(ramp_from(a[m]), 1.3 if name == 'slate' else k)


def shift(ramp, hue=None, sat=None, val=1.0):
    """Another colour for a ramp: each step keeps its brightness order, takes a new hue and saturation."""
    out = []
    for c in ramp:
        h, s, v = colorsys.rgb_to_hsv(*(np.asarray(c) / 255.0))
        if hue is not None: h = hue
        if sat is not None: s = sat
        r, g, b = colorsys.hsv_to_rgb(h, s, min(1, v * val))
        out.append((int(r * 255), int(g * 255), int(b * 255)))
    return out


def jc(ramp, k=.45): return tuple(int(c * k) for c in ramp[0])


def build(out):
    out = Path(out)
    rub, wood, slate, flag, sand = stones_of('timber'), stones_of('timber', True), stones_of('slate'), stones_of('flag'), stones_of('sand')
    crypt, vault = stones_of('crypt'), stones_of('vault')
    oak = wood[1:]; oak_line = jc(wood, .55)
    moss = ([(40, 60, 32), (54, 78, 40), (70, 96, 50)], .3)
    TS = {
        'Timber and Stone 2': dict(rub=rub, slate=slate, flag=flag, sand=sand, oak=oak, line=oak_line, moss=None),
        'Timber and Stone 2 Pine': dict(rub=shift(rub, .07, .32, 1.35), slate=shift(slate, .1, .28, 1.2), flag=shift(flag, .08, .14, 1.25),
                                        sand=shift(sand, .1, .22, 1.08), oak=shift(oak, .08, .14, 1.15), line=shift([oak_line], .08, .2)[0], moss=None),
        'Timber and Stone 2 Black oak': dict(rub=shift(rub, .25, .12, .95), slate=shift(slate, .6, .08, .8), flag=shift(flag, .3, .07, .85),
                                             sand=shift(sand, .12, .1, .7), oak=shift(oak, .06, .3, .45), line=shift([oak_line], .06, .3, .4)[0], moss=moss),
    }
    for name, p in TS.items():
        d = out / name; d.mkdir(parents=True, exist_ok=True)
        for tag, s in (('lower', 11), ('lower b', 12)):
            timber_lower(p['rub'], jc(p['rub']), p['oak'], p['line'], s, moss=p['moss']).save(d / f'{tag}.png')
        lab = slate_layout()
        for tag, s in (('upper', 21), ('upper b', 22)):
            paint_stones(lab, p['slate'], jc(p['slate'], .5), s, rim=.1, moss=p['moss'], bevel=9).save(d / f'{tag}.png')
        plain(p['sand'], jc(p['sand'], .6), 31).save(d / 'ceiling.png')
        lab = layout_from_old('flag')
        for tag, s in (('floor', 41), ('floor b', 42)):
            paint_stones(lab, p['flag'], jc(p['flag']), s, moss=p['moss']).save(d / f'{tag}.png')
        print('wrote', d)
    D1 = {
        'Dungeon1 stone 2': dict(wall=crypt, vault=vault, flag=flag, trim=sand, moss=None),
        'Dungeon1 stone 2 Limestone': dict(wall=shift(crypt, .1, .16, 1.35), vault=shift(vault, .08, .2, 1.3), flag=shift(flag, .09, .12, 1.3), trim=shift(sand, .08, .26, 1.05), moss=None),
        'Dungeon1 stone 2 Basalt': dict(wall=shift(crypt, .62, .18, .62), vault=shift(vault, .02, .38, .9), flag=shift(flag, .62, .14, .6), trim=shift(sand, .1, .1, .75), moss=moss),
    }
    for name, p in D1.items():
        d = out / name; d.mkdir(parents=True, exist_ok=True)
        lab = layout_from_old('crypt')
        for tag, s in (('wall', 51), ('wall b', 52)):
            paint_stones(lab, p['wall'], jc(p['wall']), s, moss=p['moss']).save(d / f'{tag}.png')
        paint_stones(layout_from_old('vault'), p['vault'], jc(p['vault']), 61).save(d / 'ceiling.png')
        lab = layout_from_old('flag')
        for tag, s in (('floor', 71), ('floor b', 72)):
            paint_stones(lab, p['flag'], jc(p['flag']), s, moss=p['moss']).save(d / f'{tag}.png')
        plain(p['trim'], jc(p['trim'], .6), 81).save(d / 'trim.png')
        print('wrote', d)


if __name__ == '__main__':
    build(sys.argv[1] if len(sys.argv) > 1 else 'Temp/StyleRedraw')

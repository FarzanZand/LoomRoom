"""Room style surfaces laid from painted stones (Timber and Stone 2, Dungeon1 stone 2 and their colours). Every whole stone
in a set of Meshy paintings (house style, painted at about 5 pixels a painted pixel, brought to one texel per painted
pixel by style_tiles.grid) is cut out and laid again, so no stone is ever cut or stretched (quilting a painting cut
patches through stones and made the courses wander):

  courses  a wall, vault or floor: courses of one height, each packed with stones at their own painted width (all the
           widths in a course scaled by the same few percent so it closes at 384), every course from its own offset;
  rubble   the timber frame's infill: the big round stones packed first, the small ones into the gaps, mortar between;
  frame    beams and posts cut as strips from a quilted timber tile (its grain runs along them).

Stones from all the stone paintings are pooled and each recoloured to the surface it goes on (its brightness rank in its
own painting's colours, the colour at that rank in the surface's), so a wall has more different stones than one
painting gives. Colour versions are the finished tiles gradient-mapped to other ramps; moss and lichen keep their green.

    python Tools/style_stones.py <out root>

Paintings in Temp/Styles2/gen/<name>/<name>.png (Meshy concepts, task ids in their logs): D1_wall_big, vault_big,
flagstone_big (stone courses), slate_big (slate colours), rubble_big and rubble (round stones), timber (oak) and
sandstone (plain cut stone), the last two quilted into Temp/Styles2/tiles/{timber,sandstone}.png by style_tiles.py.
"""
import colorsys
import sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, str(Path(__file__).parent))
import style_tiles as st  # noqa: E402

N = 384
GEN = Path('Temp/Styles2/gen')
TILES = Path('Temp/Styles2/tiles')


def painting(name, margin=2):
    img = np.asarray(Image.open(GEN / name / f'{name}.png').convert('RGB'))
    g, _ = st.grid(img)
    return g[margin:-margin, margin:-margin].astype(np.float32)


def joints(g):
    L = g.mean(2)
    lo, hi = np.percentile(L, 6), np.percentile(L, 60)
    return L < lo + (hi - lo) * .32


def cut_stones(g, full_only=True, min_area=300):
    """Whole stones: pieces between joints. Those touching the painting's edge are kept only when full size."""
    j = joints(g)
    lab, _ = ndimage.label(~j)
    H, W = j.shape
    objs = ndimage.find_objects(lab)
    dims = [(sl[0].stop - sl[0].start, sl[1].stop - sl[1].start) for sl in objs]
    full_h = max(h for h, w in dims); full_w = max(w for h, w in dims)
    out = []
    for i, sl in enumerate(objs, 1):
        h, w = dims[i - 1]
        m = lab[sl] == i
        if m.sum() < min_area or h < 18 or w < 18: continue
        cut = sl[0].start == 0 or sl[1].start == 0 or sl[0].stop == H or sl[1].stop == W
        if cut and (not full_only or h < full_h * .85 or w < min(full_w * .8, 80)): continue
        m = ndimage.binary_fill_holes(ndimage.binary_closing(np.pad(m, 4), iterations=4))[4:-4, 4:-4]
        out.append((g[sl].copy(), m))
    return out


def joint_texels(g):
    j = joints(g)
    jt = g[j & ~ndimage.binary_dilation(~j, iterations=1)]
    return jt if len(jt) > 50 else g[j]


# ── Colour ─────────────────────────────────────────────────────────────

def lum(c): return c @ np.array([.3, .59, .11], np.float32)


def is_green(c):
    """Moss and lichen: clearly green (a green-grey stone is not)."""
    return (c[..., 1] > c[..., 0] * 1.22) & (c[..., 1] > c[..., 2] * 1.25) & (c[..., 1] > 40)


def recolour(pix, src, dst, bins=48):
    """Each pixel takes dst's mean colour at the brightness rank it has in src (both stone colour samples; a smooth
    curve through 48 bins, so neighbouring texels of one tone stay one tone)."""
    sl = np.sort(lum(src.reshape(-1, 3)))
    d = dst.reshape(-1, 3); dsorted = d[np.argsort(lum(d))]
    curve = np.array([c.mean(0) for c in np.array_split(dsorted, bins)], np.float32)
    q = np.searchsorted(sl, lum(pix.reshape(-1, 3))) / max(1, len(sl) - 1)
    f = q * (bins - 1); i0 = np.clip(f.astype(int), 0, bins - 2); t = (f - i0)[:, None]
    out = (curve[i0] * (1 - t) + curve[i0 + 1] * t).reshape(pix.shape)
    keep = is_green(pix)
    out[keep] = pix[keep]
    return out


def hue_map(img, hue, sat, val=1.0, keep_green=True, mask=None, own=.6):
    """Recolour keeping every pixel's brightness and the surface's contrast: hue set, saturation scaled to `sat` of
    its own (plus a floor), value scaled."""
    a = np.asarray(img, np.float32) / 255; out = a.copy()
    m = np.ones(a.shape[:2], bool) if mask is None else mask
    px = a[m]
    mx = px.max(1); mn = px.min(1); v = mx; s0 = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    s1 = np.clip(s0 * own + sat, 0, 1); v1 = np.clip(v * val, 0, 1)
    rgb = np.array([colorsys.hsv_to_rgb(hue, ss, vv) for ss, vv in zip(s1, v1)], np.float32)
    if keep_green:
        g = is_green(px * 255); rgb[g] = px[g]
    out[m] = rgb
    return Image.fromarray(np.clip(out * 255, 0, 255).astype(np.uint8))


def ramp_map(img, ramp, keep_green=True, mask=None):
    """Gradient map: every pixel's brightness (its rank among the masked pixels) picks a colour along ramp."""
    a = np.asarray(img, np.float32); out = a.copy()
    m = np.ones(a.shape[:2], bool) if mask is None else mask
    px = a[m]; L = lum(px); r = np.argsort(np.argsort(L)) / max(1, len(L) - 1)
    rp = np.asarray(ramp, np.float32); k = len(rp) - 1
    i0 = np.clip((r * k).astype(int), 0, k - 1); t = (r * k - i0)[:, None]
    new = rp[i0] * (1 - t) + rp[i0 + 1] * t
    if keep_green:
        g = is_green(px); new[g] = px[g]
    out[m] = new
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8))


# ── Laying ─────────────────────────────────────────────────────────────

def base_canvas(jt, rng, h=N, w=N):
    """Mortar: the painting's joint colour (its median) and a step darker, in soft clusters, never texel noise."""
    med = np.median(jt, 0)
    n = ndimage.gaussian_filter(rng.random((h, w)), 3, mode='wrap')
    dark = n < np.percentile(n, 40)
    return np.where(dark[..., None], med * .78, med).astype(np.float32)


def paste(canvas, pix, m, y0, x0):
    h, w = m.shape
    ys = (np.arange(h) + y0) % N; xs = (np.arange(w) + x0) % N
    sub = canvas[np.ix_(ys, xs)]; sub[m] = pix[m]; canvas[np.ix_(ys, xs)] = sub


def resize(pix, m, h, w):
    yi = (np.arange(h) * pix.shape[0] / h).astype(int); xi = (np.arange(w) * pix.shape[1] / w).astype(int)
    return pix[np.ix_(yi, xi)], m[np.ix_(yi, xi)]


def lay_courses(pool, jt, course, seed, joint=4, min_w=36):
    rng = np.random.default_rng(seed)
    canvas = base_canvas(jt, rng)
    pool = [(p, m) for p, m in pool if p.shape[1] >= min_w]
    last = -1
    for c in range(N // course):
        th = course - joint
        widths, picks = [], []
        while sum(widths) < N:
            i = int(rng.integers(len(pool)))
            if i == last and len(pool) > 1: continue
            last = i; p, m = pool[i]
            widths.append(max(min_w, round(p.shape[1] * th / p.shape[0])) + joint); picks.append(i)
        # close the course at 384: drop the last stone or scale every width a little, whichever bends less
        if len(widths) > 1 and sum(widths) - N > widths[-1] / 2:
            widths.pop(); picks.pop()
        k = N / sum(widths)
        ws = [round(w * k) for w in widths]; ws[-1] += N - sum(ws)
        x = int(rng.integers(N))
        for w, i in zip(ws, picks):
            p, m = pool[i]
            if rng.random() < .5: p, m = p[:, ::-1], m[:, ::-1]
            p2, m2 = resize(p, m, th, w - joint)
            paste(canvas, p2, m2, c * course + joint // 2, x + joint // 2)
            x += w
    return Image.fromarray(np.clip(canvas, 0, 255).astype(np.uint8))


def lay_rubble(big, small, jt, h, w, seed, gap=3, tries=6000):
    """Round stones packed into an h x w panel: big ones first (darts, kept where they don't touch a placed stone grown
    by gap), then small ones into what is left."""
    rng = np.random.default_rng(seed)
    panel = base_canvas(jt, rng, h, w); occ = np.zeros((h, w), bool)
    # the small stones also at 85% (within the texel check's 20%), so the last gaps fill
    smaller = [resize(p, m, max(12, int(p.shape[0] * .85)), max(12, int(p.shape[1] * .85))) for p, m in small]
    for pool, n in ((big, tries), (small, tries * 2), (smaller, tries * 3)):
        for _ in range(n):
            p, m = pool[int(rng.integers(len(pool)))]
            if rng.random() < .5: p, m = p[:, ::-1], m[:, ::-1]
            mh, mw = m.shape
            if mh > h or mw > w: continue
            y, x = int(rng.integers(0, h - mh + 1)), int(rng.integers(0, w - mw + 1))
            grown = ndimage.binary_dilation(np.pad(m, gap), iterations=gap)
            y0, x0 = y - gap, x - gap
            ys0, xs0 = max(0, y0), max(0, x0); ys1, xs1 = min(h, y0 + grown.shape[0]), min(w, x0 + grown.shape[1])
            if (occ[ys0:ys1, xs0:xs1] & grown[ys0 - y0:ys1 - y0, xs0 - x0:xs1 - x0]).any(): continue
            sub = panel[y:y + mh, x:x + mw]; sub[m] = p[m]; occ[y:y + mh, x:x + mw] |= m
    return panel, occ


# ── Build ──────────────────────────────────────────────────────────────

POST, BEAM = 60, 72   # the old frame: posts 5 old pixels each side of the tile's edge, beam and sill 6


def build(out):
    out = Path(out)
    wall, vault, flag, slate = painting('D1_wall_big'), painting('vault_big'), painting('flagstone_big'), painting('slate_big')
    families = {'wall': wall, 'vault': vault, 'flag': flag, 'slate': slate}
    stones = {k: cut_stones(v) for k, v in families.items() if k != 'slate'}
    colours = {k: v[~joints(v)] for k, v in families.items()}   # every stone texel of the painting

    def pool_for(target):
        pool = []
        for fam, ss in stones.items():
            for p, m in ss:
                pool.append((p if fam == target else recolour(p, colours[fam], colours[target]), m))
        return pool

    tiles = {}
    tiles['wall'] = lay_courses(pool_for('wall'), joint_texels(wall), 48, 1)
    tiles['wall b'] = lay_courses(pool_for('wall'), joint_texels(wall), 48, 11)
    tiles['vault'] = lay_courses(pool_for('vault'), joint_texels(vault), 48, 2)
    tiles['floor'] = lay_courses(pool_for('flag'), joint_texels(flag), 48, 3)
    tiles['floor b'] = lay_courses(pool_for('flag'), joint_texels(flag), 48, 13)
    tiles['upper'] = lay_courses(pool_for('slate'), joint_texels(slate), 48, 4, min_w=60)
    tiles['upper b'] = lay_courses(pool_for('slate'), joint_texels(slate), 48, 14, min_w=60)
    sand = Image.open(TILES / 'sandstone.png').convert('RGB')
    tiles['sandstone'] = sand

    # the timber frame: posts 60 each side of the tile edge (120 where two tiles meet), beam and sill 72, rubble between
    rub_big, rub_small = painting('rubble_big'), painting('rubble')
    big = cut_stones(rub_big, min_area=900)
    small = [(recolour(p, rub_small.reshape(-1, 3), rub_big.reshape(-1, 3)), m) for p, m in cut_stones(rub_small)]
    tb = np.asarray(Image.open(TILES / 'timber.png').convert('RGB'), np.float32)
    dark = tb.reshape(-1, 3)[np.argsort(lum(tb.reshape(-1, 3)))[300]]
    for tag, seed in (('lower', 21), ('lower b', 22)):
        a = np.zeros((N, N, 3), np.float32)
        panel, _ = lay_rubble(big, small, joint_texels(rub_big) * 1.35, N - 2 * BEAM, N - 2 * POST, seed, gap=1, tries=20000)
        a[BEAM:N - BEAM, POST:N - POST] = panel
        a[:BEAM] = tb[0:BEAM]; a[N - BEAM:] = tb[200:200 + BEAM]
        p1 = np.rot90(tb[100:100 + POST // 2, :]); p2 = np.rot90(tb[300:300 + POST // 2, :])   # grain running up
        a[BEAM:N - BEAM, :POST // 2] = p1[BEAM:N - BEAM]; a[BEAM:N - BEAM, POST // 2:POST] = p2[BEAM:N - BEAM]
        a[BEAM:N - BEAM, N - POST:N - POST // 2] = p1[BEAM:N - BEAM]; a[BEAM:N - BEAM, N - POST // 2:] = p2[BEAM:N - BEAM]
        for x in (POST // 2, POST - 1, N - POST, N - POST // 2): a[BEAM:N - BEAM, x] = dark   # the boards' seams
        a[BEAM - 1, :] = dark; a[N - BEAM, :] = dark; a[BEAM // 2, :] = dark; a[N - BEAM // 2, :] = dark
        tiles[tag] = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))

    sets = {
        'Timber and Stone 2': {'lower': 'lower', 'lower b': 'lower b', 'upper': 'upper', 'upper b': 'upper b', 'ceiling': 'sandstone', 'floor': 'floor', 'floor b': 'floor b'},
        'Dungeon1 stone 2': {'wall': 'wall', 'wall b': 'wall b', 'ceiling': 'vault', 'floor': 'floor', 'floor b': 'floor b', 'trim': 'sandstone'},
    }
    for style, files in sets.items():
        d = out / style; d.mkdir(parents=True, exist_ok=True)
        for f, t in files.items(): tiles[t].save(d / f'{f}.png')

    frame = np.zeros((N, N), bool); frame[:BEAM] = frame[-BEAM:] = True; frame[:, :POST] = frame[:, -POST:] = True
    # (hue, saturation added, brightness) per surface of each colour version
    looks = {
        'Timber and Stone 2 Pine': dict(stone=(.08, .16, 1.25), oak=(.08, .05, 1.35), ceiling=(.1, .06, 1.05)),   # buff sandstone, silvered pine
        'Timber and Stone 2 Black oak': dict(stone=(.58, .02, .82), oak=(.06, .12, .5), ceiling=(.12, .02, .72)),  # cold granite, tarred oak
        'Dungeon1 stone 2 Limestone': dict(stone=(.1, .1, 1.3), ceiling=(.09, .12, 1.25), trim=(.08, .2, 1.0)),
        'Dungeon1 stone 2 Basalt': dict(stone=(.62, .06, .62), ceiling=(.03, .28, .85), trim=(.6, .02, .7)),
    }
    for name, lk in looks.items():
        src = 'Timber and Stone 2' if name.startswith('Timber') else 'Dungeon1 stone 2'
        d = out / name; d.mkdir(parents=True, exist_ok=True)
        for f, t in sets[src].items():
            img = tiles[t]
            if f.startswith('lower'):
                img = hue_map(hue_map(img, *lk['stone'], mask=~frame, own=.25 if 'Black' in name else .6), *lk['oak'], keep_green=False, mask=frame)
            else:
                look = lk.get(f, lk['stone'])
                img = hue_map(img, *look, own=.25 if (f == 'ceiling' and name.startswith('Timber')) or 'Black' in name else .6)
            img.save(d / f'{f}.png')
    print('wrote', ', '.join(sorted(p.name for p in out.iterdir() if p.is_dir())))


if __name__ == '__main__':
    build(sys.argv[1] if len(sys.argv) > 1 else 'Temp/Styles2/out')

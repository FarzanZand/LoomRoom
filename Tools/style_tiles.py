"""Dungeon room style surfaces from Meshy paintings: one seamless tile per wall segment or floor cell, 128 texels per unit.

A painting has about 190 painted pixels across, less than a wall tile (256 x 384 texels: a 2-unit segment by the 3-unit
tile height, as the Church styles) or a floor tile (256 x 256, a 2 x 2 cell), so a crop would repeat inside the tile. Instead:
  1. grid   the painting is brought down to one texel per painted pixel (the painted grid's period and phase found
            from where colour changes; each texel is its cell's middle);
  2. quilt  a 384 x 384 tile is laid from overlapping square patches of those texels (each patch picked among the
            ones that best match what is already laid, joined along the cut where the two differ least; the last row
            and column wrap onto the first), so every texel is a painted one, the tile repeats seamlessly and the
            painting's larger shapes are kept within each patch;
  3. level  the tile's broad light drift is taken out (a painting is often lighter on one side), so a wall of tiles
            reads even.

    python Tools/style_tiles.py tile <painting.png> <out.png> [--size 256x384] [--patch 72] [--seed 1]
    python Tools/style_tiles.py grid <painting.png> <out.png>          (step 1 only, to look at)
"""
import sys
import argparse
import numpy as np
from PIL import Image


def period(sig, lo=3.0, hi=12.0):
    """The painted grid's period along one axis: the strongest repeat of colour changes between lo and hi pixels."""
    sig = sig - sig.mean()
    n = len(sig)
    spec = np.abs(np.fft.rfft(sig, n * 8))
    freqs = np.fft.rfftfreq(n * 8)
    band = (freqs > 1 / hi) & (freqs < 1 / lo)
    f = freqs[band][np.argmax(spec[band])]
    p = 1 / f
    # The phase: where cell edges sit (the comb of edges at this period lines up best).
    best, phase = -1, 0.0
    for off in np.linspace(0, p, 24, endpoint=False):
        idx = np.round(np.arange(off, n - 1, p)).astype(int)
        score = sig[idx[idx < n]].sum()
        if score > best:
            best, phase = score, off
    return p, phase


def grid(img):
    a = img.astype(np.float32)
    dx = np.abs(np.diff(a, axis=1)).sum(axis=(0, 2))
    dy = np.abs(np.diff(a, axis=0)).sum(axis=(1, 2))
    px, ox = period(dx)
    py, oy = period(dy)
    p = (px + py) / 2
    xs = np.arange(ox + 1 + p / 2, a.shape[1], px)
    ys = np.arange(oy + 1 + p / 2, a.shape[0], py)
    xs = np.clip(np.round(xs).astype(int), 0, a.shape[1] - 1)
    ys = np.clip(np.round(ys).astype(int), 0, a.shape[0] - 1)
    return img[np.ix_(ys, xs)], (px, py)


def min_cut(err):
    """Columns of the cheapest top-to-bottom path through err (h x w): left of it from the old, right from the new."""
    h, w = err.shape
    cost = err.copy()
    for y in range(1, h):
        prev = cost[y - 1]
        left = np.r_[np.inf, prev[:-1]]
        right = np.r_[prev[1:], np.inf]
        cost[y] += np.minimum(np.minimum(left, prev), right)
    path = np.zeros(h, int)
    path[-1] = int(np.argmin(cost[-1]))
    for y in range(h - 2, -1, -1):
        x = path[y + 1]
        lo, hi = max(0, x - 1), min(w, x + 2)
        path[y] = lo + int(np.argmin(cost[y, lo:hi]))
    return path


def outliers(src, share):
    """Texels whose hue stands out from the painting's (a brown pit on grey rock, a red pebble on dirt): the share of
    texels farthest from the median colour once brightness is taken out, grown by a texel. The base tile avoids them;
    they belong on rare variants."""
    a = src.astype(np.float32) + 1
    chroma = a / a.sum(axis=2, keepdims=True)
    d = np.linalg.norm(chroma - np.median(chroma.reshape(-1, 3), axis=0), axis=2)
    if share <= 0:
        return np.zeros(d.shape, bool)
    m = d > np.quantile(d, 1 - share)
    grown = m.copy()
    grown[1:] |= m[:-1]; grown[:-1] |= m[1:]; grown[:, 1:] |= m[:, :-1]; grown[:, :-1] |= m[:, 1:]
    return grown


def quilt(src, size, patch, overlap, rng, candidates=400, best_share=.04, avoid=None):
    """size: (width, height); avoid: texels of src no patch may hold (outliers)."""
    """A size x size tile from patches of src, toroidal (the tile repeats seamlessly). No spot of the painting is used
    twice (a candidate closer than half a patch to one already laid is skipped), so nothing repeats inside a tile."""
    src = src.astype(np.float32)
    sh, sw, _ = src.shape
    step = patch - overlap
    width, height = size
    nx, ny = width // step, height // step
    out = np.zeros((height, width, 3), np.float32)
    done = np.zeros((height, width), bool)

    def wrap(y, x, h, w):
        return np.ix_(np.arange(y, y + h) % height, np.arange(x, x + w) % width)

    used = []
    for by in range(ny):
        for bx in range(nx):
            y, x = by * step, bx * step
            region = wrap(y, x, patch, patch)
            have = done[region]
            target = out[region]
            ys = rng.integers(0, sh - patch, candidates * 4)
            xs = rng.integers(0, sw - patch, candidates * 4)
            if used:
                u = np.array(used)
                far = (np.abs(ys[:, None] - u[None, :, 0]) > patch // 2) | (np.abs(xs[:, None] - u[None, :, 1]) > patch // 2)
                keep = far.all(axis=1)
                if keep.sum() >= 20:
                    ys, xs = ys[keep], xs[keep]
            if avoid is not None:
                # Patches holding outlier texels are left out (the cleanest ones if every candidate has some).
                bad = np.array([avoid[a:a + patch, b:b + patch].sum() for a, b in zip(ys, xs)])
                order = np.argsort(bad, kind='stable')
                clean = order[bad[order] == 0]
                pick = clean if len(clean) >= 20 else order[:max(20, candidates // 4)]
                ys, xs = ys[pick], xs[pick]
            ys, xs = ys[:candidates], xs[:candidates]
            if have.any():
                errs = np.array([(((src[a:a + patch, b:b + patch] - target) ** 2).sum(axis=2) * have).sum() for a, b in zip(ys, xs)])
                pool = np.argsort(errs)[:max(1, int(candidates * best_share))]
                k = rng.choice(pool)
            else:
                k = 0
            used.append((ys[k], xs[k]))
            new = src[ys[k]:ys[k] + patch, xs[k]:xs[k] + patch].copy()
            # Keep the old texels left of / above the cheapest seams through the overlaps.
            keep_old = np.zeros((patch, patch), bool)
            err = ((new - target) ** 2).sum(axis=2)
            if have[:, :overlap].all():
                cut = min_cut(err[:, :overlap])
                for r in range(patch):
                    keep_old[r, :cut[r]] = True
            if have[:overlap, :].all():
                cut = min_cut(err[:overlap, :].T)
                for c in range(patch):
                    keep_old[:cut[c], c] = True
            # Wrapping round: the far overlaps (already laid by the first row / column) are cut the same way.
            if have[:, -overlap:].all():
                cut = min_cut(err[:, -overlap:][:, ::-1])
                for r in range(patch):
                    keep_old[r, patch - cut[r]:] = True
            if have[-overlap:, :].all():
                cut = min_cut(err[-overlap:, :][::-1].T)
                for c in range(patch):
                    keep_old[patch - cut[c]:, c] = True
            keep_old &= have
            merged = np.where(keep_old[..., None], target, new)
            out[region] = merged
            done[region] = True
    return out


def level(tile, sigma=48):
    """Take out the broad light drift: each texel's brightness against a wide (wrapping) blur of the tile."""
    t = tile.astype(np.float32)
    lum = t @ np.array([.3, .59, .11], np.float32)
    k = np.fft.fftfreq(t.shape[0])[:, None] ** 2 + np.fft.fftfreq(t.shape[1])[None, :] ** 2
    blur = np.real(np.fft.ifft2(np.fft.fft2(lum) * np.exp(-2 * (np.pi * sigma) ** 2 * k)))
    gain = lum.mean() / np.maximum(blur, 1.0)
    return np.clip(t * gain[..., None], 0, 255)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('mode', choices=['tile', 'grid'])
    ap.add_argument('painting')
    ap.add_argument('out')
    ap.add_argument('--size', default='256x256', help='width x height in texels (walls 256x384, floors and ceilings 256x256)')
    ap.add_argument('--patch', type=int, default=40)
    ap.add_argument('--overlap', type=int, default=8)
    ap.add_argument('--seed', type=int, default=1)
    ap.add_argument('--avoid', type=float, default=0, help='share of texels counted as hue outliers that the base keeps out (0 = none; it catches moss in cracks too, so --exclude is usually better)')
    ap.add_argument('--level', type=float, default=48, help='blur width in texels the light drift is measured over (smaller evens out smaller blotches; too small flattens the shading)')
    ap.add_argument('--saturation', type=float, default=1, help='colour strength (below 1 mutes a painting that is too hot for the dungeon palette)')
    ap.add_argument('--gain', type=float, default=1, help='brightness (a ceiling quilted from its wall painting, a little darker)')
    ap.add_argument('--dark', type=float, default=0, help='also keep out the darkest share of texels, grown by 2 (deep holes); 0 = off')
    ap.add_argument('--warm', type=int, default=0, help='also keep out texels this much redder than green (brown or orange marks on grey or green), grown by 3; 0 = off')
    ap.add_argument('--exclude', default='', help='boxes on the texel grid the base keeps out, x0,y0,x1,y1;... (marks that would show as a repeat; see grid mode)')
    ap.add_argument('--margin', type=int, default=3, help='painted texels cut off each edge of the painting (its border is often smeared)')
    a = ap.parse_args()
    img = np.asarray(Image.open(a.painting).convert('RGB'))
    g, p = grid(img)
    m = a.margin
    g = g[m:-m, m:-m]
    print(f'{a.painting}: painted pixel {p[0]:.2f} x {p[1]:.2f}, {g.shape[1]} x {g.shape[0]} texels')
    if a.mode == 'grid':
        Image.fromarray(g).save(a.out)
        m = outliers(g, a.avoid)
        marked = g.copy(); marked[m] = (255, 0, 255)
        Image.fromarray(marked).save(a.out.replace('.png', ' outliers.png'))
        return
    step = a.patch - a.overlap
    size = tuple(int(v) for v in a.size.lower().split('x'))
    if size[0] % step or size[1] % step:
        raise SystemExit(f'patch - overlap ({step}) must divide the size ({a.size})')
    avoid = outliers(g, a.avoid)
    if a.dark:
        lum = g.astype(np.float32) @ np.array([.3, .59, .11], np.float32)
        d = lum <= np.quantile(lum, a.dark)
        for _ in range(2):
            d[1:] |= d[:-1].copy(); d[:-1] |= d[1:].copy(); d[:, 1:] |= d[:, :-1].copy(); d[:, :-1] |= d[:, 1:].copy()
        avoid |= d
    if a.warm:
        w = (g[..., 0].astype(int) - g[..., 1].astype(int)) >= a.warm
        for _ in range(3):
            w[1:] |= w[:-1].copy(); w[:-1] |= w[1:].copy(); w[:, 1:] |= w[:, :-1].copy(); w[:, :-1] |= w[:, 1:].copy()
        avoid |= w
    for box in filter(None, a.exclude.split(';')):
        x0, y0, x1, y1 = (int(v) for v in box.split(','))
        avoid[y0:y1, x0:x1] = True   # in the grid as grid mode saves it (edges already trimmed)
    tile = quilt(g, size, a.patch, a.overlap, np.random.default_rng(a.seed), avoid=avoid)
    tile = level(tile, a.level)
    tile = np.clip(tile * a.gain, 0, 255)
    if a.saturation != 1:
        grey = (tile @ np.array([.3, .59, .11], np.float32))[..., None]
        tile = np.clip(grey + (tile - grey) * a.saturation, 0, 255)
    Image.fromarray(np.round(tile).astype(np.uint8)).save(a.out)
    print('wrote', a.out)


if __name__ == '__main__':
    main()

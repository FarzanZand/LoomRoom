"""Sharp pixel book spines for a shelf insert (the painted books were blurry mush).

Every book face takes its own model colour (Synty's swatch: red, blue, green, brown, gold...) in three shades, with
a dark outline where one book meets the next, a light top edge, two light bands across tall spines (like titles and
bindings), and fine lines on the cream page edges. Wood faces of the insert are left as they are in <base.png>.

    python Tools/book_spines.py <base.png> <gbuffer.bin> <out.png>
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, __file__.rsplit('\\', 1)[0].rsplit('/', 1)[0])
from prop_structure import segments, lum  # noqa: E402

base_path, gb_path, out_path = sys.argv[1:4]
B = np.asarray(Image.open(base_path).convert('RGB')).astype(np.float32) / 255
H, W = B.shape[:2]
g = np.fromfile(gb_path, dtype=np.float32).reshape(H, W, 12)[::-1]
cov = g[..., 8] > .5; tri = g[..., 0].astype(int); nrm = g[..., 1:4]; pos = g[..., 4:7]; own = g[..., 9:12]
# A spine is two triangles; Synty's atlas sometimes gives them different swatches (a diagonal two-tone book). Pair
# each triangle with the coplanar neighbour it shares the longest edge with, and give the pair the colour of its
# larger triangle.
tt = np.where(cov, tri, -1)
shared = {}
for dy, dx in ((0, 1), (1, 0)):
    a_ = tt[:H - dy, :W - dx]; b_ = tt[dy:, dx:]
    na = nrm[:H - dy, :W - dx]; nb = nrm[dy:, dx:]
    m_ = (a_ >= 0) & (b_ >= 0) & (a_ != b_) & ((na * nb).sum(2) > .98)
    for p_, q_ in zip(a_[m_], b_[m_]):
        k_ = (min(p_, q_), max(p_, q_)); shared[k_] = shared.get(k_, 0) + 1
best = {}
for (p_, q_), c_ in shared.items():
    for u_, v_ in ((p_, q_), (q_, p_)):
        if c_ > best.get(u_, (None, 0))[1]: best[u_] = (v_, c_)
area = np.bincount(tt[cov], minlength=tri.max() + 1)
tri_col = {}
for t_ in np.unique(tt[cov]):
    tri_col[t_] = np.median(own[tt == t_], axis=0)
for t_, (u_, _) in best.items():
    if best.get(u_, (None, 0))[0] == t_:          # mutual partners: one spine
        big = t_ if area[t_] >= area[u_] else u_
        own[tt == t_] = tri_col[big]
lab, n = segments(cov, nrm, tri, np.round(own * 10) / 10)

out = B.copy()


def hh(*v):
    x = 1469598103934665603
    for k in v: x = ((x ^ (int(k) & 0xffffffff)) * 1099511628211) & 0xffffffffffffffff
    return (x % 100003) / 100003

wood = np.array([.28, .22, .16])
for s, sl in enumerate(ndimage.find_objects(lab + 1)):
    if sl is None: continue
    m = lab[sl] == s
    c = np.median(own[sl][m], axis=0)
    cream = c.min() > .45 and (c.max() - c.min()) < .2
    py = pos[sl][..., 1]
    y0, y1 = py[m].min(), py[m].max(); hgt = max(y1 - y0, 1e-4)
    t = (py - y0) / hgt
    # Outline only where this book meets something really different (another book, the shelf, the edge), never on
    # the seam between the two triangles of one spine.
    border = np.zeros_like(m)
    Y0, X0 = sl[0].start, sl[1].start
    for y, x in zip(*np.nonzero(m)):
        Y, X = y + Y0, x + X0
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            yy_, xx_ = Y + dy, X + dx
            if not (0 <= yy_ < H and 0 <= xx_ < W) or not cov[yy_, xx_]:
                border[y, x] = True; break
            if np.abs(own[yy_, xx_] - own[Y, X]).sum() > .1 or np.dot(nrm[yy_, xx_], nrm[Y, X]) < .8:
                border[y, x] = True; break
    yy, xx = np.nonzero(m)
    for y, x in zip(yy, xx):
        col = c.copy()
        if cream:
            # page edges: a darker line every other texel across the height
            ty = int(np.floor((py[y, x] - y0) / hgt * (m.sum() ** .5) * 1.0))
            col = c * (.82 if ty % 2 else 1.0)
        else:
            vertical = abs(nrm[sl][y, x, 1]) < .4
            if vertical and m.sum() > 20 and (.16 < t[y, x] < .23 or .77 < t[y, x] < .84):
                col = np.clip(c * 1.35 + np.array([.08, .06, 0]), 0, 1)        # binding bands
        if border[y, x]:
            col = c * .55 if t[y, x] < .9 else np.clip(c * 1.25, 0, 1)        # outline; light top edge
        else:
            Y, X = y + sl[0].start, x + sl[1].start
            # Old, handled books: the cloth varies in small steps from texel to texel, in clumps (2 x 1), never smooth.
            col = col * (1 + (hh(Y // 2, X, s, 1) - .5) * .22) * (1 + (hh(Y, X, s, 2) - .5) * .08)
            # Dirt and grime gather toward the bottom of each spine and its shelf.
            if t[y, x] < .3 and hh(Y // 2, X // 2, s, 3) < (.3 - t[y, x]) * 2.2:
                col = col * .72 + np.array([.05, .04, .03])
            # Faded, sun-bleached tops.
            if t[y, x] > .8 and hh(Y, X // 2, s, 4) < .35:
                col = col * .85 + np.array([.07, .06, .05])
            # A few stains and scuffs per book, a couple of texels each.
            if hh(Y // 2, X // 2, s, 5) < .07:
                col = col * .6
            elif hh(Y, X, s, 6) < .03:
                col = np.clip(col * 1.25 + .05, 0, 1)
        # Grey dust and dirt over everything, a little.
        col = col * .9 + np.array([.04, .035, .03])
        out[sl][y, x] = np.clip(col, 0, 1)

idx = ndimage.distance_transform_edt(~cov, return_distances=False, return_indices=True)
out[~cov] = out[idx[0], idx[1]][~cov]
Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8)).save(out_path)
print('books drawn', n, 'faces')

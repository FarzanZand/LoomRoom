"""Paint faces that look along one axis (a chest's ends) with the painting of the faces that look along another (its
front and back), so the sides match: each texel takes the colour of the front texel of the same model colour at the
same height and the same relative position across the face.

    python Tools/prop_wrap.py <painted.png> <gbuffer.bin> <out.png> --to x --from z
"""
import argparse
import numpy as np
from PIL import Image
from scipy.spatial import cKDTree
from scipy import ndimage

ap = argparse.ArgumentParser()
ap.add_argument('painted'); ap.add_argument('gbuffer'); ap.add_argument('out')
ap.add_argument('--fallback', default='', help='image whose texels go on parts with nothing like them on the source faces');
ap.add_argument('--to', default='x'); ap.add_argument('--from', dest='src', default='z')
a = ap.parse_args()
P = np.asarray(Image.open(a.painted).convert('RGB')).astype(np.float32) / 255
H, W = P.shape[:2]
g = np.fromfile(a.gbuffer, dtype=np.float32).reshape(H, W, 12)[::-1]
cov = g[..., 8] > .5; nrm = g[..., 1:4]; pos = g[..., 4:7]; own = g[..., 9:12]
ti, si = 'xyz'.index(a.to), 'xyz'.index(a.src)
# across-axis for each: the horizontal axis lying in the face (not y, not its normal)
t_across = [k for k in (0, 2) if k != ti][0]; s_across = [k for k in (0, 2) if k != si][0]
dst = cov & (np.abs(nrm[..., ti]) > .7); src = cov & (np.abs(nrm[..., si]) > .7)
# The base trim is shaped differently on the ends than on the front: it takes the fallback, not stretched front rows.
base = cov & (np.abs(nrm[..., ti]) > .7) & (pos[..., 1] < .16)
dst &= ~base
key = np.round(own * 8).astype(int); key = key[..., 0] * 100 + key[..., 1] * 10 + key[..., 2]
out = P.copy(); done = 0
F = np.asarray(Image.open(a.fallback).convert('RGB')).astype(np.float32) / 255 if a.fallback else None
for k in np.unique(key[dst]):
    d = dst & (key == k); s = src & (key == k)
    # the front's lock and hasps stay on the front (rusty orange texels are not copied round the sides)
    hasp = (P[..., 0] > P[..., 1] * 1.45) & (P[..., 0] > .35)
    s = s & ~ndimage.binary_dilation(hasp, iterations=7)
    if s.sum() < 10:                              # nothing like it on the front: the fallback (generated) version
        if a.fallback: out[d] = F[d]
        continue
    sy, sx = pos[..., 1][s], pos[..., s_across][s]
    dy, dx = pos[..., 1][d], pos[..., t_across][d]
    sxn = (sx - sx.min()) / max(np.ptp(sx), 1e-4); dxn = (dx - dx.min()) / max(np.ptp(dx), 1e-4)
    tree = cKDTree(np.stack([sy * 4, sxn], 1))
    _, idx = tree.query(np.stack([dy * 4, dxn], 1))
    out[d] = P[s][idx]; done += d.sum()
if F is not None: out[base] = F[base]
Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8)).save(a.out)
print('repainted', int(done), 'texels')

"""Keep a prop's Meshy painting and replace only what is broken in it with the generated version (prop_structure.py).

Broken, per texel or per face:
  * background specks: pale, colourless texels (the painter's grey background or white highlights) where the model's
    own colour is not pale grey
  * stray bright specks: much brighter than the texels around them on the same face
  * smeared or unpainted faces: a face whose painting is one flat block, or big blocks of one colour (the painter only
    saw it edge-on), measured by how few colour changes it has per texel
Replaced regions are grown by one texel so no half-specks are left.

    python Tools/prop_mix.py <meshy.png> <generated.png> <gbuffer.bin> <out.png> [--all-generated] [--report]
"""
import argparse
import numpy as np
from PIL import Image
from scipy import ndimage


def lum(c):
    return c[..., 0] * .299 + c[..., 1] * .587 + c[..., 2] * .114


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('meshy'); ap.add_argument('generated'); ap.add_argument('gbuffer'); ap.add_argument('out')
    ap.add_argument('--all-generated', action='store_true')
    ap.add_argument('--axis-faces', default='', help='x, y or z: faces pointing along this axis of the model take the generated version (a side the painter only saw edge-on)')
    ap.add_argument('--flat', type=float, default=.12, help='faces with fewer colour changes per texel than this are smeared')
    a = ap.parse_args()
    Mp = np.asarray(Image.open(a.meshy).convert('RGB')).astype(np.float32) / 255
    G = np.asarray(Image.open(a.generated).convert('RGB')).astype(np.float32) / 255
    H, W = Mp.shape[:2]
    g = np.fromfile(a.gbuffer, dtype=np.float32).reshape(H, W, 12)[::-1]
    cov = g[..., 8] > .5; tri = g[..., 0].astype(int); nrm = g[..., 1:4]; own = g[..., 9:12]
    if a.all_generated:
        Image.fromarray((G * 255 + .5).astype(np.uint8)).save(a.out); print('all generated'); return

    L = lum(Mp); mx = Mp.max(2); mn = Mp.min(2); sat = (mx - mn) / (mx + 1e-6)
    own_sat = (own.max(2) - own.min(2)) / (own.max(2) + 1e-6)
    bad = np.zeros_like(cov)
    # Background grey / white: colourless and pale where the model is coloured (wood, cloth).
    bad |= cov & (sat < .12) & (L > .32) & (own_sat > .2)
    # Blue-grey cast of the painter's background on wood.
    bad |= cov & (Mp[..., 2] > Mp[..., 0] + .02) & (own_sat > .2)
    # Stray bright specks.
    local = ndimage.median_filter(L, size=5)
    bad |= cov & (L > local * 1.55 + .05)

    # Smeared faces: per triangle, the share of texels that differ from their right or lower neighbour.
    diff = np.zeros_like(cov)
    diff[:, :-1] |= np.abs(Mp[:, 1:] - Mp[:, :-1]).sum(2) > .03
    diff[:-1] |= np.abs(Mp[1:] - Mp[:-1]).sum(2) > .03
    nt = tri.max() + 1; tt = np.clip(tri, 0, None)
    cnt = np.bincount(tt[cov], minlength=nt).astype(float)
    chg = np.bincount(tt[cov & diff], minlength=nt).astype(float)
    rate = chg / np.maximum(cnt, 1)
    smeared_tri = (cnt >= 30) & (rate < a.flat)
    smeared = cov & smeared_tri[tt]
    bad |= smeared

    if a.axis_faces:
        ax = 'xyz'.index(a.axis_faces)
        bad |= cov & (np.abs(nrm[..., ax]) > .7)
    bad = ndimage.binary_dilation(bad, iterations=1) & cov
    # Replaced texels take the painting's colours: per model colour, the generated texels are scaled so their median
    # matches the painting's median on the texels of that colour that were kept.
    Gm = G.copy()
    key = np.round(own * 8).astype(int); key = key[..., 0] * 100 + key[..., 1] * 10 + key[..., 2]
    for k in np.unique(key[bad]):
        keep = cov & ~bad & (key == k); rep = bad & (key == k)
        if keep.sum() < 20: keep = cov & ~bad
        if keep.sum() < 20: continue
        ratio = np.median(Mp[keep], axis=0) / np.maximum(np.median(G[keep | rep], axis=0), 1e-3)
        Gm[rep] = np.clip(G[rep] * ratio, 0, 1)
    out = Mp.copy(); out[bad] = Gm[bad]
    print(f'replaced {bad.sum() / max(1, cov.sum()):.1%} of the painted texels '
          f'(smeared faces {smeared.sum() / max(1, cov.sum()):.1%})')
    Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8)).save(a.out)


if __name__ == '__main__':
    main()

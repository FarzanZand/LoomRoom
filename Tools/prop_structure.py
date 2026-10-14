"""Hand-made pixel structure for a restyled prop, from its own geometry (the dungeon props looked AI-made: blotches
that ignored the model, too many shades, details that didn't line up with the real edges).

Every texel's colour comes from rules a pixel artist would follow, read off the model (PixelRestyle.BakeGBuffer):
  * faces: texels are grouped into flat faces (connected, same facing); each face is one plank, band or panel
  * materials: faces are grouped by their colour in the approved painting; each material gets a ramp of 5 shades taken
    from the painting (so hues stay: rust bands, oak staves), and every texel uses one of those 5 shades
  * edges: the top edge of a face takes the light shade, the bottom edge the dark one (light from above), with a few
    single chipped texels on top edges
  * grain: wood faces get grain lines along their long side, broken by hand-like gaps
  * creases and the base: darker where the model is enclosed (ambient occlusion) and along the bottom
  * small dark features in the painting (nail heads, rivets) are kept, as the darkest shade

    python Tools/prop_structure.py <painted.png> <gbuffer.bin> <out.png> [--wood-sat 0.55] [--grain 3]
"""
import argparse
import numpy as np
from PIL import Image
from scipy import ndimage


def lum(c):
    return c[..., 0] * .299 + c[..., 1] * .587 + c[..., 2] * .114


def h(*a):
    """Stable hash of integers to 0..1."""
    x = 1469598103934665603
    for v in a:
        x = ((x ^ (int(v) & 0xffffffff)) * 1099511628211) & 0xffffffffffffffff
    return (x % 100003) / 100003


def segments(cov, nrm, tri):
    """Flat faces: 4-connected texels whose face normals agree within ~20 degrees."""
    H, W = cov.shape
    lab = -np.ones((H, W), int); n = 0
    for y0 in range(H):
        for x0 in range(W):
            if not cov[y0, x0] or lab[y0, x0] >= 0: continue
            stack = [(y0, x0)]; lab[y0, x0] = n; ref = nrm[y0, x0]
            while stack:
                y, x = stack.pop()
                for yy, xx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                    if 0 <= yy < H and 0 <= xx < W and cov[yy, xx] and lab[yy, xx] < 0 and np.dot(nrm[yy, xx], ref) > .94:
                        lab[yy, xx] = n; stack.append((yy, xx))
            n += 1
    return lab, n


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('painted'); ap.add_argument('gbuffer'); ap.add_argument('out')
    ap.add_argument('--wood-sat', type=float, default=.5, help='materials less saturated than this get wood grain')
    ap.add_argument('--grain', type=int, default=4, help='texels between grain lines')
    ap.add_argument('--materials', type=float, default=.03, help='colour distance that separates materials')
    a = ap.parse_args()

    P = np.asarray(Image.open(a.painted).convert('RGB')).astype(np.float32) / 255
    H, W = P.shape[:2]
    g = np.fromfile(a.gbuffer, dtype=np.float32).reshape(H, W, 12)[::-1]   # texture rows run bottom-up
    cov = g[..., 8] > .5; tri = g[..., 0].astype(int); nrm = g[..., 1:4]; pos = g[..., 4:7]; ao = g[..., 7]; own = g[..., 9:12]

    lab, n = segments(cov, nrm, tri)
    # Each face's colour in the painting, then faces grouped into materials by that colour.
    med = np.zeros((n, 3)); size = np.zeros(n, int)
    for s, sl in enumerate(ndimage.find_objects(lab + 1)):
        if sl is None: continue
        m = lab[sl] == s; size[s] = m.sum(); med[s] = np.median(own[sl][m], axis=0)
    order = np.argsort(-size); mat = -np.ones(n, int); centres = []
    for s in order:
        if size[s] == 0: continue
        for k, c in enumerate(centres):
            if np.abs(med[s] - c).sum() < a.materials * 3: mat[s] = k; break
        else:
            mat[s] = len(centres); centres.append(med[s])
    M = mat[np.clip(lab, 0, None)]; M[~cov] = -1
    # A ramp of 5 shades per material from the painting's own colours at set brightness steps.
    ramps = []
    for k in range(len(centres)):
        c = P[M == k]; L = lum(c); ramp = []
        for q in (6, 26, 50, 74, 94):
            t = np.percentile(L, q); near = np.abs(L - t) < max(.02, (L.max() - L.min()) * .05)
            ramp.append(np.median(c[near], axis=0) if near.any() else c.mean(0))
        # Hues from the painting, brightness in fixed steps from the middle shade (the painting's darkest stains made
        # the bottom shade nearly black).
        mid = lum(ramp[2]); steps = (.62, .81, 1.0, 1.16, 1.32)
        ramp = [c * (mid * f / max(lum(c), 1e-3)) for c, f in zip(ramp, steps)]
        ramps.append(np.clip(np.array(ramp), 0, 1))
    # Metal or wood from the model's own colour: Synty paints metal grey, wood brown. `sat` below is that test
    # (values >= wood_sat count as metal).
    own_sat = [float((c.max() - c.min()) / (c.max() + 1e-6)) for c in centres]
    sat = [1.0 if os_ < .22 else 0.0 for os_ in own_sat]
    print('materials', len(ramps), 'own colours', [tuple(np.round(c, 2)) for c in centres], 'metal', [s_ > .5 for s_ in sat])

    shade = np.full((H, W), 2, int)
    # Large shapes from the painting, kept coarse: the face's own light/dark half-step, no noise.
    # Creases and the base.
    shade[cov & (ao < .62)] -= 1
    shade[cov & (ao < .4)] -= 1
    shade[cov & (pos[..., 1] < .06)] -= 1

    # Edges of each face: distance to the face's border, and which way is up across the texture.
    for s, sl in enumerate(ndimage.find_objects(lab + 1)):
        if sl is None or size[s] < 4: continue
        m = lab[sl] == s
        if m.shape[0] < 3 or m.shape[1] < 3: continue
        d = ndimage.distance_transform_cdt(m, metric='taxicab')
        py = pos[sl][..., 1] + pos[sl][..., 0] * .15 + pos[sl][..., 2] * .1
        gy, gx = np.gradient(np.where(m, py, ndimage.grey_dilation(np.where(m, py, -1), size=3)))
        gy = np.nan_to_num(gy); gx = np.nan_to_num(gx)
        oy, ox = np.gradient(-d.astype(float))
        up = (gy * oy + gx * ox) > 0
        ys, xs = np.nonzero(m & (d == 1))
        for y, x in zip(ys, xs):
            Y, X = y + sl[0].start, x + sl[1].start
            if up[y, x]:
                shade[Y, X] = 3 + (1 if h(s, y, x) < .07 else 0)
            else:
                shade[Y, X] = min(shade[Y, X], 1)
        # Wood grain along the face's long side.
        k = mat[s]
        if sat[k] < a.wood_sat and size[s] > 30:
            yy, xx = np.nonzero(m)
            cy, cx = yy.mean(), xx.mean()
            cov2 = np.cov(np.vstack([yy - cy, xx - cx]))
            ev, evec = np.linalg.eigh(cov2); along = evec[:, 1]
            # Planks run along a texture axis (islands are unwrapped square to the texture): snap to the nearest one,
            # and a squarish face takes the vertical one rather than a diagonal.
            ang = np.arctan2(along[1], along[0]); snap = np.round(ang / (np.pi / 2)) * (np.pi / 2)
            if ev[1] < ev[0] * 1.6: snap = 0.0
            along = np.array([np.cos(snap), np.sin(snap)]); across = np.array([-along[1], along[0]])
            for y, x in zip(yy, xx):
                if d[y, x] <= 1: continue
                ac = (y - cy) * across[0] + (x - cx) * across[1]; al = (y - cy) * along[0] + (x - cx) * along[1]
                line = int(np.floor(ac + 100.5))
                if line % a.grain != int(h(s, 7) * a.grain): continue
                runlen = 5 + int(h(s, line, 1) * 11)            # each grain line its own dash length
                run = int(np.floor((al + 1000 + h(s, line, 2) * 20) / runlen))
                if h(s, line, run) < .35: continue          # gaps in the grain
                Y, X = y + sl[0].start, x + sl[1].start
                shade[Y, X] = min(shade[Y, X], 1)
            # One knot on some planks.
            if h(s, 11) < .35:
                i = int(h(s, 12) * len(yy)); ky, kx = yy[i], xx[i]
                if d[ky, kx] > 2:
                    for dy, dx in ((0, 0), (0, 1), (1, 0)):
                        if ky + dy < m.shape[0] and kx + dx < m.shape[1] and m[ky + dy, kx + dx]: shade[ky + dy + sl[0].start, kx + dx + sl[1].start] = 0

    # Wear by rules, the way a pixel artist places it.
    BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.
    ys, xs = np.nonzero(cov)
    for y, x in zip(ys, xs):
        k = M[y, x]; py = pos[y, x, 1]
        # Damp and dirt up from the base, dithered so it fades in steps.
        # in clumps (2 x 2 cells of a hashed grid), denser toward the base
        if py < .14 and h(y // 2, x // 2, k, 31) < (.14 - py) / .14 * .55:
            shade[y, x] = max(1, shade[y, x] - 1)
    for s, sl in enumerate(ndimage.find_objects(lab + 1)):
        if sl is None or size[s] < 12: continue
        m = lab[sl] == s; k = mat[s]
        yy, xx = np.nonzero(m)
        if sat[k] >= a.wood_sat:
            # Metal: rivets along the band's middle, rust in its lower part.
            cy, cx = yy.mean(), xx.mean()
            ev, evec = np.linalg.eigh(np.cov(np.vstack([yy - cy, xx - cx]))); along = evec[:, 1]; across = evec[:, 0]
            al = (yy - cy) * along[0] + (xx - cx) * along[1]; ac = (yy - cy) * across[0] + (xx - cx) * across[1]
            for i in range(len(yy)):
                Y, X = yy[i] + sl[0].start, xx[i] + sl[1].start
                if abs(ac[i]) < .6 and int(round(al[i])) % 12 == 0:
                    shade[Y, X] = 0
                    if 0 < Y: shade[Y - 1, X] = max(shade[Y - 1, X], 4) if lab[Y - 1, X] == s else shade[Y - 1, X]
                elif h(s, int(al[i] // 3), int(ac[i] // 2), 41) < (.16 if pos[Y, X, 1] < .5 else .07):
                    shade[Y, X] = max(0, shade[Y, X] - 1)
        else:
            # Wood: a couple of stains per plank, small and lower down, and the odd scratch.
            for j in range(int(h(s, 21) * 3)):
                i = int(h(s, 22, j) * len(yy)); sy, sx = yy[i], xx[i]
                if pos[sy + sl[0].start, sx + sl[1].start, 1] > .6: continue
                for dy in range(-1, 2):
                    for dx in range(-2, 3):
                        Y, X = sy + dy + sl[0].start, sx + dx + sl[1].start
                        if 0 <= Y < H and 0 <= X < W and lab[Y, X] == s and h(s, j, dy, dx) < .7:
                            shade[Y, X] = max(0, shade[Y, X] - 1)

    # Moss in low creases of wood, in the painting's own green (one accent, as the creatures have).
    hsv_g = (P[..., 1] > P[..., 0] * .98) & (P[..., 1] > P[..., 2] * 1.1) & cov
    print('green texels', int(hsv_g.sum()))
    moss = np.median(P[hsv_g], axis=0) if hsv_g.sum() > 30 else None
    mossmask = np.zeros_like(cov)
    if moss is not None:
        for y, x in zip(ys, xs):
            if sat[M[y, x]] < a.wood_sat and pos[y, x, 1] < .35 and h(y // 2, x // 2, 5) < (.35 - pos[y, x, 1]) * 1.6:
                mossmask[y, x] = True

    # Small dark features in the painting (nail heads, rivets): kept, as the darkest shade.
    Lp = lum(P)
    for k, r in enumerate(ramps):
        dark = (M == k) & ((Lp < lum(r[0]) * .85) | (Lp < .16))
        dl, dn = ndimage.label(dark)
        if dn:
            sizes = ndimage.sum(dark, dl, range(1, dn + 1))
            small = np.isin(dl, 1 + np.flatnonzero((sizes >= 2) & (sizes <= 60)))
            pass

    shade = np.clip(shade, 0, 4)
    # Only nail heads and rivets reach the darkest shade on large faces; creases stop one step above it.
    nailmask = np.zeros_like(cov)
    Lp2 = lum(P)
    for k, r in enumerate(ramps):
        dark = (M == k) & ((Lp2 < lum(r[0]) * .85) | (Lp2 < .16))
        dl, dn = ndimage.label(dark)
        if dn:
            sizes = ndimage.sum(dark, dl, range(1, dn + 1))
            # Nail heads are compact dots (about as wide as tall, mostly filled) and never the dirt along the base.
            for j, sl2 in enumerate(ndimage.find_objects(dl)):
                if sl2 is None or not (3 <= sizes[j] <= 40): continue
                hh, ww = sl2[0].stop - sl2[0].start, sl2[1].stop - sl2[1].start
                comp = dl[sl2] == j + 1
                if max(hh, ww) > 2.2 * min(hh, ww) or comp.sum() < .5 * hh * ww: continue
                if np.median(pos[sl2][..., 1][comp]) < .1: continue
                cc = P[sl2][comp].mean(0)
                if (cc.max() - cc.min()) / (cc.max() + 1e-6) > .3: continue   # brown dirt, not an iron nail head
                nailmask[sl2] |= comp
    out = P.copy()
    nailcol = np.array([.09, .08, .08])
    for k, r in enumerate(ramps):
        m = M == k; out[m] = r[shade[m]]
    out[nailmask] = nailcol
    # Each plank or band a slightly different tone (boards are never all one colour).
    tone = np.array([1 + (h(s, 3) - .5) * .14 for s in range(n)])
    out[cov] *= tone[lab[cov]][:, None]
    if moss is not None:
        mm = mossmask & cov
        out[mm] = moss * np.where(shade[mm] <= 1, .8, 1.0)[:, None]
    # Unpainted texels (padding) take the nearest painted one, so mipmaps don't pull in stray colours.
    idx = ndimage.distance_transform_edt(~cov, return_distances=False, return_indices=True)
    out = out[idx[0], idx[1]]
    Image.fromarray((np.clip(out, 0, 1) * 255 + .5).astype(np.uint8)).save(a.out)
    print('wrote', a.out, W, 'x', H, 'faces', n)


if __name__ == '__main__':
    main()

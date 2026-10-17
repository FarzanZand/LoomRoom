"""Hand-made pixel shading for bone and stone props (skull piles, statues, tombs): the prop_structure.py approach (every
texel from rules read off the model, nothing from a painting) with detail that fits the material instead of wood grain.
The model is read from PixelRestyle.BakeGBuffer: per texel its outward normal, place on the model, how enclosed it is
(ambient occlusion) and the Synty original's own colour there (sampled per texel, so detail Synty draws in its texture,
like a skull's eye sockets, nose and teeth, comes through).

  * colour: Synty's colour, muted toward the dungeon palette, its brightness stepped into 5 shades (no gradients)
  * creases: enclosed texels step darker and take a grime brown; the base one step darker
  * edges: a texel whose neighbour is two steps darker is a lit edge: one step lighter
  * chips: sparse single lighter texels on open, upward faces
  * bone: thin hairline cracks (short wandering dark lines); no grain; drawn features (sockets, teeth) darkest
  * stone: grain (speckles a step up or down, by place on the model), more cracks, and moss in clumps on enclosed
    upward faces (--moss)

    python Tools/prop_organic.py <gbuffer.bin> <width> <height> <out.png> --material bone|stone [--moss 0.3] [--sat 0.7]
"""
import argparse
import colorsys
import numpy as np
from PIL import Image
from scipy import ndimage


def h(*a):
    x = 1469598103934665603
    for v in a:
        x = ((x ^ (int(v) & 0xffffffff)) * 1099511628211) & 0xffffffffffffffff
    return (x % 100003) / 100003


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('gbuffer'); ap.add_argument('width', type=int); ap.add_argument('height', type=int); ap.add_argument('out')
    ap.add_argument('--material', choices=['bone', 'stone'], required=True)
    ap.add_argument('--sat', type=float, default=.7, help='colour strength against the Synty colours')
    ap.add_argument('--bright', type=float, default=.9, help='overall brightness against the Synty colours')
    ap.add_argument('--moss', type=float, default=0, help='0-1 (stone): share of enclosed upward faces with moss')
    ap.add_argument('--cracks', type=float, default=1, help='how many cracks (1 = the default number for the material)')
    ap.add_argument('--steps', type=int, default=5)
    a = ap.parse_args()
    H, W = a.height, a.width
    g = np.fromfile(a.gbuffer, dtype=np.float32).reshape(H, W, 12)[::-1]
    cov = g[..., 8] > .5; nrm = g[..., 1:4]; pos = g[..., 4:7]; ao = np.clip(g[..., 7], 0, 1); own = np.clip(g[..., 9:12], 0, 1)
    up = nrm[..., 1]

    hsv = np.zeros_like(own)
    for y, x in zip(*np.nonzero(cov)):
        hsv[y, x] = colorsys.rgb_to_hsv(*own[y, x])
    hue, sat, val = hsv[..., 0], hsv[..., 1] * a.sat, hsv[..., 2]
    top = np.percentile(val[cov], 95)
    # Value stepped into bands over the whole model, so neighbouring texels of one colour share a shade.
    step = np.clip(np.floor(val / max(top, 1e-3) * a.steps * .999), 0, a.steps - 1).astype(int)

    # What Synty draws dark (a skull's sockets, nose and the gaps between teeth) is the darkest shade, crisp.
    drawn = cov & (val < .6 * top)
    step[drawn] = 1
    step[ao < .45] -= 1
    step[ao < .25] -= 1
    step[pos[..., 1] < .05] -= 1
    grime = cov & (ao < .4)
    edge = np.zeros_like(cov)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nb = np.roll(np.roll(step, dy, 0), dx, 1); ncov = np.roll(np.roll(cov, dy, 0), dx, 1)
        edge |= (nb <= step - 2) & ncov
    step[edge & (up > -.2)] += 1
    for y, x in zip(*np.nonzero(cov & (ao > .6) & (up > .2))):
        if h(x, y, 11) < .025: step[y, x] += 1

    rng = np.random.default_rng(3)
    if a.material == 'stone':
        cell = np.floor(pos * 140).astype(int)
        for y, x in zip(*np.nonzero(cov)):
            r = h(cell[y, x, 0], cell[y, x, 1], cell[y, x, 2], 5)
            if r < .16: step[y, x] -= 1
            elif r > .9: step[y, x] += 1
    n_cracks = int((cov.sum() / 900) * (1.5 if a.material == 'stone' else .8) * a.cracks)
    ys, xs = np.nonzero(cov & (ao > .55))
    crack = np.zeros_like(cov)
    moves = [(0, 1), (1, 0), (0, -1), (-1, 0)]
    for i in range(n_cracks):
        if len(ys) == 0: break
        k = rng.integers(len(ys)); y, x = int(ys[k]), int(xs[k])
        d = int(rng.integers(4)); length = int(rng.integers(3, 8 if a.material == 'stone' else 6))
        for _ in range(length):
            if not (0 <= y < H and 0 <= x < W) or not cov[y, x]: break
            crack[y, x] = True
            if rng.random() < .35: d = (d + int(rng.choice([-1, 1]))) % 4
            y += moves[d][0]; x += moves[d][1]
    step[crack] = np.minimum(step[crack], 0)

    step = np.clip(step, 0, a.steps - 1)
    step[drawn & ~crack] = np.maximum(step[drawn & ~crack], 1)   # drawn features dark brown, never black
    v_out = (step + .55) / a.steps * top * a.bright
    out = np.zeros((H, W, 3))
    for y, x in zip(*np.nonzero(cov)):
        hh, ss, vv = hue[y, x], sat[y, x], min(1, v_out[y, x])
        if grime[y, x] or crack[y, x]:      # creases and cracks lean to a dirty brown
            hh, ss = .08, max(ss, .3)
        out[y, x] = colorsys.hsv_to_rgb(hh, min(1, ss), vv)
    if a.moss > 0:
        moss = cov & (up > .4) & (ao < .55) & np.array([[h(x // 4, y // 4, 3) < a.moss for x in range(W)] for y in range(H)])
        mramp = np.array([(.18, .24, .12), (.24, .32, .15), (.31, .40, .19), (.38, .47, .23), (.45, .54, .27)])
        out[moss] = mramp[np.clip(step[moss], 0, 4)]
    img = out
    if (~cov).any():
        _, ind = ndimage.distance_transform_edt(~cov, return_indices=True)
        img = img[ind[0], ind[1]]
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(a.out)
    print(f'{a.out}: {a.material}, {n_cracks} cracks')


if __name__ == '__main__':
    main()

"""Hand-made pixel look for painted textures: a small palette and no stray specks.

AI paintings read as AI because every texel is a slightly different colour (noise, smeared gradients). Pixel art uses a
handful of shades per material. This quantises a texture to `colours` shades (k-means in a perceptual-ish space, picked
per texture), then removes specks: a texel whose colour appears in fewer than 2 of its 8 neighbours takes the most common
neighbour colour (lines one texel wide survive because they continue along the line).

    python Tools/pixel_cleanup.py <in.png> <out.png> [colours]
"""
import sys
import numpy as np
from PIL import Image


def kmeans(x, k, iters=25, seed=3):
    rng = np.random.default_rng(seed)
    # k-means++ start
    c = [x[rng.integers(len(x))]]
    for _ in range(1, k):
        d = np.min(((x[:, None] - np.array(c)[None]) ** 2).sum(2), 1)
        c.append(x[rng.choice(len(x), p=d / d.sum())])
    c = np.array(c)
    for _ in range(iters):
        lab = np.argmin(((x[:, None] - c[None]) ** 2).sum(2), 1)
        for j in range(k):
            m = lab == j
            if m.any(): c[j] = x[m].mean(0)
    return c


def cleanup(src, dst, k=10):
    im = Image.open(src).convert('RGBA'); a = np.asarray(im).astype(np.float32) / 255
    cov = a[..., 3] > .5; rgb = a[..., :3]
    # Weight brightness more than hue so shading steps survive.
    w = np.array([.5, 1.0, .4])
    px = rgb[cov] * w
    sample = px[np.random.default_rng(1).choice(len(px), min(len(px), 20000), replace=False)]
    cent = kmeans(sample, k)
    lab = np.full(cov.shape, -1)
    lab[cov] = np.argmin(((px[:, None] - cent[None]) ** 2).sum(2), 1)
    pal = cent / w
    # Speck removal, twice.
    H, W = lab.shape
    for _ in range(2):
        out = lab.copy()
        for y in range(H):
            for x in range(W):
                if lab[y, x] < 0: continue
                nb = [lab[yy, xx] for yy in range(max(0, y - 1), min(H, y + 2)) for xx in range(max(0, x - 1), min(W, x + 2)) if (yy, xx) != (y, x) and lab[yy, xx] >= 0]
                if not nb: continue
                same = sum(1 for v in nb if v == lab[y, x])
                if same < 2:
                    vals, cnt = np.unique(nb, return_counts=True); out[y, x] = vals[np.argmax(cnt)]
        lab = out
    res = a.copy()
    res[..., :3][cov] = pal[lab[cov]]
    Image.fromarray((np.clip(res, 0, 1) * 255 + .5).astype(np.uint8), 'RGBA').save(dst)


if __name__ == '__main__':
    cleanup(sys.argv[1], sys.argv[2], int(sys.argv[3]) if len(sys.argv) > 3 else 10)

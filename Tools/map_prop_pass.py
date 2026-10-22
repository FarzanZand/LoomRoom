"""Map props: take the per-texel grain out of the baked pixel textures so each prop reads as a few flat tones
(lit side, body, shade), like the room's furniture. Usage: python Tools/map_prop_pass.py <orig dir> <out dir> [k] [only substring]
Every PNG under <orig dir> (keeping its sub folders) is written to the same path under <out dir>."""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

src, dst = sys.argv[1], sys.argv[2]
K = int(sys.argv[3]) if len(sys.argv) > 3 else 5
ONLY = sys.argv[4] if len(sys.argv) > 4 else ""

def flatten(a):
    rgb = a[..., :3].astype(np.float32) / 255
    H, W, _ = rgb.shape
    # A 3x3 median, wrapping like the texture: grain goes, edges between colours stay.
    sm = np.stack([ndimage.median_filter(rgb[..., c], size=3, mode="wrap") for c in range(3)], -1)
    pts = sm.reshape(-1, 3)
    k = min(K, len(np.unique((pts * 31).astype(int), axis=0)))
    if k < 2: return a
    rng = np.random.default_rng(3)
    cent = pts[rng.choice(len(pts), k, replace=False)]
    for _ in range(20):
        lab = ((pts[:, None] - cent[None]) ** 2).sum(-1).argmin(1)
        for i in range(k):
            m = lab == i
            if m.any(): cent[i] = pts[m].mean(0)
    out = cent[lab].reshape(H, W, 3)
    res = a.copy()
    res[..., :3] = np.clip(out * 255, 0, 255).astype(np.uint8)
    return res

n = 0
for root, _, files in os.walk(src):
    for f in files:
        if not f.endswith(".png") or ONLY not in f: continue
        p = os.path.join(root, f)
        rel = os.path.relpath(p, src)
        q = os.path.join(dst, rel)
        os.makedirs(os.path.dirname(q), exist_ok=True)
        im = Image.open(p)
        mode = im.mode
        a = np.asarray(im.convert("RGBA"))
        Image.fromarray(flatten(a), "RGBA").convert(mode if mode in ("RGB", "RGBA") else "RGBA").save(q)
        n += 1
print(n, "textures flattened")

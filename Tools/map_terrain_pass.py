"""Campaign map terrain: from noisy procedural paint to flat, drawn pixel art like the room's rug and floor.
Usage: python Tools/map_terrain_pass.py <in.png> <out.png> [palette_size] [mode_radius] [outline 0/1] [tufts 0/1] [ink 0/1]
    [least blob texels] [snow saturation]
The map's terrain texture was made with: 22 3 1 1 0 350 0.45, from the generated texture CampaignMapBuilder.Build() writes.
"""
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

src, dst = sys.argv[1], sys.argv[2]
K = int(sys.argv[3]) if len(sys.argv) > 3 else 22
R = int(sys.argv[4]) if len(sys.argv) > 4 else 2
OUTLINE = int(sys.argv[5]) if len(sys.argv) > 5 else 1
TUFTS = int(sys.argv[6]) if len(sys.argv) > 6 else 1
INK = int(sys.argv[7]) if len(sys.argv) > 7 else 1
MINBLOB = int(sys.argv[8]) if len(sys.argv) > 8 else 0
SNOWSAT = float(sys.argv[9]) if len(sys.argv) > 9 else 1.0

im = Image.open(src).convert("RGBA")
a = np.asarray(im).astype(np.float32) / 255.0
rgb, alpha = a[..., :3], a[..., 3]
H, W, _ = rgb.shape
luma = rgb @ np.array([.299, .587, .114], np.float32)

# 1. Smooth the noise away before choosing colours: a median takes out speckle and dither but keeps edges.
smooth = np.stack([ndimage.gaussian_filter(ndimage.median_filter(rgb[..., c], size=7), 1.4) for c in range(3)], -1)

# 2. A small palette (k-means on the smoothed colours, in a perceptual-ish space).
pts = smooth.reshape(-1, 3)
rng = np.random.default_rng(1)
cent = pts[rng.choice(len(pts), K, replace=False)]
for _ in range(25):
    d = ((pts[:, None, :] - cent[None, :, :]) ** 2).sum(-1)
    lab = d.argmin(1)
    for k in range(K):
        m = lab == k
        if m.any(): cent[k] = pts[m].mean(0)
labels = lab.reshape(H, W)

# 3. Ink: thin dark lines in the original (roads drawn on the ground, mountain marks) stay as they are.
local = ndimage.uniform_filter(luma, size=5)
ink = (luma < local - .12) & (luma < .32)
# Only real marks count as ink: long thin dark runs (mountain peaks, crags), never the dither specks along wood edges.
lab_ink, n_ink = ndimage.label(ink, structure=np.ones((3, 3)))
if n_ink:
    sizes = ndimage.sum(ink, lab_ink, range(1, n_ink + 1))
    keep = np.zeros(n_ink + 1, bool); keep[1:] = sizes >= 10
    ink = keep[lab_ink]
# Roads painted on the ground: grey (unsaturated), darker than what is around them.
sat = rgb.max(-1) - rgb.min(-1)
road = (sat < .07) & (luma > .12) & (luma < .42) & (luma < ndimage.uniform_filter(luma, size=9) - .02)
road = ndimage.binary_closing(road, iterations=1) & (sat < .1)
road = ndimage.binary_opening(road, iterations=1)
road[:] = False   # the real roads are meshes on top; painted ones are not kept

# 4. Majority filter on the labels: every texel takes the most common colour around it, so borders are crisp steps
# and lone specks vanish.
def mode_filter(lbl, r):
    out = lbl.copy()
    best = np.zeros(lbl.shape, np.int32)
    for k in range(K):
        cnt = ndimage.uniform_filter((lbl == k).astype(np.float32), size=2 * r + 1)
        upd = cnt > best
        out[upd] = k
        best = np.where(upd, cnt, best)
    return out
for _ in range(3):
    labels = mode_filter(labels, R)

# 4b. Blobs smaller than MINBLOB texels (spots in the forest, islands in the snow) take the colour around them.
if MINBLOB:
    for _ in range(3):
        changed = 0
        for k in range(K):
            comp, n = ndimage.label(labels == k)
            if not n: continue
            sizes = ndimage.sum(np.ones_like(comp), comp, range(1, n + 1))
            for i in np.nonzero(sizes < MINBLOB)[0] + 1:
                m = comp == i
                ring = ndimage.binary_dilation(m) & ~m
                if not ring.any(): continue
                labels[m] = np.bincount(labels[ring], minlength=K).argmax()
                changed += 1
        if not changed: break
# Snow: pale cold colours lose part of their blue, so the snow sits with the room's warm whites.
if SNOWSAT != 1.0:
    for k in range(K):
        c = cent[k]; l = c @ np.array([.299, .587, .114])
        if l > .42 and c[2] > c[0] + .03:
            cent[k] = l + (c - l) * SNOWSAT
out = cent[labels]

# 5. Outline: where two colours meet, the darker side gets a line one shade darker (a drawn border, like the rug's).
if OUTLINE:
    # Kinds of land: palette colours grouped by hue (shades of one land are one kind). Only borders between kinds get a
    # line; shading steps inside one land stay plain flat bands, as on the rug.
    import colorsys
    hue = np.array([colorsys.rgb_to_hsv(*c)[0] for c in cent]); sat_c = np.array([colorsys.rgb_to_hsv(*c)[1] for c in cent])
    kind = np.where(sat_c < .16, 100, np.round(hue * 12).astype(int) % 12)
    kinds = kind[labels]
    lum = out @ np.array([.299, .587, .114], np.float32)
    edge = np.zeros((H, W), bool)
    for dy, dx in ((0, 1), (1, 0), (0, -1), (-1, 0)):
        nk = np.roll(np.roll(kinds, dy, 0), dx, 1)
        nl = np.roll(np.roll(lum, dy, 0), dx, 1)
        edge |= (nk != kinds) & (lum < nl)
    out[edge] *= .8

# 6. Tufts: sparse small marks on grass (green, mid-light), on a jittered grid, so fields read as grass up close.
if TUFTS:
    g = (out[..., 1] > out[..., 0] * 1.05) & (out[..., 1] > out[..., 2] * 1.2)
    step = 7
    for y in range(2, H - 2, step):
        for x in range(2, W - 2, step):
            jx, jy = x + int(rng.integers(0, 4)), y + int(rng.integers(0, 4))
            if jx >= W - 1 or jy >= H - 1 or not g[jy, jx] or rng.random() < .35: continue
            base = out[jy, jx].copy()
            dark = base * .78
            out[jy, jx] = dark
            out[jy - 1, jx - 1] = dark
            out[jy - 1, jx + 1] = dark

# Roads: two flat tones, a lighter middle and a darker edge, like the road meshes.
if road.any():
    inner = ndimage.binary_erosion(road, iterations=1)
    out[road] = np.array([.30, .27, .24], np.float32)
    out[inner] = np.array([.42, .38, .33], np.float32)
if INK: out[ink & ~road] = rgb[ink & ~road] * .9
res = np.concatenate([np.clip(out, 0, 1), alpha[..., None]], -1)
Image.fromarray((res * 255).astype(np.uint8), "RGBA").save(dst)
print("palette", K, "colours; ink texels", int(ink.sum()))

"""Lava for a pit, in the house style (Docs/Pixel-texture-style.md) at 128 texels a metre.

python Tools/lava_texture.py <out dir> [seed]

Writes "Lava.png" (the colour) and "Lava emission.png" (what glows) into <out dir>: 512 x 512 texels, one repeat
every 4 m, wrapping on both axes so pit cells meet without a seam (the sheet's UVs are its world position / 4 m).

The look: dark cooled crust in plates (stone-black, ash brown, a little rust), blotchy in clusters of a few tones and
varying texel to texel; each plate darker in its middle's creases and heated along its rim (dark red into orange), with
glowing cracks between the plates (red at their edges, orange, a yellow core where they are widest) and a few molten
pools where plates have sunk. Only the cracks, the rims and the pools glow (the emission map); the crust is dark.
"""
import sys, os
import numpy as np
from PIL import Image

SIZE = 512            # texels (4 m at 128 a metre)
PLATES = 70           # crust plates in a repeat (about 45 cm across each)
POOLS = 5             # plates that are molten instead of crust

out = sys.argv[1] if len(sys.argv) > 1 else "."
seed = int(sys.argv[2]) if len(sys.argv) > 2 else 7
rng = np.random.default_rng(seed)
os.makedirs(out, exist_ok=True)

yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)


def wrap_noise(cells, octave_seed):
    """Value noise on a wrapping grid of cells x cells, smooth-stepped (0..1)."""
    g = np.random.default_rng(octave_seed).random((cells, cells)).astype(np.float32)
    fx = xx / SIZE * cells; fy = yy / SIZE * cells
    x0 = np.floor(fx).astype(int) % cells; y0 = np.floor(fy).astype(int) % cells
    x1 = (x0 + 1) % cells; y1 = (y0 + 1) % cells
    tx = fx - np.floor(fx); ty = fy - np.floor(fy)
    tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty)
    a = g[y0, x0] * (1 - tx) + g[y0, x1] * tx
    b = g[y1, x0] * (1 - tx) + g[y1, x1] * tx
    return a * (1 - ty) + b * ty


# ── Plates: a wrapping Voronoi, its seeds jittered by noise so the cracks wander ──
pts = rng.random((PLATES, 2)).astype(np.float32) * SIZE
warp = (wrap_noise(16, seed + 1) - .5) * 14 + (wrap_noise(48, seed + 2) - .5) * 4
wx = xx + warp; wy = yy + (wrap_noise(16, seed + 3) - .5) * 14 + (wrap_noise(48, seed + 4) - .5) * 4
d1 = np.full((SIZE, SIZE), 1e9, np.float32); d2 = np.full((SIZE, SIZE), 1e9, np.float32)
owner = np.zeros((SIZE, SIZE), np.int32)
for i, (px, py) in enumerate(pts):
    dx = np.abs(wx - px); dx = np.minimum(dx, SIZE - np.abs(dx))
    dy = np.abs(wy - py); dy = np.minimum(dy, SIZE - np.abs(dy))
    d = np.sqrt(dx * dx + dy * dy)
    closer = d < d1
    d2 = np.where(closer, d1, np.minimum(d2, d))
    owner = np.where(closer, i, owner)
    d1 = np.where(closer, d, d1)
edge = d2 - d1                         # texels to the crack between two plates

# The crack's width wanders: hairline in places, a hand's width where plates have pulled apart.
width = 1.0 + 2.4 * wrap_noise(10, seed + 5) ** 2
pools = set(rng.choice(PLATES, POOLS, replace=False).tolist())
pool = np.isin(owner, list(pools))

# ── Crust: tones in clusters (blobs of 6-16 texels), a step up or down texel to texel ──
crust_tones = np.array([
    (14, 11, 11), (25, 19, 17), (38, 28, 23), (53, 39, 31), (70, 52, 39), (90, 68, 50),
], np.float32)
blob = wrap_noise(48, seed + 6) * .55 + wrap_noise(96, seed + 7) * .45
plate_shift = (rng.random(PLATES) - .5) * 1.6              # each plate a little lighter or darker than the next
level = (blob - .5) * 7.5 + 2.2 + plate_shift[owner]
# Creases: darker toward each plate's middle ridge lines, and in the plate's own pits.
level -= np.clip((d1 - 10) / 18, 0, 1) * 1.1 * (wrap_noise(24, seed + 8) > .45)
level += (rng.random((SIZE, SIZE)) < .14) * rng.choice([-1, 1], (SIZE, SIZE))   # single-texel steps
idx = np.clip(np.round(level), 0, len(crust_tones) - 1).astype(int)
# Cooled fissures inside each plate: a finer net of dark creases, the texel up and left of each a step lighter (its lip
# catches the light).
fine = rng.random((260, 2)).astype(np.float32) * SIZE
f1 = np.full((SIZE, SIZE), 1e9, np.float32); f2 = np.full((SIZE, SIZE), 1e9, np.float32)
for px, py in fine:
    dx = np.abs(wx - px); dx = np.minimum(dx, SIZE - np.abs(dx))
    dy = np.abs(wy - py); dy = np.minimum(dy, SIZE - np.abs(dy))
    d = np.sqrt(dx * dx + dy * dy)
    f2 = np.where(d < f1, f1, np.minimum(f2, d)); f1 = np.minimum(f1, d)
fissure = ((f2 - f1) < .9) & (wrap_noise(20, seed + 14) > .35)
idx = np.where(fissure, 0, idx)
lit = np.roll(np.roll(fissure, -1, 0), -1, 1) & ~fissure
idx = np.where(lit, np.minimum(idx + 2, len(crust_tones) - 1), idx)
colour = crust_tones[idx]
# A little rust and ash in some clusters (the muted base: brown, grey).
rust = (wrap_noise(28, seed + 9) > .68) & (rng.random((SIZE, SIZE)) < .7)
colour = np.where(rust[..., None], colour * np.array([1.18, .92, .8], np.float32), colour)
ash = (wrap_noise(36, seed + 10) > .72)
colour = np.where(ash[..., None], colour * .55 + np.array([38, 36, 34], np.float32) * .45, colour)
# Lit edges: the crust's lip along a crack catches the glow, two texels of dark red then warm brown.
glow = np.zeros((SIZE, SIZE, 3), np.float32)
lip = (edge >= width) & (edge < width + 1.6)
lip2 = (edge >= width + 1.6) & (edge < width + 3.2) & (rng.random((SIZE, SIZE)) < .55)
colour = np.where(lip[..., None], np.array([96, 34, 18], np.float32), colour)
colour = np.where(lip2[..., None], np.array([70, 32, 22], np.float32), colour)
glow = np.where(lip[..., None], np.array([70, 14, 4], np.float32), glow)

# ── Cracks and pools: stepped hot tones, the core only where the crack is widest ──
hot = np.array([(150, 30, 10), (205, 62, 14), (240, 112, 22), (255, 178, 52), (255, 226, 120)], np.float32)
heat = np.clip(1 - edge / np.maximum(width, .01), 0, 1)               # 1 at the crack's middle
heat_level = heat * 2.4 + (width - 1.0) / 2.4 * 1.8 + (wrap_noise(30, seed + 11) - .5) * 1.2
heat_level += (rng.random((SIZE, SIZE)) < .18) * rng.choice([-1, 1], (SIZE, SIZE))
crack = edge < width
# Pools: molten through, with dark skin floating on them in specks and small rafts.
pool_level = 1.7 + (wrap_noise(64, seed + 12) - .5) * 2.6 + (rng.random((SIZE, SIZE)) < .2) * rng.choice([-1, 1], (SIZE, SIZE))
skin = pool & (wrap_noise(70, seed + 13) > .62)
melt = (crack | pool) & ~skin
level_hot = np.where(pool & ~crack, pool_level, heat_level)
hidx = np.clip(np.round(level_hot), 0, len(hot) - 1).astype(int)
colour = np.where(melt[..., None], hot[hidx], colour)
glow = np.where(melt[..., None], hot[hidx], glow)
skin_tone = np.where(rng.random((SIZE, SIZE)) < .5, 0, 1)
colour = np.where(skin[..., None], np.array([(58, 22, 14), (84, 30, 16)], np.float32)[skin_tone], colour)
glow = np.where(skin[..., None], np.array([(30, 6, 2), (46, 10, 3)], np.float32)[skin_tone], glow)
# A few lone bright texels in the crust: embers.
embers = (~melt) & (rng.random((SIZE, SIZE)) < .0025) & (edge < width + 8)
colour = np.where(embers[..., None], hot[2], colour)
glow = np.where(embers[..., None], hot[2], glow)

Image.fromarray(np.clip(colour, 0, 255).astype(np.uint8)).save(os.path.join(out, "Lava.png"))
Image.fromarray(np.clip(glow, 0, 255).astype(np.uint8)).save(os.path.join(out, "Lava emission.png"))
print("wrote", os.path.join(out, "Lava.png"), "and Lava emission.png,", SIZE, "texels")

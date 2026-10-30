"""The standing water in the ruins' pits (DungeonBiome.pitWater; one 2 m quad per pit cell, UV 0-1), drawn in the house
style instead of the forest's glossy teal sheet: dark, murky green-brown water in a few tones laid in clusters (silt and
depth), slow ripple dashes a step lighter, sparse pale scum and floating flecks. 256 x 256 at the dungeon's 128 texels a
metre, wrapping on every side so neighbouring cells meet without a seam.
    python Tools/pit_water.py <out.png>"""
import sys
import numpy as np
from PIL import Image

N = 256
rng = np.random.default_rng(31)


def wrap_field(cell, seed):
    """A smooth 0-1 field that wraps every N texels, features about cell texels across."""
    r = np.random.default_rng(seed)
    k = N // cell
    g = r.random((k, k))
    g = np.concatenate([g, g[:1]], 0); g = np.concatenate([g, g[:, :1]], 1)
    y, x = np.mgrid[0:N, 0:N] / cell
    x0, y0 = x.astype(int), y.astype(int); fx, fy = x - x0, y - y0
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    a = g[y0, x0] * (1 - fx) + g[y0, x0 + 1] * fx
    b = g[y0 + 1, x0] * (1 - fx) + g[y0 + 1, x0 + 1] * fx
    return a * (1 - fy) + b * fy


tones = np.array([[18, 24, 20], [24, 32, 26], [32, 40, 31], [40, 47, 35]], float)
f = wrap_field(32, 1) * .6 + wrap_field(16, 2) * .3 + wrap_field(8, 3) * .1
t = np.clip(((f - f.min()) / (f.max() - f.min()) * 4).astype(int), 0, 3)
img = tones[t] * (1 + (rng.random((N, N, 1)) - .5) * .08)
# Ripples: short horizontal dashes a step lighter, where the field rises (slow rings of a drip).
ripple = np.abs((wrap_field(16, 4) * 7) % 1 - .5) < .035
img[ripple & (t >= 1)] += [10, 12, 9]
# Scum and flecks: sparse pale-olive clusters and single pale texels.
scum = wrap_field(8, 5) > .86
img[scum] = img[scum] * .5 + np.array([70, 74, 52]) * .5
fleck = rng.random((N, N)) < .004
img[fleck] = [86, 88, 66]
Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).save(sys.argv[1])
print(sys.argv[1])

"""Paints the ruins' vines: PolygonDarkFantasy's Vines_Diffuse (white leaf and stem shapes in alpha, coloured by a flat
tint in Synty's material) box-filtered to 560 px, 128 texels a metre on its cards, alpha cut hard at half, then coloured
in the house style: muted greens laid in clusters that vary texel to texel, now and then a dead brown leaf, the lit side
of each shape a step lighter and its edge a step darker (the dungeon's drawn props).
    python Tools/ruins_vines.py"""
from PIL import Image, ImageFilter
import numpy as np
SRC = "Assets/Synty/PolygonDarkFantasy/Textures/Misc/Vines_Diffuse.png"
OUT = "Assets/Game/Levels/Dungeon1/Ruins/Props/Vines/Vines pixel.png"
N = 560
rng = np.random.default_rng(11)
alpha = np.asarray(Image.open(SRC).convert("RGBA").getchannel("A").resize((N, N), Image.BOX)).astype(float) / 255
solid = alpha >= .5

def blotch(scale):
    g = rng.random((N // scale + 2, N // scale + 2))
    return np.asarray(Image.fromarray((g * 255).astype("uint8")).resize((N + 2 * scale, N + 2 * scale), Image.BICUBIC)).astype(float)[scale:scale + N, scale:scale + N] / 255

# Muted greens, dark to lit, and a dead leaf brown.
# The ruins' moss tones (Tools/ruins_variety.py), not leaf green: bright lime stood out against the stone (the user).
greens = np.array([[36, 42, 28], [46, 54, 34], [56, 64, 40], [68, 74, 48]], float)
dead = np.array([[70, 58, 40], [84, 68, 46]], float)
n = blotch(7) * .7 + blotch(3) * .3
idx = np.clip((n * 4.4 - .7).astype(int), 0, 3)
col = greens[idx]
# Dead leaves in clusters.
d = blotch(9) > .78
col[d] = dead[(blotch(4)[d] > .5).astype(int)]
# Texel-to-texel grain.
col *= (1 + (rng.random((N, N, 1)) - .5) * .12)
# Lit from above: a shape's top edge a step lighter, its other edges a step darker.
up = np.roll(solid, 1, axis=0)
down = np.roll(solid, -1, axis=0)
left = np.roll(solid, 1, axis=1)
right = np.roll(solid, -1, axis=1)
edge = solid & ~(up & down & left & right)
top = solid & ~up
col[edge] *= .5
col[top] = col[top] / .5 * 1.1
out = np.zeros((N, N, 4))
out[..., :3] = np.clip(col, 0, 255)
out[..., 3] = solid * 255
Image.fromarray(out.astype("uint8"), "RGBA").save(OUT)
print("vines", N, "px, covered", solid.mean().round(3))

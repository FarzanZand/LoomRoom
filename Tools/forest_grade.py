"""Grades the forest props whose restyle came out in flat fills (the rock formations and the moss lumps) the way the house
style asks (Docs/Pixel-texture-style.md): colour varying in clusters and texel to texel, the darker texels pulled darker
(dirt in the creases), never a flat area. Works from '<name> base.png' (the restyle's own output, kept the first time
this runs), so it can be run again after a rebuild of the props.
    python Tools/forest_grade.py"""
import os
import numpy as np
from PIL import Image
ROOT = "Assets/Game/Levels/Dungeon1/Forest/Props"
NAMES = ["Formation 1", "Formation 2", "Formation 5", "Formation 9", "Formation 12", "Moss 1", "Moss 2", "Moss 3", "Moss 4"]
rng = np.random.default_rng(5)

def field(h, w, scale):
    """A smooth 0-1 field with features about scale texels across (cut into tones below, so its shapes come out
    irregular and hard-edged: the drawn style's clusters, never a square grid or a soft gradient)."""
    g = rng.random((h // scale + 3, w // scale + 3))
    big = Image.fromarray((g * 255).astype("uint8")).resize(((w // scale + 3) * scale, (h // scale + 3) * scale), Image.BICUBIC)
    return np.asarray(big).astype(float)[scale:scale + h, scale:scale + w] / 255

def steps(x, levels):
    """A 0-1 field cut into a few tones."""
    return np.asarray(levels)[np.clip((x * len(levels)).astype(int), 0, len(levels) - 1)]

for n in NAMES:
    path = f"{ROOT}/{n}/{n}.png"
    base = f"{ROOT}/{n}/{n} base.png"
    if not os.path.exists(path): print("missing", path); continue
    if not os.path.exists(base): Image.open(path).save(base)
    im = np.asarray(Image.open(base).convert("RGBA")).astype(float)
    h, w = im.shape[:2]
    rgb, a = im[..., :3], im[..., 3:]
    lum = rgb.mean(-1, keepdims=True)
    # Irregular clusters of lighter and darker stone or moss in four tones, smaller flecks, and a little texel grain.
    big = field(h, w, 9) * .65 + field(h, w, 4) * .35
    vary = 1 + steps(np.clip((big - .2) / .6, 0, .999), [-.14, -.05, .03, .1]) + steps(field(h, w, 2), [-.05, 0, .04]) + (rng.random((h, w)) - .5) * .05
    vary = vary[..., None]
    out = rgb * vary
    # Creases: texels darker than their surroundings get darker still.
    local = np.asarray(Image.fromarray(np.clip(lum[..., 0], 0, 255).astype("uint8")).resize((max(1, w // 6), max(1, h // 6)), Image.BOX).resize((w, h), Image.BILINEAR)).astype(float)[..., None]
    out = np.where(lum < local * .93, out * .82, out)
    Image.fromarray(np.concatenate([np.clip(out, 0, 255), a], -1).astype("uint8"), "RGBA").save(path)
    print("graded", n, w, "x", h)

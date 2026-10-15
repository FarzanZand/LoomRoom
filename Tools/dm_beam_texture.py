"""Paints the Dungeon Master's beam textures from the Spells Pack's own art (no new art):

  DM beam body.png   the beam's body: the pack Glow's soft falloff across it, two of Spell 5's flame wisps turned to
                     run along it (they scroll toward the player)
  DM beam core.png   its white-hot middle: a narrow falloff with a faint trace of the wisps
  DM beam glow.png   the soft halo round it: a smooth falloff across, fading to nothing at its edges
  DM beam flare.png  where it meets the shield: Spell 3's first burst over the Glow

White with the shape in alpha (LoomRoom/Beam Additive reads alpha as the mask). Usage:
  python Tools/dm_beam_texture.py
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PACK = os.path.join(ROOT, "Assets", "Spells Pack", "Particles", "Textures")
OUT = os.path.join(ROOT, "Assets", "Game", "Combat", "Effects", "DM beam")
W, H = 128, 32


def save(alpha, name):
    img = np.zeros(alpha.shape + (4,), np.uint8)
    img[..., :3] = 255
    img[..., 3] = (np.clip(alpha, 0, 1) * 255).astype(np.uint8)
    Image.fromarray(img, "RGBA").save(os.path.join(OUT, name))


def main():
    glow = np.asarray(Image.open(os.path.join(PACK, "Glow.png")).convert("L")).astype(float) / 255
    row = glow[glow.shape[0] // 2]
    row = row / row.max()
    lit = np.where(row > .02)[0]
    prof = row[lit[0]:lit[-1] + 1]
    across = np.interp(np.linspace(0, len(prof) - 1, H), np.arange(len(prof)), prof)

    sheet = Image.open(os.path.join(PACK, "Spell 5.png")).convert("L")
    f = sheet.size[1]
    a = sheet.crop((0, 0, f, f)).rotate(-90, resample=Image.BICUBIC)
    b = sheet.crop((f, 0, 2 * f, f)).rotate(-90, resample=Image.BICUBIC).transpose(Image.FLIP_TOP_BOTTOM)
    ys, xs = np.where(np.asarray(a) > 20)
    box = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
    a = a.crop(box).resize((W // 2, H), Image.BILINEAR)
    b = b.crop(box).resize((W // 2, H), Image.BILINEAR)
    flow = np.concatenate([np.asarray(a), np.asarray(b)], axis=1).astype(float)
    flow /= max(flow.max(), 1e-3)

    save((across ** .5)[:, None] * (.4 + .6 * flow) * 1.1, "DM beam body.png")
    # The halo: a smooth falloff across (no hard edge, so it never reads as a tube), 64 rows so it stays smooth even
    # point-filtered, and the same along the beam.
    v = np.linspace(-1, 1, 64)
    halo = np.exp(-(v / .42) ** 2) * (1 - np.abs(v)) ** .6
    save(np.tile(halo[:, None], (1, 8)), "DM beam glow.png")
    save((across ** 3)[:, None] * (.8 + .2 * flow), "DM beam core.png")

    burst = Image.open(os.path.join(PACK, "Spell 3.png")).convert("L")
    f = burst.size[1]
    burst = np.asarray(burst.crop((0, 0, f, f)).resize((64, 64), Image.BILINEAR)).astype(float) / 255
    soft = np.asarray(Image.open(os.path.join(PACK, "Glow.png")).convert("L").resize((64, 64), Image.BILINEAR)).astype(float) / 255
    save(np.maximum(burst * .5, soft), "DM beam flare.png")


if __name__ == "__main__":
    main()

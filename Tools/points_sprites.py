"""Pixel sprites for the attribute points panel (AttributePointsUI): an ink stroke that strikes out a number on the
Ledger, and an ember for the Stone slots. Drawn at the inventory's scale (shown at two
canvas units per texel), dirty and uneven like the rest of the UI art, never a flat fill.

    python Tools/points_sprites.py <out dir>
"""
import math, os, random, sys
from PIL import Image

out = sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/UI/Sprites/Points"
os.makedirs(out, exist_ok=True)
rng = random.Random(7)


def ink_line(w=64, h=6):
    # A pen stroke drawn left to right: uneven thickness, a heavier start, a thin flick at the end.
    im = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    mid = h / 2 - .5 + rng.uniform(-.3, .3)
    for x in range(w):
        t = x / (w - 1)
        y = mid + math.sin(t * 3.1) * .7 - t * .8
        thick = 2.8 - 1.2 * t + rng.uniform(-.2, .2)
        if t < .06:
            thick += .8
        for yy in range(h):
            d = abs(yy - y)
            if d < thick * .5 + .35:
                a = 245 if d < thick * .5 - .15 else 170
                ink = rng.randint(28, 46)
                im.putpixel((x, yy), (ink + 6, ink, ink - 6 if ink > 6 else 0, a))
    return im


def ember(size=4):
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    for y in range(size):
        for x in range(size):
            d = math.hypot(x - (size - 1) / 2, y - (size - 1) / 2)
            if d <= size / 2:
                im.putpixel((x, y), (255, 255, 255, 255 if d < size / 2 - .8 else 140))
    return im


ink_line().save(os.path.join(out, "Ink stroke.png"))
ember().save(os.path.join(out, "Ember.png"))
print("written to", out)

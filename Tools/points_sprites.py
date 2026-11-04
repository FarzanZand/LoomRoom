"""Pixel sprites for the attribute points panel (AttributePointsUI): a pewter token for the Tokens look, an ink stroke
that strikes out a number on the Ledger, and an ember for the Stone slots. Drawn at the inventory's scale (shown at two
canvas units per texel), dirty and uneven like the rest of the UI art, never a flat fill.

    python Tools/points_sprites.py <out dir>
"""
import math, os, random, sys
from PIL import Image

out = sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/UI/Sprites/Points"
os.makedirs(out, exist_ok=True)
rng = random.Random(7)


def token(size=20):
    # A cast pewter coin: a darker rim, a raised ring, a flat face with grain, light from the top left, a dent or two.
    im = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    c = (size - 1) / 2
    for y in range(size):
        for x in range(size):
            dx, dy = x - c, y - c
            r = math.hypot(dx, dy)
            if r > c + .35:
                continue
            light = (-dx - dy) / (size * .9)
            if r > c - 1.2:
                base = 70 + 30 * light          # outer rim, dark
            elif r > c - 2.6:
                base = 150 + 55 * light         # raised ring, catches the light
            elif r > c - 3.4:
                base = 95 + 20 * light          # the groove inside the ring
            else:
                base = 122 + 26 * light         # the face
            base += rng.uniform(-9, 9)
            if rng.random() < .05:
                base -= 22                      # pits
            v = max(30, min(220, base))
            im.putpixel((x, y), (int(v * .94), int(v * .96), int(v), 255))
    # a stamped mark in the face: a small die pip cross
    for px, py in ((c, c), (c - 2.5, c - 2.5), (c + 2.5, c + 2.5)):
        x, y = int(round(px)), int(round(py))
        p = im.getpixel((x, y))
        im.putpixel((x, y), (max(0, p[0] - 45), max(0, p[1] - 45), max(0, p[2] - 42), 255))
    return im


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


token().save(os.path.join(out, "Pewter token.png"))
ink_line().save(os.path.join(out, "Ink stroke.png"))
ember().save(os.path.join(out, "Ember.png"))
print("written to", out)

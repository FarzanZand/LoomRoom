"""The Z's that float over a sleeper's head (Characters/Resources/Sleep Zs, SleepZs): Sleep Zs.png, two tiles of 24 x 24
texels side by side, the big Z and the small z of the game's pixel font (PixelOperator-Bold) at twice its size (every
font pixel 2 x 2 texels). SleepZs draws the z's smaller the nearer the head they are. Each has a one-texel dark outline
all round, so it reads over light stone and in the dark, and is pale with a slightly cooler lower half (no glow: the value
stays under white). Hard pixels, alpha 0 or 1.

    python Tools/sleep_zs.py "<out folder>"
"""
import sys, os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

out = sys.argv[1]
FONT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Game', 'UI', 'Fonts', 'PixelOperator-Bold.ttf')
TILE = 24
LIGHT, SHADE, EDGE = (226, 232, 246), (176, 188, 216), (20, 22, 36)


def glyph(letter, scale):
    """The letter's pixels at the font's own size (16), each drawn scale x scale texels."""
    font = ImageFont.truetype(FONT, 16)
    im = Image.new('L', (32, 32), 0)
    ImageDraw.Draw(im).text((8, 8), letter, font=font, fill=255)
    a = np.array(im) > 127
    ys, xs = np.where(a)
    a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    return a.repeat(scale, 0).repeat(scale, 1)


def tile(letter, scale):
    g = glyph(letter, scale)
    h, w = g.shape
    assert h + 2 <= TILE and w + 2 <= TILE, (letter, scale, g.shape)
    img = np.zeros((TILE, TILE, 4), np.uint8)
    y0, x0 = (TILE - h) // 2, (TILE - w) // 2
    body = np.zeros((TILE, TILE), bool); body[y0:y0 + h, x0:x0 + w] = g
    edge = np.zeros_like(body)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            edge |= np.roll(np.roll(body, dy, 0), dx, 1)
    img[edge] = (*EDGE, 255)
    for y, x in zip(*np.where(body)):
        img[y, x] = (*(LIGHT if y - y0 < h * .55 else SHADE), 255)
    return img, (w, h)


os.makedirs(out, exist_ok=True)
tiles = [tile('Z', 2), tile('z', 2)]
Image.fromarray(np.concatenate([t[0] for t in tiles], axis=1), 'RGBA').save(os.path.join(out, 'Sleep Zs.png'))
print('written', out, [t[1] for t in tiles])

"""Pixel sheets for the Pianist's crescendo burst (Characters/Classes/Pianist/Effects), at the dungeon's 128 texels a metre:
each particle is drawn at its sheet's cell size, so the notes' pixels match the walls and creatures.

  Notes.png      4 cells of 12 x 12 (9.4 cm): crotchet, quaver, a beamed pair rising and one falling. The crescendo's small notes.
  Big notes.png  4 cells of 16 x 16 (12.5 cm): the same four, larger. The crescendo's large notes. (No sharp sign: it read as a
                 hashtag. Round 1 had 16 and 24 texel cells: the crescendo's glyphs hid the enemy.)
                 Both painted in near-white greys with a dark outline: the particle's colour tints them, so one sheet
                 gives every colour and the outline stays dark. Lit from the upper left, uneven texel to texel.
  (No Ring.png any more: the crescendo's ring is gone. Round 1 it was deep red and read as blood; round 2 gold, the
  colour of CRESCENDO!, which it crossed; round 3 a cold white ring 30 cm under the hit, which stood apart from the
  blow; as two arcs beside the number, among notes that now stay close to the hit, it read as drips.)

    python Tools/pianist_notes.py "<out folder>"
"""
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
rng = np.random.default_rng(88)

HEAD = ["..###.", ".#####", "######", "#####.", ".###.."]                       # a note's head, tilted
SMALL_HEAD = [".###", "####", "###."]


def stamp(a, rows, x, y):
    for j, row in enumerate(rows):
        for i, c in enumerate(row):
            if c == '#': a[y + j, x + i] = True


def bar(a, x0, y0, x1, y1):
    a[y0:y1 + 1, x0:x1 + 1] = True


def beam(a, x0, y0, x1, y1, thick):
    """A beam from the top of one stem to the top of the next: each column its own height, 'thick' texels deep."""
    for x in range(x0, x1 + 1):
        y = round(y0 + (y1 - y0) * (x - x0) / max(1, x1 - x0))
        a[y:y + thick, x] = True


# The small notes: 12 x 12.
def small_crotchet(c):
    a = np.zeros((c, c), bool)
    stamp(a, SMALL_HEAD, 3, 7); bar(a, 6, 2, 6, 8)
    return a


def small_quaver(c):
    a = np.zeros((c, c), bool)
    stamp(a, SMALL_HEAD, 2, 7); bar(a, 5, 2, 5, 8)
    for y, (x0, x1) in zip(range(2, 6), [(6, 7), (7, 8), (8, 9), (8, 8)]): a[y, x0:x1 + 1] = True
    return a


def small_pair(c, falling):
    a = np.zeros((c, c), bool)
    if falling:
        stamp(a, SMALL_HEAD, 1, 6); bar(a, 4, 2, 4, 7)
        stamp(a, SMALL_HEAD, 6, 8); bar(a, 9, 4, 9, 9)
        beam(a, 4, 2, 9, 4, 2)
    else:
        stamp(a, SMALL_HEAD, 1, 8); bar(a, 4, 4, 4, 9)
        stamp(a, SMALL_HEAD, 6, 6); bar(a, 9, 2, 9, 7)
        beam(a, 4, 4, 9, 2, 2)
    return a


# The crescendo's notes: 16 x 16.
def crotchet(c):
    a = np.zeros((c, c), bool)
    stamp(a, HEAD, 4, 9); bar(a, 9, 2, 10, 11)
    return a


def quaver(c):
    a = np.zeros((c, c), bool)
    stamp(a, HEAD, 3, 9); bar(a, 8, 2, 9, 11)
    for y, (x0, x1) in zip(range(2, 8), [(10, 11), (10, 12), (11, 13), (12, 13), (12, 13), (12, 12)]): a[y, x0:x1 + 1] = True
    return a


def pair(c, falling):
    a = np.zeros((c, c), bool)
    if falling:                                   # the beam falls to the right, the second note lower
        stamp(a, HEAD, 1, 8); bar(a, 6, 2, 7, 10)
        stamp(a, HEAD, 8, 10); bar(a, 13, 4, 14, 12)
        beam(a, 6, 2, 14, 4, 3)
    else:
        stamp(a, HEAD, 1, 10); bar(a, 6, 4, 7, 12)
        stamp(a, HEAD, 8, 8); bar(a, 13, 2, 14, 10)
        beam(a, 6, 4, 14, 2, 3)
    return a


def paint(a):
    """Greys for the particle's colour to tint: lit edge up and left, shaded down and right, a dark outline all round."""
    c = a.shape[0]
    img = np.zeros((c, c, 4), np.uint8)
    for y in range(c):
        for x in range(c):
            if a[y, x]:
                up = y == 0 or not a[y - 1, x]; left = x == 0 or not a[y, x - 1]
                down = y == c - 1 or not a[y + 1, x]; right = x == c - 1 or not a[y, x + 1]
                tone = 255 if (up or left) and not (down and right) else 198 if (down or right) else 232
                tone = int(np.clip(tone + rng.choice([-10, -5, 0, 0, 0]), 0, 255))
                img[y, x] = (tone, tone, tone, 255)
            else:
                near = a[max(0, y - 1):y + 2, max(0, x - 1):x + 2].any()
                if near: img[y, x] = (24, 20, 30, 255)
    return img


def sheet(name, cell, shapes):
    s = np.zeros((cell, cell * len(shapes), 4), np.uint8)
    for i, shape in enumerate(shapes):
        a = shape(cell)
        assert not (a[0].any() or a[-1].any() or a[:, 0].any() or a[:, -1].any()), (name, i, "no room for the outline")
        s[:, i * cell:(i + 1) * cell] = paint(a)
    Image.fromarray(s, "RGBA").save(os.path.join(out, name))
    print(name, s.shape[1], "x", s.shape[0])


sheet("Notes.png", 12, [small_crotchet, small_quaver, lambda c: small_pair(c, False), lambda c: small_pair(c, True)])
sheet("Big notes.png", 16, [crotchet, quaver, lambda c: pair(c, False), lambda c: pair(c, True)])

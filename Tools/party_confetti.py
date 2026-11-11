"""Pixel sheets for the Party Crasher's confetti burst (Items/Effects/Party confetti), at the dungeon's 128 texels a metre:
each particle is drawn at that size (an 8 texel cell = 6.25 cm), so the flecks' pixels match the walls and creatures.

  Confetti.png   4 x 4 cells of 8 x 8: a row per fleck shape (slip, square, long strip, crinkled strip), four frames of it
                 turning over (face up, edge-ward, edge on, back up). Painted in greys: the particle's colour tints it,
                 so one sheet gives every colour. Lit from the upper left, the back side darker, uneven texel to texel.
  Sparkle.png    4 cells of 9 x 9: a twinkle from a speck to a four-pointed star and back.
  Pop.png        40 x 40: the burst's first flash, short rays from a bright middle.

    python Tools/party_confetti.py "<out folder>"
"""
import sys, os, math
import numpy as np
from PIL import Image

out = sys.argv[1]
rng = np.random.default_rng(11)
os.makedirs(out, exist_ok=True)

# ── Confetti ──
C = 8
SHAPES = [
    ["..###...", "..####..", "..####..", "..####..", "...###..", "........", "........", "........"],   # slip, a corner nicked
    ["........", ".####...", ".####...", ".####...", ".###....", "........", "........", "........"],   # square
    ["...##...", "...##...", "...##...", "...##...", "...##...", "...##...", "....#...", "........"],   # long strip
    ["..##....", "...##...", "...##...", "..##....", "..##....", "...##...", "...#....", "........"],   # crinkled strip
]
SQUASH = [1.0, .6, .2, .6]          # width as it turns over; the last two frames show its back


def fleck(rows, squash, back):
    """Each row's span squeezed toward the fleck's middle (never under one texel), so it turns over row by row."""
    m = np.array([[c == '#' for c in r] for r in rows], bool)
    ys, xs = np.nonzero(m)
    cx = (xs.min() + xs.max() + 1) / 2
    a = np.zeros((C, C), bool)
    for y in range(C):
        row = np.nonzero(m[y])[0]
        if len(row) == 0: continue
        mid = (row.min() + row.max() + 1) / 2
        mid = cx + (mid - cx) * squash
        w = max(1, round(len(row) * squash))
        x0 = int(round(mid - w / 2)); a[y, max(0, x0):min(C, x0 + w)] = True
    img = np.zeros((C, C, 4)); img[:, :, 3] = np.where(a, 255, 0)
    tone = np.where(a, 222.0, 0)
    for y in range(C):
        for x in range(C):
            if not a[y, x]: continue
            up = y == 0 or not a[y - 1, x]; left = x == 0 or not a[y, x - 1]
            down = y == C - 1 or not a[y + 1, x]; right = x == C - 1 or not a[y, x + 1]
            if up or left: tone[y, x] = 255
            if down or right: tone[y, x] = 180
            tone[y, x] += rng.choice([-14, -7, 0, 0, 7])
    if squash < .3: tone[a] = 165                   # edge on: a thin darker line
    if back: tone[a] *= .74
    img[:, :, 0] = img[:, :, 1] = img[:, :, 2] = np.clip(tone, 0, 255)
    return img


sheet = np.zeros((4 * C, 4 * C, 4))
for r, shape in enumerate(SHAPES):
    for f, sq in enumerate(SQUASH):
        sheet[r * C:(r + 1) * C, f * C:(f + 1) * C] = fleck(shape, sq, f >= 2)
Image.fromarray(sheet.astype(np.uint8), 'RGBA').save(os.path.join(out, 'Confetti.png'))

# ── Sparkle ──
S = 9
spark = np.zeros((S, 4 * S, 4))


def star(cell, arm, glow):
    c = S // 2
    for d in range(-arm, arm + 1):
        for x, y in ((c + d, c), (c, c + d)):
            v = 255 if abs(d) <= 1 else (200 if abs(d) < arm else 130)
            spark[y, cell * S + x] = (v, v, v, v)
    if glow:
        for x, y in ((c - 1, c - 1), (c + 1, c - 1), (c - 1, c + 1), (c + 1, c + 1)):
            spark[y, cell * S + x] = (150, 150, 150, 150)
    spark[c, cell * S + c] = (255, 255, 255, 255)


star(0, 0, False); star(1, 1, False); star(2, 4, True); star(3, 2, True)
Image.fromarray(spark.astype(np.uint8), 'RGBA').save(os.path.join(out, 'Sparkle.png'))

# ── Pop ──
# Short rays from a bright middle, long and short in turn: a ring round it read as a drawn sun.
P = 40
pop = np.zeros((P, P, 4))
c = (P - 1) / 2
rays = 10
for k in range(rays):
    ang = 2 * math.pi * (k + rng.uniform(-.15, .15)) / rays
    start, end = (.22 if k % 2 else .3) * c, (rng.uniform(.85, 1.0) if k % 2 == 0 else rng.uniform(.5, .65)) * c
    for t in np.arange(start, end, .35):
        x, y = int(round(c + t * math.cos(ang))), int(round(c + t * math.sin(ang)))
        v = 255 if t < .6 * c else 175
        pop[y, x] = (v, v, v, v)
for y in range(P):
    for x in range(P):
        r = math.hypot(x - c, y - c)
        if r < 2.2: pop[y, x] = (255, 255, 255, 255)
        elif r < 3.4 and (x + y) % 2 == 0: pop[y, x] = (200, 200, 200, 200)
Image.fromarray(pop.astype(np.uint8), 'RGBA').save(os.path.join(out, 'Pop.png'))
print('written', out)

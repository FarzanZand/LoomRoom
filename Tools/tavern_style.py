"""Paints the Tavern room style's textures as drawn pixel art, the way the Church style is drawn (its walls, floor and
vault): every shape (board, post, stone, chip) has a dark outline, a lit edge on its upper or left side and a shaded one
opposite, two or three tones inside laid in clusters of several texels (never texel-by-texel noise), drawn cracks and
grain lines, a few single specks. Muted colours: limewash, green paint, dark oak. Seamless, at the dungeon's density of
384 texels an architecture tile (3 m, 128 a metre). The floor is the Church floor itself (pale flagstones).

  Tavern wall.png     384 x 384, tiling 1: a green-painted board wainscot to 1 m (chipped back to the wood), a dado
                      rail, limewashed plaster panels between timber posts (cracks, a flaked patch showing the stones
                      behind, smoke along the top), a wall plate
  Tavern ceiling.png  384 x 384, tiling 1: dark boards with grain, nail heads and soot
  Tavern trim.png     384 x 384, tiling 1: rough dark timber with grain lines and splits (beams, rails, frames)

python Tools/tavern_style.py <out dir>
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image


def value_noise(w, h, c, seed):
    """Tileable value noise in 0..1, c texels between lattice points (divides w and h)."""
    rnd = random.Random(seed)
    nx, ny = w // c, h // c
    lat = [[rnd.random() for _ in range(nx)] for _ in range(ny)]
    out = [[0.0] * w for _ in range(h)]
    for y in range(h):
        fy = y / c; y0 = int(fy); ty = fy - y0; ty = ty * ty * (3 - 2 * ty)
        for x in range(w):
            fx = x / c; x0 = int(fx); tx = fx - x0; tx = tx * tx * (3 - 2 * tx)
            a = lat[y0 % ny][x0 % nx]; b = lat[y0 % ny][(x0 + 1) % nx]
            cc = lat[(y0 + 1) % ny][x0 % nx]; d = lat[(y0 + 1) % ny][(x0 + 1) % nx]
            out[y][x] = (a + (b - a) * tx) * (1 - ty) + (cc + (d - cc) * tx) * ty
    return out


def tone(ramp, t):
    return ramp[max(0, min(len(ramp) - 1, int(t * len(ramp))))]


# Ramps, dark to light, few steps (a drawn look keeps to them).
PLASTER = [(118, 112, 98), (146, 139, 122), (170, 162, 143), (190, 182, 160)]
PLASTER_LINE = (92, 86, 74)
GREEN = [(28, 46, 36), (38, 62, 48), (50, 78, 60), (64, 94, 72)]
GREEN_LINE = (18, 28, 22)
OAK = [(44, 30, 22), (60, 41, 28), (78, 54, 36), (98, 69, 45)]
OAK_LINE = (26, 17, 12)
CEIL = [(34, 28, 24), (44, 37, 31), (56, 47, 39), (68, 57, 47)]
CEIL_LINE = (20, 16, 13)
STONE = [(70, 70, 72), (92, 92, 92), (114, 112, 108), (134, 131, 124)]
NAIL = (40, 38, 36)


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.img = Image.new("RGB", (w, h)); self.px = self.img.load()

    def set(self, x, y, c): self.px[x % self.w, y % self.h] = c
    def get(self, x, y): return self.px[x % self.w, y % self.h]


def board(cv, x0, y0, bw, bh, ramp, line, rnd, clusters, along_x=True, grain=True, knot_chance=.3, nails=True):
    """One outlined board: dark outline, lit leading edge, shaded trailing edge, inside two or three tones by cluster
    noise stretched along the board, broken grain lines, maybe a knot, nail heads at its ends."""
    t0 = rnd.uniform(-.12, .12)
    span, depth = (bw, bh) if along_x else (bh, bw)
    for j in range(bh):
        for i in range(bw):
            a, d = (i, j) if along_x else (j, i)            # a: along the board, d: across it
            n = clusters[(d * 3 + y0 * 7) % len(clusters)][(a // 5 + x0) % len(clusters[0])]
            c = tone(ramp, .45 + t0 + (n - .5) * .7)
            if d == 0 or a == 0: c = line
            elif d == 1: c = tone(ramp, .99)                 # lit edge
            elif d == depth - 1: c = tone(ramp, 0)           # shaded edge
            cv.set(x0 + i, y0 + j, c)
    if grain:
        for g in range(max(1, depth // 7)):
            off = 3 + g * 7 + rnd.randrange(0, 3)
            if off >= depth - 2: break
            pos = rnd.randrange(2, 10)
            while pos < span - 3:
                run = rnd.randrange(6, 22)
                for k in range(run):
                    if pos + k >= span - 2: break
                    x, y = (x0 + pos + k, y0 + off) if along_x else (x0 + off, y0 + pos + k)
                    cv.set(x, y, tone(ramp, 0))
                pos += run + rnd.randrange(3, 12)
                if rnd.random() < .4: off = min(depth - 3, max(2, off + rnd.choice((-1, 1))))
    if rnd.random() < knot_chance and span > 30:
        kx = rnd.randrange(10, span - 10); ky = depth // 2
        for dk in range(-3, 4):
            for dd in range(-1, 2):
                if abs(dk) == 3 and dd != 0: continue
                x, y = (x0 + kx + dk, y0 + ky + dd) if along_x else (x0 + ky + dd, y0 + kx + dk)
                cv.set(x, y, line if (abs(dk) == 3 or abs(dd) == 1) else tone(ramp, .3))
    if nails:
        for end in (4, span - 5):
            for side in (3, depth - 4):
                if side <= 1 or side >= depth - 1: continue
                x, y = (x0 + end, y0 + side) if along_x else (x0 + side, y0 + end)
                cv.set(x, y, NAIL); cv.set(x - 1, y - 1, tone(ramp, .99))


def wall(path):
    w = h = 384
    rnd = random.Random(51)
    cv = Canvas(w, h)
    clusters = value_noise(w, h, 6, 52)
    plate, rail_h, wains, post = 20, 14, 126, 16
    rail_top = h - wains - rail_h
    # plaster: big zones of three tones from broad noise, quantised; smoke along the top as a ragged darker band
    big = value_noise(w, h, 24, 53)
    for y in range(plate, rail_top):
        for x in range(w):
            v = .64 + (big[y][x] - .5) * .26 + (clusters[y][x] - .5) * .1      # one tone, a few soft-edged patches
            if y < plate + 26 and clusters[y][x] + (y - plate) / 40 < .9: v -= .3
            cv.set(x, y, tone(PLASTER, v))
    # where one zone steps darker than the one above, a drawn line (on half of them, so it stays loose)
    zone = [[cv.get(x, y) for x in range(w)] for y in range(h)]
    for y in range(plate + 1, rail_top):
        for x in range(w):
            if sum(zone[y][x]) < sum(zone[y - 1][x]) - 40 and rnd.random() < .5: cv.set(x, y, tone(PLASTER, .25))
    # trowel strokes: short light dashes with a darker one under, scattered, so the limewash reads as laid by hand
    for _ in range(420):
        x, y = rnd.randrange(post, w - post), rnd.randrange(plate + 30, rail_top - 3)
        L = rnd.randrange(3, 8)
        for k in range(L):
            if cv.get(x + k, y) in PLASTER: cv.set(x + k, y, tone(PLASTER, .99))
            if cv.get(x + k + 1, y + 1) in PLASTER and k < L - 1: cv.set(x + k + 1, y + 1, tone(PLASTER, .3))
    # (No flaked patch showing the stones: one patch repeats on every 3 m wall tile and reads as a stamp.)
    # cracks: drawn dark lines with a lit texel beside
    for _ in range(6):
        x, y = rnd.randrange(post, w - post), rnd.randrange(plate + 4, rail_top - 6)
        for _ in range(rnd.randrange(12, 30)):
            if plate < y < rail_top - 1 and post // 2 < x % w < w - post // 2:
                cv.set(x, y, PLASTER_LINE); cv.set(x + 1, y, tone(PLASTER, .99))
            x += rnd.choice((-1, 0, 1)); y += rnd.choice((1, 1, 0))
    # wainscot: vertical green-painted boards; chips back to the wood, outlined, heavier toward the floor
    bw = 24
    for i in range(w // bw):
        board(cv, i * bw, h - wains, bw, wains, GREEN, GREEN_LINE, rnd, clusters, along_x=False, knot_chance=0)
    for _ in range(26):
        x0, y0 = rnd.randrange(w), rnd.randrange(h - wains + 6, h - 2)
        if y0 < h - 60 and rnd.random() < .6: continue
        rx, ry = rnd.randrange(2, 6), rnd.randrange(1, 4)
        for dy in range(-ry - 1, ry + 2):
            for dx in range(-rx - 1, rx + 2):
                d = (dx / rx) ** 2 + (dy / ry) ** 2
                if d <= 1: cv.set(x0 + dx, y0 + dy, tone(OAK, .5 + (.3 if dy < 0 else 0)))
                elif d <= 1.7 and cv.get(x0 + dx, y0 + dy) in GREEN: cv.set(x0 + dx, y0 + dy, GREEN_LINE)
    # timber: the dado rail and the wall plate, then posts at the tile's edges (half each side)
    for (y0, hh) in ((rail_top, rail_h), (0, plate)):
        board(cv, 0, y0, w, hh, OAK, OAK_LINE, rnd, clusters, along_x=True, knot_chance=.6, nails=False)
    for y in range(rail_top + rail_h, rail_top + rail_h + 2):
        for x in range(w): cv.set(x, y, GREEN_LINE)
    for y in range(plate, plate + 2):
        for x in range(w): cv.set(x, y, tone(PLASTER, .1))
    for side in (0, 1):
        x0 = 0 if side == 0 else w - post // 2
        for y in range(plate, rail_top):
            for i in range(post // 2):
                x = x0 + i
                n = clusters[(y // 6) % h][x]
                c = tone(OAK, .45 + (n - .5) * .7)
                edge_i = i if side == 1 else post // 2 - 1 - i
                if edge_i == 0: c = OAK_LINE
                elif edge_i == 1: c = tone(OAK, .99) if side == 1 else tone(OAK, 0)
                if (y + x * 7) % 23 < 7 and edge_i in (3, 5): c = tone(OAK, 0)
                cv.set(x, y, c)
        px = 2 if side == 0 else w - 3
        for py in (plate + 5, rail_top - 6):
            cv.set(px, py, NAIL); cv.set(px - 1, py - 1, tone(OAK, .99))
    for _ in range(140):
        x, y = rnd.randrange(w), rnd.randrange(plate, h)
        if cv.get(x, y) in PLASTER: cv.set(x, y, PLASTER_LINE if rnd.random() < .7 else tone(PLASTER, .99))
    cv.img.save(path)


def ceiling(path):
    w = h = 384
    rnd = random.Random(61)
    cv = Canvas(w, h)
    clusters = value_noise(w, h, 6, 62)
    for row in range(h // 32):
        start = rnd.randrange(w); x = 0
        while x < w:
            L = rnd.choice([96, 128, 160, 192])
            if w - x - L < 64: L = w - x
            sub = Canvas(L, 32)
            board(sub, 0, 0, L, 32, CEIL, CEIL_LINE, rnd, clusters, along_x=True, knot_chance=.4)
            for j in range(32):
                for i in range(L): cv.set(start + x + i, row * 32 + j, sub.get(i, j))
            x += L
    soot = value_noise(w, h, 24, 63)
    for y in range(h):
        for x in range(w):
            if soot[y][x] > .72 and clusters[y][x] > .45:
                c = cv.get(x, y)
                if c in CEIL[1:]: cv.set(x, y, CEIL[CEIL.index(c) - 1])
    cv.img.save(path)


def trim(path):
    w = h = 384
    rnd = random.Random(71)
    cv = Canvas(w, h)
    clusters = value_noise(w, h, 6, 72)
    for y in range(h):
        for x in range(w):
            cv.set(x, y, tone(OAK, .52 + (clusters[y][(x // 4) % w] - .5) * .32))
    for gy in range(2, h, 6):
        off = gy; pos = rnd.randrange(8)
        while pos < w:
            run = rnd.randrange(10, 40)
            for k in range(run): cv.set(pos + k, off, tone(OAK, 0))
            pos += run + rnd.randrange(4, 16)
            if rnd.random() < .3: off += rnd.choice((-1, 1))
    for _ in range(18):
        x, y, n = rnd.randrange(w), rnd.randrange(h), rnd.randrange(24, 80)
        for k in range(n):
            cv.set(x + k, y, OAK_LINE); cv.set(x + k, y - 1, tone(OAK, .99))
    for _ in range(10):
        cx, cy = rnd.randrange(w), rnd.randrange(h)
        for dk in range(-4, 5):
            for dd in range(-2, 3):
                d = (dk / 4.5) ** 2 + (dd / 2.5) ** 2
                if d <= 1: cv.set(cx + dk, cy + dd, OAK_LINE if d > .55 else tone(OAK, .3))
    cv.img.save(path)


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    out.mkdir(parents=True, exist_ok=True)
    wall(out / "Tavern wall.png")
    ceiling(out / "Tavern ceiling.png")
    trim(out / "Tavern trim.png")
    print("wrote", out)


if __name__ == "__main__":
    main()

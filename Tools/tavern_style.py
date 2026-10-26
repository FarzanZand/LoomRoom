"""Paints the Tavern room style's textures as pixel art in the house style (Docs/Pixel-texture-style.md): a small
palette picked texel by texel (no smooth blends), dark creases and lit edges, muted warm browns, dirt and wear. Every
texture is seamless and at the dungeon's density, 384 texels an architecture tile (3 m, 128 a metre):

  Tavern floor.png    768 x 768, tiling .5: grey slate flagstones, dark grout with grit, worn tops, ale stains, cracks
  Tavern wall.png     384 x 384, tiling 1: a green-painted plank wainscot to 1 m (worn back to the wood at its
                      edges), a dado rail, limewashed plaster between timber posts at the tile's edges, smoke at the
                      top, a wall plate along the top
  Tavern ceiling.png  384 x 384, tiling 1: dark sooty boards, close to neutral
Not everything brown (the user, 2026-10-26: "every layer is just different shades of brown"): stone floor, pale walls,
green paint, so the wood of the furniture and the timbers stands out against them.
  Tavern trim.png     384 x 384, tiling 1: rough dark timber (the kit blocks' beams, frames and rails)

python Tools/tavern_style.py <out dir>
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image


def value_noise(w, h, cx, cy, seed):
    """Tileable value noise in 0..1; cx, cy = texels between lattice points (must divide w and h)."""
    rnd = random.Random(seed)
    nx, ny = w // cx, h // cy
    lat = [[rnd.random() for _ in range(nx)] for _ in range(ny)]
    out = [[0.0] * w for _ in range(h)]
    for y in range(h):
        fy = y / cy; y0 = int(fy); ty = fy - y0; ty = ty * ty * (3 - 2 * ty)
        for x in range(w):
            fx = x / cx; x0 = int(fx); tx = fx - x0; tx = tx * tx * (3 - 2 * tx)
            a = lat[y0 % ny][x0 % nx]; b = lat[y0 % ny][(x0 + 1) % nx]
            c = lat[(y0 + 1) % ny][x0 % nx]; d = lat[(y0 + 1) % ny][(x0 + 1) % nx]
            out[y][x] = (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty
    return out


def shade(c, k):
    return tuple(max(0, min(255, int(round(v * k)))) for v in c)


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def pick(palette, t):
    return palette[max(0, min(len(palette) - 1, int(t * len(palette))))]


OAK = [(62, 40, 26), (76, 50, 32), (90, 60, 38), (104, 70, 44), (118, 81, 51), (132, 92, 58)]
DARK = [(34, 23, 16), (44, 30, 20), (54, 37, 25), (64, 44, 29), (74, 51, 33)]
PLASTER = [(132, 130, 122), (146, 144, 135), (160, 157, 147), (172, 169, 158), (184, 180, 168), (194, 190, 177)]
GREEN = [(26, 42, 34), (32, 52, 41), (38, 61, 48), (44, 70, 55), (52, 80, 62)]
GREYWOOD = [(34, 31, 28), (42, 38, 34), (50, 45, 40), (58, 52, 46), (66, 59, 52)]
SLATE = [(56, 58, 62), (65, 67, 71), (74, 76, 80), (83, 85, 88), (93, 94, 96), (104, 104, 105)]
GAP = (24, 16, 11)
IRON = (52, 50, 48)


def boards(px, w, h, x0, y0, bw, bh, along_x, rnd, grain, palette, seed_tone, joints=True, nails=True, lit=1.12, dark=.74):
    """Planks in a rectangle: rows of boards bh texels thick running along x (or columns along y), random lengths,
    each board its own tone, grain streaks along it, a 1-texel gap, lit leading edge, shaded trailing edge."""
    span, across = (bw, bh) if along_x else (bh, bw)
    count = across // 24 if along_x else across // 24
    size = 24
    for k in range(across // size):
        a0 = k * size
        start = rnd.randrange(span)
        cuts = [start]
        while True:
            nxt = cuts[-1] + rnd.randrange(span // 4, span // 2 + 1)
            if nxt >= start + span - span // 6: break
            cuts.append(nxt)
        for j, c0 in enumerate(cuts):
            c1 = cuts[j + 1] if j + 1 < len(cuts) else start + span
            tone = rnd.uniform(-.18, .18) + seed_tone
            for along in range(c0, c1):
                for t in range(size):
                    if along_x: x, y = x0 + along % span, y0 + a0 + t
                    else: x, y = x0 + a0 + t, y0 + along % span
                    g = grain[y % h][x % w]
                    v = .5 + tone + (g - .5) * .9 + (rnd.random() - .5) * .35
                    col = pick(palette, v)
                    if t == 0: col = GAP
                    elif t == 1: col = shade(col, lit)
                    elif t == size - 1: col = shade(col, dark)
                    elif t == size - 2: col = shade(col, .88)
                    if joints and along == c0: col = GAP
                    elif joints and along == c0 + 1: col = shade(col, .82)
                    px[x % w, y % h] = col
            # nail heads near each end, two per board
            if nails:
                for end in (c0 + 4, c1 - 5):
                    for t in (6, size - 7):
                        if along_x: x, y = x0 + end % span, y0 + a0 + t
                        else: x, y = x0 + a0 + t, y0 + end % span
                        px[x % w, y % h] = IRON
                        px[(x + 1) % w, (y + 1) % h] = shade(px[(x + 1) % w, (y + 1) % h], .7)
                        px[(x - 1) % w, (y - 1) % h] = shade(px[(x - 1) % w, (y - 1) % h], 1.12)


def knots(px, w, h, rnd, n, region=None):
    for _ in range(n):
        if region: x0, y0, x1, y1 = region; cx, cy = rnd.randrange(x0, x1), rnd.randrange(y0, y1)
        else: cx, cy = rnd.randrange(w), rnd.randrange(h)
        rx, ry = rnd.randrange(3, 6), rnd.randrange(2, 3)
        for dy in range(-ry - 2, ry + 3):
            for dx in range(-rx - 3, rx + 4):
                d = (dx / (rx + .01)) ** 2 + (dy / (ry + .01)) ** 2
                x, y = (cx + dx) % w, (cy + dy) % h
                if d <= 1: px[x, y] = shade(DARK[1], .85 + .3 * rnd.random())
                elif d <= 2.2 and rnd.random() < .7: px[x, y] = shade(px[x, y], .8)


def stains(px, w, h, rnd, n, colour, rmin, rmax, strength=.4, region=None):
    for _ in range(n):
        if region: x0, y0, x1, y1 = region; cx, cy = rnd.randrange(x0, x1), rnd.randrange(y0, y1)
        else: cx, cy = rnd.randrange(w), rnd.randrange(h)
        r = rnd.randrange(rmin, rmax)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if math.hypot(dx, dy * 1.2) + rnd.random() * 2.5 <= r:
                    x, y = (cx + dx) % w, (cy + dy) % h
                    px[x, y] = mix(px[x, y], colour, strength)


def grit(px, w, h, rnd, dark_p=.025, light_p=.012, region=None):
    x0, y0, x1, y1 = region or (0, 0, w, h)
    for y in range(y0, y1):
        for x in range(x0, x1):
            n = rnd.random()
            if n < dark_p: px[x, y] = shade(px[x, y], .72)
            elif n > 1 - light_p: px[x, y] = shade(px[x, y], 1.14)


def floor(path):
    w = h = 768
    rnd = random.Random(41)
    clump = value_noise(w, h, 8, 8, 42)
    # rows of flags of uneven height, cut into slabs of uneven width; each its own grey, picked texel by texel
    rows, y = [], 0
    while y < h:
        r = rnd.choice([72, 88, 96, 112, 128])
        if h - y - r < 72: r = h - y
        rows.append((y, r)); y += r
    slab = [[None] * w for _ in range(h)]
    for (y0, rh) in rows:
        start = rnd.randrange(w); x = 0; cuts = []
        while x < w:
            sw = rnd.choice([96, 112, 128, 144, 176])
            if w - x - sw < 96: sw = w - x
            cuts.append((x, sw)); x += sw
        for (x0, sw) in cuts:
            tone = rnd.uniform(-1.1, 1.1)
            for yy in range(y0, y0 + rh):
                for xx in range(x0, x0 + sw):
                    slab[yy][(xx + start) % w] = (tone, xx - x0, yy - y0, sw, rh)
    img = Image.new("RGB", (w, h)); px = img.load()
    grout, sand = (30, 30, 32), (112, 98, 72)
    for y in range(h):
        for x in range(w):
            tone, lx, ly, sw, rh = slab[y][x]
            k = 2.5 + tone + (clump[y][x] - .5) * 1.4 + (rnd.random() - .5) * 1.6
            c = SLATE[max(0, min(len(SLATE) - 1, int(round(k))))]
            if lx < 2 or ly < 2: c = sand if rnd.random() < .18 else grout
            elif lx == 2 or ly == 2: c = shade(c, 1.14)                # the lit lip
            elif lx == sw - 1 or ly == rh - 1: c = shade(c, .7)        # the shaded edge into the next joint
            elif lx == sw - 2 or ly == rh - 2: c = shade(c, .86)
            px[x, y] = c
    # worn smooth where feet go: hard-edged lighter patches; ale and soot stains; cracks; grit
    wear = value_noise(w, h, 24, 24, 43)
    for y in range(h):
        for x in range(w):
            if wear[y][x] > .7 and rnd.random() < .8: px[x, y] = shade(px[x, y], 1.08)
    stains(px, w, h, rnd, 16, (30, 24, 20), 4, 12, .4)
    stains(px, w, h, rnd, 5, (70, 52, 26), 3, 7, .3)
    for _ in range(30):
        x, y = rnd.randrange(w), rnd.randrange(h)
        for _ in range(rnd.randrange(8, 30)):
            px[x % w, y % h] = grout
            px[(x + 1) % w, (y + 1) % h] = shade(px[(x + 1) % w, (y + 1) % h], 1.1)
            x += rnd.choice((-1, 0, 1, 1)); y += rnd.choice((0, 1, 1))
    grit(px, w, h, rnd)
    img.save(path)


def wall(path):
    w = h = 384
    rnd = random.Random(51)
    img = Image.new("RGB", (w, h)); px = img.load()
    plate, rail_top, rail_h, post = 18, h - 128 - 12, 12, 14       # rows from the top (PIL y 0 = the wall's top)
    # plaster: muted, chosen texel by texel, smoke darkening in steps toward the top, cracks and bare patches
    pg = value_noise(w, h, 8, 8, 52)
    for y in range(plate, rail_top):
        smoke = max(0.0, 1 - (y - plate) / 110)                       # 1 at the top, 0 lower down
        for x in range(w):
            v = .55 + (pg[y][x] - .5) * .5 + (rnd.random() - .5) * .6 - (.45 if rnd.random() < smoke * .8 else 0)
            px[x, y] = pick(PLASTER, v)
    # one bare patch low on the plaster, ragged, the wattle showing as rows of dark lath with a crease round it
    cx, cy = rnd.randrange(post + 60, w - post - 60), rail_top - 26
    edge = [rnd.randrange(14, 26) for _ in range(64)]
    for dy in range(-12, 13):
        for dx in range(-30, 31):
            ang = int((math.atan2(dy * 2.2, dx) + math.pi) / (2 * math.pi) * 63)
            r = edge[ang] * (1 - abs(dy) / 14)
            x, y = cx + dx, cy + dy
            if abs(dx) <= r - 2: px[x, y] = GAP if y % 4 == 0 else shade(DARK[2], .85 + .3 * rnd.random())
            elif abs(dx) <= r: px[x, y] = shade(px[x, y], .7)
    # cracks
    for _ in range(9):
        x, y = rnd.randrange(post, w - post), rnd.randrange(plate, rail_top)
        for _ in range(rnd.randrange(10, 34)):
            if plate <= y < rail_top:
                px[x % w, y] = shade(PLASTER[0], .62)
                px[(x + 1) % w, y] = shade(px[(x + 1) % w, y], 1.06)
            x += rnd.choice((-1, 0, 1)); y += rnd.choice((1, 1, 0, -1))
    grit(px, w, h, rnd, .02, .01, (0, plate, w, rail_top))
    # wainscot: vertical planks from the rail down
    grain = value_noise(w, h, 2, 24, 53)
    sub = Image.new("RGB", (w, 128 + 12)); sp = sub.load()
    g2 = [[grain[y][x] for x in range(w)] for y in range(140)]
    boards(sp, w, 140, 0, 0, w, 140, False, rnd, g2, GREEN, 0, joints=False, nails=True)
    wear = value_noise(w, 140, 8, 4, 55)
    for y in range(140):
        for x in range(w):
            c = sp[x, y]
            t = x % 24
            # paint worn through to the dark wood on the boards' edges and in chips, more toward the floor
            if (t in (2, 21) and rnd.random() < .45) or wear[y][x] + y / 700 > .9: c = pick(DARK, .3 + rnd.random() * .5)
            px[x, h - 140 + y] = c
    # dado rail and the wall plate: dark timber with a lit top and a shaded underside
    tg = value_noise(w, h, 32, 2, 54)
    def timber(y0, y1, vertical=False, x0=0, x1=w):
        for y in range(y0, y1):
            for x in range(x0, x1):
                v = .45 + (tg[y][x] - .5) * .8 + (rnd.random() - .5) * .35
                c = pick(DARK, v)
                if (y == y0 and not vertical) or (x == x0 and vertical): c = shade(c, 1.25)
                elif (y == y1 - 1 and not vertical) or (x == x1 - 1 and vertical): c = shade(c, .6)
                px[x % w, y] = c
    timber(rail_top, rail_top + rail_h)
    for x in range(w): px[x, rail_top + rail_h] = shade(px[x, rail_top + rail_h], .7)   # its shadow on the planks
    # posts at the tile's edges (half on each side, so they meet across the seam), over plaster and rail
    for y in range(plate, rail_top):
        for x in list(range(0, post // 2)) + list(range(w - post // 2, w)):
            v = .4 + (tg[(x * 7) % h][y % w] - .5) * .8 + (rnd.random() - .5) * .35
            c = pick(DARK, v)
            if x == w - post // 2: c = shade(c, 1.22)
            elif x == post // 2 - 1: c = shade(c, .6)
            px[x, y] = c
        px[post // 2, y] = shade(px[post // 2, y], .78)                # plaster's crease against the post
        px[w - post // 2 - 1, y] = shade(px[w - post // 2 - 1, y], .78)
    # pegs where the posts meet the plate and rail
    for x in (2, w - 3):
        for y in (plate + 4, rail_top - 5):
            px[x, y] = GAP; px[x + 1, y + 1] = shade(DARK[3], 1.2)
    timber(0, plate)
    for x in range(w): px[x, plate] = shade(px[x, plate], .62)            # the plate's shadow on the plaster
    img.save(path)


def ceiling(path):
    w = h = 384
    rnd = random.Random(61)
    grain = value_noise(w, h, 48, 2, 62)
    img = Image.new("RGB", (w, h)); px = img.load()
    boards(px, w, h, 0, 0, w, h, True, rnd, grain, GREYWOOD, -.04, nails=False, lit=1.08, dark=.7)
    soot = value_noise(w, h, 16, 16, 63)
    for y in range(h):
        for x in range(w):
            if soot[y][x] > .72: px[x, y] = shade(px[x, y], .84)
    knots(px, w, h, rnd, 10)
    grit(px, w, h, rnd, .03, .006)
    img.save(path)


def trim(path):
    w = h = 384
    rnd = random.Random(71)
    grain = value_noise(w, h, 64, 3, 72)
    fine = value_noise(w, h, 8, 2, 73)
    img = Image.new("RGB", (w, h)); px = img.load()
    for y in range(h):
        for x in range(w):
            v = .5 + (grain[y][x] - .5) * 1.0 + (fine[y][x] - .5) * .4 + (rnd.random() - .5) * .3
            px[x, y] = pick(DARK + [OAK[1]], v)
    # checks (long splits along the grain) with a lit lip, adze marks
    for _ in range(26):
        x, y, n = rnd.randrange(w), rnd.randrange(h), rnd.randrange(20, 70)
        for i in range(n):
            px[(x + i) % w, y % h] = GAP
            px[(x + i) % w, (y - 1) % h] = shade(px[(x + i) % w, (y - 1) % h], 1.15)
            if rnd.random() < .15: y += rnd.choice((-1, 1))
    for _ in range(60):
        x, y = rnd.randrange(w), rnd.randrange(h)
        for i in range(rnd.randrange(5, 10)):
            px[(x + i) % w, y] = shade(px[(x + i) % w, y], 1.12)
            px[(x + i) % w, (y + 1) % h] = shade(px[(x + i) % w, (y + 1) % h], .82)
    knots(px, w, h, rnd, 8)
    grit(px, w, h, rnd, .02, .008)
    img.save(path)


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    out.mkdir(parents=True, exist_ok=True)
    floor(out / "Tavern floor.png")
    wall(out / "Tavern wall.png")
    ceiling(out / "Tavern ceiling.png")
    trim(out / "Tavern trim.png")
    print("wrote", out)


if __name__ == "__main__":
    main()

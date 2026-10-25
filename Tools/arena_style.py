"""Paints the Arena room style's textures as pixel art (one texel per painted pixel), seamless:
   the pit's sand (512x512), grey stone paving for the other floors (512x512), grey stone block wall (256x192) and a
   cut-stone trim (256x256), the stone weathered with moss
   in its joints and lichen on its faces (hints of green, the user's choice 2026-10-25; it was sandstone before).

   python Tools/arena_style.py <out dir>

Writes "Arena sand.png", "Arena floor.png", "Arena wall.png" and "Arena trim.png" into <out dir>. Same density as the Church style
(256 texels per 2 m of floor at the material's 1.5 tiling).
"""
import math
import random
import sys
from pathlib import Path

from PIL import Image


def value_noise(w, h, cell, seed):
    """Tileable value noise in 0..1, cell = texels between lattice points (must divide w and h)."""
    rnd = random.Random(seed)
    cw, ch = w // cell, h // cell
    lattice = [[rnd.random() for _ in range(cw)] for _ in range(ch)]
    out = [[0.0] * w for _ in range(h)]
    for y in range(h):
        fy = y / cell; y0 = int(fy); ty = fy - y0; ty = ty * ty * (3 - 2 * ty)
        for x in range(w):
            fx = x / cell; x0 = int(fx); tx = fx - x0; tx = tx * tx * (3 - 2 * tx)
            a = lattice[y0 % ch][x0 % cw]; b = lattice[y0 % ch][(x0 + 1) % cw]
            c = lattice[(y0 + 1) % ch][x0 % cw]; d = lattice[(y0 + 1) % ch][(x0 + 1) % cw]
            out[y][x] = (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty
    return out


def mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def shade(c, k):
    return tuple(max(0, min(255, int(round(v * k)))) for v in c)


def sand(path, size=512):
    """Packed arena sand in the house style: a small palette of sand tones picked texel by texel (no smooth blends),
    raked furrows as crisp lines with a lit lip and a shaded trough, pebbles with a shadow under them, grit, damp
    patches with hard edges and a few old stains. Seamless; 512 texels repeat every two board cells (tiling .75), so
    the pit doesn't read as a grid of identical cells. The pit's floor (the Arena pit style); the rest is stone."""
    w = h = size
    rnd = random.Random(7)
    broad = value_noise(w, h, 32, 1)     # only picks which tones dominate an area; never blended in
    clump = value_noise(w, h, 8, 2)
    wander = value_noise(w, h, 64, 3)
    tones = [(118, 92, 60), (138, 108, 70), (154, 122, 80), (168, 134, 90), (182, 148, 100), (196, 162, 112)]
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        for x in range(w):
            t = broad[y][x] * .45 + clump[y][x] * .35 + rnd.random() * .45     # strong per-texel jitter
            k = max(0, min(len(tones) - 1, int(t * len(tones) * .82)))
            px[x, y] = tones[k]
    # Raked furrows: crisp wandering lines 12 texels apart, a shaded trough with a lit lip above it, broken here and there.
    for y in range(h):
        for x in range(w):
            v = (y + wander[y][x] * 20 + math.sin(x * 2 * math.pi / 128) * 3) % 12
            if clump[y][x] < .3: continue                      # scuffed flat where people walked
            if v < 1: px[x, y] = shade(px[x, y], .74)
            elif v < 2: px[x, y] = shade(px[x, y], .86)
            elif v < 3: px[x, y] = shade(px[x, y], 1.1)
    # Damp, trodden patches: hard-edged darker blobs.
    for _ in range(9 * (w * h) // 65536):
        cx, cy, r = rnd.randrange(w), rnd.randrange(h), rnd.randrange(6, 14)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                xx, yy = (cx + dx) % w, (cy + dy) % h
                if math.hypot(dx, dy * 1.3) + clump[yy][xx] * 6 <= r: px[xx, yy] = shade(px[xx, yy], .82)
    # Old stains, dark rust red, blotchy.
    for _ in range(4 * (w * h) // 65536):
        cx, cy, r = rnd.randrange(w), rnd.randrange(h), rnd.randrange(3, 7)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                xx, yy = (cx + dx) % w, (cy + dy) % h
                if math.hypot(dx, dy) + rnd.random() * 2.5 <= r: px[xx, yy] = mix(px[xx, yy], (92, 46, 34), .45)
    # Grit: single dark and bright grains.
    for y in range(h):
        for x in range(w):
            n = rnd.random()
            if n < .035: px[x, y] = shade(px[x, y], .7)
            elif n > .975: px[x, y] = shade(px[x, y], 1.15)
    # Pebbles: grey-brown stones, lit on top, a dark texel or two of shadow below.
    for _ in range(70 * (w * h) // 65536):
        cx, cy, r = rnd.randrange(w), rnd.randrange(h), rnd.choice([0, 1, 1, 1, 2])
        tone = rnd.choice([(128, 118, 104), (108, 100, 90), (150, 140, 124), (96, 84, 70)])
        for dx in range(-r - 1, r + 2):
            px[(cx + dx) % w, (cy + r + 1) % h] = shade(px[(cx + dx) % w, (cy + r + 1) % h], .62)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy <= r * r + r:
                    px[(cx + dx) % w, (cy + dy) % h] = shade(tone, 1.18 if dy < 0 else .9 if dy > 0 else 1)
    img.save(path)


def blocks(path, w, h, course, seed, light, dark, mortar, joint_every=(40, 76), moss=None, lichen=None):
    """Courses of cut blocks: each block its own tone, a lit top edge, a shaded bottom, chips; seamless.
    moss: (colour, amount) creeping out of the joints, heavier along each course's foot; lichen: (colour, amount)
    blotches on the faces. Both follow broad noise, so some stretches of wall are green and others bare."""
    rnd = random.Random(seed)
    grain = value_noise(w, h, 8, seed + 1)
    fine = value_noise(w, h, 4, seed + 2)
    damp = value_noise(w, h, 32, seed + 3)
    spot = value_noise(w, h, 8, seed + 4)
    img = Image.new("RGB", (w, h))
    px = img.load()
    rows = h // course
    for row in range(rows):
        y0 = row * course
        # This course's joints (wrapping round, so the tile repeats): which block each column belongs to.
        start = rnd.randrange(0, 30)
        joints = [start]
        while True:
            nxt = joints[-1] + rnd.randrange(*joint_every)
            if nxt >= start + w - joint_every[0] // 2: break
            joints.append(nxt)
        block_of, left_of = [0] * w, [0] * w
        for xx in range(w):
            u = (xx - start) % w
            k = max(i for i, jx in enumerate(joints) if jx - start <= u)
            block_of[xx], left_of[xx] = k, u - (joints[k] - start)
        tones = [rnd.uniform(-.12, .12) for _ in joints]
        for y in range(y0, y0 + course):
            for xx in range(w):
                k, local_x, local_y = block_of[xx], left_of[xx], y - y0
                t = .5 + tones[k] + (grain[y][xx] - .5) * .5 + (fine[y][xx] - .5) * .2
                c = mix(dark, light, max(0, min(1, t)))
                if local_y == 0 or local_x == 0: c = mortar
                elif local_y == 1: c = shade(c, 1.12)
                elif local_y >= course - 2: c = shade(c, .82)
                elif local_x == 1: c = shade(c, 1.06)
                if rnd.random() < .012: c = shade(c, .7)
                if moss:
                    # moss: in and beside the joints, more toward the course's foot, where the wall stays damp
                    edge = min(local_y, course - 1 - local_y, local_x, 3) if local_y and local_x else 0
                    near = (1 - edge / 3) + (local_y / course) * .35
                    m = (damp[y][xx] - .45) * 2.2 + near * moss[1] + (fine[y][xx] - .5) * .8
                    if m > .9: c = mix(c, shade(moss[0], .8 + fine[y][xx] * .4), min(.85, (m - .9) * 1.6 + .3))
                if lichen and spot[y][xx] * damp[y][xx] > 1 - lichen[1] and rnd.random() < .75:
                    c = mix(c, shade(lichen[0], .85 + fine[y][xx] * .3), .55)
                px[xx, y] = c
    # Chips knocked out of the edges.
    for _ in range(int(w * h / 900)):
        cx, cy = rnd.randrange(w), rnd.randrange(0, h, course)
        for dx in range(rnd.randrange(2, 5)):
            for dy in range(rnd.randrange(1, 3)):
                px[(cx + dx) % w, (cy + dy) % h] = shade(mortar, 1.15)
    img.save(path)


def flagstones(path, size=512, seed=31):
    """The arena floor: grey stone paving with hints of green (the user's choice 2026-10-25; it was yellow sand).
    Rows of slabs of uneven height and width; each slab its own grey, a palette picked texel by texel (no smooth
    blends), a lit top-left edge and a shaded bottom-right one, dark joints with moss creeping out of them and
    sand ground into them, cracks, chips and a few old stains. 512 texels repeat every two board cells (tiling .75),
    so the floor doesn't read as a grid of identical cells. Seamless."""
    w = h = size
    rnd = random.Random(seed)
    damp = value_noise(w, h, 64, seed + 1)
    clump = value_noise(w, h, 8, seed + 2)
    greys = [(78, 80, 76), (92, 94, 89), (106, 107, 101), (119, 120, 113), (132, 132, 124), (146, 145, 136)]
    moss = [(52, 70, 38), (66, 88, 46), (84, 106, 56), (104, 124, 68)]
    joint, grit = (44, 45, 42), (128, 112, 84)
    # Rows that add up to the tile's height, then slabs along each row; slab id and its local coordinates per texel.
    rows, y = [], 0
    while y < h:
        r = rnd.choice([40, 48, 56, 64, 72])
        if h - y - r < 40: r = h - y
        rows.append((y, r)); y += r
    slab = [[None] * w for _ in range(h)]
    sid = 0
    for (y0, rh) in rows:
        start = rnd.randrange(w); x = 0; cuts = []
        while x < w:
            sw = rnd.choice([56, 72, 88, 104, 120])
            if w - x - sw < 48: sw = w - x
            cuts.append((x, sw)); x += sw
        for (x0, sw) in cuts:
            tone = rnd.uniform(-1.2, 1.2)
            for yy in range(y0, y0 + rh):
                for xx in range(x0, x0 + sw):
                    slab[yy][(xx + start) % w] = (sid, tone, xx - x0, yy - y0, sw, rh)
            sid += 1
    img = Image.new("RGB", (w, h)); px = img.load()
    for y in range(h):
        for x in range(w):
            _, tone, lx, ly, sw, rh = slab[y][x]
            k = 2.4 + tone + (clump[y][x] - .5) * 1.6 + (rnd.random() - .5) * 1.8
            c = greys[max(0, min(len(greys) - 1, int(round(k))))]
            edge = min(lx, ly, sw - 1 - lx, rh - 1 - ly)
            if lx == 0 or ly == 0: c = joint
            elif ly == 1 or lx == 1: c = shade(c, 1.14)                      # lit top and left lip
            elif ly == rh - 1 or lx == sw - 1: c = shade(c, .74)            # shaded bottom and right
            elif ly == rh - 2 or lx == sw - 2: c = shade(c, .88)
            # moss out of the joints where it is damp; sand ground into the joints elsewhere
            wet = (damp[y][x] - .5) * 2.4 + (clump[y][x] - .5)
            if edge <= 2 and wet > .15 and rnd.random() < .8: c = moss[min(3, max(0, int((wet + rnd.random()) * 2)))]
            elif edge <= 3 and wet > .55 and rnd.random() < .35: c = moss[rnd.randrange(3)]
            elif lx == 0 or ly == 0:
                if rnd.random() < .3: c = grit
            px[x, y] = c
    # Lichen spots on the faces, hard-edged, pale green-grey.
    for _ in range(26):
        cx, cy, r = rnd.randrange(w), rnd.randrange(h), rnd.randrange(2, 6)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if math.hypot(dx, dy) + rnd.random() * 2 <= r: px[(cx + dx) % w, (cy + dy) % h] = mix(px[(cx + dx) % w, (cy + dy) % h], (126, 140, 96), .5)
    # Cracks: short jagged dark lines.
    for _ in range(22):
        x, y = rnd.randrange(w), rnd.randrange(h)
        for _ in range(rnd.randrange(8, 26)):
            px[x % w, y % h] = joint
            px[(x + 1) % w, (y + 1) % h] = shade(px[(x + 1) % w, (y + 1) % h], 1.1)
            x += rnd.choice((-1, 0, 1, 1)); y += rnd.choice((0, 1, 1))
    # Old stains, rust red, and grit specks.
    for _ in range(5):
        cx, cy, r = rnd.randrange(w), rnd.randrange(h), rnd.randrange(4, 9)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if math.hypot(dx, dy) + rnd.random() * 3 <= r: px[(cx + dx) % w, (cy + dy) % h] = mix(px[(cx + dx) % w, (cy + dy) % h], (84, 44, 34), .4)
    for y in range(h):
        for x in range(w):
            n = rnd.random()
            if n < .02: px[x, y] = shade(px[x, y], .72)
            elif n > .985: px[x, y] = shade(px[x, y], 1.16)
    img.save(path)


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    out.mkdir(parents=True, exist_ok=True)
    sand(out / "Arena sand.png")            # the pit's floor
    flagstones(out / "Arena floor.png")     # every other floor: the landing, the passage
    moss, lichen = (74, 96, 52), (128, 146, 92)
    blocks(out / "Arena wall.png", 256, 192, 32, 11, (152, 150, 140), (100, 99, 94), (54, 56, 50), moss=(moss, .55), lichen=(lichen, .2))
    blocks(out / "Arena trim.png", 256, 256, 64, 21, (146, 145, 136), (98, 97, 92), (52, 54, 48), (56, 96), moss=(moss, .45), lichen=(lichen, .16))
    print("wrote", out)


if __name__ == "__main__":
    main()

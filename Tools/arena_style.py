"""Paints the Arena room style's textures as pixel art (one texel per painted pixel), seamless:
   sand floor (256x256), sandstone block wall (256x192) and a cut-stone trim (256x256).

   python Tools/arena_style.py <out dir>

Writes "Arena sand.png", "Arena wall.png" and "Arena trim.png" into <out dir>. Same density as the Church style
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


def sand(path):
    w = h = 256
    rnd = random.Random(7)
    low = value_noise(w, h, 64, 1)
    mid = value_noise(w, h, 16, 2)
    fine = value_noise(w, h, 4, 3)
    light, dark = (188, 152, 102), (136, 104, 68)
    img = Image.new("RGB", (w, h))
    px = img.load()
    for y in range(h):
        for x in range(w):
            t = low[y][x] * .45 + mid[y][x] * .35 + fine[y][x] * .2
            c = mix(dark, light, max(0, min(1, (t - .3) / .45)))
            # Raked furrows: faint stripes that wander.
            r = math.sin((y + mid[y][x] * 18) * 2 * math.pi / 16)
            if r > .82: c = shade(c, .9)
            n = rnd.random()
            if n < .02: c = shade(c, .78)          # dark grains
            elif n > .985: c = shade(c, 1.12)      # bright grains
            px[x, y] = c
    # Pebbles and a few old stains (wrapping round the edges).
    for _ in range(46):
        cx, cy, r = rnd.randrange(w), rnd.randrange(h), rnd.choice([1, 1, 2])
        tone = rnd.choice([(150, 132, 112), (128, 116, 104), (186, 170, 150)])
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                if dx * dx + dy * dy <= r * r: px[(cx + dx) % w, (cy + dy) % h] = shade(tone, 1 - .08 * dy)
    for _ in range(5):
        cx, cy, r = rnd.randrange(w), rnd.randrange(h), rnd.randrange(5, 11)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                d = math.hypot(dx, dy) + fine[(cy + dy) % h][(cx + dx) % w] * 4
                if d <= r: px[(cx + dx) % w, (cy + dy) % h] = mix(px[(cx + dx) % w, (cy + dy) % h], (120, 70, 50), .35 * (1 - d / r) + .1)
    img.save(path)


def blocks(path, w, h, course, seed, light, dark, mortar, joint_every=(40, 76)):
    """Courses of cut blocks: each block its own tone, a lit top edge, a shaded bottom, chips; seamless."""
    rnd = random.Random(seed)
    grain = value_noise(w, h, 8, seed + 1)
    fine = value_noise(w, h, 4, seed + 2)
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
                px[xx, y] = c
    # Chips knocked out of the edges.
    for _ in range(int(w * h / 900)):
        cx, cy = rnd.randrange(w), rnd.randrange(0, h, course)
        for dx in range(rnd.randrange(2, 5)):
            for dy in range(rnd.randrange(1, 3)):
                px[(cx + dx) % w, (cy + dy) % h] = shade(mortar, 1.15)
    img.save(path)


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else ".")
    out.mkdir(parents=True, exist_ok=True)
    sand(out / "Arena sand.png")
    blocks(out / "Arena wall.png", 256, 192, 32, 11, (206, 172, 118), (158, 124, 84), (92, 70, 52))
    blocks(out / "Arena trim.png", 256, 256, 64, 21, (196, 168, 124), (150, 122, 88), (88, 68, 52), (56, 96))
    print("wrote", out)


if __name__ == "__main__":
    main()

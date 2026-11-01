"""Sprites for the portrait dialogue box and the versus screen (PortraitDialogueSetup):
  Dialogue box.png  - 96 x 40, 9-sliced at 8: warm dark brown, lighter at the top, a dark rim and a thin gold line
                      inset a few pixels, the gold brighter along the top (lit from above), a texel of grain.
  Versus band.png   - 64 x 64: white, fully opaque at its inner edge and fading out raggedly toward the outer edge,
                      tinted per side by the screen.
  Soft glow.png     - 128 x 128: white, opaque in the middle and fading smoothly to nothing at the rim (the glows behind
                      each side of the cinematic versus screen, tinted there; drawn smooth).
  Medallion.png     - 256 x 256: a black disc in a gold ring with a thin inner line (the VS medallion; drawn smooth).
  Versus aura.png   - 256 x 512: white, an aura rising behind a figure: widest and strongest at its foot, narrowing and
                      fading as it climbs, its edges broken into wisps that rise (the red and blue behind each side of the
                      versus screen, tinted there; drawn smooth).
Usage: python Tools/dialogue_box_sprites.py <out dir>"""
import math, os, random, sys
from PIL import Image

def box(path):
    w, h = 96, 40
    img = Image.new("RGBA", (w, h))
    rnd = random.Random(3)
    top, bottom = (66, 45, 26), (20, 12, 7)
    for y in range(h):
        k = y / (h - 1)
        base = [round(top[i] + (bottom[i] - top[i]) * k ** .8) for i in range(3)]
        for x in range(w):
            g = rnd.randint(-3, 3)
            img.putpixel((x, y), (base[0] + g, base[1] + g, max(0, base[2] + g), 236))
    for x in range(w):
        for y in (0, h - 1): img.putpixel((x, y), (10, 6, 4, 255))
    for y in range(h):
        for x in (0, w - 1): img.putpixel((x, y), (10, 6, 4, 255))
    gold = (201, 164, 92)
    for x in range(4, w - 4):
        img.putpixel((x, 3), tuple(min(255, c + 30) for c in gold) + (235,))
        img.putpixel((x, h - 4), tuple(int(c * .7) for c in gold) + (200,))
    for y in range(4, h - 4):
        k = 1 - (y - 4) / (h - 8) * .35
        for x in (3, w - 4): img.putpixel((x, y), tuple(int(c * k) for c in gold) + (215,))
    img.save(path)

def band(path):
    s = 64
    img = Image.new("RGBA", (s, s))
    rnd = random.Random(7)
    for y in range(s):
        jag = rnd.randint(-2, 2)
        for x in range(s):
            k = max(0.0, min(1.0, 1 - (x + jag) / (s * .95)))
            a = int(255 * (k ** .7))
            img.putpixel((x, y), (255, 255, 255, a))
    img.save(path)

def soft_glow(path):
    s = 128
    img = Image.new("RGBA", (s, s))
    for y in range(s):
        for x in range(s):
            r = ((x - s / 2 + .5) ** 2 + (y - s / 2 + .5) ** 2) ** .5 / (s / 2)
            a = math.exp(-(r * 2.4) ** 2) * max(0.0, 1 - r) ** .5   # a gaussian, reaching nothing at the rim
            img.putpixel((x, y), (255, 255, 255, int(255 * a)))
    img.save(path)

def aura(path):
    w, h = 256, 512
    rnd = random.Random(11)
    # Smooth value noise: a coarse random grid, eased between its points.
    def grid(cols, rows):
        return [[rnd.random() for _ in range(cols + 1)] for _ in range(rows + 1)]
    def sample(g, u, v):
        cols, rows = len(g[0]) - 1, len(g) - 1
        x, y = u * cols, v * rows
        x0, y0 = min(int(x), cols - 1), min(int(y), rows - 1)
        fx, fy = x - x0, y - y0
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        a = g[y0][x0] + (g[y0][x0 + 1] - g[y0][x0]) * fx
        b = g[y0 + 1][x0] + (g[y0 + 1][x0 + 1] - g[y0 + 1][x0]) * fx
        return a + (b - a) * fy
    edge, wisps, flicker = grid(6, 10), grid(14, 6), grid(5, 5)
    img = Image.new("RGBA", (w, h))
    for y in range(h):
        k = y / (h - 1)                      # 0 at the top, 1 at the foot
        for x in range(w):
            u = x / (w - 1)
            # Half its width at this height: narrow up top, wide at the foot, the edge wavering.
            half = .12 + .22 * k ** .8
            half *= .82 + .36 * sample(edge, u, k)
            d = abs(u - .5) / max(half, 1e-3)
            across = math.exp(-(d * 1.35) ** 2)
            # Tongues rising: long vertical streaks stretched upward, strongest toward the edges and the top.
            streak = sample(wisps, u, (k * .35) % 1.0)
            across *= .7 + .6 * streak * (.4 + .6 * min(1.0, d))
            # Strong at the foot, fading out as it climbs, gone at the top.
            up = (k ** 1.15) * (1 - math.exp(-k * 9))
            up *= .85 + .3 * sample(flicker, u, k)
            # Nothing reaches the picture's own sides (a cut there showed as a hard line).
            side = min(u, 1 - u) / .2
            across *= side * side * (3 - 2 * side) if side < 1 else 1.0
            a = max(0.0, min(1.0, across * up * 1.15))
            img.putpixel((x, y), (255, 255, 255, int(255 * a)))
    img.save(path)

def medallion(path):
    big = 1024
    from PIL import ImageDraw
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    gold, dark = (232, 196, 110, 255), (8, 6, 6, 245)
    d.ellipse((8, 8, big - 8, big - 8), fill=gold)
    d.ellipse((44, 44, big - 44, big - 44), fill=dark)
    d.ellipse((80, 80, big - 80, big - 80), outline=(232, 196, 110, 170), width=10)
    img.resize((256, 256), Image.LANCZOS).save(path)

if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    box(os.path.join(out, "Dialogue box.png")); band(os.path.join(out, "Versus band.png"))
    soft_glow(os.path.join(out, "Soft glow.png")); medallion(os.path.join(out, "Medallion.png"))
    aura(os.path.join(out, "Versus aura.png"))
    print("wrote Dialogue box.png, Versus band.png, Soft glow.png, Medallion.png and Versus aura.png to", out)

"""Sprites for the portrait dialogue box and the versus screen (PortraitDialogueSetup):
  Dialogue box.png  - 96 x 40, 9-sliced at 8: warm dark brown, lighter at the top, a dark rim and a thin gold line
                      inset a few pixels, the gold brighter along the top (lit from above), a texel of grain.
  Versus band.png   - 64 x 64: white, fully opaque at its inner edge and fading out raggedly toward the outer edge,
                      tinted per side by the screen.
  Soft glow.png     - 128 x 128: white, opaque in the middle and fading smoothly to nothing at the rim (the glows behind
                      each side of the cinematic versus screen, tinted there; drawn smooth).
  Medallion.png     - 256 x 256: a black disc in a gold ring with a thin inner line (the VS medallion; drawn smooth).
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
    print("wrote Dialogue box.png, Versus band.png, Soft glow.png and Medallion.png to", out)

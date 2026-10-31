"""The disco boss's show textures (Levels/Dungeon1/Biomes/Disco), at the dungeon's 128 texels a metre.
  Dance floor tile.png: one 1 m light-up floor tile, 128 x 128, grey (the floor tints it): a dark grout edge, a lighter
  bevel inside it, a frosted glass panel grained texel to texel, darker toward its lower right, with a cross of four
  panes. Drawn additively, so black is no light.
  Disco spots.png: the disco ball's light cookie, 256 x 256: hard little dots of light scattered on black (a jittered
  grid, a few doubled into short streaks), the same in colour and alpha.
Usage: python Tools/disco_show_textures.py <out dir>"""
import os, random, sys
from PIL import Image


def tile(rnd):
    n = 128
    img = Image.new("RGB", (n, n))
    for y in range(n):
        for x in range(n):
            edge = min(x, y, n - 1 - x, n - 1 - y)
            if edge < 6: v = 0                                    # grout: no light
            elif edge < 9: v = 235 if (x < y) else 205            # bevel: lit on its upper left
            else:
                # Four panes with a thin frame between them, frosted: grain texel to texel, darker to the lower right.
                if abs(x - n // 2) <= 1 or abs(y - n // 2) <= 1: v = 120
                else: v = 215 - (x + y - 18) * .22 + rnd.uniform(-14, 14)
                if (x % 7 == 3 and y % 7 == 3) and rnd.random() < .5: v += 25   # frost specks
            v = max(0, min(255, int(v)))
            img.putpixel((x, y), (v, v, v))
    return img


def spots(rnd):
    n = 256
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    step = 16
    for gy in range(0, n, step):
        for gx in range(0, n, step):
            cx = gx + rnd.randint(3, step - 4); cy = gy + rnd.randint(3, step - 4)
            r = rnd.choice((1, 1, 2, 2, 2, 3))
            streak = rnd.random() < .2
            for y in range(-r, r + 1):
                for x in range(-r - (2 if streak else 0), r + 1 + (2 if streak else 0)):
                    if (abs(x) - (2 if streak else 0)) ** 2 + y * y <= r * r + r:
                        px, py = cx + x, cy + y
                        if 0 <= px < n and 0 <= py < n: img.putpixel((px, py), (255, 255, 255, 255))
    # Fewer dots toward the cookie's edge, where the spot light's cone fades anyway: none in the outer ring.
    for y in range(n):
        for x in range(n):
            if (x - n / 2) ** 2 + (y - n / 2) ** 2 > (n * .48) ** 2: img.putpixel((x, y), (0, 0, 0, 0))
    return img


def main(out):
    os.makedirs(out, exist_ok=True)
    tile(random.Random(5)).save(os.path.join(out, "Dance floor tile.png"))
    spots(random.Random(9)).save(os.path.join(out, "Disco spots.png"))
    print("wrote Dance floor tile.png and Disco spots.png to", out)


if __name__ == "__main__":
    main(sys.argv[1])

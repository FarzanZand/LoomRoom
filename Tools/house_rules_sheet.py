"""The house rules sheet taped to the bedroom's east door, painted texel by texel at the bedroom's pixel size.

python Tools/house_rules_sheet.py <out.png> [rules]
  rules: how many rules are written on it (1 by default); each is a number and a line or two of hand-writing.

45 x 61 texels = 7 x 9.5 room units at 6.4 texels a unit (the door and the painting measure 6.4). Off-white paper with
grain and a soft fold, a strip of tape at each top corner, HOUSE RULES lettered in dark ink at the top, then the rules:
a number and wobbling lines of writing (too small to read from the room; the read screen shows the words).
"""
import random
import sys
from PIL import Image

W, H = 45, 61
INK = (38, 40, 58)
FONT = {  # 3 x 5 capitals and digits, rows top to bottom
    "H": ["101", "101", "111", "101", "101"], "O": ["111", "101", "101", "101", "111"],
    "U": ["101", "101", "101", "101", "111"], "S": ["111", "100", "111", "001", "111"],
    "E": ["111", "100", "110", "100", "111"], "R": ["110", "101", "110", "101", "101"],
    "L": ["100", "100", "100", "100", "111"], "1": ["010", "110", "010", "010", "111"],
    "2": ["111", "001", "111", "100", "111"], "3": ["111", "001", "011", "001", "111"],
    "4": ["101", "101", "111", "001", "001"], "5": ["111", "100", "111", "001", "111"],
    "6": ["111", "100", "111", "101", "111"], "7": ["111", "001", "010", "010", "010"],
    "8": ["111", "101", "111", "101", "111"], "9": ["111", "101", "111", "001", "111"],
    "0": ["111", "101", "101", "101", "111"], ".": ["000", "000", "000", "000", "010"], " ": ["000"] * 5,
}


def paper(rng):
    img = Image.new("RGBA", (W, H))
    px = img.load()
    for y in range(H):
        for x in range(W):
            g = rng.randint(-6, 6)
            # A soft fold across the middle, a little darker toward the bottom edge.
            fold = -7 if abs(y - H // 2) < 1 else (-3 if abs(y - H // 2) < 2 else 0)
            edge = -6 if x in (0, W - 1) or y in (0, H - 1) else 0
            shade = -int(4 * y / H)
            v = 226 + g + fold + edge + shade
            px[x, y] = (v, v - 4, v - 14, 255)
    return img


def letters(img, text, x, y, ink, rng):
    px = img.load()
    for ch in text:
        for r, row in enumerate(FONT[ch]):
            for c, bit in enumerate(row):
                if bit == "1":
                    k = rng.randint(-8, 8)
                    px[x + c, y + r] = (ink[0] + k, ink[1] + k, ink[2] + k, 255)
        x += 4
    return x


def scribble(img, x0, x1, y, rng):
    """A line of hand-writing: ink strokes that wobble a pixel up and down, with gaps between words."""
    px = img.load()
    x = x0
    while x < x1:
        word = rng.randint(3, 7)
        for i in range(word):
            if x >= x1:
                break
            dy = rng.choice((0, 0, 0, -1, 1))
            k = rng.randint(-10, 6)
            px[x, y + dy] = (INK[0] + 20 + k, INK[1] + 20 + k, INK[2] + 20 + k, 255)
            if rng.random() < .35:  # a stroke going up (a tall letter)
                px[x, y + dy - 1] = (INK[0] + 30 + k, INK[1] + 30 + k, INK[2] + 30 + k, 255)
            x += 1
        x += 2


def tape(img, x, y, w, h):
    px = img.load()
    for j in range(h):
        for i in range(w):
            if 0 <= x + i < W and 0 <= y + j < H:
                r, g, b, a = px[x + i, y + j]
                px[x + i, y + j] = ((r * 2 + 236) // 3, (g * 2 + 226) // 3, (b * 2 + 170) // 3, 255)


def main():
    out = sys.argv[1]
    rules = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    rng = random.Random(7)
    img = paper(rng)
    # HOUSE / RULES, centred, a little crooked as written by hand.
    letters(img, "HOUSE", (W - 19) // 2, 5, INK, rng)
    letters(img, "RULES", (W - 19) // 2 + 1, 12, INK, rng)
    for x in range(10, W - 10):  # underline
        if rng.random() > .1:
            img.putpixel((x, 18 + (1 if x > W // 2 + 6 else 0)), (INK[0] + 15, INK[1] + 15, INK[2] + 15, 255))
    y = 23
    for n in range(1, rules + 1):
        x = letters(img, f"{n}.", 4, y, INK, rng)
        scribble(img, x + 1, W - 4, y + 3, rng)
        scribble(img, 8, W - 6 - rng.randint(0, 10), y + 8, rng)
        y += 13
    tape(img, 1, 0, 9, 4)
    tape(img, W - 10, 0, 9, 4)
    img.save(out)
    print(out, img.size)


if __name__ == "__main__":
    main()

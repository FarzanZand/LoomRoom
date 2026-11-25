"""A class's poster for the bedroom's poster wall, at the bedroom's 6.4 texels a unit: the class portrait printed on
aged paper with the class name under it, one texel per texel of the texture (point filtered in game).

    python Tools/class_poster.py <portrait.jpg> <out.png> <width> <height> <NAME> <seed> [cool] [keys] [punch] [bright] [focus x,y,zoom]
    python Tools/class_poster.py --tape <out.png> <width> <height> <seed>

The poster is graded like the house style asks: paper and print vary texel to texel, the ink never reaches pure black,
colours are muted and warmed as old print, no folds (the user's choice), edges and
corners are grimed and a few faint stains sit on it. --tape paints a strip of masking tape for its corners. cool (0-1)
pulls a hot-coloured painting (the Rogue's orange dusk) toward the others' dusty greys and blues before it is printed, so it
doesn't stand out on the wall. keys (1) prints a row of piano keys under the name (the Pianist's poster, which shares its
painting with the Cleric's, so the two are told apart on the wall). punch (1 = as painted) prints a pale, misty painting
with more contrast so its figure still reads at the poster's size, bright (1 = as painted) lifts its darks before that,
and focus cuts closer round a point of the painting (x and y as shares of it, then how many times closer), so a small
head fills more of the paper. The Pianist's grey axeman uses all three (see class_posters.eval.cs).
"""
import random
import sys

from PIL import Image, ImageDraw, ImageEnhance, ImageFont

FONT = "Assets/Game/UI/Fonts/PixelOperator-Bold.ttf"
FONT_REGULAR = "Assets/Game/UI/Fonts/PixelOperator.ttf"
PAPER = (214, 204, 180)
INK = (46, 38, 34)


def clamp(v):
    return max(0, min(255, int(round(v))))


def mul(c, k):
    return tuple(clamp(x * k) for x in c)


def lerp(a, b, t):
    return tuple(clamp(a[i] + (b[i] - a[i]) * t) for i in range(3))


def value_noise(w, h, cell, rng):
    """Smooth-ish blotch field in -1..1: random values on a coarse grid, bilinear between."""
    gw, gh = w // cell + 2, h // cell + 2
    g = [[rng.uniform(-1, 1) for _ in range(gw)] for _ in range(gh)]
    out = [[0.0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            fx, fy = x / cell, y / cell
            ix, iy = int(fx), int(fy)
            tx, ty = fx - ix, fy - iy
            a = g[iy][ix] * (1 - tx) + g[iy][ix + 1] * tx
            b = g[iy + 1][ix] * (1 - tx) + g[iy + 1][ix + 1] * tx
            out[y][x] = a * (1 - ty) + b * ty
    return out


def print_portrait(src, w, h, cool=0.0, punch=1.0, bright=1.0, focus=None):
    """The portrait cut to w x h from its middle, averaged down, then aged: muted, warmed, its blacks lifted to ink."""
    im = Image.open(src).convert("RGB")
    if cool > 0:
        # less red and saturation, a little more blue: a dusk-orange painting brought toward the others' cooler light
        im = ImageEnhance.Color(im).enhance(1 - .35 * cool)
        r, g, b = im.split()
        r = r.point(lambda v: v * (1 - .14 * cool)); b = b.point(lambda v: min(255, v * (1 + .16 * cool)))
        im = Image.merge("RGB", (r, g, b))
    if focus:
        # a closer cut: a window 1/zoom of the painting wide, round the point given, kept inside the painting
        cx, cy, zoom = focus
        fw, fh = im.width / zoom, im.height / zoom
        x0 = min(max(cx * im.width - fw / 2, 0), im.width - fw); y0 = min(max(cy * im.height - fh / 2, 0), im.height - fh)
        im = im.crop((int(x0), int(y0), int(x0 + fw), int(y0 + fh)))
    sw, sh = im.size
    if sw / sh > w / h:
        cw = int(sh * w / h)
        im = im.crop(((sw - cw) // 2, 0, (sw - cw) // 2 + cw, sh))
    else:
        ch = int(sw * h / w)
        im = im.crop((0, 0, sw, ch))  # keep the head: cut from the foot
    im = im.resize((w, h), Image.BOX)
    im = ImageEnhance.Color(im).enhance(.85)
    if bright != 1.0:
        # the darks lifted more than the lights (a dark figure brought up out of the mud, the pale parts kept)
        im = im.point(lambda v: 255 * (v / 255) ** (1 / bright))
    im = ImageEnhance.Contrast(im).enhance(punch)
    px = im.load()
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            # printed on paper: the paper shows through the light, the ink never goes black
            c = tuple(INK[i] + (PAPER[i] + 22 - INK[i]) * (v / 255) for i, v in enumerate((r, g, b)))
            px[x, y] = tuple(clamp(v) for v in c)
    return im


def poster(src, out, w, h, name, seed, cool=0.0, keys=False, punch=1.0, bright=1.0, focus=None):
    rng = random.Random(seed)
    img = Image.new("RGB", (w, h), PAPER)
    margin, band = 3, 15
    strip = 9 if keys else 0                    # the keys' row and the paper under it
    pw, ph = w - margin * 2, h - margin - band - strip
    img.paste(print_portrait(src, pw, ph, cool, punch, bright, focus), (margin, margin))
    # thin ink rule round the picture and the name under it
    d = ImageDraw.Draw(img)
    d.rectangle((margin - 1, margin - 1, margin + pw, margin + ph), outline=lerp(INK, PAPER, .35))
    d.fontmode = "1"
    font = ImageFont.truetype(FONT, 16)
    l, t, r, b = d.textbbox((0, 0), name, font=font)
    # A name too wide for the paper in bold (NECROMANCER on its 82 texel poster) is set in the regular weight instead.
    if r - l > w - margin * 2:
        font = ImageFont.truetype(FONT_REGULAR, 16)
        l, t, r, b = d.textbbox((0, 0), name, font=font)
    tx, ty = (w - (r - l)) // 2 - l, margin + ph + (band - (b - t)) // 2 - t + 1
    d.text((tx, ty), name, font=font, fill=INK)
    if keys:
        # A row of piano keys across the paper: white keys four texels wide between ink lines, the black keys in their
        # twos and threes reaching halfway down.
        top, key, tall = margin + ph + band, 4, 6
        count = pw // key
        left = margin + (pw - count * key) // 2
        rule = lerp(INK, PAPER, .2)
        d.rectangle((left, top, left + count * key, top + tall), fill=lerp(PAPER, (255, 250, 236), .6), outline=rule)
        for i in range(1, count):
            x = left + i * key
            d.line((x, top, x, top + tall), fill=lerp(INK, PAPER, .45))
            if (i - 1) % 7 not in (2, 6):       # no black key between the 3rd and 4th, nor the 7th and the next 1st
                d.rectangle((x - 1, top + 1, x + 1, top + 3), fill=INK)

    px = img.load()
    blotch = value_noise(w, h, 9, rng)
    stains = [(rng.uniform(0, w), rng.uniform(0, h), rng.uniform(4, 9)) for _ in range(rng.randint(1, 3))]
    for y in range(h):
        for x in range(w):
            c = px[x, y]
            # paper and print vary texel to texel, with slow blotches of age over it
            k = 1 + rng.uniform(-.055, .055) + blotch[y][x] * .05
            # yellowed toward the edges, grime in the last texels
            edge = min(x, y, w - 1 - x, h - 1 - y)
            if edge == 0:
                k *= .72
            elif edge == 1:
                k *= .88
            elif edge < 5:
                k *= .96
            c = mul(c, k)
            c = lerp(c, (150, 120, 70), .06 + max(0, 4 - edge) * .025)
            # faint water stains: a darker ring, lighter inside
            for sx, sy, sr in stains:
                dd = ((x - sx) ** 2 + (y - sy) ** 2) ** .5
                if abs(dd - sr) < .8:
                    c = lerp(c, (120, 95, 60), .22)
                elif dd < sr:
                    c = lerp(c, (170, 145, 100), .07)
            px[x, y] = c
    # one dog-eared corner: the turned-down triangle shows the plain back of the paper with a shadow under it
    ear = rng.randint(4, 6)
    corner = rng.choice(((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)))
    for i in range(ear):
        for j in range(ear - i):
            x = corner[0] + (i if corner[0] == 0 else -i)
            y = corner[1] + (j if corner[1] == 0 else -j)
            back = (196, 186, 160) if i + j < ear - 1 else (120, 108, 88)
            px[x, y] = mul(back, 1 + rng.uniform(-.05, .05))
    img.save(out)


def tape(out, w, h, seed):
    rng = random.Random(seed)
    img = Image.new("RGB", (w, h))
    px = img.load()
    base = (200, 184, 140)
    for y in range(h):
        for x in range(w):
            k = 1 + rng.uniform(-.06, .06)
            if y == 0 or y == h - 1:
                k *= .8  # torn ends
            elif x == 0 or x == w - 1:
                k *= .9
            px[x, y] = mul(base, k)
    img.save(out)


if __name__ == "__main__":
    a = sys.argv[1:]
    if a[0] == "--tape":
        tape(a[1], int(a[2]), int(a[3]), int(a[4]))
    else:
        poster(a[0], a[1], int(a[2]), int(a[3]), a[4], int(a[5]), float(a[6]) if len(a) > 6 else 0.0, len(a) > 7 and a[7] == "1",
               float(a[8]) if len(a) > 8 else 1.0, float(a[9]) if len(a) > 9 else 1.0,
               tuple(float(v) for v in a[10].split(',')) if len(a) > 10 else None)

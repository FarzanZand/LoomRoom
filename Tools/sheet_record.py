"""The character sheet's art (CharacterSheetUI, the look MetaProgressionSetup.Record lays out): a printed record sheet
drawn as pixel art, two canvas units per painted pixel like the rest of the game's UI.

    python -I Tools/sheet_record.py Assets/Game/UI/Sprites/Meta/Record [--mock <out.png>]

Writes into the folder:
  Record paper.png        the sheet with everything printed on it (frame, bars, boxes, the portrait's card)
  Record paper blank.png  the same paper with nothing printed (the first lesson's washes draw it over the ink)
  Record pip empty/filled/next.png   level boxes, white masks tinted in ink or pen, 9-sliced (border 5)
  Record button.png, Record button off.png   the Train plate, lit and unlit, 9-sliced (border 5)
  Record stamp.png        an outlined stamp for the small buttons, a white mask, 9-sliced (border 4)
  Record note.png         a pale card for the hover card, 9-sliced (border 5)
  Record tape.png         a strip of masking tape over a corner of the portrait's card
  Record select.png       brackets round the button the keys or a pad have selected, 9-sliced (border 9)
  Record shadow.png       the soft shadow the sheet throws on the desk, 9-sliced (border 24)

The layout numbers (LAY) are the same ones MetaProgressionSetup.Record.cs places the live parts by: change both.
Everything is in painted pixels from the sheet's top left corner.
"""
import sys, os, random
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONTS = os.path.join(ROOT, "Assets", "Game", "UI", "Fonts")
W, H = 860, 480

# Paper tones, light to dark, and the print's ink.
TONES = [(245, 239, 222), (238, 230, 209), (232, 223, 200), (222, 211, 186), (203, 189, 161), (172, 156, 128)]
INK = (38, 33, 31); INK_WORN = (50, 44, 41); PAPER_ON_INK = (240, 232, 212)
CARD = [(250, 246, 236), (244, 239, 226)]; CARD_EDGE = (150, 134, 110)

LAY = dict(
    frame_out=10, frame_in=14,
    header=(22, 20, 816, 26), rule_y=50,
    title=(24, 19),
    put_down=(742, 21, 96, 24),
    card=(36, 58, 200, 200), picture=(41, 63, 190, 190),
    name_line=(40, 285, 192),
    spoils=(30, 295, 212, 46), spoils_bar=13,
    skills=(262, 58, 576, 13), rows_y=74, row_h=38, rows=8,
    level_col=(456, 32),
    band_y=384, band_h=76, block_w=201, block_gap=4, block_bar=16,
    fold_x=256, fold_y=226,
)


def noise(w, h, cell, rng):
    g = rng.random((h // cell + 3, w // cell + 3)).astype(np.float32)
    img = Image.fromarray((g * 255).astype(np.uint8)).resize(((w // cell + 3) * cell, (h // cell + 3) * cell), Image.BICUBIC)
    return np.asarray(img, dtype=np.float32)[:h, :w] / 255.0


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name + ".ttf"), size)


def text(draw, at, s, f, fill):
    draw.fontmode = "1"   # no smoothing: the font's own pixels
    draw.text(at, s, font=f, fill=fill)


def paper(printed):
    rng = np.random.default_rng(11)
    r = random.Random(11)
    n = .5 * noise(W, H, 46, rng) + .32 * noise(W, H, 15, rng) + .18 * noise(W, H, 6, rng)
    yy, xx = np.mgrid[0:H, 0:W]
    d = np.minimum(np.minimum(xx, W - 1 - xx), np.minimum(yy, H - 1 - yy)).astype(np.float32)
    edge = .30 * np.exp(-d / 7.0) + .13 * np.exp(-d / 34.0)
    s = (n - .5) * 1.0 + edge
    tone = np.ones((H, W), dtype=np.int32)
    tone[s < -.19] = 0
    tone[s > .17] = 2
    tone[s > .30] = 3
    tone[s > .40] = 4
    L = LAY

    if printed:
        # Every second skill row lies on a band a tone darker.
        x0, _, w, _ = L["skills"]
        for i in range(1, L["rows"], 2):
            y = L["rows_y"] + i * L["row_h"]
            tone[y:y + L["row_h"], x0:x0 + w] += 1

    # A few foxing marks and a ring left by a cup, a tone darker, never single specks.
    for _ in range(9):
        cx, cy, rad = r.randrange(30, W - 30), r.randrange(30, H - 30), r.choice((2, 2, 3))
        m = (xx - cx) ** 2 + (yy - cy) ** 2 <= rad * rad
        tone[m] += 1
    ring = np.sqrt((xx - 770.0) ** 2 + (yy - 418.0) ** 2)
    broken = noise(W, H, 9, rng) > .36
    tone[(np.abs(ring - 27) < 1.2) & broken] += 1

    # The sheet was folded in four: a light ridge with a dark line beside it, broken where the paper lies flat.
    flat = noise(W, H, 12, rng)
    for y in range(3, H - 3):
        if flat[y, L["fold_x"]] > .28:
            tone[y, L["fold_x"]] = 0
            tone[y, L["fold_x"] + 1] = max(tone[y, L["fold_x"] + 1], 3)
    for x in range(3, W - 3):
        if flat[L["fold_y"], x] > .28:
            tone[L["fold_y"], x] = 0
            tone[L["fold_y"] + 1, x] = max(tone[L["fold_y"] + 1, x], 3)

    if printed:
        # A blind stamp pressed into the paper under the purse: a twenty-sided die, a tone darker, no ink.
        die = Image.new("L", (W, H), 0)
        dd = ImageDraw.Draw(die)
        import math
        cx, cy, rad = 136, 362, 17
        hexa = [(cx + rad * math.sin(math.radians(a)), cy - rad * math.cos(math.radians(a))) for a in range(0, 360, 60)]
        tri = [(cx + rad * .56 * math.sin(math.radians(a)), cy - rad * .56 * math.cos(math.radians(a))) for a in (0, 120, 240)]
        dd.polygon(hexa, outline=255); dd.polygon(tri, outline=255)
        for k, t in enumerate(tri):
            for h in (hexa[(2 * k - 1) % 6], hexa[(2 * k) % 6], hexa[(2 * k + 1) % 6]): dd.line((t, h), fill=255)
        tone[np.asarray(die) > 0] += 1

    tone = np.clip(tone, 0, 4)
    # Lit along the top and left edges, shaded along the bottom and right.
    tone[0, :] = 0; tone[:, 0] = 0
    tone[H - 1, :] = 5; tone[:, W - 1] = 5; tone[H - 2, :] = np.maximum(tone[H - 2, :], 4); tone[:, W - 2] = np.maximum(tone[:, W - 2], 4)
    rgb = np.array(TONES, dtype=np.uint8)[tone]
    img = Image.fromarray(rgb, "RGB").convert("RGBA")
    d = ImageDraw.Draw(img)

    if printed:
        ink = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        di = ImageDraw.Draw(ink)
        bold16 = font("PixelOperator-Bold", 16); bold32 = font("PixelOperator-Bold", 32)

        def box(x, y, w, h, t=1):
            for k in range(t):
                di.rectangle((x + k, y + k, x + w - 1 - k, y + h - 1 - k), outline=INK + (255,))

        def bar(x, y, w, h, label=None, at=5):
            di.rectangle((x, y, x + w - 1, y + h - 1), fill=INK + (255,))
            for cx, cy in ((x, y), (x + w - 1, y), (x, y + h - 1), (x + w - 1, y + h - 1)):
                ink.putpixel((cx, cy), (0, 0, 0, 0))
            if label:
                text(di, (x + at, y + (h - 16) // 2 - 1), label, bold16, PAPER_ON_INK + (255,))

        # The frame: a heavy rule and a hair rule inside it, a small lozenge at each corner.
        o, i = L["frame_out"], L["frame_in"]
        box(o, o, W - 2 * o, H - 2 * o, 2)
        box(i, i, W - 2 * i, H - 2 * i, 1)
        for cx, cy in ((o, o), (W - 1 - o, o), (o, H - 1 - o), (W - 1 - o, H - 1 - o)):
            di.polygon([(cx - 3, cy), (cx, cy - 3), (cx + 3, cy), (cx, cy + 3)], fill=INK + (255,))

        # Header: the title, and a double rule under it.
        text(di, L["title"], "CHARACTER SHEET", bold32, INK + (255,))
        hx, hy, hw, hh = L["header"]
        di.rectangle((hx, L["rule_y"], hx + hw - 1, L["rule_y"] + 1), fill=INK + (255,))
        di.line((hx, L["rule_y"] + 3, hx + hw - 1, L["rule_y"] + 3), fill=INK + (255,))

        # The name's line, with its small label under it.
        nx, ny, nw = L["name_line"]
        di.line((nx, ny, nx + nw - 1, ny), fill=INK + (255,))
        di.line((nx, ny + 1, nx + nw - 1, ny + 1), fill=INK + (255,))

        # Spoils: a boxed purse under a bar.
        sx, sy, sw, sh = L["spoils"]
        box(sx, sy, sw, sh, 1)
        bar(sx, sy, sw, L["spoils_bar"], "SPOILS TO SPEND")

        # Skills: the bar with its column heads, each row's icon box and level box.
        kx, ky, kw, kh = L["skills"]
        bar(kx, ky, kw, kh, "SKILLS")
        lx, lw = L["level_col"]
        tw = di.textlength("LEVEL", font=bold16)
        text(di, (lx + (lw - tw) // 2, ky + (kh - 16) // 2 - 1), "LEVEL", bold16, PAPER_ON_INK + (255,))
        for r_ in range(L["rows"]):
            y = L["rows_y"] + r_ * L["row_h"]
            box(kx + 2, y + 1, 36, 36, 1)
            di.rectangle((kx + 3, y + 2, kx + 36, y + 35), fill=TONES[0] + (255,))
            box(lx, y + 7, lw, 24, 1)
            di.rectangle((lx + 1, y + 8, lx + lw - 2, y + 29), fill=TONES[0] + (255,))
        di.line((kx, L["rows_y"] + L["rows"] * L["row_h"], kx + kw - 1, L["rows_y"] + L["rows"] * L["row_h"]), fill=INK + (255,))
        di.line((kx, L["rows_y"] - 1, kx, L["rows_y"] + L["rows"] * L["row_h"]), fill=INK + (255,))
        di.line((kx + kw - 1, L["rows_y"] - 1, kx + kw - 1, L["rows_y"] + L["rows"] * L["row_h"]), fill=INK + (255,))

        # Attributes: four boxed blocks in a row, each under its own bar, with a box for the score.
        for b in range(4):
            x = 22 + b * (L["block_w"] + L["block_gap"]); y = L["band_y"]
            box(x, y, L["block_w"], L["band_h"], 1)
            bar(x, y, L["block_w"], L["block_bar"])
            di.rectangle((x + 1, y + 1, x + 18, y + L["block_bar"] - 1), fill=TONES[0] + (255,))   # the icon's own tab
            box(x + 6, y + 21, 34, 32, 2)
            di.rectangle((x + 8, y + 23, x + 37, y + 50), fill=TONES[0] + (255,))

        # The print is a little worn in patches (never speckled).
        worn = noise(W, H, 7, rng) > .80
        a = np.asarray(ink).copy()
        solid = (a[..., 3] > 0) & (a[..., 0] < 100)
        a[solid & worn, :3] = INK_WORN
        ink = Image.fromarray(a, "RGBA")
        img.alpha_composite(ink)

        # The portrait's card, a separate piece of drawing paper: its shadow on the sheet, its pale face, the recess
        # the picture fills.
        cx, cy, cw, ch = L["card"]
        d = ImageDraw.Draw(img)
        d.rectangle((cx + 2, cy + 2, cx + cw + 1, cy + ch + 1), fill=TONES[4] + (255,))
        d.rectangle((cx + 1, cy + 1, cx + cw, cy + ch), fill=TONES[5] + (255,))
        cn = noise(cw, ch, 9, rng)
        card = np.where((cn > .55)[..., None], np.array(CARD[1], dtype=np.uint8), np.array(CARD[0], dtype=np.uint8)).astype(np.uint8)
        img.paste(Image.fromarray(card, "RGB"), (cx, cy))
        d.rectangle((cx, cy, cx + cw - 1, cy + ch - 1), outline=CARD_EDGE + (255,))
        px, py, pw, ph = L["picture"]
        d.rectangle((px - 1, py - 1, px + pw, py + ph), outline=INK + (255,))
        d.rectangle((px, py, px + pw - 1, py + ph - 1), fill=TONES[3] + (255,))

    # A dog-ear at the top right: the corner folded over, its pale back showing, a shadow under the flap.
    s_ = 17
    px_ = img.load()
    for y in range(0, s_ + 3):
        for x in range(W - s_ - 3, W):
            k = (W - 1 - x) + y
            if k < s_:
                px_[x, y] = (0, 0, 0, 0)
            elif (W - 1 - x) <= s_ and y <= s_:
                px_[x, y] = (INK_WORN if k == s_ else TONES[0] if (x + y) % 7 else TONES[1]) + (255,)
            elif (W - 1 - x) <= s_ + 2 and y <= s_ + 2 and ((W - 1 - x) > s_ or y > s_):
                px_[x, y] = TONES[4] + (255,)

    # Rounded corners and a slightly ragged edge: nicks a pixel deep, in short runs.
    for cx, cy in ((0, 0), (0, H - 1), (W - 1, H - 1)):
        for dx, dy in ((0, 0), (1, 0), (0, 1)):
            x = cx + dx if cx == 0 else cx - dx; y = cy + dy if cy == 0 else cy - dy
            px_[x, y] = (0, 0, 0, 0)
    for side in range(4):
        pos = 6
        length = W if side < 2 else H
        while pos < length - 26:
            pos += r.randrange(40, 170)
            run = r.randrange(1, 4)
            for k in range(run):
                q = pos + k
                if q >= length - 22: break
                x, y = (q, 0) if side == 0 else (q, H - 1) if side == 1 else (0, q) if side == 2 else (W - 1, q)
                px_[x, y] = (0, 0, 0, 0)
    return img


def mask(w, h, draw_fn):
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    draw_fn(ImageDraw.Draw(img), img)
    return img


def pips():
    n = 16
    def empty(d, img):
        d.rectangle((0, 0, n - 1, n - 1), outline=(255, 255, 255, 255))
        d.rectangle((1, 1, n - 2, n - 2), outline=(255, 255, 255, 255))
        for c in ((0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)): img.putpixel(c, (255, 255, 255, 0))
        d.rectangle((2, 2, n - 3, n - 3), fill=(255, 255, 255, 26))
    def filled(d, img):
        d.rectangle((0, 0, n - 1, n - 1), fill=(255, 255, 255, 255))
        for c in ((0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)): img.putpixel(c, (255, 255, 255, 0))
    def nxt(d, img):
        a = 4
        for (x, y, sx, sy) in ((0, 0, 1, 1), (n - 1, 0, -1, 1), (0, n - 1, 1, -1), (n - 1, n - 1, -1, -1)):
            for t in range(2):
                d.line((x + sx * t, y, x + sx * t, y + sy * a), fill=(255, 255, 255, 255))
                d.line((x, y + sy * t, x + sx * a, y + sy * t), fill=(255, 255, 255, 255))
        d.rectangle((2, 2, n - 3, n - 3), fill=(255, 255, 255, 46))
    return mask(n, n, empty), mask(n, n, filled), mask(n, n, nxt)


def plate(face, lit, shade, line, text_dent=None):
    n = 16
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, n - 1, n - 1), fill=line + (255,))
    d.rectangle((1, 1, n - 2, n - 2), fill=face + (255,))
    d.line((1, 1, n - 2, 1), fill=lit + (255,))
    d.line((1, 2, 1, n - 4), fill=lit + (255,))
    d.rectangle((1, n - 4, n - 2, n - 2), fill=shade + (255,))
    d.line((n - 2, 2, n - 2, n - 4), fill=shade + (255,))
    for c in ((0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)): img.putpixel(c, (0, 0, 0, 0))
    return img


def stamp():
    n = 14
    def f(d, img):
        d.rectangle((0, 0, n - 1, n - 1), outline=(255, 255, 255, 255))
        d.rectangle((1, 1, n - 2, n - 2), outline=(255, 255, 255, 255))
        d.rectangle((2, 2, n - 3, n - 3), fill=(255, 255, 255, 34))
        for c in ((0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)): img.putpixel(c, (255, 255, 255, 0))
    return mask(n, n, f)


def note():
    n = 16
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, n - 1, n - 1), fill=INK + (255,))
    d.rectangle((2, 2, n - 3, n - 3), fill=CARD[0] + (255,))
    d.line((2, n - 3, n - 3, n - 3), fill=TONES[2] + (255,))
    d.line((n - 3, 2, n - 3, n - 3), fill=TONES[2] + (255,))
    for c in ((0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)): img.putpixel(c, (0, 0, 0, 0))
    return img


def select():
    """The frame round the button the keys have selected: heavy corner brackets in ink with a pale line inside them, so
    it shows on the paper and on a red plate alike. 9-sliced (border 9)."""
    n = 24
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    arm = 8
    for (x, y, sx, sy) in ((0, 0, 1, 1), (n - 1, 0, -1, 1), (0, n - 1, 1, -1), (n - 1, n - 1, -1, -1)):
        for t in range(3):
            d.line((x + sx * t, y, x + sx * t, y + sy * arm), fill=INK + (255,))
            d.line((x, y + sy * t, x + sx * arm, y + sy * t), fill=INK + (255,))
        d.line((x + sx * 3, y + sy * 3, x + sx * 3, y + sy * arm), fill=CARD[0] + (255,))
        d.line((x + sx * 3, y + sy * 3, x + sx * arm, y + sy * 3), fill=CARD[0] + (255,))
    return img


def tape():
    """Masking tape across the card's top left corner (flipped for the right one): a band running up to the right."""
    n = 34
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    r = random.Random(5)
    base, light, dark = (226, 206, 148), (238, 222, 170), (192, 170, 114)
    for y in range(n):
        for x in range(n):
            across = (x + y) - (n - 1)        # 0 on the band's middle line
            along = x - y
            end = 26 + ((x + 2 * y) % 3 - 1)  # torn ends
            if abs(across) <= 6 and abs(along) <= end:
                c = dark if abs(across) == 6 else light if ((x - y) // 5) % 3 == 0 and abs(across) < 5 else base
                img.putpixel((x, y), c + (238,))
    return img


def shadow():
    n = 96
    a = np.zeros((n, n), dtype=np.uint8)
    a[24:72, 24:72] = 255
    im = Image.fromarray(a).filter(ImageFilter.GaussianBlur(9))
    out = Image.new("RGBA", (n, n), (0, 0, 0, 0)); out.putalpha(im)
    return out


def write_all(folder):
    os.makedirs(folder, exist_ok=True)
    paper(True).save(os.path.join(folder, "Record paper.png"))
    paper(False).save(os.path.join(folder, "Record paper blank.png"))
    e, f, n = pips()
    e.save(os.path.join(folder, "Record pip empty.png")); f.save(os.path.join(folder, "Record pip filled.png")); n.save(os.path.join(folder, "Record pip next.png"))
    plate((166, 38, 30), (206, 78, 58), (104, 22, 22), (52, 14, 14)).save(os.path.join(folder, "Record button.png"))
    plate((208, 197, 174), (228, 219, 198), (172, 159, 134), (112, 100, 84)).save(os.path.join(folder, "Record button off.png"))
    stamp().save(os.path.join(folder, "Record stamp.png"))
    note().save(os.path.join(folder, "Record note.png"))
    tape().save(os.path.join(folder, "Record tape.png"))
    select().save(os.path.join(folder, "Record select.png"))
    shadow().save(os.path.join(folder, "Record shadow.png"))


if __name__ == "__main__":
    out = sys.argv[1]
    write_all(out)
    print("wrote the record sheet's art to", out)

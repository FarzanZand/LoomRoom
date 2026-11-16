"""The character sheet's art (CharacterSheetUI, the look MetaProgressionSetup.Record lays out): a sheet kept by hand,
drawn as pixel art, two canvas units per painted pixel like the rest of the game's UI. Redrawn 2026-11-16 (the user:
"looks messy, cramped and very digital-button square"): calm paper, nothing boxed, round bubbles for levels, a round
ring for each attribute's score, and Train as an oval drawn round the price in red pen.

    python -I Tools/sheet_record.py Assets/Game/UI/Sprites/Meta/Record [--mock <out.png>]

Writes into the folder:
  Record paper.png        the sheet with what is printed on it (two headings and their rules, a faint rule under each
                          skill, the score rings, the name's line, the portrait's card)
  Record paper blank.png  the same paper with nothing printed (the first lesson's washes draw it over the ink)
  Record pip empty/filled/next.png     a level's bubble, 12 px, white masks tinted in ink or pen
  Record ring empty/filled/next.png    a milestone's larger bubble, 22 px, the same
  Record button.png, Record button hot.png, Record button off.png   the oval round Train: in pen, under the pointer,
                          and in pencil while it can't be bought; 9-sliced (border 11)
  Record note.png         a pale card for the hover card, 9-sliced (border 6)
  Record banner.png       the card a bought milestone is announced on, 9-sliced (border 7)
  Record tape.png         a strip of masking tape over a corner of the portrait's card
  Record select.png       the ring round the button the keys or a pad have selected, 9-sliced (border 12)
  Record shadow.png       the soft shadow the sheet throws on the desk, 9-sliced (border 24)

The layout numbers (LAY) are the same ones MetaProgressionSetup.Record.cs places the live parts by: change both.
Everything is in painted pixels from the sheet's top left corner. --mock draws the live parts in too (sample values),
twice the size, to look at the whole sheet without the game.
"""
import sys, os, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FONTS = os.path.join(ROOT, "Assets", "Game", "UI", "Fonts")
META = os.path.join(ROOT, "Assets", "Game", "UI", "Sprites", "Meta")
W, H = 860, 480

# Paper tones, light to dark, and the inks.
TONES = [(244, 238, 221), (240, 233, 214), (233, 225, 204), (218, 208, 184), (198, 185, 158), (170, 155, 128)]
INK = (38, 33, 31); FAINT = (128, 118, 104); PEN = (171, 33, 26); RULE = (206, 195, 170)
CARD = [(250, 246, 236), (245, 240, 228)]; CARD_EDGE = (160, 146, 122)

LAY = dict(
    title=(30, 16),
    card=(55, 40, 150, 150), picture=(59, 44, 142, 142),
    name=(48, 196, 164, 24), name_line=(48, 222, 164),
    spoils=(48, 232, 164, 34), spoils_label=(48, 268), refund=(80, 286, 100, 24), quest=(30, 314, 212, 30),
    skills_head=(264, 26), skills_rule=(262, 46, 576), put_down=(746, 16, 92, 24), feedback=(340, 22, 396, 22),
    kx=262, rows_y=54, row_h=36, rows=8, name_x=40, level_x=190, pips_x=232, pip=12, ring=22, pip_gap=5, buy=(472, 104, 24),
    attr_head=(30, 350), attr_rule=(24, 368, 814), attr_x=30, attr_w=202, attr_y=376, score=46,
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


def disc(n, kind, ring=1):
    """A round white mask n pixels across: 'filled', 'empty' (a ring) or 'next' (a ring with a wash inside)."""
    img = Image.new("RGBA", (n, n), (255, 255, 255, 0))
    c = (n - 1) / 2.0; r = n / 2.0
    for y in range(n):
        for x in range(n):
            d = math.hypot(x - c, y - c)
            if d > r - .25: continue
            edge = d > r - .25 - ring
            if kind == "filled": a = 255
            elif kind == "empty": a = 255 if edge else 22
            else: a = 255 if edge else 84   # the next one to buy: the ring in pen, a wash of it inside
            if a: img.putpixel((x, y), (255, 255, 255, a))
    return img


def rounded(w, h, rad, fill, line, width=1, shade=None):
    """A rounded card: its face, an outline, and (shade) a darker line inside its bottom and right edges."""
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, w - 1, h - 1), radius=rad, fill=line + (255,))
    d.rounded_rectangle((width, width, w - 1 - width, h - 1 - width), radius=max(0, rad - width), fill=fill)
    if shade:
        d.line((rad, h - 1 - width, w - 1 - rad, h - 1 - width), fill=shade + (255,))
        d.line((w - 1 - width, rad, w - 1 - width, h - 1 - rad), fill=shade + (255,))
    return img


def oval(line, width, wash):
    """The oval drawn round a price: 30 x 24, its ends half circles, 9-sliced (border 11). wash: its inside's alpha."""
    return rounded(30, 24, 11, line + (wash,), line, width)


def paper(printed):
    rng = np.random.default_rng(11)
    n = .6 * noise(W, H, 64, rng) + .4 * noise(W, H, 22, rng)
    yy, xx = np.mgrid[0:H, 0:W]
    d = np.minimum(np.minimum(xx, W - 1 - xx), np.minimum(yy, H - 1 - yy)).astype(np.float32)
    s = (n - .5) + .16 * np.exp(-d / 26.0)
    # Calm: nearly all of it one tone, a few broad lighter and darker patches, darker toward the edges.
    tone = np.ones((H, W), dtype=np.int32)
    tone[s < -.17] = 0
    tone[d < 2] = 3
    L = LAY
    tone[H - 1, :] = 5; tone[:, W - 1] = 5; tone[H - 2, :] = np.maximum(tone[H - 2, :], 4); tone[:, W - 2] = np.maximum(tone[:, W - 2], 4)
    tone[0, :] = 0; tone[:, 0] = 0
    img = Image.fromarray(np.array(TONES, dtype=np.uint8)[np.clip(tone, 0, 5)], "RGB").convert("RGBA")
    d_ = ImageDraw.Draw(img)

    if printed:
        bold16 = font("PixelOperator-Bold", 16); small = font("PixelOperator", 16)
        ink, faint, rule = INK + (255,), FAINT + (255,), RULE + (255,)
        text(d_, L["title"], "CHARACTER SHEET", bold16, faint)
        # The name's line, the purse's small label.
        nx, ny, nw = L["name_line"]
        d_.line((nx, ny, nx + nw - 1, ny), fill=ink)
        lx, ly = L["spoils_label"]
        tw = d_.textlength("spoils to spend", font=small)
        text(d_, (130 - tw // 2, ly), "spoils to spend", small, faint)
        # Skills: a heading on a rule, and a faint rule under each row for the eye to follow across.
        text(d_, L["skills_head"], "SKILLS", bold16, ink)
        rx, ry, rw = L["skills_rule"]
        d_.line((rx, ry, rx + rw - 1, ry), fill=ink)
        for i in range(L["rows"]):
            y = L["rows_y"] + (i + 1) * L["row_h"] - 1
            if i < L["rows"] - 1: d_.line((rx + L["name_x"], y, rx + rw - 1, y), fill=rule)
        # Attributes: a heading on a rule, and a ring for each score.
        text(d_, L["attr_head"], "ATTRIBUTES", bold16, ink)
        ax, ay, aw = L["attr_rule"]
        d_.line((ax, ay, ax + aw - 1, ay), fill=ink)
        ringmask = disc(L["score"], "empty", 2)
        solid = Image.new("RGBA", ringmask.size, ink); face = Image.new("RGBA", ringmask.size, TONES[0] + (255,))
        full = disc(L["score"], "filled")
        for b in range(4):
            at = (L["attr_x"] + b * L["attr_w"] + 2, L["attr_y"] + 8)
            img.paste(face, at, full); img.paste(solid, at, Image.fromarray((np.asarray(ringmask)[..., 3] > 200).astype(np.uint8) * 255))
        # The portrait's card, a separate piece of drawing paper: its shadow on the sheet, its pale face, the picture's recess.
        cx, cy, cw, ch = L["card"]
        d_.rectangle((cx + 2, cy + 2, cx + cw + 1, cy + ch + 1), fill=TONES[4] + (255,))
        d_.rectangle((cx, cy, cx + cw - 1, cy + ch - 1), fill=CARD[0] + (255,), outline=CARD_EDGE + (255,))
        px, py, pw, ph = L["picture"]
        d_.rectangle((px, py, px + pw - 1, py + ph - 1), fill=TONES[3] + (255,))

    # Rounded corners.
    px_ = img.load()
    for cx, cy in ((0, 0), (W - 1, 0), (0, H - 1), (W - 1, H - 1)):
        for dx, dy in ((0, 0), (1, 0), (0, 1), (2, 0), (0, 2), (1, 1)):
            x = cx + dx if cx == 0 else cx - dx; y = cy + dy if cy == 0 else cy - dy
            px_[x, y] = (0, 0, 0, 0)
    return img


def select():
    """The ring round the button the keys have selected: a rounded line in ink with a pale line inside it, so it shows on
    the paper and on a red oval alike. 9-sliced (border 12)."""
    img = rounded(30, 30, 12, (0, 0, 0, 0), INK, 2)
    inner = rounded(26, 26, 10, (0, 0, 0, 0), CARD[0], 1)
    img.alpha_composite(inner, (2, 2))
    return img


def tape():
    """Masking tape across the card's top left corner (flipped for the right one): a band running up to the right."""
    n = 34
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
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


def sprites():
    return {
        "Record paper": paper(True), "Record paper blank": paper(False),
        "Record pip empty": disc(LAY["pip"], "empty"), "Record pip filled": disc(LAY["pip"], "filled"), "Record pip next": disc(LAY["pip"], "next"),
        "Record ring empty": disc(LAY["ring"], "empty"), "Record ring filled": disc(LAY["ring"], "filled"), "Record ring next": disc(LAY["ring"], "next"),
        "Record button": oval(PEN, 2, 20), "Record button hot": oval(PEN, 2, 70), "Record button off": oval(FAINT, 1, 0),
        "Record note": rounded(18, 18, 5, CARD[0] + (255,), INK, 1, TONES[3]),
        "Record banner": rounded(20, 20, 6, CARD[0] + (255,), PEN, 2),
        "Record tape": tape(), "Record select": select(), "Record shadow": shadow(),
    }


def nine(sprite, w, h, b):
    """A 9-sliced sprite drawn at a size, for the mock."""
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0)); sw, sh = sprite.size
    xs = [(0, b, 0, b), (b, sw - b, b, w - b), (sw - b, sw, w - b, w)]; ys = [(0, b, 0, b), (b, sh - b, b, h - b), (sh - b, sh, h - b, h)]
    for sx0, sx1, dx0, dx1 in xs:
        for sy0, sy1, dy0, dy1 in ys:
            if dx1 > dx0 and dy1 > dy0: out.paste(sprite.crop((sx0, sy0, sx1, sy1)).resize((dx1 - dx0, dy1 - dy0), Image.NEAREST), (dx0, dy0))
    return out


def mock(s):
    """The whole sheet with sample values written in, as the game lays it out."""
    L = LAY; img = s["Record paper"].copy(); d = ImageDraw.Draw(img)
    reg = font("PixelOperator", 16); bold = font("PixelOperator-Bold", 16); big = font("PixelOperator-Bold", 32)
    def tint(mask, colour): out = Image.new("RGBA", mask.size, colour + (255,)); out.putalpha(mask.getchannel("A")); return out
    def centred(box, s_, f, fill):
        x, y, w, h = box; tw = d.textlength(s_, font=f); text(d, (x + (w - tw) // 2, y + (h - f.size) // 2 - 1), s_, f, fill)
    def tag(box, label, lit=True):
        x, y, w, h = box; img.alpha_composite(nine(s["Record button" if lit else "Record button off"], w, h, 11), (x, y)); centred(box, label, reg, (PEN if lit else FAINT) + (255,))
    def icon(path, at, size):
        try:
            im = Image.open(os.path.join(META, path)).convert("RGBA").resize((size, size), Image.NEAREST); img.alpha_composite(im, at)
        except Exception: d.ellipse((at[0], at[1], at[0] + size - 1, at[1] + size - 1), outline=FAINT + (255,))
    px, py, pw, ph = L["picture"]
    try: img.paste(Image.open(os.path.join(META, "Portraits", "Warrior pencil.png")).convert("RGB").resize((pw, ph), Image.NEAREST), (px, py))
    except Exception: pass
    cx, cy, cw, ch = L["card"]
    img.alpha_composite(s["Record tape"], (cx - 12, cy - 12)); img.alpha_composite(s["Record tape"].transpose(Image.FLIP_LEFT_RIGHT), (cx + cw - 22, cy - 12))
    text(d, (cx - 22, cy + ch // 2 - 8), "<", bold, INK + (255,)); text(d, (cx + cw + 14, cy + ch // 2 - 8), ">", bold, INK + (255,))
    centred(L["name"], "Mira", bold, PEN + (255,))
    sx, sy, sw, sh = L["spoils"]; icon(os.path.join("Sheet icons", "122_Ingot_Gold.png"), (sx + 40, sy + 4), 26); text(d, (sx + 76, sy + 1), "23", big, PEN + (255,))
    tag(L["refund"], "Refund all"); tag(L["put_down"], "Put down")
    rows = [("Blocking", 25, 5, 2, "Blocking.png"), ("Swords", 15, 3, 2, "160_Mastery_Sword.png"), ("Maces", 10, 2, 2, "409_Earth_Blunt.png"), ("Sorcery", 10, 2, 2, "03_Fire_I.png"),
            ("Thaumaturgy", 10, 2, 2, "488_Light_Eucharist.png"), ("Athletics", 15, 3, 2, "587_Dash.png"), ("Stealth", 10, 2, 2, "451_Darkness_Knife.png"), ("Trading", 10, 2, 2, "122_Ingot_Gold.png")]
    for i, (name, value, have, given, ic) in enumerate(rows):
        y = L["rows_y"] + i * L["row_h"]; mid = y + L["row_h"] // 2; kx = L["kx"]
        icon(os.path.join("Sheet icons", ic), (kx + 4, mid - 14), 28)
        text(d, (kx + L["name_x"], mid - 9), name, reg, INK + (255,))
        text(d, (kx + L["level_x"], mid - 9), str(value), bold, (PEN if have > given else INK) + (255,))
        x = kx + L["pips_x"]; stones = (5, 10) if name != "Athletics" else (3, 10)
        for k in range(1, 11):
            bigone = k in stones; n = L["ring"] if bigone else L["pip"]; base = "Record ring " if bigone else "Record pip "
            kind = "filled" if k <= have else "next" if k == have + 1 else "empty"
            colour = INK if (k <= given or kind == "empty") else PEN
            img.alpha_composite(tint(s[base + kind], colour), (x, mid - n // 2))
            if bigone:
                if kind != "filled": text(d, (x + 8, mid - 8), "?", bold, INK + (255,))
            x += n + L["pip_gap"]
        bx, bw, bh = L["buy"]; tag((kx + bx, mid - bh // 2, bw, bh), "Train %d" % (10 + 5 * (have - 2) + (9 if i == 0 else 0)), lit=i != 0)
    for b, (name, score, have, ic) in enumerate([("Strength", 9, 1, "134_Strength.png"), ("Dexterity", 8, 0, "139_Critical_Male_Sword.png"), ("Constitution", 8, 0, "102_Passive_Male_Vitality.png"), ("Intelligence", 8, 0, "157_Mastery_Staff.png")]):
        x0 = L["attr_x"] + b * L["attr_w"]; y0 = L["attr_y"]
        centred((x0 + 2, y0 + 8, L["score"], L["score"]), str(score), big, (PEN if have else INK) + (255,))
        icon(os.path.join("Sheet icons", ic), (x0 + 58, y0 + 2), 16); text(d, (x0 + 78, y0 + 1), name, reg, INK + (255,))
        for k in range(8):
            kind = "filled" if k < have else "next" if k == have else "empty"
            img.alpha_composite(tint(s["Record pip " + kind], INK if kind == "empty" else PEN), (x0 + 58 + k * (L["pip"] + L["pip_gap"]), y0 + 26))
        tag((x0 + 58, y0 + 46, 132, 24), "Train %d" % (30 if have else 20), lit=b != 0)
    return img.resize((W * 2, H * 2), Image.NEAREST)


if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    made = sprites()
    for name, im in made.items(): im.save(os.path.join(out, name + ".png"))
    if "--mock" in sys.argv:
        desk = Image.new("RGBA", (1920, 1080), (16, 13, 11, 255)); desk.alpha_composite(mock(made), (100, 60)); desk.save(sys.argv[sys.argv.index("--mock") + 1])
    print("wrote the character sheet's art to", out)

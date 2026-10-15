"""Pixel sprites for the character sheet (CharacterSheetUI) and the key items, drawn at half the UI size (one pixel =
two canvas units), point filtered like the rest of the pixel UI.

Run: python -I Tools/meta_sheet_sprites.py Assets/Game/UI/Sprites/Meta

  Paper.png          clean paper: near-white stock, a faint tone, a slightly darker edge, a gently torn outline
  Bubble filled.png  / Bubble outline.png / Bubble dashed.png   white, tinted in the UI (ink, pen)
  Crest.png          a shield crest, ink outline on paper (attribute scores)
  Icon frame.png     the frame round a skill's icon
  Pen frame.png      a 9-sliced outline for buttons (white, tinted)
  Ring.png           the portrait's ring: dots outside, a solid line inside
  Portrait placeholder.png   an empty stick figure
"""
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import gaussian_filter, zoom

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
rng = np.random.default_rng(11)
INK = (43, 39, 36, 255)
PAPER = (247, 245, 239, 255)


def save(img, name):
    img.save(os.path.join(out, name))
    print("wrote", name, img.size)


# ── Paper (820 x 450 = 1640 x 900 canvas units) ─────────────────────
W, H = 820, 450
base = np.array([249, 247, 242], float)
fine = gaussian_filter(rng.normal(0, 1, (H, W)), .7); fine /= fine.std()
fibre = gaussian_filter(rng.normal(0, 1, (H, W)), 3); fibre /= fibre.std()
tone = gaussian_filter(rng.normal(0, 1, (H // 8, W // 8)), 6); tone = zoom(tone, (H / tone.shape[0], W / tone.shape[1]), order=3); tone /= tone.std()
y, x = np.mgrid[0:H, 0:W]
edge = np.minimum.reduce([x, y, W - 1 - x, H - 1 - y]).astype(float)
shade = np.clip(1 - edge / 30, 0, 1) ** 2
lum = .7 * fine + .7 * fibre + 1.4 * tone - 6 * shade
rgb = np.clip(base[None, None, :] + lum[..., None] - shade[..., None] * np.array([0, 1, 3])[None, None, :], 0, 255)


def wander(n):
    w = gaussian_filter(np.cumsum(rng.normal(0, .7, n)), 2)
    w -= w.min()
    return np.round(1 + 2.5 * w / max(w.max(), 1e-6)).astype(int)


top, bot, left, right = wander(W), wander(W), wander(H), wander(H)
alpha = ((y >= top[None, :]) & (y <= H - 1 - bot[None, :]) & (x >= left[:, None]) & (x <= W - 1 - right[:, None])).astype(np.uint8) * 255
save(Image.fromarray(np.dstack([rgb.astype(np.uint8), alpha]), "RGBA"), "Paper.png")


# ── Bubbles (15 px) ─────────────────────────────────────────────────
def circle(size, ring=None, dashed=False):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    c = (size - 1) / 2
    for j in range(size):
        for i in range(size):
            d = math.hypot(i - c, j - c)
            r = size / 2 - .3
            if ring is None:
                if d <= r: img.putpixel((i, j), (255, 255, 255, 255))
            elif r - ring <= d <= r:
                a = math.degrees(math.atan2(j - c, i - c)) % 360
                if not dashed or int(a // 30) % 2 == 0: img.putpixel((i, j), (255, 255, 255, 255))
    return img


save(circle(15), "Bubble filled.png")
save(circle(15, 1.6), "Bubble outline.png")
save(circle(15, 1.6, True), "Bubble dashed.png")

# ── Crest (32 x 36) ─────────────────────────────────────────────────
cw, ch = 32, 36
crest = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
d = ImageDraw.Draw(crest)
outline = [(2, 2), (cw - 3, 2), (cw - 3, 17), (cw - 5, 24), (cw // 2, ch - 2), (4, 24), (2, 17)]
d.polygon(outline, fill=PAPER, outline=INK)
d.line(outline + [outline[0]], fill=INK, width=2)
inner = [(5, 5), (cw - 6, 5), (cw - 6, 17), (cw - 8, 22), (cw // 2, ch - 6), (7, 22), (5, 17)]
d.line(inner + [inner[0]], fill=(43, 39, 36, 120), width=1)
save(crest, "Crest.png")

# ── Icon frame (36 px, 9-slice friendly) ────────────────────────────
fr = Image.new("RGBA", (36, 36), (0, 0, 0, 0))
d = ImageDraw.Draw(fr)
d.rectangle([0, 0, 35, 35], fill=(255, 255, 255, 90), outline=INK, width=2)
d.rectangle([3, 3, 32, 32], outline=(43, 39, 36, 70), width=1)
save(fr, "Icon frame.png")

# ── Pen frame (12 px, 9-sliced at 3) ────────────────────────────────
pf = Image.new("RGBA", (12, 12), (0, 0, 0, 0))
d = ImageDraw.Draw(pf)
d.rectangle([0, 0, 11, 11], outline=(255, 255, 255, 255), width=1)
d.rectangle([1, 1, 10, 10], fill=(255, 255, 255, 30))
save(pf, "Pen frame.png")

# ── Portrait ring (130 px) ──────────────────────────────────────────
S = 130
ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
c = (S - 1) / 2
for k in range(56):
    a = 2 * math.pi * k / 56
    px, py = c + (S / 2 - 3) * math.cos(a), c + (S / 2 - 3) * math.sin(a)
    ImageDraw.Draw(ring).ellipse([px - 1.6, py - 1.6, px + 1.6, py + 1.6], fill=INK)
for j in range(S):
    for i in range(S):
        dd = math.hypot(i - c, j - c)
        if S / 2 - 10 <= dd <= S / 2 - 8.6: ring.putpixel((i, j), INK)
save(ring, "Ring.png")
mask = circle(S - 20)
save(mask, "Ring mask.png")

# ── Portrait placeholder: an empty stick figure (60 x 80) ───────────
pw, ph = 60, 80
fig = Image.new("RGBA", (pw, ph), (0, 0, 0, 0))
d = ImageDraw.Draw(fig)
grey = (150, 148, 145, 255)
d.ellipse([22, 6, 38, 22], outline=grey, width=2)
d.line([(30, 22), (30, 50)], fill=grey, width=2)
d.line([(30, 30), (17, 42)], fill=grey, width=2)
d.line([(30, 30), (43, 42)], fill=grey, width=2)
d.line([(30, 50), (20, 72)], fill=grey, width=2)
d.line([(30, 50), (40, 72)], fill=grey, width=2)
save(fig, "Portrait placeholder.png")

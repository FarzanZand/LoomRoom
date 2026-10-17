"""The character sheet's shapes and icons drawn smooth (the sheet is a clean paper page, not pixel art):

  Bubble outline / filled / dashed, Ring, Ring mask, Icon frame, Crest, Pen frame
      redrawn as smooth ink at four times their old pixel size (import them at four times the pixels per unit, with
      bilinear filtering and mipmaps, so they keep their size on the sheet and stay crisp at any resolution);
  Sheet icons/<name>.png
      the sheet's 32 px pixel-art icons (skills, attributes, milestones) upscaled four times with Scale2x run twice:
      it rounds the stair-stepped edges into clean lines without blurring them.

    python -I Tools/meta_sheet_smooth.py Assets/Game/UI/Sprites/Meta <icon.png> [<icon.png> ...]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

out = sys.argv[1]
icons = sys.argv[2:]
SS = 4                                   # drawn this many times larger again, then brought down (smooth edges)
INK, GREY, PAPER = (43, 39, 36, 255), (160, 156, 150, 255), (243, 241, 236, 255)
WHITE = (255, 255, 255, 255)


def canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def save(img, w, h, name):
    img.resize((w, h), Image.LANCZOS).save(os.path.join(out, name + ".png"))


# Bubbles (white; the sheet tints them): 60 px.
B = 60
img = canvas(B, B); d = ImageDraw.Draw(img)
d.ellipse([5 * SS, 5 * SS, (B - 5) * SS, (B - 5) * SS], outline=WHITE, width=6 * SS)
save(img, B, B, "Bubble outline")
img = canvas(B, B); d = ImageDraw.Draw(img)
d.ellipse([3 * SS, 3 * SS, (B - 3) * SS, (B - 3) * SS], fill=WHITE)
save(img, B, B, "Bubble filled")
img = canvas(B, B); d = ImageDraw.Draw(img)
for k in range(8):                        # eight dashes round the circle
    a0 = k * 45 + 8
    d.arc([5 * SS, 5 * SS, (B - 5) * SS, (B - 5) * SS], a0, a0 + 27, fill=WHITE, width=6 * SS)
save(img, B, B, "Bubble dashed")

# Portrait ring: a ring of ink dots round a thin circle, 520 px.
R = 520
img = canvas(R, R); d = ImageDraw.Draw(img)
c = R * SS / 2
for k in range(56):
    a = np.radians(k * 360 / 56)
    x, y = c + np.cos(a) * 247 * SS, c + np.sin(a) * 247 * SS
    d.ellipse([x - 7 * SS, y - 7 * SS, x + 7 * SS, y + 7 * SS], fill=INK)
d.ellipse([c - 222 * SS, c - 222 * SS, c + 222 * SS, c + 222 * SS], outline=INK, width=3 * SS)
save(img, R, R, "Ring")
M = 440
img = canvas(M, M); d = ImageDraw.Draw(img)
d.ellipse([2 * SS, 2 * SS, (M - 2) * SS, (M - 2) * SS], fill=WHITE)
save(img, M, M, "Ring mask")

# Icon frame (9-sliced at 16): a firm ink square, a fine grey line inside it, paper within. 144 px.
F = 144
img = canvas(F, F); d = ImageDraw.Draw(img)
d.rectangle([4 * SS, 4 * SS, (F - 4) * SS, (F - 4) * SS], fill=PAPER, outline=INK, width=6 * SS)
d.rectangle([13 * SS, 13 * SS, (F - 13) * SS, (F - 13) * SS], outline=GREY, width=2 * SS)
save(img, F, F, "Icon frame")

# Crest: a shield of ink with a fine grey inner line, 128 x 144.
W, H = 128, 144
img = canvas(W, H); d = ImageDraw.Draw(img)
def shield(inset):
    i = inset
    return [((8 + i) * SS, (8 + i) * SS), ((W - 8 - i) * SS, (8 + i) * SS), ((W - 8 - i) * SS, (H * .62) * SS),
            ((W / 2) * SS, (H - 8 - i * 1.3) * SS), ((8 + i) * SS, (H * .62) * SS)]
d.polygon(shield(0), fill=PAPER)
d.line(shield(0) + [shield(0)[0]], fill=INK, width=7 * SS, joint="curve")
d.line(shield(11) + [shield(11)[0]], fill=GREY, width=2 * SS, joint="curve")
save(img, W, H, "Crest")

# Pen frame (9-sliced at 12): a white ink line the sheet tints, 48 px.
P = 48
img = canvas(P, P); d = ImageDraw.Draw(img)
d.rectangle([3 * SS, 3 * SS, (P - 3) * SS, (P - 3) * SS], outline=WHITE, width=4 * SS)
save(img, P, P, "Pen frame")


# ── Icons: Scale2x twice ──────────────────────────────────────────────
def scale2x(a):
    h, w = a.shape[:2]
    p = np.pad(a, ((1, 1), (1, 1), (0, 0)), mode="edge")
    B_, D_, E_, F_, H_ = p[:-2, 1:-1], p[1:-1, :-2], p[1:-1, 1:-1], p[1:-1, 2:], p[2:, 1:-1]
    eq = lambda x, y: np.all(x == y, axis=2)
    c = (~eq(B_, H_)) & (~eq(D_, F_))
    e0 = np.where((c & eq(D_, B_))[..., None], D_, E_)
    e1 = np.where((c & eq(B_, F_))[..., None], F_, E_)
    e2 = np.where((c & eq(D_, H_))[..., None], D_, E_)
    e3 = np.where((c & eq(H_, F_))[..., None], F_, E_)
    o = np.zeros((h * 2, w * 2, a.shape[2]), a.dtype)
    o[0::2, 0::2], o[0::2, 1::2], o[1::2, 0::2], o[1::2, 1::2] = e0, e1, e2, e3
    return o


os.makedirs(os.path.join(out, "Sheet icons"), exist_ok=True)
for path in icons:
    a = np.asarray(Image.open(path).convert("RGBA"))
    a = scale2x(scale2x(a))
    Image.fromarray(a).save(os.path.join(out, "Sheet icons", os.path.basename(path)))
print("shapes and", len(icons), "icons written")

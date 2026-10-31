"""The character sheet's class portraits: a head-and-shoulders square cut from each class painting
(Assets/Game/Art/Portraits Highquality), written twice into UI/Sprites/Meta/Portraits: <Class> pencil.png (drawn in
graphite on the sheet's paper, the look the sheet uses) and <Class> colour.png (the painting as it is; picked with
ProgressionManager > Meta progression > Sheet > Colour Sheet Portraits). The sheet's round mask cuts them to a circle.

    python Tools/sheet_portraits.py [<class> ...]     (all of them without names)
"""
import sys
import numpy as np
from PIL import Image, ImageFilter, ImageOps

SRC = "Assets/Game/Art/Portraits Highquality/"
OUT = "Assets/Game/UI/Sprites/Meta/Portraits/"
# class: painting, centre x, centre y, square size (in the 1536 x 2048 paintings)
CROPS = {
    "Warrior": ("ShieldMaiden", 768, 600, 920),
    "Cleric": ("DwarfWarrior", 800, 880, 1200),
    "Wizard": ("WomanMage", 760, 760, 1000),
    "Arcanist": ("HalfOrcAssassin", 780, 600, 960),
    "Rogue": ("WomanDuellist", 700, 520, 900),
}
# class: the share of the drawing left as bare paper (a painting with a flat mid-tone background, the Rogue's dusk, drew as
# a grey card with no paper showing; its lightest share is lifted to the paper). Classes not listed keep their tones.
PAPER_SHARE = {"Rogue": .3}
PAPER = (236, 222, 186)   # the sheet's paper
INK = (62, 54, 48)        # graphite
SIZE = 432                # twice the sheet's 216 circle


def crop(name):
    f, cx, cy, sz = CROPS[name]
    im = Image.open(SRC + f + ".jpg").convert("RGB")
    return im.crop((cx - sz // 2, cy - sz // 2, cx + sz // 2, cy + sz // 2)).resize((SIZE, SIZE), Image.LANCZOS)


def pencil(im, paper_share=0.0):
    """Lines from a colour dodge of the picture by its blurred negative, darkened to graphite, over a soft shading of its
    tones (never black), shading drawn in faint diagonal strokes, all as ink on the sheet's paper."""
    g0 = ImageOps.autocontrast(ImageOps.grayscale(im), cutoff=1)
    g = np.asarray(g0).astype(float)
    blur = np.asarray(Image.fromarray((255 - g).astype("uint8")).filter(ImageFilter.GaussianBlur(4))).astype(float)
    lines = (np.clip(g * 255 / (255 - blur + 1), 0, 255) / 255) ** 3.2
    tone = .35 + .65 * np.asarray(g0.filter(ImageFilter.GaussianBlur(1.5))).astype(float) / 255
    v = np.clip(lines * tone, 0, 1)
    if paper_share > 0:
        v = np.clip(v / max(.05, np.percentile(v, 100 - paper_share * 100)), 0, 1)
    yy, xx = np.mgrid[0:SIZE, 0:SIZE]
    hatch = np.sin((xx + yy) * .9) * .5 + .5
    v = np.where(v < .75, v * (.9 + .1 * hatch), v)
    out = np.zeros((SIZE, SIZE, 3))
    for i in range(3):
        out[..., i] = INK[i] + (PAPER[i] - INK[i]) * v
    return Image.fromarray(out.astype("uint8"))


if __name__ == "__main__":
    for name in sys.argv[1:] or CROPS:
        im = crop(name)
        im.save(OUT + f"{name} colour.png")
        pencil(im, PAPER_SHARE.get(name, 0.0)).save(OUT + f"{name} pencil.png")
        print(name)

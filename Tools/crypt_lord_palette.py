"""The Crypt Lord's colour palette (Sidekick's shared 32 x 32 layout), made from the first skeleton preset's palette:
blackened iron plate, an old-gold crown and trim, a deep crimson cape and cloth, aged ivory bone, burning red-orange eyes,
and every other colour pulled toward grey like the other Sidekick skeletons' (the house style's muted base).
Cells are (u, v) counted from the bottom, found from the faces each chosen part samples (Tools/crypt_lord.eval.cs).
    python Tools/crypt_lord_palette.py <out.png>"""
import sys
import colorsys
from PIL import Image

SRC = "Assets/Synty/SidekickCharacters/Characters/FantasySkeletons/FantasySkeletons_01/Textures/T_FantasySkeletons_01ColorMap.png"
LIB = "C:/Game projects/AssetLibrary/" + SRC
SET = {
    ((16, 16), (17, 16)): (62, 60, 66),         # armour plate: blackened iron
    ((16, 18), (17, 18)): (122, 92, 44),        # plate edges and trim: dark old gold
    ((10, 18), (11, 18)): (104, 28, 30),        # the crown's crest: crimson (gold read as blonde hair)
    ((17, 17),): (196, 150, 64),                # the crown's brow gem and face guard: bright old gold
    ((20, 8),): (146, 108, 56),                 # clasps: bronze
    ((14, 20), (15, 20)): (96, 26, 28),         # the cape: deep crimson
    ((0, 18),): (74, 24, 24),                   # cloth under the plate: darker crimson
    ((0, 20), (1, 20), (1, 21), (0, 21)): (44, 39, 42),   # leather and wraps: soot
    ((18, 4), (19, 4), (18, 5), (19, 5)): (176, 164, 138), # bone: aged ivory
    ((20, 4), (21, 4)): (128, 114, 92),         # bone in shade
    ((6, 20), (7, 20), (6, 21), (7, 21)): (52, 52, 58),  # boots: iron
    ((16, 4),): (255, 92, 34),                  # the eyes' glow: red-orange
}

def main(out):
    try:
        im = Image.open(SRC).convert("RGB")
    except FileNotFoundError:
        im = Image.open(LIB).convert("RGB")
    px = im.load()
    w, h = im.size
    fixed = {c for cells in SET for c in cells}
    for y in range(h):
        for x in range(w):
            if (x, h - 1 - y) in fixed: continue
            r, g, b = (v / 255 for v in px[x, y])
            hh, s, v = colorsys.rgb_to_hsv(r, g, b)
            r, g, b = colorsys.hsv_to_rgb(hh, s * .55, v * .94)
            px[x, y] = tuple(int(round(c * 255)) for c in (r, g, b))
    for cells, col in SET.items():
        for (u, v) in cells: px[u, h - 1 - v] = col
    im.save(out)
    print(out)

if __name__ == "__main__":
    main(sys.argv[1])

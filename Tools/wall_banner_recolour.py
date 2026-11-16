"""Cellar, catacomb and crypt colourings of the dungeon's wall banners, the long one and the torn one (the night
review, 2026-11-15).

The banner's own painting is kept texel for texel: only its blue cloth is given another hue (with its own light and
dark kept, a little duller and darker, as old cloth), and the pale knotwork is aged toward the cloth's colour. The gold
rod and everything else stay as painted.

    python Tools/wall_banner_recolour.py
writes Assets/Game/Levels/Shared/Props/Wall decor/Wall banner <red|green|black>.png from
Assets/Game/Levels/Dungeon1/Realms/Props/Full height banner/Full height banner.png, and Torn banner <colour>.png from
.../Dwarf banner torn/Dwarf banner torn.png.
"""
import colorsys
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROPS = os.path.join(ROOT, "Assets/Game/Levels/Dungeon1/Realms/Props")
# output name: the banner's own painting
SOURCES = {
    "Wall banner": os.path.join(PROPS, "Full height banner/Full height banner.png"),
    "Torn banner": os.path.join(PROPS, "Dwarf banner torn/Dwarf banner torn.png"),
}
OUT = os.path.join(ROOT, "Assets/Game/Levels/Shared/Props/Wall decor")

# name: (cloth hue 0-1, saturation x, value x, how far the pale weave leans to the cloth's hue)
LOOKS = {
    "red": (0.005, 1.05, 0.82, 0.10),      # a dull wine red: the Cellars
    "green": (0.27, 0.62, 0.70, 0.14),     # mouldy olive: the Catacombs
    "black": (0.74, 0.34, 0.42, 0.06),     # funeral black with a breath of violet: the Crypt
}


def recolour(im, hue, sat_x, val_x, lean):
    out = im.copy()
    px, src = out.load(), im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b = (c / 255 for c in src[x, y][:3])
            h, s, v = colorsys.rgb_to_hsv(r, g, b)
            if v < .02:
                continue                                   # the empty part of the sheet
            if .5 < h < .72 and s > .22:                   # the blue cloth
                nr, ng, nb = colorsys.hsv_to_rgb(hue, min(1, s * sat_x), v * val_x)
            elif s < .16 and v > .3:                       # the pale weave and knotwork: aged toward the cloth
                nr, ng, nb = colorsys.hsv_to_rgb(hue, lean, v * (.86 + .1 * val_x))
            else:
                continue                                   # the gold rod, shadows
            px[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255)) + tuple(src[x, y][3:])
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for piece, src in SOURCES.items():
        im = Image.open(src)
        for name, look in LOOKS.items():
            recolour(im, *look).save(os.path.join(OUT, piece + " " + name + ".png"))
            print("wrote " + piece + " " + name + ".png", im.size)


if __name__ == "__main__":
    main()

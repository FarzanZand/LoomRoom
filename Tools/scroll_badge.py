"""The scroll badge: a small rolled parchment in the outlined style of the spell icons (Pixel Items' Skills: 32 x 32,
a near-black outline), drawn on their pixel grid so ScrollSetup.Make can lay it over a spell's icon in the slot's
corner and the scroll reads as a scroll, not the tome or the readied spell.

    python Tools/scroll_badge.py [out.png]      (default Assets/Game/Items/Icons/Scroll badge.png)

Light from the top left as on the icons: each roll lit on its top edge and its left end, the sheet lighter on the left,
ink lines across it; every texel's tone varied a little (no flat fills)."""
import random
import sys

from PIL import Image

OUT = sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/Items/Icons/Scroll badge.png"

# K outline, H highlight, L light, M mid, D dark, I ink.
ART = [
    ".KKKKKKKKKK.",
    "KHHLLLLLLMDK",
    "KLMMMMMMMDDK",
    ".KKLLLLLMKK.",
    "..KLIIIIMK..",
    "..KLMMLMMK..",
    "..KLIIIMDK..",
    "..KLLMMMDK..",
    "..KLIIMMDK..",
    ".KKLLLLMMKK.",
    "KHHLLLLLLMDK",
    "KLMMMMMMMDDK",
    ".KKKKKKKKKK.",
]
COLOURS = {
    "K": (13, 7, 18),          # the spell icons' own outline
    "H": (246, 228, 190),
    "L": (226, 198, 150),
    "M": (196, 160, 112),
    "D": (146, 106, 72),
    "I": (112, 70, 52),
}


def main():
    rng = random.Random(41)
    w, h = len(ART[0]), len(ART)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    for y, row in enumerate(ART):
        for x, c in enumerate(row):
            if c == ".":
                continue
            r, g, b = COLOURS[c]
            if c != "K":
                v = rng.randint(-9, 9)
                warm = rng.randint(-4, 4)
                r, g, b = r + v + warm, g + v, b + v - warm
            img.putpixel((x, y), (max(0, min(255, r)), max(0, min(255, g)), max(0, min(255, b)), 255))
    img.save(OUT)
    print(f"{OUT}: {w} x {h}")


if __name__ == "__main__":
    main()

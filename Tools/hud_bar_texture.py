"""Paints the HUD bar fill (Assets/Game/UI/Sprites/Diablo/Plain bar fill.png): a 48 x 20 greyscale strip that the bar's
colour tints. A bright top edge, a shaded bottom, and grain in between like the inventory's grey slots, so the bars
aren't flat. Imported as a Tiled sprite (border top 2, bottom 3): the edges stay put and the grain repeats at its own
size however long the bar is.

    python Tools/hud_bar_texture.py
"""
import random
from pathlib import Path

from PIL import Image

W, H = 48, 20
OUT = Path(__file__).resolve().parent.parent / "Assets/Game/UI/Sprites/Diablo/Plain bar fill.png"


def main():
    rng = random.Random(7)
    im = Image.new("RGBA", (W, H))
    for y in range(H):
        for x in range(W):
            if y == 0:
                v = 1.0
            elif y == 1:
                v = .96
            elif y >= H - 3:
                v = (.74, .66, .58)[y - (H - 3)]
            else:
                # The body darkens a little toward the bottom, with grain and a few darker specks.
                t = (y - 2) / (H - 6)
                v = .93 - .09 * t + rng.uniform(-.05, .05)
                if rng.random() < .05:
                    v -= .12
            g = max(0, min(255, round(v * 255)))
            im.putpixel((x, y), (g, g, g, 255))
    im.save(OUT)
    print(OUT)


if __name__ == "__main__":
    main()

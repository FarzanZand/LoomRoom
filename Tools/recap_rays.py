"""Paints the light rays behind the run recap's big spoils image (Assets/Game/UI/Sprites/Meta/Spoils rays.png): a
64 x 64 white sunburst, ten rays that fade out from the middle, alpha stepped into a few bands for the pixel look. The
recap tints it gold and turns it slowly (RunRecapUI).

    python Tools/recap_rays.py
"""
import math
from pathlib import Path

from PIL import Image

N = 64
RAYS = 10
OUT = Path(__file__).resolve().parent.parent / "Assets/Game/UI/Sprites/Meta/Spoils rays.png"


def main():
    im = Image.new("RGBA", (N, N), (0, 0, 0, 0))
    c = (N - 1) / 2
    for y in range(N):
        for x in range(N):
            dx, dy = x - c, y - c
            r = math.hypot(dx, dy) / c
            if r > 1:
                continue
            angle = math.atan2(dy, dx)
            ray = max(0.0, math.cos(angle * RAYS / 2) ** 2 * 1.4 - .4)   # narrow wedges
            glow = max(0.0, 1 - r * 1.6)                                  # a soft core
            a = (ray * (1 - r) ** .8 * .8 + glow * .7)
            a = min(1.0, a)
            a = round(a * 4) / 4                                          # four bands
            if a > 0:
                im.putpixel((x, y), (255, 255, 255, int(a * 255)))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    im.save(OUT)
    print(OUT)


if __name__ == "__main__":
    main()

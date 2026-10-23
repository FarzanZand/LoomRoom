"""The campaign map's plaid cloth: a red tartan in flat colours, no grain. A sett of stripes runs both ways; where two
stripes cross, a 2/2 twill shows both threads (the warp on two texels of four along each diagonal), like real woven
cloth, so the crossings read as cloth rather than flat paint. Usage: python tartan.py <out.png> [size]"""
import sys
import numpy as np
from PIL import Image

out = sys.argv[1]
N = int(sys.argv[2]) if len(sys.argv) > 2 else 51

C = {
    "R": (150, 34, 28),    # red ground
    "r": (118, 26, 24),    # darker red band
    "G": (34, 70, 52),     # green
    "K": (30, 34, 52),     # navy
    "Y": (196, 150, 70),   # gold line
}
# One repeat of the sett (texels); it fills the tile exactly.
sett = [("R", 12), ("K", 2), ("R", 2), ("G", 8), ("R", 2), ("K", 2), ("R", 12), ("r", 4), ("Y", 1), ("r", 4), ("R", 2)]
assert sum(n for _, n in sett) == N, sum(n for _, n in sett)
line = "".join(c * n for c, n in sett)

img = np.zeros((N, N, 3), np.uint8)
for y in range(N):
    for x in range(N):
        warp, weft = line[x], line[y]
        # 2/2 twill: along each diagonal, two texels show the warp thread, two the weft.
        img[y, x] = C[warp] if ((x + y) // 2) % 2 == 0 else C[weft]
Image.fromarray(img, "RGB").save(out)
print("tartan", N, "x", N)

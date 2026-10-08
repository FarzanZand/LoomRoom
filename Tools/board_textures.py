"""Board-game tile art: the cut edge of a tile (layers of card) and the printed grid over each cell.
Run: python -I Tools/board_textures.py Assets/Game/Levels/Shared/Board
"""
import sys, os, random
from PIL import Image
random.seed(5)
out = sys.argv[1]

# Edge: 16 x 6, stretched over a tile's side. Printed top lip, pale card layers with a darker core.
W, H = 16, 6
edge = Image.new("RGB", (W, H)); px = edge.load()
rows = [(206, 188, 150), (176, 146, 104), (152, 122, 84), (98, 74, 52), (150, 120, 82), (118, 92, 64)]
for y, base in enumerate(rows):
    for x in range(W):
        n = random.randint(-6, 6)
        px[x, y] = tuple(max(0, min(255, c + n)) for c in base)
edge.save(os.path.join(out, "Board edge.png"))

# Grid: 16 x 16, transparent, a thin printed line along two sides (so neighbours share one line).
G = 16
grid = Image.new("RGBA", (G, G), (0, 0, 0, 0)); gp = grid.load()
for i in range(G):
    a = 150 + random.randint(-15, 15)
    gp[i, G - 1] = (24, 18, 14, a)
    gp[0, i] = (24, 18, 14, a)
grid.save(os.path.join(out, "Board grid.png"))
print("ok")

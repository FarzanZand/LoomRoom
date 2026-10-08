"""Pixel-art dungeon door, drawn at the walls' texel density (32 texels per 3-unit tile, so a
1.9 x 2.8 door is 20 x 30). Planks with grain, two shaded riveted iron straps and a ring handle,
with a shaded edge.
Run: python -I Tools/door_texture.py "Assets/Game/Levels/Dungeon1/Materials/Dungeon door.png"
"""
import sys, random
from PIL import Image

random.seed(11)
W, H = 20, 30
img = Image.new("RGB", (W, H))
px = img.load()

def clamp(c): return tuple(max(0, min(255, int(v))) for v in c)

# Planks: four boards, each a slightly different oak, with grain runs and a lit left edge.
planks = [(0, 5), (5, 10), (10, 15), (15, 20)]
bases = [(112, 74, 50), (119, 79, 54), (105, 69, 47), (115, 76, 52)]
for (x0, x1), base in zip(planks, bases):
    for x in range(x0, x1):
        for y in range(H):
            n = random.randint(-7, 7)
            c = [b + n for b in base]
            if x == x0 + 1: c = [v + 14 for v in c]           # lit edge
            if x == x1 - 1: c = [v - 12 for v in c]           # shaded edge
            px[x, y] = clamp(c)
    # grain: short darker vertical streaks
    for _ in range(5):
        gx = random.randint(x0 + 1, x1 - 2); gy = random.randint(0, H - 6); ln = random.randint(3, 7)
        for y in range(gy, min(H, gy + ln)):
            r, g, b = px[gx, y]; px[gx, y] = clamp((r - 22, g - 16, b - 10))
    # a knot
    kx, ky = random.randint(x0 + 1, x1 - 2), random.randint(8, H - 8)
    px[kx, ky] = clamp([b - 40 for b in base])

# Iron, shaded: a lit top row, a mid tone with a little wear, a dark underside, and darker ends
# where the strap wraps the door's edge.
def iron(x, y, row, rows):
    shade = [118, 92, 70, 44][min(3, int(row * 4 / rows))]
    shade += random.choice((0, 0, 0, -6, 5))
    if x <= 1 or x >= W - 2: shade -= 14
    return clamp((shade - 6, shade - 4, shade + 4))

def strap(y, rows=3):
    for x in range(W):
        for r in range(rows): px[x, y + r] = iron(x, y + r, r, rows)
    # rivets: a bright head, its shadow below and to the right
    for x in range(2, W - 2, 4):
        px[x, y + 1] = (168, 170, 178)
        px[x + 1, y + 1] = clamp((54, 54, 60))
        px[x, y + 2] = clamp((36, 36, 42))

strap(4)
strap(23)

# Ring handle on the opening side: lit on top, dark below, with the plate it hangs from.
plate = [(15, 17), (16, 17)]
for x, y in plate: px[x, y] = (70, 72, 80)
ring = {(15, 18): 132, (16, 18): 120, (14, 19): 104, (17, 19): 84, (14, 20): 80, (17, 20): 62, (15, 21): 54, (16, 21): 46}
for (x, y), v in ring.items(): px[x, y] = clamp((v - 6, v - 4, v + 4))
for x, y in ((15, 19), (16, 19), (15, 20), (16, 20)):
    r, g, b = px[x, y]; px[x, y] = clamp((r - 30, g - 24, b - 18))   # the shadow inside the ring

# Wear at the foot, then the outline.
for x in range(W):
    for y in (H - 2, H - 3):
        r, g, b = px[x, y]; px[x, y] = clamp((r - 18, g - 16, b - 12))
# The edge: the wood (or iron) itself in shade, lit along the top and left, darkest bottom and right,
# so it reads as the board's thickness instead of a drawn line.
def edge(x, y, k):
    r, g, b = px[x, y]
    v = random.choice((0, 0, -4, 4))
    px[x, y] = clamp((r * k + v, g * k + v, b * k + v + 3))
for x in range(W): edge(x, 0, .62); edge(x, H - 1, .34)
for y in range(1, H - 1): edge(0, y, .5); edge(W - 1, y, .38)

img.save(sys.argv[1])
print(sys.argv[1], img.size)

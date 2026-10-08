"""Pixel-art barrel texture in the dungeon door's style, laid out for Tools/pixel_bake.py's
cylinder unwrap. The hoop heights are read from that bake of the Meshy paint (iron is greyer than
the oak), everything else is drawn: staves with grain, lit and shaded edges, shaded iron hoops
with rivets, and two planked lids with a dark chime ring.
Run: python -I Tools/barrel_texture.py <Name_bake.png> <Name_layout.json> <out.png> [--seed 5]
"""
import argparse, json, math, random
from PIL import Image

SUPER = 8
p = argparse.ArgumentParser()
p.add_argument("bake"); p.add_argument("layout"); p.add_argument("out")
p.add_argument("--seed", type=int, default=5)
a = p.parse_args()
random.seed(a.seed)
# Layout from pixel_bake.py: side strip rows [0, side_h) of side_w columns (+ seam columns),
# one gap row, then two lid squares. Rows here count up from the bottom of the texture.
L = json.load(open(a.layout))
W, H, side_w, side_h, lid = L["W"], L["H"], L["side_w"], L["side_h"], L["lid"]

def clamp(c): return tuple(max(0, min(255, int(v))) for v in c)

img = Image.new("RGB", (W, H), (40, 28, 22))
px = img.load()
def put(x, y, c): px[x, H - 1 - y] = clamp(c)      # y up, like the UVs
def get(x, y): return px[x, H - 1 - y]

# --- hoops from the bake ------------------------------------------------------------------------
bake = Image.open(a.bake).convert("RGB")
bp = bake.load()
hoop = []
for row in range(side_h):
    best = 0                                   # thin hoops cover only part of a row
    for sy in range(SUPER):
        y = bake.height - 1 - (row * SUPER + sy)
        xs = range(0, side_w * SUPER, 2)
        best = max(best, sum(1 for x in xs if bp[x, y][2] >= bp[x, y][0] - 4) / len(xs))
    hoop.append(best > 0.3)

# --- staves -------------------------------------------------------------------------------------
widths, x = [], 0
while x < side_w:
    w = min(random.choice((3, 3, 4)), side_w - x)
    if side_w - x - w in (1, 2): w = side_w - x
    widths.append(w); x += w
bases = [(112, 74, 50), (119, 79, 54), (105, 69, 47), (115, 76, 52), (109, 72, 49)]
x0 = 0
for i, w in enumerate(widths):
    base = bases[i % len(bases)] if i < len(widths) - 1 or len(widths) % len(bases) != 1 else bases[2]
    for x in range(x0, x0 + w):
        for y in range(side_h):
            c = [b + random.randint(-7, 7) for b in base]
            if x == x0: c = [v + 14 for v in c]                 # lit edge
            if x == x0 + w - 1: c = [v - 30 for v in c]         # dark joint
            bulge = abs(y - (side_h - 1) / 2) / ((side_h - 1) / 2)
            c = [v - 14 * bulge * bulge for v in c]             # ends curve away from the light
            put(x, y, c)
    for _ in range(2):                                          # grain
        gx = random.randint(x0, x0 + w - 1); gy = random.randint(0, side_h - 3)
        for y in range(gy, min(side_h, gy + random.randint(2, 4))):
            r, g, b = get(gx, y); put(gx, y, (r - 20, g - 15, b - 9))
    x0 += w

# --- iron hoops: each run of hoop rows, lit top, dark underside, rivets, a shadow below --------
runs, y = [], 0
while y < side_h:
    if hoop[y]:
        y1 = y
        while y1 + 1 < side_h and hoop[y1 + 1]: y1 += 1
        runs.append((y, y1)); y = y1 + 1
    else: y += 1
for y0, y1 in runs:
    rows = y1 - y0 + 1
    for y in range(y0, y1 + 1):
        t = (y1 - y) / max(1, rows - 1) if rows > 1 else 0.4    # 0 = top row
        shade = [112, 88, 66][min(2, int(t * 2.99))]
        for x in range(side_w):
            s = shade + random.choice((0, 0, 0, -6, 5))
            put(x, y, (s - 8, s - 4, s + 4))
    ry = y1 if rows == 1 else y1 - (rows - 1) // 2
    for x in range(2, side_w, 8):
        put(x, ry, (150, 146, 140))
    if y0 > 0 and not hoop[y0 - 1]:
        for x in range(side_w):
            r, g, b = get(x, y0 - 1); put(x, y0 - 1, (r - 18, g - 14, b - 10))

# seam columns repeat the first ones, so faces that cross the seam stay continuous
for x in range(side_w, W):
    for y in range(side_h):
        put(x, y, get(x - side_w, y))
for x in range(W):                                              # gap row: stave-end colour
    put(x, side_h, (62, 42, 30))

# --- lids: planks across, dark chime ring ------------------------------------------------------
def draw_lid(cx, seed_shift):
    r = lid / 2
    cy = side_h + 1 + lid / 2
    for y in range(side_h + 1, side_h + 1 + lid):
        for x in range(int(cx - r) - 1, int(cx + r) + 1):
            if not (0 <= x < W): continue
            d = math.hypot(x + 0.5 - cx, y + 0.5 - cy) / r
            plank = int((x + 0.5 - (cx - r)) / (lid / 3))
            base = bases[(plank + seed_shift) % len(bases)]
            c = [b - 6 + random.randint(-6, 6) for b in base]
            if (x + 0.5 - (cx - r)) % (lid / 3) < 1: c = [v - 16 for v in c]
            if d > 0.78: c = [v * 0.55 for v in base]           # chime (stave ends)
            if d > 1.0: c = [44, 30, 22]
            put(x, y, c)
draw_lid(1 + lid / 2, 0)
draw_lid(2 + lid * 1.5, 2)

img.save(a.out)
print(f"hoop rows {[i for i, h in enumerate(hoop) if h]} staves {len(widths)}")

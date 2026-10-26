"""Herb broth's icon: the Broth bowl rendered by the icon renderer from above (TavernDressIcons.Make, turn -130 x), with
green broth painted into the bowl by hand, texel by texel (muted greens, a dark meniscus where it meets the bowl, herb
flecks and leaves, oil glints and a lit streak toward the key light).
python Tools/herb_broth_icon.py <rendered bowl.png> <out.png>"""
import random, sys
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
im = Image.open(src).convert('RGBA'); px = im.load()
rnd = random.Random(9)
cx, cy, rx, ry = 43.5, 38.5, 32.5, 19.5          # the bowl's opening in this render
GREENS = [(46, 66, 30), (56, 78, 34), (68, 92, 40), (80, 104, 46), (94, 118, 54), (108, 130, 62)]
def inside(x, y, shrink=0.0):
    return ((x + .5 - cx) / (rx - shrink)) ** 2 + ((y + .5 - cy) / (ry - shrink * .6)) ** 2 <= 1
for y in range(im.height):
    for x in range(im.width):
        if not inside(x, y) or px[x, y][3] < 128: continue
        d = ((x + .5 - cx) / rx) ** 2 + ((y + .5 - cy) / ry) ** 2
        # lighter toward the upper left (the key light), in steps; darker at the far edge under the rim
        k = 3.0 - (x - cx) / rx * .9 - (y - cy) / ry * .7 + (rnd.random() - .5) * 1.6
        if y < cy and d > .55: k -= 1.4                       # the rim's shadow on the far side
        c = GREENS[max(0, min(len(GREENS) - 1, int(k)))]
        if not inside(x, y, 1.6): c = tuple(int(v * .55) for v in GREENS[0])   # meniscus
        px[x, y] = c + (255,)
# herb flecks, small leaves, oil glints, a lit streak
for _ in range(34):
    x, y = rnd.randrange(int(cx - rx), int(cx + rx)), rnd.randrange(int(cy - ry), int(cy + ry))
    if inside(x, y, 3): px[x, y] = (28, 46, 20, 255)
for _ in range(7):
    x, y = rnd.randrange(int(cx - rx + 6), int(cx + rx - 6)), rnd.randrange(int(cy - ry + 4), int(cy + ry - 4))
    if not inside(x, y, 4): continue
    for dx, dy in ((0, 0), (1, 0), (2, -1), (1, -1), (3, -1)):
        px[x + dx, y + dy] = (40, 84, 30, 255)
    px[x + 1, y - 2] = (120, 150, 70, 255)                      # the leaf's lit edge
for _ in range(10):
    x, y = rnd.randrange(int(cx - rx + 4), int(cx + 4)), rnd.randrange(int(cy - ry + 3), int(cy + 3))
    if inside(x, y, 3): px[x, y] = (170, 168, 86, 255)
for i in range(9):
    x, y = int(cx - 16 + i), int(cy - 9 + i // 3)
    if inside(x, y, 3): px[x, y] = (150, 160, 92, 255)
im.save(out)
print('broth painted')

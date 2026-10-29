# python Tools/dice_portal_texture.py <out.png>
# The Dice Portal's vortex: a strip of 8 frames (128 x 128 each) of a dark swirl turning into its middle inside a
# crackling rim, in a few flat blues laid texel by texel (no smooth gradients), with alpha cut raggedly at the edge.
# The arms turn a third of a circle over the 8 frames, so with three arms the strip loops.
import math, random, sys
from PIL import Image

FRAMES, SIZE = 8, 128
# Deep to light: the hole, the dark of the swirl, its arms, the rim, the rim's sparks.
PALETTE = [(8, 10, 26), (16, 24, 58), (26, 44, 96), (40, 74, 140), (70, 120, 190), (128, 182, 232), (214, 240, 255)]

def noise(x, y, seed):
    r = random.Random(x * 73856093 ^ y * 19349663 ^ seed * 83492791)
    return r.random()

def frame(f):
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    px = img.load()
    phase = f / FRAMES * (2 * math.pi / 3)
    c = (SIZE - 1) / 2
    for y in range(SIZE):
        for x in range(SIZE):
            dx, dy = (x - c) / c, (y - c) / c
            r = math.hypot(dx, dy)
            a = math.atan2(dy, dx)
            # The ragged edge: the rim's outer line wavers texel to texel (fixed per texel, so it doesn't boil).
            edge = .965 - noise(x // 3, y // 3, 7) * .035 - noise(x, y, 8) * .012
            if r > edge:
                continue
            n = noise(x // 2, y // 2, f + 11)   # the grain that changes as it turns, in small clusters
            g = noise(x // 2, y // 2, 3)        # fixed grain
            if r > .84:
                # The rim: light, broken by darker flecks; sparks now and then.
                t = 4 + (1 if n > .35 else 0) + (1 if n > .9 else 0) - (1 if g < .15 else 0)
            else:
                # The swirl: three arms winding in (log spiral), lighter toward the rim, a dark hole in the middle.
                arm = math.cos(3 * (a + 1.6 * math.log(max(r, .02))) - phase * 3)
                v = .5 + .5 * arm
                v = v * (.35 + .75 * r) + (n - .5) * .16
                if r < .16 + g * .04:
                    v *= .25
                t = int(max(0, min(4, v * 5.2)))
                # A dark line just inside the rim (the rim's crease).
                if .79 < r <= .84:
                    t = max(0, t - 2)
            px[x, y] = PALETTE[t] + (255,)
    return img

out = Image.new("RGBA", (SIZE * FRAMES, SIZE), (0, 0, 0, 0))
for f in range(FRAMES):
    out.paste(frame(f), (f * SIZE, 0))
out.save(sys.argv[1])
print("wrote", sys.argv[1])

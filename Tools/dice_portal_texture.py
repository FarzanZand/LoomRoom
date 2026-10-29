# python Tools/dice_portal_texture.py <out.png>
# The Dice Portal: a hole burned through the air, as the campaign map burns away: a ragged edge with a thin line of
# embers, a charred rim flecked with ash outside it, and dim light inside, all in a dark blue tint. A strip
# of 8 frames (128 x 128 each): the embers flicker and creep and the edge wavers a little. Drawn texel by texel in the
# house style: a few muted tones laid in small clusters, dark at the crease, lit at the edge, no smooth gradients.
import math, random, sys
from PIL import Image

FRAMES, SIZE = 8, 128
# Everything in a dark blue tint (the user's choice, 2026-10-29).
# The light through the hole, deep to bright (muted navy).
INSIDE = [(6, 8, 18), (10, 14, 30), (16, 23, 46), (24, 35, 66), (36, 52, 92)]
# The burning line, cold: dim blue to white-hot.
EMBER = [(28, 46, 120), (46, 84, 182), (82, 136, 230), (150, 196, 255), (220, 236, 255)]
# The charred rim, blue-black to slate ash.
CHAR = [(8, 10, 16), (15, 19, 30), (24, 30, 46), (38, 46, 66), (78, 88, 108)]

def noise(x, y, seed):
    return random.Random(x * 73856093 ^ y * 19349663 ^ seed * 83492791).random()

def wave(a, seed, lobes):
    # A ragged closed outline: a few sines round the circle with seeded phases.
    r = random.Random(seed)
    return sum(math.sin(a * k + r.random() * 6.283) * (r.random() * .5 + .5) / k for k in lobes)

def frame(f):
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    px = img.load()
    c = (SIZE - 1) / 2
    creep = f / FRAMES * 6.283
    for y in range(SIZE):
        for x in range(SIZE):
            dx, dy = (x - c) / c, (y - c) / c
            r = math.hypot(dx, dy)
            a = math.atan2(dy, dx)
            # Where the burn has eaten to: ragged, wavering a little as it burns (fixed per texel cluster).
            edge = .68 + .09 * wave(a, 11, (2, 3, 5, 9)) + .02 * math.sin(a * 13 + creep) + (noise(x // 3, y // 3, 4) - .5) * .05
            # How far the char reaches past it.
            outer = edge + .17 + .05 * wave(a, 23, (4, 7, 11)) + (noise(x // 3, y // 3, 9) - .5) * .06
            n = noise(x // 2, y // 2, f + 31)   # flicker, in small clusters
            g = noise(x // 2, y // 2, 5)        # fixed grain
            if r < edge - .045:
                # Inside: dark, the room's warm light coming through in patches (clusters of a few tones, no smooth
                # falloff), a darker crease along the burnt edge.
                patch = noise(x // 5, y // 5, 17) * .6 + noise(x // 3, y // 3, 19) * .4
                v = patch * 4.4 - 1.1 + (g - .5) * .9 + (n - .5) * .3 - r * .8
                if r > edge - .12: v -= 1.4
                col = INSIDE[int(max(0, min(4, v)))]
            elif r < edge:
                # The burning line: embers, hotter where the flicker says, now and then a dark gap.
                v = n * 4.6 + g * .8 - .4
                col = EMBER[int(max(0, min(4, v)))]
                if n < .1: col = CHAR[1]
            elif r < outer:
                # The char: black at the embers, browner and ash-flecked further out, ragged at its edge.
                t = (r - edge) / max(.02, outer - edge)
                v = t * 2.6 + (g - .5) * 1.4
                col = CHAR[int(max(0, min(3, v)))]
                if g > .93: col = CHAR[4]                      # ash
                if t < .25 and n > .9: col = EMBER[0]          # a glowing fleck just behind the line
            else:
                continue
            px[x, y] = col + (255,)
    return img

out = Image.new("RGBA", (SIZE * FRAMES, SIZE), (0, 0, 0, 0))
for f in range(FRAMES):
    out.paste(frame(f), (f * SIZE, 0))
out.save(sys.argv[1])
print("wrote", sys.argv[1])

"""The disco ball's pixel texture (Combat/Spells/Disco Ball): mirror tiles on Unity's sphere (its UVs wrap round once and
from pole to pole), at the dungeon's 128 texels a metre for a 0.4 m ball (its UVs cover 0.95 of the square): 128 x 64 texels, tiles 4 x 4 with a dark
seam between them. Each tile is a mirror catching a different light: mostly silvers, a few lit white, a few catching
pink, cyan, gold or violet, each grained texel to texel and darker at its lower edge. The emission map keeps only the
lit and coloured tiles, so the ball sparkles in the dark.
Usage: python Tools/disco_ball_texture.py <out dir>"""
import os, random, sys
from PIL import Image

W, H, T = 128, 64, 4
GLINTS = [(255, 110, 200), (90, 230, 255), (255, 210, 90), (180, 120, 255), (120, 255, 160)]

def main(out):
    rnd = random.Random(11)
    base = Image.new("RGB", (W, H)); glow = Image.new("RGB", (W, H))
    for ty in range(H // T):
        for tx in range(W // T):
            roll = rnd.random()
            if roll < .07: colour, lit = GLINTS[rnd.randrange(len(GLINTS))], 1.0
            elif roll < .22: colour, lit = (245, 245, 250), .8
            else:
                v = rnd.randint(95, 200); colour, lit = (v, v + rnd.randint(-4, 6), v + rnd.randint(0, 12)), 0.0
            for y in range(T):
                for x in range(T):
                    px, py = tx * T + x, ty * T + y
                    if x == 0 or y == 0:
                        base.putpixel((px, py), (28, 26, 32)); glow.putpixel((px, py), (0, 0, 0)); continue
                    shade = 1.0 - .18 * (y / (T - 1)) + rnd.uniform(-.06, .06)   # each mirror darker toward its foot
                    if x == 1 and y == 1: shade += .25                            # a catch of light in its corner
                    c = tuple(max(0, min(255, int(ch * shade))) for ch in colour)
                    base.putpixel((px, py), c)
                    glow.putpixel((px, py), tuple(int(ch * lit * .8) for ch in c) if lit else (0, 0, 0))
    os.makedirs(out, exist_ok=True)
    base.save(os.path.join(out, "Disco Ball.png")); glow.save(os.path.join(out, "Disco Ball emission.png"))
    print("wrote Disco Ball.png and Disco Ball emission.png to", out)

if __name__ == "__main__":
    main(sys.argv[1])

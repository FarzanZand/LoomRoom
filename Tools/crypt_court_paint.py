"""Paints the Crypt's paved court into the realm map's ground texture (branch dungeonprop).

The court is the level ground the dungeon entrance stands on in the map's near-left corner: dungeon flagstones in the
house style (flat tones laid stone by stone, dark mortar, a lit top edge and a dark lower edge per stone, grime and moss
toward the rim), giving way raggedly to trampled earth and loose stones, then to the land. Painted texel by texel at the
map's 6.4 texels a unit; the stones are laid on the texture's own rows so their joints stay crisp.

Always paints from the texture as it was before the first run (kept in Library), so it can be run again after tuning.
Run from the project folder:  python Tools/crypt_court_paint.py [spot_x spot_z]
"""
import math
import os
import shutil
import sys

from PIL import Image

TEX = "Assets/Game/Campaign/Map/Generated/terrain texture.png"
BASE = "Library/crypt_court_terrain_base.png"
HALF_X, HALF_Z = 39.0, 53.0

SPOT = (float(sys.argv[1]), float(sys.argv[2])) if len(sys.argv) > 2 else (-28.5, 41.5)
PAVED = 6.3          # paving radius (units), wandered by the edge noise below
EDGE = 2.0           # loose stones and earth past it
STONES = [(104, 97, 87), (114, 106, 94), (93, 88, 82), (122, 113, 99), (86, 81, 76), (108, 99, 92)]
MORTAR = (50, 45, 40)
EARTH = [(96, 77, 58), (86, 69, 53), (104, 84, 62)]
MOSS = (86, 94, 60)
# The stairwell cut behind the gate (CryptEntrance: the entrance turned FACING degrees, the steps within
# STAIR_HALF of its middle line from STAIR_FROM to STAIR_TO along it): its floor is dark earth, never the land's colours.
FACING = 195.0
STAIR_HALF, STAIR_FROM, STAIR_TO = 1.7, -1.1, -7.3
PIT = [(44, 38, 33), (38, 33, 29), (50, 43, 37)]
# The passage's own pixel copies (run this after CampaignMapBuilder.PixelProps): its stair ceiling and the rock walling it.
PIXEL = "Assets/Game/Campaign/Map/Pixel"
DARKER = [("SM_Env_Ceiling_Stone_Stairs_01 s-4 c423f2", .5), ("SM_Env_Rock_Cliff_01 s-6 60a8c0", .55)]


def h(*v):
    """A stable hash of integers to 0..1."""
    x = 0x9E3779B1
    for n in v:
        x = (x ^ (int(n) & 0xFFFFFFFF)) * 0x85EBCA6B & 0xFFFFFFFF
        x ^= x >> 13
        x = x * 0xC2B2AE35 & 0xFFFFFFFF
        x ^= x >> 16
    return x / 0xFFFFFFFF


def edge_radius(a):
    return PAVED + .55 * math.sin(a * 3 + .7) + .3 * math.sin(a * 5 + 2.1) + .2 * math.sin(a * 11 + .3)


def clamp(c):
    return tuple(max(0, min(255, int(round(v)))) for v in c)


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def main():
    if not os.path.exists(BASE):
        shutil.copyfile(TEX, BASE)
    img = Image.open(BASE).convert("RGBA")
    W, H = img.size
    px = img.load()
    tpu = W / (HALF_X * 2)

    def to_px(x, z):
        return (x + HALF_X) / (HALF_X * 2) * W, (H - 1) - (z + HALF_Z) / (HALF_Z * 2) * H

    cx, cy = to_px(*SPOT)
    reach = int((PAVED + EDGE + 1.5) * tpu) + 2

    # Stones laid in courses on the texture's rows: each course 5-7 texels high, each stone 6-10 texels wide, the joints
    # offset course to course.
    stone_at = {}
    y0 = int(cy) - reach
    y = y0
    course = 0
    while y < int(cy) + reach:
        ch = 4 + int(h(course, 7) * 5)
        x = int(cx) - reach - int(h(course, 3) * 8)
        k = 0
        while x < int(cx) + reach:
            sw = 5 + int(h(course, k, 11) * 8)
            for yy in range(y, y + ch):
                for xx in range(x, x + sw):
                    # the last row and column of each stone are the joint
                    joint = yy == y + ch - 1 or xx == x + sw - 1
                    # a crack: a jagged line across the odd stone, from one joint toward the other
                    crack = False
                    if h(course, k, 31) < .14 and not joint:
                        cx0 = x + 1 + int(h(course, k, 32) * max(1, sw - 2))
                        crack = xx == cx0 + (yy - y) // 2 * (1 if h(course, k, 33) < .5 else -1)
                    stone_at[(xx, yy)] = (course, k, joint, yy == y, yy == y + ch - 2, xx == x, crack)
            x += sw
            k += 1
        y += ch
        course += 1

    for yy in range(int(cy) - reach, int(cy) + reach):
        for xx in range(int(cx) - reach, int(cx) + reach):
            if not (0 <= xx < W and 0 <= yy < H):
                continue
            # the texel's middle in map units
            mx = (xx + .5) / W * HALF_X * 2 - HALF_X
            mz = ((H - 1 - yy) + .5) / H * HALF_Z * 2 - HALF_Z
            dx, dz = mx - SPOT[0], mz - SPOT[1]
            th = math.radians(FACING)
            ex, ez = dx * math.cos(th) - dz * math.sin(th), dx * math.sin(th) + dz * math.cos(th)
            if abs(ex) <= STAIR_HALF and STAIR_TO <= ez <= STAIR_FROM:
                px[xx, yy] = PIT[int(h(xx // 2, yy // 2, 71) * 3)] + (255,)
                continue
            dist = math.hypot(dx, dz)
            r = edge_radius(math.atan2(dz, dx))
            if dist > r + EDGE:
                continue
            orig = px[xx, yy][:3]
            info = stone_at.get((xx, yy))
            if info is None:
                continue
            ci, ki, joint, top, low, left, crack = info
            # how far out: 0 inside the paving, 1 at the outer edge of the loose stones
            out = max(0.0, (dist - (r - .7)) / (EDGE + .7))
            # a stone is laid when its own roll beats how far out it is (whole stones go, never half)
            laid = h(ci, ki, 5) > out * 1.25 - .05
            earth = clamp(EARTH[int(h(xx // 3, yy // 3, 17) * 3)])
            if laid:
                if joint:
                    c = MORTAR
                    if out > .15 and h(xx, yy, 23) < .5:
                        c = MOSS
                else:
                    c = STONES[int(h(ci, ki, 1) * len(STONES))]
                    # grain: clusters of a texel or two a shade off, never noise on every texel
                    g = h(xx // 2 + ci * 3, yy // 2 + ki * 5, 9)
                    if g < .22: c = (c[0] * .9, c[1] * .9, c[2] * .91)
                    elif g > .86: c = (c[0] * 1.07, c[1] * 1.06, c[2] * 1.05)
                    # the odd stone sunk and stained darker
                    if h(ci, ki, 37) < .1: c = (c[0] * .8, c[1] * .8, c[2] * .82)
                    if top:
                        c = (c[0] + 14, c[1] + 13, c[2] + 11)    # lit top edge
                    elif low:
                        c = (c[0] - 12, c[1] - 12, c[2] - 11)    # shadowed lower edge
                    if left and not top:
                        c = (c[0] + 5, c[1] + 5, c[2] + 4)
                    # a crack now and then
                    if crack:
                        c = (MORTAR[0] + 10, MORTAR[1] + 9, MORTAR[2] + 8)
                    # grime toward the rim, in whole patches
                    if out > 0 and h(xx // 3, yy // 3, 41) < out * .7:
                        c = mix(c, earth, .55)
                px[xx, yy] = clamp(c) + (255,)
            else:
                # trampled earth round the loose stones, giving way to the land past the rim
                t = (dist - r) / EDGE
                if t < .4 or h(xx // 3, yy // 3, 53) > t * 1.1:
                    px[xx, yy] = earth + (255,)
                else:
                    px[xx, yy] = orig + (255,)
    img.save(TEX)
    print(f"painted the court at {SPOT} into {TEX} ({W}x{H}, {tpu:.2f} texels a unit)")
    for name, factor in DARKER:
        darken(name, factor)


def darken(name, factor):
    """The passage under the crag falls away into the dark: its pixel copies, only used there, taken down texel by texel,
    their grain kept. Works from the bake as it was when first darkened, so running again doesn't darken twice; a new bake
    becomes the new start."""
    png = f"{PIXEL}/{name}/{name}.png"
    base = "Library/crypt_court_" + name.replace(" ", "_") + "_base.png"
    done = base + ".done"
    with open(png, "rb") as f:
        now = f.read()
    last = None
    if os.path.exists(done):
        with open(done, "rb") as f:
            last = f.read()
    if last != now or not os.path.exists(base):
        shutil.copyfile(png, base)
    img = Image.open(base).convert("RGBA")
    px = img.load()
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            r, g, b, a = px[x, y]
            # toward the crag's cool grey, so firelight on it reads as stone and not as wood
            v = (r + g + b) / 3
            c = mix((r, g, b), (v * .96, v * .98, v * 1.06), .5)
            px[x, y] = clamp((c[0] * factor, c[1] * factor, c[2] * factor)) + (a,)
    img.save(png)
    with open(png, "rb") as f, open(done, "wb") as d:
        d.write(f.read())
    print(f"darkened {name} by {factor}")


if __name__ == "__main__":
    main()

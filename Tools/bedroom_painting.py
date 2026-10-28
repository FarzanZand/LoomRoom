# The bedroom painting, drawn by rules: a keep on a sea cliff at dusk. Flat colour bands from a small muted palette,
# ordered dithering where two bands meet, a lit texel on every ridge and a dark crease under it, and a little
# texel-to-texel variation (the house style). 707 x 309, the painting's frame at the bedroom's 6.4 texels a unit.
#   python Tools/bedroom_painting.py      writes Assets/Game/World/Art/Bedroom/Painting.png (replaced the Meshy crop, 2026-10-28)
import numpy as np
from PIL import Image

W, H = 707, 309
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], np.float32) / 16
DITH = np.tile(BAYER, (H // 4 + 1, W // 4 + 1))[:H, :W]
YY, XX = np.mgrid[0:H, 0:W]

def c(h): return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)

def vnoise(scale, seed, octaves=4, shape=(H, W)):
    """Value noise in -1..1: random grids, bilinear, octaves halving."""
    r = np.random.default_rng(seed); out = np.zeros(shape, np.float32); amp = 1; tot = 0
    for o in range(octaves):
        gh = int(shape[0] / scale) + 3; gw = int(shape[1] / scale) + 3
        g = r.uniform(-1, 1, (gh, gw)).astype(np.float32)
        ys = np.linspace(0, gh - 2.001, shape[0]); xs = np.linspace(0, gw - 2.001, shape[1])
        y0 = ys.astype(int); x0 = xs.astype(int); fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        a = g[y0][:, x0]; b = g[y0][:, x0 + 1]; cc = g[y0 + 1][:, x0]; d = g[y0 + 1][:, x0 + 1]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (cc * (1 - fx) + d * fx) * fy)
        tot += amp; amp *= .5; scale /= 2
    return out / tot

def noise1(scale, seed, octaves=4): return vnoise(scale, seed, octaves, (1, W))[0]

def bands(t, cols, mask=None):
    """t in 0..1 to one of the colours, dithered between neighbours (t given for the whole canvas, or for mask's texels)."""
    d = DITH if mask is None else DITH[mask]
    t = np.clip(t, 0, 1) * (len(cols) - 1)
    lo = np.floor(t).astype(int); i = np.clip(lo + ((t - lo) > d), 0, len(cols) - 1)
    return np.stack(cols)[i]

def outline(img, mask, lit, crease):
    top = mask & ~np.roll(mask, 1, 0); img[top] = lit
    img[np.roll(top, 2, 0) & mask & ~top] = crease

def draw(out_path, _orig=None):
    rng = np.random.default_rng(5)
    img = np.zeros((H, W, 3), np.float32)
    sun = (448, 124)
    # Sky: slate high up, through dusty rose to a warm amber at the horizon; a little brighter toward the sun.
    sky = [c(h) for h in ["343a4c", "414559", "565463", "75636a", "93736a", "ad8a6c", "c3a072"]]
    dsun = np.hypot((XX - sun[0]) / 1.4, YY - sun[1])
    img[:] = bands(YY / 172 * .9 + np.clip(1 - dsun / 260, 0, 1) * .25, sky)
    # Clouds: flat-bottomed puffs (unions of ellipses), shadowed above, lit along their undersides by the low sun.
    for k in range(9):
        cx = rng.uniform(20, W - 20); cy = rng.uniform(30, 120); w = rng.uniform(50, 120); h = rng.uniform(7, 14)
        m = np.zeros((H, W), bool)
        for j in range(5):
            ox = rng.uniform(-w * .55, w * .55); oy = rng.uniform(-h * .6, 0); rw = w * rng.uniform(.25, .45); rh = h * rng.uniform(.6, 1)
            m |= ((XX - cx - ox) / rw) ** 2 + ((YY - cy - oy) / rh) ** 2 < 1
        m &= YY < cy + h * .35
        t = np.clip(cy / 172 * .9, 0, 1)
        img[m] = bands(np.full(m.sum(), t * .7), sky, m)
        under = m & ~np.roll(m, -1, 0)
        lip = under | (np.roll(under, -1, 0) & m)
        img[lip] = bands(np.full(lip.sum(), min(1, t + .35)), sky, lip)
    # The sun, half set behind the far hills: two flat discs.
    img[(dsun < 20)] = c("d6b47a"); img[(dsun < 13)] = c("e3c68c")
    # Far mountains, blue-grey, lit on the faces toward the sun.
    far = 142 + noise1(170, 21) * 26 + noise1(40, 22, 2) * 5
    m = YY >= far[None, :]
    # Shaded in drifts from the ridge down (no straight columns): lit high and toward the sun, darker below.
    t = .62 + vnoise(26, 24, 3) * .35 - (YY - far[None, :]) / 70 - np.abs(XX - sun[0]) / 900
    img[m] = bands(t[m], [c("454a5d"), c("525669"), c("625f6f"), c("766c74")], m)
    outline(img, m, c("7a7178"), c("434859"))
    # Nearer hills, green-grey.
    near = 172 + noise1(110, 23) * 12
    m = YY >= near[None, :]
    t = .55 + vnoise(20, 25, 3) * .35 - (YY - near[None, :]) / 40
    img[m] = bands(t[m], [c("37423d"), c("434f46"), c("51604f")], m)
    outline(img, m, c("5d6a55"), c("323b37"))
    # Sea: a curved shore, darker toward us, the sun's light laid on it in short dithered dashes below the sun.
    shore = 470 + noise1(60, 30, 3)[None, :].repeat(H, 0)[:, :1] * 0 + (YY - 180) * .55 + vnoise(70, 31, 2)[:, :1] * 18
    sea = (YY >= 185) & (XX < shore)
    # The sky's colours reflected near the horizon, deepening toward us.
    img[sea] = bands((YY[sea] - 185) / 80 + vnoise(30, 33, 2)[sea] * .08, [c("7d6c6a"), c("5e5a64"), c("4a4d5c"), c("3c4150"), c("323745")], sea)
    glint = sea & (np.abs(XX - sun[0]) < 18 + (YY - 185) * .25) & ((YY + (XX // 7)) % 3 == 0) & (vnoise(7, 32, 2) > .05)
    img[glint] = c("c2a378")
    edge = sea & ~np.roll(sea, 1, 1) & (XX > 0)   # the shoreline, foam
    img[np.roll(sea, -1, 1) & ~sea & (YY >= 185) & (XX > 200)] = c("8c8a84")
    # The cliff, right: rock in strata shaded by noise, lit top edge.
    cliff_top = 160 + noise1(60, 40, 3) * 7
    cliff = (XX >= shore) & (YY >= cliff_top[None, :] + np.clip(540 - XX, 0, 80) * .55) & (YY >= 150)
    rock = vnoise(18, 41, 3) + (YY % 11 < 2) * -.35
    img[cliff] = bands((rock[cliff] + 1) / 2, [c("2b2b2d"), c("38363a"), c("45423f"), c("524d47")], cliff)
    outline(img, cliff, c("6b6457"), c("26262a"))
    # The keep: a tall tower, a wall, a squat tower, crenellations, lit windows; its sunward side lighter.
    st, sl, dk, win = c("4b4846"), c("5d5853"), c("2a292d"), c("e0b066")
    def rect(x0, y0, x1, y1, col): img[y0:y1, x0:x1] = col
    gx = 585
    rect(gx - 20, 92, gx + 20, 168, st); rect(gx - 20, 92, gx - 6, 168, sl)
    for x in range(gx - 20, gx + 20, 7): rect(x, 86, x + 4, 92, sl if x < gx - 6 else st)
    rect(gx - 24, 92, gx + 24, 94, dk)
    rect(gx + 20, 128, gx + 62, 170, st); rect(gx + 20, 126, gx + 62, 128, dk)
    for x in range(gx + 21, gx + 62, 6): rect(x, 122, x + 3, 126, st)
    rect(gx - 52, 114, gx - 20, 168, sl); rect(gx - 52, 112, gx - 20, 114, dk)
    for x in range(gx - 52, gx - 20, 6): rect(x, 107, x + 3, 112, sl)
    rect(gx + 62, 140, gx + 64, 170, dk); rect(gx - 6, 94, gx - 5, 168, dk)
    for (wx, wy) in [(gx - 2, 106), (gx + 8, 124), (gx - 40, 128), (gx + 40, 142), (gx - 12, 146)]: rect(wx, wy, wx + 3, wy + 5, win)
    rect(gx + 2, 156, gx + 9, 168, dk)                          # the gate
    # Foreground: a dark grassy rise, tufts lit along its top.
    fg_top = 268 + noise1(120, 50, 3) * 16 + np.clip((np.arange(W) - 470) * .04, 0, 9)
    fg = YY >= fg_top[None, :]
    img[fg] = bands((YY[fg] - fg_top[None, :].repeat(H, 0)[fg]) / 30, [c("394232"), c("2e362a"), c("252b22")], fg)
    tuft = fg & (YY < fg_top[None, :] + 4) & (rng.random((H, W)) < .25)
    img[tuft] = c("4b543e")
    outline(img, fg, c("58614a"), c("20251e"))
    # Gulls over the water.
    for bx, by in [(276, 96), (292, 89), (305, 99)]:
        img[by, bx - 1:bx + 2] = c("2b2e38"); img[by - 1, bx - 2] = img[by - 1, bx + 2] = c("2b2e38")
    # Texel-to-texel variation, then muted a little toward grey.
    img *= 1 + rng.normal(0, .03, (H, W, 1))
    g = img.mean(-1, keepdims=True); img = g + (img - g) * .88
    Image.fromarray(np.clip(img + .5, 0, 255).astype(np.uint8)).save(out_path)
    print("painting", W, H)


if __name__ == "__main__":
    import os
    draw(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Game", "World", "Art", "Bedroom", "Painting.png"))

"""Paints the 64x64 pixel-art atlas for the new Green Slime.

Layout (v = 0 at the bottom row of the image as Blender/Unity read UVs):
  rows 0..47  (v 0 .. .75)  body, u = angle around the slime (u .25 = front), v = height
  rows 48..63 (v .75 .. 1)  parts: eye tile at u 0..16px, mouth tile at u 16..32px
"""
import numpy as np, zlib, struct, sys

OUT = sys.argv[1]
W = H = 64
rng = np.random.default_rng(11)

# Palette, dark to light (sRGB 0-255): a mossy, slightly yellow slime green.
PAL = np.array([
    (14, 30, 12),     # 0 outline / deepest shade
    (28, 62, 20),     # 1 base shade
    (48, 104, 30),    # 2 mid dark
    (78, 148, 40),    # 3 mid
    (118, 190, 56),   # 4 light
    (170, 226, 96),   # 5 top light
    (226, 250, 170),  # 6 highlight
    (250, 255, 236),  # 7 glint
    (8, 10, 8),       # 8 eye black
    (40, 14, 16),     # 9 mouth dark red
], dtype=np.float32)

img = np.zeros((H, W), dtype=np.int32)  # palette indices, row 0 = TOP of the image

def row_for_v(v):  # v in 0..1 (0 = bottom) -> image row
    return int(round((1 - v) * (H - 1)))

# ── Body: rows 16..63 of the image (v 0 .. .75) ──────────────────────────────
body_top, body_bottom = 16, 63
for y in range(body_top, body_bottom + 1):
    h = (body_bottom - y) / (body_bottom - body_top)        # 0 bottom .. 1 top of the body band
    for x in range(W):
        u = x / W
        front = np.cos((u - .25) * 2 * np.pi)                # 1 at the front, -1 at the back
        t = .15 + .75 * h + .12 * front                       # brighter towards top and front
        t += (rng.random() - .5) * .14                        # painterly grain
        idx = 1 + int(np.clip(t, 0, .999) * 5)                # 1..5
        if h < .07: idx = 0 if (x + y) % 3 else 1             # dark wet rim at the very bottom
        elif h < .16: idx = min(idx, 1 if (x // 2 + y) % 2 else 2)  # shaded base band
        img[y, x] = idx

# Bubbles: light pixel with a darker lower lip, more of them in the middle band.
for _ in range(26):
    x = int(rng.integers(0, W)); y = int(rng.integers(body_top + 8, body_bottom - 10))
    r = int(rng.integers(1, 3))
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy <= r * r:
                yy, xx = y + dy, (x + dx) % W
                img[yy, xx] = min(6, img[yy, xx] + 1)
    img[min(body_bottom, y + r), x % W] = max(1, img[y, x % W] - 2)
    img[y - r, (x - 1) % W] = 6

# A swallowed bone floating inside, seen through the jelly on the left side.
bx, by = int(.62 * W), body_top + 26
for dx in range(-4, 5):
    img[by, (bx + dx) % W] = 2
for dy in (-1, 1):
    for dx in (-5, 5):
        img[by + dy, (bx + dx) % W] = 2

# Painted highlight on the upper front (u around .25, near the top).
hx, hy = int(.20 * W), body_top + 5
for dy in range(0, 3):
    for dx in range(0, 7 - dy * 2):
        img[hy + dy, (hx + dx + dy) % W] = 6
img[hy, (hx + 1) % W] = 7
img[hy, (hx + 2) % W] = 7

# ── Parts: rows 0..15 ───────────────────────────────────────────────────────
img[0:16, :] = 1
# Eye tile (x 0..15): black with a two-pixel glint in the upper left.
img[0:16, 0:16] = 8
img[3:6, 4:7] = 7
img[6, 9] = 7
# Mouth tile (x 16..31): dark red with a black inner edge.
img[0:16, 16:32] = 9
img[0:2, 16:32] = 8
img[14:16, 16:32] = 8

rgb = PAL[img].astype(np.uint8)


def write_png(path, rgb):
    h, w, _ = rgb.shape
    raw = b"".join(b"\x00" + rgb[y].tobytes() for y in range(h))
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xFFFFFFFF)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    open(path, "wb").write(data)


write_png(OUT, rgb)
# Preview 8x for review
write_png(OUT.replace(".png", " preview.png"), np.kron(rgb, np.ones((8, 8, 1), dtype=np.uint8)))
print("wrote", OUT)

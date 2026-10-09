"""Card art for the floating class card, in the intro's three styles (IntroController.style):
Warm (parchment, brown ink, red ribbon), Cold (slate card, bone-white ink, teal ribbon) and Dusk
(grey parchment, slate ink, plum ribbon). Card stock, inked frame, ribbon banner, insets (with hover and pressed versions), divider.

Drawn at half the card's UI size (one texel = two UI units) so it sits with the pixel UI kit.
Run: python -I Tools/paper_card.py Assets/Game/UI/Sprites/Paper Warm|Cold|Dusk
"""
import sys, os, math, random
from PIL import Image, ImageDraw, ImageFilter
import numpy as np

random.seed(4)
rng = np.random.default_rng(4)
STYLE = sys.argv[2] if len(sys.argv) > 2 else "Cold"
out = os.path.join(sys.argv[1], STYLE)
os.makedirs(out, exist_ok=True)

# Per style: card stock, ink, edge browning (r, g, b), stain, fold shadow and light, ribbon (face, tail, fold), inset wash.
STYLES = {
    "Warm": dict(paper=(226, 208, 168), ink=(62, 40, 24), edge=(.05, .12, .25), stain=(140, 100, 55, 15), fold=((120, 90, 55), (255, 245, 220)),
                 ribbon=((150, 46, 34), (96, 28, 22), (74, 20, 16)), wash=(120, 90, 50, 26)),
    "Cold": dict(paper=(52, 62, 82), ink=(214, 222, 228), edge=(.25, .18, .05), stain=(20, 26, 40, 30), fold=((14, 18, 28), (150, 170, 190)),
                 ribbon=((38, 128, 132), (24, 84, 90), (14, 54, 60)), wash=(10, 14, 24, 60)),
    "Dusk": dict(paper=(186, 182, 170), ink=(42, 46, 60), edge=(.12, .14, .12), stain=(80, 78, 90, 18), fold=((70, 70, 80), (230, 228, 220)),
                 ribbon=((104, 58, 88), (70, 38, 60), (50, 26, 44)), wash=(40, 42, 56, 34)),
}
P = STYLES[STYLE]
PAPER = np.array(P["paper"], float)
INK = P["ink"]


def noise(w, h, scale, octaves=3):
    total = np.zeros((h, w))
    amp = 1.0
    for o in range(octaves):
        s = max(1, int(scale / (2 ** o)))
        small = rng.random((h // s + 2, w // s + 2))
        img = Image.fromarray((small * 255).astype(np.uint8)).resize((w + s, h + s), Image.BILINEAR)
        total += np.asarray(img, float)[:h, :w] / 255 * amp
        amp *= .5
    return total / total.max()


def deckle_mask(w, h, depth=2.5, corner=5):
    """Alpha mask with a slightly torn, uneven edge and softened corners."""
    m = np.ones((h, w))
    edge = noise(max(w, h), 4, 6, 2)[0]
    for x in range(w):
        d = int(round(edge[x % len(edge)] * depth))
        m[:d, x] = 0
        d2 = int(round(edge[(x * 7 + 13) % len(edge)] * depth))
        m[h - d2:, x] = 0
    for y in range(h):
        d = int(round(edge[(y * 3 + 5) % len(edge)] * depth))
        m[y, :d] = 0
        d2 = int(round(edge[(y * 5 + 29) % len(edge)] * depth))
        m[y, w - d2:] = 0
    for cy, cx in ((0, 0), (0, w - 1), (h - 1, 0), (h - 1, w - 1)):
        for y in range(corner):
            for x in range(corner):
                yy = cy + (y if cy == 0 else -y); xx = cx + (x if cx == 0 else -x)
                if math.hypot(corner - x, corner - y) > corner + .5: m[yy, xx] = 0
    return m


def paper(w, h, edge_dark=.28):
    blot = noise(w, h, 40)
    grain = rng.normal(0, 1, (h, w))
    fibres = noise(w, h, 3, 2)
    yy, xx = np.mgrid[0:h, 0:w]
    dx = np.minimum(xx, w - 1 - xx) / (w * .5); dy = np.minimum(yy, h - 1 - yy) / (h * .5)
    vign = np.clip(np.minimum(dx, dy) * 3.2, 0, 1)
    shade = .9 + .12 * blot + .025 * grain + .04 * (fibres - .5)
    shade *= (1 - edge_dark) + edge_dark * vign ** .6
    rgb = PAPER[None, None, :] * shade[..., None]
    # warm the edges toward brown, like old card stock
    rgb = rgb * (1 - (1 - vign[..., None]) * np.array(P["edge"]))
    return np.clip(rgb, 0, 255)


def wobble_line(draw, pts, width=1, fill=INK, amp=.6):
    jittered = [(x + random.uniform(-amp, amp), y + random.uniform(-amp, amp)) for x, y in pts]
    draw.line(jittered, fill=fill, width=width)


def ink_rect(draw, x0, y0, x1, y1, step=6, width=1, fill=INK, amp=.45):
    def edge(a, b):
        n = max(2, int(math.dist(a, b) / step))
        return [(a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n) for i in range(n + 1)]
    for a, b in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
        wobble_line(draw, edge(a, b), width, fill, amp)


def save(img, name):
    img.save(os.path.join(out, name))
    print(name, img.size)


# ── Card ─────────────────────────────────────────────────────────────
W, H = 274, 430
rgb = paper(W, H)
img = Image.fromarray(rgb.astype(np.uint8)).convert("RGBA")
d = ImageDraw.Draw(img)
ink = INK + (235,)
faint = INK + (120,)
ink_rect(d, 8, 8, W - 9, H - 9, width=1, fill=ink)
ink_rect(d, 11, 11, W - 12, H - 12, width=1, fill=faint)
# small corner diamonds
for cx, cy in ((8, 8), (W - 9, 8), (8, H - 9), (W - 9, H - 9)):
    d.polygon([(cx, cy - 3), (cx + 3, cy), (cx, cy + 3), (cx - 3, cy)], fill=ink)
# a couple of foxing spots and a faint fold crease
stains = Image.new("RGBA", (W, H), (0, 0, 0, 0)); sd = ImageDraw.Draw(stains)
for _ in range(4):
    x, y, r = random.randint(24, W - 24), random.randint(24, H - 24), random.uniform(4, 9)
    sd.ellipse([x - r, y - r, x + r, y + r], fill=P["stain"][:3] + (random.randint(P["stain"][3] // 2, P["stain"][3]),))
img = Image.alpha_composite(img, stains.filter(ImageFilter.GaussianBlur(3))); d = ImageDraw.Draw(img)
crease = int(H * .52)
for x in range(14, W - 14):
    a = int(7 + 3 * math.sin(x * .05))
    d.point((x, crease), fill=P["fold"][0] + (a,)); d.point((x, crease + 1), fill=P["fold"][1] + (a // 6,))
alpha = (deckle_mask(W, H) * 255).astype(np.uint8)
img.putalpha(Image.fromarray(alpha))
save(img, "Paper card.png")

# ── Ribbon banner (behind the class name) ────────────────────────────
BW, BH = 232, 34
ban = Image.new("RGBA", (BW, BH), (0, 0, 0, 0))
bd = ImageDraw.Draw(ban)
red, dark, fold = (c + (255,) for c in P["ribbon"])
body = [(16, 4), (BW - 16, 4), (BW - 16, BH - 6), (16, BH - 6)]
tail_l = [(2, 9), (16, 9), (16, BH - 2), (2, BH - 2), (8, (9 + BH - 2) // 2)]
tail_r = [(BW - 3, 9), (BW - 17, 9), (BW - 17, BH - 2), (BW - 3, BH - 2), (BW - 9, (9 + BH - 2) // 2)]
bd.polygon(tail_l, fill=dark); bd.polygon(tail_r, fill=dark)
bd.polygon([(16, BH - 6), (16, BH - 2), (21, BH - 6)], fill=fold)
bd.polygon([(BW - 17, BH - 6), (BW - 17, BH - 2), (BW - 22, BH - 6)], fill=fold)
bd.polygon(body, fill=red)
arr = np.asarray(ban).astype(float)
wash = noise(BW, BH, 8)[..., None]
mask = arr[..., 3:4] > 0
arr[..., :3] = np.where(mask, np.clip(arr[..., :3] * (.85 + .3 * wash), 0, 255), arr[..., :3])
ban = Image.fromarray(arr.astype(np.uint8))
bd = ImageDraw.Draw(ban)
ink_rect(bd, 16, 4, BW - 17, BH - 7, step=5, fill=INK + (230,), amp=.35)
wobble_line(bd, tail_l + [tail_l[0]], fill=INK + (200,), amp=.3)
wobble_line(bd, tail_r + [tail_r[0]], fill=INK + (200,), amp=.3)
save(ban, "Paper banner.png")

# ── Ink inset (9-sliced: portrait frame, kit slots, buttons) ─────────
S = 24
ins = Image.new("RGBA", (S, S), (0, 0, 0, 0))
idr = ImageDraw.Draw(ins)
idr.rectangle([2, 2, S - 3, S - 3], fill=P["wash"])
ink_rect(idr, 1, 1, S - 2, S - 2, step=4, fill=INK + (220,), amp=.25)
ink_rect(idr, 3, 3, S - 4, S - 4, step=4, fill=INK + (90,), amp=.25)
save(ins, "Paper inset.png")

# Hover and pressed: the same inset with the wash deepened, so a paper button darkens like ink soaking in.
state = random.getstate()  # the sprites after these come out as they always have
for name, strength in (("Paper inset hover.png", 2.6), ("Paper inset pressed.png", 4.2)):
    var = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    vdr = ImageDraw.Draw(var)
    w = P["wash"]
    vdr.rectangle([2, 2, S - 3, S - 3], fill=w[:3] + (min(255, int(w[3] * strength)),))
    random.seed(4)
    ink_rect(vdr, 1, 1, S - 2, S - 2, step=4, fill=INK + (235,), amp=.25)
    ink_rect(vdr, 3, 3, S - 4, S - 4, step=4, fill=INK + (120,), amp=.25)
    save(var, name)
random.setstate(state)

# ── Divider ──────────────────────────────────────────────────────────
DW, DH = 210, 11
div = Image.new("RGBA", (DW, DH), (0, 0, 0, 0))
dd = ImageDraw.Draw(div)
mid = DH // 2
wobble_line(dd, [(4 + i * 6, mid) for i in range((DW - 8) // 6 + 1)], fill=INK + (200,), amp=.3)
dd.polygon([(DW // 2, mid - 4), (DW // 2 + 4, mid), (DW // 2, mid + 4), (DW // 2 - 4, mid)], fill=INK + (235,))
for s in (-1, 1):
    x = DW // 2 + s * 12
    dd.ellipse([x - 1.5, mid - 1.5, x + 1.5, mid + 1.5], fill=INK + (220,))
save(div, "Paper divider.png")

# Church3: the Church2 paintings recoloured into the dungeon's own palette (Dungeon1 Styles/Materials: crypt
# stone, slate, flagstone, vault, timber), so the room keeps Church2's blocks, cracks and layout but stops clashing.
# Each texel's brightness (normalised per texture) goes through a ramp for its material; a little of the
# original colour, desaturated, stays on top so blocks keep their texel-to-texel variation.
#   python Tools/church3_recolour.py
import os
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Game', 'Levels', 'Dungeon1', 'Styles', 'HighRes Materials')
SRC, DST = os.path.join(ROOT, 'Church2'), os.path.join(ROOT, 'Church3')

# Ramps, dark to light (sampled from the dungeon textures, stretched a little for contrast).
CRYPT = [(26, 29, 31), (46, 52, 53), (70, 78, 78), (88, 96, 95), (108, 114, 110), (128, 132, 124)]
PLASTER = [(38, 42, 44), (60, 66, 67), (82, 88, 87), (98, 103, 100), (114, 118, 112)]
FLAG = [(24, 27, 31), (44, 49, 55), (64, 71, 79), (78, 86, 94), (94, 101, 108)]
VAULT = [(22, 25, 28), (42, 48, 53), (64, 71, 77), (82, 90, 95), (100, 107, 110)]
TIMBER = [(18, 15, 14), (34, 29, 26), (52, 44, 38), (70, 58, 48), (88, 72, 58)]

def ramp(colours, t):
    t = min(1.0, max(0.0, t)) * (len(colours) - 1)
    i = min(int(t), len(colours) - 2); f = t - i
    a, b = colours[i], colours[i + 1]
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(3))

def lum(c): return .3 * c[0] + .59 * c[1] + .11 * c[2]

# Brightness range of a texture (2nd to 98th percentile): variants use their base's, so they keep its tones.
def bounds(src):
    im = Image.open(src).convert('RGBA'); px = im.load(); w, h = im.size
    ls = sorted(lum(px[x, y]) for y in range(h) for x in range(w) if px[x, y][3] > 0)
    return ls[int(len(ls) * .02)], ls[int(len(ls) * .5)], ls[int(len(ls) * .98)]

# contrast: share of the ramp the base's range covers, around its middle (plaster is nearly flat: stretching its
# faint noise to the full ramp paints blotches that show when it repeats).
def recolour(src, dst, palette, rng, contrast=1.0, keep=.12, warm=None, brick=None, despeck=False):
    im = Image.open(src).convert('RGBA'); px = im.load(); w, h = im.size
    lo, mid, hi = rng
    out = Image.new('RGBA', (w, h)); po = out.load()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0: po[x, y] = (0, 0, 0, 0); continue
            l = lum((r, g, b))
            t = .5 + ((l - mid) / max(1.0, hi - lo)) * contrast if contrast < 1 else (l - lo) / max(1.0, hi - lo)
            c = ramp(palette, t)
            # A trace of the original hue, desaturated to the ramp's brightness: block-to-block variation stays.
            grey = l
            orig = tuple(grey + (v - grey) * .35 for v in (r, g, b))
            scale = lum(c) / max(1.0, lum(orig))
            orig = tuple(v * scale for v in orig)
            c = tuple(c[k] * (1 - keep) + orig[k] * keep for k in range(3))
            # Brick showing through flaked plaster keeps the wall's stone colours.
            if brick and max(r, g, b) - min(r, g, b) > 40:
                c = ramp(CRYPT, (l - brick[0]) / max(1.0, brick[2] - brick[0]))
            # The floor's red tiles stay apart from the slate ones as a faint warm grey.
            if warm and r > g * 1.25 and r > b * 1.25:
                c = tuple(c[k] * (1 - warm[1]) + warm[0][k] * lum(c) / lum(warm[0]) * warm[1] for k in range(3))
            po[x, y] = tuple(max(0, min(255, round(v))) for v in c) + (a,)
    if despeck: Despeck(out)
    out.save(dst)

# Plaster: the base has small dark spots (nail holes, 4x4 texels) every 128 texels, a grid once the texture repeats.
# They are found on the base (texels much darker than the median around them) and the same texels are filled
# from the clean plaster beside them on every plaster texture, so the variants' soot and flake stay.
import statistics
_speck_mask = None
def SpeckMask(base_path):
    im = Image.open(base_path).convert('RGB'); px = im.load(); w, h = im.size
    mask = set()
    for y in range(h):
        for x in range(w):
            m = statistics.median(lum(px[(x + dx) % w, (y + dy) % h]) for dx in range(-4, 5) for dy in range(-4, 5))
            if lum(px[x, y]) < m - 12: mask.add((x, y))
    return mask

def Despeck(im):
    px = im.load(); w, h = im.size
    for (x, y) in _speck_mask:
        if x >= w or y >= h: continue
        for r in range(3, 12):
            ring = [px[(x + dx) % w, (y + dy) % h] for dx in (-r, r) for dy in range(-r, r + 1, 2)
                    if ((x + dx) % w, (y + dy) % h) not in _speck_mask]
            if ring: px[x, y] = ring[(x * 7 + y * 3) % len(ring)]; break

# name: (ramp, the base texture whose brightness range it uses, contrast)
PLAN = {
    'wall': (CRYPT, 'wall', 1.0), 'wall cracked': (CRYPT, 'wall', 1.0), 'wall cross': (CRYPT, 'wall', 1.0),
    'upper wall': (PLASTER, 'upper wall', .22), 'upper wall flaking': (PLASTER, 'upper wall', .22), 'upper wall soot': (PLASTER, 'upper wall', .22),
    'floor': (FLAG, 'floor', 1.0), 'floor broken': (FLAG, 'floor', 1.0), 'floor grave': (FLAG, 'floor', 1.0),
    'trim': (VAULT, 'trim', 1.0), 'ceiling': (TIMBER, 'ceiling', 1.0),
}

if __name__ == '__main__':
    os.makedirs(DST, exist_ok=True)
    _speck_mask = SpeckMask(os.path.join(SRC, 'Church2 upper wall.png'))
    for name, (palette, base, contrast) in PLAN.items():
        src = os.path.join(SRC, f'Church2 {name}.png')
        if not os.path.exists(src): print('missing', src); continue
        warm = ((96, 84, 72), .45) if name.startswith('floor') else None
        rng = bounds(os.path.join(SRC, f'Church2 {base}.png'))
        brick = bounds(os.path.join(SRC, 'Church2 wall.png')) if name == 'upper wall flaking' else None
        recolour(src, os.path.join(DST, f'Church3 {name}.png'), palette, rng, contrast, warm=warm, brick=brick, despeck=name.startswith('upper wall'))
        print('Church3', name)

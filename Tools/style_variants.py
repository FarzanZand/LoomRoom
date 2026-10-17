"""Rare variants for the hand-painted room styles (Cave1, Cave2, Nature1, Nature2, Keep, Rootbound, Ash): each is its base tile with one thing added,
never a different painting, so it sits in a wall or floor of base tiles without reading as a patch.

  feature  a thing cut out of the style's feature sheet (a Meshy painting: mushrooms, crystals, ivy, fungi...). Its
           texels are the ones in the box that differ from the sheet's own background (the median colour round the
           box's edge) by more than the threshold, the largest pieces only, so the rock or bark it grew on stays behind.
           Pasted texel for texel (same pixel size) onto the base at a place given in the tile's texels.
  damp     an irregular darker patch (a wet or shaded stain), dithered at its edge.

    python Tools/style_variants.py [style ...]      writes Temp/Styles/tiles/<Style>_<variant>.png
"""
import sys
import numpy as np
from PIL import Image

T = 'Temp/Styles/tiles/'


def grid(name):
    return np.asarray(Image.open(T + name + '_grid.png').convert('RGB')).astype(np.float32)


def cut(sheet, box, threshold, keep=1, by='colour', fill=True):
    """The feature in box: texels unlike the background round the box's edge, its largest connected pieces."""
    x0, y0, x1, y1 = box
    a = sheet[y0:y1, x0:x1]
    edge = np.concatenate([a[0], a[-1], a[:, 0], a[:, -1]])
    bg = np.median(edge, axis=0)
    if by == 'bright':      # lighter than the background (pale roots, crystals, fungi, glowing caps): its shadow stays behind
        m = (a @ np.array([.3, .59, .11], np.float32)) - bg @ np.array([.3, .59, .11], np.float32) > threshold
    elif by == 'green':     # leaves and vines: greener than red and blue (roots and stone stay behind)
        m = (a[..., 1] - np.maximum(a[..., 0], a[..., 2])) > threshold
    elif by == 'warm':      # roots and wood: redder than blue (grey stone and green leaves stay behind)
        m = (a[..., 0] - a[..., 2]) > threshold
    elif by == 'white':     # bone: near white and pale in colour (threshold is the least brightness)
        lum = a @ np.array([.3, .59, .11], np.float32)
        m = (lum > threshold) & ((a.max(axis=2) - a.min(axis=2)) < 45)
    elif by == 'blue':      # water: bluer than the background
        m = (a[..., 2] - a[..., 0]) - (bg[2] - bg[0]) > threshold
    else:
        m = np.linalg.norm(a - bg, axis=2) > threshold
    # Largest pieces (4-connected), so stray texels of the background don't come along.
    label = np.zeros(m.shape, int); n = 0; sizes = {}
    for y in range(m.shape[0]):
        for x in range(m.shape[1]):
            if m[y, x] and not label[y, x]:
                n += 1; stack = [(y, x)]; label[y, x] = n; c = 0
                while stack:
                    cy, cx = stack.pop(); c += 1
                    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                        ny, nx = cy + dy, cx + dx
                        if 0 <= ny < m.shape[0] and 0 <= nx < m.shape[1] and m[ny, nx] and not label[ny, nx]:
                            label[ny, nx] = n; stack.append((ny, nx))
                sizes[n] = c
    best = sorted(sizes, key=lambda k: -sizes[k])[:keep]
    mask = np.isin(label, best)
    # Its painted outline: dark texels touching it come along, so it keeps the edge the painter drew.
    lum = a @ np.array([.3, .59, .11], np.float32)
    dark = lum < bg @ np.array([.3, .59, .11], np.float32) - 18
    for _ in range(2):
        near = mask.copy()
        near[1:] |= mask[:-1]; near[:-1] |= mask[1:]; near[:, 1:] |= mask[:, :-1]; near[:, :-1] |= mask[:, 1:]
        mask = mask | (near & dark)
    # Holes inside the piece (a gap between leaves, a dark gill) are part of it.
    if not fill:            # strands and loose bones: the gaps between them show the base, not the sheet's wall
        return a, mask
    filled = mask.copy()
    for y in range(mask.shape[0]):
        xs = np.nonzero(mask[y])[0]
        if len(xs):
            row = np.zeros(mask.shape[1], bool); row[xs[0]:xs[-1] + 1] = True
            col_ok = np.array([mask[:y + 1, x].any() and mask[y:, x].any() for x in range(mask.shape[1])])
            filled[y] |= row & col_ok
    return a, filled


def paste(tile, piece, mask, at):
    """Pasted with its top-left at (x, y), wrapping round the tile's edges (the tile repeats)."""
    h, w = tile.shape[:2]
    ys = (np.arange(piece.shape[0]) + at[1]) % h
    xs = (np.arange(piece.shape[1]) + at[0]) % w
    sub = tile[np.ix_(ys, xs)]
    sub[mask] = piece[mask]
    tile[np.ix_(ys, xs)] = sub


def damp(tile, centre, radius, strength, rng):
    """An irregular darker patch round centre, its edge dithered texel by texel."""
    h, w = tile.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    dy = (yy - centre[1] + h / 2) % h - h / 2
    dx = (xx - centre[0] + w / 2) % w - w / 2
    ang = np.arctan2(dy, dx)
    wobble = 1 + .25 * np.sin(3 * ang + rng.uniform(0, 6)) + .15 * np.sin(5 * ang + rng.uniform(0, 6))
    r = np.sqrt(dx ** 2 + dy ** 2) / (radius * wobble)
    inside = r < 1 - .25 * rng.random((h, w))
    tile[inside] *= strength


def save(tile, name):
    Image.fromarray(np.clip(np.round(tile), 0, 255).astype(np.uint8)).save(T + name + '.png')
    print('wrote', name)


def base(name):
    return np.asarray(Image.open(T + name + '.png').convert('RGB')).astype(np.float32).copy()


# One feature per wall variant (two side by side on neighbouring variant faces made a row). Boxes on each feature sheet's grid (grid mode, --margin 1), x0, y0, x1, y1; places in tile texels.
def cave1(rng):
    s = grid('Cave1_features')
    t = base('Cave1_wall')
    for box, at in (((37, 40, 78, 74), (100, 200)),):
        a, m = cut(s, box, 30, by='bright', keep=4); paste(t, a, m, at)
    save(t, 'Cave1_wall_mushrooms')
    t = base('Cave1_wall')
    a, m = cut(s, (93, 0, 118, 197), 18, by='blue')
    for k in range(2):   # the trickle runs the whole tile height: the strip twice, end to end
        paste(t, a, m, (110, k * 192))
    save(t, 'Cave1_wall_trickle')
    t = base('Cave1_floor')
    a, m = cut(s, (140, 67, 176, 101), 30, by='bright', keep=4); paste(t, a, m, (120, 100))
    save(t, 'Cave1_floor_mushrooms')
    t = base('Cave1_floor'); damp(t, (128, 128), 60, .84, rng); save(t, 'Cave1_floor_damp')


def cave2(rng):
    s = grid('Cave2_features')
    t = base('Cave2_wall')
    for box, at in (((38, 53, 61, 76), (115, 230)),):
        a, m = cut(s, box, 28, by='bright', keep=5); paste(t, a, m, at)
    save(t, 'Cave2_wall_crystals')
    t = base('Cave2_wall')
    a, m = cut(s, (86, 12, 151, 144), 40, keep=4, by='bright'); paste(t, a, m, (95, 20))
    save(t, 'Cave2_wall_roots')
    t = base('Cave2_floor')
    a, m = cut(s, (41, 130, 69, 156), 28, by='bright', keep=5); paste(t, a, m, (115, 110))
    save(t, 'Cave2_floor_crystals')
    t = base('Cave2_floor'); damp(t, (128, 128), 60, .85, rng); save(t, 'Cave2_floor_damp')


def nature1(rng):
    s = grid('Nature1_features')
    t = base('Nature1_wall')
    for box, at in (((23, 16, 61, 82), (105, 0)),):
        a, m = cut(s, box, 40); paste(t, a, m, at)
    save(t, 'Nature1_wall_ivy')
    t = base('Nature1_wall')
    a, m = cut(s, (148, 80, 163, 94), 40); paste(t, a, m, (110, 320))
    a, m = cut(s, (77, 141, 91, 156), 40); paste(t, a, m, (132, 336))
    save(t, 'Nature1_wall_mushrooms')
    t = base('Nature1_floor')
    a, m = cut(s, (148, 80, 163, 94), 40); paste(t, a, m, (100, 110))
    a, m = cut(s, (77, 141, 91, 156), 40); paste(t, a, m, (125, 130))
    save(t, 'Nature1_floor_mushrooms')
    t = base('Nature1_floor'); damp(t, (128, 128), 60, .84, rng); save(t, 'Nature1_floor_damp')


def nature2(rng):
    s = grid('Nature2_features')
    t = base('Nature2_wall')
    for box, at in (((135, 43, 191, 86), (100, 190)),):
        a, m = cut(s, box, 30, by='bright'); paste(t, a, m, at)
    save(t, 'Nature2_wall_fungi')
    t = base('Nature2_wall')
    a, m = cut(s, (111, 90, 139, 126), 35); paste(t, a, m, (110, 160))
    save(t, 'Nature2_wall_glowmoss')
    t = base('Nature2_floor')
    a, m = cut(s, (111, 90, 139, 126), 35); paste(t, a, m, (110, 100))
    save(t, 'Nature2_floor_glowmoss')
    t = base('Nature2_floor'); damp(t, (128, 128), 60, .84, rng); save(t, 'Nature2_floor_damp')


def keep(rng):
    s = grid('Keep_features')
    t = base('Keep_wall')
    a, m = cut(s, (5, 0, 73, 166), 6, by='green', keep=4, fill=False); paste(t, a, m, (90, 0))   # vines hang from the top of the wall
    save(t, 'Keep_wall_vines')
    t = base('Keep_wall')
    a, m = cut(s, (127, 0, 193, 192), 6, by='green', keep=4, fill=False); paste(t, a, m, (40, 0))
    a, m = cut(s, (53, 43, 148, 181), 12, by='warm', keep=6, fill=False); paste(t, a, m, (140, 190))
    save(t, 'Keep_wall_root')
    t = base('Keep_floor')
    a, m = cut(s, (5, 100, 50, 166), 6, by='green', keep=3, fill=False); paste(t, a, m, (100, 90))   # a fallen trail of ivy
    save(t, 'Keep_floor_ivy')
    t = base('Keep_floor'); damp(t, (128, 128), 60, .84, rng); save(t, 'Keep_floor_damp')


def rootbound(rng):
    s = grid('Rootbound_features')
    t = base('Rootbound_wall')
    a, m = cut(s, (13, 25, 161, 191), 12, by='warm', keep=8, fill=False); paste(t, a, m, (55, 150))
    save(t, 'Rootbound_wall_rootmass')
    t = base('Rootbound_wall')
    for box, at in (((50, 0, 67, 94), (70, 0)), ((153, 0, 171, 161), (170, 0))):
        a, m = cut(s, box, 26, keep=2, fill=False); paste(t, a, m, at)
    save(t, 'Rootbound_wall_moss')
    t = base('Rootbound_floor')
    a, m = cut(s, (13, 120, 90, 191), 12, by='warm', keep=6, fill=False); paste(t, a, m, (90, 90))
    save(t, 'Rootbound_floor_roots')
    t = base('Rootbound_floor'); damp(t, (128, 128), 60, .84, rng); save(t, 'Rootbound_floor_damp')


def ash(rng):
    s = grid('Ash_features')
    t = base('Ash_wall')
    a, m = cut(s, (46, 57, 170, 111), 205, by='white', keep=14, fill=False); paste(t, a, m, (66, 250))
    save(t, 'Ash_wall_bones')
    t = base('Ash_wall'); damp(t, (128, 384), 120, .62, rng); save(t, 'Ash_wall_soot')   # soot climbing from the floor
    t = base('Ash_floor')
    a, m = cut(s, (100, 57, 170, 111), 205, by='white', keep=8, fill=False); paste(t, a, m, (95, 100))
    save(t, 'Ash_floor_bones')
    t = base('Ash_floor'); damp(t, (128, 128), 65, .7, rng); save(t, 'Ash_floor_soot')


if __name__ == '__main__':
    styles = {'Cave1': cave1, 'Cave2': cave2, 'Nature1': nature1, 'Nature2': nature2, 'Keep': keep, 'Rootbound': rootbound, 'Ash': ash}
    for name in (sys.argv[1:] or styles):
        styles[name](np.random.default_rng(len(name)))

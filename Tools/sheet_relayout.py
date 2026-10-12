"""Put a painted five-view prop sheet back into its reference layout.

The painter sometimes moves, reorders or adds views. This finds each view in the reference sheet (one per cell of the
3 x 2 grid) and each separate object in the painting, matches them by shape (aspect ratio, then size), and pastes each
painted view, scaled to its reference box, into the reference's place on a plain grey sheet. Extra painted views are
dropped.

  python Tools/sheet_relayout.py "<reference sheet.png>" "<painted.png>" "<out.png>"
"""
import itertools
import sys

from PIL import Image

BG_TOL = 18


def is_bg(px, bg):
    return all(abs(a - b) <= BG_TOL for a, b in zip(px[:3], bg[:3]))


def blobs(im, bg, min_area):
    """Bounding boxes of the separate non-background objects (8-connected, on a 1/4 grid for speed)."""
    w, h = im.size
    step = 4
    gw, gh = w // step, h // step
    px = im.load()
    solid = [[not is_bg(px[x * step, y * step], bg) for x in range(gw)] for y in range(gh)]
    seen = [[False] * gw for _ in range(gh)]
    boxes = []
    for y in range(gh):
        for x in range(gw):
            if not solid[y][x] or seen[y][x]:
                continue
            stack = [(x, y)]; seen[y][x] = True
            x0 = x1 = x; y0 = y1 = y; n = 0
            while stack:
                cx, cy = stack.pop(); n += 1
                x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nx, ny = cx + dx, cy + dy
                        if 0 <= nx < gw and 0 <= ny < gh and solid[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = True; stack.append((nx, ny))
            if n * step * step >= min_area:
                boxes.append((x0 * step, y0 * step, (x1 + 1) * step, (y1 + 1) * step))
    return boxes


def merge_close(boxes, gap):
    """Joins boxes that touch or nearly touch (a view drawn as several pieces, a chain's links)."""
    changed = True
    while changed:
        changed = False
        for i, j in itertools.combinations(range(len(boxes)), 2):
            a, b = boxes[i], boxes[j]
            if a[0] - gap <= b[2] and b[0] - gap <= a[2] and a[1] - gap <= b[3] and b[1] - gap <= a[3]:
                boxes[i] = (min(a[0], b[0]), min(a[1], b[1]), max(a[2], b[2]), max(a[3], b[3]))
                del boxes[j]; changed = True
                break
    return boxes


def cell_boxes(ref, bg):
    """One object box per cell of the reference's 3 x 2 grid (five views, the last cell empty)."""
    w, h = ref.size
    # Square cells, three across the middle of the sheet (PixelRestyle.SheetCellRect).
    cw = ch = h // 2
    left = (w - cw * 3) // 2
    out = []
    for v in range(5):
        cx, cy = left + (v % 3) * cw, (v // 3) * ch
        cell = ref.crop((cx, cy, cx + cw, cy + ch))
        bb = merge_close(blobs(cell, cell.getpixel((3, 3)), 40), 12)
        if not bb:
            out.append(None); continue
        x0 = min(b[0] for b in bb); y0 = min(b[1] for b in bb); x1 = max(b[2] for b in bb); y1 = max(b[3] for b in bb)
        out.append((cx + x0, cy + y0, cx + x1, cy + y1))
    return out


def main(ref_path, painted_path, out_path):
    ref = Image.open(ref_path).convert("RGB")
    painted = Image.open(painted_path).convert("RGB").resize(ref.size, Image.NEAREST)
    bg = ref.getpixel((2, 2)); pbg = painted.getpixel((2, 2))
    targets = [b for b in cell_boxes(ref, bg)]
    found = merge_close(blobs(painted, pbg, 600), 14)
    want = [i for i, t in enumerate(targets) if t]
    if len(found) < len(want):
        raise SystemExit(f"only {len(found)} painted views for {len(want)} reference views")

    def aspect(b):
        return (b[2] - b[0]) / max(1, b[3] - b[1])

    def cost(t, f):
        import math
        return abs(math.log(aspect(t) / aspect(f))) * 3 + abs(math.log(((t[2] - t[0]) * (t[3] - t[1])) / max(1, (f[2] - f[0]) * (f[3] - f[1])))) * .3

    best, pick = None, None
    for perm in itertools.permutations(range(len(found)), len(want)):
        c = sum(cost(targets[want[k]], found[perm[k]]) for k in range(len(want)))
        if best is None or c < best:
            best, pick = c, perm
    out = Image.new("RGB", ref.size, pbg)
    for k, v in enumerate(want):
        t, f = targets[v], found[pick[k]]
        view = painted.crop(f).resize((t[2] - t[0], t[3] - t[1]), Image.NEAREST)
        out.paste(view, (t[0], t[1]))
        print(f"view {v}: painted box {f} -> {t}")
    out.save(out_path)
    print("cost", round(best, 2), "->", out_path)


if __name__ == "__main__":
    main(*sys.argv[1:4])

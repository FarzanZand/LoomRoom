"""Spell tome icons: the rendered plain book (Tools/tome_plain.png, the tome's own model as the icon renderer drew it) tinted to a
cover colour, with the spell's mark drawn on it in 4 px blocks, a one-block dark outline and blotchy per-block colour,
the way the seven drawn tomes are (Frost Lance, Molten Boulder, Raise the Fallen ...).

The cover's shading is fitted from a drawn tome (Raise the Fallen) against the plain book's brightness, outside its mark,
and moved to the new colour, so a new cover shades as the drawn ones do.

    python Tools/tome_icons.py <out dir> [name ...]     (no names: all in MARKS)
"""
import os, sys
import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Game")
PLAIN = os.path.join(os.path.dirname(__file__), "tome_plain.png")
REFERENCE = os.path.join(ROOT, "Combat/Spells/Raise the Fallen/Raise the Fallen Tome icon.png")
BLOCK = 4
OUTLINE = (24, 18, 22)

# Marks on a grid of 4 px blocks; '.' empty, letters are colours from the mark's palette. Centred on the cover.
MARKS = {
    "Fireball": ((102, 42, 40), {"r": (196, 58, 30), "o": (238, 128, 36), "y": (252, 206, 84)}, [
        "......r.r....",
        ".....rr.rr...",
        "...r.rorrr.r.",
        "...rrroorrrr.",
        "..rrooooyorr.",
        "..rooyyyyoor.",
        ".rroyyyyyyor.",
        ".rooyyyyyyor.",
        ".rooyyyyyoor.",
        ".rrooyyyoorr.",
        "..rrooooorr..",
        "...rrrrrrr...",
    ]),
    "Heal": ((50, 84, 82), {"g": (98, 184, 86), "l": (166, 222, 128)}, [
        "....ggg....",
        "....glg....",
        "....glg....",
        "....glg....",
        "gggggllgggg",
        "glllllllllg",
        "ggggglggggg",
        "....glg....",
        "....glg....",
        "....glg....",
        "....ggg....",
    ]),
    "Flick": ((112, 94, 58), {"s": (226, 180, 140), "d": (186, 134, 100), "w": (240, 236, 220)}, [
        "......w..w...",
        ".......w.w.w.",
        ".....ww......",
        "..ssss...ww..",
        ".sssssss.....",
        ".ssssssssss..",
        ".ssssssdddd..",
        ".sssssd......",
        ".ssssd.......",
        "..sss........",
    ]),
    "Helping Hand": ((114, 80, 50), {"s": (232, 188, 146), "d": (190, 140, 104)}, [
        "...s.s.s....",
        "...s.s.s.s..",
        "...s.s.s.s..",
        "...s.s.s.s..",
        "s..sssssss..",
        "ss.sssssss..",
        ".sssssssss..",
        "..ssssssss..",
        "..sssssssd..",
        "...ssssssd..",
        "...ssssdd...",
    ]),
    "Die Throw": ((56, 54, 62), {"w": (236, 228, 204), "e": (196, 186, 160), "p": (30, 26, 30)}, [
        "wwwwwwwwwe",
        "wpwwwwwwpe",
        "wwwwwwwwwe",
        "wwwwwwwwwe",
        "wwwwpwwwwe",
        "wwwwwwwwwe",
        "wwwwwwwwwe",
        "wpwwwwwwpe",
        "wwwwwwwwwe",
        "eeeeeeeeee",
    ]),
    "Dice Portal": ((50, 54, 96), {"v": (124, 92, 214), "c": (110, 206, 230), "k": (44, 32, 76), "w": (236, 228, 204), "p": (30, 26, 30)}, [
        "...vvvvvvv...",
        "..vcccccccv..",
        ".vcckkkkkccv.",
        ".vckwwwwwkcv.",
        "vcckwpwwwkccv",
        "vckkwwpwwkkcv",
        "vcckwwwpwkccv",
        ".vckwwwwwkcv.",
        ".vcckkkkkccv.",
        "..vcccccccv..",
        "...vvvvvvv...",
    ]),
}


def lum(a):
    return a[..., :3] @ np.array([.299, .587, .114])


def fit_cover():
    """Per channel, the reference's cover colour as a line in the plain book's brightness, outside its mark; returned
    as the shading ratio (cover / cover's median) for every pixel of the plain book."""
    plain = np.asarray(Image.open(PLAIN).convert("RGBA")).astype(float)
    ref = np.asarray(Image.open(REFERENCE).convert("RGBA")).astype(float)
    L = lum(plain)
    solid = plain[..., 3] > 0
    mask = solid.copy(); mask[40:140, 50:150] = False          # leave the mark out
    rl = lum(ref)
    A = np.stack([L[mask], np.ones(mask.sum())], 1)
    k, c = np.linalg.lstsq(A, rl[mask], rcond=None)[0]
    shade = (k * L + c)
    shade = shade / np.median(shade[mask])
    return plain, solid, shade, ref, mask


def make(name, out_dir, plain, solid, shade, rng):
    cover, palette, rows = MARKS[name]
    img = np.zeros_like(plain)
    img[..., :3] = np.clip(np.array(cover)[None, None, :] * shade[..., None], 0, 255)
    img[..., 3] = plain[..., 3]
    # The mark, centred on the cover (the book's bounding box without its clasps on the left).
    ys, xs = np.nonzero(solid)
    cx = (xs.min() + 14 + xs.max()) / 2; cy = (ys.min() + ys.max()) / 2
    h, w = len(rows), max(len(r) for r in rows)
    grid = np.full((h + 2, w + 2), ".", dtype="<U1")
    for y, r in enumerate(rows):
        for x, ch in enumerate(r): grid[y + 1, x + 1] = ch
    x0 = int(round(cx - (w + 2) * BLOCK / 2)); y0 = int(round(cy - (h + 2) * BLOCK / 2))
    for y in range(h + 2):
        for x in range(w + 2):
            ch = grid[y, x]
            if ch == ".":
                near = any(grid[y + dy, x + dx] != "." for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0))
                           if 0 <= y + dy < h + 2 and 0 <= x + dx < w + 2)
                if not near: continue
                col = np.array(OUTLINE, float)
            else:
                col = np.array(palette[ch], float) * rng.uniform(.88, 1.06)   # blotchy, block by block
            img[y0 + y * BLOCK:y0 + (y + 1) * BLOCK, x0 + x * BLOCK:x0 + (x + 1) * BLOCK, :3] = np.clip(col, 0, 255)
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, f"{name} Tome icon.png")
    Image.fromarray(img.astype(np.uint8), "RGBA").save(path)
    return path


if __name__ == "__main__":
    out = sys.argv[1]
    names = sys.argv[2:] or list(MARKS)
    plain, solid, shade, ref, mask = fit_cover()
    # Check: the reference's own cover made again from its colour, against the drawn one.
    rc = np.median(ref[mask][:, :3], 0)
    again = np.clip(rc[None, None, :] * shade[..., None], 0, 255)
    err = np.abs(again[mask] - ref[mask][:, :3]).mean()
    print(f"reference cover made again: mean error {err:.1f} of 255")
    for n in names:
        print(make(n, out, plain, solid, shade, np.random.default_rng(sum(n.encode()))))

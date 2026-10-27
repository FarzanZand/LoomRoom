"""Checks where a room style's tiles meet. The generator picks the base or a variant per 2 m cell (each wall face of a
cell, each floor cell), and the material's tiling and offset decide which part of the texture lands on that cell. This
lays every pair of the style's wall tiles (base and variants) side by side along a wall, and every pair of floor tiles
along both axes, exactly as the generator maps them (UV = world metres / 3 m x tiling + offset, cell edges at odd
metres), measures the change across each cell edge against the same edge between two base tiles, and writes a sheet
of every pair to look at.

    python Tools/tile_seams.py <style.asset> [<style.asset> ...] [--out Temp/Seams] [--limit 1.35]

Prints one line per pair whose edge changes more than limit x the base's own (a hard line); 0 lines is the goal. Then
look at the sheets: a number can't tell a soft patch edge from a cut-off one.
"""
import argparse
import glob
import os
import re
from itertools import combinations_with_replacement

import numpy as np
from PIL import Image

TEXELS = 128  # per metre
TILE = 3.0  # architecture tile, metres
CELL = 2.0
_guids = None


def guid_paths():
    global _guids
    if _guids is None:
        _guids = {}
        for meta in glob.glob("Assets/Game/**/*.meta", recursive=True):
            with open(meta, encoding="utf-8", errors="ignore") as f:
                m = re.search(r"^guid: (\w+)", f.read(), re.M)
            if m:
                _guids[m.group(1)] = meta[:-5]
    return _guids


def ref(text, field):
    m = re.search(rf"^\s*{field}: \{{fileID: \d+, guid: (\w+)", text, re.M)
    return guid_paths().get(m.group(1)) if m else None


def variants(text, field):
    m = re.search(rf"^  {field}:\n((?:  [- ] .*\n|    .*\n)*)", text, re.M)
    if not m:
        return []
    out = []
    for g, w, r in re.findall(r"material: \{fileID: \d+, guid: (\w+)[^\n]*\n\s*weight: ([\d.]+)(?:\n\s*rows: (\d))?", m.group(1)):
        if g in guid_paths() and float(w) > 0:
            out.append((guid_paths()[g], float(w), int(r or 0)))
    return out


class Surface:
    def __init__(self, mat):
        self.name = os.path.basename(mat)[:-4]
        text = open(mat, encoding="utf-8").read()
        m = re.search(r"- _BaseMap:\n\s*m_Texture: \{fileID: \d+, guid: (\w+)[^\n]*\n\s*m_Scale: \{x: ([-\d.e]+), y: ([-\d.e]+)\}\n"
                      r"\s*m_Offset: \{x: ([-\d.e]+), y: ([-\d.e]+)\}", text)
        tex = guid_paths()[m.group(1)]
        self.img = np.asarray(Image.open(tex).convert("RGB")).astype(float)
        self.scale = (float(m.group(2)), float(m.group(3)))
        self.offset = (float(m.group(4)), float(m.group(5)))

    def sample(self, xs, ys):
        """Texels at world metres xs (along the surface) and ys (up the wall, or along the floor's other axis)."""
        h, w = self.img.shape[:2]
        u = (xs / TILE * self.scale[0] + self.offset[0]) % 1
        v = (ys / TILE * self.scale[1] + self.offset[1]) % 1
        cols = np.minimum((u * w).astype(int), w - 1)
        rows = np.minimum(((1 - v) * h).astype(int), h - 1)
        return self.img[rows[:, None], cols[None, :]]


def strip(a, b, cells=6, height=TILE, start=1.0):
    """Cells alternating a, b along x from world x = start (an odd metre, a cell edge), height metres tall."""
    n = int(cells * CELL * TEXELS)
    xs = start + (np.arange(n) + .5) / TEXELS
    ys = (np.arange(int(height * TEXELS))[::-1] + .5) / TEXELS + (1.0 if height == CELL else 0.0)
    out = np.zeros((len(ys), n, 3))
    per = int(CELL * TEXELS)
    for c in range(cells):
        s = (a, b)[c % 2]
        out[:, c * per:(c + 1) * per] = s.sample(xs[c * per:(c + 1) * per], ys)
    return out


def edge_change(img, per):
    """Mean colour change across each cell edge (between the last column of one cell and the first of the next)."""
    return np.array([np.abs(img[:, e] - img[:, e - 1]).mean() for e in range(per, img.shape[1], per)])


def check(style, out_dir, limit):
    text = open(style, encoding="utf-8").read()
    name = os.path.basename(style)[:-6]
    report = []
    # Rows: a variant marked BottomOnly (1) is laid only on a wall's lowest tile, UpperOnly (2) only above it.
    for kind, base_field, var_field, height, never in (("wall", "bottomWall", "wallVariants", TILE, 2), ("upper wall", "upperWall", "wallVariants", TILE, 1),
                                                       ("floor", "floor", "floorVariants", CELL, -1)):
        base = ref(text, base_field) or (ref(text, "bottomWall") if kind == "upper wall" else None)
        if base is None:
            continue
        surfaces = [Surface(base)] + [Surface(p) for p, _, r in variants(text, var_field) if r != never]
        if kind == "upper wall" and len(surfaces) == 1:
            continue
        per = int(CELL * TEXELS)
        rows = []
        for turned in ((False,) if kind != "floor" else (False, True)):
            # A floor's cells also meet along its other axis: the same strip with the texture's axes swapped.
            def lay(a, b):
                return strip(Turned(a), Turned(b), height=height) if turned else strip(a, b, height=height)
            # Each edge against the same edge (same place, same part of the texture) between two base tiles.
            same = edge_change(lay(surfaces[0], surfaces[0]), per) + 1.0
            for a, b in combinations_with_replacement(surfaces, 2):
                flat = lay(a, b)
                ratio = ((edge_change(flat, per) + 1.0) / same).max()
                rows.append(flat)
                if ratio > limit:
                    report.append(f"{name} {kind}{' (other axis)' if turned else ''}: {a.name} | {b.name} edge {ratio:.2f} x the base's")
        if rows:
            gap = np.full((6, rows[0].shape[1], 3), 255.0)
            sheet = np.concatenate([x for r in rows for x in (r, gap)], 0)
            os.makedirs(out_dir, exist_ok=True)
            Image.fromarray(sheet.clip(0, 255).astype("uint8")).save(f"{out_dir}/{name} {kind}.png")
    return report


class Turned:
    """A surface sampled with its axes swapped (a floor seen along its other axis)."""
    def __init__(self, s):
        self.s, self.name = s, s.name

    def sample(self, xs, ys):
        return np.transpose(self.s.sample(ys, xs), (1, 0, 2))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("styles", nargs="+")
    ap.add_argument("--out", default="Temp/Seams")
    ap.add_argument("--limit", type=float, default=1.35)
    args = ap.parse_args()
    lines = []
    for s in args.styles:
        lines += check(s, args.out, args.limit)
    print("\n".join(lines) if lines else "0 hard edges")
    print(f"sheets in {args.out}")


if __name__ == "__main__":
    main()

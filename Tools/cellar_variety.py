"""Gives the Cellar styles (Levels/Shared/Room styles/Cellar/Cellar1-6) colour from stone to stone, as
Tools/ruins_variety.py does for the ruins but more lightly, so the drawn style stays: every stone, brick and floor tile
found between the mortar lines gets a tint and value of its own (a little warmer, cooler, darker, lighter, now and then
dirtied), on every wall, floor and vault texture of the style and its variants (trims are left as they are). Works from
'<name> base.png' (kept the first time), so it can run again. Tiles stay seamless.
    python Tools/cellar_variety.py"""
import glob
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from ruins_variety import base_of, save, stones  # noqa: E402

ROOT = "Assets/Game/Levels/Shared/Room styles/Cellar"
PALETTE = [((1.0, 1.0, 1.0), 5), ((1.06, 1.0, .93), 2), ((.94, .97, 1.04), 2), ((.86, .86, .86), 1.6),
           ((1.1, 1.08, 1.04), 1.2), ((.95, 1.02, .9), .8), ((.78, .74, .7), .6)]


def tint(img, seed):
    lab, keys, _ = stones(img)
    tints, weights = zip(*PALETTE)
    weights = np.array(weights) / sum(weights)
    out = img.copy()
    for label in np.unique(lab):
        if label == 0:
            continue
        mask = lab == label
        if mask.sum() < 12:
            continue
        rng = np.random.default_rng(int(keys[label - 1]) * 13 + seed)
        out[mask] = img[mask] * np.array(tints[rng.choice(len(tints), p=weights)])
    return out


def main():
    for d in sorted(x for x in glob.glob(f"{ROOT}/Cellar*") if os.path.isdir(x)):
        n = int(os.path.basename(d)[-1])
        for p in sorted(glob.glob(f"{d}/*.png")):
            name = os.path.basename(p)
            if name.endswith(" base.png") or " trim" in name:
                continue
            save(tint(base_of(p), n), p)
            print("tinted", name)


if __name__ == "__main__":
    main()

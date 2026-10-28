"""Gives the Cellar styles (Levels/Shared/Room styles/Cellar/Cellar1-6) colour from stone to stone, as
Tools/ruins_variety.py does for the ruins but more lightly, so the drawn style stays: every stone, brick and floor tile
found between the mortar lines gets a tint and value of its own (a little warmer, cooler, darker, lighter, now and then
dirtied), on every wall, floor and vault texture of the style and its variants (trims are left as they are). Works from
'<name> base.png' (kept the first time), so it can run again. Tiles stay seamless. A variant (moss, cracked, soot, ...)
takes its base's colours texel for texel, and what it adds near the cell's edges gives way to the base, so tiles meet
without a line (check with Tools/tile_seams.py).
    python Tools/cellar_variety.py"""
import glob
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(__file__))
from ruins_variety import base_of, edge_fade, field, save, stones  # noqa: E402

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


# A variant's last word; the rest of its name is the surface it grows on (its family's base).
VARIANT_WORDS = {"cracked", "cross", "moss", "mould", "soot", "flaking", "grave", "broken"}


def family(name):
    parts = name.split(" ")
    return " ".join(parts[:-1]) if parts[-1] in VARIANT_WORDS else None


def on_base(var, base_raw, base_tinted, floor, seed):
    """A variant coloured stone for stone as its base (the base's tint per texel), and what it adds near the cell's
    edges given back to the base, raggedly: tinting each file alone gave the same stone two colours, and a mossy or
    cracked patch running to the edge was cut off there, a hard line against the plain tile beside it."""
    reps = (var.shape[0] // base_raw.shape[0], var.shape[1] // base_raw.shape[1], 1)
    raw, tinted = np.tile(base_raw, reps), np.tile(base_tinted, reps)
    out = var * (tinted / np.maximum(raw, 1))
    added = np.abs(var - raw).mean(-1) > 6
    h, w = var.shape[:2]
    keep = edge_fade(h, w, floor, 28) > .15 + .7 * field(h, w, 10, seed)
    give_back = added & ~keep
    out[give_back] = tinted[give_back]
    return out


def main():
    for d in sorted(x for x in glob.glob(f"{ROOT}/Cellar*") if os.path.isdir(x)):
        n = int(os.path.basename(d)[-1])
        names = sorted(os.path.basename(p)[:-4] for p in glob.glob(f"{d}/*.png"))
        names = [x for x in names if not x.endswith(" base") and " trim" not in x]
        done = {}
        # Bases first, then the variants on them.
        for name in sorted(names, key=lambda x: family(x) is not None):
            p = f"{d}/{name}.png"
            raw = base_of(p)
            fam = family(name)
            if fam in done:
                out = on_base(raw, *done[fam], "floor" in fam, n * 97 + len(name))
            else:
                out = tint(raw, n)
                done[name] = (raw, out)
            save(out, p)
            print("tinted", name, "on " + fam if fam in done else "")

if __name__ == "__main__":
    main()

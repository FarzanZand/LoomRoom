"""New dungeon room styles from the existing ones, by hand-style recolouring (no AI painting).

Each new style takes each of its surfaces (bottom wall, upper wall, floor, ceiling, trim) from one of the Church styles
and recolours it texel by texel with a gradient map: every texel keeps its brightness within the texture, so bricks,
mortar, cracks and dirt stay exactly where they were drawn, and only the colour changes. Texels that clearly are
something else (moss, mould, a red grave cloth) keep their own colour. A two-colour checker floor maps each of its two
tile colours to its own ramp. The surface's variants (cracked, cross, moss, soot, flaking, grave, broken) get the same
recolour, so a style's variants always match its base.

    python Tools/style_recolour.py <out dir>

Writes <out dir>/<Style>/<Style> <surface>[ <variant>].png and plan.json (which source texture each file came from),
for the editor step that makes the materials and the style asset.
"""
import colorsys
import json
import os
import sys

import numpy as np
from PIL import Image

SRC = "Assets/Game/Levels/Dungeon1/Styles/HighRes Materials"
# Source surfaces: (folder, file prefix, surface file name, its variant suffixes).
SOURCES = {
    "Church": ("Church", "Church tiled ", {"wall": ["cracked", "cross", "moss", "mould"], "upper wall": ["soot"],
                                            "floor": ["cracked", "grave"], "vault": [], "trim": []}),
    "Church2": ("Church2", "Church2 ", {"wall": ["cracked", "cross"], "upper wall": ["soot", "flaking"],
                                        "floor": ["broken", "grave"], "ceiling": [], "trim": []}),
    "Church3": ("Church3", "Church3 ", {"wall": ["cracked", "cross"], "upper wall": ["soot", "flaking"],
                                        "floor": ["broken", "grave"], "ceiling": [], "trim": []}),
}

# Each new style: surface -> (source style, source surface, ramp). A ramp is (dark, light) RGB, or for a checker floor
# {"split": (ramp for the saturated tiles, ramp for the grey tiles)}.
STYLES = {
    "Church4": {  # mossy cellar
        "wall": ("Church", "wall", ((24, 30, 24), (118, 128, 104))),
        "upper wall": ("Church2", "upper wall", ((66, 72, 60), (150, 156, 134))),
        "floor": ("Church", "floor", ((36, 30, 24), (148, 130, 104))),
        "ceiling": ("Church2", "ceiling", ((20, 18, 14), (104, 92, 70))),
        "trim": ("Church", "trim", ((16, 32, 24), (88, 118, 88))),
    },
    "Church5": {  # red brick chapel
        "wall": ("Church3", "wall", ((44, 20, 16), (150, 80, 62))),
        "upper wall": ("Church3", "upper wall", ((64, 52, 36), (156, 136, 98))),
        "floor": ("Church2", "floor", {"split": (((96, 88, 70), (176, 166, 136)), ((22, 22, 26), (78, 76, 84)))}),
        "ceiling": ("Church", "vault", ((32, 32, 34), (128, 122, 114))),
        "trim": ("Church2", "trim", ((46, 18, 14), (148, 78, 58))),
    },
    "Church6": {  # pale crypt
        "wall": ("Church", "wall", ((48, 44, 38), (168, 158, 134))),
        "upper wall": ("Church", "upper wall", ((28, 26, 36), (108, 102, 122))),
        "floor": ("Church3", "floor", {"by": "warm", "split": (((96, 92, 78), (158, 152, 128)), ((20, 24, 22), (70, 76, 72)))}),
        "ceiling": ("Church2", "ceiling", ((26, 26, 26), (118, 114, 106))),
        "trim": ("Church3", "trim", ((56, 52, 42), (178, 168, 142))),
    },
}


def hsv(a):
    f = np.vectorize(colorsys.rgb_to_hsv)
    h, s, v = f(a[..., 0] / 255.0, a[..., 1] / 255.0, a[..., 2] / 255.0)
    return h, s, v


def ramp(t, dark, light):
    d = np.array(dark, float); l = np.array(light, float)
    return d + (l - d) * t[..., None]


def recolour(rgb, spec, base_stats=None):
    """Gradient-map the texture's own colours; keep texels of another hue (moss, mould) as they are.
    base_stats: brightness range and hue from the base texture, so a variant maps exactly like its base."""
    lum = rgb @ np.array([.299, .587, .114])
    h, s, v = hsv(rgb)
    if base_stats is None:
        main = s < .9
        hues = h[(s > .15) & main]
        dom = float(np.median(hues)) if hues.size > rgb.shape[0] * rgb.shape[1] * .2 else None
        lo, hi = np.percentile(lum, 1), np.percentile(lum, 99)
        # A faint texture (plaster) keeps its faintness: never stretch less than 150 levels across the ramp.
        mid = (lo + hi) / 2; half = max(hi - lo, 150) / 2
        base_stats = (mid - half, mid + half, dom)
    lo, hi, dom = base_stats
    t = np.clip((lum - lo) / max(1.0, hi - lo), 0, 1)
    if isinstance(spec, dict):
        # Checker: saturated tiles on one ramp, grey tiles on the other (by each texel's saturation, smoothed by tile).
        if spec.get("by") == "warm":
            # Tiles told apart by warmth (warm grey against blue grey), smoothed so a tile stays one colour.
            from scipy import ndimage
            warm = ndimage.uniform_filter(rgb[..., 0] - rgb[..., 2], 7) > np.median(rgb[..., 0] - rgb[..., 2])
        else:
            warm = s > .28
        out = np.where(warm[..., None], ramp(t, *spec["split"][0]), ramp(t, *spec["split"][1]))
    else:
        out = ramp(t, *spec)
    # Another material drawn on top (moss green on grey stone, mould): keep its colour.
    if dom is None:
        other = (s > .3)
    else:
        dh = np.minimum(np.abs(h - dom), 1 - np.abs(h - dom))
        other = (s > .3) & (dh > .1)
    if isinstance(spec, dict):
        other &= False
    out = np.where(other[..., None], rgb, out)
    return np.clip(out, 0, 255).astype(np.uint8), base_stats


def main(out_dir):
    plan = {}
    for style, surfaces in STYLES.items():
        os.makedirs(os.path.join(out_dir, style), exist_ok=True)
        for surface, (src_style, src_surface, spec) in surfaces.items():
            folder, prefix, variants = SOURCES[src_style]
            base_path = f"{SRC}/{folder}/{prefix}{src_surface}.png"
            base = np.asarray(Image.open(base_path).convert("RGB")).astype(float)
            img, stats = recolour(base, spec)
            name = f"{style} {surface}"
            Image.fromarray(img).save(os.path.join(out_dir, style, name + ".png"))
            plan[f"{style}/{name}"] = {"source": base_path, "surface": surface, "variant": ""}
            for var in variants.get(src_surface, []):
                vp = f"{SRC}/{folder}/{prefix}{src_surface} {var}.png"
                if not os.path.exists(vp):
                    continue
                vimg, _ = recolour(np.asarray(Image.open(vp).convert("RGB")).astype(float), spec, stats)
                vname = f"{style} {surface} {var}"
                Image.fromarray(vimg).save(os.path.join(out_dir, style, vname + ".png"))
                plan[f"{style}/{vname}"] = {"source": vp, "surface": surface, "variant": var}
    with open(os.path.join(out_dir, "plan.json"), "w") as f:
        json.dump(plan, f, indent=1)
    print(len(plan), "textures")


if __name__ == "__main__":
    main(sys.argv[1])

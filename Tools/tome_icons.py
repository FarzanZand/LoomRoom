"""Spell tome icons: the rendered plain book (Tools/tome_plain.png, the tome's own model as the icon renderer drew it)
tinted to the tome's cover colour, with the spell's own hotbar icon (its SpellDefinition's icon, a 32 px sprite)
set on the cover at three times its size, so every book carries the same picture as its spell on the hotbar (the user,
2026-10-31; before, each cover had a mark drawn for it).

The cover's shading is fitted from a drawn tome (Raise the Fallen) against the plain book's brightness, outside the
middle, and moved to the cover colour. Covers keep the colours they had: COVERS, else the median of the current icon's
cover outside the middle.

    python Tools/tome_icons.py <out dir> [tome name ...]     (no names: every Combat/Spells/*/* Tome.asset)
    python Tools/tome_icons.py --install                     (writes each icon next to its tome, <spell> Tome icon.png)
"""
import glob, os, re, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "Assets")
SPELLS = os.path.join(ASSETS, "Game", "Combat", "Spells")
PLAIN = os.path.join(HERE, "tome_plain.png")
REFERENCE = os.path.join(SPELLS, "Raise the Fallen", "Raise the Fallen Tome icon.png")
SCALE = 3
# Cover colours of the six tomes whose covers were made here first (the others keep the colour their icon has).
COVERS = {"Fireball": (102, 42, 40), "Heal": (50, 84, 82), "Flick": (112, 94, 58), "Helping Hand": (114, 80, 50),
          "Die Throw": (56, 54, 62), "Dice Portal": (50, 54, 96)}


def lum(a):
    return a[..., :3] @ np.array([.299, .587, .114])


def guids():
    found = {}
    for meta in glob.glob(os.path.join(ASSETS, "**", "*.meta"), recursive=True):
        if not (meta.endswith(".png.meta") or meta.endswith(".asset.meta")): continue
        with open(meta, encoding="utf-8", errors="ignore") as f:
            g = re.search(r"guid: (\w+)", f.read())
        if g: found[g.group(1)] = meta[:-5]
    return found


def spell_icon(tome_path, by_guid):
    """The tome's spell's icon as an RGBA image (a single sprite or one cut from a sheet), or None."""
    text = open(tome_path, encoding="utf-8").read()
    s = re.search(r"\n  spell: \{fileID: \d+, guid: (\w+)", text)
    spell = by_guid.get(s.group(1)) if s else None
    if spell is None: return None
    i = re.search(r"\n  icon: \{fileID: (-?\d+), guid: (\w+)", open(spell, encoding="utf-8").read())
    tex = by_guid.get(i.group(2)) if i else None
    if tex is None: return None
    img = Image.open(tex).convert("RGBA")
    if i.group(1) != "21300000":   # a sprite cut from a sheet: its rect (y from the bottom) in the texture's meta
        meta = open(tex + ".meta", encoding="utf-8").read()
        for block in meta.split("- serializedVersion: 2")[1:]:
            if re.search(r"internalID: " + re.escape(i.group(1)) + r"\b", block):
                r = re.search(r"rect:\s+serializedVersion: 2\s+x: (\S+)\s+y: (\S+)\s+width: (\S+)\s+height: (\S+)", block)
                x, y, w, h = (int(float(v)) for v in r.groups())
                img = img.crop((x, img.height - y - h, x + w, img.height - y)); break
    return img


def fit_shade():
    plain = np.asarray(Image.open(PLAIN).convert("RGBA")).astype(float)
    ref = np.asarray(Image.open(REFERENCE).convert("RGBA")).astype(float)
    L = lum(plain); solid = plain[..., 3] > 0
    mask = solid.copy(); mask[40:140, 50:150] = False
    k, c = np.linalg.lstsq(np.stack([L[mask], np.ones(mask.sum())], 1), lum(ref)[mask], rcond=None)[0]
    shade = k * L + c
    return plain, solid, mask, shade / np.median(shade[mask])


def make(name, icon, cover, plain, solid, shade):
    img = np.zeros_like(plain)
    img[..., :3] = np.clip(np.array(cover)[None, None, :] * shade[..., None], 0, 255)
    img[..., 3] = plain[..., 3]
    out = Image.fromarray(img.astype(np.uint8), "RGBA")
    box = icon.getbbox() or (0, 0, icon.width, icon.height)
    mark = icon.crop(box).resize(((box[2] - box[0]) * SCALE, (box[3] - box[1]) * SCALE), Image.NEAREST)
    ys, xs = np.nonzero(solid)
    cx = (xs.min() + 14 + xs.max()) / 2; cy = (ys.min() + ys.max()) / 2   # the cover without its clasps on the left
    out.alpha_composite(mark, (int(round(cx - mark.width / 2)), int(round(cy - mark.height / 2))))
    return out


def main():
    install = "--install" in sys.argv
    args = [a for a in sys.argv[1:] if a != "--install"]
    out_dir = None if install else args.pop(0)
    plain, solid, mask, shade = fit_shade()
    by_guid = guids()
    for tome in sorted(glob.glob(os.path.join(SPELLS, "*", "* Tome.asset"))):
        name = os.path.basename(tome)[:-len(" Tome.asset")]
        if args and name not in args: continue
        icon = spell_icon(tome, by_guid)
        if icon is None: print(name, ": no spell icon"); continue
        path = os.path.join(os.path.dirname(tome), f"{name} Tome icon.png")
        if name in COVERS: cover = COVERS[name]
        else:
            now = np.asarray(Image.open(path).convert("RGBA")).astype(float)
            cover = tuple(np.median(now[mask][:, :3], 0) / np.median(shade[mask]))
        target = path if install else os.path.join(out_dir, f"{name} Tome icon.png")
        if not install: os.makedirs(out_dir, exist_ok=True)
        make(name, icon, cover, plain, solid, shade).save(target)
        print(target)


if __name__ == "__main__":
    main()

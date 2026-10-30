"""Grades the Realms props whose restyle came out as flat fills (the house style, Docs/Pixel-texture-style.md: colour that
varies in clusters and texel to texel, dark creases, lit edges, never a flat area or an airbrushed gradient). Run by
Tools/realms_props.eval.cs right after each of these props is restyled, on its fresh texture, so it is safe to run again.

  gold     coins and hoards: ochre gold (not orange), stepped into four tones in hard-edged clusters, a coin's worth of
           shade per cluster, single bright glints on the open tops, the gaps between coins dark
  molten   lava, molten metal and the glow of a furnace: a dark crust broken by cracks of orange and yellow heat, in
           irregular hard-edged patches; the emission map follows the same pattern (crust does not glow)
  ore      a glass ore pot: mostly dark sooty glass with lumps of glowing ore inside, so it reads as a vessel
  crystal  the violet crystals: muted, four tones by facet and cluster, a lit texel along upper edges
  bones    a skeleton's body: the pack's pinkish, flesh-toned bone turned an aged ivory (other colours untouched)
  charred  the same, but old grey bone darkened with soot in patches (the Bone Brute, so it reads apart from the hall
           skeletons on the same body)
  skin     the Pit Demon's hide: its own colour stepped into four tones in hard-edged patches, creases darker

    python Tools/realms_grade.py <mode> <texture.png> [<emission.png>]"""
import sys
import numpy as np
from PIL import Image

rng = np.random.default_rng(11)


def field(h, w, scale, seed):
    """A smooth 0-1 field with features about scale texels across, cut into tones afterwards so its shapes come out
    irregular and hard-edged (the drawn style's clusters), never a soft gradient."""
    r = np.random.default_rng(seed)
    g = r.random((h // scale + 3, w // scale + 3))
    big = Image.fromarray((g * 255).astype("uint8")).resize(((w // scale + 3) * scale, (h // scale + 3) * scale), Image.BICUBIC)
    return np.asarray(big).astype(float)[scale:scale + h, scale:scale + w] / 255


def steps(x, levels):
    return np.asarray(levels, dtype=float)[np.clip((x * len(levels)).astype(int), 0, len(levels) - 1)]


def local_mean(lum, w, h, k=6):
    small = Image.fromarray(np.clip(lum, 0, 255).astype("uint8")).resize((max(1, w // k), max(1, h // k)), Image.BOX)
    return np.asarray(small.resize((w, h), Image.BILINEAR)).astype(float)


def covered(a):
    return a[..., 3] > 8 if a.shape[-1] == 4 else np.ones(a.shape[:2], bool)


def save(path, rgb, a):
    out = np.concatenate([np.clip(rgb, 0, 255), a[..., 3:4]], -1) if a.shape[-1] == 4 else np.clip(rgb, 0, 255)
    Image.fromarray(out.astype("uint8"), "RGBA" if a.shape[-1] == 4 else "RGB").save(path)


def gold(path):
    """The coins the pack paints (light discs on darker gaps) kept, cut into four ochre gold tones by their own light, so
    each coin is a hard-edged lighter shape on a dark gap and creases go darker still; single glints on the brightest."""
    a = np.asarray(Image.open(path).convert("RGBA")).astype(float)
    h, w = a.shape[:2]
    cov = covered(a)
    lum = a[..., :3].mean(-1)
    lo, hi = np.percentile(lum[cov], 8), np.percentile(lum[cov], 92)
    rel = np.clip((lum - lo) / max(1.0, hi - lo), 0, 1)
    tones = np.array([[96, 64, 24], [156, 112, 42], [204, 158, 66], [238, 204, 116]], float)
    t = np.clip((rel + (field(h, w, 3, 1) - .5) * .18) * 4, 0, 3.999).astype(int)
    lm = local_mean(lum, w, h, 4)
    t = np.where(lum < lm * .88, np.maximum(t - 1, 0), t)
    rgb = tones[t] * (1 + (rng.random((h, w, 1)) - .5) * .06)
    glint = (rng.random((h, w)) < .02) & (t == 3)
    rgb[glint] = [255, 238, 176]
    save(path, rgb, a)


def molten(path, emission, ore=False):
    a = np.asarray(Image.open(path).convert("RGBA")).astype(float)
    h, w = a.shape[:2]
    e = np.asarray(Image.open(emission).convert("RGBA")).astype(float) if emission else None
    hot = (e[..., :3].max(-1) > 30) if e is not None else (a[..., 0] > 150) & (a[..., 0] > a[..., 2] * 1.6)
    # Heat in hard-edged patches: crust (dark, no glow), deep red, orange, yellow cores; cracks of heat run through the crust.
    f = field(h, w, 4, 3) * .55 + field(h, w, 9, 4) * .45
    crack = np.abs(field(h, w, 5, 6) - .5) < .045
    level = steps(np.clip((f - .25) / .5, 0, .999), [0, 0, 1, 2, 2, 3]) if not ore else steps(np.clip((f - .5) / .3, 0, .999), [0, 0, 0, 0, 0, 1, 2, 3])
    if not ore: level = np.where(crack & (level == 0), 2, level)
    level = level.astype(int)
    base_tones = np.array([[46, 22, 14], [120, 34, 12], [212, 92, 22], [246, 178, 64]], float)
    glow_tones = np.array([[0, 0, 0], [110, 26, 6], [230, 96, 18], [255, 190, 70]], float)
    if ore:
        base_tones[0] = [38, 32, 30]   # sooty glass
    rgb = a[..., :3].copy()
    rgb[hot] = base_tones[level][hot] * (1 + (rng.random((h, w, 1))[hot] - .5) * .1)
    save(path, rgb, a)
    if e is not None:
        ergb = e[..., :3].copy()
        ergb[hot] = glow_tones[level][hot]
        save(emission, ergb, e)


def crystal(path):
    a = np.asarray(Image.open(path).convert("RGBA")).astype(float)
    h, w = a.shape[:2]
    rgb = a[..., :3]
    lum = rgb.mean(-1)
    cov = covered(a)
    rel = lum / max(1.0, np.percentile(lum[cov], 90))
    # Four violet-grey tones by the restyle's own light (facets) and a cluster field (inclusions), crisp edges.
    tones = np.array([[44, 34, 66], [82, 64, 118], [124, 104, 164], [176, 160, 206]], float)
    cluster = field(h, w, 4, 7)
    t = np.clip(rel * 3.2 - 1.0 + (cluster - .5) * 1.1, 0, 3.999).astype(int)
    lm = local_mean(lum, w, h, 4)
    t = np.where(lum < lm * .9, np.maximum(t - 1, 0), t)
    out = tones[t] * (1 + (rng.random((h, w, 1)) - .5) * .07)
    glint = (rng.random((h, w)) < .02) & (t >= 2)
    out[glint] = [212, 204, 232]
    save(path, out, a)


def bones(path, charred=False):
    a = np.asarray(Image.open(path).convert("RGBA")).astype(float)
    h, w = a.shape[:2]
    rgb = a[..., :3] / 255
    mx, mn = rgb.max(-1), rgb.min(-1)
    v = mx; s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    # flesh-toned bone: red the highest channel, blue the lowest, not saturated, not dark
    bone = (r >= g) & (g >= b - .02) & (s > .08) & (s < .5) & (v > .38) & ((r - g) > (g - b) * .6)
    if charred:
        soot = (field(h, w, 3, 9) * .7 + field(h, w, 7, 10) * .3) < .36
        ivory = np.stack([v * .74, v * .72, v * .66], -1) * np.where(soot, .7, 1.0)[..., None]
    else:
        ivory = np.stack([v * .98, v * .92, v * .76], -1)
    out = np.where(bone[..., None], ivory, rgb) * 255
    save(path, out, a)


def skin(path):
    a = np.asarray(Image.open(path).convert("RGBA")).astype(float)
    h, w = a.shape[:2]
    rgb = a[..., :3]
    lum = rgb.mean(-1)
    cluster = field(h, w, 4, 12) * .6 + field(h, w, 8, 13) * .4
    k = 1 + steps(np.clip((cluster - .2) / .6, 0, .999), [-.16, -.06, .03, .1])
    lm = local_mean(lum, w, h, 4)
    k = np.where(lum < lm * .9, k * .8, k)
    save(path, rgb * k[..., None], a)


if __name__ == "__main__":
    mode, tex = sys.argv[1], sys.argv[2]
    emi = sys.argv[3] if len(sys.argv) > 3 else None
    if mode == "gold": gold(tex)
    elif mode == "molten": molten(tex, emi)
    elif mode == "ore": molten(tex, emi, ore=True)
    elif mode == "crystal": crystal(tex)
    elif mode == "bones": bones(tex)
    elif mode == "charred": bones(tex, charred=True)
    elif mode == "skin": skin(tex)
    else: sys.exit("unknown mode " + mode)
    print("graded", mode, tex)

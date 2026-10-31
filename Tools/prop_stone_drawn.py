"""Stone props drawn in the room style's own stone (the Realms props: obelisks, tables, pillars, the forge's basalt). The
old finish (prop_organic.py --material stone) filled each face flat and scattered square dark blocks and speckles over it,
which the user saw as an AI look. This draws every texel from the room's own drawn stone instead, so a prop is made of
the same stone as the walls beside it:

  * the stone: the style's wall texture (128 texels a metre) with its mortar lines filled in from the stone round them,
    laid on the model in metres, each face from the side it faces (top, front or side), so blotches keep their size
  * colour: the stone's own colour texel for texel (the props were pink-grey beside the walls when they took Synty's
    hue), set to --bright; a part Synty paints a clear colour (a gold trim, a red band) keeps its hue, muted by --sat;
    a part Synty paints darker stays darker
  * creases: enclosed texels a step and two darker (the model's own ambient occlusion)
  * edges: where the face turns, a step lighter (a worn, lit edge)

The model is read from PixelRestyle.BakeGBuffer (per texel: normal, place on the model 0-1, occlusion, coverage, Synty's
colour).

    python Tools/prop_stone_drawn.py <gbuffer.bin> <width> <height> <out.png> --extent x,y,z --tile <wall.png> [--sat .5] [--bright .6]
"""
import argparse
import colorsys
import numpy as np
from PIL import Image
from scipy import ndimage

TPM = 128  # the dungeon's texels a metre (the room styles' too)


def stone_surface(path):
    """The style's wall texture with its mortar lines (thin dark lines between the stones) filled from the stone round
    them: its luminance relative to its mean (1 = average) and its colour per texel."""
    t = np.asarray(Image.open(path).convert("RGB")).astype(float) / 255
    lum = t @ [.3, .59, .11]
    # Mortar: texels darker than the stone around them, in thin runs; grown by two so their soft edges and the
    # wider spots where joints cross go too (left in, they showed as dark blots on the props).
    around = ndimage.median_filter(lum, size=11, mode="wrap")
    mortar = ndimage.binary_dilation(lum < around * .8, iterations=2)
    # Fill from the nearest stone texel (wrapping, so the tile stays seamless).
    h, w = lum.shape
    bm = np.tile(mortar, (3, 3))
    idx = ndimage.distance_transform_edt(bm, return_distances=False, return_indices=True)
    big = np.tile(t, (3, 3, 1))
    colour = big[idx[0], idx[1]][h:2 * h, w:2 * w]
    filled = colour @ [.3, .59, .11]
    mean = max(filled.mean(), 1e-3)
    return filled / mean, colour / np.maximum(filled[..., None], 1e-3)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("gbuffer"); ap.add_argument("width", type=int); ap.add_argument("height", type=int); ap.add_argument("out")
    ap.add_argument("--extent", required=True, help="the model's size in metres as x,y,z")
    ap.add_argument("--tile", required=True, help="the room style's drawn wall texture")
    ap.add_argument("--sat", type=float, default=.5, help="saturation of Synty's colour (1 = as it is)")
    ap.add_argument("--bright", type=float, default=.6, help="the stone's average value")
    ap.add_argument("--contrast", type=float, default=1.0, help="how strongly the stone's light and dark show")
    a = ap.parse_args()
    H, W = a.height, a.width
    g = np.fromfile(a.gbuffer, dtype=np.float32).reshape(H, W, 12)[::-1]
    cov = g[..., 8] > .5; nrm = g[..., 1:4]; pos = g[..., 4:7]; ao = np.clip(g[..., 7], 0, 1); own = np.clip(g[..., 9:12], 0, 1)
    ext = np.array([float(v) for v in a.extent.split(",")])
    metres = pos * ext
    stone, tint = stone_surface(a.tile)
    th, tw = stone.shape

    # Each texel from the side its face looks to: x faces take (z, y), y faces (x, z), z faces (x, y).
    axis = np.argmax(np.abs(nrm), axis=2)
    u = np.where(axis == 0, metres[..., 2], metres[..., 0])
    v = np.where(axis == 1, metres[..., 2], metres[..., 1])
    # Each axis its own offset in the tile, so a corner doesn't show the same blotch on both faces.
    u = u + axis * .37; v = v + axis * .61
    tx = (np.floor(u * TPM).astype(int)) % tw
    ty = (np.floor(v * TPM).astype(int)) % th
    lum = stone[ty, tx]
    lum = 1 + (lum - 1) * a.contrast
    chroma = tint[ty, tx]   # the stone's colour at unit luminance

    # Creases and edges, as steps of the value.
    step = np.zeros((H, W))
    step[ao < .55] -= .14
    step[ao < .3] -= .14
    edge = np.zeros((H, W), bool)
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nn = np.roll(np.roll(nrm, dy, 0), dx, 1); nc = np.roll(np.roll(cov, dy, 0), dx, 1)
        edge |= nc & ((nrm * nn).sum(axis=2) < .6)
    step[edge & (ao > .45)] += .12
    # A little darker toward the foot (dust and grime rising from the floor).
    step -= np.clip(.25 - metres[..., 1], 0, .25) * .5

    # Synty's colour per part: its hue and saturation (muted), its value relative to the model's, so a darker part stays darker.
    hsv = np.zeros_like(own)
    for y, x in zip(*np.nonzero(cov)):
        hsv[y, x] = colorsys.rgb_to_hsv(*own[y, x])
    ref = max(np.percentile(hsv[..., 2][cov], 60), 1e-3)
    out = np.zeros((H, W, 3))
    for y, x in zip(*np.nonzero(cov)):
        hh, ss, vv = hsv[y, x]
        val = float(np.clip(a.bright * (vv / ref) ** .6 * lum[y, x] + step[y, x] * a.bright, .03, .97))
        if ss > .35:   # a part Synty paints a clear colour keeps its hue
            out[y, x] = colorsys.hsv_to_rgb(hh, ss * a.sat, val)
        else:          # stone: the wall's own colour at this value
            out[y, x] = np.clip(chroma[y, x] * val, 0, 1)
    # Outside the model: grow the colours out a few texels, so filtering at UV seams never picks up black.
    rgb = (out * 255).astype(np.uint8)
    if (~cov).any():
        idx = ndimage.distance_transform_edt(~cov, return_distances=False, return_indices=True)
        rgb = rgb[idx[0], idx[1]]
    Image.fromarray(rgb).save(a.out)
    print(f"{a.out}: {cov.sum()} texels, stone {tw}x{th}")


if __name__ == "__main__":
    main()

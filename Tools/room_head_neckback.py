"""The bake for her head after Tools/room_head_neckback.eval.cs shrank its UVs to make room for the back of the neck:
the original bake is scaled into the same corner (UVs start bottom left) and the free strip is filled with the bake's
own skin colour (its median skin texel), so the views, swatches and projection treat the sleeve as skin.

    python Tools/room_head_neckback.py <original bake png> <out bake png>
"""
import sys
import numpy as np
from PIL import Image

src, dst = sys.argv[1], sys.argv[2]
scale = float(open("Temp/RoomHead/neckback.txt").read().strip())
im = Image.open(src).convert("RGBA"); W, H = im.size
a = np.asarray(im).astype(np.float32) / 255
r, g, b = a[..., 0], a[..., 1], a[..., 2]
skin = (r > g * 1.04) & (g > b * .98) & (r > b * 1.1) & (a[..., :3].mean(-1) > .3)
med = np.median(a[skin][:, :3], 0)
out = Image.new("RGBA", (W, H), tuple(int(round(c * 255)) for c in med) + (255,))
small = im.resize((int(round(W * scale)), int(round(H * scale))), Image.NEAREST)
out.paste(small, (0, H - small.size[1]))
out.save(dst)
print("skin fill", (med * 255).round().astype(int).tolist(), "scaled", small.size)

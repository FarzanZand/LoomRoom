"""A copy of a pixel texture at fewer texels, for a prop drawn smaller than its art was baked for (a candle in a cluster,
a jar on a shelf): every texel of the copy the average of the source texels it covers (box filter), alpha cut back to
0 or 255 where the source is cut, so the copy keeps a crisp edge. Used by Tools/cellar_texels.eval.cs.
    python Tools/texel_fit.py <in.png> <out.png> <factor below 1>"""
import sys
from PIL import Image

src, out, k = sys.argv[1], sys.argv[2], float(sys.argv[3])
im = Image.open(src).convert("RGBA")
w, h = max(1, round(im.width * k)), max(1, round(im.height * k))
small = im.resize((w, h), Image.BOX)
a = small.getchannel("A").point(lambda v: 255 if v >= 128 else 0)
small.putalpha(a)
small.save(out)
print(out, w, "x", h)

"""The character sheet as it lies on the hobby table (UI/Sprites/Meta/Sheet on table.png, 384 x 256, kept at every
texel): a small copy of the sheet on screen, in a few flat inks, turned to face the wall as the old one was.

    python -I Tools/sheet_on_table.py <a 1920 x 1080 capture of the open sheet> "Assets/Game/UI/Sprites/Meta/Sheet on table.png"

The capture is the game with the sheet open (the record sheet, 1720 x 960 in the middle of the screen).
"""
import sys
import numpy as np
from PIL import Image

src, out = sys.argv[1], sys.argv[2]
shot = Image.open(src).convert("RGB")
sheet = shot.crop((100, 60, 1820, 1020))
# Shrunk in two steps so thin print survives as grey, then every texel takes the nearest of a few inks.
small = sheet.resize((768, 512), Image.BOX).resize((384, 256), Image.LANCZOS)
inks = np.array([(240, 232, 212), (228, 218, 195), (203, 189, 161), (150, 138, 120), (96, 86, 78), (40, 34, 32),
                 (172, 36, 28), (214, 122, 104)], dtype=np.float32)
a = np.asarray(small, dtype=np.float32)
d = ((a[:, :, None, :] - inks[None, None, :, :]) ** 2).sum(-1)
flat = inks[d.argmin(-1)].astype(np.uint8)
Image.fromarray(flat, "RGB").rotate(180).save(out)
print("wrote", out)

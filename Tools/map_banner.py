"""The banner floating over places on the realm map not yet visited (CampaignManager.Markers): a red swallowtail flag
with a gold edge and a gold tower crest on a dark pole with a gold knob, 14 x 24 texels at the map's 6.4 texels a unit,
transparent round it. Dirty like the house style: each texel's colour varies a little, darker toward the foot, a few
stains and worn texels. Usage: python Tools/map_banner.py <out.png>"""
import sys, random
from PIL import Image

ROWS = [
    "......KK......",
    ".....KKKK.....",
    "......KK......",
    "......PP......",
    "GGGGGGPPGGGGGG",
    "Gggggggggggggg",
    "GRRRRRRRRRRRRg",
    "GRRRRRRRRRRRRg",
    "GRRRRRCCRRRRRg",
    "GRRRRCCCCRRRRg",
    "GRRRRCddCRRRRg",
    "GRRRRCddCRRRRg",
    "GRRRRCCCCRRRRg",
    "GRRRCCCCCCRRRg",
    "GRRRRRRRRRRRRg",
    "GRRRRRRRRRRRRg",
    "GRRRRRRRRRRRRg",
    "GRRRRRRRRRRRRg",
    "GRRRRRggRRRRRg",
    "GRRRRg..gRRRRg",
    "GRRRg....gRRRg",
    "GRRg......gRRg",
    "GRg........gRg",
    "Gg..........gg",
]
COL = {'K': (226, 178, 74), 'P': (66, 44, 30), 'G': (214, 166, 72), 'g': (160, 112, 46),
       'R': (146, 34, 30), 'C': (222, 176, 78), 'd': (52, 28, 22)}

def main(out):
    rnd = random.Random(19)
    h, w = len(ROWS), len(ROWS[0])
    im = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    for y, row in enumerate(ROWS):
        for x, ch in enumerate(row):
            if ch not in COL: continue
            r, g, b = COL[ch]
            k = 1 - .16 * y / h + rnd.uniform(-.07, .07)      # darker toward the foot, uneven
            if ch == 'R' and rnd.random() < .12: k *= .78       # stains in the cloth
            if ch in 'Gg' and rnd.random() < .15: k *= .82      # worn thread
            im.putpixel((x, y), (min(255, int(r * k)), min(255, int(g * k)), min(255, int(b * k)), 255))
    im.save(out)
    print(out, w, h)

if __name__ == '__main__':
    main(sys.argv[1])

"""The burning enemy's embers (Combat/Spells/Fireball/Burn/Ember.png, used by BurnCoat on the Fireball Burn effect): two
small embers (4 x 4 texels each), hard-edged, yellow core to orange. The fire itself is the Fireball's own (the spell
pack's flame flipbook): small flame sprites drawn here read as stickers on the body and were dropped (2026-11-13).

    python Tools/burn_particles.py "<out folder>"
"""
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1]
BODY, HOT, CORE = (232, 96, 22), (255, 176, 44), (255, 236, 150)


def ember(kind):
    img = np.zeros((4, 4, 4), np.uint8)
    pts = [[(1, 1, CORE), (2, 1, HOT), (1, 2, HOT), (2, 2, BODY)], [(1, 1, HOT), (2, 2, BODY), (2, 1, BODY)]][kind]
    for x, y, c in pts: img[y, x] = (*c, 255)
    return img


os.makedirs(out, exist_ok=True)
Image.fromarray(np.concatenate([ember(k) for k in range(2)], axis=1), 'RGBA').save(os.path.join(out, 'Ember.png'))
print('written', out)

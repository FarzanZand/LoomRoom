"""The Gold chest's own painting (Zelda, 2026-10-31: it was the wood chest's texture tinted pale yellow, which read as
pale pinkish wood in game): the wood chest's painted texture with every texel mapped through a gold ramp by its
brightness, dark gold in the planks' grain and cracks, bright gold on the edging, so the paint's detail stays.

    python Tools/gold_chest.py   (writes Levels/Shared/Props/Chests/Gold chest <part> painted.png for the body, lid, latch)
"""
import os
import numpy as np
from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Game")
PARTS = {"body": "SM_Prop_Chest_01", "lid": "SM_Prop_Chest_01_Lid", "latch": "SM_Prop_Chest_01_Latch"}
STOPS = [(0, (40, 24, 8)), (.35, (118, 78, 22)), (.65, (196, 148, 52)), (1, (248, 218, 128))]

for part, mesh in PARTS.items():
    src = os.path.join(ROOT, f"Art/Synty Pixel/Props/{mesh}/{mesh} painted.png")
    out = os.path.join(ROOT, f"Levels/Shared/Props/Chests/Gold chest {part} painted.png")
    a = np.array(Image.open(src).convert("RGB")).astype(float)
    lum = a.mean(2)
    t = np.clip((lum - 25) / 150, 0, 1)[..., None]
    g = np.zeros_like(a)
    for (t0, c0), (t1, c1) in zip(STOPS, STOPS[1:]):
        m = (t >= t0) & (t <= t1)
        g = np.where(m, np.array(c0) + (np.array(c1) - np.array(c0)) * (t - t0) / (t1 - t0), g)
    g[lum < 10] = a[lum < 10]   # the padding round the islands stays as it is
    Image.fromarray(np.clip(g, 0, 255).astype(np.uint8)).save(out)
    print(out)

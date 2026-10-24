"""Skin of a restyled room player head matched to its body's hands (the user, 2026-10-24: the face as light as the
hands and no dirtier). Skin texels (by colour: not hair or beard, not eye whites or irises) are mapped per channel
so their median is the hands' median, and their lightness spread is brought to the hands' spread; the change fades in
over the edge between skin and hair, so no hard line. Reads "<name> restyled raw.png" (kept from the restyle) and writes
"<name> restyled.png".

    python Tools/room_head_skin_tone.py
"""
import os
import shutil
import numpy as np
from scipy import ndimage
from PIL import Image

# hands' median colour and lightness spread (measured on each body's texture, Tools/room_body_skin_to_hands.eval.cs)
HANDS = {"Woman": ((.98, .82, .73), .105), "Man": ((.98, .82, .69), .102)}
# where the weight starts (lightness): his stubble edge is grey and mid-dark, his beard near black; her hair is mid-dark
# but red, kept out by its colour instead
EDGE = {"Woman": .27, "Man": .17}

for who, (hand, spread) in HANDS.items():
    d = f"Assets/Game/Art/Synty Pixel/Characters/Room Head {who}"
    raw, out = f"{d}/Room Head {who} restyled raw.png", f"{d}/Room Head {who} restyled.png"
    if not os.path.exists(raw):
        shutil.copy(out, raw)
    a = np.asarray(Image.open(raw).convert("RGBA")).astype(np.float32) / 255
    c = a[..., :3]; l = c.mean(-1); sat = c.max(-1) - c.min(-1)
    red = c[..., 0] - c[..., 1]
    white = (l > .72) & (sat < .09)
    # weight: 0 for hair and beard (dark), 1 for skin, eased over the edge between them
    w = np.clip((l - EDGE[who]) / .08, 0, 1)
    if who == "Woman": w[(red > .2) & (l < .4)] = 0   # her red hair (her pink skin and lips go through the mapping)
    w[white] = 0
    skin = w > .99
    med = np.median(c[skin], 0); sd = l[skin].std()
    # the hands' spread at about four times their texel density reads as grain (a fine checker): half of it
    k = min(1.0, .5 * spread / max(sd, 1e-4))
    mapped = np.clip(np.array(hand) + (c - med) * k, 0, 1)
    c2 = c * (1 - w[..., None]) + mapped * w[..., None]
    # tan patches in Meshy's paint on her skin (a wedge of light hair colour on the forehead, the shading between her
    # brows): filled from the skin texels round them, ring by ring
    tan = (c[..., 2] / np.maximum(c[..., 0], 1e-3) < .62) & (l > .45)
    fill = tan.copy(); n_tan = int(tan.sum())
    for _ in range(12):
        if not fill.any(): break
        ok = ~fill & (w > .99)
        acc = np.zeros_like(c2); cnt = np.zeros(l.shape)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                acc += np.roll(np.roll(c2 * ok[..., None], dy, 0), dx, 1); cnt += np.roll(np.roll(ok, dy, 0), dx, 1)
        edge = fill & (cnt > 0)
        c2[edge] = acc[edge] / cnt[edge][:, None]; fill &= ~edge
    eyes_cleaned = 0   # his eyes are cleaned by place on the head: Tools/room_head_eyes.eval.cs, after this
    o = np.concatenate([c2, a[..., 3:]], -1)
    Image.fromarray((o * 255).round().astype(np.uint8), "RGBA").save(out)
    print(who, "skin texels", int(skin.sum()), "median", (med * 255).round().astype(int).tolist(), "->", [round(x * 255) for x in hand],
          f"spread {sd:.3f} -> x{k:.2f}, tan texels filled {n_tan}, eye white texels {eyes_cleaned}")

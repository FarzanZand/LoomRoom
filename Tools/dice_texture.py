"""Pixel-art six-sided die: a 48 x 32 atlas, faces 1-6 in a 3 x 2 grid (value v at column (v-1)%3,
row (v-1)//3 from the top), ivory with dark pips and a soft rim. RollingDie maps faces to values.
Also writes a lamp switch click and dice clatters.
Run: python -I Tools/dice_texture.py Assets/Game/Cinematics/Intro/Tabletop
"""
import sys, os, random, wave
import numpy as np
from PIL import Image
from scipy.signal import butter, sosfilt
random.seed(2)
out = sys.argv[1]
F = 16
img = Image.new("RGB", (F * 3, F * 2)); px = img.load()
PIPS = {1: [(1, 1)], 2: [(0, 0), (2, 2)], 3: [(0, 0), (1, 1), (2, 2)], 4: [(0, 0), (2, 0), (0, 2), (2, 2)],
        5: [(0, 0), (2, 0), (1, 1), (0, 2), (2, 2)], 6: [(0, 0), (2, 0), (0, 1), (2, 1), (0, 2), (2, 2)]}
for v in range(1, 7):
    ox, oy = ((v - 1) % 3) * F, ((v - 1) // 3) * F
    for x in range(F):
        for y in range(F):
            edge = min(x, y, F - 1 - x, F - 1 - y)
            base = 222 if edge > 1 else 196 if edge == 1 else 160
            n = random.randint(-5, 5)
            px[ox + x, oy + y] = (base + n, base - 8 + n, base - 26 + n)
    for gx, gy in PIPS[v]:
        cx, cy = ox + 4 + gx * 4, oy + 4 + gy * 4
        for dx in (-1, 0):
            for dy in (-1, 0):
                px[cx + dx, cy + dy] = (40, 30, 34)
        px[cx - 1, cy - 1] = (70, 56, 60)
img.save(os.path.join(out, "Die.png"))

SR = 44100
def save(name, x, vol):
    x = x / (np.max(np.abs(x)) + 1e-9) * vol
    with wave.open(os.path.join(out, name), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes((x * 32767).astype(np.int16).tobytes())
rng = np.random.default_rng(1)
# Lamp switch: two tiny clicks.
n = int(.09 * SR); t = np.arange(n) / SR
click = np.zeros(n)
for at, amp in ((0, 1), (.035, .6)):
    i = int(at * SR); m = int(.006 * SR)
    burst = sosfilt(butter(2, [1800, 7000], "bandpass", fs=SR, output="sos"), rng.normal(0, 1, m)) * np.exp(-np.arange(m) / (SR * .0012))
    click[i:i + m] += burst * amp
save("Lamp click.wav", click, .7)
# Dice clatter: a few knocks of bone on wood.
for k in range(1, 4):
    n = int(.06 * SR); t = np.arange(n) / SR
    knock = sosfilt(butter(2, [900 + 300 * k, 4200], "bandpass", fs=SR, output="sos"), rng.normal(0, 1, n)) * np.exp(-t / .008)
    knock += np.sin(2 * np.pi * (520 + 90 * k) * t) * np.exp(-t / .01) * .4
    save(f"Dice knock {k}.wav", knock, .6)
print("ok")

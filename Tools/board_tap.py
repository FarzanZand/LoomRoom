"""Soft tile taps for board-game reveals: a card tile landing on felt. Short (about 70 ms), quiet, dull.
Run: python -I Tools/board_tap.py Assets/Game/Levels/Shared/Board
"""
import sys, os, wave
import numpy as np
from scipy.signal import butter, sosfilt
SR = 44100
rng = np.random.default_rng(3)
for k in range(1, 4):
    n = int(.075 * SR)
    t = np.arange(n) / SR
    noise = sosfilt(butter(2, [300 + 120 * k, 1600 + 300 * k], "bandpass", fs=SR, output="sos"), rng.normal(0, 1, n))
    thump = np.sin(2 * np.pi * (140 + 25 * k) * t) * np.exp(-t / .018)
    x = noise * np.exp(-t / .012) * .7 + thump * .5
    x[: int(.002 * SR)] *= np.linspace(0, 1, int(.002 * SR))
    x /= np.max(np.abs(x)) + 1e-9
    data = (x * .6 * 32767).astype(np.int16)
    with wave.open(os.path.join(sys.argv[1], f"Board tap {k}.wav"), "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())
print("ok")

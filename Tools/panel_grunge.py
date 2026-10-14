"""Seamless dark grunge texture for UI panels: very dark warm brown, soft large blotches, cloudy middle
detail and a faint fine grain, plus a lighter variant for inset rows.

    python Tools/panel_grunge.py [out_dir] [size] [seed]

Writes "Panel grunge.png" and "Panel grunge light.png" (default Assets/Game/UI/Sprites/Panel, 512 px).
Every layer is noise filtered in frequency space, so the result tiles with no seams.
"""
import os
import sys

import numpy as np
from PIL import Image


def field(size, rng, beta, low_cut=1.0):
    """Periodic noise with a 1/f^beta spectrum, normalised to mean 0, std 1."""
    white = rng.standard_normal((size, size))
    fx = np.fft.fftfreq(size)[:, None]
    fy = np.fft.fftfreq(size)[None, :]
    f = np.sqrt(fx * fx + fy * fy) * size
    amp = np.where(f < low_cut, 0.0, 1.0 / np.maximum(f, 1e-6) ** (beta / 2))
    out = np.real(np.fft.ifft2(np.fft.fft2(white) * amp))
    return (out - out.mean()) / out.std()


def grunge(size, rng):
    blotch = field(size, rng, 3.2, 1.5)    # big soft stains
    cloud = field(size, rng, 2.0, 3.0)     # mottled middle detail
    grain = rng.standard_normal((size, size))
    streak = field(size, rng, 2.6, 2.0)    # faint worn streaks, stretched sideways
    streak = np.roll(streak, 0, 1)
    streak = (streak + np.roll(streak, size // 64, 1) + np.roll(streak, -size // 64, 1)) / 3
    # Dark pits: a few low spots pushed darker, like worn leather or old lacquer.
    pits = np.clip(-cloud - 1.6, 0, None)
    return blotch * 0.55 + cloud * 0.35 + streak * 0.15 + grain * 0.12 - pits * 0.8


def colourise(g, mean_rgb, spread):
    """Grey value field to warm brown: darker spots go colder and less saturated."""
    g = g / g.std()
    mean = np.array(mean_rgb, float)
    warm = np.array([1.0, 0.82, 0.55])   # how each channel follows the brightness
    rgb = mean[None, None, :] + g[..., None] * spread * warm[None, None, :] * (mean.max() / mean)
    return np.clip(rgb, 0, 255).astype(np.uint8)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join("Assets", "Game", "UI", "Sprites", "Panel")
    size = int(sys.argv[2]) if len(sys.argv) > 2 else 512
    seed = int(sys.argv[3]) if len(sys.argv) > 3 else 7
    os.makedirs(out_dir, exist_ok=True)
    rng = np.random.default_rng(seed)
    g = grunge(size, rng)
    Image.fromarray(colourise(g, (15, 12, 8), 4.2)).save(os.path.join(out_dir, "Panel grunge.png"))
    Image.fromarray(colourise(g, (31, 26, 19), 5.0)).save(os.path.join(out_dir, "Panel grunge light.png"))
    print(f"wrote {out_dir} ({size} px, seed {seed})")


if __name__ == "__main__":
    main()

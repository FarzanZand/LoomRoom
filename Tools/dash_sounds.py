"""Sounds for the table player's dash (CombatManager > Player > Dash), layered from the project's own sound library
(ThirdParty/Sound Effects Library): a body moving fast, not a blade, so the whoosh is pitched down and darkened.

    python Tools/dash_sounds.py [out dir]      (default Assets/Game/Audio/Clips/Dash)

Writes:
  Dash 1..3.wav       the push-off: a gravel scuff under the feet, a low body whoosh, cloth and gear moving, a soft thump
Needs ffmpeg on the path. 44.1 kHz mono, 16 bit.
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cascade_sounds import LIB, RATE, fade, load, mix, write  # noqa: E402


def thump(length=.25, f0=70.0):
    t = np.arange(int(RATE * length)) / RATE
    return np.sin(2 * np.pi * (f0 + 40 * np.exp(-t / .02)) * t) * np.exp(-t / .06) * (1 - np.exp(-t / .003))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join("Assets", "Game", "Audio", "Clips", "Dash")
    os.makedirs(out, exist_ok=True)
    steps = os.path.join(LIB, "Footsteps")
    weapons = os.path.join(LIB, "Weapons")
    clothes = os.path.join(LIB, "Clothes")

    for i, (whoosh, cloth, scuff) in enumerate([(1, 2, 1), (3, 5, 2), (4, 8, 1)]):
        air = load(os.path.join(weapons, f"Wpn_Whoosh_0{whoosh}.wav"), 0, .55, rate=.72, lowpass=2400)
        rustle = load(os.path.join(clothes, f"Clothes_0{cloth}.wav"), 0, .4, rate=1.05, lowpass=5000)
        push = load(os.path.join(steps, f"Fst_Gravel_JumpUp_0{scuff}.wav"), 0, .3, rate=.9, lowpass=4000)
        x = mix(.7, (fade(push, .002, .15), 0, .8), (fade(air, .015, .3), .02, 1.0),
                (fade(rustle, .01, .2), .01, .45), (thump(), 0, .35))
        write(os.path.join(out, f"Dash {i + 1}.wav"), fade(x, .001, .25), .85)


if __name__ == "__main__":
    main()

"""A pen writing a number on paper, for the attribute points panel's Ledger look: two quick strokes of a nib on paper
(high, grainy friction noise shaped by each stroke's pressure), a tap as the nib lands, and paper body under it.

    python Tools/pen_scratch.py <out.wav> [seed]
"""
import sys, wave
import numpy as np

out = sys.argv[1] if len(sys.argv) > 1 else "Pen scratch.wav"
rng = np.random.default_rng(int(sys.argv[2]) if len(sys.argv) > 2 else 3)
sr = 44100
total = int(sr * .42)
sig = np.zeros(total)


def stroke(start, length, level):
    n = int(sr * length)
    noise = rng.standard_normal(n)
    # nib friction: grainy bursts (paper fibres) on a high band
    grain = np.repeat(rng.uniform(.4, 1.0, n // 90 + 1), 90)[:n]
    hp = np.diff(noise, prepend=0)                    # tilt to the highs
    hp = np.diff(hp, prepend=0) * .5
    lp = np.convolve(hp, np.ones(3) / 3, mode="same")  # take the hiss off the very top
    t = np.linspace(0, 1, n)
    env = np.sin(np.pi * np.clip(t * 1.1, 0, 1)) ** .6 * (1 - .35 * t)
    s = int(sr * start)
    sig[s:s + n] += lp * grain * env * level


stroke(.02, .13, .5)    # the first stroke of the figure
stroke(.19, .16, .42)   # the second, a little longer and lighter
# the nib touching down
tap = int(sr * .012)
sig[int(sr * .015):int(sr * .015) + tap] += rng.standard_normal(tap) * np.linspace(.35, 0, tap)
# paper body: a soft low thump under the strokes
body = np.sin(2 * np.pi * 180 * np.arange(int(sr * .05)) / sr) * np.linspace(.12, 0, int(sr * .05))
sig[int(sr * .02):int(sr * .02) + len(body)] += body
sig /= max(1e-6, np.abs(sig).max()) / .7
data = (sig * 32767).astype(np.int16)
with wave.open(out, "wb") as w:
    w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr); w.writeframes(data.tobytes())
print("written", out)

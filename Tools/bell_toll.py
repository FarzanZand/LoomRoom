"""The bell room's toll: a large bronze bell struck once, made from its partials.

python Tools/bell_toll.py <out.wav> [seconds]

A church bell's partials sit at inharmonic ratios to its strike note (hum, prime, tierce, quint, nominal and above); each
decays at its own rate, the high ones fast, the hum slowest, with a little beating between near pairs, and a short
noisy clang at the strike. Mono, 44.1 kHz, 16 bit.
"""
import sys
import wave
import numpy as np

RATE = 44100
STRIKE = 98.0  # the strike note (Hz): a big, low bell
# (ratio to the strike note, level, decay seconds, beat Hz)
PARTIALS = [
    (0.5, 0.55, 7.5, 0.35),   # hum
    (1.0, 0.85, 5.5, 0.6),    # prime
    (1.2, 0.45, 4.0, 0.9),    # minor third (tierce)
    (1.5, 0.30, 3.2, 0.0),    # quint
    (2.0, 0.65, 3.0, 1.3),    # nominal
    (2.5, 0.22, 1.9, 0.0),
    (2.67, 0.25, 1.6, 1.8),
    (3.0, 0.18, 1.3, 0.0),
    (4.0, 0.14, .9, 2.4),
    (5.33, 0.08, .6, 0.0),
]


def toll(seconds):
    t = np.arange(int(RATE * seconds)) / RATE
    out = np.zeros_like(t)
    rng = np.random.default_rng(7)
    for ratio, level, decay, beat in PARTIALS:
        f = STRIKE * ratio
        phase = rng.uniform(0, 2 * np.pi)
        tone = np.sin(2 * np.pi * f * t + phase)
        if beat > 0:
            tone *= 1.0 - 0.35 * (0.5 + 0.5 * np.cos(2 * np.pi * beat * t))
        out += level * tone * np.exp(-t / decay)
    # The strike: a short burst of filtered noise and a metallic ping.
    n = rng.normal(0, 1, len(t))
    n = np.convolve(n, np.ones(6) / 6, mode="same")
    out += 0.5 * n * np.exp(-t / 0.035)
    out += 0.25 * np.sin(2 * np.pi * STRIKE * 7.9 * t) * np.exp(-t / 0.12)
    # A soft attack (a few ms) so it doesn't click, and a fade at the very end.
    out *= np.clip(t / 0.004, 0, 1)
    out *= np.clip((seconds - t) / 0.4, 0, 1)
    out /= np.max(np.abs(out)) * 1.12
    return out


def main():
    path = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 7.0
    data = (toll(seconds) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(data.tobytes())
    print("wrote", path)


if __name__ == "__main__":
    main()

"""Dungeon Master babble: continuous gibberish phrases from a small formant synthesiser.

A low, slightly hoarse old voice. Glottal pulses with jitter and breath, three time-varying
formant resonators gliding between vowel targets, soft consonants (nasals, liquids, light plosives,
breathy h), falling sentence pitch, short room tail. Written as 16-bit mono WAVs.
"""
import sys, os, wave
import numpy as np
from scipy.signal import butter, sosfilt

SR = 44100
rng = np.random.default_rng(int(sys.argv[2]) if len(sys.argv) > 2 else 7)

# Vowel formant targets (F1, F2, F3) for an older male voice, slightly lowered.
VOWELS = {
    "a": (700, 1150, 2450), "o": (520, 850, 2400), "u": (340, 800, 2250),
    "e": (480, 1700, 2450), "i": (320, 2050, 2800), "@": (500, 1350, 2400),
}
VOWEL_WEIGHTS = {"a": 3, "o": 3, "@": 4, "e": 2, "u": 2, "i": 1}
# Consonants: kind, formant locus (F1, F2, F3), duration (s).
CONS = {
    "m": ("nasal", (280, 1000, 2300), .07), "n": ("nasal", (280, 1500, 2500), .065),
    "l": ("liquid", (360, 1200, 2700), .06), "r": ("liquid", (420, 1250, 1700), .06),
    "b": ("stop", (250, 900, 2200), .05), "d": ("stop", (260, 1600, 2600), .05),
    "g": ("stop", (260, 1900, 2300), .055), "h": ("breath", None, .07),
    "w": ("liquid", (320, 750, 2200), .06), "v": ("nasal", (300, 1200, 2300), .06),
}
CONS_WEIGHTS = {"m": 4, "n": 4, "l": 4, "r": 3, "b": 2, "d": 3, "g": 2, "h": 3, "w": 2, "v": 2}


def pick(weights):
    keys = list(weights); w = np.array([weights[k] for k in keys], float)
    return keys[rng.choice(len(keys), p=w / w.sum())]


def plan(length):
    """A list of segments (kind, formants, seconds, gain) filling about `length` seconds."""
    segs, t = [], 0.0
    word_left = rng.integers(1, 4)
    while t < length - .18:
        c = pick(CONS_WEIGHTS)
        kind, locus, dur = CONS[c]
        dur *= rng.uniform(.8, 1.25)
        segs.append((kind, locus, dur, .0))
        v = pick(VOWEL_WEIGHTS)
        vd = rng.uniform(.085, .15)
        word_left -= 1
        stress = 1.0 if word_left == 0 or rng.random() < .3 else .82
        if word_left == 0:
            vd *= 1.35
        segs.append(("vowel", VOWELS[v], vd, stress))
        t += dur + vd
        if word_left == 0:
            word_left = rng.integers(1, 4)
            # a short dip between words, not silence
            gap = rng.uniform(.03, .07)
            segs.append(("gap", None, gap, 0.0)); t += gap
    return segs


def resonator(x, freqs, bw):
    """Two-pole resonator with per-sample centre frequency."""
    y = np.zeros_like(x)
    y1 = y2 = 0.0
    r = np.exp(-np.pi * bw / SR)
    for n in range(len(x)):
        c = 2 * r * np.cos(2 * np.pi * freqs[n] / SR)
        out = (1 - r) * x[n] + c * y1 - r * r * y2
        y[n] = out
        y2, y1 = y1, out
    return y


def smooth(a, ms):
    k = max(1, int(SR * ms / 1000))
    kernel = np.hanning(k * 2 + 1); kernel /= kernel.sum()
    pad = np.pad(a, (k, k), mode="edge")
    return np.convolve(pad, kernel, mode="same")[k:-k]


def phrase(length, base_pitch):
    segs = plan(length)
    total = sum(s[2] for s in segs) + .25
    n = int(total * SR)
    f = np.zeros((3, n)); amp = np.zeros(n); noise_amp = np.zeros(n); voiced = np.zeros(n)
    accent = np.zeros(n)
    i = 0
    last = VOWELS["@"]
    for kind, form, dur, stress in segs:
        m = int(dur * SR); sl = slice(i, min(n, i + m))
        target = form if form is not None else last
        for k in range(3):
            f[k, sl] = target[k]
        if kind == "vowel":
            amp[sl] = stress; voiced[sl] = 1; noise_amp[sl] = .05; accent[sl] = stress - .82; last = form
        elif kind == "nasal":
            amp[sl] = .32; voiced[sl] = 1; noise_amp[sl] = .02
        elif kind == "liquid":
            amp[sl] = .5; voiced[sl] = 1; noise_amp[sl] = .03
        elif kind == "stop":
            q = sl.start + int(m * .55)
            amp[sl.start:q] = .06; voiced[sl.start:q] = .4
            amp[q:sl.stop] = .25; voiced[q:sl.stop] = .8; noise_amp[q:q + int(.012 * SR)] = .5
        elif kind == "breath":
            amp[sl] = .05; voiced[sl] = .15; noise_amp[sl] = .35
        elif kind == "gap":
            amp[sl] = .04; voiced[sl] = .3; noise_amp[sl] = .02
        i += m
    end = i
    # Coarticulation: formants and loudness glide instead of jumping.
    for k in range(3):
        f[k] = smooth(f[k], 28)
    amp = smooth(amp, 16); voiced = smooth(voiced, 14); noise_amp = smooth(noise_amp, 8)
    accent = smooth(accent, 60)

    # Pitch: falling sentence line, accents on stressed vowels, slow wobble, jitter.
    t = np.arange(n) / SR
    decl = np.interp(t, [0, end / SR * .15, end / SR], [1.06, 1.0, .84])
    wobble = 1 + .012 * np.sin(2 * np.pi * 4.8 * t + rng.uniform(0, 6)) + .02 * np.sin(2 * np.pi * .7 * t + rng.uniform(0, 6))
    f0 = base_pitch * decl * wobble * (1 + .5 * accent)
    f0 *= 1 + smooth(rng.normal(0, .006, n), 3)
    phase = np.cumsum(f0 / SR)
    # Rosenberg-like glottal pulse from the phase, softened (an old, breathy voice).
    p = phase % 1.0
    open_q = .62
    pulse = np.where(p < open_q * .7, .5 * (1 - np.cos(np.pi * p / (open_q * .7))),
                     np.where(p < open_q, np.cos(.5 * np.pi * (p - open_q * .7) / (open_q * .3)), 0.0))
    glottal = np.diff(pulse, prepend=0.0) * 40
    shimmer = 1 + smooth(rng.normal(0, .05, n), 4)
    breath = rng.normal(0, 1, n)
    breath = sosfilt(butter(2, [500, 6000], "bandpass", fs=SR, output="sos"), breath)
    source = glottal * voiced * shimmer + breath * (noise_amp + .025 * voiced)

    out = np.zeros(n)
    for k, bw, g in ((0, 90, 1.0), (1, 120, .55), (2, 170, .28)):
        out += g * resonator(source, f[k], bw)
    out *= amp
    # A breathy hum under it: the chest, and a nasal low formant.
    out += .12 * resonator(source * amp, np.full(n, 260.0), 70)

    out = sosfilt(butter(2, 90, "highpass", fs=SR, output="sos"), out)
    out = sosfilt(butter(3, 4200, "lowpass", fs=SR, output="sos"), out)
    # Short wooden room: a few early reflections and a soft tail.
    tail_n = int(.32 * SR)
    ir = np.zeros(tail_n); ir[0] = 1
    for d, g in ((.011, .22), (.019, .16), (.027, .12), (.041, .09)):
        ir[int(d * SR)] += g
    ir += rng.normal(0, 1, tail_n) * np.exp(-np.arange(tail_n) / (SR * .07)) * .03
    out = np.convolve(out, ir)[:n]
    # Fade in and out, normalise, gentle saturation for warmth.
    fade_in = int(.015 * SR); fade_out = int(.12 * SR)
    env = np.ones(n); env[:fade_in] = np.linspace(0, 1, fade_in)
    stop = min(n, end + int(.12 * SR)); env[stop - fade_out:stop] *= np.linspace(1, 0, fade_out) ** 2; env[stop:] = 0
    out = out[:stop] * env[:stop]
    out /= np.max(np.abs(out)) + 1e-9
    out = np.tanh(out * 1.3) / np.tanh(1.3)
    return out * .8


def write(path, x):
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())


if __name__ == "__main__":
    out_dir = sys.argv[1]
    lengths = [.7, .9, 1.1, 1.35, 1.6, 1.9, 2.3, 2.8]
    for k, L in enumerate(lengths, 1):
        x = phrase(L, base_pitch=rng.uniform(98, 108))
        path = os.path.join(out_dir, f"DM babble {k}.wav")
        write(path, x)
        print(path, round(len(x) / SR, 2))

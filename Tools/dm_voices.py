"""Dungeon Master voices, each made a different way (dm_babble.py makes the original "Old" voice).

    python Tools/dm_voices.py <out_dir> [seed] [voice|all]

Voices:
  blips     - retro text beeps: short square-wave blips on a little tune, bit-crushed.
  animalese - fast, bright chatter: additive vowels with sing-song pitch jumps and tongue clicks.
  kazoo     - a buzzing kazoo honking a melody, one honk per syllable, gliding between notes.
  choir     - a few detuned voices singing slow vowels, with vibrato and a long hall.
  beast     - a low growl: rough pulses under heavy distortion, grunts and breaths.
  lute      - plucked strings "talking": one pluck per syllable on a speech-like contour.
  radio     - speech run backwards through an old radio: narrow band, crackle, hiss and wow.

Each writes eight phrases of rising length: "DM <Voice> 1.wav" .. "DM <Voice> 8.wav" (16-bit mono).
"""
import os
import sys
import wave

import numpy as np
from scipy.signal import butter, sosfilt, lfilter, fftconvolve

SR = 44100
LENGTHS = [.7, .9, 1.1, 1.35, 1.6, 1.9, 2.3, 2.8]
rng = np.random.default_rng(int(sys.argv[2]) if len(sys.argv) > 2 else 11)

VOWELS = {
    "a": (750, 1200, 2500), "o": (500, 850, 2450), "u": (350, 750, 2300),
    "e": (500, 1800, 2550), "i": (300, 2200, 2900), "@": (520, 1400, 2450),
}


# ── helpers ────────────────────────────────────────────────────────────────────

def sos(kind, freq, order=2):
    return butter(order, freq, kind, fs=SR, output="sos")


def env(n, attack, release, curve=1.0):
    e = np.ones(n)
    a = min(n, max(1, int(attack * SR)))
    r = min(n - a, max(1, int(release * SR)))
    e[:a] = np.linspace(0, 1, a)
    if r > 0:
        e[n - r:] *= np.linspace(1, 0, r) ** curve
    return e


def smooth(a, ms):
    k = max(1, int(SR * ms / 1000))
    kernel = np.hanning(k * 2 + 1)
    kernel /= kernel.sum()
    return np.convolve(np.pad(a, (k, k), mode="edge"), kernel, mode="same")[k:-k]


def hall(x, seconds, mix, brightness=3500):
    n = int(seconds * SR)
    t = np.arange(n) / SR
    ir = rng.normal(0, 1, n) * np.exp(-t * 6.9 / seconds)
    ir = sosfilt(sos("lowpass", brightness), ir)
    ir[0] = 0
    wet = fftconvolve(x, ir)[: len(x) + n]
    wet = np.pad(wet, (0, len(x) + n - len(wet)))
    wet /= np.max(np.abs(wet)) + 1e-9
    dry = np.pad(x, (0, n))
    dry /= np.max(np.abs(dry)) + 1e-9
    return dry * (1 - mix) + wet * mix


def finish(x, rms=.16, peak=.92):
    """Fade the ends, then level by loudness (so the voices sit together), capped at a peak."""
    x = np.asarray(x, float)
    f = min(len(x) // 4, int(.01 * SR))
    x[:f] *= np.linspace(0, 1, f)
    g = min(len(x) // 4, int(.06 * SR))
    x[-g:] *= np.linspace(1, 0, g)
    voiced = x[np.abs(x) > np.max(np.abs(x)) * .05]
    level = np.sqrt(np.mean(voiced ** 2)) if len(voiced) else 1
    x = x * (rms / (level + 1e-9))
    top = np.max(np.abs(x))
    return x * (peak / top) if top > peak else x


def plan(length, syl, words=(1, 4), word_gap=(.04, .09)):
    """Syllables filling about `length` seconds: (start, duration, stress, last_in_word)."""
    out, t = [], 0.0
    left = rng.integers(*words)
    while t < length - syl[0]:
        d = rng.uniform(*syl)
        left -= 1
        last = left == 0
        stress = 1.0 if last or rng.random() < .3 else .8
        out.append((t, d * (1.25 if last else 1), stress, last))
        t += d * (1.25 if last else 1)
        if last:
            t += rng.uniform(*word_gap)
            left = rng.integers(*words)
    return out, t


def contour(n_syl, start=1.0, end=.82, jump=.0):
    """A falling sentence line with optional random steps, one value per syllable."""
    line = np.linspace(start, end, max(1, n_syl))
    return line * (1 + jump * rng.uniform(-1, 1, len(line)))


def write(path, x):
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


def additive(f0, formants, bws, gains, harmonics):
    """Sum of harmonics of f0 (per sample), each weighted by how close it lies to the formants."""
    phase = 2 * np.pi * np.cumsum(f0) / SR
    out = np.zeros(len(f0))
    for k in range(1, harmonics + 1):
        freq = k * f0
        amp = np.zeros(len(f0))
        for F, bw, g in zip(formants, bws, gains):
            amp += g * np.exp(-((freq - F) / bw) ** 2)
        amp *= freq < SR * .45
        out += amp * np.sin(k * phase) / np.sqrt(k)
    return out


# ── voices ─────────────────────────────────────────────────────────────────────

def blips(length):
    syls, total = plan(length, (.075, .11), words=(2, 5), word_gap=(.06, .12))
    n = int((total + .1) * SR)
    out = np.zeros(n)
    scale = np.array([0, 3, 5, 7, 10, 12])            # minor pentatonic, in semitones
    base = rng.uniform(300, 360)
    line = contour(len(syls), 1.0, .88)
    for (t, d, stress, last), c in zip(syls, line):
        f = base * c * 2 ** (rng.choice(scale) / 12)
        m = int(min(d * .7, .065) * SR)
        tt = np.arange(m) / SR
        duty = .3 + .15 * stress
        wave_ = np.where((tt * f) % 1.0 < duty, 1.0, -1.0)
        i = int(t * SR)
        out[i:i + m] += wave_[: n - i] * env(m, .002, .02)[: n - i] * (.7 + .3 * stress)
    out = np.round(out * 12) / 12                      # a little bit-crush
    out = sosfilt(sos("lowpass", 5200), out)
    return finish(out, rms=.13)


def animalese(length):
    syls, total = plan(length, (.06, .085), words=(2, 5), word_gap=(.03, .06))
    n = int((total + .08) * SR)
    f0 = np.zeros(n)
    F = np.zeros((3, n))
    amp = np.zeros(n)
    clicks = np.zeros(n)
    base = rng.uniform(250, 290)
    keys = list(VOWELS)
    for t, d, stress, last in syls:
        i, m = int(t * SR), int(d * SR)
        sl = slice(i, min(n, i + m))
        f0[sl] = base * rng.choice([.84, 1.0, 1.12, 1.26, 1.5]) * (1.1 if stress == 1.0 else 1)
        v = VOWELS[keys[rng.integers(len(keys))]]
        for k in range(3):
            F[k, sl] = v[k] * 1.3                       # a small, bright mouth
        amp[sl] = env(sl.stop - sl.start, .006, .025) * (.75 + .25 * stress)
        c = int(.007 * SR)
        clicks[i:i + c] += rng.normal(0, 1, min(c, n - i)) * np.linspace(1, 0, min(c, n - i))
    f0 = smooth(np.where(f0 > 0, f0, base), 6)
    for k in range(3):
        F[k] = smooth(np.where(F[k] > 0, F[k], 1500), 10)
    voice = additive(f0, F, (120, 160, 220), (1.0, .7, .3), 22) * amp
    out = voice + .25 * sosfilt(sos("highpass", 2500), clicks)
    return finish(out, rms=.15)


def kazoo(length):
    syls, total = plan(length, (.1, .16), words=(1, 4), word_gap=(.05, .1))
    n = int((total + .12) * SR)
    f0 = np.zeros(n)
    amp = np.zeros(n)
    scale = np.array([0, 2, 4, 7, 9, 12, 14])          # major pentatonic
    base = rng.uniform(170, 200)
    degree = 3
    for (t, d, stress, last) in syls:
        degree = int(np.clip(degree + rng.integers(-2, 3), 0, len(scale) - 1))
        if last:
            degree = max(0, degree - rng.integers(1, 3))  # phrases fall at the end of words
        i, m = int(t * SR), int(d * SR)
        sl = slice(i, min(n, i + m))
        f0[sl] = base * 2 ** (scale[degree] / 12)
        amp[sl] = env(sl.stop - sl.start, .018, .04) * (.7 + .3 * stress)
    f0 = smooth(np.where(f0 > 0, f0, base), 22)           # glide between honks
    t = np.arange(n) / SR
    f0 *= 1 + .012 * np.sin(2 * np.pi * 5.5 * t)
    phase = np.cumsum(f0) / SR
    saw = 2 * (phase % 1.0) - 1
    buzz = np.tanh(saw * 3.5)                             # the membrane rattling
    d = int(.0011 * SR)
    comb = lfilter([1], np.r_[1, np.zeros(d - 1), -.55], buzz)
    out = sosfilt(sos("bandpass", [450, 3800]), comb)
    out = out + .6 * sosfilt(sos("bandpass", [1050, 1450]), comb)
    return finish(out * amp, rms=.14)


def choir(length):
    syls, total = plan(length * .9, (.22, .34), words=(1, 3), word_gap=(.08, .14))
    n = int((total + .15) * SR)
    F = np.zeros((3, n))
    amp = np.zeros(n)
    pitch = np.zeros(n)
    chord = [0, 3, 7, 10, 12]                             # minor seventh
    base = rng.uniform(140, 160)
    for t, d, stress, last in syls:
        i, m = int(t * SR), int(d * SR)
        sl = slice(i, min(n, i + m))
        v = VOWELS[rng.choice(["o", "u", "a", "@"])]
        for k in range(3):
            F[k, sl] = v[k]
        pitch[sl] = base * 2 ** (rng.choice(chord) / 12)
        amp[sl] = env(sl.stop - sl.start, .07, .09) * (.75 + .25 * stress)
    for k in range(3):
        F[k] = smooth(np.where(F[k] > 0, F[k], 600), 50)
    pitch = smooth(np.where(pitch > 0, pitch, base), 45)
    amp = smooth(amp, 25)
    tt = np.arange(n) / SR
    out = np.zeros(n)
    for cents, octave, g in ((-9, 1, 1.0), (7, 1, .9), (0, 2, .55), (3, .5, .7)):
        vib = 1 + .006 * np.sin(2 * np.pi * rng.uniform(4.6, 5.6) * tt + rng.uniform(0, 6))
        f0 = pitch * octave * 2 ** (cents / 1200) * vib
        out += g * additive(f0, F, (170, 210, 260), (1.0, .5, .2), 18)
    out /= np.max(np.abs(out)) + 1e-9
    breath = sosfilt(sos("bandpass", [600, 4000]), rng.normal(0, 1, n))
    breath *= .025 / (np.std(breath) + 1e-9)
    out = (out + breath) * amp
    return finish(hall(out, 1.2, .4, 2600), rms=.13)


def beast(length):
    syls, total = plan(length, (.12, .22), words=(1, 3), word_gap=(.07, .13))
    n = int((total + .15) * SR)
    out = np.zeros(n)
    for t, d, stress, last in syls:
        i, m = int(t * SR), int(d * SR)
        m = min(m, n - i)
        tt = np.arange(m) / SR
        f0 = rng.uniform(46, 62) * np.linspace(1.08, .86, m) * (1 + smooth(rng.normal(0, .08, m), 4))
        phase = np.cumsum(f0) / SR
        pulses = np.exp(-((phase % 1.0) * 9)) - .2        # sharp glottal clicks
        rough = 1 + .7 * np.sin(2 * np.pi * rng.uniform(24, 34) * tt)  # the rattle
        src = pulses * rough + .12 * rng.normal(0, 1, m)
        F1, F2 = rng.uniform(320, 520), rng.uniform(700, 1050)
        grunt = sosfilt(sos("bandpass", [F1 * .7, F1 * 1.3]), src) + .6 * sosfilt(sos("bandpass", [F2 * .8, F2 * 1.25]), src)
        out[i:i + m] += grunt * env(m, .02, .08, 1.5) * (.7 + .3 * stress)
        if last:                                         # a breath out after each word
            b = min(int(.09 * SR), n - i - m)
            if b > 0:
                out[i + m:i + m + b] += sosfilt(sos("bandpass", [300, 1800]), rng.normal(0, 1, b)) * env(b, .02, .06) * .18
    out /= np.max(np.abs(out)) + 1e-9
    out = np.tanh(out * 3) * .7
    out = sosfilt(sos("lowpass", 2400), out)
    return finish(out, rms=.17)


def lute(length):
    syls, total = plan(length, (.09, .15), words=(1, 4), word_gap=(.04, .09))
    n = int((total + .5) * SR)
    out = np.zeros(n)
    scale = np.array([0, 2, 3, 5, 7, 9, 10, 12])        # dorian
    base = rng.uniform(190, 215)
    degree = 4
    line = contour(len(syls), 1.0, .9)
    for (t, d, stress, last), c in zip(syls, line):
        degree = int(np.clip(degree + rng.integers(-2, 3), 0, len(scale) - 1))
        f = base * c * 2 ** (scale[degree] / 12)
        strings = [f] + ([f * 1.5] if stress == 1.0 and rng.random() < .5 else [])
        for k, fs in enumerate(strings):
            N = max(2, int(SR / fs))
            m = min(int(.55 * SR), n - int(t * SR))
            exc = np.zeros(m)
            burst = rng.uniform(-1, 1, N)
            exc[:N] = burst - burst.mean()
            decay = .996
            a = np.zeros(N + 2)
            a[0] = 1
            a[N] = -decay * .5
            a[N + 1] = -decay * .5
            pluck = lfilter([1], a, exc)
            i = int(t * SR) + k * int(.012 * SR)             # a strum: the fifth a hair later
            m = min(m, n - i)
            out[i:i + m] += pluck[:m] * (.75 + .25 * stress) * (1 if k == 0 else .6)
    body = sosfilt(sos("bandpass", [180, 260]), out) * 1.5 + sosfilt(sos("bandpass", [380, 520]), out)
    out = out + .5 * body
    out = sosfilt(sos("lowpass", 4500), out)
    return finish(out, rms=.15)


def radio(length):
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import dm_babble
    dm_babble.rng = rng
    dm_babble.V.clear()
    dm_babble.V.update(dm_babble.VOICES["old"])
    dm_babble.V.update(dict(pitch=(118, 128), formant=1.12, speed=.85, room=.3))
    x = dm_babble.phrase(length, base_pitch=rng.uniform(118, 128))
    # Backwards, a word-sized piece at a time: speech-like rhythm, unreadable sounds.
    chunk = int(.22 * SR)
    pieces = [x[i:i + chunk][::-1] for i in range(0, len(x), chunk)]
    fade = int(.008 * SR)
    for p in pieces:
        if len(p) > 2 * fade:
            p[:fade] *= np.linspace(0, 1, fade)
            p[-fade:] *= np.linspace(1, 0, fade)
    x = np.concatenate(pieces)
    n = len(x)
    t = np.arange(n) / SR
    # Wow and flutter: a slowly wandering read position.
    wobble = (.0018 * np.sin(2 * np.pi * .9 * t) + .0006 * np.sin(2 * np.pi * 7 * t)) * SR
    idx = np.clip(np.arange(n) + wobble, 0, n - 1)
    x = np.interp(idx, np.arange(n), x)
    x = sosfilt(sos("bandpass", [380, 2800], 3), x)
    x = np.tanh(x * 2.5)
    hold = 4                                             # sample-and-hold: a cheap, grainy set
    x = np.repeat(x[::hold], hold)[:n]
    hiss = sosfilt(sos("bandpass", [1500, 7000]), rng.normal(0, 1, n)) * .025
    crackle = np.zeros(n)
    for i in rng.integers(0, n, int(n / SR * 24)):
        crackle[i:i + 30] += rng.normal(0, 1, min(30, n - i)) * np.linspace(1, 0, min(30, n - i)) * rng.uniform(.2, .6)
    return finish(x + hiss + sosfilt(sos("highpass", 1200), crackle) * .5, rms=.14)


VOICES = {"blips": blips, "animalese": animalese, "kazoo": kazoo, "choir": choir,
          "beast": beast, "lute": lute, "radio": radio}


def generate(out_dir, voice):
    make = VOICES[voice]
    for k, L in enumerate(LENGTHS, 1):
        x = make(L)
        path = os.path.join(out_dir, f"DM {voice.capitalize()} {k}.wav")
        write(path, x)
        print(path, round(len(x) / SR, 2))


if __name__ == "__main__":
    out_dir = sys.argv[1]
    voice = sys.argv[3] if len(sys.argv) > 3 else "all"
    for v in (VOICES if voice == "all" else [voice]):
        generate(out_dir, v)

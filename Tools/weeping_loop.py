# A placeholder weeping loop for the weeping enemy: breathy sobs (a voiced vowel with a falling, shaking pitch through two
# formant filters), a ragged in-breath between them, and room-quiet gaps; the loop's ends meet so it can repeat.
#   python Tools/weeping_loop.py [out_dir]
import os, sys, wave
import numpy as np

RATE = 44100
rng = np.random.default_rng(23)

def resonator(signal, freq, bandwidth):
    # A two-pole band-pass: the vowel's formant.
    r = np.exp(-np.pi * bandwidth / RATE)
    a1, a2 = -2 * r * np.cos(2 * np.pi * freq / RATE), r * r
    out = np.zeros_like(signal)
    y1 = y2 = 0.0
    for i, x in enumerate(signal):
        y = x - a1 * y1 - a2 * y2
        out[i] = y; y2 = y1; y1 = y
    return out / (np.max(np.abs(out)) + 1e-9)

def sob(seconds, start_pitch, end_pitch):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    # A falling pitch with a shake in it (crying voices tremble).
    pitch = np.linspace(start_pitch, end_pitch, n) * (1 + .03 * np.sin(2 * np.pi * 7 * t) + .015 * rng.standard_normal(n).cumsum() / np.sqrt(n))
    phase = 2 * np.pi * np.cumsum(pitch) / RATE
    voice = np.sign(np.sin(phase)) * .5 + .5 * np.sin(phase)        # a buzzy glottal source
    breath = rng.standard_normal(n) * .35
    source = voice * .7 + breath
    vowel = .6 * resonator(source, 650, 110) + .4 * resonator(source, 1100, 160)   # "uh"
    # Broken into hiccups: two or three bursts inside the sob.
    bursts = np.zeros(n)
    count = rng.integers(2, 4)
    for k in range(count):
        c = (k + .5) / count
        bursts += np.exp(-((t / seconds - c) ** 2) / (2 * (.11 / count) ** 2))
    env = np.minimum(1, bursts) * np.minimum(1, t / .03) * np.minimum(1, (seconds - t) / .08)
    return vowel * env

def in_breath(seconds):
    n = int(RATE * seconds)
    t = np.arange(n) / RATE
    noise = rng.standard_normal(n)
    hiss = .7 * resonator(noise, 2400, 900) + .3 * resonator(noise, 900, 400)
    env = np.sin(np.pi * t / seconds) ** 1.5 * (1 + .6 * np.sin(2 * np.pi * 9 * t))   # a shaky draw
    return hiss * env * .45

def loop(seconds=9.0):
    out = np.zeros(int(RATE * seconds))
    at = .25
    pieces = [(sob, (1.1, 420, 300)), (in_breath, (.7,)), (sob, (1.4, 460, 310)), (sob, (.8, 380, 330)), (in_breath, (.9,)), (sob, (1.6, 440, 280))]
    for fn, args in pieces:
        part = fn(*args)
        start = int(RATE * at)
        out[start:start + len(part)] += part[:len(out) - start]
        at += args[0] + rng.uniform(.15, .45)
    # Soft room tone so the loop's quiet stretches aren't dead, and matched ends.
    out += rng.standard_normal(len(out)) * .004
    fade = int(RATE * .2)
    out[:fade] *= np.linspace(0, 1, fade); out[-fade:] *= np.linspace(1, 0, fade)
    out = np.convolve(out, np.ones(4) / 4, mode='same')
    out /= np.max(np.abs(out)) + 1e-9
    return (out * .8 * 32767).astype(np.int16)

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Game', 'Audio', 'Clips', 'Weeping')
    os.makedirs(out, exist_ok=True)
    with wave.open(os.path.join(out, 'Weeping loop.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(loop().tobytes())
    print('weeping loop in', out)

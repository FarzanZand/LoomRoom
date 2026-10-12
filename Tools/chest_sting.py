# Chest stings: a short bell arpeggio played over the creak when a chest opens, one per tier (wood: two soft notes,
# iron: three, gold: four brighter ones with a shimmer). Slightly crushed (8-bit-ish) to sit with the pixel art.
#   python Tools/chest_sting.py [out_dir]
import os, sys, wave
import numpy as np

RATE = 44100

def bell(freq, seconds, bright):
    t = np.arange(int(RATE * seconds)) / RATE
    tone = np.zeros_like(t)
    for ratio, gain, decay in ((1, 1, 3.2), (2.76, .45 * bright, 5.5), (5.4, .22 * bright, 8), (8.9, .1 * bright, 12)):
        tone += gain * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-decay * t)
    attack = np.minimum(1, t / .004)
    return tone * attack

def sting(notes, gap, bright, shimmer, seconds=1.6):
    out = np.zeros(int(RATE * seconds))
    for i, f in enumerate(notes):
        start = int(RATE * gap * i)
        b = bell(f, seconds - gap * i, bright) * (.8 + .2 * i / max(1, len(notes) - 1))
        out[start:start + len(b)] += b[:len(out) - start]
    if shimmer:
        rng = np.random.default_rng(7)
        t = np.arange(len(out)) / RATE
        sparkle = np.zeros_like(out)
        for _ in range(28):
            at = rng.uniform(.05, .9); f = rng.uniform(2600, 4200)
            env = np.exp(-60 * np.clip(t - at, 0, None)) * (t >= at)
            sparkle += .12 * np.sin(2 * np.pi * f * t) * env
        out += sparkle * shimmer
    # Crush: 10-bit steps and a gentle low-pass, so it isn't glassy next to the pixel art.
    out = np.convolve(out, np.ones(3) / 3, mode='same')
    out /= np.max(np.abs(out)) + 1e-9
    out = np.round(out * 512) / 512
    fade = np.minimum(1, (len(out) - np.arange(len(out))) / (RATE * .2))
    return (out * fade * .8 * 32767).astype(np.int16)

def save(path, data):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(data.tobytes())

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Game', 'Audio', 'Clips', 'Chests')
    os.makedirs(out, exist_ok=True)
    E5, G5, B5, D6, E6 = 659.3, 784.0, 987.8, 1174.7, 1318.5
    save(os.path.join(out, 'Chest sting wood.wav'), sting([E5, B5], .09, .6, 0, 1.2))
    save(os.path.join(out, 'Chest sting iron.wav'), sting([E5, G5, B5], .08, .8, .4, 1.4))
    save(os.path.join(out, 'Chest sting gold.wav'), sting([E5, G5, B5, E6], .075, 1.0, 1.0, 1.8))
    print('stings in', out)

# Level up: a low swell rising into a quick bell run up two octaves, landing on a held bright chord with a shimmer,
# plus a soft tick for each stat line as it appears. Crushed a little like the chest stings to sit with the pixel art.
#   python Tools/level_up_sting.py [out_dir]
import os, sys, wave
import numpy as np

RATE = 44100

def bell(freq, seconds, bright, decay=3.0):
    t = np.arange(int(RATE * seconds)) / RATE
    tone = np.zeros_like(t)
    for ratio, gain, d in ((1, 1, decay), (2.0, .35 * bright, decay * 1.6), (3.0, .18 * bright, decay * 2.4), (5.4, .08 * bright, decay * 4)):
        tone += gain * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-d * t)
    return tone * np.minimum(1, t / .004)

def swell(freq, seconds):
    # A soft pad: a few detuned partials rising in volume, cut off sharply when the run lands.
    t = np.arange(int(RATE * seconds)) / RATE
    pad = np.zeros_like(t)
    for k, g in ((1, 1), (2, .4), (3, .2), (4, .1)):
        for det in (-.004, .004):
            pad += g * np.sin(2 * np.pi * freq * k * (1 + det) * t)
    env = (t / seconds) ** 2
    return pad * env

def crush(out, seconds_fade=.35):
    out = np.convolve(out, np.ones(3) / 3, mode='same')
    out /= np.max(np.abs(out)) + 1e-9
    out = np.round(out * 512) / 512
    fade = np.minimum(1, (len(out) - np.arange(len(out))) / (RATE * seconds_fade))
    return (out * fade * .85 * 32767).astype(np.int16)

def level_up(seconds=2.8):
    out = np.zeros(int(RATE * seconds))
    # The swell under the first half second.
    sw = swell(130.8, .5) * .35
    out[:len(sw)] += sw
    # The run: C major up two octaves, quickening.
    C5, E5, G5, C6, E6, G6, C7 = 523.3, 659.3, 784.0, 1046.5, 1318.5, 1568.0, 2093.0
    run = [C5, E5, G5, C6, E6, G6]
    at = .42
    for i, f in enumerate(run):
        start = int(RATE * at)
        b = bell(f, seconds - at, .7, 6.0) * .55
        out[start:start + len(b)] += b[:len(out) - start]
        at += .055 - i * .004
    # The landing: a held chord, louder and longer.
    land = int(RATE * at)
    for f, g in ((C6, 1), (E6, .7), (G6, .7), (C7, .45), (261.6, .5)):
        b = bell(f, seconds - at, 1.0, 1.4) * g
        out[land:land + len(b)] += b[:len(out) - land]
    # Shimmer over the landing.
    rng = np.random.default_rng(11)
    t = np.arange(len(out)) / RATE
    for _ in range(40):
        s = rng.uniform(at, at + 1.3); f = rng.uniform(2800, 5200)
        out += .07 * np.sin(2 * np.pi * f * t) * np.exp(-50 * np.clip(t - s, 0, None)) * (t >= s)
    return crush(out)

def tick(seconds=.45):
    out = bell(1568.0, seconds, .5, 9.0) * .8 + bell(2093.0, seconds, .3, 12.0) * .35
    return crush(out, .1)

def save(path, data):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(data.tobytes())

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', 'Assets', 'Game', 'Audio', 'Clips', 'Level up')
    os.makedirs(out, exist_ok=True)
    save(os.path.join(out, 'Level up.wav'), level_up())
    save(os.path.join(out, 'Level up stat.wav'), tick())
    print('level up sounds in', out)

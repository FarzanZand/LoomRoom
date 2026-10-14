# Heavier chest stings, one per tier, in two styles to choose from. Dark and weighty: a low impact as the lid gives, a
# low bell in a minor key and a ringing tail, bigger with each tier (wood: a thud and one dull bell; iron: a metal clang,
# two bells and a low hum; gold: a deep boom, a minor chord of bells, a choir-like swell and falling sparkle).
#   python Tools/chest_sting_heavy.py <out_dir>
# Style A "Bells": the bells carry it. Style B "Choir": the impact and a sung swell carry it.
import os, sys, wave
import numpy as np

RATE = 44100
rng = np.random.default_rng(11)

def t_of(seconds): return np.arange(int(RATE * seconds)) / RATE

def lowpass(x, cutoff):
    # One-pole low-pass, run forward and back (no phase smear).
    a = np.exp(-2 * np.pi * cutoff / RATE)
    y = np.empty_like(x); acc = 0.0
    for i, v in enumerate(x): acc = (1 - a) * v + a * acc; y[i] = acc
    acc = 0.0
    for i in range(len(y) - 1, -1, -1): acc = (1 - a) * y[i] + a * acc; y[i] = acc
    return y

def bell(freq, seconds, bright=1.0, decay=1.6):
    # Church-bell partials (hum, prime, minor third, fifth, nominal...), each dying at its own rate.
    t = t_of(seconds)
    out = np.zeros_like(t)
    for ratio, gain, d in ((.5, .55, .7), (1, 1, 1), (1.19, .45 * bright, 1.5), (1.5, .3 * bright, 1.8),
                           (2, .5 * bright, 2.2), (2.74, .22 * bright, 3.5), (3.76, .12 * bright, 5), (5.4, .06 * bright, 7)):
        out += gain * np.sin(2 * np.pi * freq * ratio * t + rng.uniform(0, 6.28)) * np.exp(-decay * d * t)
    return out * np.minimum(1, t / .003)

def thud(seconds, pitch, wood):
    # The lid giving way: a low sine dropping in pitch, a knock of filtered noise.
    t = t_of(seconds)
    f = pitch * (1 + 2.5 * np.exp(-t * 30))
    body = np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-t * (14 if wood else 6))
    knock = lowpass(rng.standard_normal(len(t)), 900 if wood else 2400) * np.exp(-t * 45) * 2.5
    return body + knock

def clang(seconds, freq):
    # Struck iron: clashing inharmonic partials, fast decay, a long thin ring.
    t = t_of(seconds)
    out = np.zeros_like(t)
    for ratio, gain, d in ((1, 1, 4), (1.41, .7, 5), (2.13, .55, 6), (2.97, .4, 8), (4.2, .3, 10), (5.9, .2, 13)):
        out += gain * np.sin(2 * np.pi * freq * ratio * t) * np.exp(-d * t)
    out += .25 * np.sin(2 * np.pi * freq * 3.62 * t) * np.exp(-1.2 * t)   # the ring that lingers
    return out * np.minimum(1, t / .002)

def boom(seconds):
    # A deep hit: a sub sweep from 90 to 38 Hz and a burst of low noise.
    t = t_of(seconds)
    f = 38 + 52 * np.exp(-t * 9)
    sub = np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-t * 2.2)
    rumble = lowpass(rng.standard_normal(len(t)), 160) * np.exp(-t * 6) * 6
    return sub * 1.4 + rumble

def choir(freqs, seconds, swell=.35):
    # A sung swell: detuned, lightly vibrating voices on each note, filtered soft, rising then fading.
    t = t_of(seconds)
    out = np.zeros_like(t)
    for f in freqs:
        for detune in (-.006, 0, .007):
            vib = 1 + .004 * np.sin(2 * np.pi * 5.2 * t + rng.uniform(0, 6.28))
            phase = 2 * np.pi * np.cumsum(f * (1 + detune) * vib) / RATE
            out += np.sin(phase) + .35 * np.sin(2 * phase) + .15 * np.sin(3 * phase)
    out = lowpass(out, 1800)
    env = np.minimum(1, t / swell) * np.exp(-np.clip(t - swell, 0, None) * 1.1)
    return out * env

def sparkle(seconds, count, start=.15, fall=True):
    t = t_of(seconds)
    out = np.zeros_like(t)
    for i in range(count):
        at = start + rng.uniform(0, seconds * .55)
        f = rng.uniform(2200, 4600) * (1 - .35 * (at / seconds) if fall else 1)
        env = np.exp(-40 * np.clip(t - at, 0, None)) * (t >= at)
        out += np.sin(2 * np.pi * f * t) * env
    return out

def mix(seconds, parts):
    out = np.zeros(int(RATE * seconds))
    for at, gain, sig in parts:
        i = int(RATE * at)
        n = min(len(sig), len(out) - i)
        out[i:i + n] += gain * sig[:n]
    return out

def finish(out, crush=True):
    # A short room (three echoes), gently crushed to 11 bits so it sits with the pixel art, peak at -1 dB, faded tail.
    room = out.copy()
    for delay, gain in ((.031, .35), (.047, .25), (.083, .18)):
        d = int(RATE * delay); room[d:] += gain * out[:-d]
    out = room
    out /= np.max(np.abs(out)) + 1e-9
    if crush: out = np.round(out * 1024) / 1024
    fade = np.minimum(1, (len(out) - np.arange(len(out))) / (RATE * .35))
    return (out * fade * .89 * 32767).astype(np.int16)

def save(path, data):
    with wave.open(path, 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE); w.writeframes(data.tobytes())

# A minor: A2 110, C3 130.8, E3 164.8, A3 220, C4 261.6, E4 329.6, A4 440
A2, C3, E3, A3, C4, E4, A4 = 110, 130.8, 164.8, 220, 261.6, 329.6, 440

def style_a():
    wood = mix(1.6, [(0, .9, thud(.6, 70, True)), (.05, .7, bell(A3, 1.5, .5, 2.2))])
    iron = mix(2.2, [(0, .8, clang(1.2, 196)), (0, .5, thud(.8, 55, False)),
                     (.08, .8, bell(E3, 2.1, .8, 1.5)), (.22, .65, bell(A3, 1.9, .8, 1.5)), (.1, .35, choir([A2], 2.0, .5))])
    gold = mix(3.2, [(0, 1.0, boom(1.6)), (.02, .45, clang(1.0, 247)),
                     (.1, .7, bell(A2, 3.0, 1, 1.0)), (.2, .6, bell(C3, 2.9, 1, 1.1)), (.3, .6, bell(E3, 2.8, 1, 1.1)), (.45, .7, bell(A3, 2.7, 1, 1.2)),
                     (.05, .4, choir([A2, E3], 3.0, .7)), (.4, .18, sparkle(2.5, 26))])
    return wood, iron, gold

def style_b():
    wood = mix(1.6, [(0, 1.0, thud(.7, 62, True)), (.12, .45, choir([A3], 1.3, .15)), (.08, .4, bell(E3, 1.4, .3, 2.6))])
    iron = mix(2.4, [(0, .9, clang(1.4, 175)), (0, .7, thud(.9, 48, False)),
                     (.1, .7, choir([A2, E3], 2.2, .3)), (.35, .4, bell(A3, 1.9, .6, 1.6))])
    gold = mix(3.4, [(0, 1.2, boom(2.0)), (0, .4, clang(1.2, 220)),
                     (.08, .85, choir([A2, C3, E3, A3], 3.2, .55)), (.6, .5, bell(A3, 2.6, 1, 1.1)), (.75, .45, bell(E4, 2.4, 1, 1.2)),
                     (.5, .2, sparkle(2.6, 34))])
    return wood, iron, gold

if __name__ == '__main__':
    out = sys.argv[1] if len(sys.argv) > 1 else '.'
    os.makedirs(out, exist_ok=True)
    for name, sets in (('A Bells', style_a()), ('B Choir', style_b())):
        clips = [finish(s) for s in sets]
        for tier, clip in zip(('wood', 'iron', 'gold'), clips):
            save(os.path.join(out, f'Chest sting {name} {tier}.wav'), clip)
        # All three in a row with a pause, to hear the climb.
        gap = np.zeros(int(RATE * .6), np.int16)
        save(os.path.join(out, f'Chest stings {name} (wood, iron, gold).wav'), np.concatenate([clips[0], gap, clips[1], gap, clips[2]]))
    print('written to', out)

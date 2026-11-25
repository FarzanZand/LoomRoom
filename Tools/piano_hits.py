"""The Pianist's hit sounds (Crescendo, Characters/Classes/Pianist): three piano keys played fast, so a landed blow
sounds like a hit but on a piano, and a bigger one for the crescendo (a fast run up into a chord). The sound library
has no piano notes, so the notes are made here: each a struck string's partials (a little sharp of whole multiples, the
higher ones dying sooner, three strings a hair apart), a hammer's knock and click at the start, and a damper's fade.

The notes are from B flat minor, the key of the Pianist's dungeon track (Audio/Music/Self made/Journey.mp3, measured
from the file), so a hit never sounds out of tune with the music. Same output every run (seeded).

    python Tools/piano_hits.py [<out dir>]      (default Assets/Game/Audio/Clips/Pianist)

Writes Piano hit 1..6.wav (.25 s each) and Piano crescendo 1..2.wav (about 1.3 s), 44.1 kHz mono 16 bit, and prints
each clip's length, peak, loudness and how fast its first key comes up.
"""
import os
import sys
import wave
import numpy as np
from scipy.signal import lfilter

SR = 44100
RNG = np.random.default_rng(88)

# ── Tuning (edit here) ──
KEY_GAP = .042          # seconds between the three keys of a hit
HIT_LENGTH = .25        # a hit clip's length; the damper comes down over its last DAMPER seconds
DAMPER = .09
RUN_GAP = .034          # seconds between the keys of the crescendo's run
CHORD_RING = 1.05       # seconds the crescendo's chord rings before its fade
RUN_LEVEL = 1.25        # the run's keys against one key of the chord (the chord is ten keys at once, so the run needs help to be heard)
PEAK = .89              # every clip's peak (about -1 dB)
OCTAVE = -1             # every note moved this many octaves (the user, 2026-11-25: "darker / lower octave"; 0 = as first written)
DARKEN = .35            # added to every key's brightness exponent: less of the high partials (0 = as first written)
TOP_PARTIAL = 6000      # Hz: no partial above this (9500 as first written)
CLICK = .5              # the hammer's click, times this, and its hiss rolled off (1 = as first written)

A4 = 440.0
NAMES = {"C": -9, "Db": -8, "D": -7, "Eb": -6, "E": -5, "F": -4, "Gb": -3, "G": -2, "Ab": -1, "A": 0, "Bb": 1, "B": 2}


def hz(name):
    """'Bb4' -> its frequency."""
    return A4 * 2 ** ((NAMES[name[:-1]] + 12 * (int(name[-1]) + OCTAVE - 4)) / 12)


def note(name, seconds, velocity=1.0, hammer=1.0):
    """One struck key: velocity 0-1 is how hard (harder is louder and brighter); hammer scales its knock and click
    (a chord's keys share one blow, so each brings only part of it)."""
    f = hz(name)
    n = int(SR * seconds)
    t = np.arange(n) / SR
    out = np.zeros(n)
    stiff = .00035 * (f / 440.0) ** 1.1                 # how far the partials sit above whole multiples
    ring = 1.5 * (261.6 / f) ** .55                     # seconds the first partial takes to die to a third
    bright = 1.75 - .7 * velocity + DARKEN              # lower = more of the high partials
    for k in range(1, 40):
        fk = k * f * np.sqrt(1 + stiff * k * k)
        if fk > TOP_PARTIAL:
            break
        level = abs(np.sin(k * np.pi / 7.5)) / k ** bright   # the hammer's place on the string leaves some partials out
        tau = ring / (1 + .32 * (k - 1))
        shape = .72 * np.exp(-t / (tau * .3)) + .28 * np.exp(-t / tau)   # a quick first fall, then the long ring
        for cents in (-.7, 0.0, .8):                    # three strings a hair apart: the slow shimmer of a real key
            out += level / 3 * shape * np.sin(2 * np.pi * fk * 2 ** (cents / 1200) * t + RNG.uniform(0, 2 * np.pi))
    out *= np.minimum(1.0, t / .0025)                   # the string takes 2.5 ms to speak
    # The hammer: a dull knock and a short click, both gone within 20 ms. They are what makes it read as a blow.
    knock = np.sin(2 * np.pi * 150 * t) * np.exp(-t / .012)
    click = lfilter([1, -1], [1, -.6], RNG.standard_normal(n)) * np.exp(-t / .004)
    if CLICK < 1:
        click = lfilter([1 - .55], [1, -.55], click) * CLICK          # a softer, duller click
    out += velocity * hammer * (.35 * knock + .1 * click)
    return out * (.35 + .65 * velocity)


def place(mix, sound, at):
    start = int(SR * at)
    end = min(len(mix), start + len(sound))
    mix[start:end] += sound[:end - start]


def damp(mix, seconds):
    """The damper coming down over the clip's last stretch (never a cut: a cut clicks)."""
    n = int(SR * seconds)
    mix[-n:] *= np.cos(np.linspace(0, np.pi / 2, n)) ** 2


def room(mix, share):
    """A little of a small room, for the crescendo only: four echoes fed back, then smeared."""
    wet = np.zeros(len(mix))
    for delay, gain in ((1557, .78), (1617, .77), (1491, .79), (1422, .8)):
        a = np.zeros(delay + 1); a[0] = 1; a[delay] = -gain
        wet += lfilter([1], a, mix)
    for delay in (225, 556):
        b = np.zeros(delay + 1); b[0] = -.5; b[delay] = 1
        a = np.zeros(delay + 1); a[0] = 1; a[delay] = -.5
        wet = lfilter(b, a, wet)
    return mix + share * wet / 4


def finish(mix):
    return mix / np.abs(mix).max() * PEAK


def hit(keys):
    """Three keys, each a little harder than the one before, the last the accent."""
    mix = np.zeros(int(SR * HIT_LENGTH))
    for i, (key, velocity) in enumerate(zip(keys, (.72, .84, 1.0))):
        place(mix, note(key, HIT_LENGTH, velocity), i * KEY_GAP)
    damp(mix, DAMPER)
    return finish(mix)


def crescendo(run, chord):
    """A fast run up, growing, into a full chord that rings."""
    landing = len(run) * RUN_GAP
    mix = np.zeros(int(SR * (landing + CHORD_RING + .25)))
    for i, key in enumerate(run):
        place(mix, RUN_LEVEL * note(key, .5, .6 + .4 * i / max(1, len(run) - 1)), i * RUN_GAP)
    for key in chord:
        place(mix, note(key, CHORD_RING + .25, 1.0, .25), landing + RNG.uniform(0, .004))   # hands never land quite together
    mix = room(mix, .16)
    damp(mix, .25)
    return finish(mix)


HITS = [
    ("Bb4", "Db5", "F5"),
    ("Db5", "F5", "Bb5"),
    ("F5", "Bb5", "Db6"),
    ("Bb5", "F5", "Db5"),
    ("Eb5", "F5", "Bb5"),
    ("C5", "Db5", "F5"),
]
CRESCENDOS = [
    (("Bb4", "Db5", "Eb5", "F5", "Bb5", "Db6"), ("Bb2", "F3", "Bb3", "Db4", "F4", "Bb4", "Db5", "F5", "Bb5", "F6")),
    (("F4", "Bb4", "Db5", "F5", "Bb5", "Db6", "F6"), ("Bb2", "Bb3", "F4", "Bb4", "Db5", "F5", "Bb5", "Db6", "Bb6")),
]


def save(path, sound):
    with wave.open(path, "wb") as f:
        f.setnchannels(1); f.setsampwidth(2); f.setframerate(SR)
        f.writeframes((np.clip(sound, -1, 1) * 32767).astype("<i2").tobytes())


def report(name, sound):
    env = np.abs(sound)
    first = np.argmax(env > .5 * env[: int(SR * .03)].max())
    loud = 20 * np.log10(np.sqrt((sound ** 2).mean()))
    print(f"{name}: {len(sound) / SR:.3f} s, peak {20 * np.log10(env.max()):.1f} dB, loudness {loud:.1f} dB, first key up in {first / SR * 1000:.1f} ms")


def main(out):
    os.makedirs(out, exist_ok=True)
    for i, keys in enumerate(HITS, 1):
        sound = hit(keys)
        save(os.path.join(out, f"Piano hit {i}.wav"), sound)
        report(f"Piano hit {i} ({' '.join(keys)})", sound)
    for i, (run, chord) in enumerate(CRESCENDOS, 1):
        sound = crescendo(run, chord)
        save(os.path.join(out, f"Piano crescendo {i}.wav"), sound)
        report(f"Piano crescendo {i}", sound)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/Audio/Clips/Pianist")

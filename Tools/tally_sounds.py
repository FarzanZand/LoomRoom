"""Synthesizes the run recap's tally sounds into Assets/Game/Audio/Clips/Tally/ (the UI Library keys tallyTick,
tallySkull, tallyTotal, tallyBank on the AudioManager prefab):

    tally tick.wav    a bright little plink for each spoil (RunRecapUI raises its pitch as the count goes on)
    tally skull.wav   a dry knock for each skull
    tally total.wav   the total landing: a low thump under a bright chord that rings out
    tally bank.wav    two metal clinks, the spoils going into the bank

    python Tools/tally_sounds.py
"""
import wave
from pathlib import Path

import numpy as np

RATE = 44100
OUT = Path(__file__).resolve().parent.parent / "Assets/Game/Audio/Clips/Tally"
rng = np.random.default_rng(11)


def t(seconds):
    return np.arange(int(RATE * seconds)) / RATE


def env(x, attack, decay):
    a = np.clip(x / max(attack, 1e-4), 0, 1)
    return a * np.exp(-x / decay)


def tone(x, freq, decay, attack=.002, phase=0.0):
    return np.sin(2 * np.pi * freq * x + phase) * env(x, attack, decay)


def place(buffer, sound, at):
    i = int(at * RATE)
    n = min(len(sound), len(buffer) - i)
    buffer[i:i + n] += sound[:n]


def lowpass(signal, cutoff):
    # One-pole low pass: enough to take the hiss off noise.
    k = np.exp(-2 * np.pi * cutoff / RATE)
    out = np.empty_like(signal)
    y = 0.0
    for i, s in enumerate(signal):
        y = (1 - k) * s + k * y
        out[i] = y
    return out


def save(name, signal, peak=.9):
    signal = signal / max(1e-9, np.max(np.abs(signal))) * peak
    fade = min(len(signal), int(.004 * RATE))
    signal[-fade:] *= np.linspace(1, 0, fade)
    data = (signal * 32767).astype(np.int16)
    OUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUT / name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(data.tobytes())
    print(OUT / name)


def tick():
    x = t(.14)
    f = 1046.5  # C6
    s = tone(x, f, .045) + .45 * tone(x, f * 2, .03) + .2 * tone(x, f * 3.01, .02)
    s += .15 * rng.standard_normal(len(x)) * env(x, .0005, .004)  # a soft click at the front
    return s


def skull():
    x = t(.12)
    s = tone(x, 330, .035) + .6 * tone(x, 520, .022) + .3 * tone(x, 790, .015)
    noise = lowpass(rng.standard_normal(len(x)), 2500) * env(x, .0005, .012)
    return s + 1.2 * noise


def total():
    x = t(1.6)
    out = np.zeros(len(x))
    # A thump that drops in pitch.
    xs = t(.35)
    sweep = np.cumsum(np.linspace(140, 45, len(xs))) / RATE
    out[:len(xs)] += 1.4 * np.sin(2 * np.pi * sweep) * env(xs, .002, .09)
    # A bright chord (C major with the octave and a high fifth) with a slow shimmer.
    for f, gain in ((523.25, .5), (659.25, .4), (783.99, .4), (1046.5, .35), (1567.98, .18), (2093.0, .1)):
        shimmer = 1 + .25 * np.sin(2 * np.pi * 5.5 * x + f)
        out += gain * tone(x, f, .55, .004) * shimmer
        out += gain * .3 * tone(x, f * 1.003, .55, .004)
    # A burst of sparkle on top.
    sparkle = lowpass(rng.standard_normal(len(x)), 9000) - lowpass(rng.standard_normal(len(x)), 3000)
    out += .12 * sparkle * env(x, .002, .18)
    return out


def bank():
    x = t(.6)
    out = np.zeros(len(x))
    for at, gain, shift in ((0, .8, 1.0), (.085, 1.0, 1.06)):
        clink = sum(g * tone(t(.5), f * shift, d) for f, g, d in ((2093, .6, .14), (2844, .5, .1), (3951, .35, .08), (5274, .25, .05)))
        place(out, gain * clink, at)
    return out


if __name__ == "__main__":
    save("tally tick.wav", tick(), .7)
    save("tally skull.wav", skull(), .7)
    save("tally total.wav", total(), .9)
    save("tally bank.wav", bank(), .8)

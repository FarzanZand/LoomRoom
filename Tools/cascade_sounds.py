"""Sounds for the Cascade floor build (TableLevelRevealSettings > Cascade sound), cut and layered from the project's own
sound libraries (ThirdParty/Sound Effects Library): heavy, low and dry, nothing bright or chiming. The tiles' clacks are
the library's rock steps used as they are (FPC/Audio/Rock), so they are not made here.

    python Tools/cascade_sounds.py [out dir]      (default Assets/Game/Audio/Clips/Reveal)

Writes:
  Cascade footprint.wav   a low stone grind as the footprint shows (a stone door's drag, pitched down, muffled)
  Entrance slam.wav       the entrance coming down: a stone door's slam over a deep boom, then debris settling
  Torch light 1..3.wav    a torch catching: a short fire whoosh, darkened
  Entrance torches.wav    the entrance's torches catching together: a fuller flare
Needs ffmpeg on the path. 44.1 kHz mono, 16 bit.
"""
import os
import subprocess
import sys
import wave

import numpy as np

RATE = 44100
LIB = os.path.join("Assets", "ThirdParty", "Sound Effects Library")


def load(path, start=0.0, length=None, rate=1.0, lowpass=None):
    """A stretch of a library file as mono floats; rate below 1 pitches and slows it down."""
    filters = [f"aresample={RATE}", f"asetrate={int(RATE * rate)}", f"aresample={RATE}"] if rate != 1.0 else []
    if lowpass:
        filters.append(f"lowpass=f={lowpass}:p=2")
    cmd = ["ffmpeg", "-v", "error", "-ss", str(start)]
    if length:
        cmd += ["-t", str(length / rate)]
    cmd += ["-i", path, "-ac", "1", "-ar", str(RATE)]
    if filters:
        cmd += ["-af", ",".join(filters)]
    cmd += ["-f", "s16le", "-"]
    raw = subprocess.run(cmd, capture_output=True, check=True).stdout
    return np.frombuffer(raw, dtype=np.int16).astype(float) / 32768


def fade(x, inn=.01, out=.2):
    n = len(x)
    a, b = int(RATE * inn), int(RATE * out)
    x = x.copy()
    if a: x[:a] *= np.linspace(0, 1, a)
    if b: x[-b:] *= np.linspace(1, 0, b) ** 2
    return x


def mix(length, *layers):
    out = np.zeros(int(RATE * length))
    for x, at, gain in layers:
        i = int(RATE * at)
        n = min(len(x), len(out) - i)
        out[i:i + n] += x[:n] * gain
    return out


def write(path, x, peak=.9):
    x = x / max(1e-9, np.abs(x).max()) * peak
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes((np.clip(x, -1, 1) * 32767).astype(np.int16).tobytes())


def boom(length=1.0, f0=46.0):
    t = np.arange(int(RATE * length)) / RATE
    return np.sin(2 * np.pi * (f0 + 30 * np.exp(-t / .04)) * t) * np.exp(-t / .28) * (1 - np.exp(-t / .004))


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join("Assets", "Game", "Audio", "Clips", "Reveal")
    os.makedirs(out, exist_ok=True)
    doors = os.path.join(LIB, "Doors")
    magic = os.path.join(LIB, "Magic")
    ambience = os.path.join(LIB, "Ambience")

    grind = load(os.path.join(doors, "Drs_StoneDoor_Open_02.wav"), 1.3, 1.1, rate=.75, lowpass=900)
    write(os.path.join(out, "Cascade footprint.wav"), fade(grind, .35, .45), .6)

    slam = load(os.path.join(doors, "Drs_StoneDoor_Close_02.wav"), 2.5, 1.6, rate=.85, lowpass=2500)
    debris = load(os.path.join(ambience, "Amb_Drop_Debris_01.wav"), .6, 1.6, rate=.8, lowpass=2200)
    write(os.path.join(out, "Entrance slam.wav"), mix(2.0, (fade(slam, .005, .5), 0, 1.0), (boom(1.2), .12, .9), (fade(debris, .05, .6), .25, .35)))

    for i, (name, peak) in enumerate([("Mgc_Fire_Whoosh_01.wav", .55), ("Mgc_Fire_Whoosh_02.wav", .55), ("Mgc_Fire_Whoosh_05.wav", .6)]):
        whoosh = load(os.path.join(magic, name), max(0, peak - .3), 1.0, rate=.8, lowpass=3000)
        write(os.path.join(out, f"Torch light {i + 1}.wav"), fade(whoosh, .04, .4), .7)

    flare = load(os.path.join(magic, "Mgc_Fire_Cast_Start_01.wav"), 1.0, 1.8, rate=.85, lowpass=3500)
    write(os.path.join(out, "Entrance torches.wav"), fade(flare, .06, .6), .8)


if __name__ == "__main__":
    main()

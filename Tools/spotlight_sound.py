"""The theatre spotlight switching on over a boss (BossFight > Intro > Spotlight sound): a heavy switch clunk (a short
filtered noise burst with a metal ring), a low thump under it, and the lamp's electric hum swelling in and dying away.
Written as a 16-bit mono WAV. Usage: python Tools/spotlight_sound.py <out.wav>"""
import math, random, struct, sys, wave

RATE = 44100

def render(seconds=1.6):
    rnd = random.Random(5)
    n = int(RATE * seconds)
    out = [0.0] * n
    lp = 0.0
    for i in range(n):
        t = i / RATE
        # The clunk: a burst of noise, low-passed, gone in 60 ms.
        noise = rnd.uniform(-1, 1)
        lp += (noise - lp) * .18
        clunk = lp * math.exp(-t / .018) * 2.2
        # A metal ring from the switch housing.
        ring = (math.sin(2 * math.pi * 870 * t) * .5 + math.sin(2 * math.pi * 1310 * t) * .35 + math.sin(2 * math.pi * 2120 * t) * .2) * math.exp(-t / .09) * .35
        # The thump: a falling low sine.
        f = 70 * (1 + .6 * math.exp(-t / .03))
        thump = math.sin(2 * math.pi * f * t) * math.exp(-t / .16) * .9
        # The lamp's hum: 100 Hz and its harmonics, swelling in after the clunk, then dying away.
        swell = min(1.0, max(0.0, (t - .04) / .12)) * math.exp(-max(0.0, t - .2) / .55)
        hum = (math.sin(2 * math.pi * 100 * t) * .5 + math.sin(2 * math.pi * 200 * t) * .3 + math.sin(2 * math.pi * 300 * t) * .15 + rnd.uniform(-.05, .05)) * swell * .22
        out[i] = clunk + ring + thump + hum
    peak = max(abs(s) for s in out) or 1
    return [s / peak * .9 for s in out]

if __name__ == "__main__":
    path = sys.argv[1]
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s * 32000)) for s in render()))
    print("wrote", path)

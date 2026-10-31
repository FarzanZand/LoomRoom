"""Typing blips for the portrait dialogue box (PortraitDialogueUI.blips): four short soft voice blips, a little
different each, written as 16-bit mono WAVs. Usage: python Tools/dialogue_blips.py <out dir>"""
import math, os, random, struct, sys, wave

RATE = 22050

def blip(freq, length, seed):
    rnd = random.Random(seed)
    n = int(RATE * length)
    out = []
    for i in range(n):
        t = i / RATE
        env = min(1.0, i / (RATE * .004)) * math.exp(-t / (length * .32))   # quick attack, short decay
        f = freq * (1.0 + .06 * math.exp(-t / .01))                           # a small drop at the start
        # a rounded square (odd harmonics, soft): reads as a voice blip, not a beep
        s = math.sin(2 * math.pi * f * t) + .28 * math.sin(6 * math.pi * f * t) + .12 * math.sin(10 * math.pi * f * t)
        s += rnd.uniform(-.04, .04) * env                                    # a breath of noise
        out.append(s * env * .42)
    return out

def write(path, samples):
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(max(-1, min(1, s)) * 32000)) for s in samples))

if __name__ == "__main__":
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    for i, (freq, length) in enumerate([(392, .055), (440, .05), (415, .06), (466, .045)]):
        write(os.path.join(out, f"Type blip {i + 1}.wav"), blip(freq, length, i))
    print("wrote 4 blips to", out)

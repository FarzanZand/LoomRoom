"""The bedside radio's alarm, played from the radio as the room player wakes up (WakeUpCutsceneController > Alarm).

    python Tools/radio_alarm.py [out dir]      (default Assets/Game/Audio/Clips/Misc)

Writes:
  Radio alarm.wav     six seconds of a gentle alarm through the radio's small speaker: pairs of soft, rounded beeps
                      (a low, nearly pure tone that swells in and dies away, no click), a pair every 1.3 seconds,
                      starting quiet and growing a little, the last pair ending before the six seconds are up
The first version (four sharp square-wave beeps a second at 2.35 kHz) was far too intense (the user, 2026-11-11).
The sound library has no alarm clock, so it is made here. 44.1 kHz mono, 16 bit.
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cascade_sounds import RATE, write  # noqa: E402

LENGTH = 6.0        # seconds, the whole clip
TONE = 1180.0       # Hz, the beep: low for an alarm, so it isn't piercing
BEEPS = 2           # beeps in a group
BEEP_LEN = .17      # seconds a beep sounds, swell and fade included
BEEP_GAP = .09      # seconds between beeps
PERIOD = 1.3        # seconds from one group to the next
GROUPS = 5
START_LEVEL = .45   # the first pair's loudness against the last's (it grows, as a waking alarm does)
PEAK = .225         # the loudest sample, of full scale (halved from .45, the user, 2026-11-11)


def beep(length):
    t = np.arange(int(RATE * length)) / RATE
    # Nearly a sine: a faint second harmonic gives it a little body without any edge.
    phase = 2 * np.pi * TONE * t
    x = np.sin(phase) + .12 * np.sin(2 * phase)
    # Swells in over 18 ms and dies away smoothly, like a soft electronic chime: no click at either end.
    attack = np.minimum(1, t / .018)
    release = np.clip((t[-1] - t) / .05, 0, 1)
    decay = np.exp(-t / .22)
    return x * attack * release * decay


def speaker(x):
    # A small speaker: no lows and the highs rolled off well below where they would bite.
    # Padded, so the filter's ringing at the end can't wrap round onto the start.
    n = len(x) + RATE
    spec = np.fft.rfft(x, n)
    f = np.fft.rfftfreq(n, 1 / RATE)
    low = 1 / (1 + (450 / np.maximum(f, 1)) ** 4)
    high = 1 / (1 + (f / 3200) ** 4)
    return np.fft.irfft(spec * low * high, n)[:len(x)]


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join("Assets", "Game", "Audio", "Clips", "Misc")
    os.makedirs(out, exist_ok=True)
    x = np.zeros(int(RATE * LENGTH))
    b = beep(BEEP_LEN)
    for g in range(GROUPS):
        level = START_LEVEL + (1 - START_LEVEL) * g / max(1, GROUPS - 1)
        for i in range(BEEPS):
            at = int(RATE * (g * PERIOD + i * (BEEP_LEN + BEEP_GAP)))
            x[at:at + len(b)] += b * level
    x = speaker(x)
    # A short tail of the small room the radio is in: two faint, dull echoes.
    for delay, gain in ((.031, .1), (.067, .05)):
        d = int(RATE * delay)
        x[d:] += x[:-d] * gain
    write(os.path.join(out, "Radio alarm.wav"), x, PEAK)


if __name__ == "__main__":
    main()

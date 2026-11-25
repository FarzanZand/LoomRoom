"""The Pianist's icons (Characters/Classes/Pianist/Icons), drawn the way the ability icons on the sheet and the class card
are (UI/Sprites/Meta/Sheet icons): 32 x 32 pixel art with their dark outline and flat tones lit from the upper left,
saved four times larger (128 x 128) as they are.

  Crescendo.png   the passive: three notes in a rising run, each larger and brighter than the one before (growing
                  louder). Round 1 had a note pair over the printed crescendo wedge, which read as stripes at slot size.
  Mood on.png     the class action while it is on: a beamed pair of notes in white and grey with a small red note,
                  which is what the noir look does to the dungeon (black, white and red).
  Mood off.png    the same notes, each its own colour: blue, red, a gold beam, green (the red was pink until the user
                  asked for red instead).
                  (Round 1 was a framed picture, which said "image"; round 2 first tried theatre masks, which the user
                  did not want: no faces.)

    python Tools/pianist_icons.py "<out folder>"
"""
import sys, os
import numpy as np
from PIL import Image

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
N = 32
OUTLINE = (13, 7, 18)
SMALL_HEAD = [".###", "####", "###."]
HEAD = ["..###.", ".#####", "######", "#####.", ".###.."]
BIG_HEAD = ["...#####.", ".########", "#########", "#########", "#########", "########.", ".#####..."]


class Icon:
    def __init__(self):
        self.rgb = np.zeros((N, N, 3), np.uint8); self.on = np.zeros((N, N), bool)

    def fill(self, mask, light, mid, dark, flat=False):
        """A part in three tones: its upper and left rim light, its lower and right rim dark. Later parts lie on top."""
        for y in range(N):
            for x in range(N):
                if not mask[y, x]: continue
                up = y == 0 or not mask[y - 1, x]; left = x == 0 or not mask[y, x - 1]
                down = y == N - 1 or not mask[y + 1, x]; right = x == N - 1 or not mask[y, x + 1]
                tone = mid if flat else dark if (down or right) else light if (up or left) else mid
                self.rgb[y, x] = tone; self.on[y, x] = True

    def save(self, name):
        img = np.zeros((N, N, 4), np.uint8)
        for y in range(N):
            for x in range(N):
                if self.on[y, x]: img[y, x] = (*self.rgb[y, x], 255)
                elif self.on[max(0, y - 1):y + 2, max(0, x - 1):x + 2].any(): img[y, x] = (*OUTLINE, 255)
        assert not (self.on[0].any() or self.on[-1].any() or self.on[:, 0].any() or self.on[:, -1].any()), name
        Image.fromarray(img, "RGBA").resize((N * 4, N * 4), Image.NEAREST).save(os.path.join(out, name))
        print(name)


def blank(): return np.zeros((N, N), bool)


def stamp(a, rows, x, y):
    for j, row in enumerate(rows):
        for i, c in enumerate(row):
            if c == '#': a[y + j, x + i] = True


def spans(a, rows):
    """rows: {y: [(x0, x1), ...]}, both ends included."""
    for y, runs in rows.items():
        for x0, x1 in runs: a[y, x0:x1 + 1] = True


# Crescendo: a small, a middle and a large note, their stems' tops rising to the right.
icon = Icon()
small = blank(); stamp(small, SMALL_HEAD, 2, 25); small[19:27, 5] = True
icon.fill(small, (236, 190, 90), (209, 141, 40), (150, 96, 30))
middle = blank(); stamp(middle, HEAD, 8, 23); middle[11:26, 13:15] = True
icon.fill(middle, (255, 228, 120), (244, 192, 70), (190, 124, 36))
large = blank(); stamp(large, BIG_HEAD, 17, 22); large[3:26, 24:27] = True
spans(large, {3: [(27, 28)], 4: [(27, 29)], 5: [(28, 30)], 6: [(29, 30)], 7: [(29, 30)], 8: [(29, 30)], 9: [(29, 29)]})
icon.fill(large, (255, 243, 191), (255, 214, 60), (209, 141, 40))
icon.save("Crescendo.png")


# Mood: a beamed pair of notes and a small single note, in two colourings (the user, 2026-11-25: "Don't have sad or happy
# faces, have colorful notes or greyscale/red notes"). On is the noir look: the pair in white and grey, the small note
# red. Off is every note its own colour.
def mood(name, left, right, bar, single):
    icon = Icon()
    one = blank(); stamp(one, BIG_HEAD, 2, 16); one[6:20, 9:12] = True
    two = blank(); stamp(two, BIG_HEAD, 13, 13); two[3:17, 20:23] = True
    beam = blank()
    for x in range(9, 23):
        y = round(6 - 3 * (x - 9) / 13)
        beam[y:y + 4, x] = True
    icon.fill(two, *right)
    icon.fill(one, *left)
    icon.fill(beam, *bar)
    small = blank(); stamp(small, HEAD, 22, 24); small[17:27, 27:29] = True
    icon.fill(small, *single)
    icon.save(name)


WHITE = ((250, 250, 252), (222, 222, 228), (150, 150, 162))
GREY = ((196, 196, 204), (150, 150, 160), (92, 92, 104))
RED = ((250, 96, 74), (226, 42, 38), (150, 26, 30))      # no pink on any note (the user, 2026-11-25: red instead)
mood("Mood on.png", WHITE, GREY, WHITE, RED)
mood("Mood off.png", ((150, 232, 255), (84, 196, 240), (40, 130, 180)), RED,
     ((255, 243, 150), (255, 214, 60), (209, 141, 40)), ((170, 244, 150), (104, 214, 104), (52, 140, 66)))

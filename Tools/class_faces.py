"""The class card's faces: a close head-and-shoulders square cut from each class painting (Art/Portraits Highquality), so
the face fills the class card's square portrait box at the table (the whole 3:4 painting showed small inside it, with
empty space either side). Written to UI/Sprites/Meta/Portraits/<Class> face.png at twice the box's 144 px; the class's
AdventurerClass.face points at it and AdventureClassSelectionUI shows it where Use Face is on.

    python Tools/class_faces.py [<class> ...]     (all of them without names)
"""
import sys
from PIL import Image

SRC = "Assets/Game/Art/Portraits Highquality/"
OUT = "Assets/Game/UI/Sprites/Meta/Portraits/"
# class: painting, centre x, centre y, square size (in the 1536 x 2048 paintings): the face a little above the middle,
# the top of the shoulders at the foot
CROPS = {
    "Warrior": ("ShieldMaiden", 768, 480, 560),
    "Cleric": ("DwarfWarrior", 800, 880, 1040),
    "Wizard": ("WomanMage", 760, 640, 620),
    "Arcanist": ("HalfOrcAssassin", 800, 480, 600),
    "Rogue": ("WomanDuellist", 660, 410, 580),
}
SIZE = 288


def main(names):
    for name in names or CROPS:
        f, cx, cy, sz = CROPS[name]
        im = Image.open(SRC + f + ".jpg").convert("RGB")
        im = im.crop((cx - sz // 2, cy - sz // 2, cx + sz // 2, cy + sz // 2)).resize((SIZE, SIZE), Image.LANCZOS)
        im.save(OUT + name + " face.png")
        print(OUT + name + " face.png")


if __name__ == "__main__":
    main(sys.argv[1:])

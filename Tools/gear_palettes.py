# python Tools/gear_palettes.py <out dir>
# The six armour tiers' colour palettes (Items/Scripts/Editor/GearSets.cs). Sidekick outfit parts all share one 32 x 32
# palette layout (each texel a named colour: leather, metal, cloth ...), so one palette colours a whole tier whatever
# pack its parts come from. Each tier's palette is a Sidekick preset's, with the bare skin texels painted over in the
# tier's padding colour (the parts show skin at the neck, arms and between plates; worn gear shows the shirt or mail
# under it instead), and Crystal's turned to ice blue.
import colorsys, os, sys
from PIL import Image

SK = "Assets/Synty/SidekickCharacters/Characters"
# Texels the human, elf and goblin bodies are painted with (u, v from the bottom): skin and its shades.
SKIN = [(0, 10), (0, 11), (2, 4), (2, 6), (4, 4), (4, 6)]

TIERS = [
    # tier, source palette, padding colour, ice
    ("Leather", "VikingWarriors/VikingWarrior_06/Textures/T_VikingWarrior_06ColorMap.png", (58, 44, 34), False),
    ("Iron", "VikingWarriors/VikingWarrior_01/Textures/T_VikingWarrior_01ColorMap.png", (78, 80, 86), False),
    ("Steel", "GoblinFighters/GoblinFighter_05/Textures/T_GoblinFighter_05ColorMap.png", (64, 66, 72), False),
    ("Crystal", "ElvenWarriors/ElvenWarriors_05/Textures/T_ElvenWarriors_05ColorMap.png", (40, 58, 86), True),
    ("Grave", "FantasySkeletons/FantasySkeletons_01/Textures/T_FantasySkeletons_01ColorMap.png", (28, 34, 30), False),
    ("Gilded", "ElvenWarriors/ElvenWarriors_01/Textures/T_ElvenWarriors_01ColorMap.png", (30, 70, 80), False),
]

def ice(rgb):
    # Every colour toward pale blue ice, keeping its lightness order: darks go deep blue, lights go near white-blue.
    h, s, v = colorsys.rgb_to_hsv(*(c / 255 for c in rgb))
    h, s, v = .53 + (1 - v) * .06, min(1, .55 - v * .35 + s * .15), v * .62 + .38
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(h, max(.08, s), min(1, v)))

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
for tier, src, pad, icy in TIERS:
    im = Image.open(os.path.join(SK, src)).convert("RGB")
    px = im.load()
    for u, v in SKIN: px[u, 31 - v] = pad
    if icy:
        for y in range(32):
            for x in range(32):
                if px[x, y] != (255, 0, 255): px[x, y] = ice(px[x, y])
    im.save(os.path.join(out, f"{tier} palette.png"))
    print("wrote", tier)

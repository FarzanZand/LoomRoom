# python Tools/gear_palettes.py <out dir>
# The armour tiers' colour palettes (Items/Scripts/Editor/GearSets.cs). Sidekick outfit parts all share one 32 x 32
# palette layout (each texel a named colour: leather, metal, cloth ...), and every Synty preset has its own palette.
# Each piece keeps its preset's own colours: "<Tier> <Slot> palette.png" is the palette of the preset its parts come
# from (a preset with no palette of its own, such as Viking Warrior 10, uses its pack's first, as Synty's tool does),
# with only the bare skin texels painted over in the tier's padding colour (the parts show skin at the neck, arms and
# between plates; worn gear shows the shirt or mail under it instead). "<Tier> palette.png" is the body armour's, the
# builder's fallback. Keep TIERS in step with GearSets.Tiers.
import os, sys
from PIL import Image

SK = "Assets/Synty/SidekickCharacters/Characters"
# Texels the human, elf and goblin bodies are painted with (u, v from the bottom): skin and its shades.
SKIN = [(0, 10), (0, 11), (2, 4), (2, 6), (4, 4), (4, 6)]
PACKS = {"ELVN_WARR": ("ElvenWarriors", "ElvenWarriors_{:02d}"), "FANT_SKTN": ("FantasySkeletons", "FantasySkeletons_{:02d}"),
         "FANT_SORC": ("FantasySorcerers", "FantasySorcerers_{:02d}"), "GOBL_FIGT": ("GoblinFighters", "GoblinFighter_{:02d}"),
         "VIKG_WARR": ("VikingWarriors", "VikingWarrior_{:02d}")}
SLOTS = ["Helm", "Armor", "Gloves", "Boots", "Leggings"]

TIERS = [
    # tier, padding colour, the preset of each slot's parts (helm, body, gloves, boots, leggings)
    ("Leather", (58, 44, 34), ["FANT_SKTN_01", "VIKG_WARR_06", "VIKG_WARR_06", "VIKG_WARR_06", "VIKG_WARR_06"]),
    ("Iron", (78, 80, 86), ["VIKG_WARR_04", "VIKG_WARR_01", "VIKG_WARR_03", "VIKG_WARR_04", "VIKG_WARR_03"]),
    ("Steel", (64, 66, 72), ["GOBL_FIGT_01", "GOBL_FIGT_05", "GOBL_FIGT_05", "GOBL_FIGT_05", "GOBL_FIGT_05"]),
    ("Elven", (56, 58, 62), ["ELVN_WARR_05", "ELVN_WARR_05", "ELVN_WARR_03", "ELVN_WARR_01", "ELVN_WARR_05"]),
    ("Warlord", (52, 40, 38), ["VIKG_WARR_10", "GOBL_FIGT_01", "GOBL_FIGT_01", "GOBL_FIGT_02", "GOBL_FIGT_01"]),
    ("Verdant", (34, 56, 36), ["FANT_SKTN_03", "ELVN_WARR_04", "ELVN_WARR_03", "ELVN_WARR_03", "ELVN_WARR_04"]),
    ("Shadow", (30, 34, 52), ["FANT_SORC_08", "FANT_SORC_02", "FANT_SORC_02", "FANT_SORC_02", "FANT_SORC_02"]),
    ("Grave", (28, 34, 30), ["FANT_SKTN_04", "FANT_SKTN_01", "FANT_SKTN_01", "GOBL_FIGT_01", "FANT_SKTN_05"]),
    ("Blackened", (24, 24, 28), ["VIKG_WARR_05", "FANT_SKTN_05", "VIKG_WARR_05", "VIKG_WARR_05", "VIKG_WARR_05"]),
    ("Gilded", (30, 70, 80), ["ELVN_WARR_01", "ELVN_WARR_01", "ELVN_WARR_01", "ELVN_WARR_02", "ELVN_WARR_01"]),
    ("Dread", (36, 26, 40), ["FANT_SKTN_02", "FANT_SKTN_02", "FANT_SKTN_02", "FANT_SKTN_02", "FANT_SKTN_02"]),
    ("Royal", (40, 48, 80), ["ELVN_WARR_04", "ELVN_WARR_02", "ELVN_WARR_02", "ELVN_WARR_02", "ELVN_WARR_02"]),
]

def palette(preset):
    folder, fmt = PACKS[preset[:9]]
    for n in (int(preset[-2:]), 1):
        path = os.path.join(SK, folder, fmt.format(n), "Textures", f"T_{fmt.format(n)}ColorMap.png")
        if os.path.exists(path): return path
    raise FileNotFoundError(preset)

out = sys.argv[1]
os.makedirs(out, exist_ok=True)
for tier, pad, presets in TIERS:
    for slot, preset in zip(SLOTS, presets):
        im = Image.open(palette(preset)).convert("RGB")
        px = im.load()
        for u, v in SKIN: px[u, 31 - v] = pad
        im.save(os.path.join(out, f"{tier} {slot} palette.png"))
        if slot == "Armor": im.save(os.path.join(out, f"{tier} palette.png"))
    print("wrote", tier)

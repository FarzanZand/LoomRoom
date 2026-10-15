"""Room furniture wood: square tiling textures of real painted planks, made like the bedroom's tabletop (planks cut out
of a painting in Temp/Paint/Room/gen and laid end to end in rows with a dark gap). Each kind of wood has its own
painting, colour, plank width and board length, so the pieces in the room don't all look alike; each also has a
lighter "fronts" grade for doors and drawers. Planks run along u; RoomTiles projects them along each piece's long side
at the room's 6.4 texels per unit.

    python Tools/furniture_wood.py [out dir]
"""
import importlib.util
import random
import sys
from pathlib import Path

here = Path(__file__).parent
spec = importlib.util.spec_from_file_location("room_surfaces", here / "room_surfaces.py")
rs = importlib.util.module_from_spec(spec)
argv, sys.argv = sys.argv, sys.argv[:1]
spec.loader.exec_module(rs)
sys.argv = argv
rs.OUT = Path(sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/World/Art/Bedroom/Furniture")

# name: painting, block size down to texels, grade (saturation, brightness, tint), plank width in texels, rows
WOODS = {
    "Walnut":   ("Table_top", 8, (.80, .62, (1.00, .93, .88)), 16, 7),   # dark, cool brown (wardrobe)
    "Cherry":   ("Table_top", 8, (1.05, .78, (1.06, .88, .80)), 12, 9),  # red-brown, narrow boards (cabinets, nightstand)
    "Smoked oak": ("Floorboards", 5, (.55, .55, (.95, .92, .90)), 14, 8),  # grey-brown, weathered (hobby desk)
    "Mahogany": ("Table_top", 8, (1.10, .58, (1.12, .82, .78)), 20, 6),  # deep red, broad boards (the DM's chair)
}
FRONTS = 1.22   # doors and drawers: the same wood, a lighter grade

rng = random.Random(5)
for name, (src, block, (sat, bright, tint), plank, rows) in WOODS.items():
    base = rs.down(rs.load(src), block)
    size = rows * (plank + 1)
    for suffix, b in (("", bright), (" fronts", bright * FRONTS)):
        pieces, gap, _ = rs.plank_pieces(rs.grade(base, sat=sat, bright=b, tint=tint))
        rs.save(rs.lay_rows(pieces, gap, plank, size, rows, rng, min_len=size // 2, max_len=size, tone=.07), f"Wood {name.lower()}{suffix}")

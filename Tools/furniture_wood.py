"""Room furniture wood: square tiling textures of real painted boards, cut out of a painting in Temp/Paint/Room/gen
like the bedroom's tabletop, but laid as furniture is built rather than as a floor: every board runs the tile's whole
length (one end join per row, somewhere along it, so no brickwork of short planks), and the joins between boards are
a darker shade of the wood, not black gaps. Each kind of wood has its own painting, colour and board width, so the
pieces in the room don't look alike; each also has a clearly lighter "fronts" grade for doors and drawers. Boards run
along u; RoomTiles projects them along each piece's long side at the room's 6.4 texels per unit.

    python Tools/furniture_wood.py [out dir]
"""
import importlib.util
import random

import numpy as np
import sys
from pathlib import Path

here = Path(__file__).parent
spec = importlib.util.spec_from_file_location("room_surfaces", here / "room_surfaces.py")
rs = importlib.util.module_from_spec(spec)
argv, sys.argv = sys.argv, sys.argv[:1]
spec.loader.exec_module(rs)
sys.argv = argv
rs.OUT = Path(sys.argv[1] if len(sys.argv) > 1 else "Assets/Game/World/Art/Bedroom/Furniture")

# name: painting, block size down to texels, grade (saturation, brightness, tint), board width in texels, rows
WOODS = {
    "Walnut":   ("Table_top", 8, (.60, .80, (.98, .94, .92)), 18, 7),   # chocolate brown, cool (wardrobe)
    "Cherry":   ("Table_top", 8, (.90, .92, (1.04, .88, .76)), 16, 8),  # brown cherry, warm (cabinets, nightstand)
    "Smoked oak": ("Floorboards", 5, (.35, .78, (.97, .96, .95)), 18, 7),  # grey-brown, weathered (hobby desk)
    "Mahogany": ("Table_top", 8, (1.00, .85, (1.10, .84, .78)), 22, 6),  # red-brown, broad boards (the DM's chair, the trophy backings)
}
FRONTS = 1.6    # doors and drawers: the same wood, a clearly lighter grade (it must still show under the room's light)
JOIN = .62      # the line between two boards: the wood itself, this much darker



def long_boards(pieces, join, plank, size, rows, rng, tone=.05):
    """Rows of single boards, each one painted plank drawn out along its grain to the whole row (grain is long, so
    stretching it along x keeps it wood), ends trimmed so no painted end shading shows; one join per row, at the
    tile's wrap, turned to land anywhere along the row."""
    out = np.zeros((rows * (plank + 1), size, 3), np.float32)
    for r in range(rows):
        p = rs.fit_height(pieces[rng.randrange(len(pieces))], plank)[:, 3:-3]
        if rng.random() < .5:
            p = p[:, ::-1]
        idx = np.minimum((np.arange(size - 1) * p.shape[1] / (size - 1)).astype(int), p.shape[1] - 1)
        row = np.concatenate([p[:, idx], np.broadcast_to(join, (plank, 1, 3))], axis=1)
        row = np.roll(row * (1 + (rng.random() - .5) * 2 * tone), rng.randrange(size), axis=1)
        y = r * (plank + 1)
        out[y:y + plank] = row
        out[y + plank] = join
    return out


rng = random.Random(5)
for name, (src, block, (sat, bright, tint), plank, rows) in WOODS.items():
    base = rs.down(rs.load(src), block)
    size = rows * (plank + 1)
    for suffix, b in (("", bright), (" fronts", bright * FRONTS)):
        pieces, _, _ = rs.plank_pieces(rs.grade(base, sat=sat, bright=b, tint=tint))
        join = np.median(np.concatenate([p.reshape(-1, 3) for p in pieces]), axis=0) * JOIN
        rs.save(long_boards(pieces, join, plank, size, rows, rng), f"Wood {name.lower()}{suffix}")

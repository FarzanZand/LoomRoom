using UnityEngine;

// The tavern is always indoors. On a walled floor it keeps its own walls and ceiling (DungeonGenerator, Wilds); on a board
// (the forests) it gets the same: walls of its own style round its tiles, a doorway in them at each way in (its door's
// lintel, MakeDoors) and a ceiling, under a hill of the wild ground (DungeonGenerator.Wilds), so what hangs on its walls
// never stands in the open and its plaster is only seen from inside.
public partial class DungeonGenerator
{
    // The room the tavern stands in: on a board only when the shop room is built (not a keeper in a plain room).
    bool IndoorTavern(int region) => region >= 0 && region == MerchantRoom && (!Board || MerchantRoom > 0 && TemplatePrefab(MerchantRoom) != null);

    // Height of the tavern's ceiling's top over the board's foot: the hill over it starts there.
    float TavernRoofTop(Vector2Int c) => BoardElevation(c) + FloorTop + HeightAt(c.x, c.y) + .24f;

    // The lintel over the tavern's way in on a board, seen from the passage, is the banks' earth: the room's half of its
    // depth keeps the tavern's wall, the outer half is a box of the banks' face (two boxes side by side, never one face
    // over another).
    void TavernLintelOutside(GameObject lintel, Vector2Int dir)
    {
        var wilds = WildSettings;
        var earth = wilds != null && wilds.side != null ? wilds.side : KitEdge;
        if (earth == null) return;
        var outward = new Vector3(dir.x, 0f, dir.y);
        var size = lintel.transform.localScale;
        float half = WallThickness * .5f - .005f;
        var inner = dir.x != 0 ? new Vector3(half, size.y, size.z) : new Vector3(size.x, size.y, half);
        var centre = lintel.transform.position;
        var parent = lintel.transform.parent;
        DestroyImmediate(lintel);
        ArchitectureBox("Styled door lintel", centre - outward * (half * .5f), inner, WallMaterial(MerchantRoom, 0f), parent);
        var outer = ArchitectureBox("Door lintel (earth)", centre + outward * (half * .5f), inner, earth, parent);
        DestroyImmediate(outer.GetComponent<Collider>());
    }

    void BuildTavernShell(int x, int z)
    {
        var cell = new Vector2Int(x, z);
        int room = Layout.RegionIds[x, z];
        var style = RegionStyles[room];
        float height = HeightAt(x, z), floor = FloorTop, size = data.cellSize, tile = data.architectureTileSize;
        var pos = Cell(cell) + Vector3.up * floor;
        var ceilingMaterial = DungeonRoomStyle.Resolve(style != null ? style.ceiling : null, data.ceilingMaterial);
        ArchitectureBox("Ceiling", pos + Vector3.up * (height + .12f), new Vector3(size, .24f, size), ceilingMaterial, ceiling);
        var trim = DungeonRoomStyle.Resolve(style != null ? style.trim : null, data.trimMaterial);
        foreach (var dir in DungeonLayout.Neighbours)
        {
            // A way in (a passage's floor) is left open for the door; inside the tavern there is no wall.
            if (Open(cell + dir)) continue;
            for (float y = 0f; y < height - .01f; y += tile)
            {
                var (centre, scale) = WallPiece(cell, dir, y, 0, tile, floor + y + tile * .5f);
                var wall = ArchitectureBox("Tavern wall", centre, scale, WallSurface(room, y, x, z, dir), boardRegions[room]);
                MiterDiagonalEnds(wall, cell, dir, y, 0, true);
            }
            // Up past the ceiling to its top, where the hill over it starts: no gap between wall and hill shows from outside.
            var (topCentre, topScale) = WallPiece(cell, dir, height - .1f, 0, .24f, floor + height + .12f);
            var wallTop = ArchitectureBox("Tavern wall top", topCentre, topScale, WallSurface(room, Mathf.Max(0f, height - tile), x, z, dir), boardRegions[room]);
            MiterDiagonalEnds(wallTop, cell, dir, height - .1f, 0, true);
            if (style != null && style.hideTrims) continue;
            var (corniceCentre, corniceScale) = WallPiece(cell, dir, height - .6f, TrimDepth, .15f, floor + height - .55f);
            MiterDiagonalEnds(Box("Stone cornice", corniceCentre, corniceScale, trim, boardRegions[room]), cell, dir, height - .6f, TrimDepth, false);
            var (footCentre, footScale) = WallPiece(cell, dir, 0, TrimDepth, .15f, floor + .12f);
            MiterDiagonalEnds(Box("Stone footing", footCentre, footScale, trim, boardRegions[room]), cell, dir, 0, TrimDepth, false);
        }
    }
}

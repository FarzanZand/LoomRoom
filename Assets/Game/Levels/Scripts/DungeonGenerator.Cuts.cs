using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

// Holes in a hand-built room's floor (DungeonFloorCut): pits and sunken courts. Built in PlaceTemplate, before the
// architecture is combined and the NavMesh is baked: the floor tiles in the cut go, the sides are lined with the room's
// wall stone down to the bottom, and the bottom gets the room's floor (a pit's is never walked: enemies keep out).
public partial class DungeonGenerator
{
    // Cells taken by cuts, per room (furnishing keeps off them).
    readonly HashSet<Vector2Int> cutCells = new();

    void BuildCuts(GameObject instance, int index)
    {
        foreach (var cut in instance.GetComponentsInChildren<DungeonFloorCut>(true))
        {
            if (cut == null) continue;
            // A board's floor is printed tiles on the table. A pit takes its tiles away down to the table; a sunken floor
            // is left out (every tile edge on a board must meet its neighbour's height), so the room keeps its floor there.
            if (Board && cut.kind != DungeonFloorCut.Kind.Pit) { DestroyImmediate(cut); continue; }
            var cells = new HashSet<Vector2Int>();
            foreach (var p in cut.CellCentres(data.cellSize)) { var c = CellOf(p); if (Layout.InRoom(index, c)) cells.Add(c); }
            if (cells.Count == 0) { DestroyImmediate(cut.gameObject); continue; }
            cutCells.UnionWith(cells);
            float floorY = Cell(Layout.RoomCenter(index)).y;
            if (Board)
            {
                BoardPit(cells);
                // The walking surface is the tiles' top, and the drop is only down to the table: a fall counts once the
                // player is halfway down it.
                float top = floorY + TileThickness;
                cut.Initialize(this, cells, top, Mathf.Min(1.2f, top - transform.position.y) * .5f);
                continue;
            }
            // The floor tiles in the cut.
            for (int i = geometry.childCount - 1; i >= 0; i--)
            {
                var t = geometry.GetChild(i);
                if (t.name == "Flagstone" && cells.Contains(CellOf(t.position))) DestroyImmediate(t.gameObject);
            }
            float size = data.cellSize, depth = Mathf.Min(cut.depth, Mathf.Max(.2f, data.floorBase - .1f));
            var parent = new GameObject(cut.kind == DungeonFloorCut.Kind.Pit ? "Pit" : "Sunken floor").transform;
            parent.SetParent(geometry, false);
            foreach (var c in cells)
            {
                var centre = Cell(c);
                // Its sides: the room's lower wall stone, from the floor down to the bottom.
                foreach (var d in DungeonLayout.Neighbours)
                {
                    if (cells.Contains(c + d)) continue;
                    var outward = new Vector3(d.x, 0f, d.y);
                    var wallSize = d.x != 0 ? new Vector3(WallThickness, depth, size + WallThickness * 2f) : new Vector3(size + WallThickness * 2f, depth, WallThickness);
                    // Its inner face a little inside the hole, clear of the next floor column's side.
                    ArchitectureBox("Cut side", centre + outward * (size * .5f + WallThickness * .5f - .02f) + Vector3.down * (depth * .5f + .001f), wallSize,
                        WallSurface(index, 0f, c.x, c.y, -d), parent);
                }
                var bottom = ArchitectureBox(cut.kind == DungeonFloorCut.Kind.Pit ? "Pit floor" : "Sunken flagstone", centre + Vector3.down * (depth + .12f),
                    new Vector3(size, .24f, size), FloorMaterial(index, c.x, c.y), parent);
                if (cut.kind == DungeonFloorCut.Kind.Pit)
                {
                    // Not walkable: no path ever leads down there.
                    var modifier = bottom.AddComponent<NavMeshModifier>();
                    modifier.overrideArea = true; modifier.area = 1;
                }
            }
            cut.Initialize(this, cells, floorY);
        }
    }

    // A pit on a board: its printed tiles are taken away and a dark card lies on the table in the hole, which no path
    // crosses. It belongs to the room's region, so it is dealt in with the room.
    void BoardPit(HashSet<Vector2Int> cells)
    {
        var material = data.boardPitMaterial != null ? data.boardPitMaterial : data.boardEdgeMaterial;
        foreach (var c in cells)
        {
            int region = Layout.RegionIds[c.x, c.y];
            var root = region >= 0 && region < boardRegions.Length ? boardRegions[region] : null;
            if (root == null) continue;
            var tile = root.Find($"Tile {c.x},{c.y}");
            if (tile != null) DestroyImmediate(tile.gameObject);
            var centre = Cell(c); centre.y = transform.position.y;
            var bottom = ArchitectureBox("Pit floor", centre + Vector3.up * .01f, new Vector3(data.cellSize, .02f, data.cellSize), material, root);
            bottom.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var modifier = bottom.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true; modifier.area = 1;
        }
    }
}

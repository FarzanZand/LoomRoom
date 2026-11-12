using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

// Holes in a hand-built room's floor (DungeonFloorCut): pits and sunken courts. Built in PlaceTemplate, before the
// architecture is combined and the NavMesh is baked: the floor tiles in the cut go, the sides are lined with the room's
// wall stone down to the bottom, and the bottom gets the room's floor (a pit's is never walked: enemies keep out).
// A walled floor lies on the table, so the hole goes down into the table: TableCut opens the table under each cell
// (TableHoles; the reveal opens them as the rooms are built), and a hole is never deeper than the table is solid there.
public partial class DungeonGenerator
{
    // Cells taken by cuts, per room (furnishing keeps off them).
    readonly HashSet<Vector2Int> cutCells = new();
    // The table under each cut cell (TableCut), open while this floor is on the table.
    readonly List<TableCut.Hole> tableHoles = new();
    public List<TableCut.Hole> TableHoles => tableHoles;

    // True when every way into the room can still be walked to from the others with the hole (and the room's earlier cuts)
    // taken out of its floor, and no way in opens straight into the hole.
    bool KeepsWaysJoined(int room, HashSet<Vector2Int> hole)
    {
        var open = new HashSet<Vector2Int>();
        var ways = new List<Vector2Int>();
        foreach (var c in Layout.RoomCells(room))
        {
            bool way = false;
            foreach (var d in DungeonLayout.Neighbours) if (Layout.IsOpening(room, c, d)) { way = true; break; }
            if (hole.Contains(c) || cutCells.Contains(c)) { if (way) return false; continue; }
            open.Add(c);
            if (way) ways.Add(c);
        }
        if (ways.Count < 2) return true;
        var seen = new HashSet<Vector2Int> { ways[0] };
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(ways[0]);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var d in DungeonLayout.Neighbours) { var n = c + d; if (open.Contains(n) && seen.Add(n)) queue.Enqueue(n); }
        }
        foreach (var c in ways) if (!seen.Contains(c)) return false;
        return true;
    }

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
            // A pit that would cut the room's ways in off from each other (a passage coming in beyond it) is left out: the
            // floor past it could not be reached (a tavern's only passage led into a broken floor's far side).
            if (cut.kind == DungeonFloorCut.Kind.Pit && !KeepsWaysJoined(index, cells)) { DestroyImmediate(cut.gameObject); continue; }
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
            float size = data.cellSize, depth = cut.depth;
            // The bottom slab and a little wood must stay under the hole, or it would show through the table.
            foreach (var c in cells) depth = Mathf.Min(depth, TableCut.SolidDepth(CellRect(c), floorY) - .24f - .1f);
            depth = Mathf.Max(.2f, depth);
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
            if (cut.kind == DungeonFloorCut.Kind.Pit) { if (cut.fill != null) PitFill(cut, cells, floorY, depth, parent); else PitWater(cells, floorY, depth, parent); }
            foreach (var c in cells) tableHoles.Add(new TableCut.Hole { area = CellRect(c) });
            // A sliced build cuts the table once it is done (the map may still be folding away over it).
            if (!slicing) TableCut.Set(this, tableHoles);
            cut.Initialize(this, cells, floorY);
        }
    }

    // The stage biome's pit water (DungeonBiome.pitWater): a sheet over each pit cell a little under the floor, no
    // collider (the pit's edge still holds the player back, and what falls in goes down through it), no shadow.
    void PitWater(HashSet<Vector2Int> cells, float floorY, float depth, Transform parent)
    {
        var biome = StageBiome;
        if (biome == null || biome.pitWater == null) return;
        float y = floorY - Mathf.Min(biome.pitWaterBelow, depth - .05f);
        foreach (var c in cells)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Quad); water.name = "Pit water";
            DestroyImmediate(water.GetComponent<Collider>());
            water.transform.SetParent(parent, false);
            var centre = Cell(c);
            water.transform.position = new Vector3(centre.x, y, centre.z);
            water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            water.transform.localScale = new Vector3(data.cellSize, data.cellSize, 1f);
            var r = water.GetComponent<Renderer>(); r.sharedMaterial = biome.pitWater; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    // A pit's own fill (DungeonFloorCut.fill: the duel bridge's lava): one sheet over its cells a little under the floor,
    // its UVs its world position over Fill Repeat so cells and pits meet without a seam; no collider (the edge holds the
    // player back, what falls in goes through), no shadow. Its lights are the pit's children, spread along its long side.
    void PitFill(DungeonFloorCut cut, HashSet<Vector2Int> cells, float floorY, float depth, Transform parent)
    {
        float y = floorY - Mathf.Min(cut.fillBelow, depth - .05f), half = data.cellSize * .5f, repeat = Mathf.Max(.5f, cut.fillRepeat);
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        var sheet = new GameObject("Pit fill");
        sheet.transform.SetParent(parent, false);
        sheet.transform.position = new Vector3(transform.position.x, y, transform.position.z);
        Vector3 min = new(float.MaxValue, 0f, float.MaxValue), max = new(float.MinValue, 0f, float.MinValue);
        foreach (var c in cells)
        {
            var centre = Cell(c); int start = verts.Count;
            foreach (var (dx, dz) in new[] { (-1f, -1f), (-1f, 1f), (1f, 1f), (1f, -1f) })
            {
                var world = new Vector3(centre.x + dx * half, y, centre.z + dz * half);
                verts.Add(sheet.transform.InverseTransformPoint(world));
                uvs.Add(new Vector2(world.x, world.z) / repeat);
                min = Vector3.Min(min, world); max = Vector3.Max(max, world);
            }
            tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        var mesh = new Mesh { name = "Pit fill" };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        sheet.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = sheet.AddComponent<MeshRenderer>(); r.sharedMaterial = cut.fill; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        cut.OwnFillMesh(mesh);
        if (cut.fillLightIntensity <= 0f) return;
        // Along the pit's longer side, evenly, a little over the floor (lower, they lit only the fill and not the pit's sides
    // and the walls round it).
        var along = max.x - min.x >= max.z - min.z ? Vector3.right : Vector3.forward;
        float length = Vector3.Dot(max - min, along);
        int count = Mathf.Max(1, Mathf.RoundToInt(length / data.cellSize / cut.fillLightEvery));
        var middle = (min + max) * .5f;
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Pit fill light");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(middle.x, floorY + .8f, middle.z) + along * ((i + .5f) / count - .5f) * length;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point; light.color = cut.fillLightColour; light.intensity = cut.fillLightIntensity; light.range = cut.fillLightRange;
            light.shadows = LightShadows.None;
            var flicker = go.AddComponent<TorchFlicker>(); flicker.amount = .12f; flicker.speed = 1.6f;
        }
    }

    Rect CellRect(Vector2Int c)
    {
        var centre = Cell(c); float half = data.cellSize * .5f;
        return Rect.MinMaxRect(centre.x - half, centre.z - half, centre.x + half, centre.z + half);
    }

    // A pit on a board: its printed tiles are taken away and a dark card lies on the table in the hole, which no path
    // crosses. It belongs to the room's region, so it is dealt in with the room.
    void BoardPit(HashSet<Vector2Int> cells)
    {
        var material = KitPit != null ? KitPit : KitEdge;
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

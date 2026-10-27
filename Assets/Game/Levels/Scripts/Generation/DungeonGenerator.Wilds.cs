using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;

// Wild ground round a board floor (DungeonBiome.wilds): every cell of the table within Depth of a room or path becomes a
// bank of earth, rising from a little over head height at the floor's edge toward the back, its faces a cliff of earth,
// its top under trees and undergrowth, with roots and moss at its foot and glowing things along it. Nothing on the banks
// is walked: they keep their colliders (the floor's edge) but no path is laid over them. Ponds: a few cells cut out of a
// room away from its ways through, down into the table, with water standing in them (the board's pit water).
public partial class DungeonGenerator
{
    Transform wildsRoot;
    public Transform Wilds => wildsRoot;
    // A prefab's size as it is set down (its own scale), measured once.
    readonly Dictionary<GameObject, Bounds> prefabSizes = new();
    Bounds SizeOf(GameObject prefab)
    {
        if (prefabSizes.TryGetValue(prefab, out var b)) return b;
        bool any = false;
        foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var wb = GeometryUtility.CalculateBounds(new[] { mb.min, mb.max }, mf.transform.localToWorldMatrix);
            if (!any) { b = wb; any = true; } else b.Encapsulate(wb);
        }
        prefabSizes[prefab] = b;
        return b;
    }
    // The walking surface over a cell's ground: a board tile's top, or a walled floor's flagstones (their top at the cell).
    float FloorTop => Board ? TileThickness : 0f;
    BoardWilds WildSettings => Biome != null && Biome.wilds != null && Biome.wilds.enabled ? Biome.wilds : null;

    // ── Ponds ─────────────────────────────────────────────────────────

    void CutPonds()
    {
        var wilds = WildSettings;
        if (wilds == null || wilds.pondChance <= 0f || KitWater == null) return;
        var rng = new System.Random(unchecked(seed0 * 7 + 4241));
        for (int room = 1; room < Layout.rooms.Count; room++)
        {
            bool roll = rng.NextDouble() < wilds.pondChance;   // drawn for every room, so rooms keep their rolls
            int w = rng.Next(wilds.pondSize.x, wilds.pondSize.y + 1), d = rng.Next(wilds.pondSize.x, wilds.pondSize.y + 1);
            int pick = rng.Next(1 << 20);
            if (!roll || Roles[room] == RoomRole.Exit || room == MerchantRoom || TemplateOf(room) != null) continue;
            var cells = PondSpot(room, w, d, pick) ?? PondSpot(room, d, w, pick) ?? PondSpot(room, Mathf.Max(1, w - 1), Mathf.Max(1, d - 1), pick);
            if (cells != null) Pond(room, cells, wilds, rng);
        }
    }

    // A w x d block of the room's cells off its ways through, with floor all round it and the rest of the room still in
    // one piece. One of the fitting spots, by pick; null when none fits.
    HashSet<Vector2Int> PondSpot(int room, int w, int d, int pick)
    {
        var inRoom = new HashSet<Vector2Int>(Layout.RoomCells(room));
        var lanes = Lanes(room);
        var spots = new List<HashSet<Vector2Int>>();
        var rect = Layout.rooms[room];
        for (int x = rect.xMin; x <= rect.xMax - w; x++)
            for (int z = rect.yMin; z <= rect.yMax - d; z++)
            {
                var cells = new HashSet<Vector2Int>();
                bool ok = true;
                for (int i = 0; i < w && ok; i++) for (int j = 0; j < d && ok; j++)
                {
                    var c = new Vector2Int(x + i, z + j);
                    if (!inRoom.Contains(c) || lanes.Contains(c) || cutCells.Contains(c)) ok = false;
                    else cells.Add(c);
                }
                if (!ok) continue;
                // A ring of floor round it: water never meets a bank or a passage.
                foreach (var c in cells)
                    for (int dx = -1; dx <= 1 && ok; dx++) for (int dz = -1; dz <= 1 && ok; dz++)
                    {
                        var n = c + new Vector2Int(dx, dz);
                        if (!cells.Contains(n) && !inRoom.Contains(n)) ok = false;
                    }
                if (ok && StaysWhole(inRoom, cells)) spots.Add(cells);
            }
        return spots.Count > 0 ? spots[pick % spots.Count] : null;
    }

    static bool StaysWhole(HashSet<Vector2Int> room, HashSet<Vector2Int> taken)
    {
        Vector2Int? start = null; int left = 0;
        foreach (var c in room) if (!taken.Contains(c)) { left++; start ??= c; }
        if (start == null) return false;
        var seen = new HashSet<Vector2Int> { start.Value };
        var queue = new Queue<Vector2Int>(); queue.Enqueue(start.Value);
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var dir in DungeonLayout.Neighbours)
            {
                var n = c + dir;
                if (room.Contains(n) && !taken.Contains(n) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return seen.Count == left;
    }

    const float PondDepth = .9f, WaterBelow = .32f;

    // The room's tiles there go; the hole goes down into the table (TableCut), lined with the bank's earth, a bed of the
    // pit card at the bottom and water standing a little under the tiles' top. It is filed under the room, so it is dealt
    // in with it, and a DungeonFloorCut keeps the player out and lets enemies knocked into it drown.
    void Pond(int room, HashSet<Vector2Int> cells, BoardWilds wilds, System.Random rng)
    {
        var parent = new GameObject("Pond").transform;
        parent.SetParent(boardRegions[room], false);
        float table = transform.position.y, size = data.cellSize;
        float depth = PondDepth;
        foreach (var c in cells) depth = Mathf.Min(depth, TableCut.SolidDepth(CellRect(c), table) - .15f);
        depth = Mathf.Max(.25f, depth);
        var side = wilds.side != null ? wilds.side : KitEdge;
        var bed = KitPit != null ? KitPit : side;
        foreach (var c in cells)
        {
            var tile = boardRegions[room].Find($"Tile {c.x},{c.y}");
            if (tile != null) DestroyImmediate(tile.gameObject);
            var centre = Cell(c); centre.y = table;
            foreach (var d in DungeonLayout.Neighbours)
            {
                if (cells.Contains(c + d)) continue;
                var outward = new Vector3(d.x, 0f, d.y);
                var wallSize = d.x != 0 ? new Vector3(WallThickness, depth, size + WallThickness * 2f) : new Vector3(size + WallThickness * 2f, depth, WallThickness);
                ArchitectureBox("Pond bank", centre + outward * (size * .5f + WallThickness * .5f - .02f) + Vector3.down * (depth * .5f + .001f), wallSize, side, parent);
            }
            var bottom = ArchitectureBox("Pond bed", centre + Vector3.down * (depth + .05f), new Vector3(size, .1f, size), bed, parent);
            var modifier = bottom.AddComponent<NavMeshModifier>();
            modifier.overrideArea = true; modifier.area = 1;
            tableHoles.Add(new TableCut.Hole { area = CellRect(c) });
        }
        // One sheet of water over the whole pond, so it reads as one surface.
        var min = new Vector2(float.MaxValue, float.MaxValue); var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var c in cells) { var p = Cell(c); min = Vector2.Min(min, new Vector2(p.x, p.z)); max = Vector2.Max(max, new Vector2(p.x, p.z)); }
        float top = Cell(Layout.RoomCenter(room)).y + TileThickness;
        var water = GameObject.CreatePrimitive(PrimitiveType.Quad); water.name = "Water";
        DestroyImmediate(water.GetComponent<Collider>());
        water.transform.SetParent(parent, false);
        water.transform.position = new Vector3((min.x + max.x) * .5f, top - WaterBelow, (min.y + max.y) * .5f);
        water.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        water.transform.localScale = new Vector3(max.x - min.x + size, max.y - min.y + size, 1f);
        var wr = water.GetComponent<Renderer>(); wr.sharedMaterial = KitWater; wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // It glows: a soft light just over the water (the pond's own child, so it is dealt in with it).
        if (wilds.pondGlowStrength > 0f)
        {
            var glow = new GameObject("Pond glow").AddComponent<Light>();
            glow.transform.SetParent(parent, false);
            glow.transform.position = water.transform.position + Vector3.up * .5f;
            glow.type = LightType.Point; glow.color = wilds.pondGlow; glow.intensity = wilds.pondGlowStrength;
            glow.range = Mathf.Max(max.x - min.x, max.y - min.y) + size * 2.5f; glow.shadows = LightShadows.None;
        }
        TableCut.Set(this, tableHoles);
        cutCells.UnionWith(cells);
        var cut = parent.gameObject.AddComponent<DungeonFloorCut>();
        cut.kind = DungeonFloorCut.Kind.Pit;
        var features = Biome != null ? Biome.RoomFeatures(data) : null;
        cut.keepPlayerOut = features == null || !features.playerFallsInPits;
        cut.Initialize(this, cells, top, Mathf.Min(1.2f, top - table + depth) * .5f);
        // Its edge: reeds, stones and roots on the floor round it, off the ways through.
        if (wilds.pondEdge == null || wilds.pondEdge.Length == 0) return;
        var lanes = Lanes(room);
        foreach (var c in cells)
            foreach (var d in DungeonLayout.Neighbours)
            {
                var n = c + d;
                if (cells.Contains(n) || lanes.Contains(n) || rng.NextDouble() > .5) continue;
                var prefab = DungeonWeightedPrefab.Choose(wilds.pondEdge, rng);
                if (prefab == null) continue;
                var at = Cell(c) + new Vector3(d.x, 0f, d.y) * (size * .5f + .15f) + Vector3.up * TileThickness;
                at += new Vector3(d.y, 0f, d.x) * (float)(rng.NextDouble() - .5) * size * .8f;
                Instantiate(prefab, at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), parent);
                furnished.Add(n);
            }
    }

    // ── Banks ─────────────────────────────────────────────────────────

    void BuildWilds()
    {
        var wilds = WildSettings;
        if (wilds == null) return;
        var rng = new System.Random(unchecked(seed0 * 13 + 90121));
        int depth = Mathf.Clamp(wilds.depth, 1, 10);
        int w = data.width, d = data.depth;
        // Cells out to Depth past the grid's edge too: a room at the edge of the grid still stands in the woods.
        int W = w + depth * 2, D = d + depth * 2;
        var dist = new int[W, D];
        var foot = new float[W, D];   // the top of the floor the bank stands beside
        var owner = new int[W, D];    // the region of the floor the bank stands beside (walled floors take its wall style)
        var reach = new float[W, D];  // the highest ceiling beside it (a roofed bank always reaches it)
        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < W; x++) for (int z = 0; z < D; z++) dist[x, z] = int.MaxValue;
        bool IsFloor(int gx, int gz) => gx >= 0 && gz >= 0 && gx < w && gz < d && Layout.floor[gx, gz];
        for (int x = 0; x < w; x++) for (int z = 0; z < d; z++)
            if (Layout.floor[x, z])
            {
                dist[x + depth, z + depth] = 0;
                foot[x + depth, z + depth] = BoardElevation(new Vector2Int(x, z)) + FloorTop;
                owner[x + depth, z + depth] = Layout.RegionIds[x, z];
                reach[x + depth, z + depth] = Board ? 0f : HeightAt(x, z);
                queue.Enqueue(new Vector2Int(x + depth, z + depth));
            }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (dist[c.x, c.y] >= depth) continue;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                var n = new Vector2Int(c.x + dx, c.y + dz);
                if (n.x < 0 || n.y < 0 || n.x >= W || n.y >= D) continue;
                if (dist[n.x, n.y] <= dist[c.x, c.y]) continue;
                // Reached already at this distance from another floor: it stands beside both, so it takes the higher
                // floor and the taller ceiling.
                if (dist[n.x, n.y] == dist[c.x, c.y] + 1)
                {
                    foot[n.x, n.y] = Mathf.Max(foot[n.x, n.y], foot[c.x, c.y]);
                    reach[n.x, n.y] = Mathf.Max(reach[n.x, n.y], reach[c.x, c.y]);
                    // Beside the tavern and a passage at once: the passage's wall (the tavern has walls of its own).
                    if (!Board && MerchantRoom >= 0 && owner[n.x, n.y] == MerchantRoom && owner[c.x, c.y] != MerchantRoom) owner[n.x, n.y] = owner[c.x, c.y];
                    continue;
                }
                dist[n.x, n.y] = dist[c.x, c.y] + 1;
                foot[n.x, n.y] = foot[c.x, c.y];
                owner[n.x, n.y] = owner[c.x, c.y];
                reach[n.x, n.y] = reach[c.x, c.y];
                queue.Enqueue(n);
            }
        }

        // A mound inside a clearing stays a low mound: no bank stands on it.
        foreach (var m in moundCells) dist[m.x + depth, m.y + depth] = int.MaxValue;
        wildsRoot = new GameObject("Wilds").transform;
        wildsRoot.SetParent(board != null ? board : transform, false);
        var banks = new GameObject("Banks").transform; banks.SetParent(wildsRoot, false);
        var growth = new GameObject("Growth").transform; growth.SetParent(wildsRoot, false);
        var style = DefaultStyle;
        var top = wilds.top != null ? wilds.top : style != null ? style.floor : null;
        var side = wilds.side != null ? wilds.side : Board && KitEdge != null ? KitEdge : style != null ? style.bottomWall : null;
        float size = data.cellSize;
        // A bank's face: the room style's wall or one of its wall variants, drawn per cell as walls are. On a walled floor
        // with no Side of its own, the wall of the room or passage it stands beside (a ruin's broken masonry).
        Material SideAt(int gx, int gz)
        {
            if (!Board && wilds.side == null) return WallSurface(owner[gx, gz], 0f, gx, gz, Vector2Int.up);
            return style == null ? side : Pick(side, style.wallBaseWeight, style.wallVariants, TileRoll(gx, gz, 977), 1);
        }
        var heights = new float[W, D];
        // Heights rise from the floor's edge toward the back, with a slow swell across the board so the line of the
        // banks' tops wanders.
        float seedX = (float)rng.NextDouble() * 100f, seedZ = (float)rng.NextDouble() * 100f;
        for (int x = 0; x < W; x++) for (int z = 0; z < D; z++)
        {
            int k = dist[x, z];
            if (k == 0 || k == int.MaxValue) continue;
            float back = depth <= 1 ? 1f : (k - 1f) / (depth - 1f);
            float swell = Mathf.PerlinNoise(seedX + x * .17f, seedZ + z * .17f);
            float rise = Mathf.Lerp(wilds.height.x, wilds.height.y, Mathf.Clamp01(back * .7f + swell * .45f));
            // Whole steps of a fifth of a metre: neighbouring cells of a height merge into one face.
            heights[x, z] = foot[x, z] + Mathf.Round(rise * 5f) / 5f;
            // Under a roof the bank is the wall: it goes up past the ceiling of the tallest room beside it (a hand-built
            // hall two or three tiles high), or the room behind shows through the gap.
            if (wilds.roofed && !Board) heights[x, z] = Mathf.Max(heights[x, z], foot[x, z] + reach[x, z] + .4f);
        }
        Vector3 At(int gx, int gz) => transform.position + new Vector3((gx - depth - w * .5f) * size, 0f, (gz - depth - d * .5f) * size);
        // A walled floor's tavern has walls of its own (DungeonGenerator): a bank cell beside it stands back behind them,
        // or its face would lie in the plane of the tavern's plaster and flicker.
        bool TavernAt(int bx, int bz) { int gx = bx - depth, gz = bz - depth; return !Board && MerchantRoom >= 0 && IsFloor(gx, gz) && Layout.RegionIds[gx, gz] == MerchantRoom; }
        bool BesideTavern(int bx, int bz) => TavernAt(bx - 1, bz) || TavernAt(bx + 1, bz) || TavernAt(bx, bz - 1) || TavernAt(bx, bz + 1);
        // 3 cm: just behind the plaster, so the wall's own outer side stays buried in the bank (set back past it, it showed).
        const float BehindTavernWall = .03f;
        // Each bank cell: a body of earth from the table to its top, and a cap of its top's material. Runs of cells of one
        // height along x are one box (fewer pieces).
        for (int z = 0; z < D; z++)
        {
            int x = 0;
            while (x < W)
            {
                if (dist[x, z] == 0 || dist[x, z] == int.MaxValue) { x++; continue; }
                float h = heights[x, z];
                var face = SideAt(x, z);
                int run = 1;
                bool beside = BesideTavern(x, z);
                while (!beside && x + run < W && dist[x + run, z] != 0 && dist[x + run, z] != int.MaxValue && Mathf.Abs(heights[x + run, z] - h) < .01f && SideAt(x + run, z) == face && !BesideTavern(x + run, z)) run++;
                var centre = (At(x, z) + At(x + run - 1, z)) * .5f;
                var extent = new Vector3(size * run, 0f, size);
                if (beside)
                {
                    float l = TavernAt(x - 1, z) ? BehindTavernWall : 0f, r = TavernAt(x + 1, z) ? BehindTavernWall : 0f;
                    float b = TavernAt(x, z - 1) ? BehindTavernWall : 0f, f = TavernAt(x, z + 1) ? BehindTavernWall : 0f;
                    centre += new Vector3((l - r) * .5f, 0f, (b - f) * .5f);
                    extent = new Vector3(size - l - r, 0f, size - b - f);
                }
                // The cap sits on the body's top and rises only 1 cm over it: a thicker cap showed its top's leaves down its
                // side, a dashed line along every bank's edge.
                ArchitectureBox("Bank", centre + Vector3.up * (h * .5f), new Vector3(extent.x, h, extent.z), face, banks);
                var cap = ArchitectureBox("Bank top", centre + Vector3.up * (h - .005f), new Vector3(extent.x, .03f, extent.z), top, banks);
                DestroyImmediate(cap.GetComponent<Collider>());
                x += run;
            }
        }
        // No path goes over a bank.
        var modifier = banks.gameObject.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true; modifier.area = 1;

        // Trees and undergrowth on the tops.
        for (int x = 0; x < W; x++) for (int z = 0; z < D; z++)
        {
            int k = dist[x, z];
            if (k == 0 || k == int.MaxValue) continue;
            var centre = At(x, z); centre.y = transform.position.y + heights[x, z];
            if (wilds.trees != null && wilds.trees.Length > 0 && rng.NextDouble() < wilds.treeChance * (k == 1 ? .5f : 1f))
            {
                var prefab = DungeonWeightedPrefab.Choose(wilds.trees, rng);
                if (prefab != null)
                {
                    var at = centre + new Vector3((float)(rng.NextDouble() - .5) * size * .6f, 0f, (float)(rng.NextDouble() - .5) * size * .6f);
                    var tree = Instantiate(prefab, at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), growth);
                    tree.transform.localScale = prefab.transform.localScale * Mathf.Lerp(wilds.treeScale.x, wilds.treeScale.y, (float)rng.NextDouble());
                }
            }
            if (wilds.undergrowth == null || wilds.undergrowth.Length == 0) continue;
            int pieces = Mathf.FloorToInt(wilds.undergrowthPerCell + (float)rng.NextDouble());
            for (int i = 0; i < pieces; i++)
            {
                var prefab = DungeonWeightedPrefab.Choose(wilds.undergrowth, rng);
                if (prefab == null) continue;
                var at = centre + new Vector3((float)(rng.NextDouble() - .5) * size, 0f, (float)(rng.NextDouble() - .5) * size);
                Instantiate(prefab, at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), growth);
            }
        }

        // Along every edge where a bank meets the floor: foot pieces on the floor's edge, and now and then a glow.
        var dirs = DungeonLayout.Neighbours;
        // Formations stand out over the floor: only along a clearing's edge, never in a path or on a way in or out.
        var keepClear = new HashSet<Vector2Int>();
        // Nor on a hand-built room's spike plates (a glowing mushroom grew out of the spike hall's grates).
        var plates = new HashSet<Vector2Int>();
        foreach (var plate in GetComponentsInChildren<DungeonSpikeTrap>(true)) plates.Add(CellOf(plate.transform.position));
        for (int r = 0; r < Layout.rooms.Count; r++) keepClear.UnionWith(Lanes(r));
        for (int x = 0; x < w; x++) for (int z = 0; z < d; z++)
        {
            if (!Layout.floor[x, z]) continue;
            var cell = new Vector2Int(x, z);
            foreach (var dir in dirs)
            {
                var n = cell + dir;
                if (IsFloor(n.x, n.y)) continue;
                var outward = new Vector3(dir.x, 0f, dir.y);
                var along = new Vector3(dir.y, 0f, dir.x);
                var edge = Cell(cell) + outward * (size * .5f) + Vector3.up * FloorTop;
                // Nothing in a pond, and nothing on a way in or out of the floor.
                // Nor in a walled floor's tavern: its banks are its walls, kept bare.
                if (cutCells.Contains(cell) || plates.Contains(cell) || !Board && MerchantRoom >= 0 && Layout.RoomAt(cell) == MerchantRoom) continue;
                if (wilds.foot != null && wilds.foot.Length > 0 && rng.NextDouble() < wilds.footChance)
                {
                    var prefab = DungeonWeightedPrefab.Choose(wilds.foot, rng);
                    if (prefab != null)
                    {
                        var at = edge - outward * .12f + along * (float)(rng.NextDouble() - .5) * size * .8f;
                        // Facing out of the bank, turned a little either way.
                        var yaw = Quaternion.LookRotation(-outward) * Quaternion.Euler(0f, (float)(rng.NextDouble() - .5) * 70f, 0f);
                        Instantiate(prefab, at, yaw, growth);
                    }
                }
                int bx = n.x + depth, bz = n.y + depth;
                // A formation set into the bank's face, its foot a little under the floor, standing out over it.
                bool clearing = Layout.RoomAt(cell) >= 0 && !keepClear.Contains(cell);
                if (wilds.edges == BoardWilds.Edges.Formations && clearing && wilds.faces != null && wilds.faces.Length > 0 && rng.NextDouble() < wilds.faceChance)
                {
                    var prefab = DungeonWeightedPrefab.Choose(wilds.faces, rng);
                    if (prefab != null)
                    {
                        var at = edge + along * (float)(rng.NextDouble() - .5) * size * .6f;
                        var b = SizeOf(prefab);
                        // Its back half buried in the bank: the face of it stands Face Stand Out over the floor.
                        at += outward * (b.extents.x * .5f + b.extents.z * .5f - wilds.faceStandOut);
                        at.y -= .2f;
                        Instantiate(prefab, at, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), growth);
                    }
                }
                // On the bank's top at its edge, hanging over the face.
                if (wilds.lip != null && wilds.lip.Length > 0 && bx >= 0 && bz >= 0 && bx < W && bz < D && heights[bx, bz] > 0f && rng.NextDouble() < wilds.lipChance)
                {
                    var prefab = DungeonWeightedPrefab.Choose(wilds.lip, rng);
                    if (prefab != null)
                    {
                        var at = Cell(cell) + outward * (size * .5f + .25f) + along * (float)(rng.NextDouble() - .5) * size * .8f;
                        at.y = transform.position.y + heights[bx, bz];
                        Instantiate(prefab, at, Quaternion.LookRotation(-outward) * Quaternion.Euler(0f, (float)(rng.NextDouble() - .5) * 90f, 0f), growth);
                    }
                }
                if (wilds.glows != null && wilds.glows.Length > 0 && rng.NextDouble() < 1f / Mathf.Max(2, wilds.glowEvery))
                {
                    var prefab = DungeonWeightedPrefab.Choose(wilds.glows, rng);
                    if (prefab != null)
                    {
                        var at = edge - outward * .25f + along * (float)(rng.NextDouble() - .5) * size * .6f;
                        Instantiate(prefab, at, Quaternion.LookRotation(-outward), growth);
                    }
                }
                // Hung flat on the face: vines and ivy, their top just under the ceiling (roofed) or the bank's top, never
                // wider than the cell's stretch of face or taller than the wall.
                if (wilds.hangings != null && wilds.hangings.Length > 0 && bx >= 0 && bz >= 0 && bx < W && bz < D && heights[bx, bz] > 0f && rng.NextDouble() < wilds.hangChance)
                {
                    var prefab = DungeonWeightedPrefab.Choose(wilds.hangings, rng);
                    float slide = (float)(rng.NextDouble() - .5);
                    if (prefab != null)
                    {
                        var b = SizeOf(prefab);
                        float hangTop = wilds.roofed && !Board ? edge.y + HeightAt(cell.x, cell.y) - FloorTop : transform.position.y + heights[bx, bz];
                        hangTop -= .06f;
                        if (b.size.x <= size + .05f && b.size.y < hangTop - edge.y - .2f)
                        {
                            // The card's depth (local z) lies across the face, just off it.
                            var at = edge - outward * (.03f + b.extents.z + Mathf.Abs(b.center.z)) + along * slide * (size - b.size.x) - along * b.center.x;
                            at.y = hangTop - b.max.y;
                            Instantiate(prefab, at, Quaternion.LookRotation(-outward), growth);
                        }
                    }
                }
            }
        }
        // Growth lower than 40 cm (moss, pebbles, low roots, small ferns) casts no shadow: under the board light its
        // shadow is a few texels of the ground, but each one is drawn again for the shadow map.
        foreach (var r in growth.GetComponentsInChildren<MeshRenderer>(true))
            if (r.bounds.size.y < .4f) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}

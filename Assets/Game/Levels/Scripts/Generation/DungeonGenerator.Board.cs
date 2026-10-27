using System.Collections.Generic;
using UnityEngine;

// Board-game floors (a biome whose Kind is Board game): the same layout, rooms and content, built as printed tiles
// lying on the table instead of walled rooms. Rooms can be raised a layer or two; the short passages
// between them become small stairs. An invisible rim keeps everyone on the tiles, and BoardReveal keeps
// whatever lies behind a closed door off the table until it opens.
public partial class DungeonGenerator
{
    bool Board => data != null && data.BoardOn(FloorNumber);   // the floor's biome's Kind

    float[,] elevation;               // top of the floor at each cell's centre, above the table
    float[,] stairFrom, stairTo;      // passage cells: floor height at the edge toward each end
    Vector2Int[,] stairDir;           // passage cells: the way up the stairs (toward stairTo)
    Transform board;
    Transform[] boardRegions;
    readonly List<(DungeonDoor door, Vector2Int a, Vector2Int b)> boardDoors = new();

    public Transform[] BoardRegions => boardRegions;
    public IReadOnlyList<(DungeonDoor door, Vector2Int a, Vector2Int b)> BoardDoors => boardDoors;
    float TileThickness => Mathf.Max(.02f, data.boardTileThickness);
    // Under the player's step offset (.3), so stairs climb without jumping.
    const float MaxTreadRise = .22f;
    // Steepest a passage's stairs may climb, under the navmesh's 45 degree slope so enemies follow.
    const float MaxStairAngle = 38f;

    float BoardElevation(Vector2Int p) =>
        elevation != null && p.x >= 0 && p.y >= 0 && p.x < elevation.GetLength(0) && p.y < elevation.GetLength(1) ? elevation[p.x, p.y] : 0f;

    // Room heights by layer (authored, or seeded), varied by Board Verticality, and stairs along each passage
    // between two rooms.
    void ComputeBoardElevations(int seed)
    {
        int w = data.width, d = data.depth, rooms = Layout.rooms.Count;
        elevation = new float[w, d]; stairFrom = new float[w, d]; stairTo = new float[w, d]; stairDir = new Vector2Int[w, d];
        var raise = new float[rooms];
        var rng = new System.Random(unchecked(seed + 70001));
        for (int i = 0; i < rooms; i++)
        {
            int layers = Authored && i < data.AuthoredRoomList.Length && data.AuthoredRoomList[i] != null
                ? data.AuthoredRoomList[i].raised : i == 0 ? 0 : rng.Next(0, Mathf.Max(0, data.boardMaxRaise) + 1);
            raise[i] = layers * data.boardLayerHeight;
        }
        var dirs = DungeonLayout.Neighbours;
        // Passages: their cells and the rooms at their ends.
        var passages = new List<(int region, List<Vector2Int> cells, List<int> ends)>();
        for (int region = rooms; region < Layout.RegionCount; region++)
        {
            var cells = new List<Vector2Int>();
            var ends = new List<int>();
            for (int x = 0; x < w; x++) for (int z = 0; z < d; z++)
                if (Layout.floor[x, z] && Layout.RegionIds[x, z] == region)
                {
                    var c = new Vector2Int(x, z); cells.Add(c);
                    foreach (var dir in dirs) { int r = Layout.RoomAt(c + dir); if (r >= 0 && !ends.Contains(r)) ends.Add(r); }
                }
            if (cells.Count > 0) passages.Add((region, cells, ends));
        }
        // Passages that touch each other (a crossing, two running side by side) form one network: stairs can only run
        // along a passage that joins exactly two rooms on its own, so every room on a network is kept level and its
        // passages flat. Each passage's network: its region number maps to the network's first passage.
        var network = new Dictionary<int, int>();
        foreach (var p in passages) network[p.region] = p.region;
        int Root(int r) { while (network[r] != r) r = network[r] = network[network[r]]; return r; }
        foreach (var (region, cells, _) in passages)
            foreach (var c in cells)
                foreach (var dir in dirs)
                {
                    var n = c + dir;
                    if (!Layout.InBounds(n) || !Layout.floor[n.x, n.y]) continue;
                    int other = Layout.RegionIds[n.x, n.y];
                    if (other == region || !network.ContainsKey(other)) continue;
                    int a = Root(region), b = Root(other);
                    if (a != b) network[a] = b;
                }
        var networkEnds = new Dictionary<int, List<int>>();
        var networkSize = new Dictionary<int, int>();
        foreach (var (region, _, ends) in passages)
        {
            int r = Root(region);
            if (!networkEnds.TryGetValue(r, out var list)) networkEnds[r] = list = new List<int>();
            foreach (var e in ends) if (!list.Contains(e)) list.Add(e);
            networkSize[r] = (networkSize.TryGetValue(r, out int k) ? k : 0) + 1;
        }
        // A passage laid flat (its rooms level with it): one joining more than two rooms, one in a network, or one whose
        // stairs didn't meet a neighbouring tile (forced, below).
        var forcedFlat = new HashSet<int>();
        bool Flat(int region, List<int> ends) => ends.Count > 2 || networkSize[Root(region)] > 1 || forcedFlat.Contains(Root(region));
        var baseRaise = (float[])raise.Clone();
        // On a board every pair of neighbouring tiles is a way across (there are no walls), so the heights must meet
        // everywhere: plan them, then lay flat every passage where a tile edge doesn't meet its neighbour's, and plan
        // again until every edge meets.
        for (int attempt = 0; attempt < 12; attempt++)
        {
            System.Array.Copy(baseRaise, raise, raise.Length);
            VaryHeights(raise, new System.Random(unchecked(seed + 70003)));
            KeepStairsWalkable(raise, passages, dirs, region => Flat(region, passages.Find(p => p.region == region).ends) ? networkEnds[Root(region)] : null);
            PlanPassages(raise, passages, dirs, rooms, Flat, r => networkEnds[Root(r)]);
            var bad = MismatchedPassages(rooms);
            if (bad.Count == 0) break;
            bool added = false;
            foreach (var r in bad) added |= forcedFlat.Add(Root(r));
            if (!added) { Debug.LogWarning($"{data.name}: board heights still don't meet everywhere after levelling; check the floor's navigation."); break; }
        }
    }

    void PlanPassages(float[] raise, List<(int region, List<Vector2Int> cells, List<int> ends)> passages, Vector2Int[] dirs, int rooms,
        System.Func<int, List<int>, bool> flatPassage, System.Func<int, List<int>> levelledEnds)
    {
        int w = data.width, d = data.depth;
        for (int x = 0; x < w; x++) for (int z = 0; z < d; z++)
        {
            if (!Layout.floor[x, z]) continue;
            int region = Layout.RegionIds[x, z];
            if (region < rooms) { elevation[x, z] = stairFrom[x, z] = stairTo[x, z] = raise[region]; stairDir[x, z] = Vector2Int.zero; }
        }
        // Each passage: walking distance from either end, so the floor climbs evenly from one room to the next.
        foreach (var (region, cells, ends) in passages)
        {
            if (ends.Count < 2 || flatPassage(region, ends))
            {
                var all = levelledEnds(region);
                float flat = all.Count > 0 ? raise[all[0]] : 0f;
                foreach (var c in cells) { elevation[c.x, c.y] = stairFrom[c.x, c.y] = stairTo[c.x, c.y] = flat; stairDir[c.x, c.y] = Vector2Int.zero; }
                continue;
            }
            int a = ends[0], b = ends[1];
            var fromA = PassageDistance(region, cells, a, dirs);
            var fromB = PassageDistance(region, cells, b, dirs);
            foreach (var c in cells)
            {
                int da = fromA[c], db = fromB[c];
                float length = da + db + 1;
                float ea = Mathf.Lerp(raise[a], raise[b], da / length), eb = Mathf.Lerp(raise[a], raise[b], (da + 1) / length);
                elevation[c.x, c.y] = (ea + eb) * .5f;
                stairFrom[c.x, c.y] = ea; stairTo[c.x, c.y] = eb;
                stairDir[c.x, c.y] = Vector2Int.zero;
                // Up the stairs: toward the neighbour one step further from A (or room B itself).
                foreach (var dir in dirs)
                {
                    var n = c + dir;
                    if ((fromA.TryGetValue(n, out int dn) && dn == da + 1) || (Layout.RoomAt(n) == b && db == 0)) { stairDir[c.x, c.y] = dir; break; }
                }
            }
        }
    }

    // The height of a tile's edge on one side, or null along the side of a flight of steps (it climbs along that edge).
    float? EdgeHeight(Vector2Int c, Vector2Int side)
    {
        if (Mathf.Abs(stairTo[c.x, c.y] - stairFrom[c.x, c.y]) <= .01f) return elevation[c.x, c.y];
        var up = stairDir[c.x, c.y];
        if (side == up) return stairTo[c.x, c.y];
        if (side == -up) return stairFrom[c.x, c.y];
        return null;
    }

    // Passages with a tile edge that doesn't meet its neighbour's: a step taller than a tread, or a flight of steps
    // beside something that doesn't climb with it.
    HashSet<int> MismatchedPassages(int rooms)
    {
        var bad = new HashSet<int>();
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        {
            if (!Layout.floor[x, z]) continue;
            var c = new Vector2Int(x, z);
            foreach (var dir in new[] { Vector2Int.right, Vector2Int.up })
            {
                var n = c + dir;
                if (!Layout.InBounds(n) || !Layout.floor[n.x, n.y]) continue;
                float? hc = EdgeHeight(c, dir), hn = EdgeHeight(n, -dir);
                bool meets = hc.HasValue && hn.HasValue ? Mathf.Abs(hc.Value - hn.Value) <= MaxTreadRise + .02f
                    : !hc.HasValue && !hn.HasValue && stairDir[c.x, c.y] == stairDir[n.x, n.y]
                      && Mathf.Abs(stairFrom[c.x, c.y] - stairFrom[n.x, n.y]) < .05f && Mathf.Abs(stairTo[c.x, c.y] - stairTo[n.x, n.y]) < .05f;
                if (meets) continue;
                int rc = Layout.RegionIds[x, z], rn = Layout.RegionIds[n.x, n.y];
                if (rc >= rooms) bad.Add(rc);
                if (rn >= rooms) bad.Add(rn);
            }
        }
        return bad;
    }

    // Board Verticality: the tallest room (after the start) rises to its height times the verticality; of
    // the rest, about half keep their level, some dip lower and some climb part of the way toward the peak.
    // Below 1 everything flattens evenly. The start room keeps its height.
    void VaryHeights(float[] raise, System.Random rng)
    {
        float v = Mathf.Max(0f, data.boardVerticality);
        if (Mathf.Approximately(v, 1f)) return;
        int peak = -1; float top = 0f;
        for (int i = 1; i < raise.Length; i++) if (raise[i] > top) { top = raise[i]; peak = i; }
        if (peak < 0) return;
        float peakHeight = top * v;
        for (int i = 1; i < raise.Length; i++)
        {
            double roll = rng.NextDouble(), amount = rng.NextDouble(); // always two draws, so rooms keep their rolls
            float b = raise[i];
            if (i == peak) raise[i] = peakHeight;
            else if (v < 1f) raise[i] = b * v;
            else if (roll < .5) raise[i] = b;
            else if (roll < .75) raise[i] = b * Mathf.Lerp(.35f, .8f, (float)amount);
            else raise[i] = Mathf.Lerp(b, peakHeight, Mathf.Lerp(.3f, .75f, (float)amount));
        }
    }

    // A passage can only climb so steeply (MaxStairAngle over its length): where two rooms differ by more,
    // the lower one is lifted until the climb fits. Lifting never lowers anything, so this settles.
    // Stairs only run between a passage's first two rooms, so every other way between rooms is kept level: all the
    // rooms a passage touches when it touches more than two, and rooms that open straight into each other.
    // levelled: the rooms that must share one height with this passage's (its network's), or null when it has stairs.
    void KeepStairsWalkable(float[] raise, List<(int region, List<Vector2Int> cells, List<int> ends)> passages, Vector2Int[] dirs, System.Func<int, List<int>> levelled)
    {
        var limits = new List<(int a, int b, float rise)>();
        foreach (var (region, cells, ends) in passages)
        {
            var level = levelled(region);
            if (level != null)
            {
                for (int i = 1; i < level.Count; i++) limits.Add((level[0], level[i], 0f));
                continue;
            }
            if (ends.Count < 2) continue;
            int steps = 0;
            foreach (var n in PassageDistance(region, cells, ends[0], dirs).Values) steps = Mathf.Max(steps, n + 1);
            limits.Add((ends[0], ends[1], steps * data.cellSize * Mathf.Tan(MaxStairAngle * Mathf.Deg2Rad)));
        }
        int rooms = raise.Length;
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        {
            int a = Layout.RoomAt(new Vector2Int(x, z));
            if (a < 0) continue;
            foreach (var dir in dirs)
            {
                int b = Layout.RoomAt(new Vector2Int(x, z) + dir);
                if (b >= 0 && b != a && b < rooms) limits.Add((a, b, 0f));
            }
        }
        for (int pass = 0; pass < 64; pass++)
        {
            bool changed = false;
            foreach (var (a, b, rise) in limits)
            {
                if (raise[a] - raise[b] > rise + .001f) { raise[b] = raise[a] - rise; changed = true; }
                else if (raise[b] - raise[a] > rise + .001f) { raise[a] = raise[b] - rise; changed = true; }
            }
            if (!changed) break;
        }
    }

    // Steps along the passage from the cells next to the given room.
    Dictionary<Vector2Int, int> PassageDistance(int region, List<Vector2Int> cells, int room, Vector2Int[] dirs)
    {
        var dist = new Dictionary<Vector2Int, int>();
        var queue = new Queue<Vector2Int>();
        foreach (var c in cells)
            foreach (var dir in dirs)
                if (Layout.RoomAt(c + dir) == room) { dist[c] = 0; queue.Enqueue(c); break; }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var dir in dirs)
            {
                var n = c + dir;
                if (!Layout.InBounds(n) || !Layout.floor[n.x, n.y] || Layout.RegionIds[n.x, n.y] != region || dist.ContainsKey(n)) continue;
                dist[n] = dist[c] + 1; queue.Enqueue(n);
            }
        }
        foreach (var c in cells) if (!dist.ContainsKey(c)) dist[c] = 0;
        return dist;
    }

    void BeginBoard()
    {
        board = new GameObject("Board").transform; board.SetParent(transform, false);
        boardRegions = new Transform[Layout.RegionCount];
        for (int i = 0; i < boardRegions.Length; i++)
        {
            boardRegions[i] = new GameObject(i < Layout.rooms.Count ? $"Room {i}" : $"Passage {i}").transform;
            boardRegions[i].SetParent(board, false);
        }
    }

    // One cell of the board: a tile with a printed top and a card edge (stone sides when the room is
    // raised), the printed grid, or a short flight of steps on a sloping passage.
    void BuildBoardCell(int x, int z)
    {
        var p = new Vector2Int(x, z);
        int region = Layout.RegionIds[x, z];
        var cell = new GameObject($"Tile {x},{z}").transform;
        cell.SetParent(boardRegions[region], false);
        var centre = transform.position + new Vector3((x - data.width * .5f) * data.cellSize, 0, (z - data.depth * .5f) * data.cellSize);
        cell.position = centre;
        float size = data.cellSize, t = TileThickness;
        var top = FloorMaterial(region, x, z);
        bool stairs = Mathf.Abs(stairTo[x, z] - stairFrom[x, z]) > .01f;
        if (!stairs)
        {
            Slab(cell, centre, new Vector2(size, size), elevation[x, z], top, region);
            if (data.boardGridMaterial != null)
            {
                var grid = GameObject.CreatePrimitive(PrimitiveType.Quad); grid.name = "Printed grid";
                DestroyImmediate(grid.GetComponent<Collider>());
                grid.transform.SetParent(cell, false);
                grid.transform.position = centre + Vector3.up * (elevation[x, z] + t + .006f);
                grid.transform.rotation = Quaternion.Euler(90, 0, 0);
                grid.transform.localScale = new Vector3(size, size, 1);
                var r = grid.GetComponent<Renderer>(); r.sharedMaterial = data.boardGridMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return;
        }
        // Small steps: the cell split into treads along the way up, enough of them that no step is higher
        // than MaxTreadRise (a tall climb on a very vertical board makes a steeper, longer flight).
        int treads = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(stairTo[x, z] - stairFrom[x, z]) / MaxTreadRise), 4, 24);
        var up = stairDir[x, z];
        var along = new Vector3(up.x, 0, up.y);
        var across = new Vector2(up.x != 0 ? size / treads : size, up.y != 0 ? size / treads : size);
        for (int i = 0; i < treads; i++)
        {
            float k = (i + .5f) / treads;
            var at = centre + along * ((k - .5f) * size);
            Slab(cell, at, across, Mathf.Lerp(stairFrom[x, z], stairTo[x, z], (i + 1f) / treads), top, region);
        }
    }

    // A block from the table up to height + a tile's thickness, with a printed top.
    void Slab(Transform parent, Vector3 centre, Vector2 footprint, float height, Material top, int region)
    {
        float t = TileThickness;
        float bodyHeight = height + t - .02f;
        var sides = height > .01f ? WallMaterial(region, 0) : data.boardEdgeMaterial != null ? data.boardEdgeMaterial : data.woodMaterial;
        var bodySize = new Vector3(footprint.x, bodyHeight, footprint.y);
        var body = height > .01f ? ArchitectureBox("Raised block", centre + Vector3.up * (bodyHeight * .5f), bodySize, sides, parent)
            : Box("Tile edge", centre + Vector3.up * (bodyHeight * .5f), bodySize, sides, parent);
        var cap = ArchitectureBox("Printed top", centre + Vector3.up * (bodyHeight + .01f), new Vector3(footprint.x, .02f, footprint.y), top, parent);
        cap.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Invisible walls round every outer edge of the board, so no one walks off a tile.
    void BuildBoardRim()
    {
        var rim = new GameObject("Edge rim").transform; rim.SetParent(board, false);
        var dirs = DungeonLayout.Neighbours;
        float size = data.cellSize;
        for (int x = 0; x < data.width; x++) for (int z = 0; z < data.depth; z++)
        {
            if (!Layout.floor[x, z]) continue;
            var p = new Vector2Int(x, z);
            foreach (var dir in dirs)
            {
                var n = p + dir;
                if (Layout.InBounds(n) && Layout.floor[n.x, n.y]) continue;
                var centre = transform.position + new Vector3((x - data.width * .5f) * size, 0, (z - data.depth * .5f) * size);
                float top = elevation[x, z] + 3f;
                var go = new GameObject("Rim"); go.transform.SetParent(rim, false);
                go.transform.position = centre + new Vector3(dir.x, 0, dir.y) * (size * .5f + .05f) + Vector3.up * (top * .5f);
                var box = go.AddComponent<BoxCollider>();
                go.AddComponent<InvisibleWall>();
                box.size = dir.x != 0 ? new Vector3(.1f, top, size + .1f) : new Vector3(size + .1f, top, .1f);
            }
        }
    }

    bool BoardPostProps => data.boardDoorPosts != null && data.boardDoorPosts.Length > 0;

    // Cells a room's shape leaves out inside it (pillar hall columns, the middle of a ring): with mound props on the
    // level, each becomes a mound a layer above the room with a prop on it, filed under the room so it is revealed with it.
    void BuildBoardMounds()
    {
        if (data.boardMoundProps == null || data.boardMoundProps.Length == 0) return;
        var rng = new System.Random(unchecked(seed0 * 31 + 7919));
        int w = Layout.floor.GetLength(0), d = Layout.floor.GetLength(1);
        for (int r = 0; r < Layout.rooms.Count; r++)
        {
            var rect = Layout.rooms[r];
            bool Floor(int x, int z) => x >= 0 && z >= 0 && x < w && z < d && Layout.floor[x, z] && Layout.RegionIds[x, z] == r;
            for (int x = rect.xMin; x < rect.xMax; x++)
                for (int z = rect.yMin; z < rect.yMax; z++)
                {
                    if (x < 0 || z < 0 || x >= w || z >= d || Layout.floor[x, z]) continue;
                    // Inside: the room's floor on both sides along the row and along the column.
                    bool left = false, rightSide = false, below = false, above = false;
                    for (int i = rect.xMin; i < x; i++) left |= Floor(i, z);
                    for (int i = x + 1; i < rect.xMax; i++) rightSide |= Floor(i, z);
                    for (int i = rect.yMin; i < z; i++) below |= Floor(x, i);
                    for (int i = z + 1; i < rect.yMax; i++) above |= Floor(x, i);
                    if (!(left && rightSide && below && above)) continue;
                    float height = 0f;
                    foreach (var n in DungeonLayout.Neighbours) if (Floor(x + n.x, z + n.y)) height = Mathf.Max(height, elevation[x + n.x, z + n.y]);
                    height += data.boardLayerHeight;
                    var cell = new GameObject($"Mound {x},{z}").transform; cell.SetParent(boardRegions[r], false);
                    var centre = transform.position + new Vector3((x - data.width * .5f) * data.cellSize, 0, (z - data.depth * .5f) * data.cellSize);
                    cell.position = centre;
                    Slab(cell, centre, new Vector2(data.cellSize, data.cellSize), height, FloorMaterial(r, x, z), r);
                    var prefab = data.boardMoundProps[rng.Next(data.boardMoundProps.Length)];
                    if (prefab != null) Instantiate(prefab, centre + Vector3.up * (height + TileThickness), Quaternion.Euler(0, rng.Next(360), 0), cell);
                }
        }
    }

    // A board door stands in an arch: two posts from the table and the lintel above them.
    void BoardDoorFrame(DungeonDoor door, Vector2Int roomCell, Vector2Int passageCell, float top)
    {
        var right = door.transform.right;
        var basePos = door.transform.position; basePos.y = transform.position.y;
        float post = .34f, height = top - transform.position.y;
        var material = WallMaterial(Layout.RegionIds[roomCell.x, roomCell.y], 0);
        var rng = new System.Random(unchecked(roomCell.x * 92821 ^ roomCell.y * 68917 ^ FloorNumber));
        for (int side = -1; side <= 1; side += 2)
        {
            if (BoardPostProps)
            {
                // A post prop (a tree) either side of the door, standing on the room's edge.
                var prefab = data.boardDoorPosts[rng.Next(data.boardDoorPosts.Length)];
                var at = basePos + right * (side * (data.cellSize * .5f + post * .5f)); at.y = door.transform.position.y;
                if (prefab != null) Instantiate(prefab, at, Quaternion.Euler(0, rng.Next(360), 0), door.transform);
                continue;
            }
            var centre = basePos + right * (side * (data.cellSize * .5f + post * .5f)) + Vector3.up * (height * .5f);
            var size = Mathf.Abs(right.x) > .5f ? new Vector3(post, height, WallThickness + .06f) : new Vector3(WallThickness + .06f, height, post);
            ArchitectureBox("Arch post", centre, size, material, door.transform);
        }
        BoardDoorTorch(door, roomCell, passageCell, right, post);
    }

    // A board has no walls for PlaceTorches, so each door gets one torch on the room side of its arch
    // (left post as you face the door): the way on is never lost in the dark.
    void BoardDoorTorch(DungeonDoor door, Vector2Int roomCell, Vector2Int passageCell, Vector3 right, float post)
    {
        var rng = new System.Random(unchecked(roomCell.x * 73856093 ^ roomCell.y * 19349663 ^ FloorNumber));
        var entry = DungeonWallLight.Choose(WallLights, rng);
        if (entry == null || entry.prefab == null) return;
        var into = new Vector3(passageCell.x - roomCell.x, 0f, passageCell.y - roomCell.y).normalized;
        var postCentre = door.transform.position - right * (data.cellSize * .5f + post * .5f);
        var face = postCentre - into * ((WallThickness + .06f) * .5f);
        face.y = Cell(roomCell).y + TileThickness;
        // Under the door, so BoardReveal shows it with the door.
        var torch = Instantiate(entry.prefab, face, Quaternion.LookRotation(-into));
        torch.transform.SetParent(door.transform, true);
        DungeonWallLight.Apply(torch, entry.Tint ?? (Biome != null ? Biome.TorchColor : null), TorchBrightness);
    }
}

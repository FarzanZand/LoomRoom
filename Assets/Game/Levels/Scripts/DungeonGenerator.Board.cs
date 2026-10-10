using System.Collections.Generic;
using UnityEngine;

// Board-game levels (TableLevelKind.Boardgame): the same layout, rooms and content, built as printed tiles
// lying on the table instead of walled rooms. Rooms can be raised a layer or two; the short passages
// between them become small stairs. An invisible rim keeps everyone on the tiles, and BoardReveal keeps
// whatever lies behind a closed door off the table until it opens.
public partial class DungeonGenerator
{
    bool Board => data != null && data.kind == TableLevelKind.Boardgame;

    float[,] elevation;               // top of the floor at each cell's centre, above the table
    float[,] stairFrom, stairTo;      // passage cells: floor height at the edge toward each end
    Vector2Int[,] stairDir;           // passage cells: the way up the stairs (toward stairTo)
    Transform board;
    Transform[] boardRegions;
    readonly List<(DungeonDoor door, Vector2Int a, Vector2Int b)> boardDoors = new();

    public Transform[] BoardRegions => boardRegions;
    public IReadOnlyList<(DungeonDoor door, Vector2Int a, Vector2Int b)> BoardDoors => boardDoors;
    float TileThickness => Mathf.Max(.02f, data.boardTileThickness);

    float BoardElevation(Vector2Int p) =>
        elevation != null && p.x >= 0 && p.y >= 0 && p.x < elevation.GetLength(0) && p.y < elevation.GetLength(1) ? elevation[p.x, p.y] : 0f;

    // Room heights by layer (authored, or seeded), and stairs along each passage between two rooms.
    void ComputeBoardElevations(int seed)
    {
        int w = data.width, d = data.depth, rooms = Layout.rooms.Count;
        elevation = new float[w, d]; stairFrom = new float[w, d]; stairTo = new float[w, d]; stairDir = new Vector2Int[w, d];
        var raise = new float[rooms];
        var rng = new System.Random(unchecked(seed + 70001));
        for (int i = 0; i < rooms; i++)
        {
            int layers = Authored && data.authoredRooms != null && i < data.authoredRooms.Length && data.authoredRooms[i] != null
                ? data.authoredRooms[i].raised : i == 0 ? 0 : rng.Next(0, Mathf.Max(0, data.boardMaxRaise) + 1);
            raise[i] = layers * data.boardLayerHeight;
        }
        var dirs = DungeonLayout.Neighbours;
        for (int x = 0; x < w; x++) for (int z = 0; z < d; z++)
        {
            if (!Layout.floor[x, z]) continue;
            int region = Layout.RegionIds[x, z];
            if (region < rooms) { elevation[x, z] = stairFrom[x, z] = stairTo[x, z] = raise[region]; }
        }
        // Each passage: walking distance from either end, so the floor climbs evenly from one room to the next.
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
            if (cells.Count == 0) continue;
            if (ends.Count < 2)
            {
                float flat = ends.Count == 1 ? raise[ends[0]] : 0f;
                foreach (var c in cells) elevation[c.x, c.y] = stairFrom[c.x, c.y] = stairTo[c.x, c.y] = flat;
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
                // Up the stairs: toward the neighbour one step further from A (or room B itself).
                foreach (var dir in dirs)
                {
                    var n = c + dir;
                    if ((fromA.TryGetValue(n, out int dn) && dn == da + 1) || (Layout.RoomAt(n) == b && db == 0)) { stairDir[c.x, c.y] = dir; break; }
                }
            }
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
                Destroy(grid.GetComponent<Collider>());
                grid.transform.SetParent(cell, false);
                grid.transform.position = centre + Vector3.up * (elevation[x, z] + t + .006f);
                grid.transform.rotation = Quaternion.Euler(90, 0, 0);
                grid.transform.localScale = new Vector3(size, size, 1);
                var r = grid.GetComponent<Renderer>(); r.sharedMaterial = data.boardGridMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return;
        }
        // Small steps: the cell split into treads along the way up.
        const int treads = 4;
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
                box.size = dir.x != 0 ? new Vector3(.1f, top, size + .1f) : new Vector3(size + .1f, top, .1f);
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
        for (int side = -1; side <= 1; side += 2)
        {
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
        if (entry.Tint.HasValue) DungeonWallLight.Apply(torch, entry.Tint.Value);
    }
}

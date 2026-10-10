using System;
using System.Collections.Generic;
using UnityEngine;

// Serialized by integer: append only. Partition (one rectangular room per slice of the grid) is
// retired: a level still set to it is generated as Grown, with a warning.
public enum DungeonLayoutMode { Partition = 0, Grown = 1, Authored = 2 }

// Seeded dungeon layout: rooms, corridor sections, regions and reserved walkways.
// Grown (DungeonLayout.Grown.cs) packs shaped rooms outwards from the entrance. Authored
// (FromMap) reads a hand-painted floor plan.
public sealed partial class DungeonLayout
{
    public readonly bool[,] floor;
    // Bounding boxes. A shaped room's floor is RoomCells(i); use InRoom, never rooms[i].Contains.
    public readonly List<RectInt> rooms = new();
    public readonly int seed;
    public readonly List<Vector2Int> Connections = new();
    // Walkways plus the whole entrance and exit rooms: never furnished.
    public readonly HashSet<Vector2Int> Reserved = new();
    // Routes from each room's doorways to its centre.
    public readonly HashSet<Vector2Int> Walkways = new();
    // Corridor spurs that lead nowhere: the last cell and the direction it was dug in.
    public readonly List<(Vector2Int cell, Vector2Int direction)> DeadEnds = new();
    readonly int[,] corridorOwners;
    int nextCorridor;
    public int[,] RegionIds { get; private set; }
    public int RegionCount { get; private set; }
    public int CorridorCount => RegionCount - rooms.Count;
    readonly List<List<Vector2Int>> roomCells = new();
    readonly List<Vector2Int> roomCenters = new();
    public Vector2Int Start => roomCenters[0];
    public Vector2Int Exit { get; private set; }
    public static Vector2Int Center(RectInt r) => new(r.x + r.width / 2, r.y + r.height / 2);
    public Vector2Int RoomCenter(int room) => roomCenters[room];
    public IReadOnlyList<Vector2Int> RoomCells(int room) => roomCells[room];
    public bool InBounds(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < floor.GetLength(0) && p.y < floor.GetLength(1);
    // Room index of a floor cell, or -1 for corridors, walls and pillars.
    public int RoomAt(Vector2Int p) => InBounds(p) && floor[p.x,p.y] && RegionIds[p.x,p.y] < rooms.Count ? RegionIds[p.x,p.y] : -1;
    public bool InRoom(int room, Vector2Int p) => RoomAt(p) == room;
    public bool IsRectangular(int room) => roomCells[room].Count == rooms[room].width * rooms[room].height;
    // Grown layouts only: the shape each room was built from, and quarter turns for its template.
    public DungeonShape[] Shapes { get; private set; }
    public int RoomRotation(int room) => Shapes != null && Shapes[room] != null ? Shapes[room].rotation
        : authoredRotations != null && room < authoredRotations.Length ? authoredRotations[room] : 0;
    int[] authoredRotations;
    // Grown layouts only: which requested room each placed room came from (rooms that did not fit are dropped).
    public int[] PlacedRequests { get; private set; }

    DungeonLayout(int width, int depth, int seed)
    {
        if(width<14 || depth<14)throw new ArgumentException("Dungeon requires dimensions >=14.");
        this.seed = seed;
        floor = new bool[width, depth];
        corridorOwners = new int[width, depth];
        for(int x=0;x<width;x++)for(int y=0;y<depth;y++)corridorOwners[x,y]=-1;
    }

    static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    // The four neighbours in the order the generator and the reveals walk them. Shared: never modify.
    public static readonly Vector2Int[] Neighbours = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    // Room index painted in an authored map: '0'-'9', then 'A'-'Z' for rooms 10 to 35. -1 for anything else.
    public static int MapRoom(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'Z' ? 10 + c - 'A' : -1;

    // A hand-painted floor plan. Rows run north to south, columns west to east. Room digits/letters are room
    // floor (room 0 is the entrance, the highest is the exit), '.' is passage, '+' is passage with a door into
    // the room beside it; anything else is solid. Rooms must be numbered from 0 without gaps.
    public static DungeonLayout FromMap(IReadOnlyList<string> rows, int seed, int[] rotations,
        out List<(Vector2Int roomCell, Vector2Int direction)> doors)
    {
        int depth = rows.Count, width = 0;
        foreach (var r in rows) width = Math.Max(width, r.Length);
        var layout = new DungeonLayout(width, depth, seed) { authoredRotations = rotations };
        var byRoom = new SortedDictionary<int, List<Vector2Int>>();
        var doorCells = new List<Vector2Int>();
        for (int row = 0; row < depth; row++)
            for (int x = 0; x < rows[row].Length; x++)
            {
                char c = rows[row][x];
                var p = new Vector2Int(x, depth - 1 - row);
                int room = MapRoom(c);
                if (room >= 0)
                {
                    if (!byRoom.TryGetValue(room, out var cells)) byRoom[room] = cells = new List<Vector2Int>();
                    cells.Add(p); layout.floor[p.x, p.y] = true;
                }
                else if (c == '.' || c == '+')
                {
                    layout.floor[p.x, p.y] = true; layout.corridorOwners[p.x, p.y] = 0;
                    if (c == '+') doorCells.Add(p);
                }
            }
        if (byRoom.Count < 2) throw new InvalidOperationException("An authored map needs at least two rooms.");
        int expected = 0;
        foreach (var pair in byRoom)
        {
            if (pair.Key != expected++) throw new InvalidOperationException($"Authored map rooms must be numbered 0, 1, 2 ... without gaps (room {expected - 1} is missing).");
            var cells = pair.Value;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in cells) { minX = Math.Min(minX, c.x); minY = Math.Min(minY, c.y); maxX = Math.Max(maxX, c.x); maxY = Math.Max(maxY, c.y); }
            var rect = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            // The centre must be floor of the room, even in an L-shaped one.
            var middle = Center(rect); var centre = cells[0];
            foreach (var c in cells) if ((c - middle).sqrMagnitude < (centre - middle).sqrMagnitude) centre = c;
            layout.rooms.Add(rect); layout.roomCells.Add(cells); layout.roomCenters.Add(centre);
        }
        layout.Finish();
        // Passages record which rooms they join, for the log.
        for (int region = layout.rooms.Count; region < layout.RegionCount; region++)
        {
            var joined = new HashSet<int>();
            for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++)
                if (layout.RegionIds[x, y] == region)
                    foreach (var d in Directions) { int room = layout.RoomAt(new Vector2Int(x, y) + d); if (room >= 0) joined.Add(room); }
            var list = new List<int>(joined);
            for (int a = 0; a < list.Count; a++) for (int b = a + 1; b < list.Count; b++) layout.Connections.Add(new Vector2Int(list[a], list[b]));
        }
        doors = new List<(Vector2Int, Vector2Int)>();
        foreach (var p in doorCells)
            foreach (var d in Directions)
                if (layout.RoomAt(p + d) >= 0) { doors.Add((p + d, -d)); break; }
        return layout;
    }

    // Regions, walkways and the reserved entrance and exit, once every room and passage is in place.
    void Finish()
    {
        Exit = roomCenters[rooms.Count - 1];
        BuildRegions();
        BuildWalkways();
        foreach (var p in roomCells[0]) Reserved.Add(p);
        foreach (var p in roomCells[rooms.Count - 1]) Reserved.Add(p);
    }

    // Keeps furnishing off the cells an authored map puts its own content on.
    public void Reserve(Vector2Int cell) => Reserved.Add(cell);

    // Walking distance over floor cells: 1 at the start, 0 where unreachable.
    int[,] Distances(Vector2Int start)
    {
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        var distance = new int[width, depth];
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start); distance[start.x, start.y] = 1;
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var dir in Directions)
            {
                var n = p + dir;
                if (!InBounds(n) || !floor[n.x,n.y] || distance[n.x,n.y] != 0) continue;
                distance[n.x,n.y] = distance[p.x,p.y] + 1; queue.Enqueue(n);
            }
        }
        return distance;
    }

    // Doorway clearance in every room, plus the shortest path from every doorway to the centre
    // (a straight cross could run into a pillar).
    void BuildWalkways()
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            var c = roomCenters[i];
            var doors = new List<Vector2Int>();
            foreach (var p in roomCells[i])
            {
                foreach (var d in Directions)
                {
                    var n = p + d;
                    if (!InBounds(n) || RoomAt(n) == i || !floor[n.x,n.y]) continue;
                    Walkways.Add(p); Walkways.Add(p - d); doors.Add(p);
                }
            }
            if (doors.Count == 0) continue;
            var previous = new Dictionary<Vector2Int, Vector2Int> { [c] = c };
            var queue = new Queue<Vector2Int>(); queue.Enqueue(c);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in Directions)
                {
                    var n = p + d;
                    if (RoomAt(n) != i || previous.ContainsKey(n)) continue;
                    previous[n] = p; queue.Enqueue(n);
                }
            }
            Walkways.Add(c);
            foreach (var door in doors)
                for (var p = door; p != c && previous.TryGetValue(p, out var back); p = back) Walkways.Add(p);
        }
        foreach (var p in Walkways) Reserved.Add(p);
    }

    // Preserve authored corridor legs at junctions instead of merging the entire network.
    void BuildRegions()
    {
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        RegionIds = new int[width, depth];
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++) RegionIds[x,y] = -1;
        for (int i = 0; i < rooms.Count; i++)
            foreach (var p in roomCells[i]) RegionIds[p.x,p.y] = i;
        RegionCount = rooms.Count;
        var queue = new Queue<Vector2Int>();
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++)
        {
            if (!floor[x,y] || RegionIds[x,y] >= 0) continue;
            int id = RegionCount++;
            int owner = corridorOwners[x,y];
            RegionIds[x,y] = id; queue.Enqueue(new Vector2Int(x,y));
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in Directions)
                {
                    var n = p + d;
                    if (n.x < 0 || n.y < 0 || n.x >= width || n.y >= depth ||
                        !floor[n.x,n.y] || RegionIds[n.x,n.y] >= 0 || corridorOwners[n.x,n.y]!=owner) continue;
                    RegionIds[n.x,n.y] = id; queue.Enqueue(n);
                }
            }
        }
    }

    public bool[] SelectOpenRegions(float percent, int salt)
    {
        var selected = new bool[RegionCount];
        var order = new List<int>();
        for (int i = 0; i < RegionCount; i++) order.Add(i);
        var rng = new System.Random(unchecked(seed + salt));
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        int count = Mathf.RoundToInt(RegionCount * Mathf.Clamp(percent, 0, 100) / 100f);
        for (int i = 0; i < count; i++) selected[order[i]] = true;
        return selected;
    }

    // A passage can bend or branch, but walking through it should never require two gates.
    public List<(Vector2Int roomCell, Vector2Int direction)> SelectDoorways(float percent)
    {
        var result = new List<(Vector2Int, Vector2Int)>();
        var visited = new HashSet<Vector2Int>();
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        bool Floor(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < depth && floor[p.x,p.y];
        bool Passage(Vector2Int p) => Floor(p) && RegionIds[p.x,p.y] >= rooms.Count;
        var rng = new System.Random(unchecked(seed + 7321));
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++)
        {
            var start = new Vector2Int(x,y);
            if (!Passage(start) || !visited.Add(start)) continue;
            var queue = new Queue<Vector2Int>(); queue.Enqueue(start);
            var entrances = new List<(Vector2Int, Vector2Int)>();
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var dir in Neighbours)
                {
                    var n = p + dir;
                    if (Passage(n)) { if (visited.Add(n)) queue.Enqueue(n); continue; }
                    if (!Floor(n)) continue;
                    var side = new Vector2Int(-dir.y,dir.x);
                    if (!Floor(p + side) && !Floor(p - side)) entrances.Add((n,-dir));
                }
            }
            // One chance per passage, then choose just one of its room entrances.
            if (entrances.Count > 0 && rng.NextDouble() * 100 < Mathf.Clamp(percent,0,100))
                result.Add(entrances[rng.Next(entrances.Count)]);
        }
        return result;
    }

    // Only remove the outermost wall in each direction, never a wall facing another room.
    public bool FacesOutside(Vector2Int cell, Vector2Int direction)
    {
        for (var p = cell + direction; p.x >= 0 && p.y >= 0 && p.x < floor.GetLength(0) && p.y < floor.GetLength(1); p += direction)
            if (floor[p.x,p.y]) return false;
        return true;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct DungeonGrowthSettings
{
    public Vector2Int spacing;      // empty cells between a new room and the room it grows from
    public float wander;            // extra random cost per corridor cell; 0 digs straight
    public float widePercent;       // chance a corridor is two cells wide
    public float deadEndPercent;    // spurs, as a percentage of the room count
    public float loopPercent;       // extra connections, as a percentage of the room count
    public int loopReach;           // furthest gap, in cells, a loop may bridge
}

// Grown layout: shaped rooms packed outwards from the entrance, like stamping rooms onto a
// map. Each new room is placed a few cells off a room already on the map and tunnelled to it;
// the exit room is placed last, off the room furthest from the entrance. Rooms keep at least
// one wall cell between them, and corridors never run along a room they do not enter, so
// every opening is a planned doorway.
public sealed partial class DungeonLayout
{
    const int EmptyCell = -1, CorridorCell = -2;
    int[,] occupant;                // room index (floor or solid), CorridorCell or EmptyCell
    int[,] noise;
    System.Random growRandom;
    DungeonGrowthSettings growth;
    readonly List<int> degree = new();
    readonly List<HashSet<Vector2Int>> doorAnchors = new();   // null: doors anywhere on the outline
    readonly List<DungeonShape> placedShapes = new();

    public static DungeonLayout Grow(int width, int depth, IList<DungeonShape> requests, int seed, DungeonGrowthSettings settings)
    {
        if (requests == null || requests.Count < 2) throw new ArgumentException("Dungeon requires at least two rooms.");
        var layout = new DungeonLayout(width, depth, seed);
        layout.GrowRooms(requests, settings);
        return layout;
    }

    void GrowRooms(IList<DungeonShape> requests, DungeonGrowthSettings settings)
    {
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        growth = settings;
        growth.spacing = new Vector2Int(Mathf.Clamp(settings.spacing.x, 1, 8), Mathf.Clamp(Mathf.Max(settings.spacing.x, settings.spacing.y), 1, 8));
        growRandom = new System.Random(unchecked(seed * 31 + 977));
        occupant = new int[width, depth];
        noise = new int[width, depth];
        int wander = Mathf.RoundToInt(Mathf.Clamp(settings.wander, 0, 4) * 10);
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++) { occupant[x, y] = EmptyCell; noise[x, y] = growRandom.Next(wander + 1); }

        var placed = new List<int>();
        if (!PlaceFirst(requests[0])) throw new InvalidOperationException("The entrance room does not fit the grid. Increase width/depth or reduce room size.");
        placed.Add(0);
        int last = requests.Count - 1;
        for (int r = 1; r < last; r++)
            if (TryGrow(requests[r], null)) placed.Add(r);
        // The exit grows off the room furthest from the entrance, trying nearer ones if it will not fit.
        var distance = Distances(Start);
        var anchors = new List<int>();
        for (int i = 0; i < rooms.Count; i++) anchors.Add(i);
        anchors.Sort((a, b) => distance[roomCenters[b].x, roomCenters[b].y].CompareTo(distance[roomCenters[a].x, roomCenters[a].y]));
        if (!TryGrow(requests[last], anchors)) throw new InvalidOperationException("The exit room does not fit the grid. Increase width/depth or reduce room count or size.");
        placed.Add(last);
        if (placed.Count < requests.Count)
            Debug.LogWarning($"Dungeon seed {seed}: {requests.Count - placed.Count} of {requests.Count} rooms did not fit the {width}x{depth} grid. Reduce room count or size, or enlarge the grid.");
        PlacedRequests = placed.ToArray();
        Shapes = placedShapes.ToArray();

        AddLoops();
        AddDeadEnds();

        Exit = roomCenters[rooms.Count - 1];
        distance = Distances(Start);
        RoomDistances = new int[rooms.Count];
        for (int i = 0; i < rooms.Count; i++) { var c = roomCenters[i]; RoomDistances[i] = distance[c.x, c.y] - 1; }
        BuildRegions();
        BuildWalkways(false, true);
        foreach (var p in roomCells[0]) Reserved.Add(p);
        foreach (var p in roomCells[rooms.Count - 1]) Reserved.Add(p);
    }

    // ── Rooms ────────────────────────────────────────────────────────

    bool PlaceFirst(DungeonShape shape)
    {
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        foreach (var s in Fallbacks(shape))
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var origin = new Vector2Int(
                    (width - s.width) / 2 + growRandom.Next(-width / 6, width / 6 + 1),
                    (depth - s.height) / 2 + growRandom.Next(-depth / 6, depth / 6 + 1));
                if (!Fits(s, origin)) continue;
                Place(s, origin);
                return true;
            }
        return false;
    }

    bool TryGrow(DungeonShape shape, List<int> anchorOrder)
    {
        foreach (var s in Fallbacks(shape))
        {
            int attempts = anchorOrder != null ? anchorOrder.Count * 20 : 90;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int anchor = anchorOrder != null ? anchorOrder[attempt / 20] : PickAnchor();
                int gap = growRandom.Next(growth.spacing.x, growth.spacing.y + 1);
                var a = rooms[anchor];
                Vector2Int origin;
                switch (growRandom.Next(4))
                {
                    case 0: origin = new Vector2Int(a.xMax + gap, growRandom.Next(a.yMin - s.height + 1, a.yMax)); break;
                    case 1: origin = new Vector2Int(a.xMin - gap - s.width, growRandom.Next(a.yMin - s.height + 1, a.yMax)); break;
                    case 2: origin = new Vector2Int(growRandom.Next(a.xMin - s.width + 1, a.xMax), a.yMax + gap); break;
                    default: origin = new Vector2Int(growRandom.Next(a.xMin - s.width + 1, a.xMax), a.yMin - gap - s.height); break;
                }
                if (!Fits(s, origin)) continue;
                int index = Place(s, origin);
                if (Connect(anchor, index, (gap * 2 + 14) * 16)) return true;
                Unplace(index);
            }
        }
        return false;
    }

    // The requested shape, then smaller plain rooms when the map is getting full.
    static IEnumerable<DungeonShape> Fallbacks(DungeonShape shape)
    {
        var trimmed = shape.Trimmed();
        yield return trimmed;
        if (trimmed.width > 5 || trimmed.height > 5) yield return DungeonShape.Rectangle(Mathf.Min(trimmed.width, 5), Mathf.Min(trimmed.height, 5));
        yield return DungeonShape.Rectangle(3, 3);
    }

    // Prefer rooms with few connections, so the map branches instead of forming one long chain.
    int PickAnchor()
    {
        double total = 0;
        for (int i = 0; i < rooms.Count; i++) total += 1.0 / (1 + degree[i]);
        double roll = growRandom.NextDouble() * total;
        for (int i = 0; i < rooms.Count; i++) { roll -= 1.0 / (1 + degree[i]); if (roll < 0) return i; }
        return rooms.Count - 1;
    }

    // Every cell of the room keeps one clear cell (diagonals included) from anything on the map.
    bool Fits(DungeonShape s, Vector2Int origin)
    {
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        for (int x = 0; x < s.width; x++) for (int y = 0; y < s.height; y++)
        {
            if (s[x, y] == DungeonShape.Empty) continue;
            int px = origin.x + x, py = origin.y + y;
            if (px < 1 || py < 1 || px > width - 2 || py > depth - 2) return false;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                if (occupant[px + dx, py + dy] != EmptyCell) return false;
        }
        return true;
    }

    int Place(DungeonShape s, Vector2Int origin)
    {
        int index = rooms.Count;
        var cells = new List<Vector2Int>();
        HashSet<Vector2Int> anchors = s.HasDoorMarks ? new HashSet<Vector2Int>() : null;
        for (int x = 0; x < s.width; x++) for (int y = 0; y < s.height; y++)
        {
            if (s[x, y] == DungeonShape.Empty) continue;
            var p = origin + new Vector2Int(x, y);
            occupant[p.x, p.y] = index;
            if (s[x, y] != DungeonShape.Floor) continue;
            floor[p.x, p.y] = true; cells.Add(p);
            if (anchors != null && s.DoorAllowed(x, y)) anchors.Add(p);
        }
        rooms.Add(new RectInt(origin.x, origin.y, s.width, s.height));
        roomCells.Add(cells); roomCenters.Add(origin + s.center);
        placedShapes.Add(s); degree.Add(0); doorAnchors.Add(anchors);
        return index;
    }

    void Unplace(int index)
    {
        var r = rooms[index];
        foreach (var p in r.allPositionsWithin)
            if (occupant[p.x, p.y] == index) { occupant[p.x, p.y] = EmptyCell; floor[p.x, p.y] = false; }
        rooms.RemoveAt(index); roomCells.RemoveAt(index); roomCenters.RemoveAt(index);
        placedShapes.RemoveAt(index); degree.RemoveAt(index); doorAnchors.RemoveAt(index);
    }

    // ── Corridors ────────────────────────────────────────────────────

    bool Inner(Vector2Int p) => p.x >= 1 && p.y >= 1 && p.x <= floor.GetLength(0) - 2 && p.y <= floor.GetLength(1) - 2;

    // Beside a room (or its pillars): a corridor here would open into it.
    bool Halo(Vector2Int p)
    {
        foreach (var d in Directions) if (occupant[p.x + d.x, p.y + d.y] >= 0) return true;
        return false;
    }

    // A cell a corridor can enter the room through: exactly one floor neighbour in the room, and
    // otherwise touching only the room at the other end of the corridor (or nothing).
    bool IsDoor(Vector2Int q, int room, int other)
    {
        if (!Inner(q)) return false;
        int cell = occupant[q.x, q.y];
        if (cell != EmptyCell && cell != CorridorCell) return false;
        int count = 0;
        foreach (var d in Directions)
        {
            var n = q + d;
            int o = occupant[n.x, n.y];
            if (o < 0) continue;
            if (o != room && o != other) return false;
            if (!floor[n.x, n.y]) return false;
            if (o == room)
            {
                if (doorAnchors[room] != null && !doorAnchors[room].Contains(n)) return false;
                count++;
            }
        }
        return count == 1;
    }

    List<Vector2Int> DoorCells(int room, int other)
    {
        var list = new List<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        foreach (var p in roomCells[room])
            foreach (var d in Directions)
            {
                var q = p + d;
                if (seen.Add(q) && IsDoor(q, room, other)) list.Add(q);
            }
        return list;
    }

    // Cheapest tunnel between two rooms. New cells cost 10 plus noise; running through an
    // existing corridor costs 5, so passages share tunnels instead of digging twins.
    bool Connect(int a, int b, int maxCost)
    {
        var sources = DoorCells(a, b);
        var targets = new HashSet<Vector2Int>(DoorCells(b, a));
        if (sources.Count == 0 || targets.Count == 0) return false;
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        var cost = new int[width, depth];
        var previous = new Vector2Int[width, depth];
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++) cost[x, y] = int.MaxValue;
        var heap = new CellHeap();
        var start = new Vector2Int(-1, -1);
        foreach (var s in sources) { cost[s.x, s.y] = 0; previous[s.x, s.y] = start; heap.Push(0, s); }
        Vector2Int end = start;
        while (heap.Count > 0)
        {
            var (c, p) = heap.Pop();
            if (c > cost[p.x, p.y]) continue;
            if (targets.Contains(p)) { end = p; break; }
            foreach (var d in Directions)
            {
                var n = p + d;
                if (!Inner(n)) continue;
                int o = occupant[n.x, n.y];
                bool open = targets.Contains(n) || o == CorridorCell || (o == EmptyCell && !Halo(n));
                if (!open) continue;
                int next = c + (o == CorridorCell ? 5 : 10 + noise[n.x, n.y]);
                if (next > maxCost || next >= cost[n.x, n.y]) continue;
                cost[n.x, n.y] = next; previous[n.x, n.y] = p; heap.Push(next, n);
            }
        }
        if (end == start) return false;

        var path = new List<Vector2Int>();
        for (var p = end; p != start; p = previous[p.x, p.y]) path.Add(p);
        path.Reverse();
        int owner = nextCorridor++;
        var dug = new List<Vector2Int>();
        foreach (var p in path) if (Dig(p, owner)) dug.Add(p);
        // Occasionally a two-cell passage: widen the inner cells to one consistent side.
        if (path.Count >= 4 && growRandom.NextDouble() * 100 < growth.widePercent)
        {
            int side = growRandom.Next(2) == 0 ? 1 : -1;
            for (int i = 1; i < path.Count - 1; i++)
            {
                var along = path[i + 1] - path[i - 1];
                if (along.x != 0 && along.y != 0) continue;   // a bend
                var perpendicular = new Vector2Int(-Math.Sign(along.y), Math.Sign(along.x)) * side;
                var q = path[i] + perpendicular;
                if (Inner(q) && occupant[q.x, q.y] == EmptyCell && !Halo(q)) Dig(q, owner);
            }
        }
        Connections.Add(new Vector2Int(a, b));
        degree[a]++; degree[b]++;
        return true;
    }

    bool Dig(Vector2Int p, int owner)
    {
        if (occupant[p.x, p.y] != EmptyCell) return false;
        occupant[p.x, p.y] = CorridorCell; floor[p.x, p.y] = true; corridorOwners[p.x, p.y] = owner;
        return true;
    }

    // Extra connections between nearby rooms whose walking route is much longer than the gap.
    void AddLoops()
    {
        int loops = Mathf.RoundToInt(rooms.Count * Mathf.Clamp(growth.loopPercent, 0, 100) / 100f);
        if (loops <= 0) return;
        int reach = Mathf.Clamp(growth.loopReach, 2, 30);
        var pairs = new List<(int, int)>();
        for (int i = 0; i < rooms.Count; i++) for (int j = i + 1; j < rooms.Count; j++)
        {
            if (Connections.Contains(new Vector2Int(i, j)) || Connections.Contains(new Vector2Int(j, i))) continue;
            if (Gap(rooms[i], rooms[j]) <= reach) pairs.Add((i, j));
        }
        for (int i = pairs.Count - 1; i > 0; i--) { int j = growRandom.Next(i + 1); (pairs[i], pairs[j]) = (pairs[j], pairs[i]); }
        foreach (var (a, b) in pairs)
        {
            if (loops == 0) break;
            var ca = roomCenters[a]; var cb = roomCenters[b];
            int direct = Mathf.Abs(ca.x - cb.x) + Mathf.Abs(ca.y - cb.y);
            int walked = Distances(ca)[cb.x, cb.y] - 1;
            if (walked > 0 && walked < direct * 1.6f + 8) continue;
            if (Connect(a, b, (reach * 2 + 12) * 16)) loops--;
        }
    }

    static int Gap(RectInt a, RectInt b)
    {
        int dx = Mathf.Max(0, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
        int dy = Mathf.Max(0, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
        return Mathf.Max(dx, dy);
    }

    // Short winding spurs off corridors or rooms that end in rock.
    void AddDeadEnds()
    {
        int spurs = Mathf.RoundToInt(rooms.Count * Mathf.Clamp(growth.deadEndPercent, 0, 100) / 100f);
        int width = floor.GetLength(0), depth = floor.GetLength(1);
        for (int attempt = 0; spurs > 0 && attempt < spurs * 30 + 30; attempt++)
        {
            var cells = new List<Vector2Int>();
            Vector2Int from, dir;
            if (growRandom.Next(2) == 0)
            {
                // Off a corridor.
                from = new Vector2Int(growRandom.Next(1, width - 1), growRandom.Next(1, depth - 1));
                if (occupant[from.x, from.y] != CorridorCell) continue;
                dir = Directions[growRandom.Next(4)];
            }
            else
            {
                // Out of a room.
                int room = growRandom.Next(rooms.Count);
                if (room == 0 || room == rooms.Count - 1) continue;
                var doors = DoorCells(room, -1);
                if (doors.Count == 0) continue;
                var door = doors[growRandom.Next(doors.Count)];
                if (occupant[door.x, door.y] != EmptyCell) continue;
                dir = Vector2Int.zero;
                foreach (var d in Directions) if (occupant[door.x - d.x, door.y - d.y] == room) dir = d;
                from = door - dir;
                if (!SpurCell(door, from, cells)) continue;
                cells.Add(door);
            }
            int length = growRandom.Next(3, 8);
            var cur = cells.Count > 0 ? cells[^1] : from;
            while (cells.Count < length)
            {
                if (growRandom.NextDouble() < .3) dir = new Vector2Int(-dir.y, dir.x) * (growRandom.Next(2) == 0 ? 1 : -1);
                var n = cur + dir;
                if (!SpurCell(n, cur, cells))
                {
                    dir = new Vector2Int(-dir.y, dir.x);
                    n = cur + dir;
                    if (!SpurCell(n, cur, cells)) { dir = -dir; n = cur + dir; if (!SpurCell(n, cur, cells)) break; }
                }
                cells.Add(n); cur = n;
            }
            if (cells.Count < 3) continue;
            int owner = nextCorridor++;
            foreach (var c in cells) Dig(c, owner);
            DeadEnds.Add((cells[^1], dir));
            spurs--;
        }
    }

    // A spur cell touches nothing but the cell it grew from.
    bool SpurCell(Vector2Int n, Vector2Int from, List<Vector2Int> spur)
    {
        if (!Inner(n) || occupant[n.x, n.y] != EmptyCell || spur.Contains(n)) return false;
        bool fromRoom = occupant[from.x, from.y] >= 0;
        foreach (var d in Directions)
        {
            var m = n + d;
            if (m == from) continue;
            if (occupant[m.x, m.y] != EmptyCell || spur.Contains(m)) return false;
        }
        return fromRoom || !Halo(n);
    }

    // Minimal binary heap for the corridor search (no PriorityQueue in Unity's profile).
    sealed class CellHeap
    {
        readonly List<(int cost, Vector2Int cell)> items = new();
        public int Count => items.Count;
        public void Push(int cost, Vector2Int cell)
        {
            items.Add((cost, cell));
            for (int i = items.Count - 1; i > 0;)
            {
                int parent = (i - 1) / 2;
                if (items[parent].cost <= items[i].cost) break;
                (items[parent], items[i]) = (items[i], items[parent]); i = parent;
            }
        }
        public (int, Vector2Int) Pop()
        {
            var top = items[0];
            items[0] = items[^1]; items.RemoveAt(items.Count - 1);
            for (int i = 0; ;)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < items.Count && items[l].cost < items[m].cost) m = l;
                if (r < items.Count && items[r].cost < items[m].cost) m = r;
                if (m == i) break;
                (items[m], items[i]) = (items[i], items[m]); i = m;
            }
            return top;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// Pure seeded layout: rooms are connected as they are accepted, then loops are added.
public sealed class DungeonLayout
{
    public readonly bool[,] floor;
    public readonly List<RectInt> rooms = new();
    public readonly int seed;
    public Vector2Int Start => Center(rooms[0]);
    public Vector2Int Exit { get; private set; }
    public static Vector2Int Center(RectInt r) => new(r.x + r.width / 2, r.y + r.height / 2);

    public DungeonLayout(int width, int depth, int count, int seed)
    {
        this.seed = seed;
        floor = new bool[width, depth];
        var rng = new System.Random(seed);
        for (int attempt = 0; attempt < 700 && rooms.Count < count; attempt++)
        {
            int w = rng.Next(4, 8), h = rng.Next(4, 8);
            var room = new RectInt(rng.Next(2, width - w - 2), rng.Next(2, depth - h - 2), w, h);
            var padded = new RectInt(room.x - 1, room.y - 1, w + 2, h + 2);
            bool overlap = false;
            foreach (var other in rooms) if (padded.Overlaps(other)) { overlap = true; break; }
            if (overlap) continue;
            foreach (var p in room.allPositionsWithin) floor[p.x, p.y] = true;
            if (rooms.Count > 0) Connect(Center(rooms[rooms.Count - 1]), Center(room), rng.Next(2) == 0);
            rooms.Add(room);
        }
        for (int i = 2; i < rooms.Count; i += 3) Connect(Center(rooms[i - 2]), Center(rooms[i]), true);
        // Choose the most distant room by walkable path, not Euclidean distance.
        var distance = new int[width, depth];
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(Start); distance[Start.x, Start.y] = 1;
        var directions = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var dir in directions)
            {
                var n = p + dir;
                if (n.x < 0 || n.y < 0 || n.x >= width || n.y >= depth || !floor[n.x,n.y] || distance[n.x,n.y] != 0) continue;
                distance[n.x,n.y] = distance[p.x,p.y] + 1; queue.Enqueue(n);
            }
        }
        Exit = Start;
        foreach (var r in rooms) { var c = Center(r); if (distance[c.x,c.y] > distance[Exit.x,Exit.y]) Exit = c; }
    }

    void Connect(Vector2Int a, Vector2Int b, bool horizontalFirst)
    {
        var p = a;
        while (p != b)
        {
            Carve(p);
            if ((horizontalFirst && p.x != b.x) || p.y == b.y) p.x += Math.Sign(b.x - p.x);
            else p.y += Math.Sign(b.y - p.y);
        }
        Carve(b);
    }
    void Carve(Vector2Int p)
    {
        for (int x = p.x; x <= p.x + 1; x++) for (int y = p.y; y <= p.y + 1; y++)
            if (x > 0 && y > 0 && x < floor.GetLength(0)-1 && y < floor.GetLength(1)-1) floor[x,y] = true;
    }
}

using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Serialized by integer: append only, retired numbers stay retired.
public enum DungeonShapeKind
{
    Rectangle  = 0,
    CutCorners = 1,   // octagon-ish: corners trimmed
    LShape     = 2,
    TShape     = 3,
    Cross      = 4,
    PillarHall = 5,   // rows or a grid of one-cell columns
    Ring       = 6,   // a solid block in the middle, walk around it
    RoomInRoom = 7,   // an inner walled chamber with one opening
    Cave       = 8,   // cellular-automata blob
    Authored   = 9,   // a painted DungeonRoomShape asset
}

[Serializable]
public class DungeonShapeChoice
{
    [HorizontalGroup("Row"), HideLabel]
    public DungeonShapeKind kind;
    [HorizontalGroup("Row"), HideLabel, ShowIf(nameof(IsAuthored)), AssetsOnly, Required]
    public DungeonRoomShape shape;
    [HorizontalGroup("Row", 90), LabelWidth(45), Min(0), Tooltip("Relative likelihood. Zero disables this entry.")]
    public float weight = 1;
    bool IsAuthored => kind == DungeonShapeKind.Authored;

    public static DungeonShapeChoice Choose(DungeonShapeChoice[] choices, System.Random random)
    {
        if (choices == null) return null;
        double total = 0;
        foreach (var c in choices) if (Eligible(c)) total += c.weight;
        if (total <= 0) return null;
        double roll = random.NextDouble() * total;
        foreach (var c in choices)
        {
            if (!Eligible(c)) continue;
            roll -= c.weight;
            if (roll < 0) return c;
        }
        return null;
    }
    static bool Eligible(DungeonShapeChoice c) => c != null && c.weight > 0 && (c.kind != DungeonShapeKind.Authored || c.shape != null);
}

// A room footprint on the layout grid: which cells are floor, which are solid (pillars and
// inner walls that still belong to the room) and which are outside it. y is world +Z (north).
public sealed class DungeonShape
{
    public const byte Empty = 0, Floor = 1, Solid = 2;
    public readonly int width, height;
    readonly byte[] cells;
    readonly bool[] doors;
    public DungeonShapeKind kind;
    public string name;
    public Vector2Int center;
    // Quarter turns clockwise seen from above, matching Quaternion.Euler(0, 90 * rotation, 0).
    public int rotation;
    public bool mirrored;

    public DungeonShape(int width, int height)
    {
        this.width = Mathf.Max(1, width); this.height = Mathf.Max(1, height);
        cells = new byte[this.width * this.height];
        doors = new bool[this.width * this.height];
    }

    public byte this[int x, int y]
    {
        get => x < 0 || y < 0 || x >= width || y >= height ? Empty : cells[y * width + x];
        set { if (x >= 0 && y >= 0 && x < width && y < height) cells[y * width + x] = value; }
    }
    public bool IsFloor(int x, int y) => this[x, y] == Floor;
    public bool DoorAllowed(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && doors[y * width + x];
    public void MarkDoor(int x, int y) { if (x >= 0 && y >= 0 && x < width && y < height) doors[y * width + x] = true; }
    public bool HasDoorMarks { get { foreach (var d in doors) if (d) return true; return false; } }

    public int FloorCount { get { int n = 0; foreach (var c in cells) if (c == Floor) n++; return n; } }

    public void Fill(byte value) { for (int i = 0; i < cells.Length; i++) cells[i] = value; }

    public DungeonShape RotatedClockwise()
    {
        // Offset (dx, dy) becomes (dy, -dx): the same turn Unity applies for +90 degrees yaw.
        var r = new DungeonShape(height, width) { kind = kind, name = name, rotation = (rotation + 1) & 3, mirrored = mirrored };
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            r[y, width - 1 - x] = this[x, y];
            if (DoorAllowed(x, y)) r.MarkDoor(y, width - 1 - x);
        }
        r.center = new Vector2Int(center.y, width - 1 - center.x);
        return r;
    }

    public DungeonShape Mirrored()
    {
        var m = new DungeonShape(width, height) { kind = kind, name = name, rotation = rotation, mirrored = !mirrored };
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            m[width - 1 - x, y] = this[x, y];
            if (DoorAllowed(x, y)) m.MarkDoor(width - 1 - x, y);
        }
        m.center = new Vector2Int(width - 1 - center.x, center.y);
        return m;
    }

    public DungeonShape Oriented(System.Random random, bool allowRotation, bool allowMirror)
    {
        var s = this;
        if (allowMirror && random.Next(2) == 1) s = s.Mirrored();
        if (allowRotation) for (int i = random.Next(4); i > 0; i--) s = s.RotatedClockwise();
        return s;
    }

    // The most open floor cell nearest the middle: lights, stairs and enemies gather here.
    public void AutoCenter()
    {
        float mx = (width - 1) * .5f, my = (height - 1) * .5f;
        float best = float.MinValue;
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            if (!IsFloor(x, y)) continue;
            int open = 0;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (IsFloor(x + dx, y + dy)) open++;
            float score = open * 100 - (Mathf.Abs(x - mx) + Mathf.Abs(y - my));
            if (score > best) { best = score; center = new Vector2Int(x, y); }
        }
    }

    public bool OpenAroundCentre()
    {
        for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) if (!IsFloor(center.x + dx, center.y + dy)) return false;
        return true;
    }

    public bool IsConnected()
    {
        int total = FloorCount;
        if (total == 0) return false;
        var start = new Vector2Int(-1, -1);
        for (int i = 0; i < cells.Length && start.x < 0; i++) if (cells[i] == Floor) start = new Vector2Int(i % width, i / width);
        return Flood(start).Count == total;
    }

    HashSet<Vector2Int> Flood(Vector2Int start)
    {
        var seen = new HashSet<Vector2Int> { start };
        var queue = new Queue<Vector2Int>(); queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var d in Dirs)
            {
                var n = p + d;
                if (IsFloor(n.x, n.y) && seen.Add(n)) queue.Enqueue(n);
            }
        }
        return seen;
    }

    static readonly Vector2Int[] Dirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    // ── Procedural shapes ────────────────────────────────────────────

    public static DungeonShape Rectangle(int w, int h)
    {
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.Rectangle, name = "Rectangle" };
        s.Fill(Floor); s.AutoCenter();
        return s;
    }

    // Builds the requested kind inside a w by h box, then turns and mirrors it at random.
    // Kinds that need more room than the box offers fall back to a rectangle.
    public static DungeonShape Create(DungeonShapeKind kind, int w, int h, System.Random random)
    {
        // Grow the box to the smallest size the kind reads as that kind, so every kind appears.
        var least = MinimumSize(kind);
        w = Mathf.Max(least.x, w); h = Mathf.Max(least.y, h);
        DungeonShape s = kind switch
        {
            DungeonShapeKind.CutCorners => CutCorners(w, h, random),
            DungeonShapeKind.LShape => LShape(w, h, random),
            DungeonShapeKind.TShape => TShape(w, h, random),
            DungeonShapeKind.Cross => Cross(w, h, random),
            DungeonShapeKind.PillarHall => PillarHall(w, h, random),
            DungeonShapeKind.Ring => Ring(w, h, random),
            DungeonShapeKind.RoomInRoom => RoomInRoom(w, h, random),
            DungeonShapeKind.Cave => Cave(w, h, random),
            _ => null,
        };
        if (s == null || !s.IsConnected()) s = Rectangle(w, h);
        s.AutoCenter();
        return s.Oriented(random, true, true);
    }

    public static Vector2Int MinimumSize(DungeonShapeKind kind) => kind switch
    {
        DungeonShapeKind.CutCorners or DungeonShapeKind.LShape or DungeonShapeKind.Cave => new Vector2Int(5, 5),
        DungeonShapeKind.TShape => new Vector2Int(7, 5),
        DungeonShapeKind.Cross or DungeonShapeKind.Ring => new Vector2Int(7, 7),
        DungeonShapeKind.PillarHall => new Vector2Int(6, 5),
        DungeonShapeKind.RoomInRoom => new Vector2Int(9, 9),
        _ => new Vector2Int(3, 3),
    };

    static DungeonShape CutCorners(int w, int h, System.Random random)
    {
        if (w < 5 || h < 5) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.CutCorners, name = "Cut corners" };
        s.Fill(Floor);
        int cut = Mathf.Min(w, h) >= 8 ? random.Next(2, 4) : Mathf.Min(w, h) >= 6 ? random.Next(1, 3) : 1;
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
        {
            int dx = Mathf.Min(x, w - 1 - x), dy = Mathf.Min(y, h - 1 - y);
            if (dx + dy < cut) s[x, y] = Empty;
        }
        return s;
    }

    static DungeonShape LShape(int w, int h, System.Random random)
    {
        if (w < 5 || h < 5) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.LShape, name = "L" };
        s.Fill(Floor);
        int a = random.Next(2, w - 2), b = random.Next(2, h - 2);
        for (int x = w - a; x < w; x++) for (int y = h - b; y < h; y++) s[x, y] = Empty;
        return s;
    }

    static DungeonShape TShape(int w, int h, System.Random random)
    {
        if (w < 7 || h < 5) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.TShape, name = "T" };
        int bar = random.Next(2, Mathf.Max(3, h / 2) + 1);
        int stem = random.Next(3, Mathf.Max(4, w / 2)) | 1;   // odd, so it centres
        int left = (w - stem) / 2;
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
            if (y >= h - bar || (x >= left && x < left + stem)) s[x, y] = Floor;
        return s;
    }

    static DungeonShape Cross(int w, int h, System.Random random)
    {
        if (w < 7 || h < 7) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.Cross, name = "Cross" };
        int armX = random.Next(3, Mathf.Max(4, w / 2)) | 1, armY = random.Next(3, Mathf.Max(4, h / 2)) | 1;
        int x0 = (w - armX) / 2, y0 = (h - armY) / 2;
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
            if ((x >= x0 && x < x0 + armX) || (y >= y0 && y < y0 + armY)) s[x, y] = Floor;
        return s;
    }

    static DungeonShape PillarHall(int w, int h, System.Random random)
    {
        if (Mathf.Min(w, h) < 5 || Mathf.Max(w, h) < 6) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.PillarHall, name = "Pillar hall" };
        s.Fill(Floor);
        int cx = (w - 1) / 2, cy = (h - 1) / 2;
        bool grid = Mathf.Min(w, h) >= 7 && random.Next(3) == 0;
        int step = random.Next(2, 4);
        if (grid)
        {
            for (int x = 1 + random.Next(2); x < w - 1; x += step)
                for (int y = 1 + random.Next(2); y < h - 1; y += step)
                    if (x != cx && y != cy) s[x, y] = Solid;
        }
        else
        {
            // Two colonnades along the long axis, one aisle from the walls.
            bool alongX = w >= h;
            int length = alongX ? w : h, across = alongX ? h : w, mid = alongX ? cx : cy;
            int inset = across >= 7 && random.Next(2) == 0 ? 2 : 1;
            for (int i = 1 + random.Next(2); i < length - 1; i += step)
            {
                if (i == mid) continue;
                foreach (int j in new[] { inset, across - 1 - inset })
                    if (alongX) s[i, j] = Solid; else s[j, i] = Solid;
            }
        }
        return s;
    }

    static DungeonShape Ring(int w, int h, System.Random random)
    {
        if (w < 7 || h < 7) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.Ring, name = "Ring" };
        s.Fill(Floor);
        int inset = Mathf.Min(w, h) >= 9 ? random.Next(2, 4) : 2;
        for (int x = inset; x < w - inset; x++) for (int y = inset; y < h - inset; y++) s[x, y] = Solid;
        return s;
    }

    static DungeonShape RoomInRoom(int w, int h, System.Random random)
    {
        if (w < 9 || h < 9) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.RoomInRoom, name = "Room in room" };
        s.Fill(Floor);
        int x0 = 2, y0 = 2, x1 = w - 3, y1 = h - 3;
        for (int x = x0; x <= x1; x++) { s[x, y0] = Solid; s[x, y1] = Solid; }
        for (int y = y0; y <= y1; y++) { s[x0, y] = Solid; s[x1, y] = Solid; }
        switch (random.Next(4))
        {
            case 0: s[(x0 + x1) / 2, y0] = Floor; break;
            case 1: s[(x0 + x1) / 2, y1] = Floor; break;
            case 2: s[x0, (y0 + y1) / 2] = Floor; break;
            default: s[x1, (y0 + y1) / 2] = Floor; break;
        }
        return s;
    }

    static DungeonShape Cave(int w, int h, System.Random random)
    {
        if (w < 5 || h < 5) return null;
        var s = new DungeonShape(w, h) { kind = DungeonShapeKind.Cave, name = "Cave" };
        var grid = new bool[w, h];
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
        {
            // Bias towards the middle so the blob does not hug the box.
            float edge = Mathf.Min(Mathf.Min(x, w - 1 - x) / (w * .5f), Mathf.Min(y, h - 1 - y) / (h * .5f));
            grid[x, y] = random.NextDouble() < .45 + edge * .45;
        }
        for (int pass = 0; pass < 4; pass++)
        {
            var next = new bool[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
            {
                int n = 0;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < w && ny < h && grid[nx, ny]) n++;
                }
                next[x, y] = n >= 5;
            }
            grid = next;
        }
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) s[x, y] = grid[x, y] ? Floor : Empty;
        // Keep the largest pocket only.
        var best = new HashSet<Vector2Int>();
        var seen = new HashSet<Vector2Int>();
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
        {
            var p = new Vector2Int(x, y);
            if (!s.IsFloor(x, y) || seen.Contains(p)) continue;
            var part = s.Flood(p);
            seen.UnionWith(part);
            if (part.Count > best.Count) best = part;
        }
        if (best.Count < Mathf.Max(9, w * h / 3)) return null;
        for (int x = 0; x < w; x++) for (int y = 0; y < h; y++)
            if (!best.Contains(new Vector2Int(x, y))) s[x, y] = Empty;
        return s.Trimmed();
    }

    // Drops empty border rows and columns so the box hugs the floor.
    public DungeonShape Trimmed()
    {
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
            if (this[x, y] != Empty) { minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y); maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y); }
        if (maxX < 0) return this;
        var t = new DungeonShape(maxX - minX + 1, maxY - minY + 1) { kind = kind, name = name, rotation = rotation, mirrored = mirrored };
        for (int x = minX; x <= maxX; x++) for (int y = minY; y <= maxY; y++)
        {
            t[x - minX, y - minY] = this[x, y];
            if (DoorAllowed(x, y)) t.MarkDoor(x - minX, y - minY);
        }
        t.center = center - new Vector2Int(minX, minY);
        return t;
    }
}

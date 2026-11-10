using System.Collections.Generic;
using UnityEngine;

// Draws only what the table player can see on a walled dungeon floor.
//
// Why: the game has no occlusion culling for generated floors, so every room inside the camera's view cone was drawn,
// through walls, and drawn again into the shadow maps of the lamps near the player. Since the entrance moved to the
// floor's edge (facing inward) that was two thirds of the floor from the landing: 795 of 1,185 renderers and 476,000
// triangles in view on a Dungeon1 floor, 1.9 million drawn a frame. Walls stop sight, so none of it can be seen.
//
// How: the floor is a grid of cells and a cell is either floor or solid, with no thin walls between two floor cells
// (DungeonLayout.IsOpening). A cell can be seen when a straight line to it from somewhere in the eye's own cell runs
// over floor cells only (rays cast outward, worked out once per cell and kept).
// The seen cells are widened by a cell (the walls round them, and whatever moved since the last look), and so is the
// reach of every lamp that casts shadows just now, because a thing round the corner can still throw its shadow into
// view. Everything else is not drawn: the picture is the same, pixel for pixel (checked by drawing each instant both
// ways; see the commit).
//
// What is hidden: props, furniture, torches, doors, chests, breakables and everyone standing on the floor. The walls,
// floors and ceilings are never hidden: through a hairline seam between two wall pieces the eye catches whatever stands
// right behind, and with the next room's wall held back that hairline changed colour (found by the pixel comparison).
//
// The walls are still drawn differently inside. For play they are merged into 32-cell chunks (64 m), which is what the
// room's view of the whole table wants. But every lamp that casts shadows draws its surroundings into six shadow maps,
// and a 64 m chunk lies in all of them, whole: half a floor's frame was wall chunks drawn again and again into shadow
// maps. So while the player is inside, the big wall chunks stand down for the 8-cell chunks they were merged from (the
// same vertices to the last bit, DungeonStaticGeometry.Coarsen), and each shadow map draws only the few near its lamp.
// The standing props, merged into 32-cell chunks at build, stand down the same way for the props themselves, which are
// then drawn where seen. From anywhere else the big chunks are back, so the cost outside is as before.
//
// It stands down (everything drawn) whenever the eye is not inside the floor's rooms (the room's view of the table, the
// flights in and out), on a boss stage (an open platform that looks over the roofs), while the Dungeon Master reaches
// in (he lifts a roof), and with WorldManager.drawOnlySeenRooms off. Portal pockets, which stand far beside the floor
// and are only ever seen from inside, are not drawn until the player is in one; the floor is then not drawn instead.
//
// Only Renderer.forceRenderingOff is used (never enabled), on renderers this component took note of when the floor
// went into play; things made later (loot, debris, the boss's stairs) are simply always drawn.
public class DungeonVisibility : MonoBehaviour
{
    // ── What can be hidden ──
    struct Unit
    {
        public Renderer renderer;
        public int minX, maxX, minY, maxY;   // cells its bounds cover
        public bool tall;                    // reaches above one storey: seen over the roofs from a room with a skylight
        public bool always;                  // a piece of wall or floor: drawn whenever the player is inside
        public bool hidden;
    }
    readonly List<Unit> units = new();
    // Big merged chunks that stand down while the player is inside, and what is drawn in their place.
    readonly List<Renderer> parents = new();
    readonly List<(Character who, Renderer[] parts, bool hidden)> actors = new();
    readonly List<(DungeonGenerator pocket, Renderer[] parts, bool hidden)> pockets = new();

    DungeonGenerator dungeon;
    DungeonLayout layout;
    bool[,] seen, reach;
    int width, depth;
    float cell, storey;

    enum Mode { All, Seen, None }
    Mode mode = Mode.All;
    Vector3 lookedFrom = new(float.MaxValue, 0f, 0f);
    bool lookedOpenRoof;
    float nextLook, nextRoll;
    int shadowStamp;

    // Seconds between looks while standing still (lamps change who casts shadows; doors and lids don't matter: sight
    // never counts them), metres the eye may move before a new look, and how far round the eye is always drawn.
    const float LookEvery = .25f, LookAfterMoving = .3f, AlwaysNear = 2.5f;

    public static bool On => !WorldManager.HasInstance || WorldManager.Instance.drawOnlySeenRooms;
    // For tests: the cells seen at the last look, and how many things are held back.
    public bool[,] SeenCells => seen;
    public int HiddenCount { get; private set; }
    public string State => mode.ToString();

    public void Initialize(DungeonGenerator floor)
    {
        dungeon = floor; layout = floor.Layout;
        width = layout.floor.GetLength(0); depth = layout.floor.GetLength(1);
        seen = new bool[width, depth]; reach = new bool[width, depth];
        cell = floor.LevelData.cellSize; storey = floor.LevelData.architectureTileSize;
        Collect();
    }

    // ── Taking note of the floor ──
    void Collect()
    {
        units.Clear(); parents.Clear(); standIns.Clear();
        var geometry = dungeon.GetComponent<DungeonStaticGeometry>();
        var ceilings = dungeon.Ceilings;
        var taken = new HashSet<Renderer>();
        // A big merged chunk stands down, while the player is inside, for the chunks or pieces it was made of: wall
        // chunks all drawn, props where seen.
        void Split(MeshRenderer big, List<MeshRenderer> smaller, bool standIn)
        {
            if (big == null || !taken.Add(big)) return;
            parents.Add(big);
            // A chunk that itself stands in for a bigger one is switched on but held back, like its pieces: from now
            // on only forceRenderingOff says whether any of them is drawn.
            if (standIn) { big.forceRenderingOff = true; big.enabled = true; standIns.Add(big); }
            foreach (var small in smaller)
            {
                if (small == null || taken.Contains(small)) continue;
                bool prop = geometry.PropChunks.Contains(small) || !geometry.PiecesOf.ContainsKey(small);
                if (geometry.PiecesOf.TryGetValue(small, out var pieces) && prop && Wide(small)) { Split(small, pieces, true); continue; }
                taken.Add(small); standIns.Add(small);
                small.forceRenderingOff = true; small.enabled = true;
                Add(small, true, !prop);
            }
        }
        if (geometry != null)
        {
            foreach (var pair in geometry.MergedFrom)
                if (pair.Key != null && pair.Key.enabled && !pair.Key.forceRenderingOff && !(ceilings != null && pair.Key.transform.IsChildOf(ceilings))) Split(pair.Key, pair.Value, false);
            foreach (var pair in geometry.PiecesOf)
                if (pair.Key != null && pair.Key.enabled && !taken.Contains(pair.Key) && geometry.PropChunks.Contains(pair.Key) && Wide(pair.Key)) Split(pair.Key, pair.Value, false);
        }
        foreach (var r in dungeon.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || taken.Contains(r)) continue;
            if (r.name == StaticChunk && (geometry == null || !geometry.PropChunks.Contains(r as MeshRenderer))) continue;   // walls and floors: as they are
            if (ceilings != null && r.transform.IsChildOf(ceilings)) continue;                    // roofs: few, and the lid's
            if (r.GetComponentInParent<Character>() != null) continue;                            // by where they stand
            if (r.GetComponentInParent<Rigidbody>() != null || r.GetComponentInParent<WorldItem>() != null) continue;   // may move
            if (r.GetComponentInParent<Canvas>() != null) continue;
            Add(r, false, false);
        }
        RollCall();
    }

    const string StaticChunk = "Static chunk";
    bool Wide(Renderer r) => r.bounds.size.x > cell * 10f || r.bounds.size.z > cell * 10f;

    void Add(Renderer r, bool hidden, bool always)
    {
        var b = r.bounds;
        var min = dungeon.CellOf(b.min); var max = dungeon.CellOf(b.max);
        units.Add(new Unit
        {
            renderer = r, hidden = hidden, always = always,
            minX = Mathf.Clamp(min.x - 1, 0, width - 1), maxX = Mathf.Clamp(max.x + 1, 0, width - 1),
            minY = Mathf.Clamp(min.y - 1, 0, depth - 1), maxY = Mathf.Clamp(max.y + 1, 0, depth - 1),
            tall = b.max.y > dungeon.transform.position.y + storey + .3f,
        });
    }

    // Who stands on the floor now, and its pockets.
    void RollCall()
    {
        for (int i = actors.Count - 1; i >= 0; i--)
            if (actors[i].who == null) { actors.RemoveAt(i); }
        var known = new HashSet<Character>(); foreach (var a in actors) known.Add(a.who);
        foreach (var c in dungeon.GetComponentsInChildren<Character>())
            if (known.Add(c)) actors.Add((c, null, false));
        var found = new List<DungeonGenerator>(); PortalArch.PocketsOf(dungeon, found);
        foreach (var p in found)
        {
            bool have = false; foreach (var e in pockets) if (e.pocket == p) { have = true; break; }
            if (!have && p != null) pockets.Add((p, null, false));
        }
        for (int i = pockets.Count - 1; i >= 0; i--) if (pockets[i].pocket == null) pockets.RemoveAt(i);
    }

    // ── For tests ──
    // While held the component leaves the floor as the tester set it: looked at from a spot of their choosing, or
    // everything drawn.
    public bool Held;
    public void LookFrom(Vector3 eye) { ShadowStamp(); Look(eye); mode = Mode.Seen; ApplySeen(OpenRoof(eye)); SeeActors(); }
    public void ShowEverything() { mode = Mode.All; lookedFrom.x = float.MaxValue; ApplyAll(false); }

    // ── Each frame ──
    void LateUpdate()
    {
        if (dungeon == null || layout == null || Held) return;
        if (Time.unscaledTime >= nextRoll) { nextRoll = Time.unscaledTime + 1f; RollCall(); }
        var cam = Camera.main;
        var eye = cam != null ? cam.transform.position : Vector3.zero;
        DungeonGenerator inPocket = null;
        foreach (var p in pockets) if (p.pocket != null && Inside(p.pocket, eye)) { inPocket = p.pocket; break; }
        bool inside = cam != null && inPocket == null && Inside(dungeon, eye);
        Mode want = !On || cam == null ? Mode.All
            : inPocket != null ? Mode.None
            : !inside || OnStage(eye) || DungeonMasterReach.Busy ? Mode.All
            : Mode.Seen;
        for (int i = 0; i < pockets.Count; i++) ShowPocket(i, !On || cam == null || pockets[i].pocket == inPocket);
        if (want != Mode.Seen)
        {
            if (mode != want) { mode = want; lookedFrom.x = float.MaxValue; ApplyAll(want == Mode.None); }
            else if (want == Mode.None) HideActors(true);   // those that walked in or were set down since
            return;
        }
        bool openRoof = OpenRoof(eye);
        int stamp = ShadowStamp();
        bool look = mode != Mode.Seen || dungeon.CellOf(eye) != dungeon.CellOf(lookedFrom) || (eye - lookedFrom).sqrMagnitude > LookAfterMoving * LookAfterMoving
            || openRoof != lookedOpenRoof || stamp != shadowStamp || Time.unscaledTime >= nextLook;
        mode = Mode.Seen;
        if (look)
        {
            lookedFrom = eye; lookedOpenRoof = openRoof; shadowStamp = stamp; nextLook = Time.unscaledTime + LookEvery;
            Look(eye);
            ApplySeen(openRoof);
        }
        SeeActors();
    }

    static bool Inside(DungeonGenerator d, Vector3 eye)
    {
        var l = d.Layout; if (l == null) return false;
        var c = d.CellOf(eye);
        if (!l.InBounds(c) || !l.floor[c.x, c.y]) return false;
        float floorY = d.Cell(c).y;
        return eye.y > floorY - 6f && eye.y < floorY + d.CeilingHeight(c) + .2f;
    }

    bool OnStage(Vector3 eye)
    {
        if (dungeon.StageRoom < 0) return false;
        return layout.RoomAt(dungeon.CellOf(eye)) == dungeon.StageRoom;
    }

    bool OpenRoof(Vector3 eye)
    {
        var c = dungeon.CellOf(eye); var open = dungeon.OpenRoofRegions;
        if (open == null || !layout.InBounds(c)) return false;
        int region = layout.RegionIds[c.x, c.y];
        return region >= 0 && region < open.Length && open[region];
    }

    // ── What the eye sees ──
    readonly List<Light> lamps = new();
    float nextLamps;

    // Lamps that cast shadows now (the nearest few, and the light the player carries): a change means a new look.
    int ShadowStamp()
    {
        if (Time.unscaledTime >= nextLamps)
        {
            nextLamps = Time.unscaledTime + 1f; lamps.Clear();
            foreach (var l in dungeon.GetComponentsInChildren<Light>(true)) lamps.Add(l);
            var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
            if (player != null) foreach (var l in player.GetComponentsInChildren<Light>(true)) lamps.Add(l);
        }
        int stamp = 17;
        foreach (var l in lamps) if (l != null && l.isActiveAndEnabled && l.shadows != LightShadows.None) stamp = unchecked(stamp * 31 + l.GetHashCode());
        return stamp;
    }

    // What can be seen from anywhere inside each floor cell, worked out the first time the eye stands in it and kept
    // (the floor plan never changes): a third of a millisecond once per cell, where testing every cell against the eye
    // on every look took over a millisecond each time.
    bool[][] fromCell;
    static readonly float[] Corner = { 0f, -.45f, .45f };
    const int Rays = 720;

    bool[] SeenFrom(int cx, int cy)
    {
        fromCell ??= new bool[width * depth][];
        int id = cx * depth + cy;
        var s = fromCell[id];
        if (s != null) return s;
        s = fromCell[id] = new bool[width * depth];
        // Rays outward, half a degree apart, from the cell's middle, corners and edges: every cell a ray crosses before
        // it meets a solid cell can be seen (and that solid cell too: it is the wall the ray ends on).
        foreach (float ox in Corner) foreach (float oy in Corner)
            for (int a = 0; a < Rays; a++)
            {
                float angle = a * (Mathf.PI * 2f / Rays);
                March(cx + ox, cy + oy, Mathf.Cos(angle), Mathf.Sin(angle), s);
            }
        return s;
    }

    // Walks the grid cells a ray crosses (cell p covers p - .5 to p + .5), marking each, until one is solid.
    void March(float ax, float ay, float dx, float dy, bool[] mark)
    {
        float x = ax + .5f, y = ay + .5f;
        int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
        int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1;
        float tMaxX = Mathf.Abs(dx) > 1e-6f ? ((sx > 0 ? cx + 1 : cx) - x) / dx : float.MaxValue;
        float tMaxY = Mathf.Abs(dy) > 1e-6f ? ((sy > 0 ? cy + 1 : cy) - y) / dy : float.MaxValue;
        float tDeltaX = Mathf.Abs(dx) > 1e-6f ? sx / dx : float.MaxValue, tDeltaY = Mathf.Abs(dy) > 1e-6f ? sy / dy : float.MaxValue;
        for (int guard = 0; guard < 400; guard++)
        {
            if (cx < 0 || cy < 0 || cx >= width || cy >= depth) return;
            mark[cx * depth + cy] = true;
            if (!layout.floor[cx, cy]) return;
            if (tMaxX < tMaxY) { cx += sx; tMaxX += tDeltaX; } else { cy += sy; tMaxY += tDeltaY; }
        }
    }

    void Look(Vector3 eye)
    {
        System.Array.Clear(seen, 0, seen.Length); System.Array.Clear(reach, 0, reach.Length);
        var origin = dungeon.transform.position;
        // The eye in cell units (cell p is centred on p).
        float ex = (eye.x - origin.x) / cell + width * .5f, ey = (eye.z - origin.z) / cell + depth * .5f;
        int cx = Mathf.Clamp(Mathf.RoundToInt(ex), 0, width - 1), cy = Mathf.Clamp(Mathf.RoundToInt(ey), 0, depth - 1);
        var from = SeenFrom(cx, cy);
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++) seen[x, y] = from[x * depth + y];
        Disc(ex, ey, AlwaysNear / cell);
        // What stands within reach of a lamp that casts shadows can throw one into view from round a corner.
        foreach (var l in lamps)
        {
            if (l == null || !l.isActiveAndEnabled || l.shadows == LightShadows.None || l.type == LightType.Directional) continue;
            var p = l.transform.position;
            Disc((p.x - origin.x) / cell + width * .5f, (p.z - origin.z) / cell + depth * .5f, l.range / cell + 1f);
        }
        // Widened by a cell: the walls round what is seen, and a step of whatever moves.
        for (int x = 0; x < width; x++)
            for (int y = 0; y < depth; y++)
            {
                if (!seen[x, y]) continue;
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
                { int nx = x + dx, ny = y + dy; if (nx >= 0 && ny >= 0 && nx < width && ny < depth) reach[nx, ny] = true; }
            }
    }

    void Disc(float cx, float cy, float radius)
    {
        int r = Mathf.CeilToInt(radius);
        for (int x = Mathf.Max(0, Mathf.RoundToInt(cx) - r); x <= Mathf.Min(width - 1, Mathf.RoundToInt(cx) + r); x++)
            for (int y = Mathf.Max(0, Mathf.RoundToInt(cy) - r); y <= Mathf.Min(depth - 1, Mathf.RoundToInt(cy) + r); y++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius) seen[x, y] = true;
    }

    // ── Showing and hiding ──
    void ApplySeen(bool openRoof)
    {
        foreach (var p in parents) if (p != null && !p.forceRenderingOff) p.forceRenderingOff = true;
        int hiddenNow = 0;
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u.renderer == null) continue;
            bool show = u.always || openRoof && u.tall;
            for (int x = u.minX; x <= u.maxX && !show; x++)
                for (int y = u.minY; y <= u.maxY; y++)
                    if (reach[x, y]) { show = true; break; }
            if (!show) hiddenNow++;
            if (u.hidden == !show) continue;
            u.hidden = !show; u.renderer.forceRenderingOff = !show; units[i] = u;
        }
        HiddenCount = hiddenNow;
    }

    // Everything as it was drawn before this component (or nothing, while the player is in a pocket).
    void ApplyAll(bool nothing)
    {
        // The biggest chunks are drawn again; one that stood in for a bigger one stays behind it.
        foreach (var p in parents) if (p != null) p.forceRenderingOff = nothing || standIns.Contains(p);
        for (int i = 0; i < units.Count; i++)
        {
            var u = units[i];
            if (u.renderer == null) continue;
            // A piece that stands in for a big chunk goes back behind it; a thing of its own is drawn again.
            bool hide = nothing || standIns.Contains(u.renderer);
            if (u.hidden != hide || u.renderer.forceRenderingOff != hide) { u.hidden = hide; u.renderer.forceRenderingOff = hide; units[i] = u; }
        }
        HideActors(nothing);
        HiddenCount = nothing ? units.Count : 0;
    }

    // Chunks and pieces that are drawn only while the player is inside, in place of a bigger chunk.
    readonly HashSet<Renderer> standIns = new();

    void SeeActors()
    {
        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i];
            if (a.who == null) continue;
            var c = dungeon.CellOf(a.who.transform.position);
            bool show = c.x < 0 || c.y < 0 || c.x >= width || c.y >= depth || reach[c.x, c.y];
            if (a.hidden == !show) continue;
            SetActor(i, !show);
        }
    }

    void HideActors(bool hide)
    {
        for (int i = 0; i < actors.Count; i++) if (actors[i].who != null && actors[i].hidden != hide) SetActor(i, hide);
    }

    void SetActor(int i, bool hide)
    {
        var a = actors[i];
        // Its renderers now (gear comes and goes); on showing, also whatever it gained while hidden.
        if (a.parts != null) foreach (var r in a.parts) if (r != null) r.forceRenderingOff = false;
        a.parts = hide ? a.who.GetComponentsInChildren<Renderer>() : null;
        if (hide) foreach (var r in a.parts) r.forceRenderingOff = true;
        a.hidden = hide; actors[i] = a;
    }

    void ShowPocket(int i, bool show)
    {
        var p = pockets[i];
        if (p.pocket == null || p.hidden == !show) return;
        if (p.parts != null) foreach (var r in p.parts) if (r != null) r.forceRenderingOff = false;
        p.parts = show ? null : p.pocket.GetComponentsInChildren<Renderer>();
        if (!show) foreach (var r in p.parts) r.forceRenderingOff = true;
        p.hidden = !show; pockets[i] = p;
    }

    // Leaving: nothing of this component's stays behind on a renderer that outlives it.
    void OnDisable()
    {
        if (dungeon == null) return;
        mode = Mode.All; lookedFrom.x = float.MaxValue;
        ApplyAll(false);
        for (int i = 0; i < pockets.Count; i++) ShowPocket(i, true);
    }
}

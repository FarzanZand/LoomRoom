using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A hand-built room dropped into a generated dungeon: the generator still builds the walls,
// floor, ceiling and doors, then places this prefab at the room centre instead of rolling
// random props and containers.
//
// One prefab can hold the whole room: paint its outline here (Paint Footprint) and pick its
// style and height. Place wall torches with Wall Light sockets, like any other socket. Opened in
// Prefab Mode it shows the room's floor, walls, socket torches and pits in that style (not saved), so
// props can be placed against the real walls. Pits and sunken floors are dragged out on the footprint grid. Children without DungeonTemplatePiece are always kept.
//
// Author the prefab around its pivot (the room centre). Keep the centre cross and the
// doorways walkable: any child with DungeonTemplatePiece that lands on a walkway is
// removed at placement. Put enemies, chests, breakables, the boss and the merchant on
// DungeonSocket children.
//
// Reference it from a DungeonRoomProfile (Room Template), a level milestone (Arena) or the
// level's Starting Room. Grown layouts build the room in its footprint, with this prefab's
// pivot on the footprint's centre cell. The room may be turned in quarter turns (never
// mirrored); the prefab turns with it.
// What happens in a hand-built combat room. Serialized by value: append only.
public enum DungeonRoomEvent { Rolled = 0, None = 1, Ambush = 2, SleepingPack = 3 }
// What kind of room it is, for sorting rooms in the Asset Studio (nothing in the game reads it). Scripted: a room with an
// event of its own (the Skeleton Party, the tavern, a boss stage); Active: something in it does something (spikes, a
// chest, a shrine, a pit, a portal); Visual: only decor. Serialized by value: append only.
public enum DungeonRoomKind { Unsorted = 0, Scripted = 1, Active = 2, Visual = 3 }

[ExecuteAlways]
public class DungeonRoomTemplate : MonoBehaviour
{
    bool UsesAsset => !paintFootprint;

    [Tooltip("What kind of room this is, for sorting rooms in the Asset Studio (right-click a room there to set it). Scripted: an event of its own; Active: something in it does something (spikes, a chest, a shrine); Visual: only decor. The game doesn't read it.")]
    public DungeonRoomKind kind;
    [TextArea(2, 5), Tooltip("For the designer: what this room is and what it does in a run, in a sentence or two (not what props are in it). Shown in the Asset Studio. The game doesn't read it.")]
    public string description;

    [Title("Footprint")]
    [Tooltip("Paint the room's outline, pillars and door cells right here instead of referencing a Room Shape asset.")]
    public bool paintFootprint;
    [ShowIf(nameof(UsesAsset)), Tooltip("Grown layouts: the room's outline, pillars and door cells. Empty builds a rectangle of at least Minimum Cells.")]
    public DungeonRoomShape footprint;
    [ShowIf(nameof(paintFootprint)), Tooltip("The generator may turn the room in quarter turns. The prefab turns with it.")]
    public bool allowRotation = true;
    [HideInInspector] public int width = 7, height = 7;
    // rows[0] is the south (bottom) row: see DungeonShapeGrid.
    [HideInInspector] public string[] rows;
    [HideIf(nameof(HasFootprint)), Tooltip("Smallest room, in grid cells, this interior is built for. Grown layouts make the room at least this big when it fits; a smaller room still gets the interior, without the pieces that fall outside it.")]
    public Vector2Int minimumCells = new Vector2Int(6, 6);

    [Title("Architecture")]
    [Tooltip("Walls, floor and ceiling for this room. Empty keeps the profile's or the level's choice.")]
    public DungeonRoomStyle style;
    [Range(0, 3), Tooltip("Room height in wall tiles. Rooms and corridors are one tile high; set 2 or 3 only for a room that is tall for effect (a gallery, a chapel). 0 = 1.")]
    public int heightTiles;

    [Tooltip("A very large skylight over the whole room: only a thin frame of ceiling stays, so the room above shows through. The starting room uses it for the way in.")]
    public bool openCeiling;
    [ShowIf(nameof(openCeiling)), Range(.1f, 2f), Tooltip("Width of the ceiling frame left round the skylight, in world units.")]
    public float ceilingFrame = .35f;
    [ShowIf(nameof(openCeiling)), Min(0), Tooltip("Open only the cells at most this many steps from the room's centre cell (a moonlit court's opening). 0 opens the whole room.")]
    public int skylightRadius;

    [AssetsOnly, Tooltip("A door on every way into this room (a dungeon door prefab, or a variant of one such as the Tavern door), in place of the level's random doors there. Only for the merchant's room (the shop room) for now. Empty: the level's doors as usual.")]
    public GameObject wayInDoor;

    [Tooltip("A way through (the bridge rooms; the user, 2026-11-13): every door marked on its footprint gets a passage, so the room is entered at one end and left at the other, never a dead end, and every way in gets a door (the level's, unless Way In Door names another). Put into a room already laid out, that room must have a way in at each end a door is marked at.")]
    public bool wayThrough;

    [Title("Contents")]
    [Tooltip("Skip the generator's random props, supply container and features in this room.")]
    public bool replaceGeneratedProps = true;
    [HideIf(nameof(replaceGeneratedProps)), Tooltip("Also furnish the room from the level's and biome's prop rules (trees, plants, rocks...) around its own pieces.")]
    public bool addFurnishing;
    // Retired: a room's Enemy sockets always count toward its roll now (generated enemies only make up the rest, so a
    // room holds no more than its roll unless Override Enemy Count Restrictions is on, DungeonGenerator.RoomCap).
    [HideInInspector] public bool replaceGeneratedEnemies = true;
    [Tooltip("Only this room's own Enemy sockets: the floor never adds generated enemies to make up its roll (a set piece with an exact cast, like the Skeleton Party room).")]
    public bool socketEnemiesOnly;
    [Tooltip("Keep every enemy this room's author put in it (Enemy sockets, spawners, enemies dragged in), whatever the floor's enemies per room. Off: the room keeps to the same count as a generated room on that floor (the biome's Min-Max Enemies Per Room for a combat room, one guard in a treasure or storage room, none in any other), its sockets only places to put them. On for the scripted event rooms (the user, 2026-11-15).")]
    public bool overrideEnemyCountRestrictions;
    [Tooltip("Combat rooms: Rolled lets the floor roll an ambush or a sleeping pack like any room; Ambush and Sleeping Pack make it one (when the level allows it); None never.")]
    public DungeonRoomEvent roomEvent;
    [Tooltip("Dealt only while this class is still locked: once it is unlocked the room is left out of every floor (the Duel bridge: Little Undead John only until the Necromancer is unlocked, the user, 2026-11-12). Empty: always.")]
    public AdventurerClass onlyWhileLocked;

    // Done for this run: the Skeleton Party once its dancers are all dead or its chest is opened (SkeletonParty.ClearedFlag), and any
    // room whose portal leads to a room that is (the Portal hall). Done for good: a room whose class (Only While Locked)
    // is unlocked. Left out of every floor's pools.
    public bool Gone
    {
        get
        {
            if (!ProgressionManager.HasInstance) return false;
            if (onlyWhileLocked != null && onlyWhileLocked.Unlocked) return true;
            if (GetComponent<SkeletonParty>() != null && ProgressionManager.Instance.HasFlag(SkeletonParty.ClearedFlag)) return true;
            foreach (var arch in GetComponentsInChildren<PortalArch>(true))
            {
                var target = arch.room != null ? arch.room.GetComponent<DungeonRoomTemplate>() : null;
                if (target != null && target != this && target.GetComponent<SkeletonParty>() != null && ProgressionManager.Instance.HasFlag(SkeletonParty.ClearedFlag)) return true;
            }
            return false;
        }
    }

    // The room features this room is built around (a chasm, a sunken court, an archers' ledge, its light, a shrine):
    // a floor whose features switch one of them off leaves the room out.
    public DungeonRoomFeatureMask Requires()
    {
        var mask = DungeonRoomFeatureMask.None;
        foreach (var cut in GetComponentsInChildren<DungeonFloorCut>(true)) mask |= (DungeonRoomFeatureMask)(1 << (int)cut.Feature);
        foreach (var socket in GetComponentsInChildren<DungeonSocket>(true)) if (socket.archer) mask |= DungeonRoomFeatureMask.ArcherLedge;
        if (GetComponentInChildren<DungeonShrine>(true) != null) mask |= DungeonRoomFeatureMask.ShrineBargain;
        foreach (var plate in GetComponentsInChildren<DungeonSpikeTrap>(true)) if (plate.springOnStep) { mask |= DungeonRoomFeatureMask.TrappedTreasure; break; }
        var mood = GetComponent<DungeonRoomMood>();
        if (mood != null && mood.kind != DungeonRoomLight.Normal)
            mask |= mood.kind switch
            {
                DungeonRoomLight.Dark => DungeonRoomFeatureMask.Dark,
                DungeonRoomLight.Candlelit => DungeonRoomFeatureMask.Candlelit,
                DungeonRoomLight.Moonlit => DungeonRoomFeatureMask.Moonlit,
                _ => DungeonRoomFeatureMask.Eerie,
            };
        return mask;
    }

    [Title("Lighting")]
    [Tooltip("No random wall lights in this room: only the ones placed with Wall Light sockets.")]
    public bool replaceGeneratedTorches;
    [Tooltip("Recolour every wall light in this room, over the biome's colours. A socket's own colour still wins.")]
    public bool overrideLightColor;
    [ShowIf(nameof(overrideLightColor))]
    public Color lightColor = new Color(1f, .6f, .3f);

    [Title("Dungeon Master")]
    [TextArea(1, 3), Tooltip("Said when the table player first walks in. One is picked at random. Empty: nothing.")]
    public string[] enterLines = new string[0];
    [Range(0, 1), Tooltip("Chance a line is said.")]
    public float enterChance = 1f;
    [Tooltip("Said across the table, without the \"voice inside your head\" lead-in.")]
    public bool enterInPerson;

    // A random entry line, or null.
    public DungeonMaster.Line PickEnterLine()
    {
        if (enterLines == null || enterLines.Length == 0 || Random.value > enterChance) return null;
        var text = enterLines[Random.Range(0, enterLines.Length)];
        return string.IsNullOrWhiteSpace(text) ? null : new DungeonMaster.Line { text = text, inPerson = enterInPerson, mumble = true };
    }

    [Title("Preview")]
    [Tooltip("Level whose materials, cell size and wall torch the Prefab Mode preview uses.")]
    public TableLevelData previewLevel;
    [Tooltip("Grid spacing used by the gizmo when there is no preview level. Match the level's Cell Size.")]
    [Min(.5f)] public float previewCellSize = 2f;

    public bool HasFootprint => paintFootprint || footprint != null;

    // The outline as painted, before any turn.
    public DungeonShape Footprint()
    {
        var shape = paintFootprint ? DungeonShapeGrid.ToShape(width, height, rows, name) : footprint != null ? footprint.ToShape() : null;
        if (shape != null) shape.everyDoor = wayThrough;
        return shape;
    }

    // The outline as the generator places it: maybe turned, never mirrored.
    public DungeonShape FootprintShape(System.Random random) =>
        paintFootprint ? Footprint().Oriented(random, allowRotation, false) : footprint.ToShape(random, false);

    float CellSize => previewLevel != null ? previewLevel.cellSize : previewCellSize;

    void OnDrawGizmos()
    {
        // A room of a map template: the map draws it.
        if (transform.parent != null && GetComponentInParent<DungeonMapTemplate>() != null) return;
        float c = CellSize;
        var shape = HasFootprint ? Footprint() : null;
        if (shape != null)
        {
            // Pivot sits on the centre cell; draw each painted cell around it.
            Gizmos.matrix = transform.localToWorldMatrix;
            var cut = new HashSet<Vector2Int>();
#if UNITY_EDITOR
            foreach (var (_, cells) in CutCells(shape, c)) cut.UnionWith(cells);
#endif
            for (int x = 0; x < shape.width; x++) for (int y = 0; y < shape.height; y++)
            {
                byte cell = shape[x, y];
                if (cell == DungeonShape.Empty || cut.Contains(new Vector2Int(x, y))) continue;
                var p = new Vector3((x - shape.center.x) * c, 0, (y - shape.center.y) * c);
                Gizmos.color = cell == DungeonShape.Solid ? new Color(.2f, .2f, .2f, .8f) : shape.DoorAllowed(x, y) ? new Color(1f, .8f, .3f, .5f) : new Color(.4f, .8f, 1f, .25f);
                if (cell == DungeonShape.Solid) Gizmos.DrawCube(p + Vector3.up * c * .5f, new Vector3(c, c, c));
                else Gizmos.DrawCube(p + Vector3.up * .02f, new Vector3(c * .96f, .02f, c * .96f));
            }
            return;
        }
        var size = new Vector3(minimumCells.x * c, .05f, minimumCells.y * c);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(.4f, .8f, 1f, .6f);
        Gizmos.DrawWireCube(Vector3.zero, size);
        // The centre cross stays walkable: doorways connect through it.
        Gizmos.color = new Color(1f, .8f, .3f, .25f);
        Gizmos.DrawCube(Vector3.zero, new Vector3(size.x, .02f, c));
        Gizmos.DrawCube(Vector3.zero, new Vector3(c, .02f, size.z));
    }

    [Button("Snap children to grid"), PropertyTooltip("Rounds every socket and piece to the cell grid.")]
    void Snap()
    {
        float cellSize = CellSize;
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            if (child == transform || (child.GetComponent<DungeonSocket>() == null && child.GetComponent<DungeonTemplatePiece>() == null && child.GetComponent<EnemySpawner>() == null)) continue;
            var p = child.localPosition;
            float half = cellSize * .5f;
            float Snap1(float v, int cells) => (cells % 2 == 0 ? Mathf.Round((v - half) / cellSize) * cellSize + half : Mathf.Round(v / cellSize) * cellSize);
            int cx = HasFootprint ? 1 : minimumCells.x, cz = HasFootprint ? 1 : minimumCells.y; // a footprint pivot sits on a cell centre
            child.localPosition = new Vector3(Snap1(p.x, cx), p.y, Snap1(p.z, cz));
        }
    }

#if UNITY_EDITOR
    // ── Enemy spawners ────────────────────────────────────────────────

    const string SpawnerPrefab = "Assets/Game/Characters/Enemies/Enemy Spawner.prefab";

    // An Enemy Spawner in the room, a cell from the last one so they don't stack; selected, so it can be moved and
    // given an enemy at once (empty = the room's or biome's pick).
    [Button("Add enemy spawner"), PropertyTooltip("Adds an Enemy Spawner to this room and selects it. Leave its Enemy empty for one from the biome, or set one.")]
    void AddEnemySpawner()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SpawnerPrefab);
        if (prefab == null) { Debug.LogWarning($"No Enemy Spawner prefab at {SpawnerPrefab}.", this); return; }
        int n = GetComponentsInChildren<EnemySpawner>(true).Length;
        var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
        UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Add enemy spawner");
        float c = CellSize;
        go.transform.localPosition = new Vector3(((n % 3) - 1) * c, 0f, (n / 3 + 1) * c);
        UnityEditor.Selection.activeGameObject = go;
    }

    // ── Footprint painter ─────────────────────────────────────────────

    [OnInspectorGUI, PropertyOrder(-1), ShowIf(nameof(paintFootprint))]
    void Painter()
    {
        if (DungeonShapeGrid.Painter(this, ref width, ref height, ref rows, CutTools())) QueuePreview();
    }

    // ── Pits on the footprint ─────────────────────────────────────────
    // Pits and sunken floors are DungeonFloorCut children (they keep their own depth, fill and lights); the painter shows
    // them over the cells and makes them: the Pit or Sunken brush drags out a rectangle and adds a cut there, with the
    // settings of the last cut of that kind in this room (a lava pit makes lava pits); Erase on a cut removes it.

    static readonly Color PitColour = new(.05f, .04f, .05f), LavaColour = new(.85f, .35f, .08f), SunkenColour = new(.3f, .45f, .75f);

    static Color CutColour(DungeonFloorCut cut) => cut.kind == DungeonFloorCut.Kind.Sunken ? SunkenColour : cut.fill != null ? LavaColour : PitColour;

    DungeonShapeGrid.Extras CutTools()
    {
        var shape = Footprint();
        float c = CellSize;
        // The cuts as they are now: a stroke or an erase replaces cut objects in the middle of a redraw, so the list is
        // taken again after each change (an old one pointed at destroyed cuts: MissingReferenceException).
        List<(DungeonFloorCut cut, HashSet<Vector2Int> cells)> cuts = null;
        List<(DungeonFloorCut cut, HashSet<Vector2Int> cells)> Cuts()
        {
            if (cuts == null || cuts.Exists(k => k.cut == null)) cuts = CutCells(shape, c);
            return cuts;
        }
        bool marked = shape.HasDoorMarks;
        return new DungeonShapeGrid.Extras
        {
            brushes = new[] { ('P', "Pit", new Color(.45f, .2f, .12f)), ('S', "Sunken floor", SunkenColour) },
            overlay = (x, y) =>
            {
                var cell = new Vector2Int(x, y);
                foreach (var (cut, cells) in Cuts()) if (cut != null && cells.Contains(cell)) return CutColour(cut);
                return null;
            },
            stroke = (brush, painted) =>
            {
                bool done = PaintCuts(brush == 'S' ? DungeonFloorCut.Kind.Sunken : DungeonFloorCut.Kind.Pit, painted, shape, c);
                cuts = null;
                return done;
            },
            // A plain brush on a pit cell takes that cell out of the pit: Erase does only that, the others then paint
            // the cell as usual (Floor over a pit fills it in).
            click = (brush, x, y) =>
            {
                bool took = EraseCut(new Vector2Int(x, y), shape, c);
                if (took) { cuts = null; UnityEditor.EditorUtility.SetDirty(this); }
                if (brush == '.') return took;
                if (took) QueuePreview();
                return false;
            },
            footer = () =>
            {
                UnityEditor.EditorGUILayout.HelpBox("Pit / Sunken floor: paint cells. A stroke joins the pit of its kind it touches and takes its settings (lava, depth, lights), else those of the last one of its kind in this room. Erase, or any other brush, on a pit takes that cell out of it (Floor fills it in). Each pit is kept as rectangles (DungeonFloorCut children).", UnityEditor.MessageType.None);
                foreach (var (cut, cells) in Cuts())
                {
                    if (cut == null) continue;
                    UnityEditor.EditorGUILayout.BeginHorizontal();
                    var old = GUI.color; GUI.color = Color.Lerp(CutColour(cut), Color.white, .4f);
                    string what = cut.kind == DungeonFloorCut.Kind.Sunken ? "Sunken floor" : cut.fill != null ? "Pit, " + cut.fill.name : "Pit";
                    GUILayout.Label($"{cut.name}: {what}, {cut.cells.x} x {cut.cells.y}{(cut.toWalls && cut.kind == DungeonFloorCut.Kind.Pit ? ", to walls" : "")}, {cut.depth:0.##} m deep", GUILayout.ExpandWidth(true));
                    GUI.color = old;
                    if (GUILayout.Button("Select", GUILayout.Width(60))) UnityEditor.Selection.activeGameObject = cut.gameObject;
                    if (GUILayout.Button("Remove", GUILayout.Width(60))) { RemoveCut(cut); QueuePreview(); GUIUtility.ExitGUI(); }
                    UnityEditor.EditorGUILayout.EndHorizontal();
                    if (marked)
                        foreach (var cell in cells)
                            if (shape.DoorAllowed(cell.x, cell.y)) { UnityEditor.EditorGUILayout.HelpBox($"{cut.name} covers a door cell: the generator leaves a pit out when a way in opens into it.", UnityEditor.MessageType.Warning); break; }
                }
            },
        };
    }

    Vector2Int GridCell(Vector3 world, DungeonShape shape, float c)
    {
        var lp = transform.InverseTransformPoint(world);
        return new Vector2Int(Mathf.RoundToInt(lp.x / c) + shape.center.x, Mathf.RoundToInt(lp.z / c) + shape.center.y);
    }

    // Each cut's cells on the footprint as the generator takes them: the floor cells under it, a pit marked To Walls
    // widened along the room's x on its side of the middle until the room ends (DungeonGenerator.WidenToWalls).
    List<(DungeonFloorCut cut, HashSet<Vector2Int> cells)> CutCells(DungeonShape shape, float c)
    {
        var list = new List<(DungeonFloorCut, HashSet<Vector2Int>)>();
        foreach (var cut in GetComponentsInChildren<DungeonFloorCut>(true))
        {
            var cells = new HashSet<Vector2Int>();
            foreach (var p in cut.CellCentres(c)) { var g = GridCell(p, shape, c); if (shape.IsFloor(g.x, g.y)) cells.Add(g); }
            float side = transform.InverseTransformPoint(cut.transform.position).x;
            if (cut.toWalls && cut.kind == DungeonFloorCut.Kind.Pit && Mathf.Abs(side) >= .01f)
            {
                var outward = new Vector2Int(side > 0f ? 1 : -1, 0);
                foreach (var g in new List<Vector2Int>(cells))
                    for (var n = g + outward; shape.IsFloor(n.x, n.y); n += outward) cells.Add(n);
            }
            list.Add((cut, cells));
        }
        return list;
    }

    // The cells a cut was made for (before any widening, whatever lies under them).
    HashSet<Vector2Int> AuthoredCells(DungeonFloorCut cut, DungeonShape shape, float c)
    {
        var cells = new HashSet<Vector2Int>();
        foreach (var p in cut.CellCentres(c)) cells.Add(GridCell(p, shape, c));
        return cells;
    }

    // A stroke of the Pit or Sunken brush: the painted cells join the cuts of that kind they touch (or overlap), taking
    // the first one's settings (else the last cut of that kind in the room's, else the defaults), and come out of any
    // cut of the other kind; every cut that changed is laid again as rectangles. One undo step.
    bool PaintCuts(DungeonFloorCut.Kind kind, HashSet<Vector2Int> painted, DungeonShape shape, float c)
    {
        painted.RemoveWhere(p => p.x < 0 || p.y < 0 || p.x >= width || p.y >= height);
        if (painted.Count == 0) return false;
        UnityEditor.Undo.IncrementCurrentGroup();
        int group = UnityEditor.Undo.GetCurrentGroup();
        UnityEditor.Undo.SetCurrentGroupName(kind == DungeonFloorCut.Kind.Pit ? "Paint pit" : "Paint sunken floor");
        // The painted cells become floor: a pit is a hole in the room's floor.
        UnityEditor.Undo.RecordObject(this, "Paint pit");
        foreach (var p in painted)
        {
            char ch = DungeonShapeGrid.Get(rows, p.x, p.y);
            if (ch == '.' || ch == 'O') { var chars = rows[p.y].ToCharArray(); chars[p.x] = '#'; rows[p.y] = new string(chars); }
        }
        var joined = new HashSet<Vector2Int>(painted);
        DungeonFloorCut source = null, like = null;
        var merged = new List<DungeonFloorCut>();
        foreach (var cut in GetComponentsInChildren<DungeonFloorCut>(true))
        {
            var cells = AuthoredCells(cut, shape, c);
            if (cut.kind != kind)
            {
                // Painted over with the other kind: those cells leave it.
                if (cells.Overlaps(painted)) { cells.ExceptWith(painted); Relay(cut, cells, shape, c); }
                continue;
            }
            like = cut;
            bool touches = false;
            foreach (var p in cells)
            {
                if (painted.Contains(p)) { touches = true; break; }
                foreach (var d in DungeonLayout.Neighbours) if (painted.Contains(p + d)) { touches = true; break; }
                if (touches) break;
            }
            if (!touches) continue;
            if (source == null) source = cut;
            joined.UnionWith(cells);
            merged.Add(cut);
        }
        if (source == null) source = like;
        Lay(kind, joined, source, source != null ? source.transform.parent : transform, shape, c);
        foreach (var cut in merged) RemoveCut(cut);
        UnityEditor.Undo.CollapseUndoOperations(group);
        UnityEditor.EditorUtility.SetDirty(this);
        return true;
    }

    // Erase on a cut's cell: that cell comes out of it.
    bool EraseCut(Vector2Int cell, DungeonShape shape, float c)
    {
        foreach (var cut in GetComponentsInChildren<DungeonFloorCut>(true))
        {
            var cells = AuthoredCells(cut, shape, c);
            if (!cells.Remove(cell)) continue;
            UnityEditor.Undo.IncrementCurrentGroup();
            int group = UnityEditor.Undo.GetCurrentGroup();
            UnityEditor.Undo.SetCurrentGroupName("Erase pit");
            Relay(cut, cells, shape, c);
            UnityEditor.Undo.CollapseUndoOperations(group);
            return true;
        }
        return false;
    }

    // A cut laid again over these cells with its own settings (no cells: it goes).
    void Relay(DungeonFloorCut cut, HashSet<Vector2Int> cells, DungeonShape shape, float c)
    {
        Lay(cut.kind, cells, cut, cut.transform.parent, shape, c);
        RemoveCut(cut);
    }

    // New cuts over the cells, as few rectangles as a row-first sweep gives, each a copy of settings (or the defaults).
    void Lay(DungeonFloorCut.Kind kind, HashSet<Vector2Int> cells, DungeonFloorCut settings, Transform parent, DungeonShape shape, float c)
    {
        var left = new HashSet<Vector2Int>(cells);
        var order = new List<Vector2Int>(cells);
        order.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        string baseName = kind == DungeonFloorCut.Kind.Pit ? "Pit" : "Sunken floor";
        int n = 0;
        foreach (var start in order)
        {
            if (!left.Contains(start)) continue;
            int w = 1; while (left.Contains(new Vector2Int(start.x + w, start.y))) w++;
            int h = 1;
            while (true)
            {
                bool full = true;
                for (int x = 0; x < w && full; x++) full = left.Contains(new Vector2Int(start.x + x, start.y + h));
                if (!full) break;
                h++;
            }
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) left.Remove(new Vector2Int(start.x + x, start.y + y));
            var go = new GameObject(n == 0 ? baseName : $"{baseName} {n + 1}");
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Paint pit");
            var cut = go.AddComponent<DungeonFloorCut>();
            if (settings != null) UnityEditor.EditorUtility.CopySerialized(settings, cut);
            else if (kind == DungeonFloorCut.Kind.Sunken) cut.depth = .9f;
            cut.kind = kind;
            cut.cells = new Vector2Int(w, h);
            var local = new Vector3((start.x + (w - 1) * .5f - shape.center.x) * c, 0f, (start.y + (h - 1) * .5f - shape.center.y) * c);
            go.transform.SetPositionAndRotation(transform.TransformPoint(local), transform.rotation);
            go.transform.SetParent(parent != null ? parent : transform, true);
            n++;
        }
    }

    // The cut's own object goes when it holds nothing else; else only the component.
    void RemoveCut(DungeonFloorCut cut)
    {
        var go = cut.gameObject;
        if (go != gameObject && go.transform.childCount == 0 && go.GetComponents<Component>().Length == 2) UnityEditor.Undo.DestroyObjectImmediate(go);
        else UnityEditor.Undo.DestroyObjectImmediate(cut);
    }

    // A cut child changed (its fields or its place): the preview follows.
    public void CutChanged() { if (!Application.isPlaying) QueuePreview(); }

    // ── Prefab Mode preview ───────────────────────────────────────────

    const string PreviewName = "Room preview (not saved)";
    // Same numbers as DungeonGenerator: walls sit just outside the cell edge, torches just inside.
    const float WallThickness = DungeonGenerator.WallThickness;

    void OnEnable() { if (!Application.isPlaying) QueuePreview(); }
    // Moving, turning or retyping a socket or a pit rebuilds the preview.
    void Update()
    {
        if (Application.isPlaying || !InPrefabMode) return;
        bool dirty = false;
        foreach (var socket in GetComponentsInChildren<DungeonSocket>(true))
        {
            if (!socket.transform.hasChanged) continue;
            socket.transform.hasChanged = false;
            dirty = true;
        }
        // A pit added or taken away (an undo too) changes their count.
        var cuts = GetComponentsInChildren<DungeonFloorCut>(true);
        if (cuts.Length != lastCutCount) { lastCutCount = cuts.Length; dirty = true; }
        foreach (var cut in cuts)
        {
            if (!cut.transform.hasChanged) continue;
            cut.transform.hasChanged = false;
            dirty = true;
        }
        if (dirty) RebuildPreview();
    }
    [System.NonSerialized] int lastCutCount = -1;
    void OnDisable() { if (!Application.isPlaying) ClearPreview(); }
    void OnValidate()
    {
        if (paintFootprint && (rows == null || rows.Length != height)) rows = DungeonShapeGrid.Resized(rows, width, height);
        if (!Application.isPlaying) QueuePreview();
    }

    void QueuePreview()
    {
        UnityEditor.EditorApplication.delayCall -= RebuildPreview;
        UnityEditor.EditorApplication.delayCall += RebuildPreview;
    }

    bool InPrefabMode => this != null && UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null;

    void ClearPreview()
    {
        if (this == null) return;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name != PreviewName) continue;
            foreach (var mf in child.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.name == PreviewMeshName) DestroyImmediate(mf.sharedMesh);
            DestroyImmediate(child.gameObject);
        }
    }

    [Button("Refresh preview"), PropertyOrder(100)]
    void RebuildPreview()
    {
        if (this == null || Application.isPlaying) return;
        ClearPreview();
        if (!InPrefabMode || !HasFootprint) return;
        // No Preview Level set: Dungeon1, else the first dungeon level, for this preview only (written into the room, the
        // prefab was saved by Prefab Mode's auto save just from being opened).
        var lvl = previewLevel;
        if (lvl == null)
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:TableLevelData"))
            {
                var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (level == null || !level.IsDungeon) continue;
                if (lvl == null || level.name == "Dungeon1") lvl = level;
                if (level.name == "Dungeon1") break;
            }
        if (lvl == null) return;

        var shape = Footprint();
        float c = lvl.cellSize, tile = lvl.architectureTileSize, roomHeight = tile * Mathf.Max(1, heightTiles);
        var root = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);
        UnityEditor.SceneVisibilityManager.instance.DisablePicking(root, true);

        // A room with no style of its own takes the floor's: the preview shows the first one the level's first stage deals.
        var look = style != null ? style : PreviewStyle(lvl);
        var floorMat = DungeonRoomStyle.Resolve(look != null ? look.floor : null, lvl.floorMaterial);
        Material WallMat(float bottom)
        {
            var fallback = bottom < tile - .001f ? lvl.wallMaterial : DungeonRoomStyle.Resolve(lvl.upperWallMaterial, lvl.wallMaterial);
            return look != null ? look.Wall(bottom, tile, fallback) : fallback;
        }
        Vector3 Local(Vector2Int p) => new Vector3((p.x - shape.center.x) * c, 0, (p.y - shape.center.y) * c);
        bool Floor(Vector2Int p) => shape.IsFloor(p.x, p.y);
        // A door cell opens onto the corridor outside it.
        bool Open(Vector2Int cell, Vector2Int d) => Floor(cell + d) || (shape.DoorAllowed(cell.x, cell.y) && shape[cell.x + d.x, cell.y + d.y] == DungeonShape.Empty);
        var dirs = DungeonLayout.Neighbours;
        var cuts = CutCells(shape, c);
        var cutAll = new HashSet<Vector2Int>();
        foreach (var (_, cells) in cuts) cutAll.UnionWith(cells);

        for (int x = 0; x < shape.width; x++) for (int y = 0; y < shape.height; y++)
        {
            var cell = new Vector2Int(x, y);
            if (!Floor(cell)) continue;
            var p = Local(cell);
            if (!cutAll.Contains(cell)) Box(root.transform, p + Vector3.down * .12f, new Vector3(c, .24f, c), floorMat);
            foreach (var d in dirs)
            {
                if (Open(cell, d)) continue;
                var outward = new Vector3(d.x, 0, d.y);
                var size = d.x != 0 ? new Vector3(WallThickness, tile, c + WallThickness * 2) : new Vector3(c + WallThickness * 2, tile, WallThickness);
                for (float b = 0; b < roomHeight - .01f; b += tile)
                    Box(root.transform, p + outward * (c * .5f + WallThickness * .5f) + Vector3.up * (b + tile * .5f), size, WallMat(b));
            }
        }

        // Pits and sunken floors as DungeonGenerator.BuildCuts makes them: no floor tile, sides of the room's lower wall
        // stone down to the bottom, the room's floor at the bottom, a pit's fill (lava) or the level's pit water over it.
        var waterBiome = PitWaterBiome(lvl);
        foreach (var (cut, cells) in cuts)
        {
            if (cells.Count == 0) continue;
            var parent = new GameObject(cut.kind == DungeonFloorCut.Kind.Pit ? "Pit" : "Sunken floor").transform;
            parent.SetParent(root.transform, false);
            float depth = Mathf.Max(.2f, cut.depth);
            foreach (var cell in cells)
            {
                var centre = Local(cell);
                foreach (var d in dirs)
                {
                    if (cutAll.Contains(cell + d)) continue;
                    var outward = new Vector3(d.x, 0f, d.y);
                    var along = new Vector2Int(d.y, d.x);
                    float before = d.y != 0 && !cutAll.Contains(cell - along) ? WallThickness : 0f;
                    float after = d.y != 0 && !cutAll.Contains(cell + along) ? WallThickness : 0f;
                    float top = Floor(cell + d) || before > 0f && (Floor(cell - along) || Floor(cell + d - along)) || after > 0f && (Floor(cell + along) || Floor(cell + d + along)) ? .011f : .001f;
                    float length = c + before + after, shift = (after - before) * .5f;
                    var size = d.x != 0 ? new Vector3(WallThickness, depth - top + .001f, length) : new Vector3(length, depth - top + .001f, WallThickness);
                    var middle = centre + outward * (c * .5f + WallThickness * .5f - .02f) + new Vector3(along.x, 0f, along.y) * shift
                        + Vector3.down * (top + (depth - top + .001f) * .5f);
                    Box(parent, middle, size, WallMat(0f));
                }
                Box(parent, centre + Vector3.down * (depth + .12f), new Vector3(c, .24f, c), floorMat);
            }
            if (cut.kind != DungeonFloorCut.Kind.Pit) continue;
            var sheet = cut.fill != null ? cut.fill : waterBiome != null ? waterBiome.pitWater : null;
            if (sheet == null) continue;
            float below = cut.fill != null ? cut.fillBelow : waterBiome.pitWaterBelow;
            PreviewSheet(parent, cells, Local, c, -Mathf.Min(below, depth - .05f), sheet, cut.fill != null ? cut.fillRepeat : 0f);
            if (cut.fill == null || cut.fillLightIntensity <= 0f) continue;
            // Its lights, along its longer side as the generator spreads them.
            Vector3 min = new(float.MaxValue, 0f, float.MaxValue), max = new(float.MinValue, 0f, float.MinValue);
            foreach (var cell in cells) { var p = Local(cell); min = Vector3.Min(min, p - new Vector3(c, 0, c) * .5f); max = Vector3.Max(max, p + new Vector3(c, 0, c) * .5f); }
            var way = max.x - min.x >= max.z - min.z ? Vector3.right : Vector3.forward;
            float span = Vector3.Dot(max - min, way);
            int count = Mathf.Max(1, Mathf.RoundToInt(span / c / cut.fillLightEvery));
            for (int i = 0; i < count; i++)
            {
                var lightGo = new GameObject("Pit fill light");
                lightGo.transform.SetParent(parent, false);
                lightGo.transform.localPosition = (min + max) * .5f + Vector3.up * .8f + way * ((i + .5f) / count - .5f) * span;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point; light.color = cut.fillLightColour; light.intensity = cut.fillLightIntensity; light.range = cut.fillLightRange;
                light.shadows = LightShadows.None;
            }
        }

        // Wall Light sockets, snapped to the wall they face as the generator does.
        foreach (var socket in GetComponentsInChildren<DungeonSocket>(true))
        {
            if (socket.type != DungeonSocketType.WallLight) continue;
            var entry = socket.overridePrefab != null ? null : DungeonWallLight.First(lvl.lightingPrefabs);
            var prefab = entry != null ? entry.prefab : socket.overridePrefab;
            if (prefab == null) continue;
            Color? tint = entry?.Tint;
            if (overrideLightColor) tint = lightColor;
            if (socket.overrideLightColor) tint = socket.lightColor;
            var lp = transform.InverseTransformPoint(socket.transform.position);
            var cell = new Vector2Int(Mathf.RoundToInt(lp.x / c) + shape.center.x, Mathf.RoundToInt(lp.z / c) + shape.center.y);
            if (!Floor(cell)) continue;
            foreach (var d in DungeonGenerator.SocketWallOrder(transform.InverseTransformDirection(socket.transform.forward)))
            {
                if (Open(cell, d)) continue;
                var torch = (GameObject)Instantiate(prefab, root.transform);
                torch.transform.localPosition = Local(cell) + DungeonGenerator.WallLightOffset(d, c);
                torch.transform.localRotation = Quaternion.LookRotation(-new Vector3(d.x, 0, d.y));
                DungeonWallLight.Apply(torch, tint);
                break;
            }
        }

        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
    }

    static DungeonRoomStyle PreviewStyle(TableLevelData lvl)
    {
        var biomes = new List<DungeonBiome>();
        if (lvl.stages != null) foreach (var s in lvl.stages) if (s?.biome != null) biomes.Add(s.biome);
        if (lvl.baseBiome != null) biomes.Add(lvl.baseBiome);
        foreach (var b in biomes)
            if (b.roomStyles != null) foreach (var choice in b.roomStyles) if (choice?.style != null && choice.weight > 0f) return choice.style;
        return null;
    }

    // The pit water a stage of the preview level stands in its pits (the generator takes the floor's stage biome's).
    static DungeonBiome PitWaterBiome(TableLevelData lvl)
    {
        if (lvl.stages != null) foreach (var s in lvl.stages) if (s?.biome != null && s.biome.pitWater != null) return s.biome;
        return null;
    }

    // One sheet over the cells at height y (room space): UVs in metres over repeat so cells meet (a pit's fill), or 0-1
    // per cell when repeat is 0 (pit water, a quad per cell in the generator). Its mesh is freed with the preview.
    static void PreviewSheet(Transform parent, HashSet<Vector2Int> cells, System.Func<Vector2Int, Vector3> local, float c, float y, Material material, float repeat)
    {
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        var go = new GameObject(PreviewMeshName);
        go.transform.SetParent(parent, false);
        foreach (var cell in cells)
        {
            var centre = local(cell); int start = verts.Count;
            foreach (var (dx, dz) in new[] { (-1f, -1f), (-1f, 1f), (1f, 1f), (1f, -1f) })
            {
                var p = new Vector3(centre.x + dx * c * .5f, y, centre.z + dz * c * .5f);
                verts.Add(p);
                var world = go.transform.TransformPoint(p);
                uvs.Add(repeat > 0f ? new Vector2(world.x, world.z) / repeat : new Vector2(dx > 0f ? 1f : 0f, dz > 0f ? 1f : 0f));
            }
            tris.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        var mesh = new Mesh { name = PreviewMeshName, hideFlags = HideFlags.DontSave };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
    const string PreviewMeshName = "Pit fill (preview)";

    static void Box(Transform parent, Vector3 localPos, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
    }
#endif
}

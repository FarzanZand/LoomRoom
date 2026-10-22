using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A whole small map in one prefab: several rooms with their passages and doors, for a quick encounter that is more than
// one room (an arena with its landing, a short gauntlet, a boss and its antechamber).
//
// Paint the map here (rooms 0, 1, 2 ..., passages, doors), press Sync rooms, and build each room's interior under its
// Room child (a DungeonMapRoom with a DungeonRoomTemplate: style, height, light, events, enter lines). In Prefab Mode the
// map shows its floors and walls, so props can be placed against the real walls. A level uses it with Layout Style
// Authored and this prefab as its Map Template: the generator builds the map as painted and each room from its child,
// exactly like a hand-built room (sockets, kit pieces, enemies, chests).
[ExecuteAlways]
public class DungeonMapTemplate : MonoBehaviour
{
    public const char Rock = '#', Passage = '.', Door = '+';
    public const int MinGrid = 14;   // the generator's smallest grid; the map is padded with rock up to it

    [HideInInspector] public int width = 16, depth = 16;
    // rows[0] is the north (top) row, as in a level's text floor plan.
    [HideInInspector] public string[] rows;

    [Title("Preview")]
    [Tooltip("Level whose materials and cell size the Prefab Mode preview uses (and the one Play it loads this map into).")]
    public TableLevelData previewLevel;
    [Min(.5f), Tooltip("Grid spacing when there is no preview level. Match the level's Cell Size.")]
    public float previewCellSize = 2f;
    public float CellSize => previewLevel != null ? previewLevel.cellSize : previewCellSize;

    public static int MapRoom(char c) => DungeonLayout.MapRoom(c);
    public static char RoomMark(int i) => i < 10 ? (char)('0' + i) : (char)('A' + i - 10);
    public char Get(int x, int y) => rows != null && y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length ? rows[y][x] : Rock;

    // The floor plan as a level reads it (north at the top), padded with rock to the generator's smallest grid.
    public string MapText()
    {
        int w = Mathf.Max(width, MinGrid), d = Mathf.Max(depth, MinGrid);
        var lines = new string[d];
        for (int y = 0; y < d; y++)
        {
            var chars = new char[w];
            for (int x = 0; x < w; x++) chars[x] = x < width && y < depth ? Get(x, y) : Rock;
            lines[y] = new string(chars);
        }
        return string.Join("\n", lines);
    }
    public Vector2Int GridSize => new(Mathf.Max(width, MinGrid), Mathf.Max(depth, MinGrid));

    // The room marks painted, in order (0, 1, 2 ...).
    public List<int> PaintedRooms()
    {
        var found = new SortedSet<int>();
        for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++) { int r = MapRoom(Get(x, y)); if (r >= 0) found.Add(r); }
        return new List<int>(found);
    }

    public DungeonMapRoom RoomChild(int index)
    {
        char mark = RoomMark(index);
        foreach (Transform child in transform)
            if (child.TryGetComponent<DungeonMapRoom>(out var r) && r.Mark == mark) return r;
        return null;
    }

    [System.NonSerialized] TableLevelData.AuthoredRoom[] roomsCache;
    // The rooms as a level's authored rooms: each room's child is its template.
    public TableLevelData.AuthoredRoom[] Rooms()
    {
        if (Application.isPlaying && roomsCache != null) return roomsCache;
        var painted = PaintedRooms();
        int count = painted.Count > 0 ? painted[painted.Count - 1] + 1 : 0;
        var list = new TableLevelData.AuthoredRoom[count];
        for (int i = 0; i < count; i++)
        {
            var child = RoomChild(i);
            list[i] = new TableLevelData.AuthoredRoom
            {
                label = child != null ? child.name : $"Room {RoomMark(i)}",
                role = child != null ? child.role : i == 0 ? DungeonGenerator.RoomRole.Entrance : i == count - 1 ? DungeonGenerator.RoomRole.Exit : DungeonGenerator.RoomRole.Combat,
                template = child != null && !child.generated && child.GetComponent<DungeonRoomTemplate>() != null ? child.gameObject : null,
                raised = child != null ? child.raised : 0,
                merchant = child != null && child.merchant,
            };
        }
        return roomsCache = list;
    }

    // A room's cells and its centre cell, on the map's own grid (x east, y north), the way the generator picks it.
    public List<Vector2Int> Cells(int index)
    {
        var cells = new List<Vector2Int>();
        char mark = RoomMark(index);
        for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++) if (Get(x, y) == mark) cells.Add(new Vector2Int(x, depth - 1 - y));
        return cells;
    }
    // Where a grid cell sits under the map's pivot (the padded grid is centred on it, as the generator centres a level).
    public Vector3 Local(Vector2Int p)
    {
        var size = GridSize; float c = CellSize;
        // Padding goes to the east and south: shift so the painted cells keep their place in the padded grid.
        int py = p.y + (size.y - depth);
        return new Vector3((p.x - size.x * .5f) * c, 0f, (py - size.y * .5f) * c);
    }

    void OnValidate()
    {
        width = Mathf.Clamp(width, 2, 48); depth = Mathf.Clamp(depth, 2, 48);
        if (rows == null || rows.Length != depth || (rows.Length > 0 && rows[0].Length != width)) rows = Resized(rows, width, depth);
        roomsCache = null;
#if UNITY_EDITOR
        if (!Application.isPlaying) QueuePreview();
#endif
    }

    static string[] Resized(string[] old, int w, int d)
    {
        var next = new string[d];
        for (int y = 0; y < d; y++)
        {
            var chars = new char[w];
            for (int x = 0; x < w; x++) chars[x] = old != null && y < old.Length && x < old[y].Length ? old[y][x] : Rock;
            next[y] = new string(chars);
        }
        return next;
    }

    // Problems a designer should fix, in plain words (shown in the inspector; the generator throws on the first two).
    public List<string> Problems()
    {
        var problems = new List<string>();
        var painted = PaintedRooms();
        if (painted.Count < 2) problems.Add("Paint at least two rooms (0 is where the player starts, the highest is the exit).");
        for (int i = 0; i < painted.Count; i++) if (painted[i] != i) { problems.Add($"Rooms must be numbered 0, 1, 2 ... without gaps: room {RoomMark(i)} is missing."); break; }
        for (int i = 0; i < painted.Count; i++) if (RoomChild(painted[i]) == null) { problems.Add($"Room {RoomMark(painted[i])} has no Room child yet: press Sync rooms."); break; }
        foreach (Transform child in transform)
        {
            if (child.name == PreviewName) continue;
            if (!child.TryGetComponent<DungeonMapRoom>(out var r)) problems.Add($"{child.name} is not inside a room: put it under a Room child or it won't be built.");
            else if (MapRoom(r.Mark) < 0 || !painted.Contains(MapRoom(r.Mark))) problems.Add($"{child.name} is for room {r.room}, which isn't painted on the map.");
        }
        return problems;
    }

#if UNITY_EDITOR
    // ── Painter ───────────────────────────────────────────────────────

    static char brush = '0';
    static Color ColorOf(char c)
    {
        if (c == Rock) return new Color(.08f, .08f, .1f);
        if (c == Passage) return new Color(.55f, .52f, .45f);
        if (c == Door) return new Color(.95f, .75f, .3f);
        int r = MapRoom(c);
        return r < 0 ? Color.magenta : Color.HSVToRGB(r * .13f % 1f, .45f, .75f);
    }

    [OnInspectorGUI, PropertyOrder(-1)]
    void Painter()
    {
        var problems = Problems();
        foreach (var p in problems) UnityEditor.EditorGUILayout.HelpBox(p, UnityEditor.MessageType.Warning);

        UnityEditor.EditorGUILayout.BeginHorizontal();
        int w = Mathf.Clamp(UnityEditor.EditorGUILayout.IntField("Width", width), 2, 48);
        int d = Mathf.Clamp(UnityEditor.EditorGUILayout.IntField("Depth", depth), 2, 48);
        UnityEditor.EditorGUILayout.EndHorizontal();
        if (w != width || d != depth) { UnityEditor.Undo.RecordObject(this, "Resize map"); rows = Resized(rows, w, d); width = w; depth = d; Changed(); }
        if (rows == null || rows.Length != depth) rows = Resized(rows, width, depth);

        // Brushes: rock, passage, door, every painted room and the next free one.
        var painted = PaintedRooms();
        int next = painted.Count > 0 ? painted[painted.Count - 1] + 1 : 0;
        UnityEditor.EditorGUILayout.BeginHorizontal();
        Brush(Rock, "Rock"); Brush(Passage, "Passage"); Brush(Door, "Door");
        UnityEditor.EditorGUILayout.EndHorizontal();
        UnityEditor.EditorGUILayout.BeginHorizontal();
        foreach (var r in painted) Brush(RoomMark(r), $"Room {RoomMark(r)}");
        if (next < 36) Brush(RoomMark(next), $"New room {RoomMark(next)}");
        UnityEditor.EditorGUILayout.EndHorizontal();

        // North is up.
        float cell = Mathf.Clamp((UnityEditor.EditorGUIUtility.currentViewWidth - 40) / Mathf.Max(width, depth), 8, 26);
        var area = GUILayoutUtility.GetRect(width * cell, depth * cell, GUILayout.ExpandWidth(false));
        var e = Event.current;
        var label = new GUIStyle(UnityEditor.EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.black } };
        for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++)
        {
            var r = new Rect(area.x + x * cell, area.y + y * cell, cell - 1, cell - 1);
            char c = Get(x, y);
            UnityEditor.EditorGUI.DrawRect(r, ColorOf(c));
            if (MapRoom(c) >= 0 && cell >= 12) GUI.Label(r, c.ToString(), label);
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && r.Contains(e.mousePosition) && c != brush)
            {
                UnityEditor.Undo.RecordObject(this, "Paint map");
                var chars = rows[y].ToCharArray(); chars[x] = brush; rows[y] = new string(chars);
                Changed(); e.Use();
            }
        }
        UnityEditor.EditorGUIUtility.AddCursorRect(area, UnityEditor.MouseCursor.ArrowPlus);
        UnityEditor.EditorGUILayout.HelpBox("Rooms 0, 1, 2 ...: 0 is where the player starts, the highest is the exit. Passages join rooms; a Door is a passage cell with a door, next to a room. Then press Sync rooms and build each room under its Room child.", UnityEditor.MessageType.None);
    }

    void Brush(char c, string text)
    {
        var old = GUI.backgroundColor;
        GUI.backgroundColor = brush == c ? ColorOf(c) * 1.6f : ColorOf(c);
        if (GUILayout.Toggle(brush == c, text, "Button")) brush = c;
        GUI.backgroundColor = old;
    }

    void Changed() { roomsCache = null; UnityEditor.EditorUtility.SetDirty(this); QueuePreview(); }

    // ── Rooms ─────────────────────────────────────────────────────────

    // Every painted room gets a Room child (a DungeonMapRoom and a DungeonRoomTemplate) on its centre cell. A child that
    // moves takes its interior with it to the new centre: what is under it keeps its place in the world.
    [Button("Sync rooms", ButtonSizes.Medium), PropertyOrder(-2), PropertyTooltip("Adds a Room child for every painted room and puts each on its room's centre cell, keeping what is under it where it is.")]
    public void SyncRooms()
    {
        var painted = PaintedRooms();
        int last = painted.Count > 0 ? painted[painted.Count - 1] : 0;
        foreach (var i in painted)
        {
            var room = RoomChild(i);
            if (room == null)
            {
                var go = new GameObject($"Room {RoomMark(i)}");
                UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Add room");
                go.transform.SetParent(transform, false);
                room = go.AddComponent<DungeonMapRoom>();
                room.room = RoomMark(i).ToString();
                room.role = i == 0 ? DungeonGenerator.RoomRole.Entrance : i == last ? DungeonGenerator.RoomRole.Exit : DungeonGenerator.RoomRole.Combat;
                var template = go.AddComponent<DungeonRoomTemplate>();
                template.previewLevel = previewLevel;
            }
            if (room.GetComponent<DungeonRoomTemplate>() == null) UnityEditor.Undo.AddComponent<DungeonRoomTemplate>(room.gameObject);
            var cells = Cells(i);
            if (cells.Count == 0) continue;
            var target = Local(DungeonLayout.RoomCentre(cells));
            var t = room.transform;
            if ((t.localPosition - target).sqrMagnitude < 1e-6f && t.localRotation == Quaternion.identity) continue;
            var kids = new List<(Transform, Vector3, Quaternion)>();
            foreach (Transform k in t) kids.Add((k, k.position, k.rotation));
            UnityEditor.Undo.RecordObject(t, "Move room");
            foreach (var k in kids) UnityEditor.Undo.RecordObject(k.Item1, "Move room");
            t.localPosition = target; t.localRotation = Quaternion.identity;
            foreach (var k in kids) k.Item1.SetPositionAndRotation(k.Item2, k.Item3);
        }
        Changed();
    }

    // In Play mode: a run in this map at once, on a copy of the preview level (nothing saved).
    [Button("Play it"), PropertyOrder(-2), EnableIf("@UnityEngine.Application.isPlaying"), PropertyTooltip("In Play mode: starts a run in this map, on a copy of the Preview Level (its biome, look and rules).")]
    void PlayIt()
    {
        var source = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(this) ?? this;
        if (previewLevel == null || TableManager.Loader == null) { Debug.LogWarning("Play it needs Play mode in the Room scene and a Preview Level.", this); return; }
        var copy = Instantiate(previewLevel);
        copy.name = previewLevel.name;
        copy.layoutMode = DungeonLayoutMode.Authored;
        copy.mapTemplate = source;
        copy.SyncMapSize();
        TableManager.Loader.Load(copy);
    }

    // ── Prefab Mode preview: floors and walls of every room and passage ──

    const string PreviewName = "Map preview (not saved)";
    const float WallThickness = .3f;
    bool InPrefabMode => this != null && UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null;

    void OnEnable() { if (!Application.isPlaying) QueuePreview(); }
    // Opened in Prefab Mode: the stage may not know the prefab yet when OnEnable queues the preview, so it is checked
    // once more on the first editor update.
    [System.NonSerialized] bool previewChecked;
    void Update()
    {
        if (Application.isPlaying || previewChecked || !InPrefabMode) return;
        previewChecked = true;
        if (transform.Find(PreviewName) == null) RebuildPreview();
    }
    void OnDisable() { if (!Application.isPlaying) ClearPreview(); }
    void QueuePreview()
    {
        UnityEditor.EditorApplication.delayCall -= RebuildPreview;
        UnityEditor.EditorApplication.delayCall += RebuildPreview;
    }
    void ClearPreview()
    {
        if (this == null) return;
        for (int i = transform.childCount - 1; i >= 0; i--)
            if (transform.GetChild(i).name == PreviewName) DestroyImmediate(transform.GetChild(i).gameObject);
    }

    [Button("Refresh preview"), PropertyOrder(100)]
    void RebuildPreview()
    {
        if (this == null || Application.isPlaying) return;
        ClearPreview();
        if (!InPrefabMode) return;
        if (previewLevel == null)
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:TableLevelData"))
            {
                var level = UnityEditor.AssetDatabase.LoadAssetAtPath<TableLevelData>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                if (level != null && level.IsDungeon) { previewLevel = level; UnityEditor.EditorUtility.SetDirty(this); break; }
            }
        if (previewLevel == null) return;
        var lvl = previewLevel;
        float c = lvl.cellSize, tile = lvl.architectureTileSize;
        var root = new GameObject(PreviewName) { hideFlags = HideFlags.DontSave };
        root.transform.SetParent(transform, false);
        UnityEditor.SceneVisibilityManager.instance.DisablePicking(root, true);

        bool Floor(int x, int y) { char ch = Get(x, depth - 1 - y); return ch == Passage || ch == Door || MapRoom(ch) >= 0; }
        DungeonRoomTemplate TemplateAt(int x, int y)
        {
            int r = MapRoom(Get(x, depth - 1 - y));
            var child = r >= 0 ? RoomChild(r) : null;
            return child != null ? child.GetComponent<DungeonRoomTemplate>() : null;
        }
        var dirs = DungeonLayout.Neighbours;
        for (int x = 0; x < width; x++) for (int y = 0; y < depth; y++)
        {
            if (!Floor(x, y)) continue;
            var t = TemplateAt(x, y);
            var style = t != null ? t.style : null;
            int tiles = t != null ? Mathf.Max(1, t.heightTiles) : 1;
            var p = Local(new Vector2Int(x, y));
            Box(root.transform, p + Vector3.down * .12f, new Vector3(c, .24f, c), DungeonRoomStyle.Resolve(style != null ? style.floor : null, lvl.floorMaterial));
            foreach (var d in dirs)
            {
                if (Floor(x + d.x, y + d.y)) continue;
                var outward = new Vector3(d.x, 0, d.y);
                var size = d.x != 0 ? new Vector3(WallThickness, tile, c + WallThickness * 2) : new Vector3(c + WallThickness * 2, tile, WallThickness);
                for (float b = 0; b < tile * tiles - .01f; b += tile)
                {
                    var fallback = b < tile - .001f ? lvl.wallMaterial : DungeonRoomStyle.Resolve(lvl.upperWallMaterial, lvl.wallMaterial);
                    Box(root.transform, p + outward * (c * .5f + WallThickness * .5f) + Vector3.up * (b + tile * .5f), size, style != null ? style.Wall(b, tile, fallback) : fallback);
                }
            }
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
    }

    static void Box(Transform parent, Vector3 localPos, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
    }

    void OnDrawGizmos()
    {
        // The rooms' marks over their centres, so a room is easy to find in the Scene view.
        for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++)
        {
            char ch = Get(x, y);
            if (MapRoom(ch) < 0) continue;
            Gizmos.color = ColorOf(ch) * new Color(1, 1, 1, .35f);
            Gizmos.DrawCube(transform.TransformPoint(Local(new Vector2Int(x, depth - 1 - y)) + Vector3.up * .03f), new Vector3(CellSize * .94f, .02f, CellSize * .94f));
        }
    }
#endif
}

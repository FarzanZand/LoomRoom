using Sirenix.OdinInspector;
using UnityEngine;

// A hand-painted room footprint. Reference it from a level, theme or room profile shape list
// (kind Authored), or as a Room Template's footprint so the template brings its own outline.
//
// Cells: '.' outside the room, '#' floor, 'O' solid (pillar, inner wall), '+' floor where a
// corridor may attach, '*' the centre (floor; lights, stairs, enemies and the template pivot).
// With no '+' cells, corridors may attach anywhere along the outline.
[CreateAssetMenu(menuName = "Table/Room Shape", fileName = "Room Shape")]
public class DungeonRoomShape : ScriptableObject
{
    public const int MaxSize = 24;
    [Tooltip("The generator may turn the room in quarter turns. Room templates turn with it.")]
    public bool allowRotation = true;
    [Tooltip("The generator may mirror the room. Ignored when the shape is a template's footprint.")]
    public bool allowMirror = true;
    [HideInInspector] public int width = 8, height = 8;
    // rows[0] is the south (bottom) row. Kept as text so shapes diff and paste cleanly.
    [HideInInspector] public string[] rows;

    public char Get(int x, int y) => rows != null && y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length ? rows[y][x] : '.';

    public DungeonShape ToShape()
    {
        var s = new DungeonShape(width, height) { kind = DungeonShapeKind.Authored, name = name };
        bool centred = false;
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            char c = Get(x, y);
            s[x, y] = c == 'O' ? DungeonShape.Solid : c == '#' || c == '+' || c == '*' ? DungeonShape.Floor : DungeonShape.Empty;
            if (c == '+') s.MarkDoor(x, y);
            if (c == '*') { s.center = new Vector2Int(x, y); centred = true; }
        }
        if (!centred) s.AutoCenter();
        return s;
    }

    public DungeonShape ToShape(System.Random random, bool mirrorAllowed) =>
        ToShape().Oriented(random, allowRotation, allowMirror && mirrorAllowed);

    void OnValidate() { Resize(width, height); }

    void Resize(int w, int h)
    {
        w = Mathf.Clamp(w, 1, MaxSize); h = Mathf.Clamp(h, 1, MaxSize);
        var next = new string[h];
        for (int y = 0; y < h; y++)
        {
            var chars = new char[w];
            for (int x = 0; x < w; x++) chars[x] = rows == null ? '#' : Get(x, y);
            next[y] = new string(chars);
        }
        width = w; height = h; rows = next;
    }

#if UNITY_EDITOR
    static char brush = '#';
    static readonly (char c, string label, Color color)[] Brushes =
    {
        ('#', "Floor", new Color(.55f, .6f, .5f)),
        ('O', "Pillar", new Color(.22f, .2f, .18f)),
        ('+', "Door", new Color(.95f, .75f, .3f)),
        ('*', "Centre", new Color(.9f, .35f, .3f)),
        ('.', "Erase", new Color(.08f, .08f, .1f)),
    };
    static Color ColorOf(char c) { foreach (var b in Brushes) if (b.c == c) return b.color; return Color.magenta; }

    [OnInspectorGUI, PropertyOrder(10)]
    void Painter()
    {
        UnityEditor.EditorGUILayout.Space();
        UnityEditor.EditorGUILayout.BeginHorizontal();
        int w = UnityEditor.EditorGUILayout.IntField("Width", width);
        int h = UnityEditor.EditorGUILayout.IntField("Height", height);
        UnityEditor.EditorGUILayout.EndHorizontal();
        if (w != width || h != height) Edit("Resize shape", () => Resize(w, h));

        UnityEditor.EditorGUILayout.BeginHorizontal();
        foreach (var b in Brushes)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = brush == b.c ? b.color * 1.6f : b.color;
            if (GUILayout.Toggle(brush == b.c, b.label, "Button")) brush = b.c;
            GUI.backgroundColor = old;
        }
        UnityEditor.EditorGUILayout.EndHorizontal();

        // North is up, as on the map and in the Scene view from above.
        float cell = Mathf.Clamp((UnityEditor.EditorGUIUtility.currentViewWidth - 40) / Mathf.Max(width, height), 8, 26);
        var area = GUILayoutUtility.GetRect(width * cell, height * cell, GUILayout.ExpandWidth(false));
        var e = Event.current;
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            var r = new Rect(area.x + x * cell, area.y + (height - 1 - y) * cell, cell - 1, cell - 1);
            UnityEditor.EditorGUI.DrawRect(r, ColorOf(Get(x, y)));
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && r.Contains(e.mousePosition) && Get(x, y) != brush)
            {
                int px = x, py = y;
                Edit("Paint shape", () => Set(px, py, brush));
                e.Use();
            }
        }
        UnityEditor.EditorGUIUtility.AddCursorRect(area, UnityEditor.MouseCursor.ArrowPlus);

        UnityEditor.EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fill floor")) Edit("Fill shape", () => FillAll('#'));
        if (GUILayout.Button("Clear")) Edit("Clear shape", () => FillAll('.'));
        if (GUILayout.Button("Outline pillars")) Edit("Pillars", Colonnade);
        UnityEditor.EditorGUILayout.EndHorizontal();

        var shape = ToShape();
        if (shape.FloorCount == 0) UnityEditor.EditorGUILayout.HelpBox("Paint at least one floor cell.", UnityEditor.MessageType.Error);
        else if (!shape.IsConnected()) UnityEditor.EditorGUILayout.HelpBox("Floor cells are split into separate pockets. Every floor cell must be reachable.", UnityEditor.MessageType.Warning);
        else UnityEditor.EditorGUILayout.HelpBox($"{shape.FloorCount} floor cells. Centre at {shape.center}.", UnityEditor.MessageType.None);
    }

    void Set(int x, int y, char c)
    {
        // Only one centre.
        if (c == '*') for (int j = 0; j < height; j++) rows[j] = rows[j].Replace('*', '#');
        var chars = rows[y].ToCharArray(); chars[x] = c; rows[y] = new string(chars);
    }
    void FillAll(char c) { for (int y = 0; y < height; y++) rows[y] = new string(c, width); }
    void Colonnade()
    {
        // Pillars every other cell, one aisle in from the outline.
        for (int x = 1; x < width - 1; x++) for (int y = 1; y < height - 1; y++)
            if (x % 2 == 0 && y % 2 == 0 && Get(x, y) == '#') Set(x, y, 'O');
    }
    void Edit(string label, System.Action change)
    {
        UnityEditor.Undo.RecordObject(this, label);
        change();
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
}

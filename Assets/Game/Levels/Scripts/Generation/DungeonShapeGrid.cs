using UnityEngine;

// A painted room footprint stored as text rows, shared by DungeonRoomShape assets and room
// templates that paint their own outline. rows[0] is the south (bottom) row.
// Cells: '.' outside the room, '#' floor, 'O' solid (pillar, inner wall), '+' floor where a
// corridor may attach, '*' the centre (floor; lights, stairs, enemies and the template pivot).
// With no '+' cells, corridors may attach anywhere along the outline.
public static class DungeonShapeGrid
{
    public const int MaxSize = 24;

    public static char Get(string[] rows, int x, int y) =>
        rows != null && y >= 0 && y < rows.Length && x >= 0 && x < rows[y].Length ? rows[y][x] : '.';

    public static DungeonShape ToShape(int width, int height, string[] rows, string name)
    {
        var s = new DungeonShape(width, height) { kind = DungeonShapeKind.Authored, name = name };
        bool centred = false;
        for (int x = 0; x < width; x++) for (int y = 0; y < height; y++)
        {
            char c = Get(rows, x, y);
            s[x, y] = c == 'O' ? DungeonShape.Solid : c == '#' || c == '+' || c == '*' ? DungeonShape.Floor : DungeonShape.Empty;
            if (c == '+') s.MarkDoor(x, y);
            if (c == '*') { s.center = new Vector2Int(x, y); centred = true; }
        }
        if (!centred) s.AutoCenter();
        return s;
    }

    public static string[] Resized(string[] rows, int w, int h)
    {
        var next = new string[h];
        for (int y = 0; y < h; y++)
        {
            var chars = new char[w];
            for (int x = 0; x < w; x++) chars[x] = rows == null ? '#' : Get(rows, x, y);
            next[y] = new string(chars);
        }
        return next;
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

    // Inspector painter. Every change goes through Undo on the owner and returns true.
    public static bool Painter(Object owner, ref int width, ref int height, ref string[] rows)
    {
        bool changed = false;
        if (rows == null || rows.Length != height) { rows = Resized(rows, width, height); }
        UnityEditor.EditorGUILayout.BeginHorizontal();
        int w = Mathf.Clamp(UnityEditor.EditorGUILayout.IntField("Width", width), 1, MaxSize);
        int h = Mathf.Clamp(UnityEditor.EditorGUILayout.IntField("Height", height), 1, MaxSize);
        UnityEditor.EditorGUILayout.EndHorizontal();
        if (w != width || h != height)
        {
            UnityEditor.Undo.RecordObject(owner, "Resize shape");
            rows = Resized(rows, w, h); width = w; height = h; changed = true;
        }

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
            UnityEditor.EditorGUI.DrawRect(r, ColorOf(Get(rows, x, y)));
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && r.Contains(e.mousePosition) && Get(rows, x, y) != brush)
            {
                UnityEditor.Undo.RecordObject(owner, "Paint shape");
                Set(rows, height, x, y, brush); changed = true;
                e.Use();
            }
        }
        UnityEditor.EditorGUIUtility.AddCursorRect(area, UnityEditor.MouseCursor.ArrowPlus);

        UnityEditor.EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fill floor")) { UnityEditor.Undo.RecordObject(owner, "Fill shape"); FillAll(rows, width, height, '#'); changed = true; }
        if (GUILayout.Button("Clear")) { UnityEditor.Undo.RecordObject(owner, "Clear shape"); FillAll(rows, width, height, '.'); changed = true; }
        if (GUILayout.Button("Outline pillars")) { UnityEditor.Undo.RecordObject(owner, "Pillars"); Colonnade(rows, width, height); changed = true; }
        UnityEditor.EditorGUILayout.EndHorizontal();

        var shape = ToShape(width, height, rows, owner.name);
        if (shape.FloorCount == 0) UnityEditor.EditorGUILayout.HelpBox("Paint at least one floor cell.", UnityEditor.MessageType.Error);
        else if (!shape.IsConnected()) UnityEditor.EditorGUILayout.HelpBox("Floor cells are split into separate pockets. Every floor cell must be reachable.", UnityEditor.MessageType.Warning);
        else UnityEditor.EditorGUILayout.HelpBox($"{shape.FloorCount} floor cells. Centre at {shape.center}.", UnityEditor.MessageType.None);

        if (changed) UnityEditor.EditorUtility.SetDirty(owner);
        return changed;
    }

    static void Set(string[] rows, int height, int x, int y, char c)
    {
        // Only one centre.
        if (c == '*') for (int j = 0; j < height; j++) rows[j] = rows[j].Replace('*', '#');
        var chars = rows[y].ToCharArray(); chars[x] = c; rows[y] = new string(chars);
    }
    static void FillAll(string[] rows, int width, int height, char c) { for (int y = 0; y < height; y++) rows[y] = new string(c, width); }
    static void Colonnade(string[] rows, int width, int height)
    {
        // Pillars every other cell, one aisle in from the outline.
        for (int x = 1; x < width - 1; x++) for (int y = 1; y < height - 1; y++)
            if (x % 2 == 0 && y % 2 == 0 && Get(rows, x, y) == '#') Set(rows, height, x, y, 'O');
    }
#endif
}

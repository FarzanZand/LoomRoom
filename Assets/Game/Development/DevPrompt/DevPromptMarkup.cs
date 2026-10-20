#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

// Freehand marks on a developer request's screenshot. The original PNG is kept as it was taken; the marks are paths in
// the screenshot's own pixels (colour index + points), and the marked picture is always rebuilt from the original, so
// nothing is scaled twice. Undo/Redo/Clear go through one bounded history (Clear can be undone; a new stroke after an
// undo drops what could be redone).
public class DevPromptMarkup
{
    public static readonly Color32[] Palette =
    {
        new(235, 60, 50, 255),    // R
        new(70, 210, 90, 255),    // G
        new(70, 150, 255, 255),   // B
        new(255, 215, 50, 255),   // Y
    };
    public const int MaxHistory = 64;

    public class Stroke { public int colour; public int width; public readonly List<Vector2Int> points = new(); }

    readonly Texture2D original;
    readonly Color32[] originalPixels;
    List<Stroke> strokes = new();
    readonly List<List<Stroke>> undo = new(), redo = new();
    Texture2D composed;
    bool dirty = true;

    public int Width => original.width;
    public int Height => original.height;
    public bool HasMarks => strokes.Count > 0;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int Colour { get; set; }

    public DevPromptMarkup(Texture2D screenshot)
    {
        original = screenshot;
        originalPixels = screenshot.GetPixels32();
    }

    // Brush width in screenshot pixels: about 0.8% of its height (9 at 1080p), at least 3, so marks read in a thumbnail.
    public int BrushWidth => Mathf.Max(3, Mathf.RoundToInt(Height * .008f));

    Stroke current;
    public void BeginStroke(Vector2Int p)
    {
        Remember();
        current = new Stroke { colour = Colour, width = BrushWidth };
        current.points.Add(Clamp(p));
        strokes.Add(current);
        dirty = true;
    }
    public void ContinueStroke(Vector2Int p)
    {
        if (current == null) return;
        p = Clamp(p);
        if (current.points[^1] == p) return;
        current.points.Add(p);
        dirty = true;
    }
    public void EndStroke() => current = null;

    public void Clear()
    {
        if (strokes.Count == 0) return;
        Remember();
        strokes = new List<Stroke>();
        dirty = true;
    }

    public void Undo()
    {
        if (undo.Count == 0) return;
        redo.Add(strokes);
        strokes = undo[^1]; undo.RemoveAt(undo.Count - 1);
        current = null; dirty = true;
    }

    public void Redo()
    {
        if (redo.Count == 0) return;
        undo.Add(strokes);
        strokes = redo[^1]; redo.RemoveAt(redo.Count - 1);
        current = null; dirty = true;
    }

    // Before a change: the strokes as they are go on the undo list; anything that could be redone is gone.
    void Remember()
    {
        undo.Add(new List<Stroke>(strokes));
        if (undo.Count > MaxHistory) undo.RemoveAt(0);
        redo.Clear();
    }

    Vector2Int Clamp(Vector2Int p) => new(Mathf.Clamp(p.x, 0, Width - 1), Mathf.Clamp(p.y, 0, Height - 1));

    // The screenshot with its marks, rebuilt from the original whenever a mark changed. Point-filtered.
    public Texture2D Composed
    {
        get
        {
            if (composed == null) { composed = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp }; dirty = true; }
            if (!dirty) return composed;
            var pixels = (Color32[])originalPixels.Clone();
            foreach (var s in strokes) Draw(pixels, s);
            composed.SetPixels32(pixels);
            composed.Apply(false);
            dirty = false;
            return composed;
        }
    }

    public byte[] EncodePng() => (HasMarks ? Composed : original).EncodeToPNG();

    void Draw(Color32[] pixels, Stroke s)
    {
        var colour = Palette[Mathf.Clamp(s.colour, 0, Palette.Length - 1)];
        if (s.points.Count == 1) { Dot(pixels, s.points[0], s.width, colour); return; }
        for (int i = 1; i < s.points.Count; i++) Line(pixels, s.points[i - 1], s.points[i], s.width, colour);
    }

    void Line(Color32[] pixels, Vector2Int a, Vector2Int b, int width, Color32 colour)
    {
        int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : i / (float)steps;
            Dot(pixels, new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t))), width, colour);
        }
    }

    void Dot(Color32[] pixels, Vector2Int c, int width, Color32 colour)
    {
        int r = width / 2;
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (dx * dx + dy * dy > r * r + r) continue;
            int x = c.x + dx, y = c.y + dy;
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            pixels[y * Width + x] = colour;
        }
    }

    public void Release()
    {
        if (composed != null) Object.Destroy(composed);
        if (original != null) Object.Destroy(original);
    }
}
#endif

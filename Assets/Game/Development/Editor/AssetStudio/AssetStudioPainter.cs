using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Painting straight onto a model in Asset Studio's preview. The model's textures (the base map of each material) are
// loaded from their PNG files at full size into editable copies shown on the preview's own material copies, so every
// dab shows at once. A dab is found by casting the mouse ray at the real triangles (a skinned mesh as it stands) and
// reading the UV there; the brush is a square or a round block of whole texels (size 1 = one texel, the game's pixel).
// Eyedropper reads the texture at that point; Eraser puts the original texels back. Undo/Redo per stroke. Save writes
// each changed PNG back to its file (the old file first copied to ArtRevisions/<texture>/<time>.png, git-ignored) and
// reimports it; Revert throws the changes away. Only the game's own textures (under Assets/Game) can be painted:
// Synty's originals are never edited.
public class AssetStudioPainter
{
    public enum Tool { Paint, Eyedropper, Eraser, Fill }

    class Part
    {
        public Renderer renderer;
        public Mesh mesh;              // a skinned mesh baked as it stands
        public Matrix4x4 toWorld, toLocal;
        public Vector3[] vertices;
        public Vector2[] uvs;
        public int[][] triangles;      // per submesh
        public Sheet[] sheets;         // per submesh: what is painted there (null: nothing paintable)
    }

    public class Sheet
    {
        public string path;            // the PNG under Assets/Game
        public Texture2D texture;      // the editable copy, at the file's own size
        public Color32[] original;     // as the file was when painting began
        public bool dirty;
        public int width => texture.width;
        public int height => texture.height;
    }

    readonly List<Part> parts = new();
    readonly Dictionary<string, Sheet> sheets = new();
    readonly List<Material> materialCopies = new();
    readonly List<Dictionary<Sheet, Color32[]>> undo = new(), redo = new();
    Dictionary<Sheet, Color32[]> strokeBefore;
    public string Problem { get; private set; }

    public Tool tool = Tool.Paint;
    public Color32 colour = new(200, 60, 40, 255);
    public int size = 1;               // texels across
    public bool round;
    public readonly List<Color32> swatches = new();

    public bool Active => parts.Count > 0;
    public int Changed => sheets.Values.Count(s => s.dirty);
    public IEnumerable<Sheet> Sheets => sheets.Values;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    static readonly string[] TextureProperties = { "_BaseMap", "_MainTex", "_BaseColorMap" };

    // Gets the model's parts and textures ready. False (with Problem said) when nothing on it can be painted.
    public bool Begin(GameObject instance)
    {
        End(false);
        Problem = null;
        var skipped = new HashSet<string>();
        foreach (var r in instance.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            Mesh mesh = null;
            Matrix4x4 toWorld;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                smr.BakeMesh(mesh, true);
                toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                mesh = mf.sharedMesh;
                toWorld = r.transform.localToWorldMatrix;
            }
            Vector2[] uvs;
            try { uvs = mesh.uv; } catch { skipped.Add(mesh.name + " (mesh not readable)"); continue; }
            if (uvs == null || uvs.Length == 0) continue;
            var part = new Part { renderer = r, mesh = mesh, toWorld = toWorld, toLocal = toWorld.inverse, vertices = mesh.vertices, uvs = uvs };
            part.triangles = Enumerable.Range(0, mesh.subMeshCount).Select(i => mesh.GetTriangles(i)).ToArray();
            var mats = r.sharedMaterials;
            part.sheets = new Sheet[mesh.subMeshCount];
            var copies = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                var copy = new Material(m) { hideFlags = HideFlags.HideAndDontSave };
                materialCopies.Add(copy);
                copies[i] = copy;
                string prop = TextureProperties.FirstOrDefault(p => m.HasProperty(p) && m.GetTexture(p) is Texture2D);
                if (prop == null || i >= part.sheets.Length) continue;
                var sheet = SheetFor(m.GetTexture(prop) as Texture2D, skipped);
                if (sheet == null) continue;
                part.sheets[i] = sheet;
                foreach (var p in TextureProperties) if (copy.HasProperty(p) && copy.GetTexture(p) == m.GetTexture(prop)) copy.SetTexture(p, sheet.texture);
            }
            r.sharedMaterials = copies;
            if (part.sheets.Any(s => s != null)) parts.Add(part);
        }
        if (parts.Count == 0)
            Problem = skipped.Count > 0 ? "Nothing here can be painted: " + string.Join(", ", skipped.Take(3)) : "This model has no texture to paint.";
        else if (skipped.Count > 0) Problem = "Not paintable here: " + string.Join(", ", skipped.Take(3));
        return parts.Count > 0;
    }

    Sheet SheetFor(Texture2D source, HashSet<string> skipped)
    {
        string path = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrEmpty(path)) return null;
        if (sheets.TryGetValue(path, out var known)) return known;
        if (!path.StartsWith("Assets/Game/") || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        { skipped.Add(Path.GetFileName(path) + (path.StartsWith("Assets/Game/") ? " (not a PNG)" : " (a library original, never edited)")); return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = source.wrapMode, hideFlags = HideFlags.HideAndDontSave };
        if (!tex.LoadImage(File.ReadAllBytes(path))) { skipped.Add(Path.GetFileName(path) + " (could not be read)"); return null; }
        tex.filterMode = FilterMode.Point;
        var sheet = new Sheet { path = path, texture = tex, original = tex.GetPixels32() };
        sheets[path] = sheet;
        return sheet;
    }

    // Throws the editable copies away (the preview instance is thrown away by the preview); keep: leave the textures.
    public void End(bool keep)
    {
        foreach (var p in parts) if (p.renderer is SkinnedMeshRenderer && p.mesh != null) UnityEngine.Object.DestroyImmediate(p.mesh);
        parts.Clear();
        if (!keep) { foreach (var s in sheets.Values) UnityEngine.Object.DestroyImmediate(s.texture); sheets.Clear(); }
        foreach (var m in materialCopies) if (m != null) UnityEngine.Object.DestroyImmediate(m);
        materialCopies.Clear();
        undo.Clear(); redo.Clear(); strokeBefore = null;
    }

    // ── Hitting the model ──

    public struct Hit { public Sheet sheet; public Vector2 uv; public Vector3 point; public float distance; public float worldPerUv; }

    public bool Raycast(Ray ray, out Hit hit)
    {
        hit = default;
        float best = float.MaxValue;
        foreach (var part in parts)
        {
            var o = part.toLocal.MultiplyPoint3x4(ray.origin);
            var d = part.toLocal.MultiplyVector(ray.direction);
            for (int sm = 0; sm < part.triangles.Length; sm++)
            {
                var sheet = part.sheets[sm];
                if (sheet == null) continue;
                var tris = part.triangles[sm];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = part.vertices[tris[i]]; var b = part.vertices[tris[i + 1]]; var c = part.vertices[tris[i + 2]];
                    if (!Intersect(o, d, a, b, c, out float t, out float u, out float v)) continue;
                    var world = part.toWorld.MultiplyPoint3x4(o + d * t);
                    float dist = Vector3.Distance(ray.origin, world);
                    if (dist >= best) continue;
                    best = dist;
                    var uv = part.uvs[tris[i]] * (1 - u - v) + part.uvs[tris[i + 1]] * u + part.uvs[tris[i + 2]] * v;
                    var ua = part.uvs[tris[i]]; var ub = part.uvs[tris[i + 1]]; var uc = part.uvs[tris[i + 2]];
                    float uvArea = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y));
                    float worldArea = Vector3.Cross(part.toWorld.MultiplyVector(b - a), part.toWorld.MultiplyVector(c - a)).magnitude;
                    hit = new Hit { sheet = sheet, uv = uv, point = world, distance = dist, worldPerUv = uvArea > 1e-12f ? Mathf.Sqrt(worldArea / uvArea) : 0 };
                }
            }
        }
        return best < float.MaxValue;
    }

    // Möller–Trumbore, both faces.
    static bool Intersect(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
    {
        t = u = v = 0;
        var e1 = b - a; var e2 = c - a;
        var p = Vector3.Cross(d, e2);
        float det = Vector3.Dot(e1, p);
        if (Mathf.Abs(det) < 1e-9f) return false;
        float inv = 1f / det;
        var s = o - a;
        u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(s, e1);
        v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) return false;
        t = Vector3.Dot(e2, q) * inv;
        return t > 0;
    }

    public static Vector2Int TexelAt(Hit h) => Texel(h.sheet, h.uv);

    static Vector2Int Texel(Sheet s, Vector2 uv)
    {
        float x = uv.x - Mathf.Floor(uv.x), y = uv.y - Mathf.Floor(uv.y);
        return new Vector2Int(Mathf.Clamp((int)(x * s.width), 0, s.width - 1), Mathf.Clamp((int)(y * s.height), 0, s.height - 1));
    }

    // ── Strokes ──

    public void BeginStroke() { strokeBefore = new Dictionary<Sheet, Color32[]>(); }

    public void EndStroke()
    {
        if (strokeBefore != null && strokeBefore.Count > 0)
        {
            undo.Add(strokeBefore);
            if (undo.Count > 64) undo.RemoveAt(0);
            redo.Clear();
        }
        strokeBefore = null;
    }

    void Remember(Sheet s)
    {
        if (strokeBefore == null) BeginStroke();
        if (!strokeBefore.ContainsKey(s)) strokeBefore[s] = s.texture.GetPixels32();
    }

    // Applies the tool at a hit. Returns true when the picture changed (Eyedropper changes the colour instead).
    public bool Apply(Hit hit)
    {
        var s = hit.sheet;
        var centre = Texel(s, hit.uv);
        if (tool == Tool.Eyedropper) { colour = s.texture.GetPixel(centre.x, centre.y); return false; }
        Remember(s);
        if (tool == Tool.Fill) { Fill(s, centre); s.texture.Apply(false); s.dirty = true; return true; }
        int r0 = (size - 1) / 2, r1 = size / 2;
        float radius = size * .5f;
        for (int dx = -r0; dx <= r1; dx++)
        for (int dy = -r0; dy <= r1; dy++)
        {
            if (round && size > 2 && (dx + .5f - (r1 - r0) * .5f) * (dx + .5f - (r1 - r0) * .5f) + (dy + .5f - (r1 - r0) * .5f) * (dy + .5f - (r1 - r0) * .5f) > radius * radius) continue;
            int x = centre.x + dx, y = centre.y + dy;
            if (x < 0 || y < 0 || x >= s.width || y >= s.height) continue;
            s.texture.SetPixel(x, y, tool == Tool.Eraser ? (Color)s.original[y * s.width + x] : (Color)colour);
        }
        s.texture.Apply(false);
        s.dirty = true;
        return true;
    }

    // Flood fill of the touching texels of the same colour (exact match).
    void Fill(Sheet s, Vector2Int start)
    {
        var px = s.texture.GetPixels32();
        var target = px[start.y * s.width + start.x];
        if (Same(target, colour)) return;
        var stack = new Stack<Vector2Int>();
        stack.Push(start);
        int count = 0;
        while (stack.Count > 0 && count < 1 << 20)
        {
            var p = stack.Pop();
            if (p.x < 0 || p.y < 0 || p.x >= s.width || p.y >= s.height) continue;
            int i = p.y * s.width + p.x;
            if (!Same(px[i], target)) continue;
            px[i] = colour; count++;
            stack.Push(new Vector2Int(p.x + 1, p.y)); stack.Push(new Vector2Int(p.x - 1, p.y));
            stack.Push(new Vector2Int(p.x, p.y + 1)); stack.Push(new Vector2Int(p.x, p.y - 1));
        }
        s.texture.SetPixels32(px);
    }

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    public void Undo() => Swap(undo, redo);
    public void Redo() => Swap(redo, undo);

    void Swap(List<Dictionary<Sheet, Color32[]>> from, List<Dictionary<Sheet, Color32[]>> to)
    {
        if (from.Count == 0) return;
        var state = from[^1]; from.RemoveAt(from.Count - 1);
        var back = new Dictionary<Sheet, Color32[]>();
        foreach (var kv in state)
        {
            back[kv.Key] = kv.Key.texture.GetPixels32();
            kv.Key.texture.SetPixels32(kv.Value); kv.Key.texture.Apply(false);
            kv.Key.dirty = !kv.Value.SequenceEqual(kv.Key.original, Color32Comparer.Instance);
        }
        to.Add(back);
    }

    class Color32Comparer : IEqualityComparer<Color32>
    {
        public static readonly Color32Comparer Instance = new();
        public bool Equals(Color32 a, Color32 b) => Same(a, b);
        public int GetHashCode(Color32 c) => c.r | c.g << 8 | c.b << 16 | c.a << 24;
    }

    // Puts every texture back as its file is.
    public void Revert()
    {
        foreach (var s in sheets.Values) { s.texture.SetPixels32(s.original); s.texture.Apply(false); s.dirty = false; }
        undo.Clear(); redo.Clear();
    }

    // Writes the changed textures to their files: the old file is copied to ArtRevisions first, then the new PNG
    // replaces it (through a temporary file) and Unity reimports it. Returns what was saved.
    public string Save()
    {
        var saved = new List<string>();
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtRevisions"));
        foreach (var s in sheets.Values.Where(x => x.dirty))
        {
            string full = Path.GetFullPath(s.path);
            string folder = Path.Combine(root, Path.GetFileNameWithoutExtension(s.path));
            Directory.CreateDirectory(folder);
            string backup = Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
            File.Copy(full, backup, true);
            if (new FileInfo(backup).Length != new FileInfo(full).Length) throw new IOException("The backup of " + s.path + " is not complete; nothing was written.");
            string temp = full + ".tmp";
            File.WriteAllBytes(temp, s.texture.EncodeToPNG());
            File.Copy(temp, full, true);
            File.Delete(temp);
            AssetDatabase.ImportAsset(s.path, ImportAssetOptions.ForceUpdate);
            s.original = s.texture.GetPixels32();
            s.dirty = false;
            saved.Add(Path.GetFileName(s.path));
        }
        undo.Clear(); redo.Clear();
        return saved.Count == 0 ? "Nothing changed." : "Saved " + string.Join(", ", saved) + " (old copies in ArtRevisions).";
    }

    public void AddSwatch()
    {
        swatches.RemoveAll(c => Same(c, colour));
        swatches.Insert(0, colour);
        if (swatches.Count > 12) swatches.RemoveAt(swatches.Count - 1);
    }
}

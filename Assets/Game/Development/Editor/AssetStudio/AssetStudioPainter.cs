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
        public bool[] twoSided;        // per submesh: its material draws both faces (otherwise back faces are culled)
        public float[] cutoff;         // per submesh: its material's alpha clip (0: none): see-through texels aren't hit
    }

    // A texel the material clips away (an eye layer over a face): the mouse goes through it to what is drawn behind.
    static bool SeeThrough(Sheet sheet, float cutoff, Vector2 uv)
    {
        if (cutoff <= 0f || sheet == null) return false;
        var t = TexelAt(new Hit { sheet = sheet, uv = uv });
        return sheet.texture.GetPixel(t.x, t.y).a < cutoff;
    }

    public class Sheet
    {
        public string path;            // the PNG under Assets/Game
        public Texture2D texture;      // the editable copy, at the file's own size
        public Color32[] original;     // as the file was when painting began
        public bool dirty;
        public Vector3[] pos, nrm;     // per texel: where it sits on the model and which way it faces there
        public bool[] covered;         // texels some triangle shows
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
    public readonly List<Color32> palette = new();   // the model's 16 most used colours

    // The model's 16 main colours: texels the model shows (not unused texture space) are counted in small colour boxes,
    // and the most used boxes are taken while each is clearly different from those already taken.
    void BuildPalette()
    {
        palette.Clear();
        var counts = new Dictionary<int, int>();
        var sums = new Dictionary<int, Vector3>();
        foreach (var sheet in sheets.Values)
            for (int i = 0; i < sheet.original.Length; i++)
            {
                var c = sheet.original[i];
                if (c.a < 128 || sheet.covered != null && !sheet.covered[i]) continue;
                int key = (c.r >> 3) | (c.g >> 3) << 5 | (c.b >> 3) << 10;
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                sums[key] = (sums.TryGetValue(key, out var v) ? v : Vector3.zero) + new Vector3(c.r, c.g, c.b);
            }
        foreach (var minDistance in new[] { 40f, 24f, 12f, 0f })
        {
            foreach (var kv in counts.OrderByDescending(kv => kv.Value))
            {
                if (palette.Count >= 16) break;
                var m = sums[kv.Key] / kv.Value;
                var c = new Color32((byte)m.x, (byte)m.y, (byte)m.z, 255);
                if (palette.Any(p => Mathf.Abs(p.r - c.r) + Mathf.Abs(p.g - c.g) + Mathf.Abs(p.b - c.b) < minDistance * 1.7f || p.r == c.r && p.g == c.g && p.b == c.b)) continue;
                palette.Add(c);
            }
            if (palette.Count >= 16) break;
        }
        // Dark to light, so it reads as a ramp.
        palette.Sort((a, b) => (a.r * 3 + a.g * 6 + a.b).CompareTo(b.r * 3 + b.g * 6 + b.b));
    }

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
                toWorld = BakedToWorld(smr, mesh);
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
            part.twoSided = new bool[mesh.subMeshCount];
            part.cutoff = new float[mesh.subMeshCount];
            var copies = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                if (i < part.twoSided.Length) part.twoSided[i] = m.HasProperty("_Cull") && m.GetFloat("_Cull") < .5f || m.HasProperty("_CullMode") && m.GetFloat("_CullMode") < .5f;
                if (i < part.cutoff.Length && (m.HasProperty("_AlphaClip") && m.GetFloat("_AlphaClip") > .5f || m.IsKeywordEnabled("_ALPHATEST_ON")))
                    part.cutoff[i] = m.HasProperty("_Cutoff") ? Mathf.Max(.01f, m.GetFloat("_Cutoff")) : .5f;
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
        if (parts.Count > 0) MapSurface();
        BuildPalette();
        if (parts.Count == 0)
            Problem = skipped.Count > 0 ? "Nothing here can be painted: " + string.Join(", ", skipped.Take(3)) : "This model has no texture to paint.";
        else if (skipped.Count > 0) Problem = "Not paintable here: " + string.Join(", ", skipped.Take(3));
        return parts.Count > 0;
    }

    // Where a baked skinned mesh stands in the world. Unity's bake leaves out scale on the renderer's parents (the room
    // player's rig is scaled 20 times): placed by position and rotation alone, such a mesh came out 20 times too small
    // at its feet, so nothing on it could be hit. Both placements are tried and the one as big as the mesh really is
    // (its own bounds at the renderer's world scale) wins.
    public static Matrix4x4 BakedToWorld(SkinnedMeshRenderer smr, Mesh baked)
    {
        var plain = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        var scaled = smr.transform.localToWorldMatrix;
        if (smr.sharedMesh == null) return plain;
        float want = Vector3.Scale(smr.sharedMesh.bounds.size, smr.transform.lossyScale).magnitude;
        float bakedSize = baked.bounds.size.magnitude;
        float SizeOff(Matrix4x4 m) => Mathf.Abs(Mathf.Log(Mathf.Max(1e-6f, m.lossyScale.x * bakedSize) / Mathf.Max(1e-6f, want)));
        return SizeOff(scaled) < SizeOff(plain) ? scaled : plain;
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
        ReleasePick();
    }

    // ── Hitting the model ──

    public struct Hit { public Sheet sheet; public Vector2 uv; public Vector3 point; public float distance; public float worldPerUv; public Vector3 normal; }

    public bool Raycast(Ray ray, out Hit hit)
    {
        hit = default;
        float best = float.MaxValue;
        foreach (var part in parts)
        {
            var o = part.toLocal.MultiplyPoint3x4(ray.origin);
            bool mirrored = part.toWorld.determinant < 0;   // a mirrored model turns its faces' winding round
            var d = part.toLocal.MultiplyVector(ray.direction);
            for (int sm = 0; sm < part.triangles.Length; sm++)
            {
                var sheet = part.sheets[sm];
                if (sheet == null) continue;
                var tris = part.triangles[sm];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    var a = part.vertices[tris[i]]; var b = part.vertices[tris[i + 1]]; var c = part.vertices[tris[i + 2]];
                    // Only faces the camera sees: the GPU culls back faces, and on a curved or layered model a back face
                    // can lie in front of the visible surface (the paint then landed somewhere else).
                    if (!part.twoSided[sm] && Vector3.Dot(Vector3.Cross(b - a, c - a), d) * (mirrored ? -1 : 1) >= 0) continue;
                    if (!Intersect(o, d, a, b, c, out float t, out float u, out float v)) continue;
                    var world = part.toWorld.MultiplyPoint3x4(o + d * t);
                    float dist = Vector3.Distance(ray.origin, world);
                    if (dist >= best) continue;
                    var uv = part.uvs[tris[i]] * (1 - u - v) + part.uvs[tris[i + 1]] * u + part.uvs[tris[i + 2]] * v;
                    if (SeeThrough(sheet, part.cutoff[sm], uv)) continue;
                    best = dist;
                    var ua = part.uvs[tris[i]]; var ub = part.uvs[tris[i + 1]]; var uc = part.uvs[tris[i + 2]];
                    float uvArea = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y));
                    float worldArea = Vector3.Cross(part.toWorld.MultiplyVector(b - a), part.toWorld.MultiplyVector(c - a)).magnitude;
                    var wn = Vector3.Cross(part.toWorld.MultiplyVector(b - a), part.toWorld.MultiplyVector(c - a)).normalized;
                    hit = new Hit { sheet = sheet, uv = uv, point = world, distance = dist, worldPerUv = uvArea > 1e-12f ? Mathf.Sqrt(worldArea / uvArea) : 0, normal = wn };
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

    // Where in the texture a UV falls, in texels (not rounded).
    static Vector2 TexelPoint(Sheet s, Vector2 uv) => new((uv.x - Mathf.Floor(uv.x)) * s.width, (uv.y - Mathf.Floor(uv.y)) * s.height);

    public static Color32 Sample(Hit h) { var t = Texel(h.sheet, h.uv); return h.sheet.texture.GetPixel(t.x, t.y); }

    static Vector2Int Texel(Sheet s, Vector2 uv)
    {
        float x = uv.x - Mathf.Floor(uv.x), y = uv.y - Mathf.Floor(uv.y);
        return new Vector2Int(Mathf.Clamp((int)(x * s.width), 0, s.width - 1), Mathf.Clamp((int)(y * s.height), 0, s.height - 1));
    }

    // ── Where each texel sits on the model ──
    // Every texel a triangle covers gets the surface point and facing there, and a spatial grid finds the texels near a
    // point. A brush then paints the texels within its reach on the surface, wherever they sit in the texture: across
    // UV seams, and never onto another part of the model that merely lies next to it in the texture.

    readonly Dictionary<Vector3Int, List<(Sheet sheet, int texel)>> grid = new();
    float cell = .01f;

    void MapSurface()
    {
        grid.Clear();
        var bounds = new Bounds();
        bool any = false;
        foreach (var part in parts)
            foreach (var v in part.vertices) { var w = part.toWorld.MultiplyPoint3x4(v); if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; } else bounds.Encapsulate(w); }
        cell = Mathf.Max(bounds.size.magnitude / 160f, 1e-4f);
        foreach (var s in sheets.Values) { s.pos = new Vector3[s.width * s.height]; s.nrm = new Vector3[s.width * s.height]; s.covered = new bool[s.width * s.height]; }
        foreach (var part in parts)
            for (int sm = 0; sm < part.triangles.Length; sm++)
            {
                var s = part.sheets[sm];
                if (s == null) continue;
                var tris = part.triangles[sm];
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    Vector3 a = part.toWorld.MultiplyPoint3x4(part.vertices[tris[i]]), b = part.toWorld.MultiplyPoint3x4(part.vertices[tris[i + 1]]), c = part.toWorld.MultiplyPoint3x4(part.vertices[tris[i + 2]]);
                    var n = Vector3.Cross(b - a, c - a).normalized;
                    Vector2 ua = Scale(part.uvs[tris[i]], s), ub = Scale(part.uvs[tris[i + 1]], s), uc = Scale(part.uvs[tris[i + 2]], s);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.x, ub.x, uc.x))), x1 = Mathf.Min(s.width - 1, Mathf.CeilToInt(Mathf.Max(ua.x, ub.x, uc.x)));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ua.y, ub.y, uc.y))), y1 = Mathf.Min(s.height - 1, Mathf.CeilToInt(Mathf.Max(ua.y, ub.y, uc.y)));
                    float den = (ub.y - uc.y) * (ua.x - uc.x) + (uc.x - ub.x) * (ua.y - uc.y);
                    if (Mathf.Abs(den) < 1e-9f) continue;
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + .5f, py = y + .5f;
                        float wa = ((ub.y - uc.y) * (px - uc.x) + (uc.x - ub.x) * (py - uc.y)) / den;
                        float wb = ((uc.y - ua.y) * (px - uc.x) + (ua.x - uc.x) * (py - uc.y)) / den;
                        float wc = 1 - wa - wb;
                        const float e = -.02f;   // a hair over the edge, so texels on a seam belong to both sides
                        if (wa < e || wb < e || wc < e) continue;
                        int idx = y * s.width + x;
                        if (s.covered[idx]) continue;
                        s.covered[idx] = true;
                        var p = a * wa + b * wb + c * wc;
                        s.pos[idx] = p; s.nrm[idx] = n;
                        var key = Vector3Int.FloorToInt(p / cell);
                        if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<(Sheet, int)>();
                        list.Add((s, idx));
                    }
                }
            }
    }

    static Vector2 Scale(Vector2 uv, Sheet s) => new((uv.x - Mathf.Floor(uv.x)) * s.width, (uv.y - Mathf.Floor(uv.y)) * s.height);

    // The view's axes, set by the preview: a square brush is square on screen.
    public Vector3 viewRight = Vector3.right, viewUp = Vector3.up, viewForward = Vector3.forward;

    // Paints (or erases) the texels within the brush around a hit, measured on the surface. False if none were found.
    bool PaintSurface(Hit hit)
    {
        float texelWorld = hit.worldPerUv > 0 ? hit.worldPerUv / Mathf.Sqrt(hit.sheet.width * hit.sheet.height) : cell;
        float half = size * .5f * texelWorld;
        int reach = Mathf.CeilToInt(half / cell);
        var c0 = Vector3Int.FloorToInt(hit.point / cell);
        var touched = new HashSet<Sheet>();
        int painted = 0;
        for (int x = -reach; x <= reach; x++)
        for (int y = -reach; y <= reach; y++)
        for (int z = -reach; z <= reach; z++)
        {
            if (!grid.TryGetValue(c0 + new Vector3Int(x, y, z), out var list)) continue;
            foreach (var (s, idx) in list)
            {
                var d = s.pos[idx] - hit.point;
                // The side facing the camera only: a thin blade's far face lies within reach too.
                if (Vector3.Dot(s.nrm[idx], hit.normal) < .2f && Vector3.Dot(s.nrm[idx], -viewForward) < .1f) continue;
                if (Mathf.Abs(Vector3.Dot(d, viewForward)) > half * 2 + texelWorld) continue;
                float dx = Vector3.Dot(d, viewRight), dy = Vector3.Dot(d, viewUp);
                bool inside = round ? dx * dx + dy * dy <= half * half : Mathf.Abs(dx) <= half && Mathf.Abs(dy) <= half;
                if (!inside) continue;
                if (!touched.Contains(s)) { Remember(s); touched.Add(s); }
                int tx = idx % s.width, ty = idx / s.width;
                s.texture.SetPixel(tx, ty, tool == Tool.Eraser ? (Color)s.original[idx] : (Color)colour);
                painted++;
            }
        }
        foreach (var s in touched) { s.texture.Apply(false); s.dirty = true; }
        return painted > 0;
    }

    // ── Picking on the GPU ──
    // The model drawn as texture coordinates (Studio Pick.shader) from the preview's own camera; the pixel under the
    // mouse names the part, the material and the exact UV shown there. Where faces lie on top of each other, or the
    // drawn mesh differs from what the CPU reads, a ray cast hit something else than what was on screen.

    static Material pickMaterial;
    RenderTexture pickTarget;
    Texture2D pickPixel;
    readonly MaterialPropertyBlock pickBlock = new();

    public bool Pick(Camera cam, Rect r, Vector2 mouse, out Hit hit)
    {
        hit = default;
        if (parts.Count == 0) return false;
        if (pickMaterial == null)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Game/Development/Editor/AssetStudio/Studio Pick.shader");
            if (shader == null) return false;
            pickMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        int w = Mathf.Max(1, Mathf.RoundToInt(r.width)), h = Mathf.Max(1, Mathf.RoundToInt(r.height));
        if (pickTarget == null || pickTarget.width != w || pickTarget.height != h)
        {
            if (pickTarget != null) { pickTarget.Release(); UnityEngine.Object.DestroyImmediate(pickTarget); }
            pickTarget = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            pickTarget.Create();
        }
        if (pickPixel == null) pickPixel = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { hideFlags = HideFlags.HideAndDontSave };

        var cmd = new UnityEngine.Rendering.CommandBuffer { name = "Asset Studio pick" };
        cmd.SetRenderTarget(pickTarget);
        cmd.ClearRenderTarget(true, true, new Color(0, 0, 0, 0));
        cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, Matrix4x4.Perspective(cam.fieldOfView, r.width / Mathf.Max(1, r.height), cam.nearClipPlane, cam.farClipPlane));
        for (int pi = 0; pi < parts.Count; pi++)
        {
            var part = parts[pi];
            for (int sm = 0; sm < part.sheets.Length; sm++)
            {
                pickBlock.Clear();
                pickBlock.SetFloat("_Id", part.sheets[sm] != null ? pi * 64 + sm + 1 : -1);
                pickBlock.SetFloat("_Cull", part.twoSided[sm] ? 0 : 2);
                pickBlock.SetFloat("_Cutoff", part.cutoff[sm]);
                if (part.sheets[sm] != null) pickBlock.SetTexture("_MainTex", part.sheets[sm].texture);
                cmd.DrawMesh(part.mesh, part.toWorld, pickMaterial, sm, 0, pickBlock);
            }
        }
        Graphics.ExecuteCommandBuffer(cmd);
        cmd.Release();

        int px = Mathf.Clamp(Mathf.FloorToInt(mouse.x - r.x), 0, w - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(mouse.y - r.y), 0, h - 1);
        var prev = RenderTexture.active;
        RenderTexture.active = pickTarget;
        // Render targets are read from the bottom row up.
        pickPixel.ReadPixels(new Rect(px, h - 1 - py, 1, 1), 0, 0, false);
        RenderTexture.active = prev;
        var c = pickPixel.GetPixel(0, 0);
        if (c.a < .5f || c.b < .5f) return false;
        int id = Mathf.RoundToInt(c.b) - 1;
        int partIndex = id / 64, sub = id % 64;
        if (partIndex >= parts.Count || sub >= parts[partIndex].sheets.Length || parts[partIndex].sheets[sub] == null) return false;
        var sheet = parts[partIndex].sheets[sub];
        var uv = new Vector2(c.r, c.g);
        var t = Texel(sheet, uv);
        int idx = t.y * sheet.width + t.x;
        hit = new Hit { sheet = sheet, uv = uv };
        if (sheet.covered != null && sheet.covered[idx])
        {
            hit.point = sheet.pos[idx];
            hit.normal = sheet.nrm[idx];
            // A texel's width on the model, from the next texel along when it sits on the same face.
            int n = t.x + 1 < sheet.width ? idx + 1 : idx - 1;
            float step = sheet.covered[n] ? Vector3.Distance(sheet.pos[n], sheet.pos[idx]) : 0;
            hit.worldPerUv = step > 0 && step < cell * 8 ? step * Mathf.Sqrt(sheet.width * sheet.height) : 0;
        }
        else hit.normal = -cam.transform.forward;
        hit.distance = Vector3.Distance(cam.transform.position, hit.point);
        return true;
    }

    void ReleasePick()
    {
        if (pickTarget != null) { pickTarget.Release(); UnityEngine.Object.DestroyImmediate(pickTarget); pickTarget = null; }
        if (pickPixel != null) { UnityEngine.Object.DestroyImmediate(pickPixel); pickPixel = null; }
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
        // Bigger brushes paint by reach on the surface (see MapSurface); one texel is the texel under the mouse.
        if (size > 1 && PaintSurface(hit)) return true;
        // The block of texels is centred on the exact point under the mouse (not on the texel it falls in), so the
        // brush covers the same spot the outline under the mouse shows, whatever its size.
        var t = TexelPoint(s, hit.uv);
        int x0 = Mathf.RoundToInt(t.x - size * .5f), y0 = Mathf.RoundToInt(t.y - size * .5f);
        float radius = size * .5f;
        for (int x = x0; x < x0 + size; x++)
        for (int y = y0; y < y0 + size; y++)
        {
            if (round && size > 2 && (x + .5f - t.x) * (x + .5f - t.x) + (y + .5f - t.y) * (y + .5f - t.y) > radius * radius) continue;
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

    // Unsaved painting when the Studio closes or scripts reload (no dialog can be shown then): each changed texture is
    // written to ArtRevisions/Unsaved, never over the real file, and the Console says where.
    public string KeepUnsaved()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ArtRevisions", "Unsaved"));
        Directory.CreateDirectory(folder);
        var kept = new List<string>();
        foreach (var s in sheets.Values.Where(x => x.dirty))
        {
            string file = Path.Combine(folder, Path.GetFileNameWithoutExtension(s.path) + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".png");
            File.WriteAllBytes(file, s.texture.EncodeToPNG());
            kept.Add(file);
        }
        if (kept.Count > 0) Debug.Log("[Asset Studio] Unsaved painting kept (the textures are unchanged): " + string.Join(", ", kept));
        return kept.Count > 0 ? "Unsaved painting kept in ArtRevisions/Unsaved." : null;
    }

    public void AddSwatch()
    {
        swatches.RemoveAll(c => Same(c, colour));
        swatches.Insert(0, colour);
        if (swatches.Count > 12) swatches.RemoveAt(swatches.Count - 1);
    }
}

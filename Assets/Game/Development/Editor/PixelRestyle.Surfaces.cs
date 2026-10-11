using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Surfaces that are not single Synty models: the apartment's blockout (stretched cubes, spheres and cylinders in
// flat colours), and textured materials that tile across a mesh (floorboards, paintings).
// Flat colours get one small tiling pixel texture per material (plaster, wood planks, linen, metal, paint) and each
// shape gets UVs in real metres, so a texel is the same size on a 3 m wall and a 10 cm shelf.
// Textured materials get a pixel copy of their texture at low resolution with grain, keeping their own UVs.
public static partial class PixelRestyle
{
    // Per world unit. The apartment is built about 22 units to the real metre (a door is ~47 units), so 1.8 per unit is
    // ~40 texels per real metre, the same density as the restyled props.
    public const float SurfaceTexelsPerMetre = 1.8f;
    const int TileTexels = 64;                       // one tile = 1.6 m at 40 texels per metre

    // Glass, water, glows, text and eyes stay as they are.
    public static bool SkipSurface(Material m)
    {
        if (m == null) return true;
        var n = m.name.ToLowerInvariant(); var sh = m.shader.name.ToLowerInvariant();
        if (n.Contains("glass") || n.Contains("water") || n.Contains("eyes") || n.Contains("screen")) return true;
        if (sh.Contains("sprite") || sh.Contains("textmeshpro") || sh.Contains("dm eyes") || sh.Contains("unlit")) return true;
        if (m.renderQueue > 2500) return true;                                            // transparent (Synty cutout at 2450 is opaque enough)
        if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > .5f) return true;
        return false;
    }

    public static Texture MainTexture(Material m)
    {
        foreach (var p in new[] { "_BaseMap", "_Albedo_Map", "_MainTex" }) if (m.HasProperty(p) && m.GetTexture(p) != null) return m.GetTexture(p);
        return null;
    }

    // ── Flat colours ─────────────────────────────────────────────────────

    public static Kind SurfaceKind(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("walnut") || n.Contains("oak") || n.Contains("wood") || n.Contains("floor")) return Kind.Wood;
        if (n.Contains("linen") || n.Contains("fabric") || n.Contains("cloth") || n.Contains("curtain")) return Kind.Cloth;
        if (n.Contains("brass") || n.Contains("bronze") || n.Contains("metal") || n.Contains("steel")) return Kind.Metal;
        if (n.Contains("limestone") || n.Contains("stone") || n.Contains("tile") || n.Contains("concrete")) return Kind.Stone;
        return Kind.Painted;                                                               // plaster, walls, trim, paint
    }

    // A pixel copy of a flat-colour material: the colour stays on _BaseColor, the tile carries the surface.
    public static Material FlatSurfaceMaterial(Material src)
    {
        string dir = $"{Root}/Apartment"; System.IO.Directory.CreateDirectory(dir);
        var kind = SurfaceKind(src.name);
        string texPath = $"{dir}/{src.name}.png";
        SaveTile(GenerateTile(kind, src.name.GetHashCode()), TileTexels, texPath);
        string matPath = $"{dir}/{src.name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(src) { name = src.name }; AssetDatabase.CreateAsset(mat, matPath); }
        else if (mat != src) mat.CopyPropertiesFromMaterial(src);
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        mat.SetTextureScale("_BaseMap", Vector2.one); mat.SetTextureOffset("_BaseMap", Vector2.zero);
        mat.SetFloat("_Smoothness", Mathf.Min(mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0f, .15f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // A grey tile around 1 (the material's colour does the tinting), seamless.
    static Color[] GenerateTile(Kind kind, int seed)
    {
        int N = TileTexels; var px = new Color[N * N];
        // The noise lattice wraps every N * f cells, exactly once across the tile, so the tile repeats without seams.
        float P(float x, float y, float f, int oct) => PeriodicFbm(x * f, y * f, N * f, oct, seed);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float mottle = P(x, y, 1f / 8f, 3) - .5f;
            float fine = Hash(x + seed, y, 7) - .5f;
            float v = 1f;
            switch (kind)
            {
                case Kind.Wood:
                {
                    // Planks 8 texels wide running along U, a dark seam between them, grain along each plank.
                    int plank = y / 8, inPlank = y % 8;
                    float grain = PeriodicFbm(x / 6f + plank * 13.1f, inPlank / 1.5f, N / 6f, 3, seed + plank) - .5f;
                    float tone = (Hash(plank, seed, 3) - .5f) * .25f;
                    v = 1f + grain * .5f + tone + fine * .12f;
                    if (inPlank == 0) v *= .55f;
                    // Plank ends, staggered.
                    if ((x + plank * 23) % 32 == 0) v *= .6f;
                    break;
                }
                case Kind.Cloth: v = 1f + ((x + y) % 2 == 0 ? .05f : -.05f) + fine * .1f + mottle * .25f; break;
                case Kind.Metal:
                {
                    float scratch = Mathf.Abs(PeriodicFbm(x / 2f, y / 12f, N / 2f, 2, seed) - .5f) < .03f ? .25f : 0f;
                    v = 1f + fine * .2f + mottle * .35f + scratch; break;
                }
                case Kind.Stone: v = 1f + mottle * .55f + fine * .25f; break;
                default:
                {
                    // Plaster and paint: soft unevenness, faint speckle, the odd darker stain.
                    float stain = Mathf.Clamp01((P(x + 17, y + 5, 1f / 16f, 3) - .62f) * 4f);
                    v = 1f + mottle * .18f + fine * .08f - stain * .18f; break;
                }
            }
            v = Mathf.Clamp(v, .45f, 1.25f) * .92f;
            px[y * N + x] = new Color(v, v, v, 1f);
        }
        return px;
    }

    // Seamless value noise: lattice coordinates wrap at the period.
    static float PeriodicFbm(float x, float y, float period, int octaves, int seed)
    {
        float s = 0, a = .5f, t = 0; int per = Mathf.Max(1, Mathf.RoundToInt(period));
        for (int o = 0; o < octaves; o++)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float H(int i, int j) => Hash(((i % per) + per) % per, ((j % per) + per) % per, seed + o * 31);
            s += a * Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), fx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), fx), fy);
            t += a; a *= .5f; x *= 2; y *= 2; per *= 2;
        }
        return s / t;
    }

    static void SaveTile(Color[] px, int N, string path)
    {
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Repeat; ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
    }

    // A copy of a mesh with box-projected UVs in metres (one tile per TileTexels / SurfaceTexelsPerMetre metres),
    // for this object's scale. Shared by every object with the same mesh and scale.
    public static Mesh MetricMesh(Mesh src, Vector3 scale)
    {
        string dir = $"{Root}/Apartment/Meshes"; System.IO.Directory.CreateDirectory(dir);
        var s = new Vector3(Round(scale.x), Round(scale.y), Round(scale.z));
        string path = $"{dir}/{src.name} {s.x:0.###}x{s.y:0.###}x{s.z:0.###}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (existing != null) return existing;
        var m = Object.Instantiate(src); m.name = System.IO.Path.GetFileNameWithoutExtension(path);
        float tileMetres = TileTexels / SurfaceTexelsPerMetre;
        // Every triangle projects along its own dominant axis, so faces are split from their neighbours.
        var v = src.vertices; var n = src.normals; var t = src.triangles;
        var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nu = new List<Vector2>(); var nt = new List<int>();
        for (int i = 0; i < t.Length; i += 3)
        {
            var a = Vector3.Scale(v[t[i]], s); var b = Vector3.Scale(v[t[i + 1]], s); var c = Vector3.Scale(v[t[i + 2]], s);
            var fnrm = Vector3.Cross(b - a, c - a); var ax = new Vector3(Mathf.Abs(fnrm.x), Mathf.Abs(fnrm.y), Mathf.Abs(fnrm.z));
            for (int k = 0; k < 3; k++)
            {
                int vi = t[i + k]; var p = Vector3.Scale(v[vi], s);
                Vector2 uv = ax.x >= ax.y && ax.x >= ax.z ? new Vector2(p.z, p.y) : ax.y >= ax.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
                nv.Add(v[vi]); nn.Add(n.Length > 0 ? n[vi] : Vector3.up); nu.Add(uv / tileMetres); nt.Add(nt.Count);
            }
        }
        m.Clear(); m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu); m.SetTriangles(nt, 0); m.RecalculateBounds(); m.RecalculateTangents();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static float Round(float f) => Mathf.Round(f * 1000f) / 1000f;

    // ── Textured materials ───────────────────────────────────────────────

    // A pixel copy of a textured material: the texture brought down to a small point-filtered one with a little grain.
    public static Material TexturedSurfaceMaterial(Material src)
    {
        var tex = MainTexture(src);
        string dir = $"{Root}/Surfaces"; System.IO.Directory.CreateDirectory(dir);
        bool atlas = tex.width >= 2048 && (src.shader.name.Contains("Synty") || tex.name.Contains("_01_A") || tex.name.Contains("Texture_01"));
        // Atlases keep their swatches (256 px); tiling textures and art go small.
        int target = atlas ? 256 : Mathf.Clamp(tex.width / 16, 32, 128);
        var readable = ReadTexture(tex);
        int W = target, H = Mathf.Max(1, Mathf.RoundToInt(target * (float)tex.height / tex.width));
        var px = new Color[W * H];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            // Box filter over the area of each output texel.
            int sx0 = x * readable.width / W, sx1 = Mathf.Max(sx0 + 1, (x + 1) * readable.width / W), sy0 = y * readable.height / H, sy1 = Mathf.Max(sy0 + 1, (y + 1) * readable.height / H);
            Color sum = Color.clear; int cnt = 0;
            for (int yy = sy0; yy < sy1; yy += Mathf.Max(1, (sy1 - sy0) / 8)) for (int xx = sx0; xx < sx1; xx += Mathf.Max(1, (sx1 - sx0) / 8)) { sum += readable.GetPixel(xx, yy); cnt++; }
            var c = sum / Mathf.Max(1, cnt);
            if (!atlas) { float g = 1f + (Hash(x, y, 91) - .5f) * .1f; c = new Color(c.r * g, c.g * g, c.b * g, c.a); }
            px[y * W + x] = c;
        }
        Object.DestroyImmediate(readable);
        string texPath = $"{dir}/{src.name}.png";
        var t2 = new Texture2D(W, H, TextureFormat.RGBA32, false); t2.SetPixels(px); t2.Apply();
        System.IO.File.WriteAllBytes(texPath, t2.EncodeToPNG()); Object.DestroyImmediate(t2);
        AssetDatabase.ImportAsset(texPath);
        var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
        ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.wrapMode = tex.wrapMode; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
        string matPath = $"{dir}/{src.name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = src.name }; AssetDatabase.CreateAsset(mat, matPath); }
        var scale = src.HasProperty("_BaseMap") ? src.GetTextureScale("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTextureScale("_MainTex") : Vector2.one;
        var offset = src.HasProperty("_BaseMap") ? src.GetTextureOffset("_BaseMap") : Vector2.zero;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); mat.SetTextureScale("_BaseMap", scale); mat.SetTextureOffset("_BaseMap", offset);
        mat.SetColor("_BaseColor", src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white);
        mat.SetFloat("_Smoothness", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Texture2D ReadTexture(Texture tex)
    {
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(tex, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
        var t2 = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false); t2.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0); t2.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        return t2;
    }
}

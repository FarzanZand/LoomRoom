using System.IO;
using UnityEditor;
using UnityEngine;

// What lies under each texel of a restyled prop's texture, for Tools/prop_structure.py: which triangle, its outward
// face normal, the point on the model (0..1 across the model's bounds), its height in metres off the model's base, and
// how open the spot is (ambient occlusion). Written as raw float32, H x W x 9:
// tri, nx, ny, nz, px, py, pz, ao, covered, r, g, b. tri is -1 where no triangle is. r, g, b is the model's own colour
// on that triangle, from the plain restyle (same triangles in the same order, its own UVs and texture).
public static partial class PixelRestyle
{
    const int GB = 12;

    public static string BakeGBuffer(string meshPath, int width, int height, string outFile, string plainMeshPath = null, string plainTexPath = null)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null) return "no mesh at " + meshPath;
        var v = mesh.vertices; var uv = mesh.uv; var t = mesh.triangles; var b = mesh.bounds;
        float size = b.size.magnitude;
        var buf = new float[height * width * GB];
        for (int i = 0; i < width * height; i++) buf[i * GB] = -1f;
        // The model's own colour per triangle.
        var triCol = new Color[t.Length / 3];
        // Synty's own texture per texel (its swatch atlas also holds drawn detail: a skull's eye sockets, nose and
        // teeth are drawn across triangles, and one colour per triangle lost them).
        Texture2D syntyPal = null; Vector2[] syntyUv = null; int[] syntyTri = null; Color syntyTint = Color.white; Vector3[] syntyV = null;
        var plain = plainMeshPath != null ? AssetDatabase.LoadAssetAtPath<Mesh>(plainMeshPath) : null;
        Texture2D plainTex = null;
        if (plainTexPath != null) { plainTex = new Texture2D(2, 2); plainTex.LoadImage(File.ReadAllBytes(plainTexPath)); }
        // "synty:<mesh name>": the original Synty mesh and its atlas of flat colour swatches (the plain restyle's
        // colours are too noisy to tell wood from metal).
        if (plainMeshPath != null && plainMeshPath.StartsWith("synty:") && SyntySource(plainMeshPath.Substring(6), out var sMesh, out var sMats))
        {
            if (sMesh == null) sMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Props/{plainMeshPath.Substring(6)}/{plainMeshPath.Substring(6)}.asset");
            var cache = new System.Collections.Generic.Dictionary<Texture, Texture2D>();
            var st = sMesh.triangles; var suv = new System.Collections.Generic.List<Vector2>(); sMesh.GetUVs(0, suv);
            var pal = Palette(sMats[0], "_BaseMap", "_Albedo_Map", "_MainTex", cache);
            Color tint = sMats[0].HasProperty("_BaseColor") ? sMats[0].GetColor("_BaseColor") : Color.white;
            for (int f = 0; f < t.Length / 3 && f * 3 + 2 < st.Length; f++)
            {
                var u = (suv[st[f * 3]] + suv[st[f * 3 + 1]] + suv[st[f * 3 + 2]]) / 3f;
                triCol[f] = (pal != null ? Sample(pal, u) : Color.white) * tint;
            }
            if (pal != null && st.Length == t.Length) { syntyPal = pal; syntyUv = suv.ToArray(); syntyTri = st; syntyTint = tint; syntyV = sMesh.vertices; }
            foreach (var c in cache.Values) if (c != syntyPal) Object.DestroyImmediate(c);
        }
        else if (plain != null && plainTex != null)
        {
            var pt = plain.triangles; var puv = plain.uv;
            for (int f = 0; f < t.Length / 3 && f * 3 + 2 < pt.Length; f++)
            {
                Color sum = Color.clear;
                for (int k = 0; k < 7; k++)
                {
                    // the centre and points toward each corner
                    float w0 = k == 0 ? 1f / 3 : k <= 3 ? (k == 1 ? .6f : .2f) : (k == 4 ? .45f : .275f);
                    float w1 = k == 0 ? 1f / 3 : k <= 3 ? (k == 2 ? .6f : .2f) : (k == 5 ? .45f : .275f);
                    float w2 = 1 - w0 - w1;
                    var u = puv[pt[f * 3]] * w0 + puv[pt[f * 3 + 1]] * w1 + puv[pt[f * 3 + 2]] * w2;
                    sum += plainTex.GetPixelBilinear(u.x, u.y);
                }
                triCol[f] = sum / 7f;
            }
        }

        var probe = new GameObject("PixelRestyle gbuffer probe") { hideFlags = HideFlags.HideAndDontSave, layer = ProbeLayer };
        probe.transform.position = new Vector3(0, -11000, 0);
        probe.AddComponent<MeshCollider>().sharedMesh = mesh;
        Physics.SyncTransforms();
        var rng = new System.Random(7); var dirs = new Vector3[AoRays];
        for (int i = 0; i < AoRays; i++) { float u = (float)rng.NextDouble(), w = (float)rng.NextDouble(); float th = 2 * Mathf.PI * u, ph = Mathf.Acos(2 * w - 1); dirs[i] = new Vector3(Mathf.Sin(ph) * Mathf.Cos(th), Mathf.Cos(ph), Mathf.Sin(ph) * Mathf.Sin(th)); }
        float aoDist = Mathf.Clamp(size * .08f, .015f, .3f);
        try
        {
            for (int f = 0; f < t.Length; f += 3)
            {
                int i0 = t[f], i1 = t[f + 1], i2 = t[f + 2];
                Vector2 a = new Vector2(uv[i0].x * width, uv[i0].y * height), bb = new Vector2(uv[i1].x * width, uv[i1].y * height), c = new Vector2(uv[i2].x * width, uv[i2].y * height);
                var fn = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]).normalized;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(bb.x, c.x)))), x1 = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(bb.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(bb.y, c.y)))), y1 = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(bb.y, c.y))));
                float den = (bb.y - c.y) * (a.x - c.x) + (c.x - bb.x) * (a.y - c.y); if (Mathf.Abs(den) < 1e-9f) continue;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float px = x + .5f, py = y + .5f;
                    float w1 = ((bb.y - c.y) * (px - c.x) + (c.x - bb.x) * (py - c.y)) / den, w2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / den, w3 = 1 - w1 - w2;
                    if (w1 < -.02f || w2 < -.02f || w3 < -.02f) continue;
                    int k = (y * width + x) * GB; if (buf[k + 8] > 0) continue;
                    var p = v[i0] * w1 + v[i1] * w2 + v[i2] * w3;
                    float ao = AmbientOcclusion(probe.transform.position + p, fn, dirs, aoDist);
                    buf[k] = f / 3; buf[k + 1] = fn.x; buf[k + 2] = fn.y; buf[k + 3] = fn.z;
                    buf[k + 4] = (p.x - b.min.x) / Mathf.Max(1e-6f, b.size.x); buf[k + 5] = (p.y - b.min.y) / Mathf.Max(1e-6f, b.size.y); buf[k + 6] = (p.z - b.min.z) / Mathf.Max(1e-6f, b.size.z);
                    buf[k + 7] = ao; buf[k + 8] = 1f;
                    var tc = triCol[f / 3];
                    if (syntyPal != null)
                    {
                        // Each corner's Synty UV by position: the restyle can start a triangle at another corner,
                        // which turned the drawn detail inside it (a skull's teeth ran up its side).
                        Vector2 U(int corner)
                        {
                            var at = v[t[f + corner]]; int best = 0; float bd = float.MaxValue;
                            for (int q = 0; q < 3; q++) { float dq = (syntyV[syntyTri[f + q]] - at).sqrMagnitude; if (dq < bd) { bd = dq; best = q; } }
                            return syntyUv[syntyTri[f + best]];
                        }
                        tc = Sample(syntyPal, U(0) * w1 + U(1) * w2 + U(2) * w3) * syntyTint;
                    }
                    buf[k + 9] = tc.r; buf[k + 10] = tc.g; buf[k + 11] = tc.b;
                }
            }
        }
        finally { Object.DestroyImmediate(probe); if (plainTex != null) Object.DestroyImmediate(plainTex); if (syntyPal != null) Object.DestroyImmediate(syntyPal); }
        var bytes = new byte[buf.Length * 4]; System.Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
        File.WriteAllBytes(outFile, bytes);
        return $"{outFile}: {width}x{height}, {t.Length / 3} triangles";
    }
}

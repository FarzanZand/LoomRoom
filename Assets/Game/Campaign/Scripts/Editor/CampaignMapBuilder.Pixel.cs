using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// The map's pixel size: everything on it is drawn at the bedroom's density (its rug, floor, the table's edge, the
// painted furniture: 6.4 texels a unit, measured), so the map reads as part of the room it lies in, and the ground, the
// bases, the cloth and every model share one pixel size.
// The Synty models on the map get pixel copies (PixelRestyle: fresh UVs, their own colours, grain, baked creases),
// baked per mesh and size step (a mesh used at very different sizes gets one bake per step of x1.2, so no copy is more
// than ~10% off), tinted like the originals. Run from an eval: CampaignMapBuilder.PixelProps() (it saves the map
// prefab; bakes are kept in Map/Generated/Pixel, so a second run only does what is new). Build() runs it too.
public static partial class CampaignMapBuilder
{
    public const float MapTexelsPerUnit = 6.4f;   // the bedroom's painted surfaces and props, measured
    const string PixelDir = MapDir + "/Pixel";   // outside Generated, which Build clears
    const float SizeStep = 1.2f;

    // A short key for a mesh asset and the materials' textures (not their tints): which source a bake came from.
    public static string SourceKey(Mesh mesh, Material[] mats)
    {
        var sb = new System.Text.StringBuilder(AssetDatabase.GetAssetPath(mesh)).Append(mesh.name);
        foreach (var m in mats) { var t = m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null; sb.Append('|').Append(t != null ? AssetDatabase.GetAssetPath(t) + t.name : m != null ? m.shader.name : "-"); }
        unchecked { uint h = 2166136261; foreach (char c in sb.ToString()) h = (h ^ c) * 16777619; return h.ToString("x8").Substring(0, 6); }
    }

    static bool SameUnwrap(Mesh a, Mesh b)
    {
        if (a == null || b == null || a.vertexCount != b.vertexCount || a.subMeshCount != b.subMeshCount) return false;
        var ua = new List<Vector2>(); var ub = new List<Vector2>(); a.GetUVs(0, ua); b.GetUVs(0, ub);
        if (ua.Count != ub.Count) return false;
        for (int i = 0; i < ua.Count; i++) if ((ua[i] - ub[i]).sqrMagnitude > 1e-10f) return false;
        var ta = a.triangles; var tb = b.triangles;
        if (ta.Length != tb.Length) return false;
        for (int i = 0; i < ta.Length; i++) if (ta[i] != tb[i]) return false;
        return true;
    }

    // A model too small for even the smallest texture (pebbles, flowers, a lantern: a few texels across) draws finer
    // than the map. Its materials' tiling is narrowed until it measures right (point filtered, so it just takes fewer
    // of the bake's texels across). Measured once per bake per run, so a cached bake is put right too.
    static void FitSmall(Mesh mesh, Material pixel, string dir, float bakeScale)
    {
        var probe = new GameObject("Texel probe") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            probe.transform.localScale = Vector3.one * bakeScale;
            probe.AddComponent<MeshFilter>().sharedMesh = mesh;
            var pr = probe.AddComponent<MeshRenderer>(); pr.sharedMaterial = pixel;
            float measured = PixelRestyle.TexelsPerUnit(pr);
            if (measured <= 0f || measured < MapTexelsPerUnit * 1.05f) return;
            float k = MapTexelsPerUnit / measured;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !m.HasProperty("_BaseMap")) continue;
                m.SetTextureScale("_BaseMap", m.GetTextureScale("_BaseMap") * k);
                EditorUtility.SetDirty(m);
            }
        }
        finally { Object.DestroyImmediate(probe); }
    }

    // maxBakes: new bakes this call (each takes a moment); the rest is done by the next call.
    public static string PixelProps(int maxBakes = 400)
    {
        Directory.CreateDirectory(PixelDir);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        int swapped = 0, baked = 0, left = 0;
        try
        {
            var map = root.GetComponent<CampaignMap>();
            var ground = root.transform.Find("Ground");
            var tinted = new Dictionary<(Material, Color32), Material>();
            var fitted = new HashSet<string>();
            // One mesh per source (model and materials): every size step unwraps it the same way (a fixed pack
            // margin), so only the texture differs. The first bake found is the one all steps use.
            var shared = new Dictionary<string, Mesh>();
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { PixelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(Path.GetDirectoryName(path)), @"^(.*) s-?\d+ ([0-9a-f]{6})$");
                if (m.Success && !shared.ContainsKey(m.Groups[1].Value + " " + m.Groups[2].Value)) shared[m.Groups[1].Value + " " + m.Groups[2].Value] = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || renderer.sharedMaterial == null) continue;
                if (ground != null && filter.transform.IsChildOf(ground)) continue;
                if (filter.GetComponentInParent<CampaignPath>() != null || filter.name == "Base" || filter.transform.parent != null && filter.transform.parent.name == "Base") continue;
                var mat = renderer.sharedMaterial;
                if (mat.name.Contains("Glass") || mat.name.Contains("Glow") || filter.sharedMesh.name.Contains("Glow")) continue;
                // Already a pixel copy (a second run, or ArenaSetup's after Build): left as it is.
                if (filter.sharedMesh.name.EndsWith(" (Synty Pixel)") || AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(PixelDir + "/")) continue;
                // The size step this copy stands at.
                float scale = Mathf.Abs(filter.transform.lossyScale.x);
                int step = Mathf.RoundToInt(Mathf.Log(Mathf.Max(.001f, scale)) / Mathf.Log(SizeStep));
                float bakeScale = Mathf.Pow(SizeStep, step);
                // Keyed by the mesh asset and its materials too (Synty reuses mesh names across packs and atlases).
                string sourceKey = SourceKey(filter.sharedMesh, renderer.sharedMaterials);
                string source = $"{filter.sharedMesh.name} {sourceKey}";
                string name = $"{filter.sharedMesh.name} s{step} {sourceKey}";
                string dir = $"{PixelDir}/{name}";
                shared.TryGetValue(source, out var canon);
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{name}.asset");
                var pixel = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{name}.mat");
                if (mesh == null && pixel != null && canon != null) mesh = canon;   // this step's own copy was shared away
                if (mesh == null || pixel == null)
                {
                    if (baked >= maxBakes) { left++; continue; }
                    // Baked from untinted copies of its materials (the tint goes on the pixel material after).
                    var plain = System.Array.ConvertAll(renderer.sharedMaterials, m => { if (m == null) return null; var c = new Material(m); if (c.HasProperty("_BaseColor")) c.SetColor("_BaseColor", Color.white); return c; });
                    // The restyle's density is a first guess: bake, measure at the size it stands at, bake again aimed by
                    // what came out (as the spoils' ores do), until it is within 5%.
                    var probe = new GameObject("Texel probe") { hideFlags = HideFlags.HideAndDontSave };
                    probe.transform.localScale = Vector3.one * bakeScale;
                    var pf = probe.AddComponent<MeshFilter>(); var pr = probe.AddComponent<MeshRenderer>();
                    int oldMin = PixelRestyle.MinTextureSize;
                    PixelRestyle.MinTextureSize = 4;
                    try
                    {
                        float aim = MapTexelsPerUnit * bakeScale;
                        for (int pass = 0; pass < 4; pass++)
                        {
                            (mesh, pixel) = PixelRestyle.Build(filter.sharedMesh, plain, "Props", name, aim, dir);
                            if (mesh == null) break;
                            pf.sharedMesh = mesh; pr.sharedMaterial = pixel;
                            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(pixel.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                            float measured = PixelRestyle.TexelsPerUnit(pr);
                            if (measured <= 0f || Mathf.Abs(measured / MapTexelsPerUnit - 1f) < .05f) break;
                            aim *= MapTexelsPerUnit / measured;
                        }
                    }
                    finally
                    {
                        PixelRestyle.MinTextureSize = oldMin;
                        Object.DestroyImmediate(probe);
                        foreach (var c in plain) if (c != null) Object.DestroyImmediate(c);
                    }
                    baked++;
                    if (mesh == null) continue;
                }
                // A step's own copy the same as the shared mesh is dropped for it.
                if (canon == null) shared[source] = mesh;
                else if (mesh != canon && SameUnwrap(mesh, canon)) { AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(mesh)); mesh = canon; }
                filter.sharedMesh = mesh;
                if (fitted.Add(dir)) FitSmall(mesh, pixel, dir, bakeScale);
                // A tinted original (the builder's land colours) keeps its tint on the pixel copy.
                var tint = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                var key = (pixel, (Color32)tint);
                if (!tinted.TryGetValue(key, out var use))
                {
                    if (((Color32)tint).Equals((Color32)Color.white)) use = pixel;
                    else
                    {
                        string tpath = $"{dir}/{name} tint {key.Item2.r:X2}{key.Item2.g:X2}{key.Item2.b:X2}.mat";
                        use = AssetDatabase.LoadAssetAtPath<Material>(tpath);
                        if (use == null) { use = new Material(pixel); use.SetColor("_BaseColor", tint); AssetDatabase.CreateAsset(use, tpath); }
                    }
                    tinted[key] = use;
                }
                renderer.sharedMaterials = new[] { use };
                swapped++;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return $"pixel props: {swapped} swapped, {baked} baked, {left} waiting for the next run";
    }
}

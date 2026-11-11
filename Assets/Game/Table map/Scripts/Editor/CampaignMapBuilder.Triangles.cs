using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Fewer triangles on the map without changing how it looks (the user, 2026-11-10: the map was most of the room's cost).
// The models on the map are a few centimetres tall and are drawn four times a frame (twice for the picture, once into
// each of two shadow maps), and the bought models they are copies of were made to be seen from a metre away.
//
// Run from an eval: CampaignMapBuilder.ReduceTriangles() (about three minutes). It
//   1. writes the map's pixel-copy meshes and the ground to Library/MapReduce,
//   2. runs Tools/map_mesh_reduce.py over them (hidden triangles go; edges are collapsed only where neither the outline,
//      the light on a face nor a texel moves: the rules are at the top of that file),
//   3. draws every result beside its original from eight sides at twice the closest a player gets,
//   4. lets the script take a result only if it looks the same in those pictures,
//   5. writes the taken meshes back into their own assets (same file, same GUID: no prefab, material or texture changes).
// A model done once is listed in Map/Reduced meshes.txt with its triangle count and is not done again while it has that
// count (each pass may lean a face a little further, so passes must not pile up); a model baked anew is done again.
// Pieces of a place are left as they are (they are seen closest and the hover outline is built from them), and so are
// small models with few copies.
//
// The ground is reduced only from its full height grid: a tool that changes the heights calls RebuildTerrainMesh first
// (CryptEntrance.Build does) and this again after.
public static partial class CampaignMapBuilder
{
    const string ReduceDir = "Library/MapReduce";   // not Temp, which Unity empties on every start
    const float ReducePixelsPerUnit = 70f;          // twice what a player standing at the table's edge sees (about 35 px a unit)
    const string ReducedList = MapDir + "/Reduced meshes.txt";   // guid|triangles|name of every model already done

    static Dictionary<string, (int triangles, string name)> ReadReducedList()
    {
        var done = new Dictionary<string, (int, string)>();
        if (!File.Exists(ReducedList)) return done;
        foreach (var line in File.ReadAllLines(ReducedList))
        {
            var c = line.Split('|');
            if (c.Length >= 3 && int.TryParse(c[1], out int t)) done[c[0]] = (t, c[2]);
        }
        return done;
    }

    class ReduceEntry { public Mesh mesh; public string file, kind; public float scale; public int copies; public bool place; public int texels; public Material[] materials; public readonly HashSet<string> ups = new(); }

    public static string ReduceTriangles(bool apply = true)
    {
        if (EditorApplication.isPlaying) return "stop Play first";
        var entries = ExportForReduce();
        string log = RunReducer("reduce");
        if (!File.Exists($"{ReduceDir}/reduce.txt")) return "the reducer wrote nothing:\n" + log;
        RenderReduced(entries);
        log = RunReducer("choose");
        if (!File.Exists($"{ReduceDir}/report.txt")) return "the reducer chose nothing:\n" + log;
        return (apply ? ApplyReduced(entries) : "not applied") + "; " + log.Trim() + $" (per mesh: {ReduceDir}/report.txt)";
    }

    // The map's meshes as raw arrays, with what the reducer needs to know of each: the largest size it stands at, how
    // many copies there are, whether it is part of a place, its texture's width, and which way is up for its copies.
    static List<ReduceEntry> ExportForReduce()
    {
        if (Directory.Exists(ReduceDir)) Directory.Delete(ReduceDir, true);
        Directory.CreateDirectory($"{ReduceDir}/mesh");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var map = prefab.GetComponent<CampaignMap>();
        var found = new Dictionary<Mesh, ReduceEntry>(); var entries = new List<ReduceEntry>();
        var done = ReadReducedList();
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh; var renderer = filter.GetComponent<MeshRenderer>();
            if (mesh == null || renderer == null || mesh.subMeshCount != 1) continue;
            bool ground = map != null && map.terrain == filter;
            // The ground only as the full height grid: reduced once already, a second pass would drift from the heights.
            if (ground && mesh.vertexCount != (map.gridX + 1) * (map.gridZ + 1)) continue;
            if (!ground && !AssetDatabase.GetAssetPath(mesh).StartsWith(PixelDir + "/")) continue;
            if (!ground && done.TryGetValue(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh)), out var was) && was.triangles == (int)mesh.GetIndexCount(0) / 3) continue;
            if (!found.TryGetValue(mesh, out var e))
            {
                found[mesh] = e = new ReduceEntry { mesh = mesh, file = $"m{entries.Count:000}.bin", kind = ground ? "ground" : "prop", materials = renderer.sharedMaterials };
                entries.Add(e);
            }
            var s = filter.transform.lossyScale; float scale = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            if (scale > e.scale) { e.scale = scale; e.materials = renderer.sharedMaterials; }
            e.copies++;
            e.place |= filter.GetComponentInParent<CampaignPoint>(true) != null;
            foreach (var m in renderer.sharedMaterials)
            {
                var t = m != null && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                if (t != null) e.texels = Mathf.Max(e.texels, t.width, t.height);
            }
            var up = filter.transform.InverseTransformVector(Vector3.up).normalized;
            e.ups.Add(System.FormattableString.Invariant($"{up.x:0.0},{up.y:0.0},{up.z:0.0}"));
        }
        var index = new StringBuilder();
        foreach (var e in entries)
        {
            var mesh = e.mesh; var v = mesh.vertices; var n = mesh.normals; var uv = mesh.uv; var col = mesh.colors32; var tri = mesh.triangles;
            using (var w = new BinaryWriter(File.Create($"{ReduceDir}/mesh/{e.file}")))
            {
                w.Write(v.Length); w.Write(tri.Length); w.Write(col.Length == v.Length ? 1 : 0);
                foreach (var p in v) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
                foreach (var p in n) { w.Write(p.x); w.Write(p.y); w.Write(p.z); }
                foreach (var p in uv) { w.Write(p.x); w.Write(p.y); }
                if (col.Length == v.Length) foreach (var c in col) { w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a); }
                foreach (int i in tri) w.Write(i);
            }
            index.Append(System.FormattableString.Invariant($"{e.file}|{e.kind}|{mesh.name}|{e.scale:0.####}|{tri.Length / 3}|{e.copies}|{(e.place ? 1 : 0)}|{Mathf.Max(e.texels, 16)}|{string.Join(";", e.ups)}\n"));
        }
        File.WriteAllText($"{ReduceDir}/index.txt", index.ToString());
        return entries;
    }

    static string RunReducer(string command)
    {
        var p = new System.Diagnostics.Process();
        p.StartInfo.FileName = "python";
        p.StartInfo.Arguments = $"\"{Path.GetFullPath("Tools/map_mesh_reduce.py")}\" {command} \"{Path.GetFullPath(ReduceDir)}\"";
        p.StartInfo.UseShellExecute = false; p.StartInfo.RedirectStandardOutput = true; p.StartInfo.RedirectStandardError = true; p.StartInfo.CreateNoWindow = true;
        p.Start();
        var errors = p.StandardError.ReadToEndAsync();   // both streams read at once, so neither can fill and stall it
        string log = p.StandardOutput.ReadToEnd() + errors.Result;
        p.WaitForExit();
        File.WriteAllText($"{ReduceDir}/{command}.log", log);
        return log;
    }

    static Mesh ReadReduced(string path, Mesh into = null)
    {
        using var r = new BinaryReader(File.OpenRead(path));
        int nv = r.ReadInt32(), ni = r.ReadInt32(); bool colours = r.ReadInt32() == 1;
        var v = new Vector3[nv]; var n = new Vector3[nv]; var uv = new Vector2[nv]; var col = new Color32[colours ? nv : 0]; var tri = new int[ni];
        for (int i = 0; i < nv; i++) v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        for (int i = 0; i < nv; i++) n[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        for (int i = 0; i < nv; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        for (int i = 0; i < col.Length; i++) col[i] = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        for (int i = 0; i < ni; i++) tri[i] = r.ReadInt32();
        var mesh = into != null ? into : new Mesh();
        mesh.Clear();
        mesh.indexFormat = nv > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = v; mesh.normals = n; mesh.uv = uv; if (colours) mesh.colors32 = col; mesh.triangles = tri;
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }

    // Every tried model: as it is, then each try, from eight sides (rows) at twice the closest a player gets, on a
    // colour no model has. The script reads these to see whether a try shows.
    static void RenderReduced(List<ReduceEntry> entries)
    {
        Directory.CreateDirectory($"{ReduceDir}/sheets");
        var tries = new List<string>(); foreach (var d in new[] { "A", "B" }) if (Directory.Exists($"{ReduceDir}/{d}")) tries.Add(d);
        bool asyncShaders = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        var root = new GameObject("map reduce review") { hideFlags = HideFlags.DontSave };
        root.transform.position = new Vector3(0, -980, 0);
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(1f, 0f, 1f);
        cam.nearClipPlane = .01f; cam.aspect = 1f; cam.allowHDR = false; cam.allowMSAA = false;
        var light = new GameObject("light").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
        light.type = LightType.Directional; light.intensity = 1.1f; light.transform.rotation = Quaternion.Euler(55, 30, 0); light.shadows = LightShadows.None;
        var views = new (float pitch, float yaw)[] { (40f, 20f), (40f, 200f), (12f, 110f), (12f, 290f), (55f, 110f), (55f, 290f), (25f, 65f), (25f, 245f) };
        var sizes = new StringBuilder();
        try
        {
            foreach (var e in entries)
            {
                if (e.kind != "prop" || !File.Exists($"{ReduceDir}/{tries[0]}/{e.file}")) continue;
                var columns = new List<Mesh> { e.mesh };
                foreach (var d in tries) columns.Add(ReadReduced($"{ReduceDir}/{d}/{e.file}"));
                var go = new GameObject("model"); go.transform.SetParent(root.transform, false); go.transform.localScale = Vector3.one * e.scale;
                var filter = go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterials = e.materials;
                var centre = go.transform.TransformPoint(e.mesh.bounds.center); float radius = e.mesh.bounds.extents.magnitude * e.scale;
                int cell = Mathf.Clamp(Mathf.CeilToInt(radius * 2.1f * ReducePixelsPerUnit), 16, 1024);
                cam.orthographicSize = cell / ReducePixelsPerUnit * .5f; cam.farClipPlane = radius * 6 + 10;
                var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 }; cam.targetTexture = rt;
                var sheet = new Texture2D(cell * columns.Count, cell * views.Length, TextureFormat.RGB24, false);
                var px = new Texture2D(cell, cell, TextureFormat.RGB24, false);
                for (int c = 0; c < columns.Count; c++)
                {
                    filter.sharedMesh = columns[c];
                    for (int v = 0; v < views.Length; v++)
                    {
                        var dir = Quaternion.Euler(views[v].pitch, views[v].yaw, 0) * Vector3.forward;
                        camGo.transform.SetPositionAndRotation(centre - dir * (radius * 3 + 2), Quaternion.LookRotation(dir));
                        cam.Render();
                        RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, cell, cell), 0, 0); px.Apply();
                        sheet.SetPixels(c * cell, (views.Length - 1 - v) * cell, cell, cell, px.GetPixels());
                    }
                }
                sheet.Apply();
                File.WriteAllBytes($"{ReduceDir}/sheets/{e.file}.png", sheet.EncodeToPNG());
                sizes.Append($"{e.file}|{cell}|{columns.Count}|{views.Length}\n");
                RenderTexture.active = null; cam.targetTexture = null;
                Object.DestroyImmediate(rt); Object.DestroyImmediate(px); Object.DestroyImmediate(sheet); Object.DestroyImmediate(go);
                for (int c = 1; c < columns.Count; c++) Object.DestroyImmediate(columns[c]);
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
            ShaderUtil.allowAsyncCompilation = asyncShaders;
        }
        File.WriteAllText($"{ReduceDir}/sheets/sizes.txt", sizes.ToString());
    }

    // The taken meshes go back into their own assets, so everything that points at them keeps doing so.
    static string ApplyReduced(List<ReduceEntry> entries)
    {
        int changed = 0; long before = 0, after = 0;
        var done = ReadReducedList();
        foreach (var e in entries)
        {
            string path = $"{ReduceDir}/final/{e.file}";
            if (!File.Exists(path)) continue;
            before += e.mesh.triangles.Length / 3;
            ReadReduced(path, e.mesh);
            after += e.mesh.triangles.Length / 3;
            EditorUtility.SetDirty(e.mesh);
            if (e.kind == "prop") done[AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.mesh))] = (e.mesh.triangles.Length / 3, e.mesh.name);
            changed++;
        }
        AssetDatabase.SaveAssets();
        var lines = new List<string>();
        foreach (var kv in done) lines.Add($"{kv.Key}|{kv.Value.triangles}|{kv.Value.name}");
        lines.Sort((a, b) => string.CompareOrdinal(a.Substring(a.LastIndexOf('|')), b.Substring(b.LastIndexOf('|'))));
        File.WriteAllText(ReducedList, string.Join("\n", lines) + "\n");
        AssetDatabase.ImportAsset(ReducedList);
        return $"{changed} meshes written: {before} -> {after} triangles";
    }

    // The ground as its full height grid again (as Generate lays it out), for a tool that has changed the heights.
    // ReduceTriangles afterwards makes it light again.
    public static void RebuildTerrainMesh(CampaignMap map, float[] heights)
    {
        int w = map.gridX, h = map.gridZ;
        var verts = new Vector3[(w + 1) * (h + 1)]; var uvs = new Vector2[verts.Length];
        for (int z = 0; z <= h; z++)
            for (int x = 0; x <= w; x++)
            {
                int i = z * (w + 1) + x;
                verts[i] = new Vector3((x / (float)w * 2 - 1) * map.halfX, heights[i], (z / (float)h * 2 - 1) * map.halfZ);
                uvs[i] = new Vector2(x / (float)w, z / (float)h);
            }
        var tris = new int[w * h * 6]; int t = 0;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int q = z * (w + 1) + x;
                tris[t++] = q; tris[t++] = q + w + 1; tris[t++] = q + 1; tris[t++] = q + 1; tris[t++] = q + w + 1; tris[t++] = q + w + 2;
            }
        var mesh = map.terrain.sharedMesh;
        mesh.Clear();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts; mesh.uv = uvs; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
    }
}

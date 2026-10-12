using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Painted restyles for flat props (swords, knives, maces, shields): the texture is drawn like the approved creature
// concepts (Docs/Pixel-texture-style.md) instead of from noise. A flat render of the model, seen straight on from
// the front and the back, is repainted by the concept image model in the house style; the painting is projected
// back onto the model and sampled at a fixed density in the world, so every prop has the same pixel size.
//   1. RenderReferences: Temp/Paint/<name> front.png / back.png (the model, flat lit, on a plain background).
//   2. Repaint those images (Tools/meshy.py concept --ref ...) into Temp/Paint/<name> front painted.png / back painted.png.
//   3. BuildPainted: projects the paintings onto fresh UVs at ItemTexelsPerUnit and swaps the item prefab to it.
public static partial class PixelRestyle
{
    public const string PaintFolder = "Temp/Paint";
    static readonly Color PaintBackground = new(.5f, .5f, .5f);

    // The model's thinnest axis is the one it is seen along (a blade's flat, a shield's face).
    static int ThinAxis(Bounds b) { var s = b.size; return s.x <= s.y && s.x <= s.z ? 0 : s.y <= s.z ? 1 : 2; }

    public static string RenderReferences(string syntyPrefabPath, string name, int size = 1024)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(syntyPrefabPath);
        var mf = prefab.GetComponentInChildren<MeshFilter>(true);
        var mesh = mf.sharedMesh; var b = mesh.bounds; int axis = ThinAxis(b);
        Directory.CreateDirectory(PaintFolder);
        var root = new GameObject("paint ref") { hideFlags = HideFlags.DontSave };
        root.transform.position = new Vector3(0, -900, 0);
        var model = new GameObject("model"); model.transform.SetParent(root.transform, false);
        model.AddComponent<MeshFilter>().sharedMesh = mesh;
        model.AddComponent<MeshRenderer>().sharedMaterials = mf.GetComponent<MeshRenderer>().sharedMaterials;
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = PaintBackground;
        cam.nearClipPlane = .01f; cam.farClipPlane = 50f;
        var light = new GameObject("light").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
        light.type = LightType.Directional; light.intensity = 1.1f;
        int a = (axis + 1) % 3, c = (axis + 2) % 3; if (b.size[a] > b.size[c]) (a, c) = (c, a);
        Vector3 along = Vector3.zero; along[axis] = 1f; Vector3 up = Vector3.zero; up[c] = 1f; Vector3 right = Vector3.zero; right[a] = 1f;
        float half = Mathf.Max(b.extents[a], b.extents[c]) * 1.04f;
        cam.orthographicSize = half; cam.aspect = 1f;
        var rt = new RenderTexture(size, size, 24); cam.targetTexture = rt;
        foreach (var back in new[] { false, true })
        {
            Vector3 eye = root.transform.TransformPoint(b.center + (back ? -along : along) * (b.size.magnitude + 1f));
            camGo.transform.position = eye;
            camGo.transform.rotation = Quaternion.LookRotation(root.transform.TransformDirection(back ? along : -along), root.transform.TransformDirection(up));
            light.transform.rotation = Quaternion.LookRotation(camGo.transform.forward + camGo.transform.up * -.3f + camGo.transform.right * .3f);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
            File.WriteAllBytes($"{PaintFolder}/{name} {(back ? "back" : "front")}.png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
        RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(root);
        return $"{name}: axis {axis}, rendered front and back";
    }

    // The painted image's object box (pixels that differ from the background), so the painting lines up with the
    // model even if the painter moved it a little.
    static RectInt ObjectBox(Texture2D tex, out Color bg)
    {
        var px = tex.GetPixels(); int w = tex.width, h = tex.height;
        bg = (px[0] + px[w - 1] + px[(h - 1) * w] + px[h * w - 1]) / 4f;
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            var p = px[y * w + x];
            if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) < .12f) continue;
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        return x1 < 0 ? new RectInt(0, 0, w, h) : new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    // Same, logging the result or the error to Temp/Paint/build.log (for long runs started from the command line).
    public static string BuildPaintedLogged(string itemDataPath, string syntyPrefabPath, string name)
    {
        string line;
        try { line = BuildPainted(itemDataPath, syntyPrefabPath, name); }
        catch (System.Exception e) { line = name + ": " + e; }
        File.AppendAllText($"{PaintFolder}/build.log", System.DateTime.Now.ToString("HH:mm:ss ") + line + System.Environment.NewLine);
        return line;
    }

    // Projects the paintings onto the model at the item density and swaps the item's prefab to the result.
    public static string BuildPainted(string itemDataPath, string syntyPrefabPath, string name)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(itemDataPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(syntyPrefabPath);
        var smf = prefab.GetComponentInChildren<MeshFilter>(true);
        var src = smf.sharedMesh; var b = src.bounds; int axis = ThinAxis(b);
        Texture2D Load(string side, out RectInt box, out RectInt refBox)
        {
            var bytes = File.ReadAllBytes($"{PaintFolder}/{name} {side} painted.png");
            var t = new Texture2D(2, 2); t.LoadImage(bytes); t.filterMode = FilterMode.Point;
            box = ObjectBox(t, out _);
            var r = new Texture2D(2, 2); r.LoadImage(File.ReadAllBytes($"{PaintFolder}/{name} {side}.png")); refBox = ObjectBox(r, out _); Object.DestroyImmediate(r);
            return t;
        }
        var front = Load("front", out var fBox, out var fRef);
        RectInt bBox = fBox, bRef = fRef;
        Texture2D back = null;
        if (File.Exists($"{PaintFolder}/{name} back painted.png")) back = Load("back", out bBox, out bRef);
        int size = front.width;
        float half = Mathf.Max(b.extents[(axis + 1) % 3], b.extents[(axis + 2) % 3]) * 1.04f;
        Color Paint(Vector3 p, Vector3 n)
        {
            bool fromBack = n[axis] < 0f && back != null;
            var tex = fromBack ? back : front; var box = fromBack ? bBox : fBox; var rbox = fromBack ? bRef : fRef;
            // Where the point is in the reference render (pixels), then the same place in the painting's box.
            int a = (axis + 1) % 3, c = (axis + 2) % 3; if (b.size[a] > b.size[c]) (a, c) = (c, a);
            float rx = (p[a] - b.center[a]) / (half * 2f) * size + size * .5f, ry = (p[c] - b.center[c]) / (half * 2f) * size + size * .5f;
            if (n[axis] < 0f) rx = size - rx;
            float u = Mathf.InverseLerp(rbox.xMin, rbox.xMax, rx), v = Mathf.InverseLerp(rbox.yMin, rbox.yMax, ry);
            int x = Mathf.Clamp(Mathf.RoundToInt(box.xMin + u * box.width), 0, tex.width - 1), y = Mathf.Clamp(Mathf.RoundToInt(box.yMin + v * box.height), 0, tex.height - 1);
            return tex.GetPixel(x, y);
        }
        var go = item.worldPrefab; string prefabPath = AssetDatabase.GetAssetPath(go);
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        string result;
        try
        {
            var mf = root.GetComponentInChildren<MeshFilter>(true);
            float scale = Mathf.Max(.01f, mf.transform.lossyScale.x);
            PaintSource = Paint;
            // A fresh mesh asset each time: copying over an existing one kept its old UVs (the new texture then landed
            // on the old layout, scrambled).
            string category = item.itemType == ItemType.Shield ? "Shields" : "Weapons";
            // Bake, measure the pixel size it really has (median over its faces, in the world), and bake again aimed
            // off by the error until it lands within 5% of ItemTexelsPerUnit: the unwrap gives parts unequal space.
            float aim = ItemTexelsPerUnit * scale, measured = 0f;
            (Mesh mesh, Material material) built = default;
            for (int pass = 0; pass < 4; pass++)
            {
                AssetDatabase.DeleteAsset($"{Root}/{category}/{src.name}/{name} painted.asset");
                built = Build(src, smf.GetComponent<MeshRenderer>().sharedMaterials, category, name + " painted", aim);
                if (built.mesh == null) break;
                mf.sharedMesh = built.mesh; mf.GetComponent<MeshRenderer>().sharedMaterials = new[] { built.material };
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(built.material.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                measured = TexelsPerUnit(mf.GetComponent<MeshRenderer>());
                var tx = built.material.GetTexture("_BaseMap");
                File.AppendAllText($"{PaintFolder}/build.log", $"   pass {pass}: aim {aim:0.#} tex {(tx != null ? tx.width : 0)} measured {measured:0.#} mean {MeanTexelsPerUnit(mf.GetComponent<MeshRenderer>()):0.#}" + System.Environment.NewLine);
                if (measured <= 0f || Mathf.Abs(measured / ItemTexelsPerUnit - 1f) < .05f) break;
                aim *= ItemTexelsPerUnit / measured;
            }
            PaintSource = null;
            if (built.mesh == null) return name + ": build failed";
            mf.sharedMesh = built.mesh; mf.GetComponent<MeshRenderer>().sharedMaterials = new[] { built.material };
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            // The mesh asset was replaced at the same path: reimport it and the prefab, or the prefab keeps a stale (missing) reference.
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(built.mesh), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            result = $"{name}: {measured:0} texels per metre (target {ItemTexelsPerUnit:0})";
        }
        finally { PaintSource = null; PrefabUtility.UnloadPrefabContents(root); }
        Object.DestroyImmediate(front); if (back != null) Object.DestroyImmediate(back);
        return result;
    }

    // Area-weighted texels per world unit (the whole surface, not the median face).
    public static float MeanTexelsPerUnit(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>(); var mesh = mf != null ? mf.sharedMesh : null; var tex = r.sharedMaterial != null ? r.sharedMaterial.GetTexture("_BaseMap") : null;
        if (mesh == null || tex == null) return 0f;
        var v = mesh.vertices; var uv = mesh.uv; var t = mesh.triangles; var m = r.transform.localToWorldMatrix;
        double world = 0, uvA = 0;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
            world += Vector3.Cross(b - a, c - a).magnitude * .5f;
            uvA += Mathf.Abs((uv[t[i + 1]].x - uv[t[i]].x) * (uv[t[i + 2]].y - uv[t[i]].y) - (uv[t[i + 2]].x - uv[t[i]].x) * (uv[t[i + 1]].y - uv[t[i]].y)) * .5f;
        }
        return world > 0 ? (float)System.Math.Sqrt(uvA * tex.width * tex.height / world) : 0f;
    }

    // While set, Build takes each texel's colour from here (model-space position and normal) instead of Synty's atlas.
    static System.Func<Vector3, Vector3, Color> PaintSource;
}

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Painted restyles for round and boxy props (barrels, crates, pots, chests, torches), the multi-view sibling of the
// flat-prop painting in PixelRestyle.Painted.cs. Every part of the prop is rendered from five sides onto one sheet,
// the whole sheet is repainted by the concept image model in one go (so the sides match each other), and each
// painted side is projected back onto the faces that point its way and can see it.
//   1. RenderPropSheet: Temp/Paint/<name> sheet.png (front, right, back / left, top, in a 3 x 2 grid of square cells).
//   2. Repaint it (Tools/meshy.py concept --aspect 16:9 --ref ...) into Temp/Paint/<name> sheet painted.png.
//   3. BuildPaintedProp: projects the painting onto fresh UVs at ItemTexelsPerUnit on every part and saves the prop prefab.
public static partial class PixelRestyle
{
    const int SheetWidth = 1536, SheetHeight = 864, SheetCell = 432;

    // The five views, in sheet order: the direction the camera looks from (prop space) and the camera's up.
    static readonly (string name, Vector3 from, Vector3 up)[] PropViews =
    {
        ("front", Vector3.forward, Vector3.up), ("right", Vector3.right, Vector3.up), ("back", Vector3.back, Vector3.up),
        ("left", Vector3.left, Vector3.up), ("top", Vector3.up, Vector3.forward),
    };

    static RectInt SheetCellRect(int view, int width, int height)
    {
        // Three square cells across the middle of the sheet, two rows.
        float sx = width / (float)SheetWidth, sy = height / (float)SheetHeight;
        int left = (SheetWidth - SheetCell * 3) / 2;
        int col = view % 3, row = view / 3;
        return new RectInt(Mathf.RoundToInt((left + col * SheetCell) * sx), Mathf.RoundToInt((SheetHeight - (row + 1) * SheetCell) * sy),
                           Mathf.RoundToInt(SheetCell * sx), Mathf.RoundToInt(SheetCell * sy));
    }

    // The prop's paintable parts (no particles; water, glass and glows stay as they are) and their bounds in prop space.
    static List<MeshRenderer> PropParts(GameObject root, out Bounds bounds)
    {
        var parts = new List<MeshRenderer>(); bounds = default; bool any = false;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            if (System.Array.TrueForAll(r.sharedMaterials, SkipSurface)) continue;
            parts.Add(r);
            var m = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix; var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!any) { bounds = new Bounds(c, Vector3.zero); any = true; } else bounds.Encapsulate(c);
            }
        }
        return parts;
    }

    // Half the side of every view's square: one scale for all five, so a texel is the same size in each.
    static float SheetHalf(Bounds b) => Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.08f;

    public static string RenderPropSheet(string propPrefabPath, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(propPrefabPath);
        Directory.CreateDirectory(PaintFolder);
        var root = new GameObject("paint sheet") { hideFlags = HideFlags.DontSave };
        root.transform.position = new Vector3(0, -900, 0);
        var copy = (GameObject)Object.Instantiate(prefab, root.transform);
        copy.transform.localPosition = Vector3.zero; copy.transform.localRotation = Quaternion.identity;
        foreach (var ps in copy.GetComponentsInChildren<ParticleSystemRenderer>(true)) ps.enabled = false;
        foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
        var parts = PropParts(copy, out var b);
        // Only the painted parts are shown (the fountain's water would hide its basin from the painter).
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) if (!(r is MeshRenderer mr && parts.Contains(mr))) r.enabled = false;
        // Rendered in Synty's own flat colours: an earlier restyle's coarse texels would be copied by the painter as big squares.
        foreach (var r in parts)
        {
            var mf = r.GetComponent<MeshFilter>();
            string baseName = mf.sharedMesh.name.Replace(" (Synty Pixel)", "").Replace(" painted", "");
            foreach (var g in AssetDatabase.FindAssets(baseName + " t:Prefab", new[] { "Assets/Synty" }))
            {
                var synty = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (synty == null || synty.name != baseName) continue;
                var smf = synty.GetComponentInChildren<MeshFilter>(true); var smr = smf != null ? smf.GetComponent<MeshRenderer>() : null;
                if (smf == null || smr == null) continue;
                mf.sharedMesh = smf.sharedMesh; r.sharedMaterials = smr.sharedMaterials;
                break;
            }
        }
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = PaintBackground;
        cam.nearClipPlane = .01f; cam.farClipPlane = 50f; cam.aspect = 1f;
        float half = SheetHalf(b); cam.orthographicSize = half;
        var light = new GameObject("light").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
        light.type = LightType.Directional; light.intensity = 1.1f;
        var rt = new RenderTexture(SheetCell, SheetCell, 24); cam.targetTexture = rt;
        var sheet = new Texture2D(SheetWidth, SheetHeight, TextureFormat.RGB24, false);
        var bg = new Color[SheetWidth * SheetHeight]; for (int i = 0; i < bg.Length; i++) bg[i] = PaintBackground; sheet.SetPixels(bg);
        var cell = new Texture2D(SheetCell, SheetCell, TextureFormat.RGB24, false);
        for (int v = 0; v < PropViews.Length; v++)
        {
            var (_, from, up) = PropViews[v];
            var centre = copy.transform.TransformPoint(b.center);
            camGo.transform.position = centre + copy.transform.TransformDirection(from) * (b.size.magnitude + 1f);
            camGo.transform.rotation = Quaternion.LookRotation(-copy.transform.TransformDirection(from), copy.transform.TransformDirection(up));
            light.transform.rotation = Quaternion.LookRotation(camGo.transform.forward + camGo.transform.up * -.3f + camGo.transform.right * .3f);
            cam.Render();
            RenderTexture.active = rt; cell.ReadPixels(new Rect(0, 0, SheetCell, SheetCell), 0, 0); cell.Apply();
            var rect = SheetCellRect(v, SheetWidth, SheetHeight);
            sheet.SetPixels(rect.x, rect.y, SheetCell, SheetCell, cell.GetPixels());
        }
        sheet.Apply();
        File.WriteAllBytes($"{PaintFolder}/{name} sheet.png", sheet.EncodeToPNG());
        RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(cell); Object.DestroyImmediate(sheet); Object.DestroyImmediate(root);
        return $"{name}: {parts.Count} parts, sheet rendered";
    }

    // Same, logging to Temp/Paint/build.log.
    public static string BuildPaintedPropLogged(string propPrefabPath, string name)
    {
        string line;
        try { line = BuildPaintedProp(propPrefabPath, name); }
        catch (System.Exception e) { line = name + ": " + e; }
        File.AppendAllText($"{PaintFolder}/build.log", System.DateTime.Now.ToString("HH:mm:ss ") + line + System.Environment.NewLine);
        return line;
    }

    // Projects the painted sheet onto every part of the prop at the item density and saves the prefab.
    public static string BuildPaintedProp(string propPrefabPath, string name)
    {
        var painted = new Texture2D(2, 2); painted.LoadImage(File.ReadAllBytes($"{PaintFolder}/{name} sheet painted.png")); painted.filterMode = FilterMode.Point;
        var reference = new Texture2D(2, 2); reference.LoadImage(File.ReadAllBytes($"{PaintFolder}/{name} sheet.png"));
        // Each view's object box in the render and in the painting, so the painting lines up even if the painter moved
        // a view a little inside its cell.
        var refBox = new RectInt[PropViews.Length]; var paintBox = new RectInt[PropViews.Length];
        for (int v = 0; v < PropViews.Length; v++)
        {
            refBox[v] = CellObjectBox(reference, SheetCellRect(v, reference.width, reference.height));
            paintBox[v] = CellObjectBox(painted, SheetCellRect(v, painted.width, painted.height));
        }
        Object.DestroyImmediate(reference);
        // Where the painting's outline is a little inside the model's, a sample would land on the grey background:
        // every background pixel points at its nearest painted pixel instead.
        var nearest = NearestPainted(painted);

        var root = PrefabUtility.LoadPrefabContents(propPrefabPath);
        // Occlusion probe: every part as a collider, far from the scene, so a face hidden from a view (behind the lid,
        // under the bracket) takes its colour from another view that sees it.
        var probe = new GameObject("PixelRestyle prop probe") { hideFlags = HideFlags.HideAndDontSave, layer = ProbeLayer };
        probe.transform.position = new Vector3(0, -12000, 0);
        var results = new List<string>();
        try
        {
            var parts = PropParts(root, out var b);
            float half = SheetHalf(b);
            var toProp = new Dictionary<MeshRenderer, Matrix4x4>();
            foreach (var r in parts)
            {
                var m = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix; toProp[r] = m;
                var part = new GameObject(r.name) { hideFlags = HideFlags.HideAndDontSave, layer = ProbeLayer };
                part.transform.SetParent(probe.transform, false);
                part.transform.localPosition = m.GetColumn(3); part.transform.localRotation = m.rotation; part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshCollider>().sharedMesh = r.GetComponent<MeshFilter>().sharedMesh;
            }
            Physics.SyncTransforms();
            int mask = 1 << ProbeLayer;

            Color Sample(int v, Vector3 p)
            {
                var (_, from, up) = PropViews[v];
                var right = Vector3.Cross(up, -from); // the camera's right, as in RenderPropSheet (it looks along -from)
                var d = p - b.center;
                float rx = (Vector3.Dot(d, right) / (half * 2f) + .5f) * SheetCell, ry = (Vector3.Dot(d, up) / (half * 2f) + .5f) * SheetCell;
                var rc = SheetCellRect(v, SheetWidth, SheetHeight); rx += rc.x; ry += rc.y;
                float u = Mathf.InverseLerp(refBox[v].xMin, refBox[v].xMax, rx), w = Mathf.InverseLerp(refBox[v].yMin, refBox[v].yMax, ry);
                int x = Mathf.Clamp(Mathf.RoundToInt(paintBox[v].xMin + u * paintBox[v].width), 0, painted.width - 1);
                int y = Mathf.Clamp(Mathf.RoundToInt(paintBox[v].yMin + w * paintBox[v].height), 0, painted.height - 1);
                int k = nearest[y * painted.width + x];
                return painted.GetPixel(k % painted.width, k / painted.width);
            }

            // A part used more than once (a table's chairs) is painted once, where it first stands, and shared.
            var done = new Dictionary<string, (Mesh mesh, Material material)>();
            foreach (var r in parts)
            {
                var mf = r.GetComponent<MeshFilter>(); var m = toProp[r];
                string doneKey = mf.sharedMesh.name.Replace(" (Synty Pixel)", "").Replace(" painted", "");
                if (done.TryGetValue(doneKey, out var shared)) { mf.sharedMesh = shared.mesh; r.sharedMaterials = new[] { shared.material }; continue; }
                // Always from the plain restyle (a re-run must not start from the last painting's mesh and UVs).
                string baseName = mf.sharedMesh.name.Replace(" (Synty Pixel)", "").Replace(" painted", "");
                var src = AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Props/{baseName}/{baseName}.asset") ?? mf.sharedMesh;
                var srcMats = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Props/{baseName}/{baseName}.mat") is { } plain ? new[] { plain } : r.sharedMaterials;
                string partName = baseName + " painted";
                float scale = Mathf.Max(.01f, r.transform.lossyScale.x);
                // Colour from the view that faces this point most squarely and can see it.
                PaintSource = (p, n) =>
                {
                    var pp = m.MultiplyPoint3x4(p); var pn = m.MultiplyVector(n).normalized;
                    int best = -1; float bestDot = float.NegativeInfinity; int fallback = 0; float fallbackDot = float.NegativeInfinity;
                    for (int v = 0; v < PropViews.Length; v++)
                    {
                        float dot = Vector3.Dot(pn, PropViews[v].from);
                        if (dot > fallbackDot) { fallbackDot = dot; fallback = v; }
                        if (dot <= .05f || dot <= bestDot) continue;
                        var start = probe.transform.position + pp + pn * .004f;
                        if (Physics.Raycast(start, PropViews[v].from, 20f, mask, QueryTriggerInteraction.Collide)) continue;
                        best = v; bestDot = dot;
                    }
                    return Sample(best >= 0 ? best : fallback, pp);
                };
                float aim = ItemTexelsPerUnit * scale, measured = 0f;
                (Mesh mesh, Material material) built = default;
                for (int pass = 0; pass < 4; pass++)
                {
                    // A fresh mesh asset each pass: copying over an existing one keeps its old UVs.
                    AssetDatabase.DeleteAsset($"{Root}/Props/{src.name}/{partName}.asset");
                    built = Build(src, srcMats, "Props", partName, aim);
                    if (built.mesh == null) break;
                    mf.sharedMesh = built.mesh; r.sharedMaterials = new[] { built.material };
                    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(built.material.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                    measured = TexelsPerUnit(r);
                    if (measured <= 0f || Mathf.Abs(measured / ItemTexelsPerUnit - 1f) < .05f) break;
                    aim *= ItemTexelsPerUnit / measured;
                }
                PaintSource = null;
                if (built.mesh == null) { results.Add($"{src.name} failed"); continue; }
                done[doneKey] = built;
                results.Add($"{partName} {measured:0}/m");
            }
            PrefabUtility.SaveAsPrefabAsset(root, propPrefabPath);
            // The meshes were replaced at the same paths: reimport them and the prefab, or it keeps stale references.
            foreach (var r in parts) AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(propPrefabPath, ImportAssetOptions.ForceUpdate);
        }
        finally { PaintSource = null; Object.DestroyImmediate(probe); PrefabUtility.UnloadPrefabContents(root); Object.DestroyImmediate(painted); }
        return $"{name}: " + string.Join(", ", results) + $" (target {ItemTexelsPerUnit:0})";
    }

    // Long flat props (rugs, runners): no single painting reaches ItemTexelsPerUnit along their length, so one painted
    // section that repeats is laid on from above. The texture is the section across the prop's width (its x) at
    // ItemTexelsPerUnit, and repeats along its length (z).
    public static string BuildTiledTopProp(string propPrefabPath, string tilePng, string name)
    {
        var root = PrefabUtility.LoadPrefabContents(propPrefabPath);
        try
        {
            var mf = root.GetComponentInChildren<MeshFilter>(true); var r = mf.GetComponent<MeshRenderer>();
            string baseName = mf.sharedMesh.name.Replace(" (Synty Pixel)", "").Replace(" painted", "");
            var src = AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Props/{baseName}/{baseName}.asset") ?? mf.sharedMesh;
            string dir = $"{Root}/Props/{baseName}", partName = baseName + " painted";
            var tile = new Texture2D(2, 2); tile.LoadImage(File.ReadAllBytes(tilePng));
            var scale = mf.transform.lossyScale; var b = src.bounds;
            // The section spans the width; its length in the world follows from the pixel size.
            int tw = tile.width, th = tile.height;
            float tileWorld = th / ItemTexelsPerUnit, widthWorld = b.size.x * scale.x;
            var mesh = Object.Instantiate(src); mesh.name = partName;
            var v = mesh.vertices; var uv = new Vector2[v.Length];
            for (int i = 0; i < v.Length; i++) uv[i] = new Vector2((v[i].x - b.min.x) / b.size.x, (v[i].z - b.min.z) * scale.z / tileWorld);
            mesh.uv = uv; mesh.subMeshCount = 1; mesh.SetTriangles(src.triangles, 0);
            string texPath = $"{dir}/{partName}.png";
            File.WriteAllBytes(texPath, tile.EncodeToPNG()); Object.DestroyImmediate(tile);
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            ti.filterMode = FilterMode.Point; ti.wrapModeU = TextureWrapMode.Clamp; ti.wrapModeV = TextureWrapMode.Repeat; ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.BoxFilter; ti.SaveAndReimport();
            string meshPath = $"{dir}/{partName}.asset"; AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(mesh, meshPath);
            string matPath = $"{dir}/{partName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, matPath); }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath)); material.SetColor("_BaseColor", Color.white); material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            mf.sharedMesh = mesh; r.sharedMaterials = new[] { material };
            PrefabUtility.SaveAsPrefabAsset(root, propPrefabPath);
            AssetDatabase.SaveAssets();
            return $"{name}: {tw}x{th} section, {tw / widthWorld:0.#} across x {ItemTexelsPerUnit} along texels per metre, repeats every {tileWorld:0.##} m";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // A review image: each prop as it is now, seen from the front-right and above, all at one world scale, side by side.
    public static string RenderPropPreview(string[] prefabPaths, string outFile, float metresPerCell = 1.4f, int cell = 640)
    {
        // New materials would draw as Unity's cyan placeholder while their shaders compile in the background.
        bool asyncShaders = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        var root = new GameObject("prop preview") { hideFlags = HideFlags.DontSave };
        root.transform.position = new Vector3(0, -950, 0);
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .17f, .16f);
        cam.nearClipPlane = .01f; cam.farClipPlane = 60f; cam.aspect = 1f; cam.orthographicSize = metresPerCell * .5f;
        var light = new GameObject("light").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
        light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(40, -30, 0);
        var rt = new RenderTexture(cell, cell, 24); cam.targetTexture = rt;
        var sheet = new Texture2D(cell * prefabPaths.Length, cell, TextureFormat.RGB24, false);
        var px = new Texture2D(cell, cell, TextureFormat.RGB24, false);
        for (int i = 0; i < prefabPaths.Length; i++)
        {
            var copy = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]), root.transform);
            copy.transform.localPosition = Vector3.zero;
            foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
            PropParts(copy, out var b);
            var centre = copy.transform.TransformPoint(b.center);
            var dir = Quaternion.Euler(28, 145, 0) * Vector3.forward;
            camGo.transform.position = centre - dir * 20f; camGo.transform.rotation = Quaternion.LookRotation(dir);
            cam.Render();
            RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, cell, cell), 0, 0); px.Apply();
            sheet.SetPixels(i * cell, 0, cell, cell, px.GetPixels());
            Object.DestroyImmediate(copy);
        }
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
        File.WriteAllBytes(outFile, sheet.EncodeToPNG());
        RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(px); Object.DestroyImmediate(sheet); Object.DestroyImmediate(root);
        ShaderUtil.allowAsyncCompilation = asyncShaders;
        return outFile;
    }

    // For every pixel of the painted sheet, the index of the nearest pixel that is paint, not background (itself if it is
    // paint): a breadth-first fill outward from all painted pixels at once.
    static int[] NearestPainted(Texture2D tex)
    {
        int w = tex.width, h = tex.height; var px = tex.GetPixels();
        var bg = (px[0] + px[w - 1] + px[(h - 1) * w] + px[h * w - 1]) / 4f;
        var nearest = new int[w * h]; var queue = new Queue<int>();
        for (int i = 0; i < px.Length; i++)
        {
            var p = px[i];
            nearest[i] = Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) < .12f ? -1 : i;
        }
        // The painter draws a dark outline round each view; faces near a view's edge would pick it up (black stave
        // tips, striped hoop edges). Peel dark pixels off the outside, a few layers deep, so they count as background.
        for (int pass = 0; pass < 4; pass++)
        {
            var peel = new List<int>();
            for (int i = 0; i < px.Length; i++)
            {
                if (nearest[i] < 0 || px[i].grayscale > .16f) continue;
                int x = i % w, y = i / w;
                if ((x > 0 && nearest[i - 1] < 0) || (x < w - 1 && nearest[i + 1] < 0) || (y > 0 && nearest[i - w] < 0) || (y < h - 1 && nearest[i + w] < 0)) peel.Add(i);
            }
            foreach (int i in peel) nearest[i] = -1;
        }
        for (int i = 0; i < px.Length; i++) if (nearest[i] >= 0) queue.Enqueue(i);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            void Visit(int j) { if (nearest[j] < 0) { nearest[j] = nearest[i]; queue.Enqueue(j); } }
            if (x > 0) Visit(i - 1); if (x < w - 1) Visit(i + 1); if (y > 0) Visit(i - w); if (y < h - 1) Visit(i + w);
        }
        for (int i = 0; i < nearest.Length; i++) if (nearest[i] < 0) nearest[i] = i;
        return nearest;
    }

    // The object box (pixels that differ from the background) inside one sheet cell, in sheet pixels.
    static RectInt CellObjectBox(Texture2D tex, RectInt cell)
    {
        int inset = Mathf.RoundToInt(cell.width * .02f);
        var px = tex.GetPixels(cell.x + inset, cell.y + inset, cell.width - inset * 2, cell.height - inset * 2);
        int w = cell.width - inset * 2, h = cell.height - inset * 2;
        var bg = (px[0] + px[w - 1] + px[(h - 1) * w] + px[h * w - 1]) / 4f;
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            var p = px[y * w + x];
            if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) < .12f) continue;
            x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
        }
        if (x1 < 0) return cell;
        return new RectInt(cell.x + inset + x0, cell.y + inset + y0, x1 - x0 + 1, y1 - y0 + 1);
    }
}

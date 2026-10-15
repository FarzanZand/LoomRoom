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
            // Unity's built-in meshes (a quad lining a chest lid, added by hand) are not Synty parts: leave them alone.
            if (!AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith("Assets/")) continue;
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

    // The Synty model a prop part was made from: the mesh with Synty's own UVs and its flat-colour materials. A part made
    // from a changed copy (the tall crate) finds the materials by its name without the last word, and keeps its own mesh.
    // Several packs use the same mesh name (PolygonTown and PolygonFantasyKingdom both have an SM_Prop_Bookshelf_01):
    // with `shape` given, only a model of that size counts.
    public static bool SyntySource(string baseName, out Mesh mesh, out Material[] mats, Bounds? shape = null)
    {
        mesh = null; mats = null;
        for (string name = baseName; ; )
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Prefab", new[] { "Assets/Synty" }))
            {
                var synty = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (synty == null || synty.name != name) continue;
                var smf = synty.GetComponentInChildren<MeshFilter>(true); var smr = smf != null ? smf.GetComponent<MeshRenderer>() : null;
                if (smf == null || smr == null) continue;
                if (shape is Bounds want && name == baseName && smf.sharedMesh != null && (smf.sharedMesh.bounds.size - want.size).magnitude > want.size.magnitude * .02f) continue;
                mats = smr.sharedMaterials; if (name == baseName) mesh = smf.sharedMesh;
                return true;
            }
            int cut = name.LastIndexOf(' '); if (cut <= 0) return false;
            name = name.Substring(0, cut);
        }
    }

    // Half the side of every view's square: one scale for all five, so a texel is the same size in each.
    static float SheetHalf(Bounds b) => Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z)) * 1.08f;

    public static bool SheetFromRestyle;

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
            // SheetFromRestyle: the plain restyle instead (its baked shading shows openings flat Synty colour hides).
            if (SheetFromRestyle)
            {
                if (AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Room/{baseName}/{baseName}.asset") is { } rm) mf.sharedMesh = rm;
                if (AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Room/{baseName}/{baseName}.mat") is { } rmat) r.sharedMaterials = new[] { rmat };
                continue;
            }
            if (!SyntySource(baseName, out var syntyMesh, out var syntyMats, mf.sharedMesh.bounds)) continue;
            if (syntyMesh != null) mf.sharedMesh = syntyMesh;
            else if (AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Props/{baseName}/{baseName}.asset") is { } own) mf.sharedMesh = own;
            // Synty's own shaders (Generic_Basic) can draw nothing in this render: a plain lit copy with the same atlas.
            r.sharedMaterials = System.Array.ConvertAll(syntyMats, sm =>
            {
                if (sm == null || sm.shader.name == "Universal Render Pipeline/Lit") return sm;
                var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
                lit.SetTexture("_BaseMap", MainTexture(sm)); lit.SetFloat("_Smoothness", 0f);
                return lit;
            });
        }
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = PaintBackground;
        cam.nearClipPlane = .01f; cam.farClipPlane = b.size.magnitude * 3f + 50f; cam.aspect = 1f;
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
    public static string BuildPaintedPropLogged(string propPrefabPath, string name, float texelsPerUnit = ItemTexelsPerUnit, string category = "Props")
    {
        string line;
        try { line = BuildPaintedProp(propPrefabPath, name, texelsPerUnit, category); }
        catch (System.Exception e) { line = name + ": " + e; }
        File.AppendAllText($"{PaintFolder}/build.log", System.DateTime.Now.ToString("HH:mm:ss ") + line + System.Environment.NewLine);
        return line;
    }

    // Projects the painted sheet onto every part of the prop at the item density and saves the prefab. Room props
    // (category "Room") are baked at the room's own density per world unit of the prefab, which carries the prop at
    // the scale it stands at in the room; each part's plain restyle is read from Art/Synty Pixel/<category>/<mesh>.
    public static string BuildPaintedProp(string propPrefabPath, string name, float texelsPerUnit = ItemTexelsPerUnit, string category = "Props")
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
        // Where the painting's outline is a little inside the model's, a sample would land on the grey background:
        // every background pixel points at its nearest painted pixel instead.
        RimPeel = 0; // The rim peel copied one column across a band at every vertical edge (streaks); off.
        PaintFaceNormals = category == "Room";
        var nearest = NearestPainted(painted);
        RimPeel = 0;
        Object.DestroyImmediate(reference);

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
            float rayLength = Mathf.Max(20f, b.size.magnitude * 2f), rayStart = Mathf.Max(.004f, b.size.magnitude * .0002f);

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

            // A part used more than once (a table's chairs) is painted once, where it first stands, and shared. Names are
            // read first: rebuilding the first chair replaces the mesh the second one still points at.
            var baseNames = new Dictionary<MeshRenderer, string>();
            foreach (var r in parts) baseNames[r] = r.GetComponent<MeshFilter>().sharedMesh.name.Replace(" (Synty Pixel)", "").Replace(" painted", "");
            var done = new Dictionary<string, (Mesh mesh, Material material)>();
            foreach (var r in parts)
            {
                var mf = r.GetComponent<MeshFilter>(); var m = toProp[r];
                string baseName = baseNames[r];
                if (done.TryGetValue(baseName, out var shared)) { mf.sharedMesh = shared.mesh; r.sharedMaterials = new[] { shared.material }; continue; }
                // Always from the plain restyle (a re-run must not start from the last painting's mesh and UVs).
                string folder = $"{Root}/{category}/{baseName}";
                var src = AssetDatabase.LoadAssetAtPath<Mesh>($"{folder}/{baseName}.asset") ?? mf.sharedMesh;
                var srcMats = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{baseName}.mat") is { } plain ? new[] { plain } : r.sharedMaterials;
                string partName = baseName + " painted";
                float scale = Mathf.Max(.01f, r.transform.lossyScale.x);
                // Colour from the view that faces this point most squarely and can see it.
                PaintSource = (p, n) =>
                {
                    var pp = m.MultiplyPoint3x4(p); var pn = m.MultiplyVector(n).normalized;
                    if (PaintFaceNormals) return PickByColour(pp, pn, Sample, probe.transform.position, rayLength, rayStart, mask);
                    int best = -1; float bestDot = float.NegativeInfinity; int fallback = 0; float fallbackDot = float.NegativeInfinity;
                    for (int v = 0; v < PropViews.Length; v++)
                    {
                        float dot = Vector3.Dot(pn, PropViews[v].from);
                        if (dot > fallbackDot) { fallbackDot = dot; fallback = v; }
                        // A view that sees the face nearly edge-on smears one painted pixel across it (streaks under an
                        // overhang): better the view it faces, even if another part hides it there.
                        if (dot <= .35f || dot <= bestDot) continue;
                        var start = probe.transform.position + pp + pn * rayStart;
                        if (Physics.Raycast(start, PropViews[v].from, rayLength, mask, QueryTriggerInteraction.Collide)) continue;
                        best = v; bestDot = dot;
                    }
                    return Sample(best >= 0 ? best : fallback, pp);
                };
                float aim = texelsPerUnit * scale, measured = 0f;
                (Mesh mesh, Material material) built = default;
                // Room props: one bake first only to learn what the painter made of each of the model's colours.
                if (PaintFaceNormals)
                {
                    Learned = new Dictionary<int, List<Color>>(); Expected = null; PlainParts.Clear(); PartPlainSum.Clear();
                    PlainFaceColours = PlainColours ? SyntyFaceColours(baseName, src.triangles.Length / 3) : null;
                    ModelColours = PlainColours && System.Array.IndexOf(ModelColourProps, name.Substring(name.LastIndexOf('/') + 1)) >= 0;
                    AssetDatabase.DeleteAsset($"{folder}/{partName}.asset");
                    Build(src, srcMats, category, partName, aim, folder);
                    Expected = new Dictionary<int, Color>();
                    foreach (var kv in Learned)
                    {
                        var l = kv.Value; if (l.Count < 3) continue;
                        float[] rr = new float[l.Count], gg = new float[l.Count], bb = new float[l.Count];
                        for (int i = 0; i < l.Count; i++) { rr[i] = l[i].r; gg[i] = l[i].g; bb[i] = l[i].b; }
                        System.Array.Sort(rr); System.Array.Sort(gg); System.Array.Sort(bb); int h = l.Count / 2;
                        Expected[kv.Key] = new Color(rr[h], gg[h], bb[h], 1f);
                        // ModelColourProps keep the room's own colour per part (see there).
                        if (kv.Key >= PartKey && ModelColours) Expected[kv.Key] = PartColour(kv.Key - PartKey);
                    }
                    if (PlainColours && PlainFaceColours != null) for (int i = 0; i < PlainParts.Count; i++) if (!Expected.ContainsKey(PartKey + i)) Expected[PartKey + i] = PartColour(i);
                    Learned = null;
                }
                for (int pass = 0; pass < 4; pass++)
                {
                    // A fresh mesh asset each pass: copying over an existing one keeps its old UVs.
                    AssetDatabase.DeleteAsset($"{folder}/{partName}.asset");
                    built = Build(src, srcMats, category, partName, aim, folder);
                    if (built.mesh == null) break;
                    mf.sharedMesh = built.mesh; r.sharedMaterials = new[] { built.material };
                    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(built.material.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                    measured = TexelsPerUnit(r);
                    if (measured <= 0f || Mathf.Abs(measured / texelsPerUnit - 1f) < .05f) break;
                    aim *= texelsPerUnit / measured;
                }
                PaintSource = null; Expected = null; PlainFaceColours = null; ModelColours = false;
                if (built.mesh == null) { results.Add($"{src.name} failed"); continue; }
                done[baseName] = built;
                results.Add($"{partName} {measured:0}/m");
            }
            PrefabUtility.SaveAsPrefabAsset(root, propPrefabPath);
            // The meshes were replaced at the same paths: reimport them and the prefab, or it keeps stale references.
            foreach (var r in parts) AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(r.GetComponent<MeshFilter>().sharedMesh), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(propPrefabPath, ImportAssetOptions.ForceUpdate);
        }
        finally { PaintSource = null; PaintFaceNormals = false; Object.DestroyImmediate(probe); PrefabUtility.UnloadPrefabContents(root); Object.DestroyImmediate(painted); }
        return $"{name}: " + string.Join(", ", results) + $" (target {texelsPerUnit:0.#})";
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
            string meshPath = $"{dir}/{partName}.asset"; AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(mesh, meshPath); MarkReadable(mesh);
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

    // The review render for one prop: zoomed to fill each cell, from eight sides at eye height, three-quarter angles from
    // above, straight down and from low down, so every face (sides, back, base, top, undersides of overhangs) is seen up
    // close. Point filtering keeps each texel a crisp block a few screen pixels wide, so streaks and smears show.
    public static string RenderPropTurntable(string prefabPath, string outFile, int cell = 512)
    {
        bool asyncShaders = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
        var root = new GameObject("prop turntable") { hideFlags = HideFlags.DontSave };
        root.transform.position = new Vector3(0, -980, 0);
        var copy = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), root.transform);
        copy.transform.localPosition = Vector3.zero;
        foreach (var l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (var ps in copy.GetComponentsInChildren<ParticleSystemRenderer>(true)) ps.enabled = false;
        PropParts(copy, out var b);
        var centre = copy.transform.TransformPoint(b.center); float radius = b.extents.magnitude;
        var camGo = new GameObject("cam"); camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .17f, .16f);
        cam.nearClipPlane = .01f; cam.farClipPlane = radius * 6 + 10; cam.aspect = 1f; cam.orthographicSize = radius * 1.02f;
        var light = new GameObject("light").AddComponent<Light>(); light.transform.SetParent(root.transform, false);
        light.type = LightType.Directional; light.intensity = 1.1f;
        var fill = new GameObject("fill").AddComponent<Light>(); fill.transform.SetParent(root.transform, false);
        fill.type = LightType.Directional; fill.intensity = .45f;
        var views = new List<(float pitch, float yaw)>();
        for (int i = 0; i < 8; i++) views.Add((8f, i * 45f));
        views.Add((35f, 35f)); views.Add((35f, 215f)); views.Add((89f, 0f)); views.Add((-25f, 135f));
        int cols = 4, rows = (views.Count + cols - 1) / cols;
        var rt = new RenderTexture(cell, cell, 24); cam.targetTexture = rt;
        var sheet = new Texture2D(cell * cols, cell * rows, TextureFormat.RGB24, false);
        var px = new Texture2D(cell, cell, TextureFormat.RGB24, false);
        for (int i = 0; i < views.Count; i++)
        {
            var dir = Quaternion.Euler(views[i].pitch, views[i].yaw, 0) * Vector3.forward;
            camGo.transform.position = centre - dir * (radius * 3 + 2); camGo.transform.rotation = Quaternion.LookRotation(dir);
            // Key light over the camera's shoulder, fill from the other side: every face is lit, none blown out.
            light.transform.rotation = Quaternion.LookRotation(dir + camGo.transform.up * -.6f + camGo.transform.right * .5f);
            fill.transform.rotation = Quaternion.LookRotation(dir + camGo.transform.up * .3f - camGo.transform.right * .8f);
            cam.Render();
            RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, cell, cell), 0, 0); px.Apply();
            sheet.SetPixels((i % cols) * cell, (rows - 1 - i / cols) * cell, cell, cell, px.GetPixels());
        }
        sheet.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
        File.WriteAllBytes(outFile, sheet.EncodeToPNG());
        RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(px); Object.DestroyImmediate(sheet); Object.DestroyImmediate(root);
        ShaderUtil.allowAsyncCompilation = asyncShaders;
        return outFile;
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

    // Room props: the painter recolours freely (a grey Synty lamp comes back bronze), so a texel's colour is judged
    // against what the painting made of that Synty colour elsewhere on the model (Expected: the median painted colour
    // over the faces of that colour, each seen squarely by an unhidden view, learned in a first bake). Of the views
    // that see this point, the one closest to that colour wins; a view painting leaves on soil, a knob's black on a
    // door or an outline's dark on a neck loses. With no view close enough, the expected colour itself.
    static Dictionary<int, List<Color>> Learned;
    // Organic props (plants, flowers, a cake, laundry): no projection at all. Each face takes the colour the painter
    // gave that part of the model (Expected), varied a little per texel; creases still darken in Build.
    public static bool PlainColours;
    // Plain colours: the model's parts, one per Synty swatch colour (only rounding apart: some real parts differ by
    // less than 0.1, cream and white jugs). The painted colour is learned and looked up per part; the rounded colour
    // key put faces of one part on either side of a key step and gave them two painted colours (diagonal splits and a
    // lid's brown on a jar body, 2026-10-15).
    static List<Color> PlainParts = new List<Color>();
    // Plain colours: each triangle's colour on the Synty original (flat swatches), by triangle index. The plain
    // restyle the bake starts from is grainy pixel art, and colours read from it scattered one part over several.
    static Color[] PlainFaceColours;
    // Plain-colour room props whose painting can't be read per part (thin necks and leaves: most samples are outline
    // and shadow, the median came out grey-brown or olive): each part keeps the room's own colour instead.
    public static readonly string[] ModelColourProps = { "Pot plant", "Flamingo" };
    static bool ModelColours;
    // Each part's colour on the plain restyle (the room's own materials, averaged over the part): the Synty atlas only
    // tells which faces belong together (the room's flowers use a green material, the Synty prefab an olive one).
    static Dictionary<int, Vector4> PartPlainSum = new Dictionary<int, Vector4>();
    public static void NotePlainColour(Color plainColour)
    {
        if (Learned == null || !PlainColours || PlainFaceColours == null) return;
        int part = PlainParts.IndexOf(PaintBase); if (part < 0) return;
        PartPlainSum.TryGetValue(part, out var v); PartPlainSum[part] = v + new Vector4(plainColour.r, plainColour.g, plainColour.b, 1f);
    }
    static Color PartColour(int part) => PartPlainSum.TryGetValue(part, out var v) && v.w > 0 ? new Color(v.x / v.w, v.y / v.w, v.z / v.w, 1f) : PlainParts[part];
    public static Color[] SyntyFaceColours(string baseName, int triangles)
    {
        if (!SyntySource(baseName, out var sMesh, out var sMats) || sMesh == null || sMats == null || sMats.Length == 0) return null;
        return SyntyFaceColours(sMesh, sMats, triangles);
    }

    public static Color[] SyntyFaceColours(Mesh sMesh, Material[] sMats, int triangles)
    {
        if (sMesh == null || sMats == null || sMats.Length == 0) return null;
        var st = sMesh.triangles; if (st.Length / 3 != triangles) return null;
        var suv = new List<Vector2>(); sMesh.GetUVs(0, suv); if (suv.Count == 0) return null;
        var cache = new Dictionary<Texture, Texture2D>(); var result = new Color[triangles];
        for (int sub = 0, at = 0; sub < sMesh.subMeshCount; sub++)
        {
            var mat = sMats[Mathf.Min(sub, sMats.Length - 1)];
            var pal = Palette(mat, "_BaseMap", "_Albedo_Map", "_MainTex", cache);
            Color tint = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
            var t = sMesh.GetTriangles(sub);
            for (int f = 0; f < t.Length; f += 3, at++)
                result[at] = (pal != null ? Sample(pal, (suv[t[f]] + suv[t[f + 1]] + suv[t[f + 2]]) / 3f) : Color.white) * tint;
        }
        foreach (var c in cache.Values) Object.DestroyImmediate(c);
        return result;
    }
    static Color PlainPart(Color c)
    {
        foreach (var p in PlainParts) if (Mathf.Abs(p.r - c.r) + Mathf.Abs(p.g - c.g) + Mathf.Abs(p.b - c.b) < .03f) return p;
        PlainParts.Add(c); return c;
    }
    // Diagnosis: every texel takes the most head-on view that sees it, no colour judgement (for colour-coded sheets).
    public static bool DebugSquarest;
    // Diagnosis: plain-colour props show each face's own model colour (its part), not the painted colour.
    public static bool DebugPlainBase;
    static Dictionary<int, Color> Expected;
    const int PartKey = 1 << 20;
    static int ColourKey(Color c) => Mathf.RoundToInt(c.r * 7) * 64 + Mathf.RoundToInt(c.g * 7) * 8 + Mathf.RoundToInt(c.b * 7);

    // How far a painted colour is from the colour wanted: hue and saturation (colour over its brightness) plus a
    // little for brightness.
    static float ColourScore(Color c, Color want)
    {
        float cl = c.grayscale, wl = want.grayscale;
        Vector3 a = new Vector3(c.r, c.g, c.b) / (cl + .05f), b = new Vector3(want.r, want.g, want.b) / (wl + .05f);
        return (a - b).magnitude + Mathf.Abs(Mathf.Log((cl + .04f) / (wl + .04f))) * .6f;
    }

    static Color PickByColour(Vector3 pp, Vector3 pn, System.Func<int, Vector3, Color> sample, Vector3 probeAt, float rayLength, float rayStart, int mask)
    {
        // Plain colours learn and look up the painted colour per part (rounded keys could put two faces of one part
        // on either side of a step and give them two painted colours).
        int part = PlainColours && PlainFaceColours != null ? PlainParts.IndexOf(PaintBase) : -1;
        int key = part >= 0 ? PartKey + part : ColourKey(PaintBase);
        int squarest = -1; float squarestDot = DebugSquarest ? .2f : .6f;
        var seen = new List<(int v, float dot)>();
        for (int v = 0; v < PropViews.Length; v++)
        {
            float dot = Vector3.Dot(pn, PropViews[v].from);
            if (dot <= .45f) continue;
            if (Physics.Raycast(probeAt + pp + pn * rayStart, PropViews[v].from, rayLength, mask, QueryTriggerInteraction.Collide)) continue;
            seen.Add((v, dot));
            if (dot > squarestDot) { squarestDot = dot; squarest = v; }
        }
        if (DebugSquarest) return squarest >= 0 ? sample(squarest, pp) : Color.magenta;
        if (Learned != null)
        {
            if (squarest < 0) return PaintBase;
            var c0 = sample(squarest, pp);
            if (!Learned.TryGetValue(key, out var list)) Learned[key] = list = new List<Color>();
            list.Add(c0);
            return c0;
        }
        Color want = PaintBase;
        if (Expected != null && !Expected.TryGetValue(key, out want))
        {
            // A colour no view saw squarely: the painted colour of the nearest model colour that was seen.
            float bd = float.MaxValue; want = PaintBase;
            foreach (var kv in Expected)
            {
                var kc = kv.Key >= PartKey ? PlainParts[kv.Key - PartKey] : new Color(kv.Key / 64 / 7f, kv.Key / 8 % 8 / 7f, kv.Key % 8 / 7f);
                float d = Mathf.Abs(kc.r - PaintBase.r) + Mathf.Abs(kc.g - PaintBase.g) + Mathf.Abs(kc.b - PaintBase.b);
                if (d < bd) { bd = d; want = kv.Value; }
            }
        }
        if (PlainColours && DebugPlainBase) return PaintBase;
        if (PlainColours) seen.Clear();
        // The most head-on view wins; a view is only passed over when its colour is clearly not this part's (a knob
        // smear, the painter's outline, background grey), then the next most head-on one is tried.
        seen.Sort((x, y) => y.dot.CompareTo(x.dot));
        foreach (var (v, dot) in seen)
        {
            var c = sample(v, pp);
            if (ColourScore(c, want) < .8f) return c;
        }
        float bestScore = float.MaxValue; Color best = want;
        // Nothing fits: the expected colour, varied a little per texel so it isn't a flat patch.
        // One step per texel (6.4 a world unit in the room) so the variation reads as pixels, not noise.
        int hx = Mathf.FloorToInt(pp.x * RoomTexelsPerUnit), hy = Mathf.FloorToInt(pp.y * RoomTexelsPerUnit), hz = Mathf.FloorToInt(pp.z * RoomTexelsPerUnit);
        float k = 1f + (Hash(hx, hy, hz) - .5f) * .12f;
        return new Color(want.r * k, want.g * k, want.b * k, 1f);
    }

    // Extra pixels peeled off every painted silhouette's outside (set for room props only; items keep 0).
    static int RimPeel;

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
        // The painter draws a dark outline round each view, and the edge blends into the background through a ring of
        // plain grey pixels; faces near a view's edge would pick them up (black stave tips, grey bands round a grip).
        // Peel dark and plain grey pixels off the outside so they count as background: the outline is often a whole
        // painted pixel thick (8-10 image pixels; 4 layers left black notches along a shield's top), the grey ring is thin.
        for (int pass = 0; pass < 12; pass++)
        {
            var peel = new List<int>();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                bool grey = Mathf.Max(c.r, Mathf.Max(c.g, c.b)) - Mathf.Min(c.r, Mathf.Min(c.g, c.b)) < .05f;
                if (nearest[i] < 0 || c.grayscale > .16f && !(grey && pass < 4)) continue;
                int x = i % w, y = i / w;
                if ((x > 0 && nearest[i - 1] < 0) || (x < w - 1 && nearest[i + 1] < 0) || (y > 0 && nearest[i - w] < 0) || (y < h - 1 && nearest[i + w] < 0)) peel.Add(i);
            }
            foreach (int i in peel) nearest[i] = -1;
        }
        // Room props: the painter also lights every view's outer edge, and narrow faces beside an edge picked it up as a
        // pale band. Each painted pixel within RimPeel of the background is pointed RimPeel deeper inside where the
        // painting is that thick (thin parts, a chair's legs, keep their own pixels).
        int[] deep = null;
        if (RimPeel > 0)
        {
            var dist = new int[w * h]; var dq = new Queue<int>();
            for (int i = 0; i < px.Length; i++) { if (nearest[i] < 0) { dist[i] = 0; dq.Enqueue(i); } else dist[i] = int.MaxValue; }
            while (dq.Count > 0)
            {
                int i = dq.Dequeue(), x = i % w, y = i / w;
                void Step(int j) { if (dist[j] > dist[i] + 1) { dist[j] = dist[i] + 1; dq.Enqueue(j); } }
                if (x > 0) Step(i - 1); if (x < w - 1) Step(i + 1); if (y > 0) Step(i - w); if (y < h - 1) Step(i + w);
            }
            deep = new int[w * h]; var reach = new int[w * h];
            for (int i = 0; i < px.Length; i++) { deep[i] = -1; reach[i] = int.MaxValue; }
            for (int i = 0; i < px.Length; i++) if (nearest[i] >= 0 && dist[i] > RimPeel) { deep[i] = i; reach[i] = 0; dq.Enqueue(i); }
            while (dq.Count > 0)
            {
                int i = dq.Dequeue(), x = i % w, y = i / w;
                if (reach[i] >= RimPeel + 2) continue;
                void Spread(int j) { if (reach[j] == int.MaxValue && nearest[j] >= 0) { reach[j] = reach[i] + 1; deep[j] = deep[i]; dq.Enqueue(j); } }
                if (x > 0) Spread(i - 1); if (x < w - 1) Spread(i + 1); if (y > 0) Spread(i - w); if (y < h - 1) Spread(i + w);
            }
        }
        for (int i = 0; i < px.Length; i++) if (nearest[i] >= 0) queue.Enqueue(i);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue(), x = i % w, y = i / w;
            void Visit(int j) { if (nearest[j] < 0) { nearest[j] = nearest[i]; queue.Enqueue(j); } }
            if (x > 0) Visit(i - 1); if (x < w - 1) Visit(i + 1); if (y > 0) Visit(i - w); if (y < h - 1) Visit(i + w);
        }
        for (int i = 0; i < nearest.Length; i++) if (nearest[i] < 0) nearest[i] = i;
        if (deep != null) for (int i = 0; i < nearest.Length; i++) { int k = nearest[i]; if (deep[k] >= 0) nearest[i] = deep[k]; }
        return nearest;
    }

    // The object box (pixels that differ from the background) inside one sheet cell, in sheet pixels.
    static RectInt CellObjectBox(Texture2D tex, RectInt cell)
    {
        int inset = Mathf.RoundToInt(cell.width * .02f);
        var px = tex.GetPixels(cell.x + inset, cell.y + inset, cell.width - inset * 2, cell.height - inset * 2);
        int w = cell.width - inset * 2, h = cell.height - inset * 2;
        var bg = (px[0] + px[w - 1] + px[(h - 1) * w] + px[h * w - 1]) / 4f;
        // The body's box, not the outermost pixel: thin protrusions (knobs, a handle, a stray stroke) are drawn at a
        // different size by the painter and would shift the whole view. A column or row counts when it is at least a
        // quarter as full as the fullest one.
        var cols = new int[w]; var rows = new int[h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            var p = px[y * w + x];
            if (Mathf.Abs(p.r - bg.r) + Mathf.Abs(p.g - bg.g) + Mathf.Abs(p.b - bg.b) < .12f) continue;
            cols[x]++; rows[y]++;
        }
        int maxC = 0, maxR = 0; foreach (var c in cols) maxC = Mathf.Max(maxC, c); foreach (var r in rows) maxR = Mathf.Max(maxR, r);
        if (maxC == 0) return cell;
        int x0 = 0, x1 = w - 1, y0 = 0, y1 = h - 1;
        while (x0 < w - 1 && cols[x0] < maxC / 4) x0++; while (x1 > x0 && cols[x1] < maxC / 4) x1--;
        while (y0 < h - 1 && rows[y0] < maxR / 4) y0++; while (y1 > y0 && rows[y1] < maxR / 4) y1--;
        return new RectInt(cell.x + inset + x0, cell.y + inset + y0, x1 - x0 + 1, y1 - y0 + 1);
    }
}

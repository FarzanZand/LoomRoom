using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Inventory icons rendered from the items' own models, Diablo style: each icon is the item's bag footprint
// (ItemData.GridSize) at ItemIconSettings.cellPixels per cell. The model is posed in an empty preview scene, lit by a
// key and a fill light, rendered `supersample` times larger, averaged down, cut at `alphaCut` coverage and outlined.
// Worn gear without a model is cut from a Synty outfit (ItemIconRenderer.Outfits.cs).
//
// Icons are written to Items/Icons/Generated/<item asset>.png; the importer's userData keeps a key of everything the
// icon shows, so an icon is made again when that changes (OnValidate, a saved prefab, the settings), and never when
// the item's icon is custom or is a sprite from somewhere else.
[InitializeOnLoad]
public static partial class ItemIconRenderer
{
    public const string Folder = "Assets/Game/Items/Icons/Generated";
    const int Version = 1;

    enum Mode { Long, Face, Native, Auto }
    enum Cut { None, Body, Gloves, Boots, Legs }

    // What to render for one item.
    class Job
    {
        public GameObject prefab; public Mesh mesh; public Material material;
        public ItemIconSettings.Outfit outfit; public Cut cut;
        public Mode mode; public Vector3 view, turn; public bool flip, back;
        public Vector2Int cells; public Color tint = Color.white;
    }

    static ItemIconRenderer()
    {
        ItemData.EditorIconRequested = item => Report(item, Generate(item));
        ItemData.EditorValidated = Queue;
        ItemIconSource.OutfitList = () => Settings != null ? Settings.outfits.Where(o => o != null).Select(o => o.name).ToArray() : new string[0];
    }

    static ItemIconSettings settings;
    public static ItemIconSettings Settings => settings != null ? settings : settings = AssetDatabase.LoadAssetAtPath<ItemIconSettings>(ItemIconSettings.Path);

    public static string PathFor(ItemData item) => Folder + "/" + item.name + ".png";
    public static bool IsGenerated(Sprite sprite) => sprite != null && AssetDatabase.GetAssetPath(sprite).StartsWith(Folder + "/");
    static bool Archived(ItemData item) => AssetDatabase.GetAssetPath(item).Contains("/_Archive/");
    static bool Worn(ItemData item) => item.itemType == ItemType.Equipment && item.canBeEquipped && item.equipSlot is
        EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Gloves or EquipmentSlot.Boots or EquipmentSlot.Legs;

    static void Report(ItemData item, string error)
    {
        if (error == null) Debug.Log($"[Item icons] {item.itemName}: {AssetDatabase.GetAssetPath(item.icon)}", item);
        else EditorUtility.DisplayDialog("Generate icon", item.itemName + ": " + error, "OK");
    }

    // ── Automatic updates ─────────────────────────────────────────────

    static readonly HashSet<ItemData> queued = new();
    static void Queue(ItemData item)
    {
        if (item == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (queued.Add(item) && queued.Count == 1) EditorApplication.delayCall += Flush;
    }
    static void Flush()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Flush; return; }
        var items = queued.Where(i => i != null).ToList(); queued.Clear();
        var stale = items.Where(NeedsUpdate).ToList();
        if (stale.Count == 0) return;
        using var session = new Session();
        foreach (var item in stale)
        {
            string error = Generate(item, session);
            if (error != null) Debug.LogWarning($"[Item icons] {item.itemName}: {error}", item);
        }
    }
    // Saved prefabs, meshes, textures and the settings can change what an icon shows (ItemData changes arrive
    // through OnValidate); the keys decide which icons really changed.
    class Watcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (!imported.Any(p => !p.StartsWith(Folder + "/") && (p.EndsWith(".prefab") || p.EndsWith(".fbx") || p.EndsWith(".png") ||
                p.EndsWith(".asset") && p.StartsWith("Assets/Game/Art/") || p == ItemIconSettings.Path))) return;
            foreach (string guid in AssetDatabase.FindAssets("t:ItemData")) Queue(AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid)));
        }
    }

    // True when the item's generated icon is missing or out of date (custom and foreign icons are left alone).
    public static bool NeedsUpdate(ItemData item)
    {
        if (item == null || Settings == null || !AssetDatabase.Contains(item) || Archived(item)) return false;
        if (item.iconSource != null && item.iconSource.custom) return false;
        if (item.icon != null && !IsGenerated(item.icon)) return false;
        var job = Resolve(item, out _);
        if (job == null) return false;
        if (item.icon == null) return true;
        string path = AssetDatabase.GetAssetPath(item.icon);
        return path != PathFor(item) || AssetImporter.GetAtPath(path)?.userData != Key(item, job);
    }

    // ── Generating ────────────────────────────────────────────────────

    // Renders the item's icon and assigns it. Returns null, or why there is no icon. Custom icons are kept.
    public static string Generate(ItemData item)
    {
        if (Settings == null) return "No icon settings at " + ItemIconSettings.Path + ".";
        using var session = new Session(); return Generate(item, session);
    }

    // Several items in one preview scene; shows a progress bar. Returns how many icons were made.
    public static int GenerateAll(IEnumerable<ItemData> items, List<string> problems = null)
    {
        var list = items.Where(i => i != null && !Archived(i) && !(i.iconSource != null && i.iconSource.custom)).ToList();
        int made = 0;
        if (Settings == null) { problems?.Add("No icon settings at " + ItemIconSettings.Path + "."); return 0; }
        using var session = new Session();
        try
        {
            for (int i = 0; i < list.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Item icons", list[i].itemName, (float)i / list.Count);
                string error = Generate(list[i], session);
                if (error == null) made++; else problems?.Add(list[i].itemName + ": " + error);
            }
        }
        finally { EditorUtility.ClearProgressBar(); }
        return made;
    }

    static string Generate(ItemData item, Session session)
    {
        if (Settings == null) return "No icon settings at " + ItemIconSettings.Path + ".";
        if (!AssetDatabase.Contains(item)) return "Not an asset.";
        if (item.iconSource != null && item.iconSource.custom) return "The icon is custom (Icon Source > Custom); untick it to generate.";
        var job = Resolve(item, out string reason);
        if (job == null) return reason;
        string key = Key(item, job); // before rendering, which settles the job's mode and view
        Texture2D texture;
        try { texture = session.Render(job); }
        catch (System.Exception e) { Debug.LogException(e); return "Rendering failed: " + e.Message; }
        if (texture == null) return "Nothing visible to render.";

        string path = PathFor(item);
        ItemDatabaseAuthoring.EnsureFolder(Folder);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = Settings.cellPixels;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.userData = key;
        importer.SaveAndReimport();

        var old = item.icon;
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        Undo.RecordObject(item, "Generate icon");
        item.icon = sprite;
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssetIfDirty(item);
        // An icon left behind by a rename, if nothing else uses it.
        if (old != null && IsGenerated(old) && AssetDatabase.GetAssetPath(old) != path &&
            !ItemDatabaseAuthoring.All().Any(i => i != item && i.icon == old))
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(old));
        return null;
    }

    // What the item's icon shows, or null with the reason.
    static Job Resolve(ItemData item, out string reason)
    {
        reason = null;
        var source = item.iconSource ?? new ItemIconSource();
        var job = new Job { cells = item.GridSize, flip = source.flip };
        var look = Settings.Look(item.tier);
        bool worn = Worn(item);
        if (source.model != null) job.prefab = source.model;
        else if (worn && item.equipSlot == EquipmentSlot.Head)
        {
            if (look == null || look.helmPrefab == null && (look.helmMesh == null || look.helmMaterial == null)) { reason = $"No helm for tier {item.tier} in the icon settings."; return null; }
            job.prefab = look.helmPrefab; job.mesh = look.helmMesh; job.material = look.helmMaterial; job.tint = look.tint;
        }
        else if (worn)
        {
            job.outfit = Settings.FindOutfit(source.outfit);
            if (job.outfit == null) { job.outfit = Settings.FindOutfit(look?.outfit); if (look != null) job.tint = look.tint; }
            if (job.outfit == null || job.outfit.prefab == null || job.outfit.mesh == null) { reason = $"No outfit for tier {item.tier} in the icon settings."; return null; }
            job.cut = item.equipSlot switch { EquipmentSlot.Body => Cut.Body, EquipmentSlot.Gloves => Cut.Gloves, EquipmentSlot.Boots => Cut.Boots, _ => Cut.Legs };
        }
        else job.prefab = item.worldPrefab != null ? item.worldPrefab : item.pickupVisualPrefab;
        if (job.prefab == null && job.mesh == null && job.outfit == null) { reason = "No model: set a World Prefab or Icon Source > Model."; return null; }

        // Views: long items stand upright and shields face you, nearly straight on so the painted face shows; helms and cut gear are seen from the front, a little turned.
        bool tall = job.cells.x == 1 && job.cells.y >= 2;
        if (job.cut != Cut.None) { job.mode = Mode.Native; job.view = job.cut switch { Cut.Gloves => new Vector3(0, 180, 0), Cut.Boots => new Vector3(0, 215, 0), _ => new Vector3(0, 195, 0) }; }
        else if (worn && item.equipSlot == EquipmentSlot.Head) { job.mode = Mode.Native; job.view = new Vector3(8, 208, 0); }
        else if (tall) { job.mode = Mode.Long; job.view = Vector3.zero; }
        else if (item.itemType == ItemType.Shield) { job.mode = Mode.Face; job.view = Vector3.zero; job.back = true; } // Synty shields face -Z
        else { job.mode = Mode.Auto; job.view = new Vector3(14, 22, 0); }
        job.turn = source.turn;
        job.view += source.turn;
        return job;
    }

    static string Key(ItemData item, Job job)
    {
        string Asset(Object o) => o == null ? "-" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) + ":" + FileHash(AssetDatabase.GetAssetPath(o));
        var s = Settings;
        string parts = string.Join("|", Version, s.cellPixels, s.supersample, s.alphaCut, s.margin, s.drawOutline, s.outline, s.softEdges, s.sampling, s.sharpen, s.keyAngles, s.keyColor, s.keyIntensity,
            s.fillAngles, s.fillColor, s.fillIntensity, job.cells, job.mode, job.view, job.flip, job.back, job.tint, job.cut,
            Asset(job.prefab), Parts(job.prefab), Asset(job.mesh), Asset(job.material), job.outfit?.name, Asset(job.outfit?.prefab), job.outfit?.renderer, Asset(job.outfit?.mesh), Asset(job.outfit?.material));
        return Hash128.Compute(parts).ToString();
    }
    // The meshes and textures a prefab draws with (repainted art keeps the prefab file as it is). Material files are
    // left out: pixel lighting switches their shader in place, which must not count as a change.
    static string Parts(GameObject prefab)
    {
        if (prefab == null) return "-";
        var parts = new List<string>();
        foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
        {
            var mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
            parts.Add(mesh == null ? "no mesh" : FileHash(AssetDatabase.GetAssetPath(mesh)) + mesh.name);
            foreach (var m in r.sharedMaterials) if (m != null && m.mainTexture != null) parts.Add(FileHash(AssetDatabase.GetAssetPath(m.mainTexture)));
        }
        return Hash128.Compute(string.Join(",", parts)).ToString();
    }
    // The model file itself (not its textures), so switching pixel lighting never counts as a change.
    static string FileHash(string path) => string.IsNullOrEmpty(path) || !File.Exists(path) || path.EndsWith(".fbx") ? path : Hash128.Compute(File.ReadAllBytes(path)).ToString();

    // ── Rendering ─────────────────────────────────────────────────────

    // A preview scene with the camera and lights, reused for a batch of icons.
    sealed class Session : System.IDisposable
    {
        public readonly Scene scene;
        readonly Camera camera;

        public Session()
        {
            scene = EditorSceneManager.NewPreviewScene();
            camera = Make("Icon camera").AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene; camera.cameraType = CameraType.Game;
            camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
            camera.nearClipPlane = .01f; camera.farClipPlane = 100f;
            var s = Settings;
            Light(s.keyAngles, s.keyColor, s.keyIntensity); Light(s.fillAngles, s.fillColor, s.fillIntensity);
            // Pixel lighting switches materials to LoomRoom/Pixel Lit in place (in Play mode, or left on in the editor);
            // that shader has nothing to draw with in a preview scene and every icon came out flat cyan. Icons are drawn
            // with the materials' own shaders, and pixel lighting is put back afterwards.
            var library = AssetDatabase.LoadAssetAtPath<PixelLookLibrary>("Assets/Game/Rendering/PixelLook/Pixel Look Library.asset");
            if (library != null && library.pixelLitShader != null)
                foreach (var e in library.lighting)
                    if (e.material != null && e.original != null && e.material.shader == library.pixelLitShader)
                    {
                        pixelLit.Add((e.material, e.material.renderQueue));
                        e.material.shader = e.original; e.material.renderQueue = e.originalQueue;
                    }
            pixelShader = library != null ? library.pixelLitShader : null;
        }
        readonly List<(Material material, int queue)> pixelLit = new();
        readonly Shader pixelShader;
        public void Dispose()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var (material, queue) in pixelLit) if (material != null) { material.shader = pixelShader; material.renderQueue = queue; }
        }

        public GameObject Make(string name) { var g = new GameObject(name); SceneManager.MoveGameObjectToScene(g, scene); return g; }
        void Light(Vector3 angles, Color color, float intensity)
        {
            var light = Make("Light").AddComponent<Light>();
            light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(angles);
        }

        public Texture2D Render(Job job)
        {
            var root = Make("Item");
            try
            {
                if (job.outfit != null) { if (!BuildCut(this, job, root)) return null; }
                else if (!BuildModel(job, root)) return null;
                return Shoot(root, job);
            }
            finally
            {
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null && (filter.sharedMesh.hideFlags & HideFlags.DontSave) != 0) Object.DestroyImmediate(filter.sharedMesh);
                Object.DestroyImmediate(root);
            }
        }

        // A prefab or mesh under root, turned per mode and centred on the origin.
        bool BuildModel(Job job, GameObject root)
        {
            GameObject model;
            if (job.prefab != null) model = (GameObject)PrefabUtility.InstantiatePrefab(job.prefab, scene);
            else { model = Make("Mesh"); model.AddComponent<MeshFilter>().sharedMesh = job.mesh; model.AddComponent<MeshRenderer>().sharedMaterial = job.material; }
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
            Clean(model);
            var points = Points(root);
            if (points.Count == 0) return false;
            var b = BoundsOf(points);
            var align = Quaternion.identity;
            // Small items as they stand, unless they are flat (charms, pendants, books): those face the camera.
            if (job.mode == Mode.Auto)
            {
                var extent = b.size;
                bool flat = Mathf.Min(extent.x, Mathf.Min(extent.y, extent.z)) < .4f * Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z));
                job.mode = flat ? Mode.Face : Mode.Native;
                if (flat) job.view = Vector3.zero + job.turn;
            }
            if (job.mode != Mode.Native)
            {
                // Longest axis up (the pivot end down: hilts and handles), thinnest axis towards the camera.
                var size = b.size; var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
                var order = new[] { 0, 1, 2 }; System.Array.Sort(order, (i, j) => size[j].CompareTo(size[i]));
                var up = axes[order[0]];
                if (job.mode == Mode.Long && b.center[order[0]] < 0) up = -up;
                var depth = axes[order[2]];
                align = Quaternion.Inverse(Quaternion.LookRotation(depth, up));
            }
            // Flip: long items end over end, flat ones show their other side.
            if (job.back ^ (job.flip && job.mode == Mode.Face)) align = Quaternion.Euler(0, 180, 0) * align;
            if (job.flip && job.mode != Mode.Face) align = Quaternion.Euler(0, 0, 180) * align;
            model.transform.localRotation = align;
            model.transform.localPosition = -(align * b.center);
            return true;
        }

        // Frames root's meshes in the footprint, renders, averages down, outlines.
        Texture2D Shoot(GameObject root, Job job)
        {
            var s = Settings;
            root.transform.rotation = Quaternion.Euler(job.view);
            var points = Points(root);
            if (points.Count == 0) return null;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = -min;
            foreach (var p in points) { min = Vector2.Min(min, p); max = Vector2.Max(max, p); }
            int width = job.cells.x * s.cellPixels, height = job.cells.y * s.cellPixels, ss = s.supersample;
            float aspect = (float)width / height;
            var extent = (max - min) / 2f; var centre = (min + max) / 2f;
            float pad = s.margin * 2f;
            camera.orthographicSize = Mathf.Max(extent.y * height / (height - pad), extent.x / aspect * width / (width - pad));
            camera.aspect = aspect;
            camera.transform.position = new Vector3(centre.x, centre.y, -20f);

            var rt = new RenderTexture(width * ss, height * ss, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var big = new Texture2D(width * ss, height * ss, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt; camera.Render();
                RenderTexture.active = rt;
                big.ReadPixels(new Rect(0, 0, width * ss, height * ss), 0, 0); big.Apply();
            }
            finally { RenderTexture.active = previous; camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); }

            var source = big.GetPixels(); Object.DestroyImmediate(big);
            var pixels = new Color[width * height]; var solid = new bool[width * height];
            float cut = (s.softEdges ? .04f : s.alphaCut) * ss * ss;
            var buckets = new Dictionary<int, Vector4>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Coverage decides whether the pixel is part of the item; its colour is the one most of the
                    // block shares (Dominant: texels stay crisp) or the block's average (Average: softer).
                    float r = 0, g = 0, b = 0, a = 0;
                    buckets.Clear();
                    for (int j = 0; j < ss; j++)
                        for (int i = 0; i < ss; i++)
                        {
                            var c = source[(y * ss + j) * width * ss + x * ss + i];
                            r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                            if (c.a < .5f) continue;
                            int bucket = (int)(c.r * 15.99f) << 8 | (int)(c.g * 15.99f) << 4 | (int)(c.b * 15.99f);
                            buckets.TryGetValue(bucket, out var sum);
                            buckets[bucket] = sum + new Vector4(c.r, c.g, c.b, 1);
                        }
                    if (a < cut) continue;
                    var colour = new Color(r / a, g / a, b / a);
                    if (s.sampling == ItemIconSettings.Sampling.Dominant && buckets.Count > 0)
                    {
                        var best = Vector4.zero;
                        foreach (var sum in buckets.Values) if (sum.w > best.w) best = sum;
                        colour = new Color(best.x / best.w, best.y / best.w, best.z / best.w);
                    }
                    int k = y * width + x; solid[k] = true;
                    pixels[k] = new Color(colour.r * job.tint.r, colour.g * job.tint.g, colour.b * job.tint.b, s.softEdges ? Mathf.Clamp01(a / (ss * ss)) : 1f);
                }
            // Sharpen: push each pixel away from its neighbours' average (inside the item only).
            if (s.sharpen > 0)
            {
                var sharp = (Color[])pixels.Clone();
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int k = y * width + x; if (!solid[k]) continue;
                        Color around = Color.clear; int n = 0;
                        if (x > 0 && solid[k - 1]) { around += pixels[k - 1]; n++; }
                        if (x < width - 1 && solid[k + 1]) { around += pixels[k + 1]; n++; }
                        if (y > 0 && solid[k - width]) { around += pixels[k - width]; n++; }
                        if (y < height - 1 && solid[k + width]) { around += pixels[k + width]; n++; }
                        if (n > 0) sharp[k] = pixels[k] + (pixels[k] - around / n) * s.sharpen;
                    }
                pixels = sharp;
            }
            for (int k = 0; k < pixels.Length; k++)
                if (solid[k]) pixels[k] = new Color(Mathf.Clamp01(pixels[k].r), Mathf.Clamp01(pixels[k].g), Mathf.Clamp01(pixels[k].b), pixels[k].a);
            for (int y = 0; y < height && s.drawOutline; y++)
                for (int x = 0; x < width; x++)
                {
                    int k = y * width + x; if (solid[k]) continue;
                    bool edge = x > 0 && solid[k - 1] || x < width - 1 && solid[k + 1] || y > 0 && solid[k - width] || y < height - 1 && solid[k + width];
                    if (edge) pixels[k] = s.outline;
                }
            var icon = new Texture2D(width, height, TextureFormat.RGBA32, false);
            icon.SetPixels(pixels); icon.Apply();
            return icon;
        }
    }

    // Only the model's visible meshes: no scripts, particles, colliders or lower LODs.
    static void Clean(GameObject model)
    {
        foreach (var lod in model.GetComponentsInChildren<LODGroup>(true))
        {
            var lods = lod.GetLODs();
            for (int i = 1; i < lods.Length; i++) foreach (var r in lods[i].renderers) if (r != null) r.enabled = false;
        }
        foreach (var p in model.GetComponentsInChildren<ParticleSystem>(true)) p.gameObject.SetActive(false);
        foreach (var b in model.GetComponentsInChildren<Behaviour>(true)) b.enabled = false;
    }
    // World-space vertices of every enabled mesh under root.
    static List<Vector3> Points(GameObject root)
    {
        var points = new List<Vector3>();
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            Mesh mesh = null;
            if (r is SkinnedMeshRenderer skinned) { mesh = new Mesh(); skinned.BakeMesh(mesh, true); }
            else if (r.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
            if (mesh == null) continue;
            foreach (var v in mesh.vertices) points.Add(r is SkinnedMeshRenderer ? r.transform.position + r.transform.rotation * v : r.transform.TransformPoint(v));
            if (r is SkinnedMeshRenderer) Object.DestroyImmediate(mesh);
        }
        return points;
    }
    static Bounds BoundsOf(List<Vector3> points)
    {
        var b = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) b.Encapsulate(p);
        return b;
    }
}

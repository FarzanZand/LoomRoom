using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Twelve tiers of gear. Worn armour is made from Synty Sidekick outfit parts: a helm, body armour, gloves, boots and
// leggings per tier, each tier one look coloured by one palette (Tools/gear_palettes.py; Sidekick parts share one palette layout, so
// a tier's palette colours parts from any of the packs). A piece's parts are baked in their bind pose, laid out for show
// (gloves fingers up side by side, boots as a pair), restyled in the pixel look at the dungeon's 128 texels a unit
// (PixelRestyle.Build, measured and baked again until within 5%) and saved as a prefab in Items/Prefabs/Equipment. The
// prefab is the item's floor model and its icon (ItemIconSource.model). Tiers 5-12 get item assets of their own the
// first time (copies of tier 4's) and their price, armour and health set from the tables here every run. Every piece
// keeps its Synty preset's own colours (its own palette, Tools/gear_palettes.py), and the tiers are named for how they
// look. Shields and
// weapons of tiers 5-12 are Synty models restyled the same way (BuildShields, BuildWeapons); tiers 1-4 keep their
// painted ones. Run GearSets.Build(), BuildShields() and BuildWeapons() from an eval; all safe to run again.
// Build("Grave") makes one tier, Build(null, "Boots") one slot of every tier.
public static class GearSets
{
    const string Parts = "Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits";
    const string Palettes = PixelRestyle.Root + "/Armour/Palettes";
    const string Art = PixelRestyle.Root + "/Armour";
    const string Prefabs = "Assets/Game/Items/Prefabs/Equipment";
    const string ArmorData = "Assets/Game/Items/Data/Armor";
    const string ShieldData = "Assets/Game/Items/Data/Shields";
    const float Texels = 128f;

    enum Slot { Helm, Armor, Gloves, Boots, Leggings }
    static readonly Dictionary<Slot, string[]> Codes = new()
    {
        [Slot.Helm] = new[] { "22AHED" },
        [Slot.Armor] = new[] { "10TORS", "29ASHL", "30ASHR" },
        [Slot.Gloves] = new[] { "15HNDL", "16HNDR", "13ALWL", "14ALWR" },
        [Slot.Boots] = new[] { "20FOTL", "21FOTR" },
        [Slot.Leggings] = new[] { "17HIPS", "18LEGL", "19LEGR", "33AKNL", "34AKNR" },
    };
    static EquipmentSlot SlotOf(Slot s) => s switch { Slot.Helm => EquipmentSlot.Head, Slot.Armor => EquipmentSlot.Body, Slot.Gloves => EquipmentSlot.Gloves, Slot.Boots => EquipmentSlot.Boots, _ => EquipmentSlot.Legs };

    // tier, name, the Sidekick part set (pack and number) for helm, body, gloves, boots and leggings.
    static readonly (int tier, string name, string[] parts)[] Tiers =
    {
        // Patched leather under a red hood.
        (1, "Leather", new[] { "FANT_SKTN_01", "VIKG_WARR_06", "VIKG_WARR_06", "VIKG_WARR_06", "VIKG_WARR_06" }),
        // Horned iron cap, mail under a wolf pelt, iron-shod boots.
        (2, "Iron", new[] { "VIKG_WARR_04", "VIKG_WARR_01", "VIKG_WARR_03", "VIKG_WARR_04", "VIKG_WARR_03" }),
        // A knight's visored helm, plate pauldrons over a tabard, plate greaves.
        (3, "Steel", new[] { "GOBL_FIGT_01", "GOBL_FIGT_05", "GOBL_FIGT_05", "GOBL_FIGT_05", "GOBL_FIGT_05" }),
        // Swept silver elven plate with gold trim.
        (4, "Elven", new[] { "ELVN_WARR_05", "ELVN_WARR_05", "ELVN_WARR_03", "ELVN_WARR_01", "ELVN_WARR_05" }),
        // A warlord's horned, crested helm and spiked iron with red cloth.
        (5, "Warlord", new[] { "GOBL_FIGT_02", "GOBL_FIGT_01", "GOBL_FIGT_01", "GOBL_FIGT_02", "GOBL_FIGT_01" }),
        // A green hood with gold trim and green elven plate.
        (6, "Verdant", new[] { "FANT_SKTN_03", "ELVN_WARR_04", "ELVN_WARR_03", "ELVN_WARR_03", "ELVN_WARR_03" }),
        // A pale hood and dark navy plate swirled with teal.
        (7, "Shadow", new[] { "FANT_SORC_08", "FANT_SORC_02", "FANT_SORC_02", "FANT_SORC_02", "FANT_SORC_02" }),
        // A death knight's spiked crown and dark spiked plate.
        (8, "Grave", new[] { "FANT_SKTN_04", "FANT_SKTN_01", "FANT_SKTN_01", "GOBL_FIGT_01", "FANT_SKTN_05" }),
        // A closed iron helm and blackened plate.
        (9, "Blackened", new[] { "VIKG_WARR_05", "VIKG_WARR_03", "VIKG_WARR_05", "VIKG_WARR_05", "VIKG_WARR_05" }),
        // Horned gold elven plate.
        (10, "Gilded", new[] { "ELVN_WARR_01", "ELVN_WARR_01", "ELVN_WARR_01", "ELVN_WARR_01", "ELVN_WARR_01" }),
        // A gold-fanged skull helm and spiked purple and gold plate.
        (11, "Dread", new[] { "FANT_SKTN_02", "FANT_SKTN_02", "FANT_SKTN_02", "FANT_SKTN_02", "FANT_SKTN_02" }),
        // A tall gold-crested helm and blue scale plate with gold.
        (12, "Royal", new[] { "ELVN_WARR_04", "ELVN_WARR_02", "ELVN_WARR_02", "ELVN_WARR_02", "ELVN_WARR_02" }),
    };
    // Prices by tier (pieces; body armour; weapons; shields), tiers 5-12 growing by about a third a tier from tier 4's.
    // A piece's armour is 4 a tier (body armour 4 more), a shield's 10 a tier plus 10, a sword's damage 3 a tier past
    // tier 4 and a mace's 1 more.
    static readonly int[] PiecePrice = { 15, 40, 90, 225, 300, 400, 550, 750, 1000, 1350, 1850, 2500 };
    static readonly int[] BodyPrice = { 35, 90, 210, 525, 700, 950, 1300, 1750, 2350, 3150, 4350, 5900 };
    static readonly int[] WeaponPrice = { 20, 50, 120, 300, 400, 550, 750, 1000, 1350, 1850, 2500, 3350 };
    static readonly int[] ShieldPrice = { 25, 60, 150, 375, 500, 675, 900, 1250, 1700, 2300, 3100, 4200 };
    static readonly int[] SwordDamage = { 4, 6, 8, 11, 14, 17, 20, 23, 26, 29, 32, 35 };
    static readonly int[] BodyHealth = { 5, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28 };
    // Boots: how far up the shin the set's legs are kept, a share of ankle to knee (0 = none: the foot part is already
    // a tall greave, and the trousers above it only showed as rags), and which sets are built from one boot and its
    // mirror, and from which side ("L" or "R": their two legs differ, and a pair of boots should match).
    static readonly Dictionary<string, float> BootShin = new() { ["Iron"] = .5f, ["Grave"] = 0f };
    // The parts a piece can't be made without (the others, such as shoulder pads and knee guards, only some sets have).
    static readonly Dictionary<Slot, string[]> Required = new()
    {
        [Slot.Helm] = new[] { "22AHED" }, [Slot.Armor] = new[] { "10TORS" }, [Slot.Gloves] = new[] { "15HNDL", "16HNDR" },
        [Slot.Boots] = new[] { "20FOTL", "21FOTR" }, [Slot.Leggings] = new[] { "17HIPS" },
    };
    const float DefaultShin = .72f;
    static readonly Dictionary<string, string> MirroredBoots = new() { ["Steel"] = "R" };

    public static string Build(string only = null, string slotOnly = null)
    {
        var log = new System.Text.StringBuilder();
        var items = new List<ItemData>();
        foreach (var (tier, name, parts) in Tiers)
        {
            if (only != null && only != name) continue;
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Palettes}/{name} palette.png");
            if (palette == null) { log.Append($"No palette for {name}: run Tools/gear_palettes.py\n"); continue; }
            PointFilter(palette);
            for (int s = 0; s < 5; s++)
            {
                var slot = (Slot)s;
                if (slotOnly != null && slotOnly != slot.ToString()) continue;
                string piece = $"{name} {slot}";
                // A piece can have a palette of its own ("<Tier> <Slot> palette.png", its preset's own colours); the
                // tier's is the fallback.
                var own = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Palettes}/{piece} palette.png");
                if (own != null) PointFilter(own);
                var prefab = BuildPiece(piece, name, parts[s], slot, own != null ? own : palette, log);
                if (prefab == null) continue;
                var item = ItemFor(tier, name, slot, log);
                if (item == null) continue;
                item.worldPrefab = prefab; item.pickupVisualPrefab = null;
                item.iconSource ??= new ItemIconSource();
                item.iconSource.model = prefab; item.iconSource.custom = false; item.iconSource.outfit = "";
                EditorUtility.SetDirty(item);
                items.Add(item);
            }
        }
        AssetDatabase.SaveAssets();
        ItemDatabaseAuthoring.SyncCatalog();
        var problems = new List<string>();
        int made = ItemIconRenderer.GenerateAll(items, problems);
        log.Append($"{made} icons\n");
        foreach (var p in problems) log.Append("  " + p + "\n");
        return log.ToString();
    }

    // A mesh asset baked again in this session keeps drawing its old shape until it is loaded again from disk (icons
    // came out with the old shape under the new texture), so each new one is unloaded and imported fresh.
    // The mesh is also named after its file first (a mesh named otherwise logs "Main Object Name does not match
    // filename" on every import).
    static void Reload(string meshPath)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        string file = Path.GetFileNameWithoutExtension(meshPath);
        if (mesh != null && mesh.name != file) { mesh.name = file; EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh); }
        if (mesh != null) Resources.UnloadAsset(mesh);
        AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceUpdate);
    }

    static void PointFilter(Texture2D texture)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
        if (importer.filterMode == FilterMode.Point && importer.isReadable && importer.textureCompression == TextureImporterCompression.Uncompressed && !importer.mipmapEnabled) return;
        importer.filterMode = FilterMode.Point; importer.isReadable = true; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // ── One piece ─────────────────────────────────────────────────────

    static GameObject BuildPiece(string piece, string tierName, string key, Slot slot, Texture2D palette, System.Text.StringBuilder log)
    {
        var mesh = Bake(key, slot, tierName, out string missing);
        if (mesh == null) { log.Append($"{piece}: no parts for {key} ({missing})\n"); return null; }
        mesh.name = piece;
        string dir = $"{Art}/{tierName}"; ItemDatabaseAuthoring.EnsureFolder(dir);
        var source = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        source.SetTexture("_BaseMap", palette); source.SetColor("_BaseColor", Color.white);
        string path = $"{Prefabs}/{piece}.prefab";
        float aim = Texels, measured = 0;
        GameObject prefab = null;
        for (int pass = 0; pass < 4; pass++)
        {
            var (restyled, material) = PixelRestyle.Build(mesh, new[] { source }, "Armour", piece, aim, dir);
            if (restyled == null) { log.Append($"{piece}: restyle failed\n"); return null; }
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
            prefab = SavePrefab(path, piece, restyled, material);
            measured = PixelRestyle.TexelsPerUnit(prefab.GetComponent<MeshRenderer>());
            if (measured <= 0 || Mathf.Abs(measured / Texels - 1f) < .05f) break;
            aim *= Texels / measured;
        }
        Object.DestroyImmediate(mesh); Object.DestroyImmediate(source);
        Reload($"{dir}/{piece}.asset");
        log.Append($"{piece}: {key}, {measured:F1} texels a unit\n");
        return prefab;
    }

    // The prefab: the restyled mesh, a box for pickups to be found by, the floor shine. An existing prefab keeps its
    // file (and GUID); its old children (the box placeholders) are replaced.
    static GameObject SavePrefab(string path, string name, Mesh mesh, Material material)
    {
        var root = new GameObject(name);
        try
        {
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            var box = root.AddComponent<BoxCollider>(); box.center = mesh.bounds.center; box.size = mesh.bounds.size;
            var shine = root.AddComponent<ItemShine>(); shine.surfaces = new Renderer[] { renderer };
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // The slot's parts of one set, baked in bind pose, laid out and joined into one mesh centred on the origin
    // (characters face +Z).
    static Mesh Bake(string key, Slot slot, string tierName, out string missing)
    {
        missing = null;
        var combine = new List<CombineInstance>();
        float shin = slot != Slot.Boots ? 0f : BootShin.TryGetValue(tierName, out float share) ? share : DefaultShin;
        string kept = slot == Slot.Boots && MirroredBoots.TryGetValue(tierName, out string mirrorSide) ? mirrorSide : null;
        bool mirrored = kept != null;
        var codes = shin > 0f ? Codes[slot].Concat(Shins) : Codes[slot];
        if (mirrored) codes = codes.Where(c => c.EndsWith(kept));
        foreach (var code in codes)
        {
            var files = Directory.GetFiles(Parts, $"SK_{key}_{code}_*.fbx", SearchOption.AllDirectories);
            if (files.Length == 0) { if (Required[slot].Contains(code)) missing = missing == null ? code : $"{missing}, {code}"; continue; }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(files[0].Replace('\\', '/'));
            var instance = (GameObject)Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var place = Layout(slot, code, instance.transform);
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skin.sharedMesh == null) continue;
                    var baked = new Mesh(); skin.BakeMesh(baked, true);
                    var matrix = place * Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                    string side = code.EndsWith("L") ? "_l" : "_r";
                    if (slot == Slot.Boots && Shins.Contains(code)) KeepShin(baked, matrix, instance.transform, side, shin);
                    for (int sub = 0; sub < baked.subMeshCount; sub++) combine.Add(new CombineInstance { mesh = baked, subMeshIndex = sub, transform = matrix });
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }
        if (missing != null || combine.Count == 0) { foreach (var c in combine) Object.DestroyImmediate(c.mesh); return null; }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(combine.ToArray(), true, true);
        foreach (var m in combine.Select(c => c.mesh).Distinct()) Object.DestroyImmediate(m);
        if (mirrored) Mirror(mesh);
        return Compact(mesh);
    }

    // Adds the mesh's mirror image across the character's middle (x = 0): one boot makes the other.
    static void Mirror(Mesh mesh)
    {
        var v = mesh.vertices; var n = mesh.normals; var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
        int count = v.Length;
        var pv = new List<Vector3>(v); var pn = new List<Vector3>(n); var puv = new List<Vector2>(uv);
        for (int i = 0; i < count; i++)
        {
            pv.Add(new Vector3(-v[i].x, v[i].y, v[i].z));
            pn.Add(n.Length > i ? new Vector3(-n[i].x, n[i].y, n[i].z) : Vector3.up);
            puv.Add(uv.Count > i ? uv[i] : Vector2.zero);
        }
        var subs = new List<int[]>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s); var both = new int[t.Length * 2];
            System.Array.Copy(t, both, t.Length);
            // A mirror turns faces inside out, so each copied triangle is wound the other way.
            for (int i = 0; i < t.Length; i += 3) { both[t.Length + i] = t[i] + count; both[t.Length + i + 1] = t[i + 2] + count; both[t.Length + i + 2] = t[i + 1] + count; }
            subs.Add(both);
        }
        mesh.SetVertices(pv); mesh.SetNormals(pn); mesh.SetUVs(0, puv);
        for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
    }

    // Only the vertices a triangle uses (Sidekick parts carry loose ones that would throw the bounds off), centred on
    // their bounds, so the floor and the icon turn the piece about its middle.
    static Mesh Compact(Mesh mesh)
    {
        var v = mesh.vertices; var n = mesh.normals; var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
        var map = new Dictionary<int, int>(); var pv = new List<Vector3>(); var pn = new List<Vector3>(); var puv = new List<Vector2>();
        var subs = new List<int[]>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var t = mesh.GetTriangles(s);
            for (int i = 0; i < t.Length; i++)
            {
                if (!map.TryGetValue(t[i], out int j))
                {
                    j = pv.Count; map[t[i]] = j;
                    pv.Add(v[t[i]]); pn.Add(n.Length > t[i] ? n[t[i]] : Vector3.up); puv.Add(uv.Count > t[i] ? uv[t[i]] : Vector2.zero);
                }
                t[i] = j;
            }
            subs.Add(t);
        }
        var centre = Vector3.zero;
        if (pv.Count > 0) { var b = new Bounds(pv[0], Vector3.zero); foreach (var p in pv) b.Encapsulate(p); centre = b.center; }
        for (int i = 0; i < pv.Count; i++) pv[i] -= centre;
        var result = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        result.SetVertices(pv); result.SetNormals(pn); result.SetUVs(0, puv);
        result.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) result.SetTriangles(subs[s], s);
        result.RecalculateBounds();
        Object.DestroyImmediate(mesh);
        return result;
    }

    // Boots are the feet and the set's own legs from below the knee down (the foot parts alone are shoes; the legs carry
    // on down inside them, as on a character), up to share of the way from ankle to knee (BootShin).
    static readonly string[] Shins = { "18LEGL", "19LEGR" };
    static void KeepShin(Mesh baked, Matrix4x4 matrix, Transform rig, string side, float share)
    {
        var knee = Find(rig, "calf" + side); var ankle = Find(rig, "foot" + side);
        if (knee == null || ankle == null) return;
        float top = ankle.position.y + (knee.position.y - ankle.position.y) * share;
        var v = baked.vertices;
        for (int s = 0; s < baked.subMeshCount; s++)
        {
            var t = baked.GetTriangles(s); var kept = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                float a = matrix.MultiplyPoint3x4(v[t[i]]).y, b = matrix.MultiplyPoint3x4(v[t[i + 1]]).y, c = matrix.MultiplyPoint3x4(v[t[i + 2]]).y;
                if (Mathf.Max(a, Mathf.Max(b, c)) < top) kept.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
            }
            baked.SetTriangles(kept, s);
        }
    }

    // Where a part goes. Gloves: each hand and forearm turned fingers up about the wrist, leaning out a little, the two
    // side by side; everything else as it stands.
    static Matrix4x4 Layout(Slot slot, string code, Transform rig)
    {
        if (slot != Slot.Gloves) return Matrix4x4.identity;
        bool left = code.EndsWith("L");
        string side = left ? "_l" : "_r";
        var wrist = Find(rig, "hand" + side); var elbow = Find(rig, "lowerarm" + side);
        if (wrist == null || elbow == null) return Matrix4x4.identity;
        var turn = Quaternion.Euler(0, 0, left ? -12 : 12) * Quaternion.FromToRotation(wrist.position - elbow.position, Vector3.up);
        var offset = new Vector3(left ? -.11f : .11f, 0, 0);
        return Matrix4x4.Translate(offset) * Matrix4x4.Rotate(turn) * Matrix4x4.Translate(-wrist.position);
    }
    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // ── Shields ───────────────────────────────────────────────────────

    // Tiers 1, 2 and 4 keep their painted shields; 3 (its painted one was a flat grey) and 5-12 are Synty shields
    // restyled at 128 texels a unit, their larger side as long as the Crystal Shield is tall (a round shield matched by
    // height came out far wider than the rest), held where it is held; turn is the hand's turn about the shield's up axis so its painted face
    // shows when raised (these packs model their shields facing the other way from Fantasy Kingdom's).
    static readonly (int tier, string name, string synty, float turn)[] Shields =
    {
        (3, "Steel", "Assets/Synty/PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Shield_04.prefab", -90),
        (5, "Warlord", "Assets/Synty/PolygonDungeon/Prefabs/Weapons/SM_Wep_Shield_Round_01.prefab", 90),
        (6, "Verdant", "Assets/Synty/PolygonDungeon/Prefabs/Weapons/SM_Wep_Shield_Chitin_01.prefab", 90),
        (7, "Shadow", "Assets/Synty/PolygonDarkFortress/Prefabs/Weapons/SM_Wep_Shield_01.prefab", 90),
        (8, "Grave", "Assets/Synty/PolygonDarkFantasy/Prefabs/Weapons/SM_Wep_Shield_01.prefab", 90),
        (9, "Blackened", "Assets/Synty/PolygonDungeonRealms/Prefabs/Weapons/SM_Wep_Shield_07.prefab", 90),
        (10, "Gilded", "Assets/Synty/PolygonDungeonRealms/Prefabs/Weapons/SM_Wep_Shield_01.prefab", 90),
        (11, "Dread", "Assets/Synty/PolygonDungeon/Prefabs/Weapons/SM_Wep_Shield_Skull_01.prefab", 90),
        (12, "Royal", "Assets/Synty/PolygonDungeonRealms/Prefabs/Weapons/SM_Wep_Shield_05.prefab", 90),
    };

    public static string BuildShields(string only = null)
    {
        var log = new System.Text.StringBuilder();
        var crystal = AssetDatabase.LoadAssetAtPath<ItemData>($"{ShieldData}/Crystal Shield.asset");
        float height = Height(crystal.worldPrefab);
        var items = new List<ItemData>();
        foreach (var (tier, name, synty, turn) in Shields)
        {
            if (only != null && only != name) continue;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(synty);
            if (source == null) { log.Append($"Missing {synty}\n"); continue; }
            var filter = source.GetComponentInChildren<MeshFilter>();
            var mats = filter.GetComponent<MeshRenderer>().sharedMaterials;
            string piece = $"{name} Shield", dir = $"{PixelRestyle.Root}/Shields/{filter.sharedMesh.name}";
            ItemDatabaseAuthoring.EnsureFolder(dir);
            string path = $"Assets/Game/Items/Prefabs/Shields/{piece}.prefab";
            var srcSize = Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale);
            float scale = height / Mathf.Max(.01f, Mathf.Max(srcSize.x, srcSize.y)), aim = Texels, measured = 0;
            // The size goes into the mesh, not the prefab: a held item is set to its held scale, and the root's would be lost.
            var sized = Sized(filter.sharedMesh, scale);
            GameObject prefab = null;
            for (int pass = 0; pass < 4; pass++)
            {
                var (mesh, material) = PixelRestyle.Build(sized, mats, "Shields", piece, aim, dir);
                if (mesh == null) break;
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                try
                {
                    instance.name = piece;
                    var f = instance.GetComponentInChildren<MeshFilter>(); f.sharedMesh = mesh;
                    f.GetComponent<MeshRenderer>().sharedMaterials = new[] { material };
                    // Synty's convex collider is the model's own size: a box of the restyled mesh instead.
                    foreach (var c in instance.GetComponentsInChildren<MeshCollider>(true)) Object.DestroyImmediate(c);
                    if (!f.TryGetComponent<BoxCollider>(out var box)) box = f.gameObject.AddComponent<BoxCollider>();
                    box.center = mesh.bounds.center; box.size = mesh.bounds.size;
                    prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
                }
                finally { Object.DestroyImmediate(instance); }
                measured = PixelRestyle.TexelsPerUnit(prefab.GetComponentInChildren<MeshRenderer>());
                if (measured <= 0 || Mathf.Abs(measured / Texels - 1f) < .05f) break;
                aim *= Texels / measured;
            }
            Object.DestroyImmediate(sized);
            if (prefab == null) { log.Append($"{piece}: restyle failed\n"); continue; }
            Reload($"{dir}/{piece}.asset");
            string dataPath = $"{ShieldData}/{piece}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(dataPath);
            if (item == null)
            {
                AssetDatabase.CopyAsset($"{ShieldData}/Crystal Shield.asset", dataPath);
                item = AssetDatabase.LoadAssetAtPath<ItemData>(dataPath);
                item.saveId = System.Guid.NewGuid().ToString("N"); item.icon = null;
                log.Append($"New item {piece}\n");
            }
            item.itemName = piece; item.tier = tier; item.tag = $"{name} equipment"; item.value = ShieldPrice[tier - 1];
            foreach (var m in item.statModifiers) if (m.stat == StatType.Armor) m.value = 10 * tier + 10;
            item.worldPrefab = prefab; item.pickupVisualPrefab = null;
            item.heldRotation = new Vector3(0, turn, 0);
            EditorUtility.SetDirty(item);
            items.Add(item);
            log.Append($"{piece}: {measured:F1} texels a unit, scale {scale:F2}\n");
        }
        AssetDatabase.SaveAssets();
        ItemDatabaseAuthoring.SyncCatalog();
        var problems = new List<string>();
        log.Append($"{ItemIconRenderer.GenerateAll(items, problems)} icons\n");
        foreach (var p in problems) log.Append("  " + p + "\n");
        return log.ToString();
    }
    static Mesh Sized(Mesh src, float scale)
    {
        var mesh = new Mesh { name = src.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(src.vertices.Select(v => v * scale).ToArray());
        mesh.SetNormals(src.normals);
        var uv = new List<Vector2>(); src.GetUVs(0, uv); mesh.SetUVs(0, uv);
        if (src.colors.Length == src.vertexCount) mesh.colors = src.colors;
        mesh.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s);
        mesh.RecalculateBounds();
        return mesh;
    }
    static float Height(GameObject prefab)
    {
        var b = new Bounds(); bool any = false;
        foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh; if (mesh == null) continue;
            var mb = mesh.bounds; var s = r.transform.lossyScale;
            var size = Vector3.Scale(mb.size, s);
            if (!any) { b = new Bounds(Vector3.zero, size); any = true; } else b.Encapsulate(new Bounds(Vector3.zero, size));
        }
        return any ? b.size.y : .8f;
    }

    // ── Items ─────────────────────────────────────────────────────────

    // The tier's item for the slot: tiers 1-4 already have one; 5-12 are copied from tier 4 once, with their own save
    // id, and get their name, tier, price, armour and health from the tables every run.
    static ItemData ItemFor(int tier, string name, Slot slot, System.Text.StringBuilder log)
    {
        string path = $"{ArmorData}/{name} {slot}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item != null && tier < 5) return item;
        if (item == null)
        {
            if (tier < 5) { log.Append($"Missing {path}\n"); return null; }
            if (!AssetDatabase.CopyAsset($"{ArmorData}/Elven {slot}.asset", path)) { log.Append($"Could not copy to {path}\n"); return null; }
            item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            item.saveId = System.Guid.NewGuid().ToString("N"); item.icon = null;
            log.Append($"New item {name} {slot}\n");
        }
        item.itemName = $"{name} {(slot == Slot.Armor ? "Armor" : slot.ToString())}";
        item.tier = tier; item.tag = $"{name} equipment";
        item.equipSlot = SlotOf(slot);
        item.value = slot == Slot.Armor ? BodyPrice[tier - 1] : PiecePrice[tier - 1];
        foreach (var m in item.statModifiers)
        {
            if (m.stat == StatType.Armor) m.value = 4 * tier + (slot == Slot.Armor ? 4 : 0);
            else if (m.stat == StatType.MaxHealth) m.value = BodyHealth[tier - 1];
        }
        EditorUtility.SetDirty(item);
        return item;
    }

    // ── Weapons ───────────────────────────────────────────────────────

    // Tiers 1-4 keep their painted swords and maces; 5-12 are Synty weapons restyled at 128 texels a unit, laid along
    // the tier 4 weapon of their kind (its line from the grip, and the flat of the blade turned as its is, so they sit
    // in the hand and along the hitbox as it does) and as long as it. Tier 8's mace is the Grave Maul, since the Crypt
    // Warden's Grave mace has that file name already.
    const string WeaponData = "Assets/Game/Items/Data/Weapons";
    static readonly (int tier, string name, string sword, string mace, string maceName)[] Weapons =
    {
        (5, "Warlord", "PolygonDarkFortress/Prefabs/Weapons/SM_Wep_Sword_02", "PolygonDungeon/Prefabs/Weapons/SM_Wep_Mace_Blades_01", null),
        (6, "Verdant", "PolygonDarkFortress/Prefabs/Weapons/SM_Wep_Sword_03", "PolygonDarkFortress/Prefabs/Weapons/SM_Wep_Hammer_01", null),
        (7, "Shadow", "PolygonDarkFantasy/Prefabs/Weapons/SM_Wep_Sword_01", "PolygonDarkFortress/Prefabs/Weapons/SM_Wep_Mace_02", null),
        (8, "Grave", "PolygonDarkFantasy/Prefabs/Weapons/SM_Wep_Sword_02", "PolygonDarkFantasy/Prefabs/Weapons/SM_Wep_Mace_01", "Grave Maul"),
        (9, "Blackened", "PolygonFantasyKingdom/Prefabs/Weapons/SM_Wep_Sword_17", "PolygonDarkFortress/Prefabs/Weapons/SM_Wep_Mace_01", null),
        (10, "Gilded", "PolygonDungeon/Prefabs/Weapons/SM_Wep_Ornate_Sword_01", "PolygonDungeonRealms/Prefabs/Weapons/SM_Wep_Mace_Medium_01", null),
        (11, "Dread", "PolygonDungeon/Prefabs/Weapons/SM_Wep_Crystal_Ornate_Straightsword_01", "PolygonDungeon/Prefabs/Weapons/SM_Wep_Goblin_Gem_Hammer_01", null),
        (12, "Royal", "PolygonDungeon/Prefabs/Weapons/SM_Wep_Ornate_Sword_02", "PolygonDungeonRealms/Prefabs/Weapons/SM_Wep_Mace_Large_02", null),
    };

    public static string BuildWeapons(string only = null)
    {
        var log = new System.Text.StringBuilder();
        var items = new List<ItemData>();
        foreach (var (tier, name, sword, mace, maceName) in Weapons)
        {
            if (only != null && only != name) continue;
            BuildWeapon(tier, $"{name} Sword", name, $"Assets/Synty/{sword}.prefab", "Crystal Sword", 0, log, items);
            BuildWeapon(tier, maceName ?? $"{name} Mace", name, $"Assets/Synty/{mace}.prefab", "Crystal Mace", 1, log, items);
        }
        AssetDatabase.SaveAssets();
        ItemDatabaseAuthoring.SyncCatalog();
        var problems = new List<string>();
        log.Append($"{ItemIconRenderer.GenerateAll(items, problems)} icons\n");
        foreach (var p in problems) log.Append("  " + p + "\n");
        return log.ToString();
    }

    static void BuildWeapon(int tier, string piece, string tierName, string synty, string reference, int extraDamage, System.Text.StringBuilder log, List<ItemData> items)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(synty);
        var model = AssetDatabase.LoadAssetAtPath<ItemData>($"{WeaponData}/{reference}.asset");
        if (source == null || model == null) { log.Append($"{piece}: missing {(source == null ? synty : reference)}\n"); return; }
        var (srcMesh, srcFrame, mats) = MeshOf(source);
        var (refMesh, refFrame, _) = MeshOf(model.worldPrefab);
        var (srcAlong, srcFlat, srcLength) = Line(srcMesh, srcFrame);
        var (refAlong, refFlat, refLength) = Line(refMesh, refFrame);
        // The source's line onto the reference's, then turned about it until the flats agree (a flat is a line, not an
        // arrow, so the smaller of the two turns).
        var turn = Quaternion.FromToRotation(srcAlong, refAlong);
        float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(turn * srcFlat, refAlong), Vector3.ProjectOnPlane(refFlat, refAlong), refAlong);
        if (roll > 90) roll -= 180; else if (roll < -90) roll += 180;
        turn = Quaternion.AngleAxis(roll, refAlong) * turn;
        float scale = refLength / Mathf.Max(.01f, srcLength);
        var placed = Placed(srcMesh, Matrix4x4.TRS(Vector3.zero, turn, Vector3.one * scale) * srcFrame);
        string dir = $"{PixelRestyle.Root}/Weapons/{srcMesh.name}", path = $"Assets/Game/Items/Prefabs/Weapons/{piece}.prefab";
        ItemDatabaseAuthoring.EnsureFolder(dir);
        float aim = Texels, measured = 0;
        GameObject prefab = null;
        for (int pass = 0; pass < 4; pass++)
        {
            var (mesh, material) = PixelRestyle.Build(placed, mats, "Weapons", piece, aim, dir);
            if (mesh == null) break;
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
            prefab = SavePrefab(path, piece, mesh, material);
            measured = PixelRestyle.TexelsPerUnit(prefab.GetComponent<MeshRenderer>());
            if (measured <= 0 || Mathf.Abs(measured / Texels - 1f) < .05f) break;
            aim *= Texels / measured;
        }
        Object.DestroyImmediate(placed);
        if (prefab == null) { log.Append($"{piece}: restyle failed\n"); return; }
        // A blade that glows at full strength washes itself and its icon out to white: its glow is held lower.
        var glow = prefab.GetComponent<MeshRenderer>().sharedMaterial;
        if (glow != null && glow.IsKeywordEnabled("_EMISSION")) { glow.SetColor("_EmissionColor", Color.white * .45f); EditorUtility.SetDirty(glow); }
        Reload($"{dir}/{piece}.asset");
        string dataPath = $"{WeaponData}/{piece}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(dataPath);
        if (item == null)
        {
            AssetDatabase.CopyAsset($"{WeaponData}/{reference}.asset", dataPath);
            item = AssetDatabase.LoadAssetAtPath<ItemData>(dataPath);
            item.saveId = System.Guid.NewGuid().ToString("N"); item.icon = null;
            log.Append($"New item {piece}\n");
        }
        item.itemName = piece; item.tier = tier; item.tag = $"{tierName} equipment"; item.value = WeaponPrice[tier - 1];
        foreach (var m in item.statModifiers) if (m.stat == StatType.AttackDamage) m.value = SwordDamage[tier - 1] + extraDamage;
        // The Crystal weapons' own light (+1 m light radius) came along with the copy; it is theirs alone.
        item.statModifiers = item.statModifiers.Where(m => m.stat != StatType.LightRadius).ToArray();
        item.worldPrefab = prefab; item.pickupVisualPrefab = null;
        item.heldPosition = Vector3.zero; item.heldRotation = Vector3.zero; item.heldScale = 1;
        item.iconSource ??= new ItemIconSource();
        item.iconSource.model = null; item.iconSource.custom = false;
        EditorUtility.SetDirty(item);
        items.Add(item);
        log.Append($"{piece}: {srcMesh.name}, {measured:F1} texels a unit, scale {scale:F2}, roll {roll:F0}\n");
    }

    // A prefab's first mesh, with the matrix that puts its vertices where a held copy draws them about its root (the
    // hand sets the root's place and turn but keeps its scale), and its materials.
    static (Mesh mesh, Matrix4x4 frame, Material[] mats) MeshOf(GameObject prefab)
    {
        var filter = prefab.GetComponentInChildren<MeshFilter>();
        var root = prefab.transform;
        var frame = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        return (filter.sharedMesh, frame, filter.GetComponent<MeshRenderer>().sharedMaterials);
    }

    // A weapon's line: the axis it is longest along, pointing from the grip (its pivot) toward the far end, its flat
    // (the axis it is thinnest along) and its length.
    static (Vector3 along, Vector3 flat, float length) Line(Mesh mesh, Matrix4x4 frame)
    {
        var v = mesh.vertices;
        var b = new Bounds(frame.MultiplyPoint3x4(v[0]), Vector3.zero);
        foreach (var p in v) b.Encapsulate(frame.MultiplyPoint3x4(p));
        int longest = 0, thinnest = 0;
        for (int i = 1; i < 3; i++) { if (b.size[i] > b.size[longest]) longest = i; if (b.size[i] < b.size[thinnest]) thinnest = i; }
        var along = Vector3.zero; along[longest] = Mathf.Abs(b.max[longest]) >= Mathf.Abs(b.min[longest]) ? 1 : -1;
        var flat = Vector3.zero; flat[thinnest] = 1;
        return (along, flat, b.size[longest]);
    }

    static Mesh Placed(Mesh src, Matrix4x4 m)
    {
        var mesh = new Mesh { name = src.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(src.vertices.Select(p => m.MultiplyPoint3x4(p)).ToArray());
        var normals = m.inverse.transpose;
        mesh.SetNormals(src.normals.Select(n => normals.MultiplyVector(n).normalized).ToArray());
        var uv = new List<Vector2>(); src.GetUVs(0, uv); mesh.SetUVs(0, uv);
        if (src.colors.Length == src.vertexCount) mesh.colors = src.colors;
        mesh.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s);
        mesh.RecalculateBounds();
        return mesh;
    }
}

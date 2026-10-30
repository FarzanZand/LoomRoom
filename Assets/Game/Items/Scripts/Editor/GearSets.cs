using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Six tiers of worn armour made from Synty Sidekick outfit parts: a helm, body armour, gloves, boots and leggings per
// tier, each tier one look coloured by one palette (Tools/gear_palettes.py; Sidekick parts share one palette layout, so
// a tier's palette colours parts from any of the packs). A piece's parts are baked in their bind pose, laid out for show
// (gloves fingers up side by side, boots as a pair), restyled in the pixel look at the dungeon's 128 texels a unit
// (PixelRestyle.Build, measured and baked again until within 5%) and saved as a prefab in Items/Prefabs/Equipment. The
// prefab is the item's floor model and its icon (ItemIconSource.model). Tiers 5 and 6 get item assets of their own the
// first time, and two shields of their own (Synty shields restyled the same way). Run GearSets.Build() from an eval;
// safe to run again. Build("Grave") makes one tier, Build(null, "Boots") one slot of every tier.
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
        // Patched leather and a hide hood.
        (1, "Leather", new[] { "FANT_SKTN_01", "VIKG_WARR_06", "VIKG_WARR_06", "VIKG_WARR_06", "VIKG_WARR_06" }),
        // Horned iron cap, mail under a wolf pelt, iron-shod boots.
        (2, "Iron", new[] { "VIKG_WARR_04", "VIKG_WARR_01", "VIKG_WARR_03", "VIKG_WARR_04", "VIKG_WARR_03" }),
        // A knight's visored helm, plate pauldrons over a tabard, plate greaves.
        (3, "Steel", new[] { "GOBL_FIGT_01", "GOBL_FIGT_05", "GOBL_FIGT_05", "GOBL_FIGT_05", "GOBL_FIGT_05" }),
        // Swept elven plate in ice blue.
        (4, "Crystal", new[] { "ELVN_WARR_05", "ELVN_WARR_05", "ELVN_WARR_03", "ELVN_WARR_01", "ELVN_WARR_05" }),
        // A death knight's spiked crown and blackened plate.
        (5, "Grave", new[] { "FANT_SKTN_04", "FANT_SKTN_01", "FANT_SKTN_01", "GOBL_FIGT_01", "FANT_SKTN_05" }),
        // Horned gold elven plate.
        (6, "Gilded", new[] { "ELVN_WARR_01", "ELVN_WARR_01", "ELVN_WARR_01", "GOBL_FIGT_04", "ELVN_WARR_01" }),
    };
    // Prices and armour by tier (pieces; body armour adds its own), continuing tiers 1-4.
    static readonly int[] PiecePrice = { 15, 40, 90, 225, 550, 1300 }, BodyPrice = { 35, 90, 210, 525, 1250, 3000 };
    static readonly int[] BodyHealth = { 5, 8, 10, 12, 14, 16 };

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
                var prefab = BuildPiece(piece, name, parts[s], slot, palette, log);
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
    static void Reload(string meshPath)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
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
        var mesh = Bake(key, slot, out string missing);
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
    static Mesh Bake(string key, Slot slot, out string missing)
    {
        missing = null;
        var combine = new List<CombineInstance>();
        var codes = slot == Slot.Boots ? Codes[slot].Concat(Shins) : Codes[slot];
        foreach (var code in codes)
        {
            var files = Directory.GetFiles(Parts, $"SK_{key}_{code}_*.fbx", SearchOption.AllDirectories);
            if (files.Length == 0) { if (code == Codes[slot][0]) missing = code; continue; }
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
                    if (slot == Slot.Boots && Shins.Contains(code)) KeepShin(baked, matrix, instance.transform, side);
                    for (int sub = 0; sub < baked.subMeshCount; sub++) combine.Add(new CombineInstance { mesh = baked, subMeshIndex = sub, transform = matrix });
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }
        if (missing != null || combine.Count == 0) { foreach (var c in combine) Object.DestroyImmediate(c.mesh); return null; }
        var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.CombineMeshes(combine.ToArray(), true, true);
        foreach (var m in combine.Select(c => c.mesh).Distinct()) Object.DestroyImmediate(m);
        return Compact(mesh);
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

    // Boots are the feet and the set's own legs from a little below the knee down (the foot parts alone are shoes; the
    // legs carry on down inside them, as on a character).
    static readonly string[] Shins = { "18LEGL", "19LEGR" };
    static void KeepShin(Mesh baked, Matrix4x4 matrix, Transform rig, string side)
    {
        var knee = Find(rig, "calf" + side); var ankle = Find(rig, "foot" + side);
        if (knee == null || ankle == null) return;
        float top = ankle.position.y + (knee.position.y - ankle.position.y) * .72f;
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

    // Tiers 1-4 keep their painted shields; 5 and 6 are Synty shields restyled at 128 texels a unit, as tall as the
    // Crystal Shield, held where it is held.
    static readonly (int tier, string name, string synty, int price, int armour)[] Shields =
    {
        (5, "Grave", "Assets/Synty/PolygonDarkFantasy/Prefabs/Weapons/SM_Wep_Shield_01.prefab", 900, 60),
        (6, "Gilded", "Assets/Synty/PolygonDungeon/Prefabs/Weapons/SM_Wep_Shield_Ornate_01.prefab", 2200, 70),
    };

    public static string BuildShields()
    {
        var log = new System.Text.StringBuilder();
        var crystal = AssetDatabase.LoadAssetAtPath<ItemData>($"{ShieldData}/Crystal Shield.asset");
        float height = Height(crystal.worldPrefab);
        var items = new List<ItemData>();
        foreach (var (tier, name, synty, price, armour) in Shields)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(synty);
            if (source == null) { log.Append($"Missing {synty}\n"); continue; }
            var filter = source.GetComponentInChildren<MeshFilter>();
            var mats = filter.GetComponent<MeshRenderer>().sharedMaterials;
            string piece = $"{name} Shield", dir = $"{PixelRestyle.Root}/Shields/{filter.sharedMesh.name}";
            ItemDatabaseAuthoring.EnsureFolder(dir);
            string path = $"Assets/Game/Items/Prefabs/Shields/{piece}.prefab";
            float scale = height / Mathf.Max(.01f, filter.sharedMesh.bounds.size.y * filter.transform.lossyScale.y), aim = Texels, measured = 0;
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
                item.itemName = piece; item.saveId = System.Guid.NewGuid().ToString("N");
                item.tier = tier; item.tag = $"{name} equipment"; item.icon = null; item.value = price;
                foreach (var m in item.statModifiers) if (m.stat == StatType.Armor) m.value = armour;
                log.Append($"New item {piece}\n");
            }
            item.worldPrefab = prefab; item.pickupVisualPrefab = null;
            // These packs model their shields facing the other way from Fantasy Kingdom's: turned round in the hand so
            // the painted face shows when raised, as on the other shields.
            item.heldRotation = new Vector3(0, 90, 0);
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

    // The tier's item for the slot: tiers 1-4 already have one; 5 and 6 are copied from tier 4 once, with their own
    // save id, name, price and armour.
    static ItemData ItemFor(int tier, string name, Slot slot, System.Text.StringBuilder log)
    {
        string path = $"{ArmorData}/{name} {slot}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item != null) return item;
        if (tier < 5) { log.Append($"Missing {path}\n"); return null; }
        if (!AssetDatabase.CopyAsset($"{ArmorData}/Crystal {slot}.asset", path)) { log.Append($"Could not copy to {path}\n"); return null; }
        item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        item.itemName = $"{name} {(slot == Slot.Armor ? "Armor" : slot.ToString())}";
        item.saveId = System.Guid.NewGuid().ToString("N");
        item.tier = tier; item.tag = $"{name} equipment"; item.icon = null;
        item.equipSlot = SlotOf(slot);
        item.value = slot == Slot.Armor ? BodyPrice[tier - 1] : PiecePrice[tier - 1];
        foreach (var m in item.statModifiers)
        {
            if (m.stat == StatType.Armor) m.value = 4 * tier + (slot == Slot.Armor ? 4 : 0);
            else if (m.stat == StatType.MaxHealth) m.value = BodyHealth[tier - 1];
        }
        EditorUtility.SetDirty(item);
        log.Append($"New item {item.itemName}\n");
        return item;
    }
}

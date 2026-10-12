using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Heathen's furnishing: props in groups (a barrel with candles burning on it, barrels huddled together, crates with
// sacks, a shelf with pots), bigger and warmer barrels, and cheap loot lying about (DungeonFloorLoot with a skull, a
// candle, a bottle: Items/Data/Generic, worth a few coins). Built from the painted props; the groups' breakables get
// loot like any other (DungeonGenerator.SeedGroup). Adds the furnishing rules to Dungeon1. Safe to run again.
public static class DungeonPropGroups
{
    const string Prefabs = "Assets/Game/Levels/Dungeon1/Prefabs";
    const string Groups = Prefabs + "/Groups";
    const string ItemFolder = "Assets/Game/Items/Data/Generic";
    const string ItemPrefabs = "Assets/Game/Items/Prefabs/Valuables";

    [MenuItem("Tools/LoomRoom/Dungeon/Build Prop Groups and Floor Loot")]
    public static string Run()
    {
        var log = new System.Text.StringBuilder();
        if (!AssetDatabase.IsValidFolder(Groups)) AssetDatabase.CreateFolder(Prefabs, "Groups");
        if (!AssetDatabase.IsValidFolder(ItemPrefabs)) AssetDatabase.CreateFolder("Assets/Game/Items/Prefabs", "Valuables");
        WarmerBarrels(log);
        var groups = BuildGroups(log);
        var loot = BuildFloorLoot(log);
        AddRules(groups, loot, log);
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

    // Barrels and crates a size up (Heathen's are big), the barrel's paint a shade warmer.
    static void WarmerBarrels(System.Text.StringBuilder log)
    {
        foreach (var (name, scale) in new[] { ("Breakable barrel", 1.2f), ("Breakable crate", 1.1f) })
        {
            string path = $"{Prefabs}/Props/{name}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try { root.transform.localScale = Vector3.one * scale; PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        var barrel = Load($"{Prefabs}/Props/Breakable barrel.prefab");
        var mat = barrel.GetComponentInChildren<MeshRenderer>().sharedMaterial;
        if (mat != null && mat.HasProperty("_BaseColor")) { mat.SetColor("_BaseColor", new Color(1.08f, 1f, .9f)); EditorUtility.SetDirty(mat); }
        log.AppendLine("barrels x1.2 and warmer, crates x1.1");
    }

    // Candles (with their flames) from the altar, set on top of things, with a small warm light.
    static void Candles(Transform on, float top, float spread)
    {
        var altar = (GameObject)PrefabUtility.InstantiatePrefab(Load($"{Prefabs}/Features/Altar.prefab"));
        try
        {
            int i = 0;
            foreach (var name in new[] { "SM_Prop_Candle_05", "SM_Prop_Candle_03" })
            {
                var source = altar.transform.Find(name); if (source == null) continue;
                var candle = Object.Instantiate(source.gameObject, on); candle.name = name;
                candle.transform.localPosition = new Vector3((i == 0 ? -1 : 1) * spread, top, (i == 0 ? .05f : -.08f) * spread * 3f);
                candle.transform.localRotation = Quaternion.identity;
                i++;
            }
        }
        finally { Object.DestroyImmediate(altar); }
        var glow = new GameObject("Candle light"); glow.transform.SetParent(on, false); glow.transform.localPosition = new Vector3(0f, top + .35f, 0f);
        var light = glow.AddComponent<Light>(); light.type = LightType.Point; light.color = new Color(1f, .7f, .4f); light.range = 3.2f; light.intensity = 1.6f; light.shadows = LightShadows.None;
    }

    static Transform Put(Transform parent, string prefabPath, Vector3 pos, float yaw = 0f, float scale = 1f)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(Load(prefabPath), parent);
        go.transform.localPosition = pos; go.transform.localEulerAngles = new Vector3(0f, yaw, 0f); go.transform.localScale *= scale;
        return go.transform;
    }

    static GameObject Save(GameObject root)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Groups}/{root.name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    static Dictionary<string, GameObject> BuildGroups(System.Text.StringBuilder log)
    {
        var made = new Dictionary<string, GameObject>();
        string barrel = $"{Prefabs}/Props/Breakable barrel.prefab", crate = $"{Prefabs}/Props/Breakable crate.prefab", pot = $"{Prefabs}/Props/Clay pot.prefab";

        var g = new GameObject("Barrel with candles");
        var b = Put(g.transform, barrel, Vector3.zero, 20f);
        Candles(b, 1.05f, .18f);   // on the barrel itself, so they go when it breaks
        made[g.name] = Save(g);

        g = new GameObject("Barrels huddled");
        Put(g.transform, barrel, new Vector3(-.45f, 0f, -.2f), 10f); Put(g.transform, barrel, new Vector3(.5f, 0f, -.25f), 70f); Put(g.transform, barrel, new Vector3(.05f, 0f, .55f), 140f, .9f);
        made[g.name] = Save(g);

        g = new GameObject("Crates and sacks");
        var low = Put(g.transform, crate, new Vector3(-.3f, 0f, 0f), 5f);
        Put(g.transform, crate, new Vector3(-.3f, 1.0f, .02f), 28f, .8f);
        Put(g.transform, $"{Prefabs}/Furnishing/Sacks.prefab", new Vector3(.75f, 0f, .15f), 80f);
        made[g.name] = Save(g);

        g = new GameObject("Shelf with pots");
        Put(g.transform, $"{Prefabs}/Furnishing/Storage shelf.prefab", Vector3.zero);
        Put(g.transform, pot, new Vector3(-.4f, 1.87f, -.5f), 30f, .8f); Put(g.transform, pot, new Vector3(.35f, 1.87f, -.45f), 110f, .7f);
        Put(g.transform, pot, new Vector3(.95f, 0f, -.3f), 0f);
        made[g.name] = Save(g);

        log.AppendLine("groups: " + string.Join(", ", made.Keys));
        return made;
    }

    // An item worth a few coins, its world model the painted prop's mesh.
    static ItemData Valuable(string name, string saveId, int value, string description, string meshPath, float scale)
    {
        string path = $"{ItemFolder}/{name}.asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item == null) { item = ScriptableObject.CreateInstance<ItemData>(); AssetDatabase.CreateAsset(item, path); }
        item.itemName = name; item.saveId = saveId; item.itemType = ItemType.Generic; item.value = value; item.maxStackSize = 5;
        item.tier = 1; item.lootWeight = 0; item.description = description;
        string prefabPath = $"{ItemPrefabs}/{name}.prefab";
        var root = new GameObject(name);
        var model = new GameObject("Model"); model.transform.SetParent(root.transform, false); model.transform.localScale = Vector3.one * scale;
        model.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        model.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.ChangeExtension(meshPath, ".mat"));
        item.worldPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        EditorUtility.SetDirty(item);
        try { ItemIconRenderer.Generate(item); } catch (System.Exception e) { Debug.LogWarning("icon for " + name + ": " + e.Message); }
        return item;
    }

    static GameObject BuildFloorLoot(System.Text.StringBuilder log)
    {
        const string art = "Assets/Game/Art/Synty Pixel/Props";
        var skull = Valuable("Yellowed skull", "valuable_skull", 6, "An old skull, yellow with age. Someone will buy it.", $"{art}/SM_Prop_Skull_01/SM_Prop_Skull_01 painted.asset", 1f);
        var candle = Valuable("Tallow candle", "valuable_candle", 3, "A stub of tallow candle.", $"{art}/SM_Prop_Candle_03/SM_Prop_Candle_03 painted.asset", 1f);
        var bottle = Valuable("Dusty bottle", "valuable_bottle", 5, "An empty bottle, thick with dust.", $"{art}/SM_Gen_Prop_Bottle_02/SM_Gen_Prop_Bottle_02.asset", 1f);
        EditorApplication.ExecuteMenuItem("Tools/LoomRoom/Sync Item Catalog");

        var root = new GameObject("Floor valuables");
        var spawner = root.AddComponent<DungeonFloorLoot>();
        spawner.items = new[] { new DungeonFloorLoot.Choice { item = skull, weight = 1 }, new DungeonFloorLoot.Choice { item = candle, weight = 1.5f }, new DungeonFloorLoot.Choice { item = bottle, weight = 1.2f } };
        spawner.chance = .6f; spawner.count = new Vector2Int(1, 2);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{Prefabs}/Props/Floor valuables.prefab");
        Object.DestroyImmediate(root);
        log.AppendLine("floor loot: skull, candle, bottle");
        return prefab;
    }

    static void AddRules(Dictionary<string, GameObject> groups, GameObject floorLoot, System.Text.StringBuilder log)
    {
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>("Assets/Game/Levels/Dungeon1/Dungeon1.asset");
        var rules = new List<DungeonPropRule>(level.propRules);
        rules.RemoveAll(r => r == null || r.prefab == null || r.prefab == floorLoot || groups.ContainsValue(r.prefab));
        void Rule(GameObject prefab, DungeonPropPlacement placement, float per10, Vector2Int perRoom, float chance) =>
            rules.Add(new DungeonPropRule { prefab = prefab, placement = placement, footprint = Vector2Int.one, perTenCells = per10, perRoom = perRoom, chance = chance });
        Rule(groups["Barrel with candles"], DungeonPropPlacement.Corner, .12f, new Vector2Int(0, 1), .75f);
        Rule(groups["Barrels huddled"], DungeonPropPlacement.Corner, .1f, new Vector2Int(0, 1), .6f);
        Rule(groups["Crates and sacks"], DungeonPropPlacement.AgainstWall, .12f, new Vector2Int(0, 2), .7f);
        Rule(groups["Shelf with pots"], DungeonPropPlacement.AgainstWall, .1f, new Vector2Int(0, 1), .6f);
        Rule(floorLoot, DungeonPropPlacement.Anywhere, .2f, new Vector2Int(0, 2), 1f);
        level.propRules = rules.ToArray();
        EditorUtility.SetDirty(level);
        log.AppendLine("Dungeon1 furnishing rules: " + level.propRules.Length);
    }
}

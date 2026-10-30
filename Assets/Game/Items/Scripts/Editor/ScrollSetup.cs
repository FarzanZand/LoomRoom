using UnityEditor;
using UnityEngine;

// Makes a spell scroll (ItemData.IsScroll: a consumable that casts its spell with no mana, one charge a reading): the
// item in Items/Data/Consumables, a copy of the Mana Potion's pickup and use settings; its world model, a pixel copy of a
// Synty scroll at the items' 128 texels a unit (PixelRestyle.Build), shared by every scroll; and its bag icon. Run from
// an eval: ScrollSetup.Make("Flick", 1), and ScrollSetup.StartingChest("Flick") puts it in Dungeon1's starting chest.
// Safe to run again: the item keeps its save id and its numbers are set again.
public static class ScrollSetup
{
    const string Model = "Assets/Synty/PolygonDarkFortress/Prefabs/Props/SM_Prop_Scroll_01.prefab";   // a single rolled scroll on its rod
    const string Prefab = "Assets/Game/Items/Prefabs/Consumables/Scroll.prefab";
    const string Data = "Assets/Game/Items/Data/Consumables/";
    public static Vector3 IconTurn = new(0f, 0f, -45f);
    const string Dungeon1 = "Assets/Game/Levels/Dungeon1/Dungeon1.asset";

    public static string Make(string spell, int charges, int value = 15)
    {
        var definition = AssetDatabase.LoadAssetAtPath<SpellDefinition>($"Assets/Game/Combat/Spells/{spell}/{spell}.asset");
        if (definition == null) return "No spell " + spell;
        string name = "Scroll of " + definition.displayName, path = Data + name + ".asset";
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item == null)
        {
            AssetDatabase.CopyAsset(Data + "Mana Potion.asset", path);
            item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            item.saveId = System.Guid.NewGuid().ToString("N");
        }
        item.itemName = name;
        item.itemType = ItemType.Consumable;
        item.scrollSpell = definition;
        // Readied in the left hand in place of the shield, as a spell tome is (InventoryManager.ReadScroll).
        item.canBeEquipped = true;
        item.equipSlot = EquipmentSlot.LeftHand;
        item.charges = Mathf.Max(1, charges);
        item.maxStackSize = 1;
        item.gridSize = Vector2Int.zero;
        item.rarity = ItemRarity.Rare;
        item.value = value;
        item.effects = new EffectEntry[0];
        item.useMessage = "";
        item.AnimationOnUse = ItemUseAnimation.Instant;
        item.useAudioSource = ItemAudioSource.AudioClip;
        item.useClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/ThirdParty/Sound Effects Library/Items/Itm_Book_Pageturn_01.wav");
        item.useClipVolume = .8f;
        item.description = "";
        item.icon = null;
        // Its icon: the scroll lying across its slot corner to corner (seen end on otherwise).
        item.iconSource ??= new ItemIconSource();
        item.iconSource.turn = IconTurn;
        item.worldPrefab = WorldModel();
        item.pickupVisualPrefab = null;
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssets();
        string icon = ItemIconRenderer.Generate(item);
        int catalog = ItemDatabaseAuthoring.SyncCatalog();
        return $"{name}: {item.charges} charge(s), icon {icon}, catalog {catalog} items";
    }

    // The scroll's world model: the Synty scroll's mesh as a pixel copy, under a plain root.
    static GameObject WorldModel()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
        if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var filter = source.GetComponentInChildren<MeshFilter>();
        var renderer = source.GetComponentInChildren<MeshRenderer>();
        var (mesh, material) = PixelRestyle.Build(filter.sharedMesh, renderer.sharedMaterials, "Items", filter.sharedMesh.name, PixelRestyle.ItemTexelsPerUnit);
        var root = new GameObject("Scroll");
        try
        {
            var model = new GameObject(filter.sharedMesh.name);
            model.transform.SetParent(root.transform, false);
            model.transform.localRotation = filter.transform.localRotation;
            model.transform.localScale = filter.transform.lossyScale;
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterial = material;
            return PrefabUtility.SaveAsPrefabAsset(root, Prefab);
        }
        finally { Object.DestroyImmediate(root); }
    }

    // In Dungeon1's starting chest, beside its potion.
    public static string StartingChest(string spell)
    {
        var definition = AssetDatabase.LoadAssetAtPath<SpellDefinition>($"Assets/Game/Combat/Spells/{spell}/{spell}.asset");
        var item = definition != null ? AssetDatabase.LoadAssetAtPath<ItemData>(Data + "Scroll of " + definition.displayName + ".asset") : null;
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(Dungeon1);
        if (item == null || level == null) return "No scroll or no Dungeon1";
        level.startingChestItems ??= new System.Collections.Generic.List<ItemData>();
        if (!level.startingChestItems.Contains(item)) level.startingChestItems.Add(item);
        EditorUtility.SetDirty(level);
        AssetDatabase.SaveAssets();
        return $"Dungeon1's starting chest: {level.startingChestConsumable?.DisplayName} and {string.Join(", ", level.startingChestItems.ConvertAll(i => i != null ? i.DisplayName : "-"))}";
    }
}

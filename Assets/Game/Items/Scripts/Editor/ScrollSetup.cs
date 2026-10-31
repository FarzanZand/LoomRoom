using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Makes a spell scroll (ItemData.IsScroll: a consumable that casts its spell with no mana, one charge a reading): the
// item in Items/Data/Consumables, a copy of the Mana Potion's pickup and use settings; its world model, a pixel copy of a
// Synty scroll at the items' 128 texels a unit (PixelRestyle.Build), shared by every scroll; and its icon, the spell's
// icon with a small scroll badge in its bottom-left corner (Icon), so it reads as a scroll and not the tome. Run from
// an eval: ScrollSetup.Make("Flick", 1), and ScrollSetup.StartingChest("Flick") puts it in Dungeon1's starting chest.
// Safe to run again: the item keeps its save id and its numbers are set again.
public static class ScrollSetup
{
    const string Model = "Assets/Synty/PolygonDarkFortress/Prefabs/Props/SM_Prop_Scroll_01.prefab";   // a single rolled scroll on its rod
    const string Prefab = "Assets/Game/Items/Prefabs/Consumables/Scroll.prefab";
    const string Data = "Assets/Game/Items/Data/Consumables/";
    public static Vector3 IconTurn = new(0f, 0f, -45f);
    const string Badge = "Assets/Game/Items/Icons/Scroll badge.png";
    const string IconFolder = "Assets/Game/Items/Icons/Scrolls";
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
        item.worldPrefab = WorldModel();
        item.pickupVisualPrefab = null;
        // Its icon: the spell's own icon with the scroll badge in the corner (custom, so the icon renderer leaves it).
        var old = item.icon;
        item.icon = Icon(definition, name);
        item.iconSource ??= new ItemIconSource();
        item.iconSource.custom = item.icon != null;
        item.iconSource.turn = IconTurn;
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssets();
        string icon = item.icon != null ? AssetDatabase.GetAssetPath(item.icon) : "rendered: " + (ItemIconRenderer.Generate(item) ?? "ok");
        // The rendered scroll icon it had before, if nothing else uses it.
        if (old != null && old != item.icon && AssetDatabase.GetAssetPath(old).StartsWith("Assets/Game/Items/Icons/Generated/") &&
            !ItemDatabaseAuthoring.All().Any(i => i != item && i.icon == old))
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(old));
        int catalog = ItemDatabaseAuthoring.SyncCatalog();
        return $"{name}: {item.charges} charge(s), icon {icon}, catalog {catalog} items";
    }

    // The spell's icon with the scroll badge (Tools/scroll_badge.py) laid over its bottom-left corner on the icon's own
    // pixel grid (the hotbar's key number is top left, the charges bottom right). Written to Items/Icons/Scrolls.
    static Sprite Icon(SpellDefinition spell, string name)
    {
        if (spell.icon == null) return null;
        var source = Load(AssetDatabase.GetAssetPath(spell.icon.texture));
        var badge = Load(Badge);
        if (source == null || badge == null) return null;
        var rect = spell.icon.textureRect;
        int x0 = Mathf.RoundToInt(rect.x), y0 = Mathf.RoundToInt(rect.y), w = Mathf.RoundToInt(rect.width), h = Mathf.RoundToInt(rect.height);
        int scale = Mathf.Max(1, Mathf.RoundToInt(w / 32f));   // the badge is drawn for 32 x 32 icons
        var icon = new Texture2D(w, h, TextureFormat.RGBA32, false);
        icon.SetPixels(source.GetPixels(x0, y0, w, h));
        for (int y = 0; y < badge.height; y++)
            for (int x = 0; x < badge.width; x++)
            {
                var c = badge.GetPixel(x, y);
                if (c.a <= 0f) continue;
                for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        if (x * scale + dx < w && y * scale + dy < h) icon.SetPixel(x * scale + dx, y * scale + dy, c);
            }
        ItemDatabaseAuthoring.EnsureFolder(IconFolder);
        string path = $"{IconFolder}/{name}.png";
        File.WriteAllBytes(path, icon.EncodeToPNG());
        Object.DestroyImmediate(icon); Object.DestroyImmediate(source); Object.DestroyImmediate(badge);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = w;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // A PNG's own pixels, whatever its import settings.
    static Texture2D Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
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

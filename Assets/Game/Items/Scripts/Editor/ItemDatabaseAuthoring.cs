using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The ItemData assets are the database. Moves preserve their GUIDs and references.
public static class ItemDatabaseAuthoring
{
    public const string Root = "Assets/Game/Items/Data";
    public static bool IsArchived(ItemData item) => AssetDatabase.GetAssetPath(item).Contains("/_Archive/");
    public static List<ItemData> All() => AssetDatabase.FindAssets("t:ItemData")
        .Select(g => AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g)))
        .Where(i => i != null).OrderBy(i => i.itemName, StringComparer.OrdinalIgnoreCase).ToList();
    public static string Folder(ItemType type) => Root + "/" + (type switch
    {
        ItemType.Weapon => "Weapons", ItemType.Shield => "Shields",
        ItemType.Equipment => "Armor", ItemType.Consumable => "Consumables",
        ItemType.Tool => "Tools", ItemType.Key => "Keys", _ => "Generic"
    });
    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    public static ItemData Create(ItemType type)
    {
        string folder = Folder(type); EnsureFolder(folder);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/New " + type + ".asset");
        var item = ScriptableObject.CreateInstance<ItemData>();
        item.itemName = Path.GetFileNameWithoutExtension(path); item.itemType = type;
        item.canBeEquipped = type == ItemType.Weapon || type == ItemType.Shield || type == ItemType.Equipment;
        item.equipSlot = type == ItemType.Shield ? EquipmentSlot.LeftHand : type == ItemType.Equipment ? EquipmentSlot.Head : EquipmentSlot.RightHand;
        AssetDatabase.CreateAsset(item, path); Save(); return item;
    }
    public static ItemData Duplicate(ItemData source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.GetDirectoryName(sourcePath).Replace('\\', '/') + "/" + source.name + " copy.asset");
        if (!AssetDatabase.CopyAsset(sourcePath, path)) throw new IOException("Could not duplicate " + sourcePath);
        var copy = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        var names = new HashSet<string>(All().Where(i => i != copy).Select(i => i.itemName), StringComparer.OrdinalIgnoreCase);
        string name = source.itemName + " copy"; int suffix = 2;
        while (names.Contains(name)) name = source.itemName + " copy " + suffix++;
        copy.itemName = name; EditorUtility.SetDirty(copy); Save(); return copy;
    }
    public static string Destination(ItemData item) => Folder(item.itemType) + "/" + Path.GetFileName(AssetDatabase.GetAssetPath(item));
    public static void Move(ItemData item, string destination)
    {
        string from = AssetDatabase.GetAssetPath(item);
        if (from == destination) return;
        EnsureFolder(Path.GetDirectoryName(destination).Replace('\\', '/'));
        string error = AssetDatabase.MoveAsset(from, AssetDatabase.GenerateUniqueAssetPath(destination));
        if (!string.IsNullOrEmpty(error)) throw new IOException(error);
    }
    public static List<string> Issues(ItemData item, IReadOnlyList<ItemData> all)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(item.itemName)) issues.Add("Set an item name.");
        else if (all.Any(i => i != item && !IsArchived(i) && string.Equals(i.itemName, item.itemName, StringComparison.OrdinalIgnoreCase)))
            issues.Add("Another active item has this name. Name-based lookups would be ambiguous.");
        if (item.icon == null) issues.Add("Choose an inventory icon.");
        if (item.maxStackSize < 1) issues.Add("Stack size must be at least one.");
        bool hand = item.equipSlot == EquipmentSlot.RightHand || item.equipSlot == EquipmentSlot.LeftHand;
        if (item.canBeEquipped && hand && item.worldPrefab == null) issues.Add("This hand item needs a World Prefab to be visible while held. The pouch is a pickup fallback only.");
        if (item.IsConsumable && (item.effects == null || !item.effects.Any(e => e != null && e.trigger == EffectTrigger.OnUse)))
            issues.Add("This consumable has no OnUse effect.");
        if (item.effects != null && item.effects.Any(e => e != null && e.type == EffectType.FoodRegen && (e.value <= 0 || e.duration <= 0)))
            issues.Add("Food regeneration needs a positive rate and duration.");
        return issues;
    }
    public static void Save()
    {
        var previous = EditorSettings.serializationMode;
        try { EditorSettings.serializationMode = SerializationMode.ForceText; AssetDatabase.SaveAssets(); }
        finally { EditorSettings.serializationMode = previous; }
    }
    public static int SyncCatalog()
    {
        var items = All().Where(i => !IsArchived(i)).ToList();
        const string path = "Assets/Game/Core/Prefabs/InventoryManager.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        var previous = EditorSettings.serializationMode;
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            root.GetComponent<InventoryManager>().itemCatalog = items;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); EditorSettings.serializationMode = previous; }
        foreach (var manager in UnityEngine.Object.FindObjectsByType<InventoryManager>(FindObjectsInactive.Include))
        {
            Undo.RecordObject(manager, "Sync item catalog");
            manager.itemCatalog = new List<ItemData>(items);
            EditorUtility.SetDirty(manager); PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
        }
        Save(); return items.Count;
    }
}

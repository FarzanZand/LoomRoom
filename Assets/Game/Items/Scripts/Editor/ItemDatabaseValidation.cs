using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ItemDatabaseValidation
{
    static ItemDatabaseValidation() => EditorApplication.update += Tick;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    [MenuItem("Tools/Items/Validate item database")]
    static void Request() => File.WriteAllText("Temp/ItemDatabaseValidation.request", "run");
    static void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/ItemDatabaseValidation.request")) return;
        File.Delete("Temp/ItemDatabaseValidation.request");
        string folder = "Assets/Game/Items/Data/_DatabaseValidation_" + Guid.NewGuid().ToString("N");
        var previous = EditorSettings.serializationMode;
        EditorSettings.serializationMode = SerializationMode.ForceText;
        try
        {
            ItemDatabaseAuthoring.EnsureFolder(folder);
            var item = ScriptableObject.CreateInstance<ItemData>();
            item.itemName = "Database validation"; item.itemType = ItemType.Consumable;
            AssetDatabase.CreateAsset(item, folder + "/Source.asset");
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item));
            var holder = new GameObject("Reference check"); var pickup = holder.AddComponent<WorldItem>();
            var serialized = new SerializedObject(pickup);
            var reference = serialized.FindProperty("itemData");
            Check(reference != null, "WorldItem item reference not found");
            reference.objectReferenceValue = item; serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(holder, folder + "/Reference.prefab");
            UnityEngine.Object.DestroyImmediate(holder);
            var copy = ItemDatabaseAuthoring.Duplicate(item);
            var copy2 = ItemDatabaseAuthoring.Duplicate(item);
            Check(copy != item && copy.itemName != item.itemName && copy2.itemName != copy.itemName, "Duplicate names or identities collide");
            ItemDatabaseAuthoring.Move(item, folder + "/Moved/Source.asset");
            Check(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)) == guid, "Move changed item GUID");
            AssetDatabase.ImportAsset(folder + "/Reference.prefab", ImportAssetOptions.ForceUpdate);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/Reference.prefab");
            Check(new SerializedObject(saved.GetComponent<WorldItem>()).FindProperty("itemData").objectReferenceValue == item, "Move broke prefab reference");
            Undo.RecordObject(item, "Database validation edit"); item.description = "Changed"; Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Check(item.description != "Changed", "Item editing cannot be undone");
            Check(ItemDatabaseAuthoring.Issues(item, ItemDatabaseAuthoring.All()).Any(s => s.Contains("icon")), "Missing icon not reported");
            item.canBeEquipped = false;
            Check(!ItemDatabaseAuthoring.Issues(item, ItemDatabaseAuthoring.All()).Any(s => s.Contains("World Prefab")), "Meshless pickup incorrectly rejected");
            var active = ItemDatabaseAuthoring.All().Where(i => !ItemDatabaseAuthoring.IsArchived(i) && !AssetDatabase.GetAssetPath(i).StartsWith(folder)).ToList();
            foreach (string tier in new[] { "Bronze", "Iron" })
                foreach (string name in new[] { "Helm", "Armor", "Gloves", "Boots", "Sword", "Shield" })
                {
                    var gear = active.Single(i => i.itemName == tier + " " + name);
                    Check(ItemDatabaseAuthoring.Issues(gear, active).Count == 0, "New gear has authoring issues: " + gear.itemName);
                }
            File.WriteAllText("Temp/ItemDatabaseValidation.report", "PASS: duplicate identities/names are unique; organization preserves GUIDs and prefab references; edits support Undo; validation flags missing icons without rejecting meshless pickup fallback; all 12 new gear items have valid authoring data.");
        }
        catch (Exception ex) { File.WriteAllText("Temp/ItemDatabaseValidation.report", ex.ToString()); Debug.LogException(ex); }
        finally { AssetDatabase.DeleteAsset(folder); EditorSettings.serializationMode = previous; }
        if (File.ReadAllText("Temp/ItemDatabaseValidation.report").StartsWith("PASS"))
        {
            int count = ItemDatabaseAuthoring.SyncCatalog();
            previous = EditorSettings.serializationMode;
            try { EditorSettings.serializationMode = SerializationMode.ForceText; EditorSceneManager.SaveOpenScenes(); }
            finally { EditorSettings.serializationMode = previous; }
            File.AppendAllText("Temp/ItemDatabaseValidation.report", $"\nPASS: runtime catalog synchronized with {count} active items; scene saved.");
            ItemDatabaseWindow.ShowWindow();
        }
    }
}


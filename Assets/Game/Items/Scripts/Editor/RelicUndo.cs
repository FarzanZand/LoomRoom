using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Ctrl+Z after the Item Database's New > Relic takes the new relic away again (review 2026-11-12: undo turned it back
// into a Common copy of the gear piece it was made from, which stayed in the loot catalog and could drop).
//
// Unity's Undo can't delete an asset file it didn't create, so the new relic is written into this editor-only
// singleton as one undo step named "New relic". When an undo takes it out of the list, the relic's file and its
// generated icon go to the recycle bin and the catalog is synced. A redo after that brings back nothing (the file is
// gone; make it again). The singleton is never saved to disk and outlives script reloads, so the steps keep working
// across a compile.
public class RelicUndo : ScriptableSingleton<RelicUndo>
{
    // Each entry is "guid|first name": the relic's asset GUID and the name its asset had before it was renamed (the
    // copy's icon was rendered under that name).
    [SerializeField] List<string> made = new();
    static List<string> known;
    const string GeneratedIcons = "Assets/Game/Items/Icons/Generated/";

    // Called once the relic's asset is set up and saved: the undo step that takes it back.
    public static void Made(ItemData relic, string firstName)
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(relic));
        if (string.IsNullOrEmpty(guid)) return;
        var me = instance;
        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("New relic");
        Undo.RecordObject(me, "New relic");
        me.made.Add(guid + "|" + firstName);
        known = new List<string>(me.made);
    }

    [InitializeOnLoadMethod]
    static void Hook()
    {
        Undo.undoRedoPerformed -= Check;
        Undo.undoRedoPerformed += Check;
    }

    static void Check()
    {
        var now = instance.made;
        known ??= new List<string>(now);
        bool removed = false;
        foreach (string entry in known)
        {
            if (now.Contains(entry)) continue;
            int bar = entry.IndexOf('|');
            string guid = bar < 0 ? entry : entry.Substring(0, bar), firstName = bar < 0 ? "" : entry.Substring(bar + 1);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var item = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item == null) continue;
            // Its generated icons go too (Duplicate renders one for the copy, the renderer another under its new name),
            // unless another item shows the same file.
            var icons = new List<string>();
            string drawn = item.icon != null ? AssetDatabase.GetAssetPath(item.icon) : "";
            if (drawn.StartsWith(GeneratedIcons)) icons.Add(drawn);
            foreach (string name in new[] { item.name, firstName })
            {
                string own = GeneratedIcons + name + ".png";
                if (name.Length > 0 && !icons.Contains(own) && AssetDatabase.LoadAssetAtPath<Sprite>(own) != null) icons.Add(own);
            }
            foreach (var other in ItemDatabaseAuthoring.All())
                if (other != null && other != item && other.icon != null) icons.Remove(AssetDatabase.GetAssetPath(other.icon));
            if (!AssetDatabase.MoveAssetToTrash(path)) continue;
            removed = true;
            foreach (string icon in icons) AssetDatabase.MoveAssetToTrash(icon);
        }
        known = new List<string>(now);
        if (removed) ItemDatabaseAuthoring.SyncCatalog();
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Delete room... (the user, 2026-11-16: "I want to be able to delete rooms by using the asset studio on this list,
// deleting them from both the rotation and project to save space"). On a room's DETAILS and on its right-click menu, in
// place of the plain Delete... (which left an empty entry in every pool the room was in).
//   Out of the rotation: its entry is taken out of every biome's room pool (every biome asset in the project, the base
//   biomes inside the levels too, whether a level runs them or not), out of every level's Starting Room Variants, and
//   off the developer tools' lists of every room (the spawn window's and the console's, for builds).
//   Out of the project: the prefab goes to the recycle bin, and with it the kit preview meshes only this room used
//   (Room pieces/Previews: made per block size, nothing else to them). Props and art the room used are left alone.
//   Refused, with what holds it, while something needs the room to stand: a level's Starting Room or Shop Room, a boss's
//   arena, a painted level's room, a map template, a portal in another room.
//   Not made again: its name goes into Deleted rooms.txt (DungeonChunkKit.Retire), which the room builders skip, with
//   the pools and weights it had, so it is on record what to put back.
public partial class AssetStudioWindow
{
    static bool IsRoom(string path) => path.EndsWith(".prefab") && AssetDatabase.LoadMainAssetAtPath(path) is GameObject g && g.GetComponent<DungeonRoomTemplate>() != null;

    // Everything in the project that points at the room: its pool entries and starting-room-variant entries (taken out
    // with it), and whatever else holds it (it can't go while that stands).
    static void RoomHolders(GameObject room, string path, List<AssetStudioCatalog.Category> categories,
        List<(DungeonBiome biome, string where, float weight)> pools, List<(TableLevelData level, string where)> variants,
        List<(Component tool, string at)> toolLists, List<string> held)
    {
        AssetStudioUsage.Forget();   // read fresh: a pool edited a moment ago counts
        foreach (var (userPath, entry) in AssetStudioUsage.Find(AssetDatabase.AssetPathToGUID(path), categories))
        {
            string file = Path.GetFileNameWithoutExtension(userPath);
            if (userPath.EndsWith(".unity")) { held.Add($"{file} (a scene)"); continue; }
            if (!userPath.EndsWith(".asset"))
            {
                // A prefab: which of its components points here. The developer tools' lists of every room (the spawn
                // window's, the console's) are no reason to keep a room: its line there goes with it.
                bool found = false;
                if (AssetDatabase.LoadMainAssetAtPath(userPath) is GameObject prefab)
                    foreach (var part in prefab.GetComponentsInChildren<Component>(true))
                    {
                        if (part == null) continue;
                        var p = new SerializedObject(part).GetIterator();
                        while (p.Next(true))
                        {
                            if (p.propertyType != SerializedPropertyType.ObjectReference || p.objectReferenceValue != room) continue;
                            found = true;
                            string at = p.propertyPath;
                            if (part is DevSpawner && at.StartsWith("rooms.Array.data[") || part is DevConsole && at.StartsWith("templates.Array.data[")) toolLists.Add((part, at));
                            else held.Add($"{file}: {ObjectNames.NicifyVariableName(part.GetType().Name)} > {ObjectNames.NicifyVariableName(at.Split('.')[0])}");
                        }
                    }
                if (!found) held.Add($"{file} (the room is built into this prefab)");
                continue;
            }
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(userPath))
            {
                if (obj == null) continue;
                string where = obj.name == file || string.IsNullOrEmpty(obj.name) ? file : $"{file} · {obj.name}";
                var it = new SerializedObject(obj).GetIterator();
                while (it.Next(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue != room) continue;
                    string at = it.propertyPath;
                    if (obj is DungeonBiome biome && at.StartsWith("roomTemplates.Array.data[") && at.EndsWith("].template"))
                    {
                        var choice = biome.roomTemplates.FirstOrDefault(c => c != null && c.template == room);
                        if (!pools.Any(p => p.biome == biome)) pools.Add((biome, where, choice != null ? choice.weight : 0f));
                    }
                    else if (obj is TableLevelData level && at.StartsWith("startingRoomVariants.Array.data["))
                    { if (!variants.Any(v => v.level == level)) variants.Add((level, where)); }
                    else held.Add($"{where}: {ObjectNames.NicifyVariableName(at.Split('.')[0])}");
                }
            }
        }
    }

    // The kit preview meshes (Room pieces/Previews) nothing but this room uses.
    static List<string> OwnPreviews(string path, List<AssetStudioCatalog.Category> categories)
    {
        var own = new List<string>();
        foreach (var dep in AssetDatabase.GetDependencies(path, true))
        {
            if (!dep.StartsWith("Assets/Game/Levels/Shared/Room pieces/Previews/") || !dep.EndsWith(".asset")) continue;
            if (AssetStudioUsage.Find(AssetDatabase.AssetPathToGUID(dep), categories).All(u => u.path == path)) own.Add(dep);
        }
        return own;
    }

    static long FileBytes(string path) => (File.Exists(path) ? new FileInfo(path).Length : 0) + (File.Exists(path + ".meta") ? new FileInfo(path + ".meta").Length : 0);

    // For tests from an eval: the same without the questions (returns what it did, or why not), and what holds a room
    // (nothing is changed).
    public string TestDeleteRoom(string path) => DeleteRoom(path, false);
    public string TestRoomHolders(string path)
    {
        var pools = new List<(DungeonBiome biome, string where, float weight)>();
        var variants = new List<(TableLevelData level, string where)>();
        var held = new List<string>();
        var toolLists = new List<(Component tool, string at)>();
        RoomHolders(AssetDatabase.LoadMainAssetAtPath(path) as GameObject, path, categories, pools, variants, toolLists, held);
        return $"{(DeleteBlocked(path) is string no ? "blocked (" + no + ")" : held.Count > 0 ? "needed" : "can go")}: pools {pools.Count}, starting room lists {variants.Count}, tool lists {toolLists.Count}{(held.Count > 0 ? ", needed by " + string.Join("; ", held.Distinct()) : "")}";
    }

    string DeleteRoom(string path, bool ask = true)
    {
        string why = DeleteBlocked(path);
        if (why != null) { Toast("Not deleted: " + why + "."); return "blocked: " + why; }
        var room = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
        if (room == null) return "no room at " + path;
        string name = Path.GetFileNameWithoutExtension(path);
        var pools = new List<(DungeonBiome biome, string where, float weight)>();
        var variants = new List<(TableLevelData level, string where)>();
        var held = new List<string>();
        var toolLists = new List<(Component tool, string at)>();
        RoomHolders(room, path, categories, pools, variants, toolLists, held);
        if (held.Count > 0)
        {
            string needed = $"{name} is still needed:\n\n{string.Join("\n", held.Distinct().Take(8))}{(held.Distinct().Count() > 8 ? "\n..." : "")}\n\nGive those another room first, then delete it.";
            if (ask) EditorUtility.DisplayDialog("Can't delete " + name, needed, "OK");
            return needed;
        }
        var previews = OwnPreviews(path, categories);
        long bytes = FileBytes(path) + previews.Sum(FileBytes);
        string rotation = pools.Count + variants.Count == 0 ? "It is in no room pool."
            : "Taken out of " + string.Join(", ", pools.Select(p => $"{p.where} (weight {p.weight:0.##})").Concat(variants.Select(v => v.where + " (a starting room)"))) + ".";
        string files = previews.Count == 0 ? "" : $" and {previews.Count} preview mesh{(previews.Count == 1 ? "" : "es")} only it uses";
        string question = $"{name}\n{path}\n\n{rotation}\n\nThe room{files} go{(previews.Count == 0 ? "es" : "")} to the recycle bin ({bytes / 1024f:0} KB). Got back from there it is the same room, but not back in its pools.\nThe room builders will not make it again (Deleted rooms.txt).";
        if (ask && !EditorUtility.DisplayDialog("Delete room " + name, question, "Delete room", "Cancel")) return "cancelled";

        CommitDialogue(true);
        foreach (var (biome, _, _) in pools)
        {
            biome.roomTemplates = biome.roomTemplates.Where(c => c == null || c.template != room).ToArray();
            EditorUtility.SetDirty(biome);
        }
        foreach (var (level, _) in variants)
        {
            level.startingRoomVariants = level.startingRoomVariants.Where(v => v != room).ToArray();
            EditorUtility.SetDirty(level);
        }
        // Off the developer tools' room lists (the spawn window's list has a group name beside each room).
        int Index(string at) => int.Parse(at.Substring(at.IndexOf('[') + 1, at.IndexOf(']') - at.IndexOf('[') - 1));
        foreach (var tool in toolLists.GroupBy(t => t.tool))
        {
            var so = new SerializedObject(tool.Key);
            foreach (string at in tool.Select(t => t.at).OrderByDescending(Index))
            {
                int i = Index(at);
                var list = so.FindProperty(at.Substring(0, at.IndexOf(".Array")));
                list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                list.DeleteArrayElementAtIndex(i);
                var groups = tool.Key is DevSpawner ? so.FindProperty("roomGroups") : null;
                if (groups != null && i < groups.arraySize) groups.DeleteArrayElementAtIndex(i);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SavePrefabAsset(tool.Key.transform.root.gameObject);
        }
        AssetDatabase.SaveAssets();
        DungeonChunkKit.Retire(name, string.Join(", ", pools.Select(p => $"{p.where} {p.weight:0.##}").Concat(variants.Select(v => v.where + " starting room"))));
        // Let go of it first: the preview's copy, a painting on it, the inspector.
        if (selected != null && selected.path == path) Select(null);
        if (inspected != null && AssetDatabase.GetAssetPath(inspected) == path) inspected = null;
        var failed = new List<string>();
        var gone = new List<string>(previews) { path };
        AssetDatabase.MoveAssetsToTrash(gone.ToArray(), failed);
        if (failed.Contains(path)) Toast("Could not delete " + name + " (it is out of its pools).");
        AssetStudioCatalog.Apply(categories, gone);
        Filter();
        world = null; tomes = null;
        AssetStudioUsage.Forget();
        usedBy = null; usedByFor = null;
        if (!failed.Contains(path)) Toast($"Deleted {name}: out of {pools.Count} pool{(pools.Count == 1 ? "" : "s")}, {bytes / 1024f:0} KB to the recycle bin.");
        Repaint();
        return (failed.Contains(path) ? "NOT deleted. " : "deleted. ") + question;
    }
}

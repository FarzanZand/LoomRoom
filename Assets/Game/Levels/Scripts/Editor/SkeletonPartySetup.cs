using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// SkeletonPartySetup.Build(), run from an eval (safe to run again; it keeps what exists):
// - Levels/Shared/Portal/Portal arch: the kit arch with a PortalArch on it. Put it in any room template and set its Room.
// - Levels/Pockets/Pocket: the level a portal's pocket is built from (its room 0 takes the portal's Room; Crypt stone
//   unless the portal names a biome).
// - Rooms/Templates/Chunks/Skeleton Party room: the Moonlit fountain room (open skylight) without its fountain, a ring
//   of skeleton dancers (Enemy sockets, Crypt Soldier), a chest in the middle, RoomMusic and SkeletonParty on the root.
// - Rooms/Templates/Chunks/Portal hall: the Gate hall with its first arch swapped for a Portal arch to the party room.
// - Cellars and Crypt: the Cell block's place in the room pools goes to the Skeleton Party room, and the Portal hall
//   joins at most once a floor.
// Edit the prefabs freely afterwards (the dance clip and the party's track go on the room's root).
public static class SkeletonPartySetup
{
    const string Chunks = "Assets/Game/Levels/Dungeon1/Rooms/Templates/Chunks/";
    const string Party = Chunks + "Skeleton Party room.prefab";
    const string Portal = Chunks + "Portal hall.prefab";
    const string PocketFolder = "Assets/Game/Levels/Pockets";
    const string Pocket = PocketFolder + "/Pocket.asset";
    const string PortalFolder = "Assets/Game/Levels/Shared/Portal";
    const string PortalArchPrefab = PortalFolder + "/Portal arch.prefab";
    const string Dancer = "Assets/Game/Characters/Enemies/Crypt/Crypt Soldier.prefab";
    const string Arch = "Assets/Game/Levels/Dungeon1/Rooms/Kit/Arch.prefab";

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        BuildPartyRoom(log);
        var pocket = BuildPocket(log);
        var arch = BuildPortalArch(pocket, log);
        BuildPortalHall(arch, log);
        UpdatePools(log);
        AssetDatabase.SaveAssets();
        Debug.Log("[Skeleton party] " + log);
        return log.ToString();
    }

    static void BuildPartyRoom(System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Party) == null)
        {
            AssetDatabase.CopyAsset(Chunks + "Moonlit fountain.prefab", Party);
            log.AppendLine("Copied Moonlit fountain to Skeleton Party room");
        }
        var root = PrefabUtility.LoadPrefabContents(Party);
        try
        {
            root.name = "Skeleton Party room";
            var template = root.GetComponent<DungeonRoomTemplate>();
            if (template != null)
            {
                var ev = typeof(DungeonRoomTemplate).GetField("roomEvent");
                if (ev != null) ev.SetValue(template, System.Enum.ToObject(ev.FieldType, 1));   // None: the party is the event
            }
            // The fountain and the merchant's spot go.
            foreach (var s in root.GetComponentsInChildren<DungeonSocket>(true))
                if (s.type == DungeonSocketType.Feature || s.type == DungeonSocketType.Merchant) { Object.DestroyImmediate(s.gameObject); log.AppendLine("Removed a " + s.type + " socket"); }
            var dancer = AssetDatabase.LoadAssetAtPath<GameObject>(Dancer);
            if (root.transform.Find("Dancers") == null)
            {
                var ring = new GameObject("Dancers").transform;
                ring.SetParent(root.transform, false);
                for (int i = 0; i < 5; i++)
                {
                    var spot = new GameObject("Dancer " + (i + 1)).transform;
                    spot.SetParent(ring, false);
                    var dir = Quaternion.Euler(0, i * 72f + 18f, 0) * Vector3.forward;
                    spot.localPosition = dir * 3.2f;
                    spot.localRotation = Quaternion.LookRotation(-dir);
                    var socket = spot.gameObject.AddComponent<DungeonSocket>();
                    socket.type = DungeonSocketType.Enemy;
                    socket.overridePrefab = dancer;
                }
                log.AppendLine("Added 5 dancers");
            }
            if (root.transform.Find("Party chest") == null)
            {
                var chest = new GameObject("Party chest").transform;
                chest.SetParent(root.transform, false);
                chest.gameObject.AddComponent<DungeonSocket>().type = DungeonSocketType.Chest;
                log.AppendLine("Added the chest in the middle");
            }
            if (root.GetComponent<RoomMusic>() == null) root.AddComponent<RoomMusic>();
            if (root.GetComponent<SkeletonParty>() == null) root.AddComponent<SkeletonParty>();
            // The way back is made by the portal in its pocket now; an older build's gate goes.
            var oldGate = root.transform.Find("Way back");
            if (oldGate != null) { Object.DestroyImmediate(oldGate.gameObject); log.AppendLine("Removed the old way-back gate"); }
            PrefabUtility.SaveAsPrefabAsset(root, Party);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // The kit arch with the portal on its root, the generator's "clear it off a walkway" marker taken off.
    static GameObject BuildPortalArch(TableLevelData pocket, System.Text.StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder(PortalFolder)) AssetDatabase.CreateFolder("Assets/Game/Levels/Shared", "Portal");
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PortalArchPrefab);
        GameObject go = existing != null ? PrefabUtility.LoadPrefabContents(PortalArchPrefab) : (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Arch));
        try
        {
            go.name = "Portal arch";
            var portal = go.GetComponent<PortalArch>();
            if (portal == null) portal = go.AddComponent<PortalArch>();
            portal.mode = PortalArch.Mode.Enter;
            if (portal.pocketLevel == null) portal.pocketLevel = pocket;
            foreach (var piece in go.GetComponentsInChildren<DungeonTemplatePiece>(true)) Object.DestroyImmediate(piece, true);
            var saved = PrefabUtility.SaveAsPrefabAsset(go, PortalArchPrefab);
            if (existing == null) log.AppendLine("Made " + PortalArchPrefab);
            return saved;
        }
        finally { if (existing != null) PrefabUtility.UnloadPrefabContents(go); else Object.DestroyImmediate(go); }
    }

    static TableLevelData BuildPocket(System.Text.StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder(PocketFolder)) AssetDatabase.CreateFolder("Assets/Game/Levels", "Pockets");
        if (AssetDatabase.LoadAssetAtPath<TableLevelData>(PocketFolder + "/Party pocket.asset") != null && AssetDatabase.LoadAssetAtPath<TableLevelData>(Pocket) == null)
            AssetDatabase.MoveAsset(PocketFolder + "/Party pocket.asset", Pocket);
        var pocket = AssetDatabase.LoadAssetAtPath<TableLevelData>(Pocket);
        if (pocket == null)
        {
            AssetDatabase.CopyAsset("Assets/Game/Levels/Dungeon1/Dungeon1.asset", Pocket);
            pocket = AssetDatabase.LoadAssetAtPath<TableLevelData>(Pocket);
            log.AppendLine("Made the Party pocket level from Dungeon1");
        }
        var so = new SerializedObject(pocket);
        so.FindProperty("pocket").boolValue = true;
        var mode = so.FindProperty("layoutMode");
        mode.enumValueIndex = System.Array.IndexOf(mode.enumNames, "Authored");
        // A 5 x 5 room in the middle of the smallest map the generator takes (14 x 14), with a little alcove
        // off it (an authored map needs two rooms; a pocket has no exit, so the alcove stays empty).
        var rows = new List<string>();
        for (int y = 0; y < 14; y++)
            rows.Add(y == 7 ? "#####00000.11#" : y == 6 || y == 8 ? "#####00000#11#" : y >= 5 && y < 10 ? "#####00000####" : "##############");
        so.FindProperty("authoredMap").stringValue = string.Join("\n", rows);
        so.FindProperty("authoredContent").stringValue = "";
        var rooms = so.FindProperty("authoredRooms");
        rooms.arraySize = 1;
        var room = rooms.GetArrayElementAtIndex(0);
        room.FindPropertyRelative("label").stringValue = "Portal room";
        room.FindPropertyRelative("template").objectReferenceValue = null;   // each portal puts its own Room here
        // A taste of a later biome: the Crypt's stone, its one stage, no boss.
        var stages = so.FindProperty("stages");
        if (stages.arraySize > 1)
        {
            var crypt = stages.GetArrayElementAtIndex(1).FindPropertyRelative("biome").objectReferenceValue;
            stages.arraySize = 1;
            stages.GetArrayElementAtIndex(0).FindPropertyRelative("biome").objectReferenceValue = crypt;
        }
        if (stages.arraySize > 0)
        {
            stages.GetArrayElementAtIndex(0).FindPropertyRelative("floors").intValue = 1;
            stages.GetArrayElementAtIndex(0).FindPropertyRelative("boss").boolValue = false;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(pocket);
        return pocket;
    }

    static void BuildPortalHall(GameObject portalArch, System.Text.StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Portal) == null)
        {
            AssetDatabase.CopyAsset(Chunks + "Gate hall.prefab", Portal);
            log.AppendLine("Copied Gate hall to Portal hall");
        }
        var party = AssetDatabase.LoadAssetAtPath<GameObject>(Party);
        var root = PrefabUtility.LoadPrefabContents(Portal);
        try
        {
            root.name = "Portal hall";
            // An older build's portal (a bare PortalArch inside an arch) goes with its arch's place kept.
            Transform place = null;
            foreach (var old in root.GetComponentsInChildren<PortalArch>(true))
                if (PrefabUtility.GetCorrespondingObjectFromSource(old.gameObject) == null && old.name == "Portal")
                { place = old.transform.parent; Object.DestroyImmediate(old.gameObject); }
            PortalArch portal = null;
            foreach (var p in root.GetComponentsInChildren<PortalArch>(true)) portal = p;
            if (portal == null)
            {
                // The first kit arch in the hall becomes a Portal arch.
                if (place == null)
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t != root.transform && (t.name.StartsWith("Arch") || t.name.Contains("Kit arch"))) { place = t; break; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(portalArch, place != null ? place.parent : root.transform);
                if (place != null) { go.transform.SetLocalPositionAndRotation(place.localPosition, place.localRotation); go.transform.localScale = place.localScale; Object.DestroyImmediate(place.gameObject); }
                portal = go.GetComponent<PortalArch>();
                log.AppendLine("Portal arch in the Portal hall");
            }
            portal.room = party;
            PrefabUtility.SaveAsPrefabAsset(root, Portal);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static void UpdatePools(System.Text.StringBuilder log)
    {
        var party = AssetDatabase.LoadAssetAtPath<GameObject>(Party);
        var portal = AssetDatabase.LoadAssetAtPath<GameObject>(Portal);
        var cell = AssetDatabase.LoadAssetAtPath<GameObject>(Chunks + "Cell block.prefab");
        var gateHall = AssetDatabase.LoadAssetAtPath<GameObject>(Chunks + "Gate hall.prefab");
        foreach (var path in new[] { "Assets/Game/Levels/Dungeon1/Biomes/Cellars.asset", "Assets/Game/Levels/Dungeon1/Biomes/Crypt.asset" })
        {
            var biome = AssetDatabase.LoadAssetAtPath<DungeonBiome>(path);
            var list = new List<DungeonBiome.RoomTemplateChoice>(biome.roomTemplates ?? new DungeonBiome.RoomTemplateChoice[0]);
            bool hasParty = list.Exists(c => c != null && c.template == party), hasPortal = list.Exists(c => c != null && c.template == portal);
            foreach (var c in list)
                if (c != null && c.template == cell && !hasParty) { c.template = party; c.maxPerFloor = 1; hasParty = true; log.AppendLine(biome.name + ": Cell block -> Skeleton Party room"); }
            if (!hasParty) { list.Add(new DungeonBiome.RoomTemplateChoice { template = party, weight = 1, rooms = DungeonRoomRoles.Combat, maxPerFloor = 1 }); log.AppendLine(biome.name + ": added Skeleton Party room"); }
            if (!hasPortal)
            {
                var like = list.Find(c => c != null && c.template == gateHall);
                list.Add(new DungeonBiome.RoomTemplateChoice { template = portal, weight = like != null ? like.weight * .5f : .5f, rooms = like != null ? like.rooms : DungeonRoomRoles.Combat | DungeonRoomRoles.Rest, maxPerFloor = 1 });
                log.AppendLine(biome.name + ": added Portal hall");
            }
            biome.roomTemplates = list.ToArray();
            EditorUtility.SetDirty(biome);
        }
    }
}

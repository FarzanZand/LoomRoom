using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's World: making and arranging things. Every New puts the file in its folder and links it where it belongs
// (a level in the catalog, a biome on its level's run, a room in its biome's pool, a place in the campaign manager's list
// and on the map, an event in the manager's events), so it shows up in the game at once. Every change is saved straight
// away and can be undone: asset edits with Ctrl+Z, edits to the campaign manager and realm map prefabs (and the places
// and events made with them) with the World's Undo button (PrefabUndo below), which asks before overwriting anything
// changed since. A level's asset name is how saved runs find it: a new one gets a name no other level has.
public static partial class AssetStudioWorld
{
    // ── Levels, biomes, rooms ─────────────────────────────────────────

    // A new level made from an existing one (its look, layout and rules): Levels/<name>/<name>.asset with Biomes and
    // Rooms folders, its own copy of each of the template's stage biomes when ownBiomes (else it shares them), in the
    // catalog. The base biome comes along inside the file (renamed for the new level).
    public static TableLevelData NewLevel(string name, TableLevelData template, bool ownBiomes, out string message)
    {
        if (template == null) { message = "Pick a level to start from."; return null; }
        if (LevelNameProblem(name) is string problem) { message = problem; return null; }
        name = CleanName(name);
        string folder = LevelsRoot + "/" + name;
        EnsureFolder(folder + "/Biomes"); EnsureFolder(folder + "/Rooms");
        string path = folder + "/" + name + ".asset";
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template), path)) { message = "Could not copy " + template.name + "."; return null; }
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(path);
        level.MakeBaseBiome();   // the copied base biome, named for this level (made when the template had none)
        level.displayName = name;
        bool staged = level.HasStages;
        if (ownBiomes && level.stages != null)
            for (int i = 0; i < level.stages.Length; i++)
                if (level.stages[i]?.biome != null) level.stages[i].biome = CopyBiome(level.stages[i].biome, folder + "/Biomes", name + " " + level.stages[i].biome.name, null);
        EditorUtility.SetDirty(level);
        AddToCatalog(level);
        AssetDatabase.SaveAssets();
        message = $"Made {name} from {template.name}" + (!staged ? ", its floors on its own base biome." : ownBiomes ? " with its own biomes." : ", sharing its biomes.");
        return level;
    }

    // fileName names the asset; displayName (what the game shows, e.g. on the run recap) stays the source's unless given.
    // A level's base biome is copied out of its level's file on its own (copying the file would copy the whole level).
    static DungeonBiome CopyBiome(DungeonBiome source, string folder, string fileName, string displayName)
    {
        EnsureFolder(folder);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + CleanName(fileName) + ".asset");
        DungeonBiome copy;
        if (IsBase(source))
        {
            copy = UnityEngine.Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(path);
            copy.isLevelBase = false;
            AssetDatabase.CreateAsset(copy, path);
        }
        else
        {
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path);
            copy = AssetDatabase.LoadAssetAtPath<DungeonBiome>(path);
        }
        if (!string.IsNullOrEmpty(displayName)) copy.displayName = CleanName(displayName);
        EditorUtility.SetDirty(copy);
        return copy;
    }

    // A new biome on a level's run (a copy of template, or of the level's last biome, or its base biome), for some floors.
    public static DungeonBiome NewBiome(TableLevelData level, string name, DungeonBiome template, int floors, bool boss, out string message)
    {
        name = CleanName(name);
        template ??= level != null && level.stages != null ? level.stages.LastOrDefault(s => s?.biome != null)?.biome : null;
        template ??= level != null ? level.baseBiome : null;
        if (level == null || template == null) { message = "Pick a level, and a biome to start from."; return null; }
        if (name.Length == 0) { message = "Give the biome a name."; return null; }
        string folder = (LevelFolder(level) ?? Path.GetDirectoryName(AssetDatabase.GetAssetPath(level)).Replace('\\', '/')) + "/Biomes";
        var biome = CopyBiome(template, folder, name, name);
        AddStage(level, biome, floors, boss);
        message = $"Made {biome.name} ({floors} floor{(floors == 1 ? "" : "s")}) at the end of {level.name}'s run.";
        return biome;
    }

    public static void AddStage(TableLevelData level, DungeonBiome biome, int floors = 2, bool boss = false)
    {
        Undo.RecordObject(level, "Add stage");
        var list = (level.stages ?? Array.Empty<TableLevelData.BiomeStage>()).ToList();
        list.Add(new TableLevelData.BiomeStage { biome = biome, floors = Mathf.Max(1, floors), boss = boss });
        level.stages = list.ToArray();
        Save(level);
    }

    // Takes a stage off the run. When it was the last, the level's floors run on its base biome: as many floors as that
    // stage had, its boss too. Returns what changed (with what the base biome lacks for that).
    public static string RemoveStage(TableLevelData level, int index)
    {
        if (level.stages == null || index < 0 || index >= level.stages.Length) return "";
        Undo.RecordObject(level, "Remove stage");
        var removed = level.stages[index];
        var list = level.stages.ToList(); list.RemoveAt(index); level.stages = list.ToArray();
        string result = $"Took {(removed?.biome != null ? removed.biome.name : "the stage")} off {level.name}'s run.";
        if (level.IsDungeon && !level.HasStages)
        {
            if (removed != null) { level.floors = Mathf.Max(1, removed.floors); level.bossOnLastFloor = removed.boss; }
            var b = level.baseBiome;
            if (b == null) result += " It has no stages and no base biome now: make the base biome on its Base biome tab.";
            else
            {
                result += $" Its {level.FloorCount} floor{(level.FloorCount == 1 ? "" : "s")} now run on its base biome.";
                var lacks = new List<string>();
                if (b.enemies == null || !b.enemies.Any(e => e != null && e.enemy != null)) lacks.Add("no enemies");
                if (b.loot == null) lacks.Add("no loot");
                if (lacks.Count > 0) result += " Its base biome has " + string.Join(" and ", lacks) + ": give it some.";
            }
        }
        Save(level);
        return result;
    }

    public static void MoveStage(TableLevelData level, int index, int by)
    {
        int to = index + by;
        if (level.stages == null || index < 0 || to < 0 || index >= level.stages.Length || to >= level.stages.Length) return;
        Undo.RecordObject(level, "Move stage");
        (level.stages[index], level.stages[to]) = (level.stages[to], level.stages[index]);
        Save(level);
    }

    // A blank 7 x 7 room (the room kit's), in the level's Rooms folder (or Shared/Rooms), added to the biome's pool.
    public static GameObject NewRoom(string name, TableLevelData level, DungeonBiome biome, out string message)
    {
        name = CleanName(name);
        if (name.Length == 0) { message = "Give the room a name."; return null; }
        string folder = level != null && LevelFolder(level) is string f ? f + "/Rooms" : SharedRooms;
        EnsureFolder(folder);
        var room = DungeonChunkKit.NewRoomTemplate(folder, name);
        if (biome != null) AddToPool(biome, room);
        message = $"Made {room.name} in {folder}" + (biome != null ? $" and put it in {biome.name}'s pool." : ".") + " Open it to paint its footprint and drag kit pieces in.";
        return room;
    }

    public static bool AddToPool(DungeonBiome biome, GameObject room, float weight = 1)
    {
        if (biome == null || room == null || room.GetComponent<DungeonRoomTemplate>() == null) return false;
        if (biome.roomTemplates != null && biome.roomTemplates.Any(c => c != null && c.template == room)) return false;
        Undo.RecordObject(biome, "Add room to pool");
        var list = (biome.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>()).ToList();
        list.Add(new DungeonBiome.RoomTemplateChoice { template = room, weight = weight });
        biome.roomTemplates = list.ToArray();
        Save(biome);
        return true;
    }

    public static void RemoveFromPool(DungeonBiome biome, int index)
    {
        if (biome.roomTemplates == null || index < 0 || index >= biome.roomTemplates.Length) return;
        Undo.RecordObject(biome, "Remove room from pool");
        var list = biome.roomTemplates.ToList(); list.RemoveAt(index); biome.roomTemplates = list.ToArray();
        Save(biome);
    }

    // ── A biome's enemies ─────────────────────────────────────────────

    public static bool IsEnemy(UnityEngine.Object o) => o is GameObject g && g.GetComponent<EnemyBrain>() != null && EditorUtility.IsPersistent(g);

    public static bool AddEnemy(DungeonBiome biome, GameObject enemy, float weight = 1)
    {
        if (biome == null || !IsEnemy(enemy)) return false;
        if (biome.enemies != null && biome.enemies.Any(s => s != null && s.enemy == enemy)) return false;
        Undo.RecordObject(biome, "Add enemy");
        var list = (biome.enemies ?? Array.Empty<DungeonBiome.Spawn>()).ToList();
        list.Add(new DungeonBiome.Spawn { enemy = enemy, weight = weight, fromFloor = 1 });
        biome.enemies = list.ToArray();
        Save(biome);
        return true;
    }

    public static void RemoveEnemy(DungeonBiome biome, int index)
    {
        if (biome.enemies == null || index < 0 || index >= biome.enemies.Length) return;
        Undo.RecordObject(biome, "Remove enemy");
        var list = biome.enemies.ToList(); list.RemoveAt(index); biome.enemies = list.ToArray();
        Save(biome);
    }

    public static void SetEnemy(DungeonBiome biome, int index, float weight, int fromFloor)
    {
        if (biome.enemies == null || index < 0 || index >= biome.enemies.Length || biome.enemies[index] == null) return;
        Undo.RecordObject(biome, "Enemy weight");
        biome.enemies[index].weight = Mathf.Max(0, weight);
        biome.enemies[index].fromFloor = Mathf.Max(1, fromFloor);
        Save(biome);
    }

    // A hand-painted layout of rooms in a row, west to east: room 0 the entrance (the level's starting room unless set), the
    // last the exit with the stairs, the ones between fights; each 7 x 7, a door and a short passage between neighbours.
    // Rooms already set up keep their template, role, height and merchant.
    // The plan of count 7 x 7 rooms in a row, a door and a short passage between neighbours, north row first.
    static List<string> RowPlan(int count, int minimum, int margin)
    {
        const int size = 7, gap = 3;
        int width = Mathf.Max(minimum, margin * 2 + count * size + (count - 1) * gap), depth = Mathf.Max(minimum, margin * 2 + size);
        int top = (depth - size) / 2;
        var grid = new char[depth, width];
        for (int y = 0; y < depth; y++) for (int x = 0; x < width; x++) grid[y, x] = '#';
        for (int i = 0; i < count; i++)
        {
            int x0 = margin + i * (size + gap);
            char c = (char)(i < 10 ? '0' + i : 'A' + i - 10);
            for (int y = top; y < top + size; y++) for (int x = x0; x < x0 + size; x++) grid[y, x] = c;
            if (i < count - 1)
            {
                int mid = top + size / 2;
                grid[mid, x0 + size] = '+';
                for (int g = 1; g < gap; g++) grid[mid, x0 + size + g] = '.';
            }
        }
        var rows = new List<string>();
        for (int y = 0; y < depth; y++) { var row = new char[width]; for (int x = 0; x < width; x++) row[x] = grid[y, x]; rows.Add(new string(row)); }
        return rows;
    }

    // Whether laying the level out again throws away a plan someone painted: a floor plan that isn't a row of rooms, or
    // anything on its content map (enemies, items, breakables placed by letter).
    public static bool HasHandPaintedPlan(TableLevelData level)
    {
        if (level == null || string.IsNullOrWhiteSpace(level.authoredMap)) return false;
        if (level.authoredContent != null && level.authoredContent.Any(c => c != '\n' && c != '\r' && c != ' ' && c != '#' && c != '.')) return true;
        int rooms = level.authoredRooms?.Length ?? 0;
        return rooms < 2 || string.Join('\n', RowPlan(rooms, 14, 2)) != level.authoredMap.Replace("\r", "");
    }

    // The level as a row of count rooms. Replaces its floor plan and empties its content map (the World asks first).
    public static void MakeRoomsInARow(TableLevelData level, int count)
    {
        count = Mathf.Clamp(count, 2, 8);
        // The generator wants a plan at least 14 cells each way: rock above and below the row makes up the depth.
        var rows = RowPlan(count, 14, 2);
        int width = rows[0].Length, depth = rows.Count;
        Undo.RecordObject(level, "Rooms in a row");
        level.layoutMode = DungeonLayoutMode.Authored;
        level.authoredMap = string.Join('\n', rows);
        level.authoredContent = new string('\n', depth - 1);
        level.width = width; level.depth = depth;
        var old = level.authoredRooms ?? Array.Empty<TableLevelData.AuthoredRoom>();
        var list = new List<TableLevelData.AuthoredRoom>();
        for (int i = 0; i < count; i++)
        {
            var keep = i < old.Length && old[i] != null ? old[i] : null;
            var role = i == 0 ? DungeonGenerator.RoomRole.Entrance : i == count - 1 ? DungeonGenerator.RoomRole.Exit : DungeonGenerator.RoomRole.Combat;
            list.Add(keep ?? new TableLevelData.AuthoredRoom { label = i == 0 ? "Entrance" : i == count - 1 ? "Exit" : "Room " + i, role = role });
            if (keep != null && i == count - 1 && keep.role != DungeonGenerator.RoomRole.Exit) keep.role = DungeonGenerator.RoomRole.Exit;
        }
        level.authoredRooms = list.ToArray();
        Save(level);
    }

    // ── Map templates (a whole small map in one prefab) ────────────────

    // A new map template: count rooms in a row painted on it, a Room child per room (SyncRooms), saved in the level's
    // folder (or Levels/Shared/Maps). use: the level plays it (Authored, its Map Template).
    public static DungeonMapTemplate NewMapTemplate(string name, TableLevelData level, int count, bool use, out string message)
    {
        name = CleanName(name);
        if (name.Length == 0) { message = "Give the map a name."; return null; }
        count = Mathf.Clamp(count, 2, 8);
        string folder = level != null && LevelFolder(level) is string f ? f : LevelsRoot + "/Shared/Maps";
        EnsureFolder(folder);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".prefab");
        var root = new GameObject(name);
        DungeonMapTemplate saved;
        try
        {
            var map = root.AddComponent<DungeonMapTemplate>();
            var rows = RowPlan(count, 0, 1);
            map.width = rows[0].Length; map.depth = rows.Count; map.rows = rows.ToArray();
            map.previewLevel = level;
            map.SyncRooms();   // a Room child per painted room, on its centre cell
            for (int i = 0; i < count; i++)
            {
                var room = map.RoomChild(i);
                if (room != null) room.name = $"Room {DungeonMapTemplate.RoomMark(i)} " + (i == 0 ? "Entrance" : i == count - 1 ? "Exit" : "Fight");
            }
            saved = PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<DungeonMapTemplate>();
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        if (use && level != null) SetMapTemplate(level, saved);
        message = $"Made {saved.name} ({count} rooms in a row) in {folder}" + (use && level != null ? $"; {level.name} plays it." : ".") + " Open it to paint the map and build each room under its Room child.";
        return saved;
    }

    // The level plays this map (Authored layout), or its own text plan again when map is null.
    public static void SetMapTemplate(TableLevelData level, DungeonMapTemplate map)
    {
        Undo.RecordObject(level, "Map template");
        level.mapTemplate = map;
        if (map != null) level.layoutMode = DungeonLayoutMode.Authored;
        Save(level);
    }

    public static void AddToCatalog(TableLevelData level)
    {
        var catalog = AssetDatabase.LoadAssetAtPath<TableLevelCatalog>(CatalogPath);
        if (catalog == null || level == null) return;
        if (catalog.levels != null && Array.IndexOf(catalog.levels, level) >= 0) return;
        Undo.RecordObject(catalog, "Add level to catalog");
        catalog.levels = (catalog.levels ?? Array.Empty<TableLevelData>()).Append(level).ToArray();
        Save(catalog);
    }

    // ── Campaign ──────────────────────────────────────────────────────

    // A new place: its asset in Campaign/Encounters/Act <act>, appended to the campaign manager's list (append only:
    // the list's order is what saves keep), and a point on the map near the act's other places (move it in the map
    // prefab). With a level it plays that level; without one it is a placeholder ("not made yet").
    public static CampaignEncounter NewPlace(string name, int act, TableLevelData level, out string message)
    {
        name = CleanName(name);
        if (name.Length == 0) { message = "Give the place a name."; return null; }
        act = Mathf.Max(1, act);
        string folder = $"{EncountersRoot}/Act {act}";
        PrefabUndo.Begin("New place " + name, CampaignManagerPrefab, RealmMapPrefab);
        PrefabUndo.Folder(folder);
        EnsureFolder(folder);
        var place = ScriptableObject.CreateInstance<CampaignEncounter>();
        place.id = FreeId(name, All<CampaignEncounter>(EncountersRoot).Select(p => p.id));
        place.displayName = name;
        place.act = act;
        place.kind = level != null ? CampaignEncounterKind.Level : CampaignEncounterKind.Placeholder;
        place.level = level;
        place.description = "";
        string placePath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset");
        AssetDatabase.CreateAsset(place, placePath);
        PrefabUndo.Created(placePath);
        ListInManager(place, null, false);
        string point = PutOnMap(place, false);
        AssetDatabase.SaveAssets();
        PrefabUndo.Commit();
        message = $"Made {name} in Act {act}" + (level != null ? $", playing {level.name}" : " (a placeholder until it has a level)") + ". " + point;
        return place;
    }

    // A new event shown once after an act opens: its asset in Campaign/Encounters/Act <act>/<title>, in the manager's events.
    public static CampaignEvent NewEvent(string title, int afterAct, out string message)
    {
        title = CleanName(title);
        if (title.Length == 0) { message = "Give the event a title."; return null; }
        afterAct = Mathf.Max(1, afterAct);
        string folder = $"{EncountersRoot}/Act {afterAct}/{title}";
        PrefabUndo.Begin("New event " + title, CampaignManagerPrefab);
        PrefabUndo.Folder($"{EncountersRoot}/Act {afterAct}");
        PrefabUndo.Folder(folder);
        EnsureFolder(folder);
        var e = ScriptableObject.CreateInstance<CampaignEvent>();
        e.id = FreeId(title, All<CampaignEvent>("Assets/Game/Campaign").Select(x => x.id));
        e.title = title;
        e.afterAct = afterAct;
        e.text = "";
        e.choices = new[] { new CampaignEvent.Choice { label = "Leave", outcome = "" } };
        string eventPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{title}.asset");
        AssetDatabase.CreateAsset(e, eventPath);
        PrefabUndo.Created(eventPath);
        ListInManager(null, e, false);
        AssetDatabase.SaveAssets();
        PrefabUndo.Commit();
        message = $"Made the event {title} (after Act {afterAct}). Write its text, picture and choices in the inspector.";
        return e;
    }

    // Appends a place or an event the campaign manager doesn't list yet (it only runs what it lists).
    public static void ListInManager(CampaignEncounter place, CampaignEvent ev, bool undoStep = true)
    {
        if (undoStep) PrefabUndo.Begin("List in the campaign manager", CampaignManagerPrefab);
        var root = PrefabUtility.LoadPrefabContents(CampaignManagerPrefab);
        try
        {
            var manager = root.GetComponent<CampaignManager>();
            if (manager == null) return;
            if (place != null && (manager.encounters == null || Array.IndexOf(manager.encounters, place) < 0))
                manager.encounters = (manager.encounters ?? Array.Empty<CampaignEncounter>()).Append(place).ToArray();
            if (ev != null && (manager.events == null || Array.IndexOf(manager.events, ev) < 0))
                manager.events = (manager.events ?? Array.Empty<CampaignEvent>()).Append(ev).ToArray();
            PrefabUtility.SaveAsPrefabAsset(root, CampaignManagerPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); PrefabUndo.Commit(); }
    }

    // A point for the place on the map, under Points, beside the act's other places (or the map's middle).
    public static string PutOnMap(CampaignEncounter place, bool undoStep = true)
    {
        if (undoStep) PrefabUndo.Begin("Put " + place.displayName + " on the map", RealmMapPrefab);
        var root = PrefabUtility.LoadPrefabContents(RealmMapPrefab);
        try
        {
            var points = root.GetComponentsInChildren<CampaignPoint>(true);
            if (points.Any(p => p.encounter == place)) return "It is already on the map.";
            var parent = root.transform.Find("Points") ?? points.FirstOrDefault()?.transform.parent ?? root.transform;
            var go = new GameObject(place.displayName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = FreeSpot(points, place.act, null);
            var point = go.AddComponent<CampaignPoint>();
            point.encounter = place;
            PrefabUtility.SaveAsPrefabAsset(root, RealmMapPrefab);
            return "It stands on the map near the other Act " + place.act + " places, with no road yet: move it and add roads in the Realm map prefab.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); PrefabUndo.Commit(); }
    }

    // A free spot among the points (local positions): round the middle of the act's places (or of every place), away
    // from every other place. except: a point being moved, left out of both.
    static Vector3 FreeSpot(CampaignPoint[] points, int act, CampaignPoint except)
    {
        var others = points.Where(p => p != except).ToList();
        var near = others.Where(p => p.encounter != null && p.encounter.act == act).Select(p => p.transform.localPosition).ToList();
        if (near.Count == 0) near = others.Select(p => p.transform.localPosition).ToList();
        Vector3 centre = near.Count > 0 ? near.Aggregate(Vector3.zero, (a, b) => a + b) / near.Count : Vector3.zero;
        for (int i = 0; i < 24; i++)
        {
            float angle = i * 137.5f * Mathf.Deg2Rad, radius = 6f + i * 1.5f;
            var tryAt = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            if (others.All(p => (p.transform.localPosition - tryAt).sqrMagnitude > 64f)) return tryAt;
        }
        return centre;
    }

    // After a place's act changed: its point moves near that act's other places (its props go with it) and its file to
    // Campaign/Encounters/Act <act>. The World's Undo puts both back.
    public static string MoveToAct(CampaignEncounter place)
    {
        PrefabUndo.Begin("Move " + place.displayName + " to Act " + place.act, RealmMapPrefab);
        string result;
        var root = PrefabUtility.LoadPrefabContents(RealmMapPrefab);
        try
        {
            var points = root.GetComponentsInChildren<CampaignPoint>(true);
            var point = points.FirstOrDefault(p => p.encounter == place);
            if (point == null) result = "It is not on the map.";
            else
            {
                var spot = FreeSpot(points, place.act, point);
                point.transform.localPosition = new Vector3(spot.x, point.transform.localPosition.y, spot.z);
                PrefabUtility.SaveAsPrefabAsset(root, RealmMapPrefab);
                result = $"It stands near the Act {place.act} places, with its roads as they were: check them in the Realm map prefab.";
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        string from = AssetDatabase.GetAssetPath(place), folder = $"{EncountersRoot}/Act {place.act}";
        if (Path.GetDirectoryName(from).Replace('\\', '/') != folder)
        {
            PrefabUndo.Folder(folder);
            EnsureFolder(folder);
            string to = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(from));
            string err = AssetDatabase.MoveAsset(from, to);
            if (string.IsNullOrEmpty(err)) { PrefabUndo.Moved(from, to); result += $" Its file is in {folder.Replace("Assets/Game/", "")}."; }
            else result += " Its file stays: " + err;
        }
        PrefabUndo.Commit();
        return result;
    }

    // ── A place's props (the models on its disc in the map prefab) ──────

    // The props on the map's places, by name, with a place that has one (for Add a prop).
    public static List<string> MapProps()
    {
        var map = AssetDatabase.LoadAssetAtPath<GameObject>(RealmMapPrefab);
        if (map == null) return new List<string>();
        return map.GetComponentsInChildren<CampaignPoint>(true).SelectMany(p => p.transform.Cast<Transform>()).Select(t => t.name)
            .Where(n => n != "Base" && n != "poi light").Distinct().OrderBy(n => n).ToList();
    }

    // Props per place (-1: not on the map), read once per change of the map prefab (the place panel asks every frame).
    static Dictionary<CampaignEncounter, int> propCounts;
    static Hash128 propCountsFor;
    public static int PropCount(CampaignEncounter place)
    {
        var hash = AssetDatabase.GetAssetDependencyHash(RealmMapPrefab);
        if (propCounts == null || hash != propCountsFor)
        {
            propCounts = new Dictionary<CampaignEncounter, int>();
            propCountsFor = hash;
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(RealmMapPrefab);
            if (map != null)
                foreach (var point in map.GetComponentsInChildren<CampaignPoint>(true))
                    if (point.encounter != null) propCounts[point.encounter] = point.transform.Cast<Transform>().Count(t => t.name != "Base");
        }
        return place != null && propCounts.TryGetValue(place, out int n) ? n : -1;
    }

    // Edits the place's point in the map prefab: copy is given the prefab's root and the point; saves when it returns true.
    static string EditPoint(CampaignEncounter place, string undoName, Func<GameObject, CampaignPoint, string> edit)
    {
        PrefabUndo.Begin(undoName + " (" + place.displayName + ")", RealmMapPrefab);
        var root = PrefabUtility.LoadPrefabContents(RealmMapPrefab);
        try
        {
            var point = root.GetComponentsInChildren<CampaignPoint>(true).FirstOrDefault(p => p.encounter == place);
            if (point == null) return place.displayName + " is not on the map.";
            string result = edit(root, point);
            PrefabUtility.SaveAsPrefabAsset(root, RealmMapPrefab);
            return result;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
            // No raw Synty atlas stays on the map: any prop not baked yet gets its pixel bake (only new ones are baked).
            CampaignMapBuilder.PixelProps();
            PrefabUndo.Commit();
        }
    }

    // Every prop of another place (not its base), where it stands on that disc.
    public static string CopyProps(CampaignEncounter place, CampaignEncounter from) => EditPoint(place, "Copy props", (root, point) =>
    {
        var source = root.GetComponentsInChildren<CampaignPoint>(true).FirstOrDefault(p => p.encounter == from);
        if (source == null) return from.displayName + " is not on the map.";
        int n = 0;
        foreach (Transform t in source.transform)
        {
            if (t.name == "Base") continue;
            var copy = UnityEngine.Object.Instantiate(t.gameObject, point.transform, false);
            copy.name = t.name; n++;
        }
        return $"Copied {n} prop{(n == 1 ? "" : "s")} from {from.displayName}.";
    });

    // One prop (a copy of the first one of that name on the map) at a free spot on the disc, turned at random.
    public static string AddProp(CampaignEncounter place, string prop) => EditPoint(place, "Add " + prop, (root, point) =>
    {
        var model = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == prop && t.parent != null && t.parent.GetComponent<CampaignPoint>() != null);
        if (model == null) return "No prop called " + prop + " on the map.";
        var copy = UnityEngine.Object.Instantiate(model.gameObject, point.transform, false);
        copy.name = prop;
        var taken = point.transform.Cast<Transform>().Where(t => t != copy.transform && t.name != "Base").Select(t => t.localPosition).ToList();
        Vector3 best = Vector3.zero; float bestGap = -1;
        var rng = new System.Random(point.transform.childCount * 7919 + prop.Length);
        for (int i = 0; i < 24; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = point.radius * (.2f + .55f * (float)rng.NextDouble());
            var at = new Vector3(Mathf.Cos(a) * r, model.localPosition.y, Mathf.Sin(a) * r);
            float gap = taken.Count == 0 ? 99f : taken.Min(p => (new Vector2(p.x, p.z) - new Vector2(at.x, at.z)).magnitude);
            if (gap > bestGap) { bestGap = gap; best = at; }
        }
        copy.transform.localPosition = best;
        copy.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * model.localRotation;
        return $"Added {prop} to {place.displayName}.";
    });

    public static string ClearProps(CampaignEncounter place) => EditPoint(place, "Clear props", (root, point) =>
    {
        var props = point.transform.Cast<Transform>().Where(t => t.name != "Base").ToList();
        foreach (var t in props) UnityEngine.Object.DestroyImmediate(t.gameObject);
        return $"Took {props.Count} prop{(props.Count == 1 ? "" : "s")} off {place.displayName}.";
    });

    // A level makes it a Level place; no level makes a Level place a placeholder ("not made yet"). Home stays home
    // until a level is picked for it.
    public static void SetPlaceLevel(CampaignEncounter place, TableLevelData level)
    {
        Undo.RecordObject(place, "Set place level");
        place.level = level;
        if (level != null) place.kind = CampaignEncounterKind.Level;
        else if (place.kind == CampaignEncounterKind.Level) place.kind = CampaignEncounterKind.Placeholder;
        Save(place);
    }

    public static void SetPlaceKind(CampaignEncounter place, CampaignEncounterKind kind)
    {
        Undo.RecordObject(place, "Set place kind");
        place.kind = kind;
        Save(place);
    }

    public static void SetPlaceAct(CampaignEncounter place, int act)
    {
        Undo.RecordObject(place, "Set place act");
        place.act = Mathf.Max(1, act);
        Save(place);
    }

    public static void SetEventAct(CampaignEvent e, int act)
    {
        Undo.RecordObject(e, "Set event act");
        e.afterAct = Mathf.Max(1, act);
        Save(e);
    }

    // ── Undo for prefab edits ─────────────────────────────────────────

    // Prefab edits made through PrefabUtility.LoadPrefabContents can't go on Unity's undo, so each World edit of the
    // campaign manager or the realm map keeps the prefab files as they were (and the assets and folders it made or
    // moved), and the World's Undo button puts them back. Each step also keeps a fingerprint of every file as the step
    // left it (Commit): a file changed since (in the inspector, by hand, by another tool) is only put back, and an asset
    // the step made is only deleted, after asking. Kept in Library/AssetStudioUndo (through recompiles), last first.
    public static class PrefabUndo
    {
        const string Dir = "Library/AssetStudioUndo", Index = Dir + "/steps.json";
        [Serializable] class Snap { public string path, before, after; }   // before: the copy of the file; after: its fingerprint as the step left it
        [Serializable] class Made { public string path, after; }           // a file or folder the step made
        [Serializable] class MovedFile { public string from, to; }
        [Serializable] class Step { public string name; public List<Snap> files = new(); public List<Made> created = new(); public List<MovedFile> moved = new(); }
        [Serializable] class State { public List<Step> steps = new(); }

        static State state;
        static Step open;
        static double openedAt;

        static List<Step> Steps
        {
            get
            {
                if (state == null)
                {
                    try { if (File.Exists(Index)) state = JsonUtility.FromJson<State>(File.ReadAllText(Index)); } catch (Exception e) { Debug.LogWarning("[Asset Studio] World undo steps unreadable: " + e.Message); }
                    state ??= new State();
                    state.steps ??= new List<Step>();
                }
                return state.steps;
            }
        }

        static void Write()
        {
            try { Directory.CreateDirectory(Dir); File.WriteAllText(Index, JsonUtility.ToJson(state ?? new State())); }
            catch (Exception e) { Debug.LogWarning("[Asset Studio] Could not keep the World undo steps: " + e.Message); }
        }

        static string Fingerprint(string path)
        {
            if (!File.Exists(path)) return "none";
            using var md5 = System.Security.Cryptography.MD5.Create();
            return Convert.ToBase64String(md5.ComputeHash(File.ReadAllBytes(path)));
        }

        // A new step, keeping the prefabs as they are now. Calls within the same moment (one button) join one step.
        public static void Begin(string name, params string[] prefabs)
        {
            var steps = Steps;
            if (open == null || !steps.Contains(open) || EditorApplication.timeSinceStartup - openedAt > .5)
            {
                open = new Step { name = name };
                steps.Add(open);
                while (steps.Count > 30) { Forget(steps[0]); steps.RemoveAt(0); }
            }
            openedAt = EditorApplication.timeSinceStartup;
            Directory.CreateDirectory(Dir);
            foreach (var p in prefabs)
                if (File.Exists(p) && !open.files.Any(f => f.path == p))
                {
                    string copy = Dir + "/" + Guid.NewGuid().ToString("N") + ".bytes";
                    File.Copy(p, copy);
                    open.files.Add(new Snap { path = p, before = copy });
                }
            Write();
        }
        public static void Created(string path) { if (open != null) { open.created.Add(new Made { path = path }); Write(); } }
        // Keeps the open step joinable a moment longer: a long making (several files) and a Begin inside it are one step.
        public static void Keep() { if (open != null) openedAt = EditorApplication.timeSinceStartup; }
        // A folder that doesn't exist yet: made by this step, removed with it when empty.
        public static void Folder(string path) { if (open != null && !AssetDatabase.IsValidFolder(path)) { open.created.Add(new Made { path = path }); Write(); } }
        public static void Moved(string from, string to) { if (open != null) { open.moved.Add(new MovedFile { from = from, to = to }); Write(); } }

        // Right after the step's writes: the fingerprint of every file as the step left them. A step that changed
        // nothing (the place was on the map already, listed already) is dropped, so Undo never offers it.
        public static void Commit()
        {
            if (open == null) return;
            foreach (var f in open.files) f.after = Fingerprint(f.path);
            foreach (var c in open.created) c.after = AssetDatabase.IsValidFolder(c.path) ? "folder" : Fingerprint(c.path);
            if (open.created.Count == 0 && open.moved.Count == 0 && open.files.All(f => f.after == Fingerprint(f.before)))
            {
                Forget(open);
                Steps.Remove(open);
                open = null;
            }
            Write();
        }

        public static string Last => Steps.Count > 0 ? Steps[Steps.Count - 1].name : null;

        static void Forget(Step step)
        {
            foreach (var f in step.files) if (!string.IsNullOrEmpty(f.before) && File.Exists(f.before)) File.Delete(f.before);
        }

        static string Names(IEnumerable<string> paths) => string.Join(", ", paths.Select(Path.GetFileNameWithoutExtension));

        public static string UndoLast()
        {
            var steps = Steps;
            if (steps.Count == 0) return "Nothing to undo.";
            var step = steps[steps.Count - 1];
            // A file no longer where the step left it (moved or deleted since) is not written there again: a copy at the
            // old path would be a second asset with a new GUID, nothing pointing at it.
            var gone = step.files.Where(f => !File.Exists(f.path)).Select(f => f.path).ToList();
            // A file changed since the step left it: putting it back loses those later changes.
            var changed = step.files.Where(f => !gone.Contains(f.path) && (string.IsNullOrEmpty(f.after) || Fingerprint(f.path) != f.after)).Select(f => f.path).ToList();
            if (gone.Count > 0 && !EditorUtility.DisplayDialog("Undo: " + step.name,
                    $"{Names(gone)} {(gone.Count == 1 ? "is" : "are")} no longer where this step left {(gone.Count == 1 ? "it" : "them")} (moved or deleted since), so {(gone.Count == 1 ? "it" : "they")} can't be put back. Undo the rest of the step?",
                    "Undo the rest", "Cancel"))
                return "Not undone: " + step.name + ".";
            if (changed.Count > 0 && !EditorUtility.DisplayDialog("Undo: " + step.name,
                    $"{Names(changed)} changed after this step (in the inspector, by hand or by another tool). Undoing puts {(changed.Count == 1 ? "it" : "them")} back as before the step, and those later changes are lost.",
                    "Undo anyway", "Cancel"))
                return "Not undone: " + step.name + ".";
            // An asset the step made and someone edited since: deleted only when asked.
            var edited = step.created.Where(c => !AssetDatabase.IsValidFolder(c.path) && File.Exists(c.path) && (string.IsNullOrEmpty(c.after) || Fingerprint(c.path) != c.after)).Select(c => c.path).ToList();
            bool keepEdited = false;
            if (edited.Count > 0)
            {
                int pick = EditorUtility.DisplayDialogComplex("Undo: " + step.name,
                    $"{Names(edited)} {(edited.Count == 1 ? "was" : "were")} edited after this step made {(edited.Count == 1 ? "it" : "them")}. Undoing deletes {(edited.Count == 1 ? "it" : "them")}.",
                    "Delete", "Cancel", "Undo, keep " + (edited.Count == 1 ? "it" : "them"));
                if (pick == 1) return "Not undone: " + step.name + ".";
                keepEdited = pick == 2;
            }
            steps.RemoveAt(steps.Count - 1);
            open = null;
            var problems = gone.Select(p => Path.GetFileNameWithoutExtension(p) + " is gone from " + Path.GetDirectoryName(p).Replace('\\', '/')).ToList();
            // Each part on its own: one that fails (a file locked, a folder gone) doesn't stop the rest, and the step is
            // taken off the list either way.
            void Try(string what, Action act) { try { act(); } catch (Exception e) { problems.Add(what + ": " + e.Message); } }
            foreach (var f in step.files)
                if (!gone.Contains(f.path) && !string.IsNullOrEmpty(f.before) && File.Exists(f.before))
                    Try(Path.GetFileName(f.path), () => { File.Copy(f.before, f.path, true); AssetDatabase.ImportAsset(f.path, ImportAssetOptions.ForceUpdate); });
            for (int i = step.moved.Count - 1; i >= 0; i--)
            {
                var mv = step.moved[i];
                if (File.Exists(mv.to) && !File.Exists(mv.from))
                    Try(Path.GetFileName(mv.to), () => { string err = AssetDatabase.MoveAsset(mv.to, mv.from); if (!string.IsNullOrEmpty(err)) throw new Exception(err); });
            }
            for (int i = step.created.Count - 1; i >= 0; i--)
            {
                string c = step.created[i].path;
                if (AssetDatabase.IsValidFolder(c)) { if (Directory.Exists(c) && Directory.GetFileSystemEntries(c).Length == 0) Try(c, () => AssetDatabase.DeleteAsset(c)); }
                else if (File.Exists(c) && !(keepEdited && edited.Contains(c))) Try(Path.GetFileName(c), () => AssetDatabase.DeleteAsset(c));
            }
            Forget(step);
            Write();
            AssetDatabase.Refresh();
            return "Undid: " + step.name + "." + (problems.Count > 0 ? " Left as it is: " + string.Join("; ", problems) + "." : "");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────

    static void Save(UnityEngine.Object o) { EditorUtility.SetDirty(o); AssetDatabase.SaveAssetIfDirty(o); }

    public static string CleanName(string s)
    {
        s = (s ?? "").Trim();
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        return s;
    }

    // A short lower-case id (the campaign's own style: "crypt", "village"), free among the others.
    static string FreeId(string name, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken.Where(t => !string.IsNullOrEmpty(t)));
        string words = new string(name.ToLowerInvariant().Where(c => c != '\'' && c != '’').Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray());
        var parts = words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != "the" && w != "a" && w != "of").ToArray();
        string id = parts.Length > 0 ? string.Join("-", parts) : "place";
        string free = id; int n = 2;
        while (used.Contains(free)) free = id + "-" + n++;
        return free;
    }
}

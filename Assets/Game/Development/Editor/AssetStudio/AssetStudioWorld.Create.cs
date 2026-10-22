using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's World: making and arranging things. Every New puts the file in its folder and links it where it belongs
// (a level in the catalog, a biome on its level's run, a room in its biome's pool, a place in the campaign manager's list
// and on the map, an event in the manager's events), so it shows up in the game at once. Every change is undoable and
// saved straight away. A level's asset name is how saved runs find it: a new one gets a name no other level has.
public static partial class AssetStudioWorld
{
    // ── Levels, biomes, rooms ─────────────────────────────────────────

    // A new level made from an existing one (its look, layout and rules): Levels/<name>/<name>.asset with Biomes and
    // Rooms folders, its own copy of each of the template's biomes when ownBiomes (else it shares them), in the catalog.
    public static TableLevelData NewLevel(string name, TableLevelData template, bool ownBiomes, out string message)
    {
        name = CleanName(name);
        if (template == null) { message = "Pick a level to start from."; return null; }
        if (name.Length == 0) { message = "Give the level a name."; return null; }
        if (All<TableLevelData>().Any(l => string.Equals(l.name, name, StringComparison.OrdinalIgnoreCase))) { message = $"A level is already called {name}: saved runs find levels by name."; return null; }
        string folder = LevelsRoot + "/" + name;
        EnsureFolder(folder + "/Biomes"); EnsureFolder(folder + "/Rooms");
        string path = folder + "/" + name + ".asset";
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template), path)) { message = "Could not copy " + template.name + "."; return null; }
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(path);
        level.displayName = name;
        if (ownBiomes && level.stages != null)
            for (int i = 0; i < level.stages.Length; i++)
                if (level.stages[i]?.biome != null) level.stages[i].biome = CopyBiome(level.stages[i].biome, folder + "/Biomes", name + " " + level.stages[i].biome.name);
        EditorUtility.SetDirty(level);
        AddToCatalog(level);
        AssetDatabase.SaveAssets();
        message = $"Made {name} from {template.name}" + (ownBiomes ? " with its own biomes." : ", sharing its biomes.");
        return level;
    }

    static DungeonBiome CopyBiome(DungeonBiome source, string folder, string name)
    {
        EnsureFolder(folder);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + CleanName(name) + ".asset");
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path);
        var copy = AssetDatabase.LoadAssetAtPath<DungeonBiome>(path);
        copy.displayName = CleanName(name);
        EditorUtility.SetDirty(copy);
        return copy;
    }

    // A new biome on a level's run (a copy of template, or of the level's last biome), for some floors.
    public static DungeonBiome NewBiome(TableLevelData level, string name, DungeonBiome template, int floors, bool boss, out string message)
    {
        name = CleanName(name);
        template ??= level != null && level.stages != null ? level.stages.LastOrDefault(s => s?.biome != null)?.biome : null;
        if (level == null || template == null) { message = "Pick a level, and a biome to start from."; return null; }
        if (name.Length == 0) { message = "Give the biome a name."; return null; }
        string folder = (LevelFolder(level) ?? Path.GetDirectoryName(AssetDatabase.GetAssetPath(level)).Replace('\\', '/')) + "/Biomes";
        var biome = CopyBiome(template, folder, name);
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

    public static void RemoveStage(TableLevelData level, int index)
    {
        if (level.stages == null || index < 0 || index >= level.stages.Length) return;
        Undo.RecordObject(level, "Remove stage");
        var list = level.stages.ToList(); list.RemoveAt(index); level.stages = list.ToArray();
        Save(level);
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

    // A hand-painted layout of rooms in a row, west to east: room 0 the entrance (the level's starting room unless set), the
    // last the exit with the stairs, the ones between fights; each 7 x 7, a door and a short passage between neighbours.
    // Rooms already set up keep their template, role, height and merchant.
    public static void MakeRoomsInARow(TableLevelData level, int count)
    {
        count = Mathf.Clamp(count, 2, 8);
        // The generator wants a plan at least 14 cells each way: rock above and below the row makes up the depth.
        const int size = 7, gap = 3, margin = 2, minimum = 14;
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
        act = Mathf.Clamp(act, 1, 4);
        string folder = $"{EncountersRoot}/Act {act}";
        EnsureFolder(folder);
        var place = ScriptableObject.CreateInstance<CampaignEncounter>();
        place.id = FreeId(name, All<CampaignEncounter>(EncountersRoot).Select(p => p.id));
        place.displayName = name;
        place.act = act;
        place.kind = level != null ? CampaignEncounterKind.Level : CampaignEncounterKind.Placeholder;
        place.level = level;
        place.description = "";
        AssetDatabase.CreateAsset(place, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));
        ListInManager(place, null);
        string point = PutOnMap(place);
        AssetDatabase.SaveAssets();
        message = $"Made {name} in Act {act}" + (level != null ? $", playing {level.name}" : " (a placeholder until it has a level)") + ". " + point;
        return place;
    }

    // A new event shown once after an act opens: its asset in Campaign/Encounters/Act <act>/<title>, in the manager's events.
    public static CampaignEvent NewEvent(string title, int afterAct, out string message)
    {
        title = CleanName(title);
        if (title.Length == 0) { message = "Give the event a title."; return null; }
        afterAct = Mathf.Clamp(afterAct, 1, 4);
        string folder = $"{EncountersRoot}/Act {afterAct}/{title}";
        EnsureFolder(folder);
        var e = ScriptableObject.CreateInstance<CampaignEvent>();
        e.id = FreeId(title, All<CampaignEvent>("Assets/Game/Campaign").Select(x => x.id));
        e.title = title;
        e.afterAct = afterAct;
        e.text = "";
        e.choices = new[] { new CampaignEvent.Choice { label = "Leave", outcome = "" } };
        AssetDatabase.CreateAsset(e, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{title}.asset"));
        ListInManager(null, e);
        AssetDatabase.SaveAssets();
        message = $"Made the event {title} (after Act {afterAct}). Write its text, picture and choices in the inspector.";
        return e;
    }

    // Appends a place or an event the campaign manager doesn't list yet (it only runs what it lists).
    public static void ListInManager(CampaignEncounter place, CampaignEvent ev)
    {
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
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // A point for the place on the map, under Points, beside the act's other places (or the map's middle).
    public static string PutOnMap(CampaignEncounter place)
    {
        var root = PrefabUtility.LoadPrefabContents(RealmMapPrefab);
        try
        {
            var points = root.GetComponentsInChildren<CampaignPoint>(true);
            if (points.Any(p => p.encounter == place)) return "It is already on the map.";
            var parent = root.transform.Find("Points") ?? points.FirstOrDefault()?.transform.parent ?? root.transform;
            var near = points.Where(p => p.encounter != null && p.encounter.act == place.act).Select(p => p.transform.localPosition).ToList();
            if (near.Count == 0) near = points.Select(p => p.transform.localPosition).ToList();
            Vector3 centre = near.Count > 0 ? near.Aggregate(Vector3.zero, (a, b) => a + b) / near.Count : Vector3.zero;
            // A free spot: round the act's middle, away from every other place.
            Vector3 spot = centre;
            for (int i = 0; i < 24; i++)
            {
                float angle = i * 137.5f * Mathf.Deg2Rad, radius = 6f + i * 1.5f;
                var tryAt = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (points.All(p => (p.transform.localPosition - tryAt).sqrMagnitude > 64f)) { spot = tryAt; break; }
            }
            var go = new GameObject(place.displayName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = spot;
            var point = go.AddComponent<CampaignPoint>();
            point.encounter = place;
            PrefabUtility.SaveAsPrefabAsset(root, RealmMapPrefab);
            return "It stands on the map near the other Act " + place.act + " places, with no road yet: move it and add roads in the Realm map prefab.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
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

    public static int PropCount(CampaignEncounter place)
    {
        var map = AssetDatabase.LoadAssetAtPath<GameObject>(RealmMapPrefab);
        var point = map != null ? map.GetComponentsInChildren<CampaignPoint>(true).FirstOrDefault(p => p.encounter == place) : null;
        return point == null ? -1 : point.transform.Cast<Transform>().Count(t => t.name != "Base");
    }

    // Edits the place's point in the map prefab: copy is given the prefab's root and the point; saves when it returns true.
    static string EditPoint(CampaignEncounter place, Func<GameObject, CampaignPoint, string> edit)
    {
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
        }
    }

    // Every prop of another place (not its base), where it stands on that disc.
    public static string CopyProps(CampaignEncounter place, CampaignEncounter from) => EditPoint(place, (root, point) =>
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
    public static string AddProp(CampaignEncounter place, string prop) => EditPoint(place, (root, point) =>
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

    public static string ClearProps(CampaignEncounter place) => EditPoint(place, (root, point) =>
    {
        var props = point.transform.Cast<Transform>().Where(t => t.name != "Base").ToList();
        foreach (var t in props) UnityEngine.Object.DestroyImmediate(t.gameObject);
        return $"Took {props.Count} prop{(props.Count == 1 ? "" : "s")} off {place.displayName}.";
    });

    public static void SetPlaceLevel(CampaignEncounter place, TableLevelData level)
    {
        Undo.RecordObject(place, "Set place level");
        place.level = level;
        if (level != null && place.kind == CampaignEncounterKind.Placeholder) place.kind = CampaignEncounterKind.Level;
        Save(place);
    }

    public static void SetPlaceAct(CampaignEncounter place, int act)
    {
        Undo.RecordObject(place, "Set place act");
        place.act = Mathf.Clamp(act, 1, 4);
        Save(place);
    }

    public static void SetEventAct(CampaignEvent e, int act)
    {
        Undo.RecordObject(e, "Set event act");
        e.afterAct = Mathf.Clamp(act, 1, 4);
        Save(e);
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

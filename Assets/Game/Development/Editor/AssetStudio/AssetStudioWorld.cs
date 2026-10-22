using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's World: how the game's content hangs together, read from the assets themselves each refresh (nothing to
// keep in sync). Campaign > act > places (CampaignEncounter) and events (CampaignEvent); a place plays a level; a level
// (TableLevelData) runs stages, each a biome for some floors; a biome has a pool of hand-built rooms, enemies, loot and a
// guardian. A room belongs to a level through the fields that name it (the level's starting, shop and authored rooms,
// its biomes' pools and boss arenas, room prefabs nested in its map template, and rooms behind a portal in those),
// never through folders, so the World view
// always tells the truth and Tidy can move files to match it. Levels under Development (the debug arena) and the pocket
// level are tools, not places: they don't make a room "shared".
public static partial class AssetStudioWorld
{
    public const string LevelsRoot = "Assets/Game/Levels", SharedRooms = LevelsRoot + "/Shared/Rooms", SharedBiomes = LevelsRoot + "/Shared/Biomes";
    public const string CampaignManagerPrefab = "Assets/Game/Campaign/Prefabs/CampaignManager.prefab", RealmMapPrefab = "Assets/Game/Campaign/Map/Realm map.prefab";
    public const string CatalogPath = LevelsRoot + "/Resources/TableLevels.asset", EncountersRoot = "Assets/Game/Campaign/Encounters";
    // What every level shares (chests, the merchant, breakables, features, wall lights, the run's HUD pieces).
    public const string DefaultsPath = LevelsRoot + "/Resources/Table level defaults.asset";

    public class RoomUse
    {
        public TableLevelData level;
        public DungeonBiome biome;      // null for the level's own rooms
        public string role;             // "Pool", "Starting room", "Shop", "Boss arena", "Authored room", "In its map", "Behind a portal"
    }

    public class Stage
    {
        public int index, firstFloor, lastFloor;
        public DungeonBiome biome;
        public bool boss;
    }

    public class Level
    {
        public TableLevelData data;
        public string path;
        public bool inCatalog, tool;    // tool: a debug or pocket level
        public readonly List<Stage> stages = new();
        public readonly List<CampaignEncounter> places = new();
        public readonly List<string> problems = new();
    }

    public class Model
    {
        public readonly List<Level> levels = new();
        public readonly List<DungeonBiome> biomes = new();
        public readonly List<GameObject> rooms = new();
        public readonly List<DungeonMapTemplate> maps = new();
        public readonly Dictionary<DungeonMapTemplate, List<TableLevelData>> mapLevels = new();
        public readonly Dictionary<GameObject, List<RoomUse>> roomUses = new();
        public readonly Dictionary<DungeonBiome, List<Level>> biomeLevels = new();
        public readonly List<CampaignEncounter> places = new();
        public readonly List<CampaignEvent> events = new();
        public readonly HashSet<CampaignEncounter> listedPlaces = new(), placesOnMap = new();
        public readonly HashSet<CampaignEvent> listedEvents = new();
        public readonly List<string> campaignProblems = new();

        public Level LevelOf(TableLevelData d) => levels.FirstOrDefault(l => l.data == d);
        public List<RoomUse> UsesOf(GameObject room) => room != null && roomUses.TryGetValue(room, out var u) ? u : new List<RoomUse>();
        // The real (non-tool) levels a room is in.
        public List<TableLevelData> LevelsOf(GameObject room) => UsesOf(room).Where(u => !IsTool(u.level)).Select(u => u.level).Distinct().ToList();
        public List<Level> LevelsOf(DungeonBiome b) => b != null && biomeLevels.TryGetValue(b, out var l) ? l : new List<Level>();
        public int Acts => Mathf.Max(4, Mathf.Max(places.Select(p => p.act).DefaultIfEmpty(1).Max(), events.Select(e => e.afterAct).DefaultIfEmpty(1).Max()));
        // The real (non-tool) levels that play a map template.
        public List<TableLevelData> MapLevels(DungeonMapTemplate map) => map != null && mapLevels.TryGetValue(map, out var l) ? l.Where(x => !IsTool(x)).ToList() : new List<TableLevelData>();
    }

    public static bool IsTool(TableLevelData l) => l != null && (AssetDatabase.GetAssetPath(l).StartsWith("Assets/Game/Development/") || AssetDatabase.GetAssetPath(l).Contains("/Pockets/"));

    static IEnumerable<T> All<T>(string root = "Assets/Game") where T : UnityEngine.Object =>
        AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { root }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !p.Contains("/_Archive/")).Select(AssetDatabase.LoadAssetAtPath<T>).Where(a => a != null);

    public static Model Build()
    {
        var m = new Model();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/_Archive/")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            if (go.GetComponent<DungeonRoomTemplate>() != null) { m.rooms.Add(go); m.roomUses[go] = new List<RoomUse>(); }
            if (go.TryGetComponent<DungeonMapTemplate>(out var mapTemplate)) m.maps.Add(mapTemplate);
        }
        m.rooms.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        m.maps.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        m.biomes.AddRange(All<DungeonBiome>().OrderBy(b => b.name));
        var catalog = AssetDatabase.LoadAssetAtPath<TableLevelCatalog>(CatalogPath);

        foreach (var data in All<TableLevelData>().OrderBy(l => l.name))
        {
            var level = new Level { data = data, path = AssetDatabase.GetAssetPath(data), tool = IsTool(data) };
            level.inCatalog = catalog != null && catalog.levels != null && Array.IndexOf(catalog.levels, data) >= 0;
            m.levels.Add(level);
            void Use(GameObject room, DungeonBiome biome, string role)
            {
                if (room == null || !m.roomUses.TryGetValue(room, out var list)) return;
                if (list.Any(u => u.level == data && u.biome == biome && u.role == role)) return;
                list.Add(new RoomUse { level = data, biome = biome, role = role });
                // A portal in this room leads to another room: that one is in the level too.
                foreach (var portal in room.GetComponentsInChildren<PortalArch>(true))
                    if (portal.room != null && portal.room != room) Use(portal.room, biome, "Behind a portal");
            }
            Use(data.startingRoom, null, "Starting room");
            Use(data.shopRoom, null, "Shop");
            if (data.authoredRooms != null) foreach (var a in data.authoredRooms) if (a != null) Use(a.template, null, "Authored room");
            if (data.mapTemplate != null)
            {
                if (!m.mapLevels.TryGetValue(data.mapTemplate, out var ml)) m.mapLevels[data.mapTemplate] = ml = new List<TableLevelData>();
                ml.Add(data);
                // Room prefabs nested in the map (the Arena's pit under its Room child) are the level's too.
                foreach (var t in data.mapTemplate.GetComponentsInChildren<Transform>(true))
                    if (t != data.mapTemplate.transform && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject) is GameObject source)
                        Use(source, null, "In its map");
            }
            int floor = 1;
            if (data.stages != null)
                for (int i = 0; i < data.stages.Length; i++)
                {
                    var s = data.stages[i];
                    if (s == null) continue;
                    var stage = new Stage { index = i, biome = s.biome, boss = s.boss, firstFloor = floor, lastFloor = floor + Mathf.Max(1, s.floors) - 1 };
                    floor = stage.lastFloor + 1;
                    level.stages.Add(stage);
                    if (s.biome == null) continue;
                    if (!m.biomeLevels.TryGetValue(s.biome, out var bl)) m.biomeLevels[s.biome] = bl = new List<Level>();
                    if (!bl.Contains(level)) bl.Add(level);
                    if (s.biome.roomTemplates != null) foreach (var c in s.biome.roomTemplates) if (c != null) Use(c.template, s.biome, "Pool");
                    if (s.biome.guardian != null && s.biome.guardian.enabled) Use(s.biome.guardian.arena, s.biome, "Boss arena");
                }
        }

        // The campaign: places and events, what the manager lists and what stands on the map.
        m.places.AddRange(All<CampaignEncounter>(EncountersRoot).OrderBy(p => p.act).ThenBy(p => p.displayName));
        m.events.AddRange(All<CampaignEvent>("Assets/Game/Campaign").OrderBy(e => e.afterAct).ThenBy(e => e.title));
        var manager = AssetDatabase.LoadAssetAtPath<GameObject>(CampaignManagerPrefab)?.GetComponent<CampaignManager>();
        if (manager != null)
        {
            if (manager.encounters != null) foreach (var e in manager.encounters) if (e != null) m.listedPlaces.Add(e);
            if (manager.events != null) foreach (var e in manager.events) if (e != null) m.listedEvents.Add(e);
        }
        var map = AssetDatabase.LoadAssetAtPath<GameObject>(RealmMapPrefab);
        if (map != null) foreach (var p in map.GetComponentsInChildren<CampaignPoint>(true)) if (p.encounter != null) m.placesOnMap.Add(p.encounter);
        foreach (var p in m.places)
            if (p.kind == CampaignEncounterKind.Level && p.level != null) m.LevelOf(p.level)?.places.Add(p);
        foreach (var dup in m.places.Where(p => !string.IsNullOrEmpty(p.id)).GroupBy(p => p.id).Where(g => g.Count() > 1))
            m.campaignProblems.Add($"Two places share the id \"{dup.Key}\": {string.Join(", ", dup.Select(p => p.displayName))}.");
        foreach (var dup in m.events.Where(e => !string.IsNullOrEmpty(e.id)).GroupBy(e => e.id).Where(g => g.Count() > 1))
            m.campaignProblems.Add($"Two events share the id \"{dup.Key}\".");

        foreach (var l in m.levels) Check(m, l);
        return m;
    }

    // ── Health ────────────────────────────────────────────────────────

    static void Check(Model m, Level l)
    {
        var d = l.data;
        var p = l.problems;
        if (l.tool) return;
        if (!l.inCatalog) p.Add("Not in the level catalog (TableLevels): the game can't load it by name.");
        if (!d.IsDungeon) return;
        if (l.stages.Count == 0) p.Add("No stages: add a biome on its Run tab.");
        foreach (var s in l.stages)
        {
            if (s.biome == null) { p.Add($"Stage {s.index + 1} has no biome."); continue; }
            if (s.biome.enemies == null || !s.biome.enemies.Any(e => e != null && e.enemy != null)) p.Add($"{s.biome.name}: no enemies.");
            if (s.biome.loot == null) p.Add($"{s.biome.name}: no loot source.");
            if (s.biome.roomTemplates == null || s.biome.roomTemplates.Length == 0) p.Add($"{s.biome.name}: no hand-built rooms in its pool (every room is generated).");
            else
            {
                int empty = s.biome.roomTemplates.Count(c => c == null || c.template == null);
                if (empty > 0) p.Add($"{s.biome.name}: {empty} empty entr{(empty == 1 ? "y" : "ies")} in its room pool.");
                foreach (var c in s.biome.roomTemplates)
                    if (c != null && c.template != null && c.template.GetComponent<DungeonRoomTemplate>() == null) p.Add($"{s.biome.name}: {c.template.name} in its pool is not a room template.");
            }
            if (s.boss && (s.biome.guardian == null || !s.biome.guardian.enabled || s.biome.guardian.boss == null)) p.Add($"Stage {s.index + 1} ({s.biome.name}) has Boss on last floor, but the biome has no guardian.");
        }
        if (d.layoutMode != DungeonLayoutMode.Authored && d.startingRoom == null) p.Add("No starting room.");
        if (d.layoutMode == DungeonLayoutMode.Authored)
        {
            if (d.mapTemplate != null) foreach (var mp in d.mapTemplate.Problems()) p.Add($"Map {d.mapTemplate.name}: {mp}");
            else if (string.IsNullOrWhiteSpace(d.authoredMap)) p.Add("Hand-painted layout with no floor plan: give it a map template or lay it out as rooms in a row.");
        }
        if (l.places.Count == 0) p.Add("Not on the campaign map: no place plays it.");
    }

    // ── Tidy: files follow the World ─────────────────────────────────

    public class Move { public string from, to, why; public bool on = true; }

    // Paths written in code (a builder or a loader writes or reads them there): left where they are. Their folders too.
    public static HashSet<string> PinnedPaths()
    {
        var pins = new HashSet<string>();
        foreach (var file in Directory.GetFiles("Assets/Game", "*.cs", SearchOption.AllDirectories))
        {
            if (file.Replace('\\', '/').Contains("/AssetStudio/")) continue;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"(Assets/Game/[^\"{]+)\""))
                pins.Add(match.Groups[1].Value.TrimEnd('/'));
        }
        return pins;
    }

    static bool Pinned(string path, HashSet<string> pins)
    {
        if (path.StartsWith("Assets/Game/Campaign/") || path.StartsWith(LevelsRoot + "/Arena/") || path.StartsWith(LevelsRoot + "/Resources/") || path.StartsWith("Assets/Game/Development/")) return true;
        if (pins.Contains(path)) return true;
        // A folder named in code pins the files right in it (a builder writes there), not its subfolders.
        string dir = Path.GetDirectoryName(path).Replace('\\', '/');
        return pins.Contains(dir);
    }

    // The level folder a level's own files go in: Levels/<Level>/ for a level in Levels, else none.
    public static string LevelFolder(TableLevelData l)
    {
        string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(l)).Replace('\\', '/');
        return dir.StartsWith(LevelsRoot + "/") && dir.Count(c => c == '/') == LevelsRoot.Count(c => c == '/') + 1 ? dir : null;
    }

    // Each room in Levels/<Level>/Rooms when one level uses it, Levels/Shared/Rooms when several do; each biome in
    // Levels/<Level>/Biomes or Levels/Shared/Biomes the same way. Unused things stay where they are (they show as unused).
    public static List<Move> TidyPlan(Model m, out List<string> kept)
    {
        var moves = new List<Move>();
        var keptList = new List<string>();
        var pins = PinnedPaths();
        void Plan(UnityEngine.Object asset, List<TableLevelData> users, string sub, string shared)
        {
            if (asset == null || users.Count == 0) return;
            string from = AssetDatabase.GetAssetPath(asset);
            string folder = users.Count == 1 ? (LevelFolder(users[0]) is string f ? f + "/" + sub : null) : shared;
            if (folder == null) return;
            string to = folder + "/" + Path.GetFileName(from);
            if (to == from || Path.GetDirectoryName(from).Replace('\\', '/').StartsWith(folder + "/")) return;   // already there (or in a subfolder of it)
            string why = users.Count == 1 ? $"only {users[0].name} uses it" : $"used by {string.Join(", ", users.Select(u => u.name))}";
            if (Pinned(from, pins)) { keptList.Add($"{from}: a tool writes or reads this path in code ({why})."); return; }
            moves.Add(new Move { from = from, to = to, why = why });
        }
        foreach (var room in m.rooms) Plan(room, m.LevelsOf(room), "Rooms", SharedRooms);
        foreach (var b in m.biomes) Plan(b, m.LevelsOf(b).Where(l => !l.tool).Select(l => l.data).ToList(), "Biomes", SharedBiomes);
        kept = keptList;
        return moves;
    }

    // Moves the files (Unity keeps every reference: they are by GUID). Returns how many moved, any renames (a file of
    // that name was already there) and any errors; done lists each move made, from and to, for Move back.
    public static string ApplyTidy(List<Move> moves, out List<(string from, string to)> done)
    {
        done = new List<(string from, string to)>();
        var errors = new List<string>(); var renamed = new List<string>();
        // Folders first: one made inside the batch below isn't known to the moves yet.
        foreach (var mv in moves.Where(x => x.on)) EnsureFolder(Path.GetDirectoryName(mv.to).Replace('\\', '/'));
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var mv in moves.Where(x => x.on))
            {
                string to = AssetDatabase.GenerateUniqueAssetPath(mv.to);
                string err = AssetDatabase.MoveAsset(mv.from, to);
                if (!string.IsNullOrEmpty(err)) { errors.Add($"{mv.from}: {err}"); continue; }
                done.Add((mv.from, to));
                if (to != mv.to) renamed.Add($"{Path.GetFileName(mv.from)} as {Path.GetFileName(to)}");
            }
        }
        finally { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
        return $"Moved {done.Count} file{(done.Count == 1 ? "" : "s")}." + (renamed.Count > 0 ? " Renamed (the name was taken): " + string.Join(", ", renamed) + "." : "")
            + (errors.Count > 0 ? " Could not move: " + string.Join("; ", errors) : "");
    }

    // Puts files moved by ApplyTidy back where they were (each only while it is still where Tidy put it).
    public static string MoveBack(List<(string from, string to)> done)
    {
        int back = 0; var errors = new List<string>();
        // A folder emptied by the Tidy and removed since is made again first (not inside the batch, as in ApplyTidy).
        foreach (var (from, to) in done) if (File.Exists(to) && !File.Exists(from)) EnsureFolder(Path.GetDirectoryName(from).Replace('\\', '/'));
        AssetDatabase.StartAssetEditing();
        try
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                var (from, to) = done[i];
                if (!File.Exists(to)) { errors.Add($"{Path.GetFileName(to)} is no longer in {Path.GetDirectoryName(to).Replace('\\', '/')}"); continue; }
                if (File.Exists(from)) { errors.Add($"{from} is taken"); continue; }
                string err = AssetDatabase.MoveAsset(to, from);
                if (string.IsNullOrEmpty(err)) back++; else errors.Add($"{to}: {err}");
            }
        }
        finally { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); }
        return $"Moved {back} file{(back == 1 ? "" : "s")} back." + (errors.Count > 0 ? " Left: " + string.Join("; ", errors) : "");
    }

    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        // On disk but not imported yet (made inside an asset-editing batch): import it, or CreateFolder makes "<name> 1".
        if (Directory.Exists(folder)) { AssetDatabase.ImportAsset(folder); return; }
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}

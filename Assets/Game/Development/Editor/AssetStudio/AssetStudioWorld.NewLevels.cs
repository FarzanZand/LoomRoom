using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's World: the two guided New level flows. A one-floor encounter (a hand-built map of a few rooms, like
// the Arena or the Peddler's Cellar) and a generated dungeon of several floors (stages of biomes). Each makes the level
// in Levels/<name>/ from a level of the same kind (its settings: look, doors, torches, chests, shop, dialogue), runs it
// on its base biome or its stages, puts it in the level catalog and, if asked, on the campaign map. The whole making is
// one step of the World's Undo (PrefabUndo): it deletes the files made and puts the catalog, the campaign manager and
// the realm map back.
public static partial class AssetStudioWorld
{
    public class EncounterPlan
    {
        public string name = "";
        public int act = 1;
        public bool board, boss, place = true;
        public DungeonMapTemplate copyMap;   // null: a new blank map of rooms in a row
        public int rooms = 3;
        public DungeonBiome contentFrom;     // its enemies, loot and look go in the base biome
        public TableLevelData settingsFrom;  // null: picked by kind (SettingsLevel)
    }

    public class StagePlan
    {
        public DungeonBiome biome;
        public bool copy;                    // a new biome made from it, in the level's Biomes folder
        public string copyName = "";
        public int floors = 2;
        public bool boss;
        public string Name => copy || IsBase(biome) ? CleanName(copyName) : biome != null ? biome.name : "";
    }

    public class DungeonPlan
    {
        public string name = "";
        public int act = 1;
        public bool board, place = true;
        public readonly List<StagePlan> stages = new();
        public TableLevelData balanceFrom;   // its balance is copied into the level's folder
        public int gear;                     // GearPresets index; the last keeps the settings level's rows
        public TableLevelData settingsFrom;  // null: picked by kind (SettingsLevel)
        public int Floors => stages.Sum(s => Mathf.Max(1, s.floors));
    }

    public static readonly string[] GearPresets = { "Bronze to Iron", "Iron to Steel", "Steel to Crystal", "As the settings level has it" };

    // ── Checks ───────────────────────────────────────────────────────

    static readonly char[] Invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).Distinct().ToArray();

    // Why a name can't be a new level's (null: it can): its folder and asset are named after it, and saved runs find a
    // level by its asset's name.
    // levels: the levels to check against (the World's, so a form asks no asset scan); null reads them.
    public static string LevelNameProblem(string raw, IEnumerable<TableLevelData> levels = null)
    {
        string name = (raw ?? "").Trim();
        if (name.Length == 0) return "Give the level a name.";
        if (name.IndexOfAny(Invalid) >= 0) return "A name can't have any of these: / \\ : * ? \" < > |";
        if (name.StartsWith(".") || name.EndsWith(".")) return "A name can't start or end with a dot.";
        if (name.Length > 60) return "Keep the name under 60 letters.";
        string folder = LevelsRoot + "/" + name;
        if (AssetDatabase.IsValidFolder(folder) || Directory.Exists(folder) || File.Exists(folder + ".asset")) return $"Levels/{name} is already there: pick another name.";
        if ((levels ?? All<TableLevelData>()).Any(l => l != null && string.Equals(l.name, name, StringComparison.OrdinalIgnoreCase))) return $"A level is already called {name}: saved runs find levels by name.";
        return null;
    }

    // A level to take the settings from (look, doors, torches, chests, shop, dialogue): one in the catalog of the same
    // kind (board or walled), a hand-built one for an encounter and a generated one for a dungeon when there is one.
    public static TableLevelData SettingsLevel(Model m, bool board, bool authored)
    {
        var candidates = m.levels.Where(l => !l.tool && l.inCatalog && l.data.IsDungeon && !l.data.pocket && l.data.baseBiome != null).Select(l => l.data).ToList();
        var kind = candidates.Where(l => l.IsBoard == board).ToList();
        if (!board && kind.FirstOrDefault(l => l.name == "Dungeon1") is TableLevelData d1 && !authored) return d1;
        return kind.FirstOrDefault(l => (l.layoutMode == DungeonLayoutMode.Authored) == authored) ?? kind.FirstOrDefault() ?? candidates.FirstOrDefault();
    }

    // The run line, as the level's Run tab writes it: "Floors 1-2 Tower stores, Floors 3-5 Tower halls (boss on 5)".
    public static string RunLine(IEnumerable<(string name, int floors, bool boss)> stages)
    {
        var parts = new List<string>();
        int start = 1;
        foreach (var (name, floors, boss) in stages)
        {
            int count = Mathf.Max(1, floors), end = start + count - 1;
            parts.Add((count == 1 ? $"Floor {start}" : $"Floors {start}-{end}") + " " + (string.IsNullOrEmpty(name) ? "?" : name) + (boss ? $" (boss on {end})" : ""));
            start = end + 1;
        }
        return string.Join(", ", parts);
    }

    public static bool HasGuardian(DungeonBiome b) => b != null && b.guardian != null && b.guardian.enabled && b.guardian.boss != null;

    // ── Making them ──────────────────────────────────────────────────

    public static TableLevelData MakeEncounter(Model m, EncounterPlan p, out DungeonMapTemplate map, out string message)
    {
        map = null;
        if (LevelNameProblem(p.name) is string problem) { message = problem; return null; }
        var from = p.settingsFrom ?? SettingsLevel(m, p.board, true);
        if (from == null) { message = "No level to take the settings from."; return null; }
        string name = CleanName(p.name);
        PrefabUndo.Begin("New level " + name, CampaignManagerPrefab, RealmMapPrefab, CatalogPath);
        try
        {
            var level = LevelFile(name, from, p.board, out string folder);
            if (level == null) { message = "Could not copy " + from.name + "."; return null; }
            level.floors = 1;
            level.layoutMode = DungeonLayoutMode.Authored;
            BaseFrom(level, p.contentFrom, name);
            level.bossOnLastFloor = p.boss && HasGuardian(level.baseBiome);
            Save(level.baseBiome); Save(level);

            // Its map: a copy of one, or a new one of rooms in a row with a Room child each.
            string mapNote;
            if (p.copyMap != null)
            {
                string mapPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name} map.prefab");
                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(p.copyMap), mapPath)) { message = "Could not copy " + p.copyMap.name + "."; return level; }
                PrefabUndo.Created(mapPath);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(mapPath);
                map = go != null ? go.GetComponent<DungeonMapTemplate>() : null;
                if (map != null) { map.previewLevel = level; PrefabUtility.SavePrefabAsset(go); }
                mapNote = $"a copy of {p.copyMap.name}";
            }
            else
            {
                map = NewMapTemplate(name + " map", level, p.rooms, false, out _);
                if (map != null) PrefabUndo.Created(AssetDatabase.GetAssetPath(map));
                mapNote = $"a new map of {Mathf.Clamp(p.rooms, 2, 8)} rooms in a row";
            }
            if (map != null) { level.mapTemplate = map; level.SyncMapSize(); }
            Save(level);
            Finish(level, p.place, p.act, name, out string placeNote);
            message = $"Made {name}: one {(p.board ? "board" : "walled")} floor on {mapNote}, settings from {from.name}"
                + (p.contentFrom != null ? $", enemies, loot and look from {p.contentFrom.name}" : "") + ". " + placeNote
                + " Build each room under its Room child in the map, then Play it.";
            return level;
        }
        finally { AssetDatabase.SaveAssets(); PrefabUndo.Commit(); }
    }

    public static TableLevelData MakeDungeon(Model m, DungeonPlan p, out string message)
    {
        if (LevelNameProblem(p.name) is string problem) { message = problem; return null; }
        if (DungeonPlanProblem(p) is string planProblem) { message = planProblem; return null; }
        var from = p.settingsFrom ?? SettingsLevel(m, p.board, false);
        if (from == null) { message = "No level to take the settings from."; return null; }
        string name = CleanName(p.name);
        PrefabUndo.Begin("New level " + name, CampaignManagerPrefab, RealmMapPrefab, CatalogPath);
        try
        {
            var level = LevelFile(name, from, p.board, out string folder);
            if (level == null) { message = "Could not copy " + from.name + "."; return null; }
            level.layoutMode = DungeonLayoutMode.Grown;
            level.floors = p.Floors;
            level.bossOnLastFloor = false;
            // The way in from Dungeon1: its starting room, door and the starting chest's potion.
            var d1 = m.levels.FirstOrDefault(l => l.data.name == "Dungeon1")?.data;
            if (d1 != null && d1 != from) { level.startingRoom = d1.startingRoom; level.doorPrefab = d1.doorPrefab; level.startingChestConsumable = d1.startingChestConsumable; }

            // The stages: each a biome as it is, or a new biome made from it in the level's Biomes folder.
            var stages = new List<TableLevelData.BiomeStage>();
            foreach (var s in p.stages)
            {
                var biome = s.biome;
                if (s.copy || IsBase(s.biome))
                {
                    PrefabUndo.Folder(folder + "/Biomes");
                    biome = CopyBiome(s.biome, folder + "/Biomes", s.Name, s.Name);
                    PrefabUndo.Created(AssetDatabase.GetAssetPath(biome));
                }
                stages.Add(new TableLevelData.BiomeStage { biome = biome, floors = Mathf.Max(1, s.floors), boss = s.boss });
            }
            level.stages = stages.ToArray();

            // Its own balance, copied from a level's.
            var balance = p.balanceFrom != null ? p.balanceFrom.balance : null;
            if (balance != null)
            {
                string balancePath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name} balance.asset");
                if (AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(balance), balancePath))
                {
                    PrefabUndo.Created(balancePath);
                    level.balance = AssetDatabase.LoadAssetAtPath<DungeonBalance>(balancePath);
                }
            }
            if (p.gear >= 0 && p.gear < GearPresets.Length - 1) level.gearByFloor = GearRows(p.gear, p.Floors);
            Save(level);
            Finish(level, p.place, p.act, name, out string placeNote);
            message = $"Made {name}: {RunLine(p.stages.Select(s => (s.Name, s.floors, s.boss && HasGuardian(s.biome))))}, settings from {from.name}"
                + (level.balance != null && balance != null ? $", balance copied from {p.balanceFrom.name}" : "") + ". " + placeNote;
            return level;
        }
        finally { AssetDatabase.SaveAssets(); PrefabUndo.Commit(); }
    }

    public static string DungeonPlanProblem(DungeonPlan p)
    {
        if (p.stages.Count == 0) return "Add a stage.";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < p.stages.Count; i++)
        {
            var s = p.stages[i];
            if (s.biome == null) return $"Stage {i + 1} has no biome.";
            if (s.copy || IsBase(s.biome))
            {
                if (s.copyName == null || s.copyName.Trim().Length == 0) return $"Name the new biome of stage {i + 1}.";
                if (s.copyName.IndexOfAny(Invalid) >= 0) return $"Stage {i + 1}'s biome name can't have any of these: / \\ : * ? \" < > |";
                if (!names.Add(s.Name)) return $"Two new biomes are called {s.Name}.";
            }
        }
        if (p.Floors > 30) return "Thirty floors at most.";
        return null;
    }

    // The level's file: a copy of the settings level in Levels/<name>/, its base biome renamed for it, its run, plan and
    // text emptied (the flow sets them), in the catalog.
    static TableLevelData LevelFile(string name, TableLevelData from, bool board, out string folder)
    {
        folder = LevelsRoot + "/" + name;
        PrefabUndo.Folder(folder);
        EnsureFolder(folder);
        string path = folder + "/" + name + ".asset";
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(from), path)) return null;
        PrefabUndo.Created(path);
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(path);
        level.MakeBaseBiome();
        level.displayName = name;
        level.description = "";
        level.kind = TableLevelKind.Dungeon;
        if (level.baseBiome != null) { level.baseBiome.kind = board ? DungeonFloorKind.Boardgame : DungeonFloorKind.Dungeon; EditorUtility.SetDirty(level.baseBiome); }
        level.stages = new TableLevelData.BiomeStage[0];
        level.pocket = false;
        level.fixedSeed = 0;
        level.mapTemplate = null;
        level.authoredMap = "";
        level.authoredRooms = new TableLevelData.AuthoredRoom[0];
        level.authoredContent = "";
        level.authoredMarkers = new TableLevelData.AuthoredMarker[0];
        EditorUtility.SetDirty(level);
        return level;
    }

    // The base biome takes another biome's content (enemies, loot, look, sound, guardian, pool) and, when that one has
    // them, its room styles, furnishing rules and room features; the Level tab (default style, torches, chests, shop,
    // board pieces) stays the settings level's.
    static void BaseFrom(TableLevelData level, DungeonBiome source, string name)
    {
        var b = level.baseBiome;
        if (b == null) return;
        if (source != null && source != b)
        {
            b.CopyContentFrom(source);
            if (source.roomStyles != null && source.roomStyles.Length > 0) b.roomStyles = DungeonBiome.CloneAll(source.roomStyles);
            if (source.corridorStyles != null && source.corridorStyles.Length > 0) b.corridorStyles = DungeonBiome.CloneAll(source.corridorStyles);
            if (source.propRules != null && source.propRules.Length > 0) b.propRules = DungeonBiome.CloneAll(source.propRules);
            if (source.overrideDungeonSettings || IsBase(source)) b.roomFeatures = DungeonBiome.Clone(source.roomFeatures ?? new DungeonRoomFeatures());
        }
        b.displayName = name;
        EditorUtility.SetDirty(b);
    }

    // In the catalog, and a place on the map in its act (joined to the same Undo step).
    static void Finish(TableLevelData level, bool place, int act, string name, out string note)
    {
        AddToCatalog(level);
        note = "In the level catalog.";
        if (!place) { note += " Not on the campaign map yet."; return; }
        PrefabUndo.Keep();
        var made = NewPlace(name, act, level, out string placeMessage);
        note += made != null ? " " + placeMessage : " No place made: " + placeMessage;
    }

    // Gear by floor going from one tier to the next over the run: the first floor all the lower tier, the last all the
    // higher, the floors between shared in steps of a quarter.
    public static List<LootFloorTiers> GearRows(int preset, int floors)
    {
        int low = Mathf.Clamp(preset, 0, 2) + 1;
        var rows = new List<LootFloorTiers>();
        float lastShare = -1;
        for (int f = 1; f <= Mathf.Max(1, floors); f++)
        {
            float share = floors <= 1 ? 0 : Mathf.Round((f - 1) / (float)(floors - 1) * 4f) * 25f;
            if (Mathf.Approximately(share, lastShare)) continue;
            lastShare = share;
            var row = new LootFloorTiers { fromFloor = f, tier1 = 0, upgradeChance = 0 };
            SetTier(row, low, 100 - share);
            SetTier(row, low + 1, share);
            rows.Add(row);
        }
        return rows;
    }

    static void SetTier(LootFloorTiers row, int tier, float weight)
    {
        row.SetShare(tier, weight);
    }
}

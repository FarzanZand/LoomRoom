using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Room events page measures what a dungeon's floors really hold: it builds runs of the level (each floor
// as the game builds it, in a test scene, as DungeonStressTest does) and counts each floor's rooms by kind and its room
// events, chests and room light. Averages per floor, saved in Library/RoomEventSurvey/<level guid>.json with a hash of
// the settings they were measured with (SettingsHash), so the page can say when a change has made them out of date.
public static class RoomEventSurvey
{
    const string Folder = "Library/RoomEventSurvey";

    [Serializable]
    public class Floor
    {
        public int floor, built;
        public string biome;
        public float rooms, fights, treasure, rest, storage, tavern, handBuilt, quietHandBuilt;
        public float ambushes, sleeping, shrines, builtShrines, chests, trapped, dark, eerie, candlelit, moonlit, pits, sunken, ledges;
        public int withAmbush, withShrine, withSleeping, withTrap;
        // Share of floors with at least one.
        public float Share(int n) => built == 0 ? 0 : n / (float)built;
    }

    [Serializable]
    public class Result
    {
        public int runs, failed;
        public long ticks;      // when it was measured (local time)
        public string hash;     // SettingsHash of the level then
        public List<Floor> floors = new();
        public DateTime At => new(ticks);
    }

    static readonly Dictionary<string, Result> last = new();

    static string FileFor(TableLevelData level) => $"{Folder}/{AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level))}.json";

    // The last measurement of this level: kept in memory, read from its file after a recompile or restart.
    public static Result Last(TableLevelData level)
    {
        if (level == null) return null;
        string key = AssetDatabase.GetAssetPath(level);
        if (last.TryGetValue(key, out var r)) return r;
        string file = FileFor(level);
        r = System.IO.File.Exists(file) ? JsonUtility.FromJson<Result>(System.IO.File.ReadAllText(file)) : null;
        last[key] = r;
        return r;
    }

    // What the numbers depend on: the room count, each floor's biome, its share of fights and of hand-built rooms, and
    // the room feature set it uses. Two measurements with the same hash were made with the same settings.
    public static string SettingsHash(TableLevelData level)
    {
        if (level == null) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append(level.roomCount).Append('|').Append(level.FloorCount).Append('|');
        if (level.baseBiome != null) sb.Append(EditorJsonUtility.ToJson(level.baseBiome.roomFeatures));
        for (int f = 1; f <= level.FloorCount; f++)
        {
            var b = level.Biome(f);
            if (b == null) { sb.Append("|-"); continue; }
            sb.Append('|').Append(b.name).Append(b.encounterChance).Append(b.templateChance).Append(b.overrideDungeonSettings);
            if (b.overrideDungeonSettings) sb.Append(EditorJsonUtility.ToJson(b.roomFeatures));
        }
        // FNV-1a: the same text gives the same hash in every Editor session.
        uint h = 2166136261;
        foreach (char c in sb.ToString()) { h ^= c; h *= 16777619; }
        return h.ToString("x8");
    }

    // Builds `runs` runs of the level (every floor of each) and counts. Null in Play mode (floors are built in a test
    // scene beside the open one, which needs Edit mode) or when cancelled.
    public static Result Measure(TableLevelData level, int runs)
    {
        if (level == null || Application.isPlaying) return null;
        var result = new Result { runs = runs, ticks = DateTime.Now.Ticks, hash = SettingsHash(level) };
        int floors = Mathf.Max(1, level.FloorCount);
        for (int f = 1; f <= floors; f++) result.floors.Add(new Floor { floor = f, biome = level.Biome(f) != null ? level.Biome(f).name : "" });
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = DungeonStressTest.BeginTestScene();
        try
        {
            for (int run = 0; run < runs; run++)
            {
                int seed = 3000 + run * 7919;
                for (int f = 1; f <= floors; f++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Measuring room events", $"{level.name}: run {run + 1} of {runs}, floor {f}", (run * floors + f - 1) / (float)(runs * floors)))
                        return null;
                    var go = new GameObject("Room event survey floor");
                    go.transform.position = new Vector3(500, 0, 500);
                    try
                    {
                        var g = go.AddComponent<DungeonGenerator>();
                        g.Build(level, unchecked(seed + f * 104729), f);
                        Count(g, result.floors[f - 1]);
                    }
                    catch (Exception e) { result.failed++; Debug.LogWarning($"[Room events] {level.name} floor {f}: {e.Message}"); }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            DungeonStressTest.EndTestScene(scene, previous);
        }
        foreach (var fl in result.floors) Average(fl);
        last[AssetDatabase.GetAssetPath(level)] = result;
        System.IO.Directory.CreateDirectory(Folder);
        System.IO.File.WriteAllText(FileFor(level), JsonUtility.ToJson(result, true));
        return result;
    }

    static void Count(DungeonGenerator g, Floor fl)
    {
        fl.built++;
        int n = g.Layout.rooms.Count;
        int ambush = 0, shrine = 0, sleep = 0;
        for (int i = 0; i < n; i++)
        {
            var role = g.Roles[i];
            var t = g.BuiltFrom(i);
            fl.rooms++;
            if (i == g.MerchantRoom) fl.tavern++;
            else if (role == DungeonGenerator.RoomRole.Combat) fl.fights++;
            else if (role == DungeonGenerator.RoomRole.Treasure) fl.treasure++;
            else if (role == DungeonGenerator.RoomRole.Rest) fl.rest++;
            else if (role == DungeonGenerator.RoomRole.Storage) fl.storage++;
            bool middle = i != 0 && role != DungeonGenerator.RoomRole.Exit && i != g.MerchantRoom;
            if (t != null && middle) { fl.handBuilt++; if (role != DungeonGenerator.RoomRole.Combat) fl.quietHandBuilt++; }
            switch (g.EventOf(i))
            {
                case DungeonGenerator.RoomEvent.Ambush: fl.ambushes++; ambush++; break;
                case DungeonGenerator.RoomEvent.SleepingPack: fl.sleeping++; sleep++; break;
                case DungeonGenerator.RoomEvent.Shrine: fl.shrines++; shrine++; break;
            }
            switch (g.LightOf(i))
            {
                case DungeonRoomLight.Dark: fl.dark++; break;
                case DungeonRoomLight.Eerie: fl.eerie++; break;
                case DungeonRoomLight.Candlelit: fl.candlelit++; break;
                case DungeonRoomLight.Moonlit: fl.moonlit++; break;
            }
            if (t != null)
            {
                var needs = t.Requires();
                if ((needs & DungeonRoomFeatureMask.Pit) != 0) fl.pits++;
                if ((needs & DungeonRoomFeatureMask.SunkenFloor) != 0) fl.sunken++;
                if ((needs & DungeonRoomFeatureMask.ArcherLedge) != 0) fl.ledges++;
                // A hand-built room with its own shrine (the Shrine of Ashes) is a shrine too.
                if ((needs & DungeonRoomFeatureMask.ShrineBargain) != 0 && g.EventOf(i) != DungeonGenerator.RoomEvent.Shrine) { fl.shrines++; fl.builtShrines++; shrine++; }
            }
        }
        var chests = g.GetComponentsInChildren<DungeonContainer>(true).Where(c => c.chest).ToList();
        int trapped = chests.Count(c => c.GetComponent<DungeonChestTrap>() != null);
        fl.chests += chests.Count; fl.trapped += trapped;
        if (ambush > 0) fl.withAmbush++;
        if (shrine > 0) fl.withShrine++;
        if (sleep > 0) fl.withSleeping++;
        if (trapped > 0) fl.withTrap++;
    }

    static void Average(Floor fl)
    {
        if (fl.built == 0) return;
        float k = 1f / fl.built;
        fl.rooms *= k; fl.fights *= k; fl.treasure *= k; fl.rest *= k; fl.storage *= k; fl.tavern *= k; fl.handBuilt *= k; fl.quietHandBuilt *= k;
        fl.ambushes *= k; fl.sleeping *= k; fl.shrines *= k; fl.builtShrines *= k; fl.chests *= k; fl.trapped *= k; fl.dark *= k; fl.eerie *= k; fl.candlelit *= k; fl.moonlit *= k;
        fl.pits *= k; fl.sunken *= k; fl.ledges *= k;
    }
}

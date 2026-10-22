using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Test buttons: everything that can be tried in the game gets the same buttons in the library's TRY IT and in the World
// panes. GameLauncher starts each one (the Debug arena for runs, the room for the campaign); a run tests a copy, so
// the level, biome or room is never changed by it.
public partial class AssetStudioWindow
{
    List<(string label, string tip, Action run)> TestActions(Object asset)
    {
        var list = new List<(string label, string tip, Action run)>();
        void Add(string label, string tip, Func<string> run) => list.Add((label, tip, () => Toast(run())));
        switch (asset)
        {
            case TableLevelData level when level.IsDungeon:
                Add("Play this level", "Generate floor 1 of this level and play it.", () => GameLauncher.PlayLevel(level));
                if (level.stages == null) break;
                int floor = 1;
                for (int i = 0; i < level.stages.Length; i++)
                {
                    var s = level.stages[i];
                    if (s == null) continue;
                    string name = s.biome != null ? s.biome.name : "stage " + (i + 1);
                    int at = i;
                    if (i > 0) Add($"From floor {floor} ({name})", $"A run of {level.name} that starts at its {name} floors.", () => GameLauncher.PlayStage(level, at, false));
                    if (s.boss && s.biome != null && s.biome.guardian != null && s.biome.guardian.enabled)
                        Add($"Fight the boss of {name}", $"Straight to {name}'s last floor, where {(s.biome.guardian.boss != null ? s.biome.guardian.boss.name : "its guardian")} waits.", () => GameLauncher.PlayStage(level, at, true));
                    floor += Mathf.Max(1, s.floors);
                }
                break;
            case DungeonBiome biome:
                Add("Play this biome", "A run that starts at this biome's floors: in a level that has it, else in the Dungeon button's pick with only this biome.", () => GameLauncher.PlayBiome(biome, false));
                if (biome.guardian != null && biome.guardian.enabled && biome.guardian.boss != null)
                    Add("Fight its boss", $"Straight to the floor where {biome.guardian.boss.name} waits.", () => GameLauncher.PlayBiome(biome, true));
                break;
            case BossFight fight:
                foreach (var b in World.biomes.Where(b => b != null && b.guardian != null && b.guardian.enabled && b.guardian.fight == fight))
                    Add($"Fight it ({b.name})", $"Straight to {b.name}'s boss floor, where this fight plays.", () => GameLauncher.PlayBiome(b, true));
                break;
            case CampaignEncounter place:
                if (place.level != null) Add("Play " + place.level.name, "Generate floor 1 of the level this place plays.", () => GameLauncher.PlayLevel(place.level));
                Add("Sit at the table", "Start the room and sit at the table: the campaign map, as the save in use has it.", GameLauncher.OpenTable);
                break;
            case CampaignEvent e:
                Add("Show the event", "The event screen over the room, as the map shows it. A preview: choices cost and give nothing.", () => GameLauncher.PreviewEvent(e));
                break;
            case GameObject go when go.GetComponent<DungeonMapTemplate>() is DungeonMapTemplate map:
                var users = World.MapLevels(map);
                foreach (var l in users.Take(3)) Add("Play " + l.name, $"Generate floor 1 of {l.name}, which plays this map.", () => GameLauncher.PlayLevel(l));
                Add(users.Count == 0 ? "Play this map" : "Play this map alone", "A run laid out by this map, in a copy of its preview level (or the Dungeon button's pick).", () => GameLauncher.PlayMap(map));
                break;
            case GameObject go:
                if (go.GetComponent<EnemyBrain>() != null) Add("Fight it in the arena", "Start the Debug arena with just this enemy in front of you.", () => GameLauncher.FightInArena(go));
                if (go.GetComponent<DungeonRoomTemplate>() != null)
                {
                    Add("Play from this room", "A new run that opens in this room (the dev console's loadtemplate).", () => GameLauncher.PlayRoom(go));
                    var uses = World.UsesOf(go);
                    foreach (var b in uses.Where(u => u.role == "Boss arena" && u.biome != null).Select(u => u.biome).Distinct())
                        Add($"Fight the boss here ({b.name})", $"Straight to {b.name}'s boss floor, fought in this room.", () => GameLauncher.PlayBiome(b, true));
                    foreach (var l in uses.Where(u => u.biome == null && !AssetStudioWorld.IsTool(u.level)).Select(u => u.level).Distinct().Take(3))
                        Add("Play " + l.name, $"Generate floor 1 of {l.name}, which has this room.", () => GameLauncher.PlayLevel(l));
                }
                if (go.GetComponent<CampaignManager>() != null || go.GetComponentInChildren<CampaignMap>(true) != null)
                    Add("Sit at the table", "Start the room and sit at the table: the campaign map, as the save in use has it.", GameLauncher.OpenTable);
                break;
        }
        return list;
    }

    // The World panes' TEST section, two buttons a row.
    void TestSection(Object asset) => TestSection(TestActions(asset));

    void TestSection(List<(string label, string tip, Action run)> tests)
    {
        if (tests.Count == 0) return;
        Section("TEST");
        for (int i = 0; i < tests.Count; i += 2)
        {
            GUILayout.BeginHorizontal();
            for (int j = i; j < Mathf.Min(i + 2, tests.Count); j++) if (Big(tests[j].label, tests[j].tip)) tests[j].run();
            GUILayout.EndHorizontal();
        }
    }

    // A stage of a level: its own floors, and its boss when it has one.
    void StageTests(AssetStudioWorld.Level l, AssetStudioWorld.Stage st)
    {
        var tests = new List<(string label, string tip, Action run)>();
        var level = l.data;
        tests.Add(("Play from here", $"A run of {level.name} that starts at floor {st.firstFloor}, this stage's first.", () => Toast(GameLauncher.PlayStage(level, st.index, false))));
        if (st.boss && st.biome != null && st.biome.guardian != null && st.biome.guardian.enabled)
            tests.Add(("Fight its boss", $"Straight to floor {st.lastFloor}, where {(st.biome.guardian.boss != null ? st.biome.guardian.boss.name : "its guardian")} waits.", () => Toast(GameLauncher.PlayStage(level, st.index, true))));
        TestSection(tests);
    }
}

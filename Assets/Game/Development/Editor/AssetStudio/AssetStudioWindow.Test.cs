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
                // No stages: one run on its base biome, and its boss when the base biome has a guardian on the last floor.
                if (!level.HasStages)
                {
                    var b = level.baseBiome;
                    Add("From floor 1", $"A run of {level.name} from its first floor, started from the Debug arena (a copy: nothing is changed).", () => GameLauncher.PlayStage(level, 0, false));
                    if (level.bossOnLastFloor && AssetStudioWorld.HasGuardian(b))
                        Add("Fight the boss", $"Straight to one floor where {b.guardian.boss.name} waits.", () => GameLauncher.PlayStage(level, 0, true));
                    break;
                }
                int floor = 1;
                for (int i = 0; i < level.stages.Length; i++)
                {
                    var s = level.stages[i];
                    if (s == null || s.biome == null) continue;
                    string name = s.biome != null ? s.biome.name : "stage " + (i + 1);
                    int at = i;
                    if (i > 0) Add($"From floor {floor} ({name})", $"A run of {level.name} that starts at its {name} floors.", () => GameLauncher.PlayStage(level, at, false));
                    if (s.boss && s.biome != null && s.biome.guardian != null && s.biome.guardian.enabled)
                        Add($"Fight the boss of {name}", $"Straight to {name}'s last floor, where {(s.biome.guardian.boss != null ? s.biome.guardian.boss.name : "its guardian")} waits.", () => GameLauncher.PlayStage(level, at, true));
                    floor += Mathf.Max(1, s.floors);
                }
                break;
            case DungeonBiome biome when AssetStudioWorld.OwnerOf(biome) is TableLevelData owner:
                // A level's base biome: its level's run (it runs the floors only when the level has no stages).
                if (owner.HasStages) { Add("Play " + owner.name, "Its base biome holds only level-wide settings (its stages run the floors): generate floor 1 of the level.", () => GameLauncher.PlayLevel(owner)); break; }
                Add("Play this biome", $"A run of {owner.name}, whose floors run on this base biome.", () => GameLauncher.PlayStage(owner, 0, false));
                if (owner.bossOnLastFloor && AssetStudioWorld.HasGuardian(biome))
                    Add("Fight its boss", $"Straight to one floor where {biome.guardian.boss.name} waits.", () => GameLauncher.PlayStage(owner, 0, true));
                break;
            case DungeonBiome biome:
                Add("Play this biome", "A run that starts at this biome's floors: in a level that has it, else in the Dungeon button's pick with only this biome.", () => GameLauncher.PlayBiome(biome, false));
                if (biome.guardian != null && biome.guardian.enabled && biome.guardian.boss != null)
                    Add("Fight its boss", $"Straight to the floor where {biome.guardian.boss.name} waits.", () => GameLauncher.PlayBiome(biome, true));
                break;
            case BossFight fight:
                // Biome files and the levels' base biomes that run their floors.
                var withFight = World.biomes.Concat(World.levels.Where(l => !l.tool && !l.Staged && l.data.baseBiome != null).Select(l => l.data.baseBiome));
                foreach (var b in withFight.Where(b => b != null && b.guardian != null && b.guardian.enabled && b.guardian.fight == fight))
                    Add($"Fight it ({b.name})", $"Straight to {b.name}'s boss floor, where this fight plays.", () => GameLauncher.PlayBiome(b, true));
                break;
            case CampaignEncounter place:
                if (place.level != null) Add("Play " + place.level.name, "Generate floor 1 of the level this place plays.", () => GameLauncher.PlayLevel(place.level));
                Add("Sit at the table", "Start the room and sit at the table: the campaign map, as the save in use has it.", GameLauncher.OpenTable);
                if (place.Locked && !string.IsNullOrEmpty(place.deathsIn.id))
                    Add($"Sit at the table with {place.deathsToOpen} death{(place.deathsToOpen == 1 ? "" : "s")} in {place.deathsIn.displayName}",
                        $"Sets campaign.deaths.{place.deathsIn.id} to {place.deathsToOpen} in the save in use (and {place.displayName} not risen yet), then sits at the table: the map raises it with its line.",
                        () => GameLauncher.OpenTableWithDeaths(place));
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
                // A new character made from this one (a variant with its own copy of the data), as New Enemy / New NPC do.
                if (go.GetComponent<Character>() != null && go.GetComponent<Player>() == null)
                {
                    bool enemy = go.GetComponent<EnemyBrain>() != null;
                    Add(enemy ? "New enemy from this" : "New NPC from this", $"A variant of {go.name} with its own copy of its data: name it, then it opens here to edit.", () =>
                    {
                        // The studio may have been closed while the window was open: then there is nothing to show it in.
                        NewCharacterWindow.Open(enemy, go, made => { if (made != null && this != null && categories != null) ShowInLibrary(AssetDatabase.GetAssetPath(made), $"Made {made.name}. Edit it on its Character component."); });
                        return "Name the new " + (enemy ? "enemy" : "NPC") + " in the window that opened.";
                    });
                }
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

    // A file just made: the library sorts it in and shows it, in its category.
    void ShowInLibrary(string path, string message)
    {
        AssetStudioCatalog.Apply(categories, new[] { path });
        var e = categories.SelectMany(c => c.entries).FirstOrDefault(x => x.path == path);
        if (e != null) ShowEntry(e);
        else { studioView = StudioView.Category; kindFilter = 0; search = ""; Filter(); Select(null); }
        Toast(message);
        Focus();
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

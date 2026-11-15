using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// A biome's own page: everything that shapes a floor of it in the order a player meets it (its feel, who lives there,
// its rooms, look, loot and boss, what the Dungeon Master says entering it, its sound), edited in place and saved at
// once. It was the Biomes view's page (2026-10-30); that view is gone (the user's UX review, 2026-11-15: it was a third
// copy of the Dungeon's floor page). A biome a level runs is edited on its floor's page in Dungeon; this page is what
// World shows for a biome no level runs (BiomeCard, AssetStudioWindow.Levels), which has no floor page.
public partial class AssetStudioWindow
{
    void BiomeView(DungeonBiome b)
    {
        var m = World;
        // Where it runs: each level and stage, with how hard its floors are there.
        var runs = m.levels.Where(x => !x.tool).SelectMany(x => x.RunStages.Where(st => st.biome == b).Select(st => (level: x, stage: st))).ToList();
        string where = runs.Count == 0 ? "No level runs it." : string.Join(", ", runs.Select(u => $"{u.level.data.name} {(u.stage.lastFloor > u.stage.firstFloor ? $"floors {u.stage.firstFloor}-{u.stage.lastFloor}" : $"floor {u.stage.firstFloor}")}"));
        GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Width(pageWidth));
        GUILayout.BeginVertical();
        GUILayout.Label(b.name, titleStyle);
        GUILayout.Label($"\"{b.displayName}\" · {DungeonAct(b)} · {(b.IsBoard ? "board" : "walled")} · {where}", new GUIStyle(dim) { wordWrap = true }, GUILayout.Width(pageWidth - 220));
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUILayout.Width(180));
        if (GUILayout.Button(new GUIContent("Play it", "A run of this biome: from its stage in the level that runs it, else alone."), GUILayout.Height(28))) Toast(GameLauncher.PlayBiome(b, false));
        if (AssetStudioWorld.HasGuardian(b) && GUILayout.Button(new GUIContent("Fight its boss", "Straight to its guardian."), GUILayout.Height(28))) Toast(GameLauncher.PlayBiome(b, true));
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        if (runs.Select(u => u.level).Distinct().Count() > 1 || runs.Count > 1)
            GUILayout.Label("<color=#e0a85a>Several stages run it: a change here shows on each.</color>", Wrapped, GUILayout.Width(pageWidth));

        // How hard it is where it runs.
        Section("HOW HARD ITS FLOORS ARE");
        if (runs.Count == 0) GUILayout.Label("It runs nowhere, so no floor's difficulty applies: it takes the floor it is put on.", dim);
        foreach (var (level, stage) in runs)
        {
            var floors = RunDifficulty(level).Where(f => f.floor >= stage.firstFloor && f.floor <= stage.lastFloor).ToList();
            foreach (var f in floors)
            {
                var typical = Typical(level.data, b, f, 0f);
                GUILayout.Label($"{level.data.name} floor {f.floor}{(f.bossFloor ? " <color=#e24b4a>boss</color>" : "")}:  <color=#d8aa48>x{f.damage:0.00} damage</color>  <color=#d8665e>x{f.health:0.00} health</color>" + (f.step > 0 ? $"  <color=#e0a85a>jump +{f.step:0.#}</color>" : "") + (typical.weight > 0 ? $"  ·  a typical enemy {typical.health:0} hp, {typical.damage:0.#} dmg" : ""), new GUIStyle(StudioSkin.Label) { richText = true });
            }
        }

        // The feel of a floor: how many fights, how big, elites.
        Section("THE FEEL OF A FLOOR");
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        Fields(b, "encounterChance", "eliteChance", "eliteFloors", "roomCount", "templateChance", "merchantChance", "lightRadius");
        GUILayout.EndVertical();

        // Who lives there, its boss and loot, its rooms and its look (the same panels as a level's Run tab).
        var first = runs.FirstOrDefault();
        StageEditor(first.level, first.stage, b);

        // What he says entering it, and its sound.
        Section("THE DUNGEON MASTER, ENTERING IT");
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        Fields(b, "entryLineChance", "entryLines");
        GUILayout.EndVertical();
        Section("SOUND");
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(pageWidth));
        Fields(b, "music", "musicVolume", "ambience");
        GUILayout.EndVertical();
        GUILayout.Label("Everything else about it (its look tab, wild ground, lighting preset, room features) is in the inspector on the right.", Wrapped, GUILayout.Width(pageWidth));
    }
}

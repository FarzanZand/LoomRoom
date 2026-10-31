using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Biomes view (the user, 2026-10-30: tweak the experience per biome, every biome in the project). Left,
// every biome: each dungeon's in run order under its dungeon acts (Dungeon1: the Cellars, the Realms, the Ruins), the
// one-floor levels' base biomes, then the biome files no level runs. In the middle, the picked biome's page, everything
// that shapes a floor of it in the order a player meets it: where it runs and how hard it is there, its feel (who lives
// there, how many to a room, how many rooms are fights, elites), its rooms, its look, its loot and boss, what the Dungeon
// Master says entering it and its sound. Everything is edited in place and saved at once (Ctrl+Z undoes it); the rest of
// the biome is in the inspector on the right.
public partial class AssetStudioWindow
{
    [SerializeField] string biomeViewPath;
    Vector2 biomeListScroll, biomePageScroll;
    string biomeSearch = "";
    bool showingBiomes => studioView == StudioView.Biomes;

    DungeonBiome BiomeViewPicked => string.IsNullOrEmpty(biomeViewPath) ? null : AssetDatabase.LoadAssetAtPath<DungeonBiome>(biomeViewPath) ?? BaseBiomeAt(biomeViewPath);

    // A level's base biome lives inside the level's own file: its path is the level's.
    static DungeonBiome BaseBiomeAt(string path) => AssetDatabase.LoadAssetAtPath<TableLevelData>(path) is TableLevelData d ? d.baseBiome : null;

    void OpenBiomes()
    {
        studioView = StudioView.Biomes;
        if (BiomeViewPicked == null && BiomeRows().FirstOrDefault(r => r.biome != null) is var first && first.biome != null) PickBiomeView(first.biome);
        GUI.FocusControl(null);
    }

    void PickBiomeView(DungeonBiome b)
    {
        biomeViewPath = AssetDatabase.GetAssetPath(b);
        biomePageScroll = Vector2.zero;
        ShowAsset(b);
        Repaint();
    }

    // The list's rows: a heading (biome null) or a biome with a note.
    List<(string label, string note, DungeonBiome biome, bool heading)> BiomeRows()
    {
        var rows = new List<(string, string, DungeonBiome, bool)>();
        var m = World;
        var listed = new HashSet<DungeonBiome>();
        foreach (var (group, levels) in m.Groups(false))
            foreach (var l in levels.Where(x => x.data.IsDungeon))
            {
                var run = l.RunStages.Where(s => s.biome != null).ToList();
                if (run.Count == 0) continue;
                rows.Add((l.data.name.ToUpperInvariant(), group, null, true));
                string act = null;
                foreach (var st in run)
                {
                    if (l.Staged && DungeonAct(st.biome) != act) { act = DungeonAct(st.biome); rows.Add((act, "", null, false)); }
                    string floors = st.lastFloor > st.firstFloor ? $"floors {st.firstFloor}-{st.lastFloor}" : $"floor {st.firstFloor}";
                    rows.Add((st.IsBase ? l.data.name + " (its own)" : st.biome.name, floors + (st.boss && AssetStudioWorld.HasGuardian(st.biome) ? " · boss" : ""), st.biome, false));
                    listed.Add(st.biome);
                }
            }
        var loose = m.biomes.Where(b => !listed.Contains(b)).OrderBy(b => b.name, StringComparer.OrdinalIgnoreCase).ToList();
        if (loose.Count > 0)
        {
            rows.Add(("NO LEVEL RUNS THESE", "", null, true));
            foreach (var b in loose) rows.Add((b.name, AssetDatabase.GetAssetPath(b).Contains("/_Archive/") ? "archived" : "not in any run", b, false));
        }
        return rows;
    }

    void DrawBiomeList(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 16));
        biomeSearch = EditorGUILayout.TextField(biomeSearch, EditorStyles.toolbarSearchField);
        GUILayout.Label("Every biome, each dungeon's in the order its run goes. Pick one to tune its floors.", new GUIStyle(dim) { wordWrap = true });
        string s = biomeSearch.Trim().ToLowerInvariant();
        var picked = BiomeViewPicked;
        biomeListScroll = GUILayout.BeginScrollView(biomeListScroll);
        foreach (var (label, note, biome, heading) in BiomeRows())
        {
            if (biome == null)
            {
                if (s.Length > 0) continue;
                GUILayout.Space(heading ? 8 : 2);
                GUILayout.Label(heading ? $"{label}  <color=#b8ae9c>{note}</color>" : "  " + label, new GUIStyle(gold) { richText = true, fontSize = heading ? StudioSkin.Size : StudioSkin.Size - 2 });
                continue;
            }
            if (s.Length > 0 && !(label + " " + note + " " + biome.displayName).ToLowerInvariant().Contains(s)) continue;
            if (GUILayout.Button($"{label}  <color=#b8ae9c>{note}</color>", biome == picked ? cellSelected : cell, GUILayout.Height(24))) PickBiomeView(biome);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawBiomePage(Rect r)
    {
        if (worldPane != WorldPane.Picked) { DrawWorldMiddle(ref r); return; }   // a New room form opened from the pool
        GUI.Box(r, GUIContent.none);
        pageWidth = r.width - 28 - 18;
        ReadGameWide();
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        biomePageScroll = GUILayout.BeginScrollView(biomePageScroll);
        try
        {
            var b = BiomeViewPicked;
            if (b == null) { Title("Biomes", "Pick a biome on the left."); return; }
            BiomeView(b);
        }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

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
        if (runs.Count > 0 && GUILayout.Button(new GUIContent("Its level's difficulty...", "The Balance tab of the first level that runs it."), GUILayout.Width(220), GUILayout.Height(24)))
        { OpenLevel(runs[0].level.data, runs[0].stage.index); levelTab = BalanceTabIndex; }

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

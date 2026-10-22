using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's World: the New level form (+ New level at the top of the levels, New... > Level). Three ways, one
// Create button each, with a live summary of what will be made:
// - One-floor encounter: a hand-built map of a few rooms (like the Arena or the Peddler's Cellar), board or walled, its
//   floor on its base biome (enemies, loot and look copied from a biome); the map template opens in Prefab Mode.
// - Generated dungeon: several floors grown from rooms, its run a list of stages (a biome as it is, or a new copy).
// - Copy a level: a copy of a level as it is (the older flow).
// The making itself is AssetStudioWorld.MakeEncounter / MakeDungeon / NewLevel; the World's Undo takes it all back.
public partial class AssetStudioWindow
{
    [SerializeField] int newLevelFlow;
    static readonly string[] NewLevelFlows = { "One-floor encounter", "Generated dungeon", "Copy a level" };
    AssetStudioWorld.EncounterPlan encounterPlan;
    AssetStudioWorld.DungeonPlan dungeonPlan;
    TableLevelData justMade;   // the level the last flow made: its pane offers Play it and its map
    // The name check, read again only when the name or the World changes (it looks at folders and levels).
    string nameCheckFor, nameCheck;
    AssetStudioWorld.Model nameCheckWorld;
    // The biome choices (biome files, then each level's base biome), read again only when the World changes.
    List<(string label, DungeonBiome biome)> biomeChoices;
    AssetStudioWorld.Model biomeChoicesWorld;

    void StartNewLevel()
    {
        var m = World;
        int act = Mathf.Max(1, formAct);
        var cellars = Biome("Cellars") ?? m.biomes.FirstOrDefault();
        encounterPlan = new AssetStudioWorld.EncounterPlan { act = act, contentFrom = cellars, rooms = 3 };
        dungeonPlan = new AssetStudioWorld.DungeonPlan { act = act, balanceFrom = m.levels.FirstOrDefault(l => l.data.name == "Dungeon1")?.data };
        if (cellars != null) dungeonPlan.stages.Add(new AssetStudioWorld.StagePlan { biome = cellars, floors = 2 });
        var crypt = Biome("Crypt");
        if (crypt != null) dungeonPlan.stages.Add(new AssetStudioWorld.StagePlan { biome = crypt, floors = 1, boss = AssetStudioWorld.HasGuardian(crypt) });
        nameCheckFor = null;
    }

    DungeonBiome Biome(string name) => World.biomes.FirstOrDefault(b => b.name == name);

    // ── The form ─────────────────────────────────────────────────────

    void NewLevelForm()
    {
        if (encounterPlan == null || dungeonPlan == null) StartNewLevel();
        Title("New level", "Pick how it plays and fill in the few things below; the summary says what will be made. Create makes it all, filed and linked (Undo above takes it all back).");
        newLevelFlow = GUILayout.Toolbar(Mathf.Clamp(newLevelFlow, 0, 2), NewLevelFlows, GUILayout.Height(24));
        GUILayout.Space(8);
        switch (newLevelFlow)
        {
            case 0: EncounterForm(encounterPlan); break;
            case 1: DungeonForm(dungeonPlan); break;
            default: CopyLevelForm(); break;
        }
    }

    void EncounterForm(AssetStudioWorld.EncounterPlan p)
    {
        GUILayout.Label("One floor of a few rooms built by hand, like the Arena or the Peddler's Cellar: a map template with a Room child per room, entrance to exit.", dim);
        p.name = EditorGUILayout.TextField("Name", p.name);
        p.act = ActPopup("Act", p.act);
        p.board = KindToggle(p.board);
        var maps = World.maps;
        int mapAt = p.copyMap != null ? maps.IndexOf(p.copyMap) + 1 : 0;
        int mapPick = EditorGUILayout.Popup(new GUIContent("Start from", "A new blank map of rooms in a row, or a copy of a map template (its rooms stay the same prefabs)."), mapAt, new[] { "A new blank map (rooms in a row)" }.Concat(maps.Select(x => "Copy of " + x.name)).ToArray());
        p.copyMap = mapPick == 0 ? null : maps[mapPick - 1];
        if (p.copyMap == null) p.rooms = EditorGUILayout.IntSlider(new GUIContent("Rooms", "Rooms in a row: the first is where the player starts, the last has the stairs out."), p.rooms, 2, 8);
        p.contentFrom = BiomePopup(new GUIContent("Enemies, loot, look", "Its base biome gets this biome's enemies, loot, look, sound, guardian and room styles."), p.contentFrom);
        using (new EditorGUI.DisabledScope(!AssetStudioWorld.HasGuardian(p.contentFrom)))
            p.boss = EditorGUILayout.Toggle(new GUIContent("Boss on its floor", "The biome's guardian waits on the floor (only when the biome has one set up)."), p.boss && AssetStudioWorld.HasGuardian(p.contentFrom));
        p.settingsFrom = SettingsPopup(p.board, true, p.settingsFrom);
        p.place = EditorGUILayout.Toggle(new GUIContent($"A place in Act {p.act}", "Put it on the campaign map near the act's other places, in the campaign manager's list."), p.place);

        // What will be made.
        var from = p.settingsFrom ?? AssetStudioWorld.SettingsLevel(World, p.board, true);
        string name = AssetStudioWorld.CleanName(p.name);
        string shown = name.Length > 0 ? name : "<name>";
        var lines = new List<string>
        {
            $"Levels/{shown}/{shown}.asset: one {(p.board ? "board" : "walled")} floor, settings from {(from != null ? from.name : "<color=#e0a85a>no level</color>")} (look, doors, torches, chests, shop, dialogue).",
            p.copyMap != null ? $"Its map: a copy of {p.copyMap.name} in its folder ({p.copyMap.Rooms().Length} rooms; their prefabs stay shared with {p.copyMap.name})." : $"Its map: Levels/{shown}/{shown} map.prefab, {p.rooms} rooms in a row, a Room child each to build in Prefab Mode.",
            $"Its base biome: enemies, loot and look from {(p.contentFrom != null ? p.contentFrom.name : "the settings level")}.",
            "The run: " + AssetStudioWorld.RunLine(new[] { (shown + " base", 1, p.boss && AssetStudioWorld.HasGuardian(p.contentFrom)) }),
            "In the level catalog." + (p.place ? $" A place in Act {p.act} on the campaign map." : " Not on the campaign map (add a place later)."),
        };
        if (from != null && from.IsBoard != p.board) lines.Add($"<color=#e0a85a>{from.name} is {(from.IsBoard ? "a board" : "a walled")} level: set the board pieces on the new base biome's Level tab.</color>");
        string problem = NameCheck(p.name) ?? (from == null ? "No level to take the settings from." : null);
        if (Summary(lines, problem)) CreateEncounter(true);
    }

    void DungeonForm(AssetStudioWorld.DungeonPlan p)
    {
        GUILayout.Label("Several floors grown from rooms, a new layout every run: its run is a list of stages, each a biome for some floors, with its guardian on its last floor if you like.", dim);
        p.name = EditorGUILayout.TextField("Name", p.name);
        p.act = ActPopup("Act", p.act);
        p.board = KindToggle(p.board);
        // The total is the stages' floors added up; changing it changes the last stage's.
        EditorGUI.BeginChangeCheck();
        int total = EditorGUILayout.IntSlider(new GUIContent("Floors", "Floors in a run. Changing it changes the last stage."), p.Floors, 1, 30);
        if (EditorGUI.EndChangeCheck() && p.stages.Count > 0)
        {
            var last = p.stages[p.stages.Count - 1];
            last.floors = Mathf.Clamp(last.floors + total - p.Floors, 1, 20);
        }

        Section("STAGES (top floor first)");
        for (int i = 0; i < p.stages.Count; i++)
        {
            var s = p.stages[i];
            GUILayout.BeginHorizontal();
            GUILayout.Label((i + 1).ToString(), GUILayout.Width(14));
            var picked = BiomePopup(GUIContent.none, s.biome, GUILayout.MinWidth(120));
            if (picked != s.biome)
            {
                s.biome = picked;
                if (AssetStudioWorld.IsBase(picked)) s.copy = true;
                if (s.copy) s.copyName = DefaultCopyName(p, picked);
                s.boss = s.boss && AssetStudioWorld.HasGuardian(picked);
            }
            using (new EditorGUI.DisabledScope(AssetStudioWorld.IsBase(s.biome)))
            {
                bool copy = GUILayout.Toggle(s.copy || AssetStudioWorld.IsBase(s.biome), new GUIContent("new copy", "Make a new biome from it in the level's Biomes folder, to change freely (a level's base biome is always copied)."), GUILayout.Width(76));
                if (copy != s.copy) { s.copy = copy; if (copy && string.IsNullOrWhiteSpace(s.copyName)) s.copyName = DefaultCopyName(p, s.biome); }
            }
            if (s.copy || AssetStudioWorld.IsBase(s.biome)) s.copyName = EditorGUILayout.TextField(s.copyName, GUILayout.MinWidth(100));
            GUILayout.Label("floors", dim, GUILayout.Width(34));
            s.floors = Mathf.Clamp(EditorGUILayout.IntField(s.floors, GUILayout.Width(28)), 1, 20);
            using (new EditorGUI.DisabledScope(!AssetStudioWorld.HasGuardian(s.biome)))
                s.boss = GUILayout.Toggle(s.boss && AssetStudioWorld.HasGuardian(s.biome), new GUIContent("boss", "Its guardian waits on this stage's last floor (only when the biome has one set up)."), GUILayout.Width(46));
            if (GUILayout.Button("▲", GUILayout.Width(22)) && i > 0) (p.stages[i - 1], p.stages[i]) = (p.stages[i], p.stages[i - 1]);
            if (GUILayout.Button("▼", GUILayout.Width(22)) && i < p.stages.Count - 1) (p.stages[i + 1], p.stages[i]) = (p.stages[i], p.stages[i + 1]);
            bool remove = GUILayout.Button("✕", GUILayout.Width(22));
            GUILayout.EndHorizontal();
            if (remove) { p.stages.RemoveAt(i); GUIUtility.ExitGUI(); }
        }
        if (GUILayout.Button("Add a stage", GUILayout.Width(120)))
            p.stages.Add(new AssetStudioWorld.StagePlan { biome = p.stages.LastOrDefault()?.biome ?? World.biomes.FirstOrDefault(), floors = 1 });

        Section("RULES");
        var withBalance = World.levels.Where(l => !l.tool && l.data.IsDungeon && l.data.balance != null).Select(l => l.data).ToList();
        int balanceAt = Mathf.Max(0, withBalance.IndexOf(p.balanceFrom));
        if (withBalance.Count > 0) p.balanceFrom = withBalance[EditorGUILayout.Popup(new GUIContent("Balance from", "Its enemy scaling per floor, copied into the level's folder to tune freely."), balanceAt, withBalance.Select(l => $"{l.name} ({l.balance.name})").ToArray())];
        p.gear = EditorGUILayout.Popup(new GUIContent("Gear by floor", "Which gear tiers drop on each floor: from the lower tier on the first floor to the higher on the last."), p.gear, AssetStudioWorld.GearPresets);
        p.settingsFrom = SettingsPopup(p.board, false, p.settingsFrom);
        p.place = EditorGUILayout.Toggle(new GUIContent($"A place in Act {p.act}", "Put it on the campaign map near the act's other places, in the campaign manager's list."), p.place);

        // What will be made.
        var from = p.settingsFrom ?? AssetStudioWorld.SettingsLevel(World, p.board, false);
        var d1 = World.levels.FirstOrDefault(l => l.data.name == "Dungeon1")?.data;
        string name = AssetStudioWorld.CleanName(p.name);
        string shown = name.Length > 0 ? name : "<name>";
        var lines = new List<string>
        {
            $"Levels/{shown}/{shown}.asset: a {(p.board ? "board" : "walled")} dungeon of {p.Floors} floor{(p.Floors == 1 ? "" : "s")}, grown anew every run, settings from {(from != null ? from.name : "<color=#e0a85a>no level</color>")}" + (d1 != null && d1 != from ? "; starting room, door and the first chest's potion from Dungeon1." : "."),
            "The run: " + AssetStudioWorld.RunLine(p.stages.Select(s => (s.Name, s.floors, s.boss && AssetStudioWorld.HasGuardian(s.biome)))),
        };
        foreach (var s in p.stages.Where(s => s.biome != null && (s.copy || AssetStudioWorld.IsBase(s.biome))))
            lines.Add($"New biome Levels/{shown}/Biomes/{(s.Name.Length > 0 ? s.Name : "<name>")}.asset, a copy of {s.biome.name}.");
        var shared = p.stages.Where(s => s.biome != null && !s.copy && !AssetStudioWorld.IsBase(s.biome)).Select(s => s.biome.name).Distinct().ToList();
        if (shared.Count > 0) lines.Add("Shared biomes (a change there shows in every level that runs them): " + string.Join(", ", shared) + ".");
        lines.Add(p.balanceFrom != null && p.balanceFrom.balance != null ? $"Balance: a copy of {p.balanceFrom.balance.name} in its folder." : $"Balance: the settings level's ({(from != null && from.balance != null ? from.balance.name : "none")}).");
        lines.Add("Gear by floor: " + (p.gear < AssetStudioWorld.GearPresets.Length - 1 ? string.Join("; ", AssetStudioWorld.GearRows(p.gear, p.Floors).Select(r => r.Label)) : "as " + (from != null ? from.name : "the settings level") + " has it") + ".");
        lines.Add("In the level catalog." + (p.place ? $" A place in Act {p.act} on the campaign map." : " Not on the campaign map (add a place later)."));
        if (from != null && from.IsBoard != p.board) lines.Add($"<color=#e0a85a>{from.name} is {(from.IsBoard ? "a board" : "a walled")} level: set the board pieces on the new base biome's Level tab.</color>");
        foreach (var s in p.stages.Where(s => s.boss && !AssetStudioWorld.HasGuardian(s.biome))) lines.Add($"<color=#e0a85a>{s.Name} has no guardian: no boss there.</color>");
        string problem = NameCheck(p.name) ?? AssetStudioWorld.DungeonPlanProblem(p) ?? (from == null ? "No level to take the settings from." : null);
        if (Summary(lines, problem)) CreateDungeon(true);
    }

    // The summary box and the Create button (pressed: true). A problem keeps it greyed out.
    bool Summary(List<string> lines, string problem)
    {
        Section("WHAT WILL BE MADE");
        var style = new GUIStyle(dim) { richText = true, wordWrap = true };
        GUILayout.BeginVertical(EditorStyles.helpBox);
        foreach (var line in lines) GUILayout.Label("• " + line, style);
        GUILayout.EndVertical();
        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        if (problem != null) GUILayout.Label($"<color=#e0a85a>{problem}</color>", style);
        else if (playing) GUILayout.Label("<color=#e0a85a>Stop Play to make a level.</color>", style);
        GUILayout.Space(4);
        using (new EditorGUI.DisabledScope(problem != null || playing))
            return GUILayout.Button(new GUIContent("Create", "Makes everything above. The World's Undo button takes it all back."), GUILayout.Height(30));
    }

    // ── Making it ────────────────────────────────────────────────────

    void CreateEncounter(bool fromGui)
    {
        var level = AssetStudioWorld.MakeEncounter(World, encounterPlan, out var map, out string message);
        AfterNewLevel(level, map != null ? map.gameObject : null, message, fromGui);
    }

    void CreateDungeon(bool fromGui)
    {
        var level = AssetStudioWorld.MakeDungeon(World, dungeonPlan, out string message);
        AfterNewLevel(level, null, message, fromGui);
    }

    // The studio reads everything again, opens the level's group and picks the level (its pane has Play it); a new map
    // opens in Prefab Mode to build its rooms.
    void AfterNewLevel(TableLevelData level, GameObject map, string message, bool fromGui)
    {
        Toast(message);
        Debug.Log("[Asset Studio] " + message);
        RefreshLibrary();
        if (level != null)
        {
            justMade = level;
            StartNewLevel();
            PickAsset(level);   // opens its group and picks its row
            if (map != null) EditorApplication.delayCall += () => { if (map != null) AssetDatabase.OpenAsset(map); };
        }
        if (fromGui) GUIUtility.ExitGUI();
    }

    // ── Fields ───────────────────────────────────────────────────────

    string NameCheck(string name)
    {
        if (nameCheckFor != name || nameCheckWorld != World)
        {
            nameCheckFor = name; nameCheckWorld = World;
            nameCheck = AssetStudioWorld.LevelNameProblem(name, World.levels.Select(l => l.data));
        }
        return nameCheck;
    }

    int ActPopup(string label, int act)
    {
        int acts = World.Acts + 1;   // one past the last: a new act
        var names = Enumerable.Range(1, acts).Select(a => a == acts ? $"Act {a} (a new act)" : "Act " + a).ToArray();
        return EditorGUILayout.Popup(label, Mathf.Clamp(act, 1, acts) - 1, names) + 1;
    }

    static bool KindToggle(bool board)
    {
        GUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel(new GUIContent("Kind", "Walled: rooms with walls and ceilings. Board: printed tiles on the table, no walls, rooms dealt in as their doors open."));
        int pick = GUILayout.Toolbar(board ? 1 : 0, new[] { "Walled", "Board" });
        GUILayout.EndHorizontal();
        return pick == 1;
    }

    string DefaultCopyName(AssetStudioWorld.DungeonPlan p, DungeonBiome b)
    {
        string level = AssetStudioWorld.CleanName(p.name);
        string biome = b == null ? "biome" : AssetStudioWorld.IsBase(b) ? (AssetStudioWorld.OwnerOf(b)?.name ?? b.name) : b.name;
        return (level.Length > 0 ? level + " " : "New ") + biome;
    }

    DungeonBiome BiomePopup(GUIContent label, DungeonBiome current, params GUILayoutOption[] options)
    {
        if (biomeChoices == null || biomeChoicesWorld != World)
        {
            biomeChoicesWorld = World;
            biomeChoices = World.biomes.Select(b => (b.name, b)).ToList();
            foreach (var l in World.levels.Where(l => !l.tool && l.data.IsDungeon && l.data.baseBiome != null))
                biomeChoices.Add(("Level base biomes/" + l.data.name, l.data.baseBiome));
        }
        if (biomeChoices.Count == 0) { EditorGUILayout.LabelField(label, new GUIContent("No biomes")); return null; }
        int at = Mathf.Max(0, biomeChoices.FindIndex(c => c.biome == current));
        int pick = EditorGUILayout.Popup(label, at, biomeChoices.Select(c => new GUIContent(c.label)).ToArray(), options);
        return biomeChoices[Mathf.Clamp(pick, 0, biomeChoices.Count - 1)].biome;
    }

    // The level the settings come from: automatic (one of the same kind) or picked.
    TableLevelData SettingsPopup(bool board, bool authored, TableLevelData current)
    {
        var auto = AssetStudioWorld.SettingsLevel(World, board, authored);
        var options = World.levels.Where(l => !l.tool && l.inCatalog && l.data.IsDungeon && !l.data.pocket && l.data.baseBiome != null).Select(l => l.data).ToList();
        int at = current != null ? options.IndexOf(current) + 1 : 0;
        var names = new[] { "Automatic: " + (auto != null ? auto.name : "none") }.Concat(options.Select(o => $"{o.name} ({(o.IsBoard ? "board" : "walled")})")).ToArray();
        int pick = EditorGUILayout.Popup(new GUIContent("Settings from", "The level copied for the new one's settings: look, doors, torches, chests, shop, dialogue, music. Automatic takes one of the same kind."), Mathf.Max(0, at), names);
        return pick == 0 ? null : options[pick - 1];
    }

    // ── For tests (an eval) ──────────────────────────────────────────
    // Fill the form's plan and press Create (what the form does, nothing else). stages: "Cellars:2:0;+Crypt:1:1"
    // (biome:floors:boss, + for a new copy).
    public string TestNewEncounter(string name, int act, bool board, string copyMap = null, int rooms = 3, string contentFrom = "Cellars", bool place = true)
    {
        StartForm(WorldPane.NewLevel); newLevelFlow = 0;
        encounterPlan = new AssetStudioWorld.EncounterPlan { name = name, act = act, board = board, rooms = rooms, place = place,
            copyMap = copyMap == null ? null : World.maps.FirstOrDefault(x => x.name == copyMap), contentFrom = World.biomes.FirstOrDefault(b => b.name == contentFrom) };
        CreateEncounter(false);
        return notice;
    }

    public string TestNewDungeon(string name, int act, bool board, string stages, int gear = 0, bool place = true)
    {
        StartForm(WorldPane.NewLevel); newLevelFlow = 1;
        dungeonPlan = new AssetStudioWorld.DungeonPlan { name = name, act = act, board = board, gear = gear, place = place, balanceFrom = World.levels.FirstOrDefault(l => l.data.name == "Dungeon1")?.data };
        foreach (var part in (stages ?? "").Split(';'))
        {
            var f = part.Split(':');
            if (f.Length < 1 || f[0].Trim().Length == 0) continue;
            bool copy = f[0].StartsWith("+");
            var biome = World.biomes.FirstOrDefault(b => b.name == f[0].TrimStart('+'));
            var s = new AssetStudioWorld.StagePlan { biome = biome, copy = copy, floors = f.Length > 1 && int.TryParse(f[1], out int n) ? n : 1, boss = f.Length > 2 && f[2] == "1" };
            if (copy) s.copyName = DefaultCopyName(dungeonPlan, biome);
            dungeonPlan.stages.Add(s);
        }
        CreateDungeon(false);
        return notice;
    }
}

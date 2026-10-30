using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Dungeon section: one dungeon (Dungeon1, the Crypt, unless another is picked at the top) laid out the
// way a run goes, so the whole of it is made, tuned and played from one place. Left, the pages in the order a player
// meets them (the way in, each floor, the tavern, the boss) and the pages that run across the whole dungeon (who lives
// where, rooms, loot, difficulty, the Dungeon Master's words, music), with the play controls under them: start from the
// table as a player does, or straight at any floor or the boss, and once playing, the next floor, god mode and the rest.
// In the middle, the page; on the right, the inspector of whatever was clicked on it. Numbers shown for enemies are what
// the game gives them on that floor (their stats, the level's balance, the guardian's own multipliers and the game-wide
// ProgressionManager > Balance), worked out the way DungeonBalance.Apply does. Every change is saved at once and goes
// through Undo.
public partial class AssetStudioWindow
{
    const string DungeonDefaultPath = "Assets/Game/Levels/Dungeon1/Dungeon1.asset";
    const string RevealSettingsPath = "Assets/Game/Levels/Shared/Reveal/Table assembly.asset";
    const string InventoryManagerPath = "Assets/Game/Core/Prefabs/InventoryManager.prefab";
    const string ProgressionManagerPath = "Assets/Game/Core/Prefabs/ProgressionManager.prefab";
    const string DishesFolder = "Assets/Game/Levels/Resources/Tavern dishes";

    [SerializeField] string dungeonPath;
    [SerializeField] string dungeonPage = "overview";
    Vector2 dungeonListScroll, dungeonPageScroll;
    string dungeonRoomSearch = "";
    bool showingDungeon => studioView == StudioView.Dungeon;

    // Each enemy prefab's data (inside its prefab), read once per World model.
    readonly Dictionary<GameObject, CharacterData> enemyData = new();
    AssetStudioWorld.Model enemyDataFor;
    // ProgressionManager > Balance, read once per draw of the page.
    float wideDamage = 1, wideHealth = 1;

    TableLevelData DungeonLevel
    {
        get
        {
            var d = !string.IsNullOrEmpty(dungeonPath) ? AssetDatabase.LoadAssetAtPath<TableLevelData>(dungeonPath) : null;
            return d != null ? d : AssetDatabase.LoadAssetAtPath<TableLevelData>(DungeonDefaultPath);
        }
    }

    void OpenDungeon()
    {
        studioView = StudioView.Dungeon;
        worldPane = WorldPane.Picked;
        var d = DungeonLevel;
        if (d != null && inspected == null) ShowAsset(d);
        GUI.FocusControl(null);
    }

    void GoTo(string page, UnityEngine.Object show = null)
    {
        dungeonPage = page;
        dungeonPageScroll = Vector2.zero;
        worldPane = WorldPane.Picked;
        if (show != null) ShowAsset(show);
        GUI.FocusControl(null);
    }

    // ── The left column: the pages and the play controls ──

    void DrawDungeonList(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        var d = DungeonLevel;
        var l = d != null ? World.LevelOf(d) : null;
        const float playHeight = 214;
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - playHeight - 12));
        // Which dungeon: Dungeon1 unless another is picked.
        var dungeons = World.levels.Where(x => !x.tool && x.data != null && x.data.IsDungeon).OrderBy(x => x.act == 0 ? 99 : x.act).ThenBy(x => x.order).Select(x => x.data).ToList();
        int at = Mathf.Max(0, dungeons.IndexOf(d));
        int pick = EditorGUILayout.Popup(at, dungeons.Select(x => DungeonLabel(x)).ToArray());
        if (dungeons.Count > 0 && pick != at) { dungeonPath = AssetDatabase.GetAssetPath(dungeons[pick]); GoTo("overview", dungeons[pick]); }
        if (l == null) { GUILayout.Label("No dungeon to show.", dim); GUILayout.EndArea(); DrawPlayPanel(new Rect(r.x, r.yMax - playHeight, r.width, playHeight), null); return; }
        dungeonListScroll = GUILayout.BeginScrollView(dungeonListScroll);
        GUILayout.Space(4);
        GUILayout.Label("THE RUN", gold);
        PageButton("overview", "Overview", $"{d.FloorCount} floors · {(l.problems.Count == 0 ? "ready" : l.problems.Count + " to look at")}", d);
        PageButton("entering", "The way in", "the map, the build, the first room", d);
        var guardianStage = l.RunStages.LastOrDefault(st => st.boss && AssetStudioWorld.HasGuardian(st.biome));
        foreach (var st in l.RunStages)
        {
            string floors = st.lastFloor > st.firstFloor ? $"Floors {st.firstFloor}-{st.lastFloor}" : $"Floor {st.firstFloor}";
            string what = st.biome == null ? "<color=#e0a85a>no biome</color>" : st.biome.name + (st == guardianStage ? "  ·  boss" : "");
            PageButton("floor:" + st.index, floors, what, st.biome);
        }
        PageButton("tavern", "The tavern", "on every floor: food, rumours, rest", d.baseBiome != null ? d.baseBiome.shopRoom : null);
        if (guardianStage != null) PageButton("boss", "The boss", guardianStage.biome.guardian.boss.name, guardianStage.biome.guardian.boss);
        GUILayout.Space(8);
        GUILayout.Label("THE WHOLE DUNGEON", gold);
        PageButton("enemies", "Who lives where", "every enemy, floor by floor", null);
        PageButton("rooms", "Rooms", "every hand-built room, floor by floor", null);
        PageButton("events", "Room events", "ambushes, shrines, traps, dark rooms", d.baseBiome);
        PageButton("loot", "Loot", "chests, drops, gear by floor", null);
        PageButton("difficulty", "Difficulty", "balance, health and damage by floor", d.balance);
        PageButton("words", "The Dungeon Master", "what he says in this dungeon", null);
        PageButton("sound", "Music and sound", "", null);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        DrawPlayPanel(new Rect(r.x, r.yMax - playHeight, r.width, playHeight), l);
    }

    string DungeonLabel(TableLevelData d)
    {
        var l = World.LevelOf(d);
        string place = l != null && l.places.Count > 0 ? l.places[0].displayName : d.displayName;
        return string.IsNullOrEmpty(place) || place == d.name ? d.name : $"{place}  ({d.name})";
    }

    void PageButton(string key, string label, string sub, UnityEngine.Object show)
    {
        bool on = dungeonPage == key;
        if (GUILayout.Button($"{label}\n<color=#b8ae9c>{sub}</color>", on ? cellSelected : cell, GUILayout.Height(40))) GoTo(key, show);
    }

    // Always under the pages: how to play it now, and while playing, what helps a test along.
    void DrawPlayPanel(Rect r, AssetStudioWorld.Level l)
    {
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 8, r.width - 20, r.height - 14));
        GUILayout.Label("PLAY IT", gold);
        var d = l?.data;
        if (EditorApplication.isPlaying)
        {
            var loader = TableManager.HasInstance ? TableManager.Loader : null;
            string where = loader != null && loader.Current != null && loader.Dungeon != null
                ? $"In a run of {loader.Current.name}: floor {loader.FloorNumber} of {loader.Current.FloorCount}" + (loader.Current.Biome(loader.FloorNumber) is DungeonBiome b ? $" ({b.name})" : "")
                : "Playing, not in a run.";
            GUILayout.Label(where, dim);
            bool inRun = loader != null && loader.Dungeon != null && !loader.Busy;
            GUILayout.BeginHorizontal();
            GUI.enabled = inRun && loader.FloorNumber < loader.Current.FloorCount;
            if (GUILayout.Button(new GUIContent("Next floor", "Down the stairs at once."), GUILayout.Height(26))) loader.Descend();
            GUI.enabled = inRun;
            if (GUILayout.Button(new GUIContent(DevGodMode.On ? "God mode: on" : "God mode: off", "No damage; health, mana and stamina kept full."), GUILayout.Height(26))) DevConsole.RunCommand("god");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Ghost", "Fly through walls; enemies don't see you. Again to stop."), GUILayout.Height(26))) DevConsole.RunCommand("ghost");
            if (GUILayout.Button(new GUIContent("Revive", "Back on your feet where you fell."), GUILayout.Height(26))) DevConsole.RunCommand("revive");
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (d != null && GUILayout.Button(new GUIContent("Start it again from the table", "Back to the room, then the table and the map: a new run as a player starts one."), GUILayout.Height(26))) Toast(GameLauncher.PlayFromTable(l.places.FirstOrDefault()));
            if (GUILayout.Button("Stop playing", GUILayout.Height(26))) EditorApplication.isPlaying = false;
            GUILayout.EndArea();
            Repaint();
            return;
        }
        if (d == null) { GUILayout.EndArea(); return; }
        var place = l.places.FirstOrDefault();
        GUI.enabled = place != null;
        if (GUILayout.Button(new GUIContent("From the table, as a player", place != null ? $"The room, the table, then {place.displayName} picked on the map: the map goes and the floor is built on the table. Uses your save." : "No place on the map plays it."), GUILayout.Height(30)))
            Toast(GameLauncher.PlayFromTable(place));
        GUI.enabled = true;
        GUILayout.Label("Straight in, no build:", dim);
        var run = l.RunStages;
        int perRow = 3;
        for (int i = 0; i < run.Count; i += perRow)
        {
            GUILayout.BeginHorizontal();
            for (int k = i; k < Mathf.Min(run.Count, i + perRow); k++)
            {
                var st = run[k];
                string label = st.lastFloor > st.firstFloor ? $"F{st.firstFloor}-{st.lastFloor}" : $"Floor {st.firstFloor}";
                if (GUILayout.Button(new GUIContent(label, $"A run of {d.name} starting at {(st.biome != null ? st.biome.name : "this stage")}."), GUILayout.Height(26)))
                    Toast(GameLauncher.PlayStage(d, Mathf.Max(0, st.index), false));
            }
            GUILayout.EndHorizontal();
        }
        var bossStage = run.LastOrDefault(st => st.boss && AssetStudioWorld.HasGuardian(st.biome));
        GUILayout.BeginHorizontal();
        if (bossStage != null && GUILayout.Button(new GUIContent("The boss", $"Floor {bossStage.lastFloor} alone, {bossStage.biome.guardian.boss.name} waiting on it."), GUILayout.Height(26)))
            Toast(GameLauncher.PlayStage(d, Mathf.Max(0, bossStage.index), true));
        if (GUILayout.Button(new GUIContent("Debug arena", "The test arena: enemies from Debug Dungeon Settings, chests, a tavern."), GUILayout.Height(26))) { if (!DebugDungeonToolbar.StartDebug()) Toast("Unity is busy: try again in a moment."); }
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    // ── The page ──

    void DrawDungeonPage(Rect r)
    {
        // A New form (a new room for a pool) is drawn where the page is.
        if (worldPane != WorldPane.Picked) { DrawWorldMiddle(ref r); return; }
        GUI.Box(r, GUIContent.none);
        var d = DungeonLevel;
        var l = d != null ? World.LevelOf(d) : null;
        ReadGameWide();
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        dungeonPageScroll = GUILayout.BeginScrollView(dungeonPageScroll);
        try
        {
            if (l == null) Title("Dungeon", $"{DungeonDefaultPath} is missing. Pick a dungeon at the top left.");
            else if (dungeonPage.StartsWith("floor:") && int.TryParse(dungeonPage.Substring(6), out int index) && l.RunStages.FirstOrDefault(s => s.index == index) is AssetStudioWorld.Stage st) FloorPage(l, st);
            else switch (dungeonPage)
            {
                case "entering": WayInPage(l); break;
                case "tavern": TavernPage(l); break;
                case "boss": BossPage(l); break;
                case "enemies": EnemiesPage(l); break;
                case "rooms": RoomsPage(l); break;
                case "events": RoomEventsPage(l); break;
                case "loot": LootPage(l); break;
                case "difficulty": DifficultyPage(l); break;
                case "words": WordsPage(l); break;
                case "sound": SoundPage(l); break;
                default: OverviewPage(l); break;
            }
        }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    // A page's header: its title, a line under it, and its play buttons on the right.
    void PageHeader(string title, string sub, params (string label, string tip, Action run)[] buttons)
    {
        GUILayout.BeginHorizontal(StudioSkin.Inset);
        GUILayout.BeginVertical();
        GUILayout.Label(title, titleStyle);
        if (!string.IsNullOrEmpty(sub)) GUILayout.Label(sub, new GUIStyle(dim) { wordWrap = true });
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        if (buttons.Length > 0)
        {
            GUILayout.BeginVertical(GUILayout.Width(190));
            foreach (var (label, tip, run) in buttons) if (GUILayout.Button(new GUIContent(label, tip), GUILayout.Height(28))) run();
            GUILayout.EndVertical();
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
    }

    string FloorsLabel(AssetStudioWorld.Stage st) => st.lastFloor > st.firstFloor ? $"Floors {st.firstFloor}-{st.lastFloor}" : $"Floor {st.firstFloor}";
    AssetStudioWorld.Stage GuardianStage(AssetStudioWorld.Level l) => l.RunStages.LastOrDefault(st => st.boss && AssetStudioWorld.HasGuardian(st.biome));

    // ── Overview ──

    void OverviewPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        var place = l.places.FirstOrDefault();
        string where = place != null ? $"{place.displayName} on the campaign map, act {place.act}" : "Not on the campaign map";
        PageHeader(place != null ? place.displayName : d.name, $"{d.name} · {where} · {d.FloorCount} floor{(d.FloorCount == 1 ? "" : "s")} · {(d.IsBoard ? "board" : "walled")} · about {d.roomCount} rooms a floor",
            ("From the table", "As a player starts it: the room, the table, the map, the build.", () => Toast(GameLauncher.PlayFromTable(place))),
            ("Floor 1, no build", "Straight onto floor 1.", () => Toast(GameLauncher.PlayLevel(d))));
        if (l.problems.Count == 0) GUILayout.Label("<color=#6fd46a>✔ Nothing missing: every floor has enemies, loot and rooms.</color>", new GUIStyle(dim) { richText = true });
        foreach (var p in l.problems) GUILayout.Label("<color=#e0a85a>• " + p + "</color>", new GUIStyle(dim) { richText = true, wordWrap = true });

        Section("THE RUN, FLOOR BY FLOOR");
        GUILayout.Label("Click a floor to change what is on it.", dim);
        var guardian = GuardianStage(l);
        const float w = 176, h = 196;
        GUILayout.BeginHorizontal();
        foreach (var st in l.RunStages)
        {
            var b = st.biome;
            var card = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            GUI.DrawTexture(card, StudioSkin.Slot);
            GUI.Label(new Rect(card.x + 10, card.y + 6, card.width - 20, 18), FloorsLabel(st).ToUpperInvariant(), gold);
            GUI.Label(new Rect(card.x + 10, card.y + 22, card.width - 20, 18), b != null ? b.name : "<color=#e0a85a>no biome</color>", new GUIStyle(EditorStyles.boldLabel) { richText = true });
            var style = b == null ? null : b.roomStyles != null && b.roomStyles.FirstOrDefault(c => c != null && c.style != null) is DungeonStyleChoice c0 ? c0.style : d.defaultStyle;
            var thumb = StyleThumb(style);
            var pic = new Rect(card.x + 8, card.y + 44, card.width - 16, 74);
            if (thumb != null) GUI.DrawTexture(pic, thumb, ScaleMode.ScaleAndCrop); else EditorGUI.DrawRect(pic, new Color(.1f, .1f, .12f));
            if (b != null)
            {
                int kinds = b.enemies?.Count(e => e != null && e.enemy != null) ?? 0;
                int rooms = b.roomTemplates?.Count(c => c != null && c.template != null) ?? 0;
                string lines = $"{kinds} kinds of enemy, {b.minEnemiesPerRoom}-{b.maxEnemiesPerRoom} a room\n{rooms} hand-built rooms\n" +
                    (b.merchantChance >= 1 ? "a tavern" : b.merchantChance > 0 ? $"a tavern {b.merchantChance * 100:0}% of the time" : "no tavern") +
                    (st == guardian ? $"\n<color=#e24b4a>{b.guardian.boss.name} waits at the end</color>" : "");
                GUI.Label(new Rect(card.x + 10, pic.yMax + 4, card.width - 20, 74), lines, new GUIStyle(dim) { richText = true, wordWrap = true });
            }
            if (Event.current.type == EventType.MouseDown && card.Contains(Event.current.mousePosition)) { GoTo("floor:" + st.index, b); Event.current.Use(); }
            GUILayout.Space(6);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        Section("AT A GLANCE");
        var biomes = l.RunStages.Where(s => s.biome != null).Select(s => s.biome).Distinct().ToList();
        int enemyKinds = biomes.SelectMany(b => b.enemies ?? Array.Empty<DungeonBiome.Spawn>()).Where(e => e != null && e.enemy != null).Select(e => e.enemy).Distinct().Count();
        int roomCount = biomes.SelectMany(b => b.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>()).Where(c => c != null && c.template != null).Select(c => c.template).Distinct().Count();
        var lines2 = new List<string>
        {
            $"{enemyKinds} kinds of enemy across the floors" + (guardian != null ? $", and {guardian.biome.guardian.boss.name} on floor {guardian.lastFloor}." : "."),
            $"{roomCount} hand-built rooms to draw from; the rest are generated.",
            $"Starts in {(d.startingRoom != null ? d.startingRoom.name : "a generated room")}{(d.startingChest ? ", with a chest" + (d.startingChestConsumable != null ? " and " + d.startingChestConsumable.DisplayName : "") : "")}.",
            $"Balance: {(d.balance != null ? d.balance.name : "none")}" + (d.balance != null ? $": enemies {d.balance.damageMultiplier:0.##}x damage and {d.balance.healthMultiplier:0.##}x health, then +{d.balance.damageGrowthPerFloor * 100:0}% damage and +{d.balance.healthGrowthPerFloor * 100:0}% health a floor." : "."),
            $"Music: {(d.backgroundMusic != null ? d.backgroundMusic.name : "none")}.",
        };
        foreach (var s in lines2) GUILayout.Label("•  " + s, new GUIStyle(dim) { wordWrap = true });

        Section("WHAT EACH PAGE HOLDS");
        GUILayout.Label("The way in: from the map to the first room. Each floor: its enemies (with their health and damage there), rooms, look, room events and what is said on the way in. The tavern, the boss and the pages under THE WHOLE DUNGEON cover the rest. Click anything to see it in the inspector on the right; changes are saved at once and Ctrl+Z undoes them.", new GUIStyle(dim) { wordWrap = true });
    }

    // ── The way in ──

    void WayInPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        var place = l.places.FirstOrDefault();
        PageHeader("The way in", "Picked on the campaign map, the map goes, the floor is built on the table piece by piece, and the view flies down into the first room.",
            ("Watch it, from the table", "The room, the table, the map, then the build.", () => Toast(GameLauncher.PlayFromTable(place))));

        var cm = AssetDatabase.LoadAssetAtPath<GameObject>(AssetStudioWorld.CampaignManagerPrefab)?.GetComponent<CampaignManager>();
        Section("THE MAP GOES");
        GUILayout.Label("Every dungeon: the campaign map burns away (or folds flat) while the floor is built under it.", dim);
        Fields(cm, "burnAway", "burnSeconds", "levelGenerator", "frameWholeTable");

        var reveal = AssetDatabase.LoadAssetAtPath<TableLevelRevealSettings>(RevealSettingsPath);
        Section("THE FLOOR IS BUILT");
        GUILayout.Label("The pieces drop in from the far end toward the entrance, the entrance slams down last, then the lamps come on.", dim);
        Fields(reveal, "cascadeSeconds", "variety", "entrancePause", "entranceShake", "lightsSeconds");
        if (reveal != null && GUILayout.Button("Every build setting...", GUILayout.Width(200), GUILayout.Height(24))) ShowAsset(reveal);

        Section("THE FIRST ROOM");
        GUILayout.BeginHorizontal();
        var pic = RoomPicture(d.startingRoom, 160, 120);
        if (pic.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown && d.startingRoom != null) { ShowAsset(d.startingRoom); Event.current.Use(); }
        GUILayout.BeginVertical();
        RoomField(d, d, "Starting room", d.startingRoom, v => d.startingRoom = v);
        Fields(d, "startingChest", "startingChestConsumable", "doorPrefab");
        GUILayout.BeginHorizontal();
        if (d.startingRoom != null && GUILayout.Button("Open to build", GUILayout.Height(26))) AssetDatabase.OpenAsset(d.startingRoom);
        if (d.startingRoom != null && GUILayout.Button("Play from it", GUILayout.Height(26))) Toast(GameLauncher.PlayRoom(d.startingRoom));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        Section("EVERY FLOOR'S PLAN");
        GUILayout.Label("How big a floor is and how its rooms are joined.", dim);
        Fields(d, "roomCount", "width", "depth", "loopPercent", "loopReach", "corridorWander", "wideCorridorPercent", "nextFloorMessage");
    }

    // ── A floor ──

    // The biome's Kind (walled or a board game) and, for a board, how its rooms come onto the table.
    void BiomeKind(DungeonBiome b)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("Kind", "Dungeon: walled rooms. Board game: printed tiles on the table, no walls (the board's pieces and light are on the level's base biome)."), dim, GUILayout.Width(130));
        EditorGUI.BeginChangeCheck();
        var kind = (DungeonFloorKind)EditorGUILayout.EnumPopup(b.kind, GUILayout.Width(160));
        var reveal = b.boardReveal;
        if (kind == DungeonFloorKind.Boardgame)
        {
            GUILayout.Space(12);
            GUILayout.Label(new GUIContent("Rooms appear", "As their doors open: rooms are dealt onto the table as you open the doors to them. All at once: the whole floor is built from the start."), dim, GUILayout.Width(95));
            reveal = (BoardRoomReveal)EditorGUILayout.EnumPopup(b.boardReveal, GUILayout.Width(160));
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(b, "Biome kind");
        b.kind = kind; b.boardReveal = reveal;
        EditorUtility.SetDirty(b);
        AssetDatabase.SaveAssetIfDirty(b);
    }

    void FloorPage(AssetStudioWorld.Level l, AssetStudioWorld.Stage st)
    {
        var d = l.data;
        var b = st.biome;
        var guardian = GuardianStage(l);
        var buttons = new List<(string, string, Action)> { ("Play from here", $"A run starting at {FloorsLabel(st).ToLowerInvariant()}.", () => Toast(GameLauncher.PlayStage(d, Mathf.Max(0, st.index), false))) };
        if (st == guardian) buttons.Add(("Fight its boss", $"Floor {st.lastFloor} alone, the boss on it.", () => Toast(GameLauncher.PlayStage(d, Mathf.Max(0, st.index), true))));
        PageHeader($"{FloorsLabel(st)} · {(b != null ? b.name : "no biome")}", b == null ? "This stage has no biome." :
            (b.entryLines != null && b.entryLines.Length > 0 && !string.IsNullOrEmpty(b.entryLines[0].text) ? $"On the way in: \"{b.entryLines[0].text}\"" : "Its biome sets what lives here, its rooms and its look."), buttons.ToArray());
        if (b == null) return;
        var shared = World.LevelsOf(b).Where(x => !x.tool && x.data != d).ToList();
        if (shared.Count > 0) GUILayout.Label($"<color=#e0a85a>{b.name} is also run by {string.Join(", ", shared.Select(x => x.data.name))}: changes here show there too.</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        if (d.stages != null && st.index >= 0 && st.index < d.stages.Length)
        {
            var stage = d.stages[st.index];
            GUILayout.BeginHorizontal();
            GUILayout.Label("Floors in this stage", dim, GUILayout.Width(130));
            int n = EditorGUILayout.DelayedIntField(stage.floors, GUILayout.Width(40));
            GUILayout.Space(12);
            bool boss = GUILayout.Toggle(stage.boss, new GUIContent(" Boss on its last floor", "Its biome's guardian waits there."), GUILayout.Width(190));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (n != stage.floors || boss != stage.boss) SetStage(d, st.index, n, boss);
        }
        BiomeKind(b);

        Section("WHAT YOU MEET HERE");
        EnemyRows(b, st.firstFloor, st.lastFloor);
        if (st == guardian)
        {
            GUILayout.Space(4);
            var g = b.guardian;
            GUILayout.BeginHorizontal(StudioSkin.Inset);
            EnemyPicture(g.boss, 40);
            var at = NumbersAt(d, g.boss, st.lastFloor, g);
            GUILayout.BeginVertical();
            if (GUILayout.Button($"<color=#e24b4a>Boss</color>  {g.boss.name}", cell, GUILayout.Height(20))) GoTo("boss", g.boss);
            GUILayout.Label($"on floor {st.lastFloor}: {at.health:0} health, {at.damage:0.#} damage, {at.armor:0.#} armour · in {(g.arena != null ? g.arena.name : "a generated room")}", dim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(6);
        RoomTiles(l, b, 60);
        GUILayout.Space(6);
        StyleTiles(b, AssetStudioWorld.IsBase(b));

        FloorEventsSummary(l, st);

        Section("ON THE WAY IN");
        GUILayout.Label("The Dungeon Master says one of these as the floor starts.", dim);
        Fields(b, "entryLineChance");
        Lines(b, "entryLines");

        Section("LOOT AND THE TAVERN");
        Fields(b, "loot", "merchantChance");
        if (b.loot != null && GUILayout.Button("Show " + b.loot.name, GUILayout.Width(240), GUILayout.Height(24))) ShowAsset(b.loot);

        Section("MUSIC");
        GUILayout.Label("Empty: the dungeon's own music plays.", dim);
        Fields(b, "music", "musicVolume");
    }

    // The enemies of a biome on these floors: picture, how often each comes (a share of the rooms' enemies), what it has
    // on the first of these floors, its weight and the floor it first comes on, and a fight with it alone.
    void EnemyRows(DungeonBiome b, int floor, int lastFloor)
    {
        var d = DungeonLevel;
        var spawns = b.enemies ?? Array.Empty<DungeonBiome.Spawn>();
        float total = spawns.Where(s => s != null && s.enemy != null && s.fromFloor <= 1).Sum(s => s.weight);
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(44));
        GUILayout.Label("enemy", dim, GUILayout.MinWidth(120));
        GUILayout.Label("comes", dim, GUILayout.Width(52));
        GUILayout.Label(lastFloor > floor ? $"on floor {floor}" : "here it has", dim, GUILayout.Width(200));
        GUILayout.Label("weight", dim, GUILayout.Width(48));
        GUILayout.Label("from", dim, GUILayout.Width(36));
        GUILayout.Space(110);
        GUILayout.EndHorizontal();
        for (int i = 0; i < spawns.Length; i++)
        {
            var s = spawns[i];
            if (s == null) continue;
            GUILayout.BeginHorizontal(GUILayout.Height(44));
            EnemyPicture(s.enemy, 40);
            if (GUILayout.Button(s.enemy != null ? s.enemy.name : "<color=#e0a85a>empty</color>", cell, GUILayout.MinWidth(120), GUILayout.Height(40)) && s.enemy != null) ShowAsset(s.enemy);
            GUILayout.Label(total > 0 && s.fromFloor <= 1 ? (s.weight / total * 100).ToString("0") + "%" : "-", GUILayout.Width(52));
            var at = NumbersAt(d, s.enemy, floor, null);
            GUILayout.Label($"{at.health:0} hp · {at.damage:0.#} dmg · {at.armor:0.#} arm\n<color=#b8ae9c>{at.xp:0} xp</color>", new GUIStyle(dim) { richText = true }, GUILayout.Width(200));
            float w = EditorGUILayout.DelayedFloatField(s.weight, GUILayout.Width(48));
            int from = EditorGUILayout.DelayedIntField(s.fromFloor, GUILayout.Width(36));
            if (GUILayout.Button(new GUIContent("Fight", "This enemy alone in the Debug arena."), GUILayout.Width(56)) && s.enemy != null) Toast(GameLauncher.FightInArena(s.enemy));
            if (GUILayout.Button(new GUIContent("✕", "Take it off these floors."), GUILayout.Width(28))) { AssetStudioWorld.RemoveEnemy(b, i); RefreshWorld(); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            if (w != s.weight || from != s.fromFloor) { AssetStudioWorld.SetEnemy(b, i, w, from); RefreshWorld(); }
        }
        if (spawns.Length == 0) GUILayout.Label("<color=#e0a85a>Nothing lives here yet: its rooms stay empty.</color>", new GUIStyle(dim) { richText = true });
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add an enemy", GUILayout.Width(180), GUILayout.Height(26))) AddEnemyMenu(b);
        var drop = GUILayoutUtility.GetRect(10, 26, GUILayout.ExpandWidth(true));
        GUI.Box(drop, "or drop enemy prefabs here", new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter, normal = { background = StudioSkin.Slot }, border = new RectOffset(8, 8, 8, 8) });
        var ev = Event.current;
        if (drop.Contains(ev.mousePosition) && (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && DragAndDrop.objectReferences.Any(AssetStudioWorld.IsEnemy))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); foreach (var o in DragAndDrop.objectReferences) if (o is GameObject g) AssetStudioWorld.AddEnemy(b, g); RefreshWorld(); }
            ev.Use();
        }
        GUILayout.EndHorizontal();
        Fields(b, "minEnemiesPerRoom", "maxEnemiesPerRoom");
    }

    void AddEnemyMenu(DungeonBiome b)
    {
        var menu = new GenericMenu();
        var spawns = b.enemies ?? Array.Empty<DungeonBiome.Spawn>();
        foreach (var e in categories.Where(c => c.name == "Enemies").SelectMany(c => c.entries))
        {
            var go = e.Load() as GameObject;
            if (go == null || !AssetStudioWorld.IsEnemy(go) || spawns.Any(x => x != null && x.enemy == go)) continue;
            menu.AddItem(new GUIContent(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(e.path)) + "/" + e.name), false, () => { AssetStudioWorld.AddEnemy(b, go); RefreshWorld(); });
        }
        if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Every enemy lives here already"));
        menu.ShowAsContext();
    }

    void EnemyPicture(GameObject enemy, float size)
    {
        var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        GUI.DrawTexture(r, StudioSkin.Slot);
        var pic = enemy != null ? AssetPreview.GetAssetPreview(enemy) ?? AssetPreview.GetMiniThumbnail(enemy) : null;
        if (pic != null) GUI.DrawTexture(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), pic, ScaleMode.ScaleToFit);
    }

    // A room template's picture at this size; returns its rect.
    Rect RoomPicture(GameObject room, float w, float h)
    {
        var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
        GUI.DrawTexture(r, StudioSkin.Slot);
        var pic = room != null ? AssetPreview.GetAssetPreview(room) : null;
        if (pic != null) GUI.DrawTexture(new Rect(r.x + 4, r.y + 4, r.width - 8, r.height - 8), pic, ScaleMode.ScaleToFit);
        else GUI.Label(r, room != null ? room.name : "none", new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter });
        return r;
    }

    // ── Enemy numbers, as the game gives them ──

    struct EnemyNumbers { public float health, damage, armor, xp; }

    // ProgressionManager > Balance: the room's own manager when the scene is open (or playing), else its prefab. Read
    // once a frame (at the layout pass).
    void ReadGameWide()
    {
        if (Event.current.type != EventType.Layout) return;
        ProgressionManager pm = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        if (pm == null) pm = AssetDatabase.LoadAssetAtPath<GameObject>(ProgressionManagerPath)?.GetComponent<ProgressionManager>();
        wideDamage = pm != null ? pm.enemyBaseDamage : 1;
        wideHealth = pm != null ? pm.enemyBaseHealth : 1;
    }

    CharacterData DataOf(GameObject prefab)
    {
        if (prefab == null) return null;
        if (enemyDataFor != World) { enemyData.Clear(); enemyDataFor = World; }
        if (!enemyData.TryGetValue(prefab, out var data))
            enemyData[prefab] = data = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(prefab)).OfType<CharacterData>().FirstOrDefault();
        return data;
    }

    static float Stat(CharacterData data, StatType stat)
    {
        if (data == null || data.stats == null) return 0;
        foreach (var e in data.stats) if (e.stat == stat) return e.baseValue;
        return 0;
    }

    // What this enemy has on that floor of this dungeon (DungeonBalance.Apply, the guardian's own multipliers and the
    // game-wide ones). guardian: it is the boss.
    EnemyNumbers NumbersAt(TableLevelData d, GameObject enemy, int floor, DungeonMilestone guardian)
    {
        var data = DataOf(enemy);
        float hp = Stat(data, StatType.MaxHealth), dmg = Stat(data, StatType.AttackDamage), arm = Stat(data, StatType.Armor);
        var bal = d != null ? d.balance : null;
        int depth = Mathf.Max(0, floor - 1);
        bool boss = guardian != null;
        if (bal != null)
        {
            dmg *= (boss && bal.separateBossDamage ? bal.bossDamageMultiplier : bal.damageMultiplier) * (1 + bal.damageGrowthPerFloor * depth);
            hp *= boss ? bal.bossHealthMultiplier : bal.healthMultiplier * (1 + bal.healthGrowthPerFloor * depth);
            arm += bal.armorPerFloor * depth;
        }
        if (boss) { hp *= guardian.healthMultiplier; dmg *= guardian.damageMultiplier; }
        return new EnemyNumbers { health = hp * wideHealth, damage = dmg * wideDamage, armor = arm, xp = data is EnemyData e ? e.experience : 0 };
    }

    // ── The tavern ──

    void TavernPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        var room = d.baseBiome != null ? d.baseBiome.shopRoom : null;
        PageHeader("The tavern", "A walled-off room on every floor behind a shield door: the keeper's wares, a hot meal, rumours and a fire to rest by.",
            ("Walk into it", "A run that opens in the tavern.", () => Toast(room != null ? GameLauncher.PlayRoom(room) : "No tavern room.")),
            ("Visit it in the arena", "The Debug arena has a tavern west of its floor.", () => { if (!DebugDungeonToolbar.StartDebug()) Toast("Unity is busy: try again in a moment."); }));

        Section("THE ROOM");
        GUILayout.BeginHorizontal();
        var pic = RoomPicture(room, 180, 130);
        if (pic.Contains(Event.current.mousePosition) && Event.current.type == EventType.MouseDown && room != null) { ShowAsset(room); Event.current.Use(); }
        GUILayout.BeginVertical();
        if (d.baseBiome != null) RoomField(d, d.baseBiome, "Tavern room", room, v => d.baseBiome.shopRoom = v);
        if (room != null && GUILayout.Button("Open to build", GUILayout.Height(26), GUILayout.Width(200))) AssetDatabase.OpenAsset(room);
        GUILayout.Label("How often a floor has one:", dim);
        foreach (var st in l.RunStages.Where(s => s.biome != null))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{FloorsLabel(st)} · {st.biome.name}", GUILayout.Width(200));
            float chance = EditorGUILayout.Slider(st.biome.merchantChance, 0, 1);
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(chance, st.biome.merchantChance)) { Undo.RecordObject(st.biome, "Tavern chance"); st.biome.merchantChance = chance; EditorUtility.SetDirty(st.biome); AssetDatabase.SaveAssetIfDirty(st.biome); }
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        var hearth = room != null ? room.GetComponentInChildren<TavernHearth>(true) : null;
        if (hearth != null)
        {
            Section("THE HEARTH");
            GUILayout.Label("Rest once a floor: you sit by the fire and get this share of your health back.", dim);
            Fields(hearth, "heal", "restSeconds", "restedMessage");
        }

        var defaults = AssetDatabase.LoadAssetAtPath<TableLevelDefaults>(AssetStudioWorld.DefaultsPath);
        var keeper = defaults != null ? defaults.merchantPrefab : null;
        Section("THE KEEPER");
        if (keeper == null) GUILayout.Label("<color=#e0a85a>No merchant in Table level defaults.</color>", new GUIStyle(dim) { richText = true });
        else
        {
            GUILayout.BeginHorizontal();
            EnemyPicture(keeper, 56);
            GUILayout.BeginVertical();
            if (GUILayout.Button(keeper.name, cell, GUILayout.Height(22))) ShowAsset(keeper);
            var face = keeper.GetComponent<KeeperFace>();
            if (face != null) Fields(face, "face");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            var shop = keeper.GetComponentInChildren<Merchant>(true);
            if (shop != null)
            {
                GUILayout.Label("His wares (every keeper in every dungeon):", dim);
                Fields(shop, "merchantName", "staples", "stockTable", "gearCount", "alwaysTome", "pricePerFloor", "buysItems");
            }
            GUILayout.Label("Hit him three times and he turns on you (Provokable NPC on him).", dim);
        }

        Section("FOOD");
        GUILayout.Label("Each dish once a run, lasting the run.", dim);
        foreach (var guid in AssetDatabase.FindAssets("t:TavernDish", new[] { DishesFolder }))
        {
            var dish = AssetDatabase.LoadAssetAtPath<TavernDish>(AssetDatabase.GUIDToAssetPath(guid));
            if (dish == null) continue;
            GUILayout.BeginHorizontal(StudioSkin.Inset);
            var icon = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
            GUI.DrawTexture(icon, StudioSkin.Slot);
            if (dish.icon != null) GUI.DrawTexture(new Rect(icon.x + 3, icon.y + 3, 34, 34), dish.icon.texture, ScaleMode.ScaleToFit);
            GUILayout.BeginVertical();
            if (GUILayout.Button(dish.displayName, cell, GUILayout.Height(20))) ShowAsset(dish);
            Fields(dish, "price", "effectText");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        Section("RUMOURS");
        var rumoured = World.places.Where(p => p.rumoured).ToList();
        if (rumoured.Count == 0) GUILayout.Label("No place is told of in rumours.", dim);
        foreach (var p in rumoured)
        {
            GUILayout.BeginVertical(StudioSkin.Inset);
            if (GUILayout.Button(p.displayName, cell, GUILayout.Height(20))) ShowAsset(p);
            Fields(p, "rumour", "rumourTold");
            GUILayout.EndVertical();
        }
    }

    // ── The boss ──

    void BossPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        var st = GuardianStage(l);
        if (st == null) { PageHeader("No boss", "No stage of this dungeon ends with a guardian: tick Boss on its last floor on a floor's page."); return; }
        var b = st.biome;
        var g = b.guardian;
        var fight = g.fight;
        PageHeader(g.boss.name, $"Waits on floor {st.lastFloor} ({b.name}), {(g.arena != null && g.arena.GetComponentInChildren<DungeonStage>(true) != null ? "on a stage in front of the Dungeon Master" : "in " + (g.arena != null ? g.arena.name : "a generated room"))}.",
            ("Fight it", $"Floor {st.lastFloor} alone, the boss on it.", () => Toast(GameLauncher.PlayStage(d, Mathf.Max(0, st.index), true))),
            ("Just the stage", "The boss stage on its own (the toolbar's Boss): walk to its middle.", () => Toast(GameLauncher.PlayBossStage())),
            ("Alone in the arena", "The boss in the Debug arena, no stage and no fight script.", () => Toast(GameLauncher.FightInArena(g.boss))));

        GUILayout.BeginHorizontal(StudioSkin.Inset);
        EnemyPicture(g.boss, 72);
        var at = NumbersAt(d, g.boss, st.lastFloor, g);
        var data = DataOf(g.boss);
        GUILayout.BeginVertical();
        if (GUILayout.Button(g.boss.name, cell, GUILayout.Height(22))) ShowAsset(data != null ? (UnityEngine.Object)data : g.boss);
        GUILayout.Label($"On floor {st.lastFloor}: {at.health:0} health, {at.damage:0.#} damage, {at.armor:0.#} armour, {at.xp:0} xp.", EditorStyles.boldLabel);
        GUILayout.Label($"From its own {Stat(data, StatType.MaxHealth):0} health and {Stat(data, StatType.AttackDamage):0.#} damage, times the balance's boss health ({(d.balance != null ? d.balance.bossHealthMultiplier : 1):0.##}x) and its own multipliers below.", new GUIStyle(dim) { wordWrap = true });
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        Section("THE GUARDIAN");
        Fields(b, "guardian");

        if (fight == null) { GUILayout.Label("No fight script: it fights like any enemy.", dim); return; }
        Section("THE FIGHT  ·  " + fight.name);
        GUILayout.Label("The script of the stage fight: music, what the Dungeon Master does and says. Everything else is in its inspector.", dim);
        if (GUILayout.Button("Open the whole fight in the inspector", GUILayout.Width(300), GUILayout.Height(24))) ShowAsset(fight);
        Fields(fight, "music", "bpm");
        Lines(fight, "openingLine", "fightLine");

        Section("WHAT HAPPENS, IN ORDER");
        BossEvents(fight);

        Section("THE DUNGEON MASTER'S BEAM");
        GUILayout.Label("He pours light at the player: shield up and facing him, or it burns.", dim);
        Fields(fight, "beamSeconds", "beamDamagePerSecond", "beamWarning", "beamKneel", "beamOnBeat");

        Section("PHASE TWO");
        Fields(fight, "phaseTwoHealth");
        Lines(fight, "phaseTwoLine");

        Section("THE REWARD");
        GUILayout.Label("A chest comes out where the boss fell: its loot, plus gold and the items listed on the guardian.", dim);
        Fields(b, "loot");
        if (g.bossLoot != null && GUILayout.Button("Show " + g.bossLoot.name, GUILayout.Width(240), GUILayout.Height(24))) ShowAsset(g.bossLoot);
    }

    // The fight's events, one compact row each: on or off, when, what, and its own number or line.
    void BossEvents(BossFight fight)
    {
        using var so = new SerializedObject(fight);
        so.Update();
        var list = so.FindProperty("events");
        float beat = 60f / Mathf.Max(1, fight.bpm);
        for (int i = 0; list != null && i < list.arraySize; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            GUILayout.BeginHorizontal(StudioSkin.Inset);
            var on = e.FindPropertyRelative("enabled");
            on.boolValue = GUILayout.Toggle(on.boolValue, GUIContent.none, GUILayout.Width(18));
            var when = e.FindPropertyRelative("when");
            EditorGUILayout.PropertyField(when, GUIContent.none, GUILayout.Width(110));
            switch ((BossEvent.When)when.enumValueIndex)
            {
                case BossEvent.When.AtSeconds: Num(e, "seconds", "s", 50); break;
                case BossEvent.When.AtBeat: Num(e, "startBeat", "beat", 50); GUILayout.Label($"({e.FindPropertyRelative("startBeat").floatValue * beat:0.#} s)", dim, GUILayout.Width(60)); break;
                case BossEvent.When.EveryBeats:
                    Num(e, "beats", "every", 44); Num(e, "startBeat", "from beat", 44);
                    GUILayout.Label($"(every {e.FindPropertyRelative("beats").floatValue * beat:0.#} s)", dim, GUILayout.Width(96));
                    break;
                case BossEvent.When.AtBossHealth:
                    var h = e.FindPropertyRelative("health");
                    GUILayout.Label("at", dim, GUILayout.Width(18));
                    h.floatValue = Mathf.Clamp01(EditorGUILayout.FloatField(h.floatValue * 100, GUILayout.Width(40)) / 100);
                    GUILayout.Label("% health", dim, GUILayout.Width(60));
                    break;
            }
            var what = e.FindPropertyRelative("what");
            EditorGUILayout.PropertyField(what, GUIContent.none, GUILayout.Width(80));
            switch ((BossEvent.What)what.enumValueIndex)
            {
                case BossEvent.What.Say: var text = e.FindPropertyRelative("line").FindPropertyRelative("text"); text.stringValue = EditorGUILayout.TextField(text.stringValue); break;
                case BossEvent.What.Music: EditorGUILayout.PropertyField(e.FindPropertyRelative("music"), GUIContent.none); break;
                case BossEvent.What.Spawn: EditorGUILayout.PropertyField(e.FindPropertyRelative("enemy"), GUIContent.none); Num(e, "count", "x", 32); break;
                case BossEvent.What.Shake: Num(e, "shake", "strength", 40); break;
                case BossEvent.What.Volley: EditorGUILayout.PropertyField(e.FindPropertyRelative("spell"), GUIContent.none, GUILayout.Width(110)); Num(e, "damage", "damage a ball", 40); break;
                default: GUILayout.Label($"{fight.beamSeconds:0.#} s of beam", dim); break;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", GUILayout.Width(26))) { list.DeleteArrayElementAtIndex(i); GUILayout.EndHorizontal(); break; }
            GUILayout.EndHorizontal();
        }
        if (list != null && GUILayout.Button("+ Something else happens", GUILayout.Width(240), GUILayout.Height(26))) list.arraySize++;
        if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(fight);
    }

    static void Num(SerializedProperty parent, string name, string label, float width)
    {
        var p = parent.FindPropertyRelative(name);
        if (p == null) return;
        GUILayout.Label(label, new GUIStyle(EditorStyles.label) { normal = { textColor = StudioSkin.Faint.normal.textColor } }, GUILayout.ExpandWidth(false));
        if (p.propertyType == SerializedPropertyType.Integer) p.intValue = EditorGUILayout.IntField(p.intValue, GUILayout.Width(width));
        else p.floatValue = EditorGUILayout.FloatField(p.floatValue, GUILayout.Width(width));
    }

    // ── Who lives where ──

    void EnemiesPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        PageHeader("Who lives where", "Every enemy of this dungeon, floor by floor: its weight there (how often it comes; empty: it doesn't) and what it has there. Type a weight in an empty cell to bring it to that floor, clear it to take it off.");
        var run = l.RunStages.Where(s => s.biome != null).ToList();
        var enemies = run.SelectMany(s => s.biome.enemies ?? Array.Empty<DungeonBiome.Spawn>()).Where(e => e != null && e.enemy != null).Select(e => e.enemy).Distinct()
            .OrderBy(e => run.FindIndex(s => s.biome.enemies != null && s.biome.enemies.Any(x => x != null && x.enemy == e))).ThenBy(e => e.name).ToList();
        const float col = 150;
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(44));
        GUILayout.Label("enemy", dim, GUILayout.Width(150));
        foreach (var st in run) GUILayout.Label($"{FloorsLabel(st)}\n<color=#b8ae9c>{st.biome.name}</color>", new GUIStyle(gold) { richText = true }, GUILayout.Width(col));
        GUILayout.EndHorizontal();
        foreach (var enemy in enemies)
        {
            GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Height(46));
            EnemyPicture(enemy, 40);
            GUILayout.BeginVertical(GUILayout.Width(150));
            if (GUILayout.Button(enemy.name, cell, GUILayout.Height(20))) ShowAsset(DataOf(enemy) != null ? (UnityEngine.Object)DataOf(enemy) : enemy);
            if (GUILayout.Button(new GUIContent("Fight it", "Alone in the Debug arena."), EditorStyles.miniButton, GUILayout.Width(70))) Toast(GameLauncher.FightInArena(enemy));
            GUILayout.EndVertical();
            foreach (var st in run)
            {
                var b = st.biome;
                int i = Array.FindIndex(b.enemies ?? Array.Empty<DungeonBiome.Spawn>(), x => x != null && x.enemy == enemy);
                float weight = i >= 0 ? b.enemies[i].weight : 0;
                GUILayout.BeginVertical(GUILayout.Width(col));
                string typed = EditorGUILayout.DelayedTextField(i >= 0 ? weight.ToString("0.##") : "", GUILayout.Width(50));
                if (i >= 0)
                {
                    var at = NumbersAt(d, enemy, st.firstFloor, null);
                    GUILayout.Label($"{at.health:0} hp · {at.damage:0.#} dmg · {at.armor:0.#} arm", new GUIStyle(dim) { fontSize = Mathf.Max(9, StudioSkin.Size - 2) });
                }
                else GUILayout.Label("not here", dim);
                GUILayout.EndVertical();
                float.TryParse(typed, out float now);
                if (typed.Trim().Length == 0) now = 0;
                if (!Mathf.Approximately(now, weight))
                {
                    if (i < 0 && now > 0) AssetStudioWorld.AddEnemy(b, enemy, now);
                    else if (i >= 0 && now <= 0) AssetStudioWorld.RemoveEnemy(b, i);
                    else if (i >= 0) AssetStudioWorld.SetEnemy(b, i, now, b.enemies[i].fromFloor);
                    RefreshWorld(); GUIUtility.ExitGUI();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        var guardian = GuardianStage(l);
        if (guardian != null)
        {
            var g = guardian.biome.guardian;
            var at = NumbersAt(d, g.boss, guardian.lastFloor, g);
            GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.Height(46));
            EnemyPicture(g.boss, 40);
            if (GUILayout.Button($"<color=#e24b4a>Boss</color>  {g.boss.name}", cell, GUILayout.Width(150), GUILayout.Height(20))) GoTo("boss", g.boss);
            GUILayout.Label($"Floor {guardian.lastFloor}: {at.health:0} hp · {at.damage:0.#} dmg · {at.armor:0.#} arm", dim);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Bring another enemy in...", GUILayout.Width(260), GUILayout.Height(28)))
        {
            var menu = new GenericMenu();
            foreach (var e in categories.Where(c => c.name == "Enemies").SelectMany(c => c.entries))
            {
                var go = e.Load() as GameObject;
                if (go == null || !AssetStudioWorld.IsEnemy(go) || enemies.Contains(go)) continue;
                foreach (var st in run)
                {
                    var b = st.biome;
                    menu.AddItem(new GUIContent($"{e.name}/{FloorsLabel(st)} ({b.name})"), false, () => { AssetStudioWorld.AddEnemy(b, go); RefreshWorld(); });
                }
            }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Every enemy is in already"));
            menu.ShowAsContext();
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("A room has its biome's few to many enemies (on each floor's page). An enemy's own stats are in its inspector: click its name.", dim);
    }

    // ── Rooms ──

    void RoomsPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        PageHeader("Rooms", "Every hand-built room this dungeon draws from, and which floors' pools it is in. Tick a floor to put a room in its pool; the number is how common it is there. Right-click a picture to open or play it.");
        var run = l.RunStages.Where(s => s.biome != null).ToList();
        // Search, a kind to show (every kind, or one) and the order.
        GUILayout.BeginHorizontal();
        dungeonRoomSearch = EditorGUILayout.TextField(dungeonRoomSearch, EditorStyles.toolbarSearchField, GUILayout.Width(150));
        GUILayout.Space(6);
        for (int k = -1; k < AssetStudioRoomKinds.Order.Length; k++)
        {
            var kind = k < 0 ? (DungeonRoomKind?)null : AssetStudioRoomKinds.Order[k];
            bool on = dungeonRoomKind == (k < 0 ? -1 : (int)kind.Value);
            var icon = kind.HasValue ? AssetStudioRoomKinds.Icon(kind.Value) : null;
            var content = new GUIContent(" " + (kind.HasValue ? AssetStudioRoomKinds.Name(kind.Value) : "All"), icon, kind.HasValue ? AssetStudioRoomKinds.Tip(kind.Value) : "Rooms of every kind.");
            if (GUILayout.Toggle(on, content, StudioSkin.Tab, GUILayout.Height(24), GUILayout.Width(kind.HasValue ? 86 : 44)) && !on) dungeonRoomKind = k < 0 ? -1 : (int)kind.Value;
        }
        GUILayout.FlexibleSpace();
        dungeonRoomSort = EditorGUILayout.Popup(dungeonRoomSort, DungeonRoomSorts, GUILayout.Width(84));
        GUILayout.EndHorizontal();
        string s = dungeonRoomSearch.Trim().ToLowerInvariant();
        bool Shown(GameObject r) => (s.Length == 0 || r.name.ToLowerInvariant().Contains(s)) && (dungeonRoomKind < 0 || (int)AssetStudioRoomKinds.Of(r) == dungeonRoomKind);
        // The level's own rooms first, then the pools'.
        var own = new List<(GameObject room, string role)>();
        if (d.startingRoom != null) own.Add((d.startingRoom, "the first room"));
        if (d.baseBiome != null && d.baseBiome.shopRoom != null) own.Add((d.baseBiome.shopRoom, "the tavern, every floor"));
        foreach (var st in run) if (st.boss && AssetStudioWorld.HasGuardian(st.biome) && st.biome.guardian.arena != null) own.Add((st.biome.guardian.arena, $"the boss's, floor {st.lastFloor}"));
        Section("ITS OWN ROOMS");
        GUILayout.BeginHorizontal();
        foreach (var (room, role) in own.Where(o => Shown(o.room)))
        {
            GUILayout.BeginVertical(GUILayout.Width(150));
            RoomCell(room, 150, 104);
            GUILayout.Label(role, new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        var pooled = run.SelectMany(st => st.biome.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>()).Where(c => c != null && c.template != null).Select(c => c.template).Distinct()
            .Where(Shown).OrderBy(r => dungeonRoomSort == 1 ? AssetStudioRoomKinds.Rank(r) : 0).ThenBy(r => r.name, StringComparer.OrdinalIgnoreCase).ToList();
        GUILayout.BeginHorizontal();
        Section($"THE POOLS  ·  {pooled.Count} rooms");
        GUILayout.FlexibleSpace();
        int unsorted = World.rooms.Count(r => AssetStudioRoomKinds.Of(r) == DungeonRoomKind.Unsorted);
        if (unsorted > 0 && GUILayout.Button(new GUIContent($"Sort the {unsorted} unsorted for me...", "Gives every room without a kind one, from what it holds: an event script makes it Scripted; a chest, spikes, a shrine or feature, a pit or a portal Active; the rest Visual. Rooms you sorted keep theirs."), GUILayout.Height(24)))
            GuessRoomKinds();
        GUILayout.EndHorizontal();
        const float col = 110;
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(96 + 300));
        foreach (var st in run) GUILayout.Label($"{FloorsLabel(st)}\n<color=#b8ae9c>{st.biome.name}</color>", new GUIStyle(gold) { richText = true }, GUILayout.Width(col));
        GUILayout.EndHorizontal();
        foreach (var room in pooled)
        {
            GUILayout.BeginHorizontal(StudioSkin.Inset, GUILayout.MinHeight(70));
            RoomCell(room, 90, 64);
            GUILayout.BeginVertical(GUILayout.Width(300));
            if (GUILayout.Button(room.name, cell, GUILayout.Height(20))) ShowAsset(room);
            var choice = run.SelectMany(st => st.biome.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>()).FirstOrDefault(c => c != null && c.template == room);
            var tmpl = room.GetComponent<DungeonRoomTemplate>();
            string what = tmpl != null && !string.IsNullOrWhiteSpace(tmpl.description) ? tmpl.description.Trim() : "<color=#e0a85a>No description yet.</color>";
            GUILayout.Label(new GUIContent(what + "\n<color=#8f897e>" + RoomAbout(room, choice) + "</color>", tmpl != null ? tmpl.description : null), new GUIStyle(dim) { wordWrap = true, richText = true });
            GUILayout.EndVertical();
            foreach (var st in run)
            {
                var b = st.biome;
                int i = Array.FindIndex(b.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>(), c => c != null && c.template == room);
                GUILayout.BeginHorizontal(GUILayout.Width(col));
                bool inPool = GUILayout.Toggle(i >= 0, GUIContent.none, GUILayout.Width(18));
                float weight = i >= 0 ? b.roomTemplates[i].weight : 1;
                float typed = i >= 0 ? EditorGUILayout.DelayedFloatField(weight, GUILayout.Width(40)) : weight;
                GUILayout.EndHorizontal();
                if (inPool != (i >= 0))
                {
                    if (inPool) AssetStudioWorld.AddToPool(b, room); else AssetStudioWorld.RemoveFromPool(b, i);
                    RefreshWorld(); GUIUtility.ExitGUI();
                }
                if (i >= 0 && !Mathf.Approximately(typed, weight)) { Undo.RecordObject(b, "Pool weight"); b.roomTemplates[i].weight = Mathf.Max(0, typed); EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(6);
        if (GUILayout.Button("+ Add a room...", GUILayout.Width(220), GUILayout.Height(28)))
        {
            var menu = new GenericMenu();
            foreach (var st in run) menu.AddItem(new GUIContent($"A new room/for {FloorsLabel(st)} ({st.biome.name})..."), false, () => { StartForm(WorldPane.NewRoom); formLevel = d; formBiome = st.biome; });
            menu.AddSeparator("");
            foreach (var room in World.rooms.Where(r => !pooled.Contains(r) && !own.Any(o => o.room == r)))
                foreach (var st in run) menu.AddItem(new GUIContent($"An existing room/{room.name}/{FloorsLabel(st)} ({st.biome.name})"), false, () => { AssetStudioWorld.AddToPool(st.biome, room); RefreshWorld(); });
            menu.ShowAsContext();
        }
    }

    [SerializeField] int dungeonRoomKind = -1, dungeonRoomSort = 1;   // the Rooms page: the kind shown (-1 every kind) and the order
    static readonly string[] DungeonRoomSorts = { "By name", "By kind" };

    // Every room without a kind gets the one its contents suggest (AssetStudioRoomKinds.Guess), after asking.
    void GuessRoomKinds()
    {
        var rooms = World.rooms.Where(r => AssetStudioRoomKinds.Of(r) == DungeonRoomKind.Unsorted).ToList();
        var guesses = rooms.Select(r => (room: r, kind: AssetStudioRoomKinds.Guess(r))).ToList();
        string Count(DungeonRoomKind k) { int n = guesses.Count(g => g.kind == k); return $"{n} {AssetStudioRoomKinds.Name(k).ToLowerInvariant()}"; }
        if (!EditorUtility.DisplayDialog("Sort the rooms", $"{rooms.Count} rooms have no kind yet. From what they hold: {Count(DungeonRoomKind.Scripted)}, {Count(DungeonRoomKind.Active)}, {Count(DungeonRoomKind.Visual)}.\n\nRooms you sorted keep theirs. Change any with a right-click after.", "Sort them", "Cancel")) return;
        Undo.SetCurrentGroupName("Sort the rooms");
        foreach (var (room, kind) in guesses) AssetStudioRoomKinds.Set(room, kind);
        Toast($"Sorted {rooms.Count} rooms. Right-click any to change it.");
        Repaint();
    }

    // What a pooled room is in a few words: the kinds of room it may become, and what stands out about it.
    static string RoomAbout(GameObject room, DungeonBiome.RoomTemplateChoice choice)
    {
        var t = room.GetComponent<DungeonRoomTemplate>();
        var parts = new List<string>();
        if (choice != null) parts.Add(choice.rooms.ToString().ToLowerInvariant().Replace(" | ", ", "));
        if (t != null)
        {
            if (t.heightTiles > 1) parts.Add($"{t.heightTiles} tiles high");
            if (t.openCeiling) parts.Add("open sky");
            if (t.roomEvent != DungeonRoomEvent.Rolled) parts.Add(t.roomEvent == DungeonRoomEvent.None ? "no event" : t.roomEvent.ToString().ToLowerInvariant());
            if (t.enterLines != null && t.enterLines.Any(x => !string.IsNullOrWhiteSpace(x))) parts.Add("he says something");
        }
        return string.Join(" · ", parts);
    }

    // A room's picture that shows it on a click and offers to open or play it on a right-click.
    void RoomCell(GameObject room, float w, float h)
    {
        var r = RoomPicture(room, w, h);
        if (room != null) AssetStudioRoomKinds.Badge(new Rect(r.x + 3, r.y + 3, 28, 28), room);
        if (room != null && room.TryGetComponent<DungeonRoomTemplate>(out var described) && !string.IsNullOrWhiteSpace(described.description)) GUI.Label(r, new GUIContent("", room.name + ": " + described.description));
        var ev = Event.current;
        if (room == null || ev.type != EventType.MouseDown || !r.Contains(ev.mousePosition)) return;
        if (ev.button == 1)
        {
            var menu = new GenericMenu();
            AssetStudioRoomKinds.AddTo(menu, room, Repaint);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open to build"), false, () => AssetDatabase.OpenAsset(room));
            menu.AddItem(new GUIContent("Play from this room"), false, () => Toast(GameLauncher.PlayRoom(room)));
            menu.AddItem(new GUIContent("Show in Project"), false, () => { Selection.activeObject = room; EditorGUIUtility.PingObject(room); });
            menu.ShowAsContext();
        }
        else ShowAsset(room);
        ev.Use();
    }

    // ── Loot ──

    void LootPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        PageHeader("Loot", "What drops on these floors: the gear's tier by floor, each floor's loot, the chests, the boss's reward and the rules every dungeon shares.");

        Section("GEAR BY FLOOR");
        GUILayout.Label("Which tier gear comes in, from each floor on (1 Bronze/Leather, 2 Iron, 3 Steel, 4 Crystal).", dim);
        if (d.gearByFloor != null)
            foreach (var row in d.gearByFloor.Where(x => x != null))
            {
                float sum = Mathf.Max(.01f, row.tier1 + row.tier2 + row.tier3 + row.tier4 + row.tier5 + row.tier6);
                var bar = GUILayoutUtility.GetRect(10, 18, GUILayout.ExpandWidth(true));
                float x = bar.x + 90;
                GUI.Label(new Rect(bar.x, bar.y, 90, 18), $"from floor {row.fromFloor}", dim);
                var tiers = new[] { (row.tier1, new Color(.62f, .45f, .26f), "1"), (row.tier2, new Color(.55f, .57f, .6f), "2"), (row.tier3, new Color(.78f, .8f, .84f), "3"), (row.tier4, new Color(.45f, .75f, .9f), "4"), (row.tier5, new Color(.3f, .42f, .36f), "5"), (row.tier6, new Color(.9f, .72f, .3f), "6") };
                foreach (var (share, colour, name) in tiers)
                {
                    if (share <= 0) continue;
                    float w = (bar.width - 90) * share / sum;
                    EditorGUI.DrawRect(new Rect(x, bar.y + 2, w - 1, 14), colour);
                    if (w > 40) GUI.Label(new Rect(x + 4, bar.y, w, 18), $"tier {name} {share / sum * 100:0}%", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.black } });
                    x += w;
                }
            }
        Fields(d, "gearByFloor");

        Section("EACH FLOOR'S LOOT");
        foreach (var st in l.RunStages.Where(s => s.biome != null))
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{FloorsLabel(st)} · {st.biome.name}", GUILayout.Width(200));
            var loot = (LootSource)EditorGUILayout.ObjectField(st.biome.loot, typeof(LootSource), false);
            if (loot != st.biome.loot) { Undo.RecordObject(st.biome, "Loot"); st.biome.loot = loot; EditorUtility.SetDirty(st.biome); AssetDatabase.SaveAssetIfDirty(st.biome); }
            if (st.biome.loot != null && GUILayout.Button("Show", GUILayout.Width(60))) ShowAsset(st.biome.loot);
            GUILayout.EndHorizontal();
        }

        Section("CHESTS");
        if (d.baseBiome != null)
        {
            var tiers = d.baseBiome.chestTiers ?? Array.Empty<DungeonWeightedPrefab>();
            float total = Mathf.Max(.01f, tiers.Where(t => t != null && t.prefab != null).Sum(t => t.weight));
            foreach (var t in tiers.Where(t => t != null && t.prefab != null))
            {
                GUILayout.BeginHorizontal();
                EnemyPicture(t.prefab, 36);
                if (GUILayout.Button(t.prefab.name, cell, GUILayout.Width(180), GUILayout.Height(20))) ShowAsset(t.prefab);
                GUILayout.Label($"{t.weight / total * 100:0}% of chests", GUILayout.Width(110));
                var c = t.prefab.GetComponent<DungeonContainer>();
                if (c != null) GUILayout.Label($"{(c.extraRolls > 0 ? "+" + c.extraRolls + " roll" + (c.extraRolls == 1 ? "" : "s") : "")}{(c.rareRollFloors > 0 ? $", one item rolled {c.rareRollFloors} floors deeper" : "")}", dim);
                GUILayout.EndHorizontal();
            }
            Fields(d.baseBiome, "chestTiers");
        }
        Fields(d, "startingChest", "startingChestConsumable");

        var guardian = GuardianStage(l);
        if (guardian != null)
        {
            Section("THE BOSS'S CHEST");
            var g = guardian.biome.guardian;
            GUILayout.Label($"{g.boss.name} leaves a chest with its own loot.", dim);
            if (g.bossLoot != null && GUILayout.Button("Show " + g.bossLoot.name, GUILayout.Width(240), GUILayout.Height(24))) ShowAsset(g.bossLoot);
        }

        var inv = AssetDatabase.LoadAssetAtPath<GameObject>(InventoryManagerPath)?.GetComponent<InventoryManager>();
        if (inv != null)
        {
            Section("EVERY DUNGEON: DROPS AND RARITY");
            GUILayout.Label("InventoryManager: what each kind of source drops, the rarity steps and the whole-game multipliers.", dim);
            Fields(inv, "dropChanceMultiplier", "enemyDropMultiplier", "rarityChanceMultiplier", "goldMultiplier", "merchantRarity", "enemyDrops", "breakableDrops", "chestDrops", "rarities");
        }
    }

    // ── Difficulty ──

    void DifficultyPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        PageHeader("Difficulty", "How hard each floor is: the level's balance and what every enemy has on each floor because of it.");
        var run = l.RunStages.Where(s => s.biome != null).ToList();
        int floors = Mathf.Max(1, d.FloorCount);
        var enemies = run.SelectMany(s => s.biome.enemies ?? Array.Empty<DungeonBiome.Spawn>()).Where(e => e != null && e.enemy != null).Select(e => e.enemy).Distinct().ToList();

        Section("HEALTH AND DAMAGE ON EACH FLOOR");
        GUILayout.Label("Shown on the floors where it comes. hp · damage · armour.", dim);
        const float col = 170;
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(180));
        for (int f = 1; f <= floors; f++) GUILayout.Label($"Floor {f}", gold, GUILayout.Width(col));
        GUILayout.EndHorizontal();
        foreach (var enemy in enemies)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(enemy.name, cell, GUILayout.Width(180))) ShowAsset(DataOf(enemy) != null ? (UnityEngine.Object)DataOf(enemy) : enemy);
            for (int f = 1; f <= floors; f++)
            {
                var b = d.Biome(f);
                bool here = b != null && b.enemies != null && b.enemies.Any(x => x != null && x.enemy == enemy && x.fromFloor <= 1 + f - FirstFloorOf(l, b));
                var at = NumbersAt(d, enemy, f, null);
                GUILayout.Label(here ? $"{at.health:0} · {at.damage:0.#} · {at.armor:0.#}" : $"<color=#5d5a55>{at.health:0} · {at.damage:0.#} · {at.armor:0.#}</color>", new GUIStyle(EditorStyles.label) { richText = true }, GUILayout.Width(col));
            }
            GUILayout.EndHorizontal();
        }
        var guardian = GuardianStage(l);
        if (guardian != null)
        {
            var g = guardian.biome.guardian;
            var at = NumbersAt(d, g.boss, guardian.lastFloor, g);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"<color=#e24b4a>Boss</color> {g.boss.name}", cell, GUILayout.Width(180))) GoTo("boss", g.boss);
            GUILayout.Space((guardian.lastFloor - 1) * col);
            GUILayout.Label($"{at.health:0} · {at.damage:0.#} · {at.armor:0.#}", EditorStyles.boldLabel, GUILayout.Width(col));
            GUILayout.EndHorizontal();
        }

        Section("THE LEVEL'S BALANCE" + (d.balance != null ? "  ·  " + d.balance.name : ""));
        Fields(d, "balance");
        if (d.balance != null)
        {
            var users = World.levels.Where(x => !x.tool && x.data.balance == d.balance && x.data != d).ToList();
            if (users.Count > 0) GUILayout.Label($"<color=#e0a85a>Also used by {string.Join(", ", users.Select(u => u.data.name))}.</color>", new GUIStyle(dim) { richText = true });
            AllFields(d.balance);
        }
        Section("EVERY DUNGEON");
        GUILayout.Label($"ProgressionManager > Balance multiplies every enemy on top: damage {wideDamage:0.##}x, health {wideHealth:0.##}x (the room's Systems object). Shown in the numbers above.", new GUIStyle(dim) { wordWrap = true });
        if (GUILayout.Button("Show ProgressionManager", GUILayout.Width(240), GUILayout.Height(24)))
        {
            UnityEngine.Object pm = ProgressionManager.HasInstance ? ProgressionManager.Instance : AssetDatabase.LoadAssetAtPath<GameObject>(ProgressionManagerPath);
            if (pm != null) { Selection.activeObject = pm; EditorGUIUtility.PingObject(pm); }
        }
    }

    // The first floor of the run a biome runs.
    static int FirstFloorOf(AssetStudioWorld.Level l, DungeonBiome b) => l.RunStages.FirstOrDefault(s => s.biome == b)?.firstFloor ?? 1;

    // ── The Dungeon Master ──

    void WordsPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        PageHeader("The Dungeon Master", "What he says in this dungeon: when things happen in a run, on the way into each floor, and in the boss fight. Every line in the game is also in the Dialogue view.",
            ("Open the Dialogue view", "Every Dungeon Master line in the game, by where it plays.", () => OpenDialogue()));
        DialogueTab(d);
        foreach (var st in l.RunStages.Where(s => s.biome != null))
        {
            Section($"ON THE WAY INTO {FloorsLabel(st).ToUpperInvariant()} · {st.biome.name}");
            Fields(st.biome, "entryLineChance");
            Lines(st.biome, "entryLines");
        }
        var guardian = GuardianStage(l);
        var fight = guardian?.biome.guardian.fight;
        if (fight != null)
        {
            Section("THE BOSS FIGHT · " + fight.name);
            Lines(fight, "openingLine", "fightLine", "phaseTwoLine", "beamWarned", "beamBlocked", "beamHit", "volleyWarned", "bossHalf", "bossLow", "playerLow", "victory", "defeat");
        }
    }

    // Dungeon Master lines (DungeonMaster.Line, alone or in a list or array) as plain rows: each line's text in full, its
    // mumble and in-person switches, a cross to take it out, and + Another line. (Unity's own list drawer shows a line
    // folded, by its first words, on rows the studio's skin leaves blank.) Saved at once; Ctrl+Z undoes it.
    void Lines(UnityEngine.Object owner, params string[] names)
    {
        if (owner == null) return;
        using var so = new SerializedObject(owner);
        so.Update();
        foreach (var name in names)
        {
            var p = so.FindProperty(name);
            if (p == null) continue;
            if (p.isArray && p.propertyType == SerializedPropertyType.Generic)
            {
                GUILayout.Label(p.displayName, new GUIStyle(dim) { fontStyle = FontStyle.Bold });
                for (int i = 0; i < p.arraySize; i++)
                    if (LineRow(p.GetArrayElementAtIndex(i), true)) { p.DeleteArrayElementAtIndex(i); break; }
                if (p.arraySize == 0) GUILayout.Label("No lines: he says nothing here.", dim);
                if (GUILayout.Button("+ Another line", GUILayout.Width(150), GUILayout.Height(22)))
                {
                    p.arraySize++;
                    var added = p.GetArrayElementAtIndex(p.arraySize - 1);
                    added.FindPropertyRelative("text").stringValue = "";
                }
                GUILayout.Space(4);
            }
            else if (p.FindPropertyRelative("text") != null)
            {
                GUILayout.Label(p.displayName, new GUIStyle(dim) { fontStyle = FontStyle.Bold });
                LineRow(p, false);
            }
            else EditorGUILayout.PropertyField(p, true);
        }
        if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(owner);
    }

    // One line's row; true when its cross was pressed.
    bool LineRow(SerializedProperty line, bool removable)
    {
        var text = line.FindPropertyRelative("text");
        if (text == null) { EditorGUILayout.PropertyField(line, true); return false; }
        GUILayout.BeginHorizontal();
        text.stringValue = EditorGUILayout.TextField(text.stringValue);
        var mumble = line.FindPropertyRelative("mumble");
        if (mumble != null) mumble.boolValue = GUILayout.Toggle(mumble.boolValue, new GUIContent(" mumble", "His gibberish voice as the line appears (a recorded voice, set in the inspector, plays instead)."), GUILayout.Width(80));
        var person = line.FindPropertyRelative("inPerson");
        if (person != null) person.boolValue = GUILayout.Toggle(person.boolValue, new GUIContent(" in person", "Said across the table: paced for reading, and the seated Dungeon Master gestures."), GUILayout.Width(90));
        bool remove = removable && GUILayout.Button(new GUIContent("✕", "Take this line out."), GUILayout.Width(26));
        GUILayout.EndHorizontal();
        return remove;
    }

    // For tests (an eval): the Dungeon section on one page ("overview", "floor:0", "boss", ...).
    public void TestDungeon(string page) { OpenDungeon(); GoTo(page); Repaint(); }

    // ── Music and sound ──

    void SoundPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        PageHeader("Music and sound", "The dungeon's music, each floor's own, the boss fight's and the tavern's.");
        SoundTab(l);
        var guardian = GuardianStage(l);
        var fight = guardian?.biome.guardian.fight;
        if (fight != null) { Section("THE BOSS FIGHT"); Fields(fight, "music", "musicVolume", "bpm", "firstBeat"); }
        var room = d.baseBiome != null ? d.baseBiome.shopRoom : null;
        var music = room != null ? room.GetComponent<RoomMusic>() : null;
        if (music != null) { Section("THE TAVERN"); Fields(music, "music"); }
        var reveal = AssetDatabase.LoadAssetAtPath<TableLevelRevealSettings>(RevealSettingsPath);
        if (reveal != null) { Section("THE FLOOR BEING BUILT"); Fields(reveal, "footprintSound", "entranceSlamSound", "entranceTorchSound", "clackVolume"); }
    }
}

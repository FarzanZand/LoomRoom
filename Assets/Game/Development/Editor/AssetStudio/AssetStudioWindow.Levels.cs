using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's Levels page: every level in one list (by the act of the place that plays it), and one page per level
// that reads like its run. A header with what it is and Play buttons; tabs Run, Map, Look, Balance, Sound and Dialogue.
// Run shows the floors as a strip of stages (the biome each runs on, its boss) and, for the stage picked, what its
// floors are made of: the enemies (weight drawn as a bar), the boss and loot, the hand-built rooms as pictures and the
// room styles as tiled samples (AssetStudioStyles). Everything else is still in the inspector on the right, which shows
// the level or the picked stage's biome. Changes go through Undo and are saved at once.
public partial class AssetStudioWindow
{
    [SerializeField] string levelPath, levelBiomePath;
    [SerializeField] int levelStage = -1, levelTab;
    [SerializeField] bool levelDefaults, levelTools;
    Vector2 levelListScroll, levelPageScroll;
    string levelSearch = "";
    static readonly string[] LevelTabs = { "Run", "Map", "Look", "Balance", "Sound", "Dialogue" };
    readonly Dictionary<DungeonRoomStyle, Texture2D> styleThumbs = new();

    bool showingLevels => studioView == StudioView.Levels;

    TableLevelData PickedLevel => string.IsNullOrEmpty(levelPath) ? null : AssetDatabase.LoadAssetAtPath<TableLevelData>(levelPath);
    DungeonBiome PickedBiome => string.IsNullOrEmpty(levelBiomePath) ? null : AssetDatabase.LoadAssetAtPath<DungeonBiome>(levelBiomePath);

    void OpenLevels()
    {
        studioView = StudioView.Levels;
        worldPane = WorldPane.Picked;
        if (PickedLevel == null && PickedBiome == null && !levelDefaults)
        {
            var first = World.Groups(false).SelectMany(g => g.levels).FirstOrDefault();
            if (first != null) OpenLevel(first.data, -1);
        }
        GUI.FocusControl(null);
    }

    // A level's page; stage: the index of the stage picked on its Run tab (-1: its first stage, or its base biome).
    void OpenLevel(TableLevelData level, int stage)
    {
        if (level == null) return;
        studioView = StudioView.Levels;
        worldPane = WorldPane.Picked;
        levelPath = AssetDatabase.GetAssetPath(level);
        levelBiomePath = null; levelDefaults = false;
        var l = World.LevelOf(level);
        levelStage = stage >= 0 ? stage : l != null && l.Staged && l.stages.Count > 0 ? l.stages[0].index : -1;
        levelPageScroll = Vector2.zero;
        ShowAsset(level);
        Repaint();
    }

    // A biome file several levels run (or none): its page on its own.
    void OpenSharedBiome(DungeonBiome b)
    {
        studioView = StudioView.Levels;
        worldPane = WorldPane.Picked;
        levelPath = null; levelDefaults = false;
        levelBiomePath = AssetDatabase.GetAssetPath(b);
        levelPageScroll = Vector2.zero;
        ShowAsset(b);
    }

    // ── The list ──

    void DrawLevelList(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20));
        if (GUILayout.Button("+ New level", GUILayout.Height(30))) { StartForm(WorldPane.NewLevel); }
        GUILayout.Space(4);
        levelSearch = EditorGUILayout.TextField(levelSearch, EditorStyles.toolbarSearchField);
        string s = levelSearch.Trim().ToLowerInvariant();
        levelListScroll = GUILayout.BeginScrollView(levelListScroll);
        var m = World;
        foreach (var (group, levels) in m.Groups(levelTools))
        {
            var shownLevels = levels.Where(l => s.Length == 0 || l.data.name.ToLowerInvariant().Contains(s) || l.places.Any(p => p.displayName.ToLowerInvariant().Contains(s))).ToList();
            if (shownLevels.Count == 0) continue;
            GUILayout.Space(8);
            GUILayout.Label(group, gold);
            foreach (var l in shownLevels)
            {
                bool on = !levelDefaults && PickedBiome == null && PickedLevel == l.data;
                string where = l.places.Count > 0 ? string.Join(", ", l.places.Select(p => p.displayName)) : "not on the map";
                string what = l.data.IsDungeon ? $"{l.data.FloorCount} floor{(l.data.FloorCount == 1 ? "" : "s")} · {(l.data.IsBoard ? "board" : "walled")}" : l.data.kind.ToString().ToLowerInvariant();
                string warn = l.problems.Count > 0 ? "  <color=#e0a85a>!</color>" : "";
                if (GUILayout.Button($"{l.data.name}{warn}\n<color=#b8ae9c>{where} · {what}</color>", on ? cellSelected : cell, GUILayout.Height(40))) OpenLevel(l.data, -1);
            }
        }
        var shared = m.SharedBiomes;
        if (shared.Count > 0 && s.Length == 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("Shared biomes", gold);
            foreach (var b in shared)
            {
                var users = m.LevelsOf(b).Where(x => !x.tool).ToList();
                bool on = PickedBiome == b;
                if (GUILayout.Button($"{b.name}\n<color=#b8ae9c>{(users.Count == 0 ? "no level runs it" : "run by " + string.Join(", ", users.Select(u => u.data.name)))}</color>", on ? cellSelected : cell, GUILayout.Height(40))) OpenSharedBiome(b);
            }
        }
        GUILayout.Space(8);
        GUILayout.Label("Every level", gold);
        var defaults = AssetDatabase.LoadAssetAtPath<TableLevelDefaults>(AssetStudioWorld.DefaultsPath);
        if (GUILayout.Button("Shared defaults\n<color=#b8ae9c>chests, merchant, breakables, room events</color>", levelDefaults ? cellSelected : cell, GUILayout.Height(40)))
        { levelDefaults = true; levelPath = null; levelBiomePath = null; worldPane = WorldPane.Picked; ShowAsset(defaults); }
        GUILayout.EndScrollView();
        bool tools = GUILayout.Toggle(levelTools, " Test and debug levels too");
        if (tools != levelTools) levelTools = tools;
        GUILayout.EndArea();
    }

    // ── The page ──

    void DrawLevelPage(Rect r)
    {
        // A New form (New level, New biome stage, New room) is drawn where the page is.
        if (worldPane != WorldPane.Picked) { DrawWorldMiddle(ref r); return; }
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24));
        levelPageScroll = GUILayout.BeginScrollView(levelPageScroll);
        try
        {
            if (levelDefaults) DefaultsPane(AssetDatabase.LoadAssetAtPath<TableLevelDefaults>(AssetStudioWorld.DefaultsPath));
            else if (PickedBiome is DungeonBiome shared) { Title(shared.name, "A biome file: " + SharedLine(shared)); StageEditor(null, null, shared); }
            else if (World.LevelOf(PickedLevel) is AssetStudioWorld.Level l) LevelPage(l);
            else Title("Levels", "Pick a level on the left, or make one with + New level.");
        }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
    }

    string SharedLine(DungeonBiome b)
    {
        var users = World.LevelsOf(b).Where(x => !x.tool).ToList();
        return users.Count == 0 ? "no level runs it." : "run by " + string.Join(", ", users.Select(u => u.data.name)) + ": a change here shows in each.";
    }

    void LevelPage(AssetStudioWorld.Level l)
    {
        var d = l.data;
        // The header: what it is and how to play it.
        GUILayout.BeginHorizontal(StudioSkin.Inset);
        GUILayout.BeginVertical();
        GUILayout.Label(d.name, titleStyle);
        string where = l.places.Count > 0 ? string.Join(", ", l.places.Select(p => $"{p.displayName} on the map · act {p.act}")) : "Not on the map";
        string what = d.IsDungeon ? $"{(d.IsBoard ? "board" : "walled")} · {d.FloorCount} floor{(d.FloorCount == 1 ? "" : "s")} · {(d.layoutMode == DungeonLayoutMode.Authored ? (d.mapTemplate != null ? "map " + d.mapTemplate.name : "rooms in a row") : $"about {d.roomCount} rooms a floor")}" : d.kind.ToString().ToLowerInvariant();
        GUILayout.Label($"{where} · {what}", dim);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(GUILayout.Width(180));
        if (d.IsDungeon && GUILayout.Button(new GUIContent("Play floor 1", "Generate its first floor and play it."), GUILayout.Height(30))) Toast(GameLauncher.PlayLevel(d));
        var bossStage = l.RunStages.LastOrDefault(st => st.boss && AssetStudioWorld.HasGuardian(st.biome));
        if (bossStage != null && GUILayout.Button(new GUIContent("Play the boss", $"Straight to floor {bossStage.lastFloor}, where {bossStage.biome.guardian.boss.name} waits."), GUILayout.Height(30)))
            Toast(GameLauncher.PlayStage(d, bossStage.index, true));
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        foreach (var p in l.problems) GUILayout.Label("<color=#e0a85a>• " + p + "</color>", dim);
        if (!l.inCatalog && !l.tool && GUILayout.Button("Add it to the level catalog", GUILayout.Height(28))) { AssetStudioWorld.AddToCatalog(d); RefreshWorld(); }
        if (l.places.Count == 0 && !l.tool && GUILayout.Button("Put it on the campaign map", GUILayout.Height(28))) { StartForm(WorldPane.NewPlace); formLevel = d; formName = d.displayName ?? d.name; }
        GUILayout.Space(8);

        if (!d.IsDungeon) { GUILayout.Label("A town level: it has no floors to edit here. Its settings are in the inspector on the right.", dim); return; }
        levelTab = GUILayout.Toolbar(levelTab, LevelTabs, StudioSkin.Tab, GUILayout.Height(32));
        GUILayout.Space(8);
        switch (levelTab)
        {
            case 0: RunTab(l); break;
            case 1: LayoutSection(d); break;
            case 2: LookTab(l); break;
            case 3: BalanceTab(d); break;
            case 4: SoundTab(l); break;
            default: DialogueTab(d); break;
        }
    }

    // ── Run: the stages and the picked one ──

    void RunTab(AssetStudioWorld.Level l)
    {
        var d = l.data;
        GUILayout.BeginHorizontal();
        // The strip: one row per stage, top floor first.
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(230));
        GUILayout.Label("The run", gold);
        GUILayout.Space(4);
        var run = l.RunStages;
        if (run.Count == 0) GUILayout.Label("<color=#e0a85a>No stages and no base biome: its floors have nothing to run on.</color>", dim);
        foreach (var st in run)
        {
            string floors = st.lastFloor > st.firstFloor ? $"F{st.firstFloor}-{st.lastFloor}" : $"F{st.firstFloor}";
            string boss = st.boss && AssetStudioWorld.HasGuardian(st.biome) ? "  <color=#e24b4a>boss</color>" : "";
            bool on = st.index == levelStage || (st.IsBase && !l.Staged);
            if (GUILayout.Button($"<color=#b8ae9c>{floors}</color>  {(st.biome != null ? st.biome.name : "<color=#e0a85a>no biome</color>")}{boss}", on ? cellSelected : cell, GUILayout.Height(30)))
            { levelStage = st.index; ShowAsset(st.biome != null ? (UnityEngine.Object)st.biome : d); }
        }
        if (l.Staged && l.baseStage != null)
        {
            GUILayout.Space(6);
            bool on = levelStage == -1;
            if (GUILayout.Button("<color=#b8ae9c>every floor</color>  base biome", on ? cellSelected : cell, GUILayout.Height(30))) { levelStage = -1; ShowAsset(l.baseStage.biome); }
        }
        GUILayout.Space(6);
        if (GUILayout.Button("+ Add a stage", GUILayout.Height(28)))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("New biome stage..."), false, () => { StartForm(WorldPane.NewBiome); formLevel = d; });
            menu.AddSeparator("");
            foreach (var b in World.biomes) menu.AddItem(new GUIContent("Existing biome/" + b.name), false, () => { AssetStudioWorld.AddStage(d, b); RefreshWorld(); });
            menu.ShowAsContext();
        }
        if (!l.Staged && l.baseStage == null && d.baseBiome == null && GUILayout.Button("Make the base biome", GUILayout.Height(28))) { d.MakeBaseBiome(); AssetDatabase.SaveAssets(); RefreshLibrary(); GUIUtility.ExitGUI(); }
        GUILayout.EndVertical();

        GUILayout.Space(8);
        GUILayout.BeginVertical();
        var picked = run.FirstOrDefault(st => st.index == levelStage) ?? (levelStage == -1 ? l.baseStage : null) ?? run.FirstOrDefault();
        if (picked != null) StageEditor(l, picked, picked.biome);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    // What a stage's floors are made of. l and st are null for a biome file shown on its own.
    void StageEditor(AssetStudioWorld.Level l, AssetStudioWorld.Stage st, DungeonBiome b)
    {
        var d = l?.data;
        bool levelWideOnly = l != null && st != null && st.IsBase && l.Staged;
        // Its header: floors, boss, order, tests.
        GUILayout.BeginHorizontal(StudioSkin.Inset);
        string floors = st == null ? "" : st.IsBase && l.Staged ? "Every floor · " : st.lastFloor > st.firstFloor ? $"Floors {st.firstFloor}-{st.lastFloor} · " : $"Floor {st.firstFloor} · ";
        GUILayout.Label(floors + (b != null ? b.name : "no biome"), new GUIStyle(gold) { fontSize = StudioSkin.Size }, GUILayout.ExpandWidth(true));
        if (d != null && st != null && !st.IsBase && d.stages != null && st.index < d.stages.Length)
        {
            var stage = d.stages[st.index];
            GUILayout.Label("floors", dim, GUILayout.Width(52));
            int n = EditorGUILayout.DelayedIntField(stage.floors, GUILayout.Width(40));
            bool boss = GUILayout.Toggle(stage.boss, " boss", GUILayout.Width(64));
            if (n != stage.floors || boss != stage.boss) SetStage(d, st.index, n, boss);
            if (GUILayout.Button("▲", GUILayout.Width(30))) { AssetStudioWorld.MoveStage(d, st.index, -1); levelStage = Mathf.Max(0, st.index - 1); RefreshWorld(); }
            if (GUILayout.Button("▼", GUILayout.Width(30))) { AssetStudioWorld.MoveStage(d, st.index, 1); levelStage = Mathf.Min(d.stages.Length - 1, st.index + 1); RefreshWorld(); }
            if (GUILayout.Button("✕", GUILayout.Width(30)) && EditorUtility.DisplayDialog("Remove stage", $"Take {(b != null ? b.name : "this stage")} off {d.name}'s run? The biome itself stays.", "Remove", "Keep"))
            { RemoveStage(d, st.index); levelStage = -1; GUIUtility.ExitGUI(); }
        }
        else if (d != null && st != null && st.IsBase && !l.Staged)
        {
            GUILayout.Label("floors", dim, GUILayout.Width(52));
            int n = EditorGUILayout.DelayedIntField(d.floors, GUILayout.Width(40));
            bool boss = GUILayout.Toggle(d.bossOnLastFloor, " boss", GUILayout.Width(64));
            if (n != d.floors || boss != d.bossOnLastFloor) SetBaseRun(d, n, boss);
        }
        GUILayout.EndHorizontal();
        if (b == null) return;
        if (l != null && st != null && !levelWideOnly)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Play from here", $"A run of {d.name} that starts at floor {st.firstFloor}."), GUILayout.Height(28))) Toast(GameLauncher.PlayStage(d, st.index, false));
            if (st.boss && AssetStudioWorld.HasGuardian(b) && GUILayout.Button(new GUIContent("Fight its boss", $"Straight to floor {st.lastFloor}."), GUILayout.Height(28))) Toast(GameLauncher.PlayStage(d, st.index, true));
            GUILayout.EndHorizontal();
        }
        if (levelWideOnly)
        {
            GUILayout.Label("The base biome holds what every floor shares (chests, torches, the default style, the shop room); each stage adds its own enemies, rooms and look on top.", dim);
            StyleTiles(b, true);
            return;
        }
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.MinWidth(260));
        EnemyCards(b);
        GUILayout.EndVertical();
        GUILayout.Space(6);
        GUILayout.BeginVertical(StudioSkin.Inset, GUILayout.Width(250));
        BossAndLoot(b);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        RoomTiles(l, b);
        GUILayout.Space(6);
        StyleTiles(b, AssetStudioWorld.IsBase(b));
    }

    // The enemies, each with its picture, its weight as a bar (how common, against the others) and the biome floor it
    // first comes on.
    void EnemyCards(DungeonBiome b)
    {
        GUILayout.Label("Enemies", gold);
        var spawns = b.enemies ?? Array.Empty<DungeonBiome.Spawn>();
        float most = Mathf.Max(.01f, spawns.Where(x => x != null).Select(x => x.weight).DefaultIfEmpty(1).Max());
        for (int i = 0; i < spawns.Length; i++)
        {
            var s = spawns[i];
            if (s == null) continue;
            GUILayout.BeginHorizontal(GUILayout.Height(36));
            var icon = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
            GUI.DrawTexture(icon, StudioSkin.Slot);
            var pic = s.enemy != null ? AssetPreview.GetAssetPreview(s.enemy) ?? AssetPreview.GetMiniThumbnail(s.enemy) : null;
            if (pic != null) GUI.DrawTexture(new Rect(icon.x + 2, icon.y + 2, icon.width - 4, icon.height - 4), pic, ScaleMode.ScaleToFit);
            GUILayout.BeginVertical();
            if (GUILayout.Button(s.enemy != null ? s.enemy.name : "<color=#e0a85a>empty</color>", cell, GUILayout.Height(18)) && s.enemy != null) ShowAsset(s.enemy);
            var bar = GUILayoutUtility.GetRect(10, 6, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bar, new Color(.23f, .23f, .25f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(s.weight / most), bar.height), new Color(.72f, .565f, .24f));
            GUILayout.EndVertical();
            float w = EditorGUILayout.DelayedFloatField(s.weight, GUILayout.Width(40));
            GUILayout.Label("from", dim, GUILayout.Width(38));
            int from = EditorGUILayout.DelayedIntField(s.fromFloor, GUILayout.Width(28));
            if (GUILayout.Button("✕", GUILayout.Width(28))) { AssetStudioWorld.RemoveEnemy(b, i); RefreshWorld(); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            if (w != s.weight || from != s.fromFloor) { AssetStudioWorld.SetEnemy(b, i, w, from); RefreshWorld(); }
        }
        if (spawns.Length == 0) GUILayout.Label("<color=#e0a85a>Nothing lives here yet: its rooms stay empty.</color>", dim);
        var drop = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
        GUI.Box(drop, "Drop enemies here, or", new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter, normal = { background = StudioSkin.Slot }, border = new RectOffset(8, 8, 8, 8) });
        var ev = Event.current;
        if (drop.Contains(ev.mousePosition) && (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && DragAndDrop.objectReferences.Any(AssetStudioWorld.IsEnemy))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); foreach (var o in DragAndDrop.objectReferences) if (o is GameObject g) AssetStudioWorld.AddEnemy(b, g); RefreshWorld(); }
            ev.Use();
        }
        if (GUILayout.Button("+ Add an enemy", GUILayout.Height(28)))
        {
            var menu = new GenericMenu();
            foreach (var e in categories.Where(c => c.name == "Enemies").SelectMany(c => c.entries))
            {
                var go = e.Load() as GameObject;
                if (go == null || spawns.Any(x => x != null && x.enemy == go)) continue;
                menu.AddItem(new GUIContent(System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(e.path)) + "/" + e.name), false, () => { AssetStudioWorld.AddEnemy(b, go); RefreshWorld(); });
            }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Every enemy lives here already"));
            menu.ShowAsContext();
        }
        // How many come into a room, and how many rooms are fights.
        GUILayout.Space(4);
        using (var so = new SerializedObject(b))
        {
            so.Update();
            var min = so.FindProperty("minEnemiesPerRoom"); var max = so.FindProperty("maxEnemiesPerRoom");
            if (min != null && max != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("A room has", dim, GUILayout.Width(90));
                min.intValue = Mathf.Clamp(EditorGUILayout.IntField(min.intValue, GUILayout.Width(32)), 1, 6);
                GUILayout.Label("to", dim, GUILayout.Width(20));
                max.intValue = Mathf.Clamp(EditorGUILayout.IntField(max.intValue, GUILayout.Width(32)), min.intValue, 6);
                GUILayout.Label("enemies", dim);
                GUILayout.EndHorizontal();
            }
            if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(b);
        }
    }

    void BossAndLoot(DungeonBiome b)
    {
        GUILayout.Label("Boss", gold);
        var g = b.guardian;
        if (g != null && g.enabled && g.boss != null)
        {
            GUILayout.BeginHorizontal();
            var icon = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32), GUILayout.Height(32));
            GUI.DrawTexture(icon, StudioSkin.Slot);
            var pic = AssetPreview.GetAssetPreview(g.boss) ?? AssetPreview.GetMiniThumbnail(g.boss);
            if (pic != null) GUI.DrawTexture(new Rect(icon.x + 2, icon.y + 2, 28, 28), pic, ScaleMode.ScaleToFit);
            GUILayout.BeginVertical();
            if (GUILayout.Button(g.boss.name, cell, GUILayout.Height(18))) ShowAsset(g.boss);
            GUILayout.Label((g.arena != null ? "in " + g.arena.name : "in a generated room") + (g.arena != null && g.arena.GetComponentInChildren<DungeonStage>(true) != null ? ", a stage fight" : ""), dim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        else GUILayout.Label("None: its floors end at the stairs.", dim);
        GUILayout.Label("Its Guardian tab is in the inspector.", dim);
        GUILayout.Space(8);
        GUILayout.Label("Loot", gold);
        var loot = (LootSource)EditorGUILayout.ObjectField(b.loot, typeof(LootSource), false);
        if (loot != b.loot) { Undo.RecordObject(b, "Loot"); b.loot = loot; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
        GUILayout.BeginHorizontal();
        GUILayout.Label("Merchant", dim, GUILayout.Width(80));
        float merchant = EditorGUILayout.Slider(b.merchantChance, 0, 1);
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(merchant, b.merchantChance)) { Undo.RecordObject(b, "Merchant chance"); b.merchantChance = merchant; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
    }

    // The hand-built rooms its floors are made of, as pictures: click one to see it, right-click for more.
    void RoomTiles(AssetStudioWorld.Level l, DungeonBiome b)
    {
        GUILayout.BeginVertical(StudioSkin.Inset);
        var pool = b.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>();
        GUILayout.Label($"Rooms it can build  <color=#b8ae9c>{pool.Count(c => c != null && c.template != null)}</color>", gold);
        const float w = 112, h = 92;
        // The page's width less the run strip and the frames round it.
        float width = Mathf.Max(w, position.width - CategoryWidth - ListWidth - InspectorWidth - Pad * 5 - (l != null ? 300 : 60));
        int perRow = Mathf.Max(1, Mathf.FloorToInt(width / (w + 6)));
        int count = pool.Length + 1;
        for (int row = 0; row * perRow < count; row++)
        {
            GUILayout.BeginHorizontal();
            for (int k = row * perRow; k < Mathf.Min(count, (row + 1) * perRow); k++)
            {
                var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
                GUI.DrawTexture(r, StudioSkin.Slot);
                if (k == pool.Length)
                {
                    GUI.Label(r, "+ Add a room\nor drop one here", new GUIStyle(dim) { alignment = TextAnchor.MiddleCenter });
                    var ev = Event.current;
                    if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition)) { RoomMenu(l, b, pool); ev.Use(); }
                    if (r.Contains(ev.mousePosition) && (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && DragAndDrop.objectReferences.Any(o => o is GameObject g && g.GetComponent<DungeonRoomTemplate>() != null))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                        if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); foreach (var o in DragAndDrop.objectReferences) if (o is GameObject g) AssetStudioWorld.AddToPool(b, g); RefreshWorld(); }
                        ev.Use();
                    }
                    continue;
                }
                var c = pool[k];
                if (c == null) continue;
                var pic = c.template != null ? AssetPreview.GetAssetPreview(c.template) : null;
                if (pic != null) GUI.DrawTexture(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 30), pic, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x + 6, r.yMax - 22, r.width - 12, 18), c.template != null ? c.template.name : "<color=#e0a85a>empty</color>", new GUIStyle(dimLine) { alignment = TextAnchor.MiddleCenter, normal = { textColor = StudioSkin.Text } });
                if (c.weight != 1) GUI.Label(new Rect(r.xMax - 40, r.y + 4, 34, 18), "x" + c.weight.ToString("0.#"), new GUIStyle(dimLine) { alignment = TextAnchor.UpperRight });
                var e2 = Event.current;
                if (e2.type == EventType.MouseDown && r.Contains(e2.mousePosition))
                {
                    int index = k;
                    if (e2.button == 1)
                    {
                        var menu = new GenericMenu();
                        if (c.template != null)
                        {
                            menu.AddItem(new GUIContent("Open to build"), false, () => AssetDatabase.OpenAsset(c.template));
                            menu.AddItem(new GUIContent("Play from this room"), false, () => Toast(GameLauncher.PlayRoom(c.template)));
                        }
                        foreach (var weight in new[] { .5f, 1f, 2f, 3f })
                            menu.AddItem(new GUIContent($"How common/x{weight:0.#}"), Mathf.Approximately(c.weight, weight), () => { Undo.RecordObject(b, "Pool weight"); c.weight = weight; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); });
                        menu.AddSeparator("");
                        menu.AddItem(new GUIContent("Take it out of the pool"), false, () => { AssetStudioWorld.RemoveFromPool(b, index); RefreshWorld(); });
                        menu.ShowAsContext();
                    }
                    else if (c.template != null) ShowAsset(c.template);
                    e2.Use();
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    void RoomMenu(AssetStudioWorld.Level l, DungeonBiome b, DungeonBiome.RoomTemplateChoice[] pool)
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("New room in this pool..."), false, () => { StartForm(WorldPane.NewRoom); formLevel = l?.data; formBiome = b; });
        menu.AddSeparator("");
        foreach (var room in World.rooms.Where(r => !pool.Any(c => c != null && c.template == r)))
        {
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(room)).Replace('\\', '/').Replace(AssetStudioWorld.LevelsRoot + "/", "");
            menu.AddItem(new GUIContent(folder.Replace('/', ' ') + "/" + room.name), false, () => { AssetStudioWorld.AddToPool(b, room); RefreshWorld(); });
        }
        menu.ShowAsContext();
    }

    // The room styles, each as a corner of a room built from it; the base biome's default style first.
    void StyleTiles(DungeonBiome b, bool withDefault)
    {
        GUILayout.BeginVertical(StudioSkin.Inset);
        GUILayout.Label("Look", gold);
        var tiles = new List<(DungeonRoomStyle style, string note, int index)>();
        if (withDefault && b.defaultStyle != null) tiles.Add((b.defaultStyle, "default", -1));
        var choices = b.roomStyles ?? Array.Empty<DungeonStyleChoice>();
        for (int i = 0; i < choices.Length; i++) if (choices[i] != null && choices[i].style != null) tiles.Add((choices[i].style, "rooms x" + choices[i].weight.ToString("0.#"), i));
        const float w = 168, h = 120;
        GUILayout.BeginHorizontal();
        foreach (var (style, note, index) in tiles)
        {
            var r = GUILayoutUtility.GetRect(w, h, GUILayout.Width(w), GUILayout.Height(h));
            GUI.DrawTexture(r, StudioSkin.Slot);
            var thumb = StyleThumb(style);
            if (thumb != null) GUI.DrawTexture(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 32), thumb, ScaleMode.ScaleAndCrop);
            GUI.Label(new Rect(r.x + 6, r.yMax - 24, r.width - 12, 18), $"{style.name}  <color=#b8ae9c>{note}</color>", new GUIStyle(dimLine) { richText = true, normal = { textColor = StudioSkin.Text } });
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
            {
                if (ev.button == 1 && index >= 0)
                {
                    var menu = new GenericMenu();
                    int at = index;
                    menu.AddItem(new GUIContent("Take this style out"), false, () => { Undo.RecordObject(b, "Room style"); b.roomStyles = b.roomStyles.Where((_, j) => j != at).ToArray(); EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); });
                    menu.ShowAsContext();
                }
                else ShowAsset(style);
                ev.Use();
            }
        }
        if (GUILayout.Button("+ Add a\nroom style", GUILayout.Width(110), GUILayout.Height(h)))
        {
            var menu = new GenericMenu();
            foreach (var g in AssetDatabase.FindAssets("t:DungeonRoomStyle", new[] { "Assets/Game" }))
            {
                var style = AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(AssetDatabase.GUIDToAssetPath(g));
                if (style == null || choices.Any(c => c != null && c.style == style)) continue;
                menu.AddItem(new GUIContent(style.name), false, () => { Undo.RecordObject(b, "Room style"); b.roomStyles = (b.roomStyles ?? Array.Empty<DungeonStyleChoice>()).Append(new DungeonStyleChoice { style = style, weight = 1 }).ToArray(); EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); });
            }
            menu.ShowAsContext();
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
        if (tiles.Count == 0) GUILayout.Label("No room styles of its own: its rooms take the level's default style.", dim);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Lighting", dim, GUILayout.Width(80));
        var lighting = (DungeonBiome.Lighting)EditorGUILayout.EnumPopup(b.lighting, GUILayout.Width(180));
        GUILayout.Space(12);
        GUILayout.Label("Filmic look", dim, GUILayout.Width(96));
        float filmic = EditorGUILayout.Slider(b.filmicLook, 0, 1);
        GUILayout.EndHorizontal();
        if (lighting != b.lighting || !Mathf.Approximately(filmic, b.filmicLook)) { Undo.RecordObject(b, "Biome look"); b.lighting = lighting; b.filmicLook = filmic; EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
        GUILayout.EndVertical();
    }

    // A style's sample room (AssetStudioStyles), rendered once and kept until the library is refreshed.
    Texture2D StyleThumb(DungeonRoomStyle style)
    {
        if (style == null) return null;
        if (styleThumbs.TryGetValue(style, out var t) && t != null) return t;
        if (Event.current.type != EventType.Repaint) return null;
        var p = new AssetStudioPreview();
        try
        {
            p.SetTarget(style); p.Frame(); p.TestView(35f, 24f);
            var png = p.CapturePng();
            if (png == null) return null;
            t = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            t.LoadImage(png);
            styleThumbs[style] = t;
            return t;
        }
        finally { p.Dispose(); }
    }

    // ── The other tabs ──

    void LookTab(AssetStudioWorld.Level l)
    {
        var d = l.data;
        Section("THE ROOM WHILE IT IS PLAYED");
        GUILayout.Label("How the bedroom looks while this level is on the table. A walled floor's own light is its biome's (Run, Look).", dim);
        Fields(d, "roomLighting", "roomBrightness", "roomTint");
        if (d.IsBoard && d.baseBiome != null) { Section("BOARD LIGHT"); Fields(d.baseBiome, "boardLightColor", "boardLightIntensity"); }
        if (d.baseBiome != null) { Section("EVERY FLOOR"); StyleTiles(d.baseBiome, true); }
    }

    void BalanceTab(TableLevelData d)
    {
        Section("HOW HARD IT IS");
        Fields(d, "balance");
        if (d.balance != null)
        {
            GUILayout.Label($"{d.balance.name}, shared by every level that uses it:", dim);
            AllFields(d.balance);
        }
        Section("GEAR BY FLOOR");
        Fields(d, "gearByFloor", "startingChest");
    }

    void SoundTab(AssetStudioWorld.Level l)
    {
        var d = l.data;
        Section("THE LEVEL'S MUSIC");
        GUILayout.Label("Plays on every floor whose biome has no music of its own. Empty: no music.", dim);
        Fields(d, "backgroundMusic", "backgroundMusicVolume");
        foreach (var st in l.RunStages.Where(x => x.biome != null))
        {
            Section((st.IsBase ? "Every floor" : st.lastFloor > st.firstFloor ? $"Floors {st.firstFloor}-{st.lastFloor}" : $"Floor {st.firstFloor}") + " · " + st.biome.name);
            Fields(st.biome, "music", "musicVolume", "ambience");
        }
    }

    void DialogueTab(TableLevelData d)
    {
        Section("WHAT THE DUNGEON MASTER SAYS HERE");
        GUILayout.Label("Lines picked at random when something happens in a run of this level (a kill, a backstab, a new floor). Every line in the game is also in the Dialogue view.", dim);
        if (GUILayout.Button("Open the Dialogue view", GUILayout.Height(28), GUILayout.Width(240))) OpenDialogue();
        using var so = new SerializedObject(d);
        so.Update();
        var list = so.FindProperty("dialogue");
        for (int i = 0; list != null && i < list.arraySize; i++)
        {
            var e = list.GetArrayElementAtIndex(i);
            var when = e.FindPropertyRelative("trigger");
            GUILayout.BeginVertical(StudioSkin.Inset);
            GUILayout.BeginHorizontal();
            GUILayout.Label("When", dim, GUILayout.Width(50));
            EditorGUILayout.PropertyField(when, GUIContent.none, GUILayout.Width(200));
            GUILayout.Label("chance", dim, GUILayout.Width(60));
            var chance = e.FindPropertyRelative("chance");
            chance.floatValue = EditorGUILayout.Slider(chance.floatValue, 0, 1);
            var once = e.FindPropertyRelative("oncePerRun");
            once.boolValue = GUILayout.Toggle(once.boolValue, " once a run", GUILayout.Width(110));
            if (GUILayout.Button("✕", GUILayout.Width(28))) { list.DeleteArrayElementAtIndex(i); break; }
            GUILayout.EndHorizontal();
            var lines = e.FindPropertyRelative("lines");
            for (int j = 0; j < lines.arraySize; j++)
            {
                GUILayout.BeginHorizontal();
                var line = lines.GetArrayElementAtIndex(j);
                line.stringValue = EditorGUILayout.TextField(line.stringValue);
                if (GUILayout.Button("✕", GUILayout.Width(28))) { lines.DeleteArrayElementAtIndex(j); GUILayout.EndHorizontal(); break; }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("+ Another line", GUILayout.Width(150), GUILayout.Height(24))) { lines.arraySize++; lines.GetArrayElementAtIndex(lines.arraySize - 1).stringValue = ""; }
            GUILayout.EndVertical();
        }
        if (list != null && GUILayout.Button("+ New moment he speaks", GUILayout.Height(28), GUILayout.Width(240))) list.arraySize++;
        if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(d);
    }

    // Unity's own fields for these properties of an asset, written back and saved at once (Ctrl+Z undoes them).
    static void Fields(UnityEngine.Object target, params string[] names)
    {
        if (target == null) return;
        using var so = new SerializedObject(target);
        so.Update();
        foreach (var n in names) { var p = so.FindProperty(n); if (p != null) EditorGUILayout.PropertyField(p, true); }
        if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(target);
    }

    static void AllFields(UnityEngine.Object target)
    {
        using var so = new SerializedObject(target);
        so.Update();
        var it = so.GetIterator();
        for (bool enter = true; it.NextVisible(enter); enter = false)
            if (it.name != "m_Script") EditorGUILayout.PropertyField(it, true);
        if (so.ApplyModifiedProperties()) AssetDatabase.SaveAssetIfDirty(target);
    }
}

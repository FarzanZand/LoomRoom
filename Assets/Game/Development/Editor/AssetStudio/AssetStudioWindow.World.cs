using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio's World view (AssetStudioWorld): the campaign and the levels as one tree, from acts and their places and
// events down to each level's stages, their biomes and the rooms in each biome's pool. Levels are grouped by the act of
// the place that plays them (board or walled is a tag on the row); a level without stages shows its base biome as its
// one stage, a level with stages its base biome (level-wide settings) after them; biomes several levels run are under
// Shared biomes. + New level opens the guided New level form (AssetStudioWindow.NewLevel.cs). Left of the preview, the tree with
// a search and a Show filter; in the middle, what the picked thing is made of, with buttons to change it and New buttons
// that make things already filed and linked; right, its inspector as everywhere else in the studio. Rooms can be dragged
// from the tree (or the Project window) onto a biome to put them in its pool, enemy prefabs to have them live there, and
// a level onto a place to have it play it.
// Map templates (DungeonMapTemplate: a whole small map in one prefab) show under the levels that play them and in MAPS.
public partial class AssetStudioWindow
{
    // Group: a group of levels (an act, Not on the map, Other); Action: a row that opens a form (New level).
    enum WorldKind { Header, Act, Place, Event, Level, Stage, Room, Note, Map, Defaults, Group, Action }
    enum WorldPane { Picked, NewLevel, NewBiome, NewRoom, NewPlace, NewEvent, Tidy, NewMap }

    class WorldNode
    {
        public string key, label, note;
        public string tag;              // a small word at the row's right end (board, walled, base)
        public bool poolUsed = true;    // a stage row: rooms and enemies dropped on it play (not on a staged level's base biome)
        public bool warn, open, hasChildren;
        public int depth, act, stage = -1;
        public WorldKind kind;
        public UnityEngine.Object asset;
        public TableLevelData level;
        public DungeonBiome biome;
    }

    AssetStudioWorld.Model world;
    readonly HashSet<string> worldOpen = new() { "campaign", "levels", "act1", "lgroup:Act 1" };
    readonly List<WorldNode> worldRows = new();
    WorldNode worldPicked;
    WorldPane worldPane;
    string worldSearch = "";
    int worldShow;    // 0 everything, 1 needs attention, 2 rooms nobody uses, 3 test levels too
    static readonly string[] WorldShowNames = { "Everything", "Needs attention", "Rooms nobody uses", "With test levels" };
    Vector2 worldScroll, worldPaneScroll;
    const string WorldOpenKey = "LoomRoom.Studio.WorldOpen";

    // The New forms' fields.
    string formName = "";
    int formAct = 1, formFloors = 2, formRooms = 3, formLayout;    // formLayout 0: like the template, 1: rooms in a row
    bool formOwnBiomes = true, formBoss, formUseMap;
    // The painted layout's room count, for the level it was set for (not fed from the level every frame).
    TableLevelData layoutRoomsFor;
    int layoutRooms = 3;
    TableLevelData formLevel;
    DungeonBiome formBiome;
    List<AssetStudioWorld.Move> tidyMoves;
    List<string> tidyKept;
    // The last Tidy's moves, from and to, for Move back (kept through a recompile), and what it said.
    [SerializeField] List<string> tidyFrom = new(), tidyTo = new();
    [SerializeField] string tidyResult;
    string worldRepick;   // a node key to pick again once the tree is built (after a recompile)

    AssetStudioWorld.Model World => world ??= AssetStudioWorld.Build();

    void OpenWorld()
    {
        studioView = StudioView.World;
        var saved = EditorPrefs.GetString(WorldOpenKey, "");
        if (saved.Length > 0) { worldOpen.Clear(); foreach (var k in saved.Split('|')) if (k.Length > 0) worldOpen.Add(k); }
        // Saved before levels were grouped by act: the first act's levels open, as they were before.
        if (!worldOpen.Any(k => k.StartsWith("lgroup:"))) worldOpen.Add("lgroup:Act 1");
        GUI.FocusControl(null);
    }

    void ToggleOpen(string key)
    {
        if (!worldOpen.Remove(key)) worldOpen.Add(key);
        EditorPrefs.SetString(WorldOpenKey, string.Join("|", worldOpen));
    }

    // After a change: the World model is read again on the next draw (a few asset reads). After files were made, moved
    // or deleted, the library sorts in just those (the watcher noted them; a moved folder builds it all again) and the
    // World model is read again with it (RefreshLibrary). Refresh library still builds everything.
    void RefreshWorld() { world = null; Repaint(); }
    void RefreshLibrary()
    {
        ApplyLibraryChanges();
        world = null; tomes = null;
        AssetStudioUsage.Forget();
        usedBy = null; usedByFor = null;
        Repaint();
    }

    // ── The tree ─────────────────────────────────────────────────────

    void BuildRows()
    {
        worldRows.Clear();
        var m = World;
        string s = worldSearch.Trim().ToLowerInvariant();
        bool searching = s.Length > 0;
        void Row(WorldNode n)
        {
            if (searching && !(n.label + " " + n.note).ToLowerInvariant().Contains(s) && n.kind != WorldKind.Header) return;
            n.open = worldOpen.Contains(n.key);
            if (searching) { n.depth = 0; n.hasChildren = false; }
            worldRows.Add(n);
        }
        bool Open(string key) => searching || worldOpen.Contains(key);

        if (worldShow == 2)
        {
            Row(new WorldNode { key = "unused", label = "ROOMS NOBODY USES", kind = WorldKind.Header });
            foreach (var r in m.rooms.Where(r => m.UsesOf(r).Count == 0)) Row(RoomNode(m, r, 1, null, null, ""));
            return;
        }

        // The campaign: acts, their places and events.
        Row(new WorldNode { key = "campaign", label = "CAMPAIGN", kind = WorldKind.Header, hasChildren = true });
        if (Open("campaign"))
        {
            if (m.campaignProblems.Count > 0) Row(new WorldNode { key = "campaign-problems", label = m.campaignProblems[0], kind = WorldKind.Note, depth = 1, warn = true });
            for (int act = 1; act <= m.Acts; act++)
            {
                var places = m.places.Where(p => p.act == act).ToList();
                var events = m.events.Where(e => e.afterAct == act).ToList();
                var placeWarn = places.Where(p => PlaceProblem(m, p) != null).ToList();
                if (worldShow == 1 && placeWarn.Count == 0) continue;
                Row(new WorldNode { key = "act" + act, label = "Act " + act, note = $"{places.Count} place{(places.Count == 1 ? "" : "s")}" + (events.Count > 0 ? $", {events.Count} event{(events.Count == 1 ? "" : "s")}" : ""), kind = WorldKind.Act, act = act, depth = 1, hasChildren = places.Count + events.Count > 0, warn = placeWarn.Count > 0 });
                if (!Open("act" + act)) continue;
                foreach (var p in places)
                {
                    string problem = PlaceProblem(m, p);
                    if (worldShow == 1 && problem == null) continue;
                    string note = p.kind == CampaignEncounterKind.Home ? "home" : p.kind == CampaignEncounterKind.Placeholder ? "not made yet" : p.level != null ? "plays " + p.level.name : "no level";
                    if (p.Locked) note = "shut: " + ShutText(p) + " · " + note;
                    Row(new WorldNode { key = "place:" + p.GetHashCode(), label = p.displayName, note = problem ?? note, warn = problem != null, kind = WorldKind.Place, asset = p, act = act, level = p.level, depth = 2 });
                }
                if (worldShow != 1) foreach (var e in events)
                    Row(new WorldNode { key = "event:" + e.GetHashCode(), label = e.title, note = "event" + (m.listedEvents.Contains(e) ? "" : " · not in the campaign manager"), warn = !m.listedEvents.Contains(e), kind = WorldKind.Event, asset = e, act = act, depth = 2 });
            }
        }

        // The levels, by the act of the place that plays them; under each its own rooms, its map, its run (stages, or
        // its base biome) and, under stages, its base biome's level-wide settings. Biomes several levels run (or none)
        // come after, under SHARED BIOMES.
        Row(new WorldNode { key = "levels", label = "LEVELS", kind = WorldKind.Header, hasChildren = true });
        var defaults = AssetDatabase.LoadAssetAtPath<TableLevelDefaults>(AssetStudioWorld.DefaultsPath);
        if (Open("levels") && worldShow != 1 && !searching)
        {
            Row(new WorldNode { key = "newlevel", label = "+ New level", note = "a one-floor encounter or a generated dungeon, step by step", kind = WorldKind.Action, depth = 1 });
            Row(new WorldNode { key = "defaults", label = "Shared defaults", note = defaults != null ? "chests, merchant, breakables, features, wall lights, room events, HUD: every level" : "missing: " + AssetStudioWorld.DefaultsPath, warn = defaults == null, kind = WorldKind.Defaults, asset = defaults, depth = 1 });
        }
        if (Open("levels"))
            foreach (var (group, levels) in m.Groups(worldShow == 3))
            {
                var inGroup = worldShow == 1 ? levels.Where(l => l.problems.Count > 0).ToList() : levels;
                if (inGroup.Count == 0) continue;
                string groupKey = "lgroup:" + group;
                int warned = inGroup.Count(l => l.problems.Count > 0);
                Row(new WorldNode { key = groupKey, label = group, note = $"{inGroup.Count} level{(inGroup.Count == 1 ? "" : "s")}" + (warned > 0 ? $" · {warned} to look at" : ""), warn = warned > 0, kind = WorldKind.Group, act = group.StartsWith("Act ") && int.TryParse(group.Substring(4), out int ga) ? ga : 0, depth = 1, hasChildren = true });
                if (!Open(groupKey)) continue;
                foreach (var l in inGroup) LevelRows(m, l, 2, Row, Open);
            }

        if (Open("levels") && worldShow != 1)
        {
            var shared = m.SharedBiomes;
            if (shared.Count > 0)
            {
                Row(new WorldNode { key = "sharedbiomes", label = "Shared biomes", note = "run by several levels, or by none", kind = WorldKind.Group, depth = 1, hasChildren = true });
                if (Open("sharedbiomes"))
                    foreach (var b in shared)
                    {
                        var users = m.LevelsOf(b).Where(x => !x.tool).ToList();
                        int pool = b.roomTemplates != null ? b.roomTemplates.Count(c => c != null && c.template != null) : 0;
                        Row(new WorldNode { key = "biome:" + AssetDatabase.GetAssetPath(b), label = b.name, note = (users.Count == 0 ? "no level runs it" : "run by " + string.Join(", ", users.Select(u => u.data.name))) + $" · {pool} room{(pool == 1 ? "" : "s")}", kind = WorldKind.Stage, asset = b, biome = b, stage = -2, depth = 2 });
                    }
            }
        }

        if (worldShow == 0)
        {
            Row(new WorldNode { key = "maps", label = "MAPS", note = m.maps.Count.ToString(), kind = WorldKind.Header, hasChildren = m.maps.Count > 0 });
            if (Open("maps"))
                foreach (var map in m.maps)
                {
                    var users = m.MapLevels(map);
                    string mapKey = "map::" + AssetDatabase.GetAssetPath(map);
                    var rooms = map.Rooms();
                    Row(new WorldNode { key = mapKey, label = map.name, note = $"{rooms.Length} room{(rooms.Length == 1 ? "" : "s")} · " + (users.Count == 0 ? "no level plays it" : "played by " + string.Join(", ", users.Select(u => u.name))), kind = WorldKind.Map, asset = map.gameObject, level = users.FirstOrDefault(), depth = 1, hasChildren = rooms.Length > 0 });
                    if (Open(mapKey)) for (int i = 0; i < rooms.Length; i++) Row(MapRoomNode(map, rooms[i], i, 2, mapKey));
                }
            int unused = m.rooms.Count(r => m.UsesOf(r).Count == 0);
            Row(new WorldNode { key = "unused", label = "ROOMS NOBODY USES", note = unused.ToString(), kind = WorldKind.Header, hasChildren = unused > 0 });
            if (Open("unused")) foreach (var r in m.rooms.Where(r => m.UsesOf(r).Count == 0)) Row(RoomNode(m, r, 1, null, null, ""));
        }
    }

    // One level's rows: the level, its own rooms, its map, its run (its stages, or its base biome alone) and, under
    // stages, its base biome with the level-wide settings.
    void LevelRows(AssetStudioWorld.Model m, AssetStudioWorld.Level l, int depth, Action<WorldNode> row, Func<string, bool> open)
    {
        var d = l.data;
        bool severalActs = l.places.Select(p => p.act).Distinct().Count() > 1;
        string onMap = l.places.Count > 0 ? "on the map: " + string.Join(", ", l.places.Select(p => (severalActs ? $"Act {p.act} " : "") + p.displayName))
            : l.tool ? "test level" : !l.inCatalog ? "not in the level catalog" : "not on the map";
        string tag = d.kind == TableLevelKind.Town ? "town" : d.IsBoard ? "board" : "walled";
        row(new WorldNode { key = "level:" + l.path, label = d.name, tag = tag, note = (d.IsDungeon ? $"{d.FloorCount} floor{(d.FloorCount == 1 ? "" : "s")} · " : "") + (l.problems.Count > 0 ? l.problems.Count + " to look at · " : "") + onMap, warn = l.problems.Count > 0, kind = WorldKind.Level, asset = d, level = d, act = l.act, depth = depth, hasChildren = true });
        if (!open("level:" + l.path)) return;
        // The level's own rooms, and the map it plays with the rooms painted on it.
        foreach (var (room, role) in OwnRooms(d))
            row(RoomNode(m, room, depth + 1, d, null, role));
        if (d.mapTemplate != null)
        {
            var map = d.mapTemplate;
            string mapKey = $"map:{l.path}:{AssetDatabase.GetAssetPath(map)}";
            var rooms = map.Rooms();
            var problems = map.Problems();
            row(new WorldNode { key = mapKey, label = map.name, note = $"map · {rooms.Length} room{(rooms.Length == 1 ? "" : "s")}" + (problems.Count > 0 ? " · " + problems[0] : ""), warn = problems.Count > 0, kind = WorldKind.Map, asset = map.gameObject, level = d, depth = depth + 1, hasChildren = rooms.Length > 0 });
            if (open(mapKey)) for (int i = 0; i < rooms.Length; i++) row(MapRoomNode(map, rooms[i], i, depth + 2, mapKey));
        }
        foreach (var st in l.RunStages) StageRows(m, l, st, depth + 1, row, open);
        if (l.Staged && l.baseStage != null) StageRows(m, l, l.baseStage, depth + 1, row, open);
    }

    // A stage of a level's run and the rooms in its pool. A staged level's base biome only holds level-wide settings:
    // its own pool, enemies and loot don't play, so it shows no rooms and takes none.
    void StageRows(AssetStudioWorld.Model m, AssetStudioWorld.Level l, AssetStudioWorld.Stage st, int depth, Action<WorldNode> row, Func<string, bool> open)
    {
        var b = st.biome;
        bool runs = !st.IsBase || !l.Staged;
        int pool = runs && b != null && b.roomTemplates != null ? b.roomTemplates.Count(c => c != null && c.template != null) : 0;
        bool arena = runs && b != null && b.guardian != null && b.guardian.enabled && b.guardian.arena != null;
        string key = st.IsBase ? "base:" + l.path : $"stage:{l.path}:{st.index}";
        string floors = $"Floor{(st.lastFloor > st.firstFloor ? "s" : "")} {st.firstFloor}{(st.lastFloor > st.firstFloor ? "-" + st.lastFloor : "")}";
        string label = runs ? floors + " · " + (b != null ? b.name : "no biome") : "Base biome";
        string note = runs
            ? $"{pool} room{(pool == 1 ? "" : "s")}" + (st.boss && AssetStudioWorld.HasGuardian(b) ? " · boss" : "") + (!st.IsBase && b != null && m.LevelsOf(b).Count(x => !x.tool) > 1 ? " · shared biome" : "")
            : "level-wide: style, torches, chests, shop, room features";
        row(new WorldNode { key = key, label = label, tag = st.IsBase ? "base" : null, note = note, warn = b == null, kind = WorldKind.Stage, asset = b, level = l.data, biome = b, stage = st.index, depth = depth, hasChildren = pool > 0 || arena, poolUsed = runs });
        if (!runs || !open(key) || b == null) return;
        if (b.roomTemplates != null) foreach (var c in b.roomTemplates) if (c != null && c.template != null) row(RoomNode(m, c.template, depth + 1, l.data, b, $"weight {c.weight:0.##}"));
        if (arena) row(RoomNode(m, b.guardian.arena, depth + 1, l.data, b, "boss arena"));
    }

    static IEnumerable<(GameObject room, string role)> OwnRooms(TableLevelData l)
    {
        if (l.startingRoom != null) yield return (l.startingRoom, "starting room");
        if (l.shopRoom != null) yield return (l.shopRoom, "shop");
        if (l.layoutMode == DungeonLayoutMode.Authored && l.mapTemplate == null && l.authoredRooms != null)
            for (int i = 0; i < l.authoredRooms.Length; i++)
                if (l.authoredRooms[i]?.template != null) yield return (l.authoredRooms[i].template, $"room {i}" + (l.authoredRooms[i].merchant ? " · merchant" : ""));
    }

    WorldNode RoomNode(AssetStudioWorld.Model m, GameObject room, int depth, TableLevelData level, DungeonBiome biome, string role)
    {
        var levels = m.LevelsOf(room);
        string where = levels.Count == 0 ? "nobody uses it" : levels.Count == 1 ? "only " + levels[0].name : $"in {levels.Count} levels";
        return new WorldNode { key = $"room:{room.GetHashCode()}:{level?.GetHashCode()}:{biome?.GetHashCode()}", label = room.name, note = (role.Length > 0 ? role + " · " : "") + where, kind = WorldKind.Room, asset = room, level = level, biome = biome, depth = depth };
    }

    // One room painted on a map: its child's name, kind and whether it is hand-built (picking it shows the map).
    static WorldNode MapRoomNode(DungeonMapTemplate map, TableLevelData.AuthoredRoom room, int i, int depth, string mapKey) => new()
    {
        key = mapKey + ":" + i, label = room.label, kind = WorldKind.Note, asset = map.gameObject, depth = depth,
        note = $"room {DungeonMapTemplate.RoomMark(i)} · {room.role.ToString().ToLowerInvariant()}" + (room.merchant ? " · merchant" : "") + (room.template == null ? " · furnished by the generator" : ""),
    };

    static string PlaceProblem(AssetStudioWorld.Model m, CampaignEncounter p)
    {
        if (!m.placesOnMap.Contains(p)) return "not on the map";
        if (!m.listedPlaces.Contains(p)) return "not in the campaign manager's list";
        if (p.kind == CampaignEncounterKind.Level && p.level == null) return "plays no level";
        if (string.IsNullOrEmpty(p.id)) return "no id";
        // Shut until deaths somewhere (CampaignEncounter.deathsToOpen): that place must be one the player can die in.
        if (p.deathsToOpen > 0)
        {
            if (p.deathsIn == null) return "shut until deaths, but Deaths In is empty: it opens with its act";
            if (p.deathsIn == p) return "shut until deaths in itself: it can never open";
            if (!m.placesOnMap.Contains(p.deathsIn)) return $"shut until deaths in {p.deathsIn.displayName}, which is not on the map";
            if (!m.listedPlaces.Contains(p.deathsIn)) return $"shut until deaths in {p.deathsIn.displayName}, which the campaign manager doesn't list";
            if (string.IsNullOrEmpty(p.deathsIn.id)) return $"shut until deaths in {p.deathsIn.displayName}, which has no id";
        }
        return null;
    }

    static string ShutText(CampaignEncounter p) => $"opens after {p.deathsToOpen} death{(p.deathsToOpen == 1 ? "" : "s")} in {(p.deathsIn != null ? p.deathsIn.displayName : "?")}";

    void DrawWorldTree(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        float y = r.y + 6;
        GUI.SetNextControlName("worldSearch");
        string s = EditorGUI.TextField(new Rect(r.x + 6, y, r.width - 12, 20), worldSearch, EditorStyles.toolbarSearchField);
        if (s != worldSearch) { worldSearch = s; worldScroll = Vector2.zero; }
        y += 24;
        int show = EditorGUI.Popup(new Rect(r.x + 6, y, r.width - 12, 20), "Show", worldShow, WorldShowNames);
        if (show != worldShow) { worldShow = show; worldScroll = Vector2.zero; }
        y += 24;
        float bw = (r.width - 16) / 2;
        if (GUI.Button(new Rect(r.x + 6, y, bw, 24), new GUIContent("New...", "A level, a biome, a room, a place on the map or an event: made in its folder and linked where it belongs.")))
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Level"), false, () => StartForm(WorldPane.NewLevel));
            menu.AddItem(new GUIContent("Biome (a stage of a level)"), false, () => StartForm(WorldPane.NewBiome));
            menu.AddItem(new GUIContent("Room"), false, () => StartForm(WorldPane.NewRoom));
            menu.AddItem(new GUIContent("Map template (a small map of rooms in one prefab)"), false, () => StartForm(WorldPane.NewMap));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Place on the campaign map"), false, () => StartForm(WorldPane.NewPlace));
            menu.AddItem(new GUIContent("Campaign event"), false, () => StartForm(WorldPane.NewEvent));
            menu.ShowAsContext();
        }
        if (GUI.Button(new Rect(r.x + 10 + bw, y, bw, 24), new GUIContent("Tidy folders...", "Files follow the World: each level's own rooms and biomes in its folder, shared ones in Levels/Shared. Shows the moves first.")))
        { tidyMoves = AssetStudioWorld.TidyPlan(World, out tidyKept); worldPane = WorldPane.Tidy; }
        y += 30;

        if (Event.current.type == EventType.Layout || worldRows.Count == 0) BuildRows();
        // After a recompile: the node picked before, found again by its key.
        if (worldRepick != null && Event.current.type == EventType.Layout)
        {
            var again = worldRows.FirstOrDefault(n => n.key == worldRepick);
            worldRepick = null;
            if (again != null) { worldPicked = again; worldPane = WorldPane.Picked; ShowAsset(again.asset); }
        }
        const float rowH = 22;
        var area = new Rect(r.x, y, r.width, r.yMax - y);
        var view = new Rect(0, 0, r.width - 16, Mathf.Max(worldRows.Count * rowH, area.height - 2));
        worldScroll = GUI.BeginScrollView(area, worldScroll, view);
        var ev = Event.current;
        for (int i = 0; i < worldRows.Count; i++)
        {
            var n = worldRows[i];
            var row = new Rect(2, i * rowH, view.width - 2, rowH - 1);
            if (row.yMax < worldScroll.y || row.y > worldScroll.y + area.height) continue;
            bool picked = worldPicked != null && worldPicked.key == n.key;
            if (picked) GUI.Box(row, GUIContent.none, cellSelected);
            float x = row.x + 4 + n.depth * 14;
            if (n.hasChildren && GUI.Button(new Rect(x, row.y + 2, 16, 18), n.open ? "▾" : "▸", EditorStyles.label)) ToggleOpen(n.key);
            x += 16;
            var labelStyle = n.kind == WorldKind.Header || n.kind == WorldKind.Action ? gold : n.kind == WorldKind.Group ? EditorStyles.boldLabel : EditorStyles.label;
            string label = n.kind == WorldKind.Event ? "✉ " + n.label : n.label;
            // A tag (board, walled, base) sits at the row's right end; the note takes what is left.
            float right = row.xMax;
            if (!string.IsNullOrEmpty(n.tag))
            {
                float tw = TagStyle.CalcSize(new GUIContent(n.tag)).x + 8;
                right = row.xMax - tw - 4;
                var tagRect = new Rect(right + 2, row.y + 3, tw, 15);
                GUI.Box(tagRect, GUIContent.none, EditorStyles.helpBox);
                GUI.Label(tagRect, n.tag, TagStyle);
            }
            float lw = Mathf.Min(labelStyle.CalcSize(new GUIContent(label)).x + 6, right - x - 20);
            GUI.Label(new Rect(x, row.y + 2, lw, 18), label, labelStyle);
            if (!string.IsNullOrEmpty(n.note)) GUI.Label(new Rect(x + lw + 4, row.y + 3, Mathf.Max(0, right - x - lw - 6), 16), n.warn ? $"<color=#e0a85a>{n.note}</color>" : n.note, NoteStyle);
            // A biome stage takes rooms dropped on it; a place takes a level.
            if (row.Contains(ev.mousePosition) && (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && Accepts(n, DragAndDrop.objectReferences))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Link;
                if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Drop(n, DragAndDrop.objectReferences); }
                ev.Use();
            }
            if (ev.type == EventType.MouseDown && ev.button == 0 && row.Contains(ev.mousePosition)) { Pick(n); dragFrom = n; ev.Use(); }
            // Right-click a row with an asset file behind it: Show in Project, Copy ID, Delete... (not a base biome: its
            // file is its level's).
            if (ev.type == EventType.MouseDown && ev.button == 1 && row.Contains(ev.mousePosition) && n.asset != null && EditorUtility.IsPersistent(n.asset) && AssetDatabase.IsMainAsset(n.asset))
            { AssetMenu(AssetDatabase.GetAssetPath(n.asset)); ev.Use(); }
            if (ev.type == EventType.MouseDrag && dragFrom == n && n.asset != null && (n.kind == WorldKind.Room || n.kind == WorldKind.Level) && row.Contains(ev.mousePosition))
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new[] { n.asset };
                DragAndDrop.StartDrag(n.label);
                dragFrom = null;
                ev.Use();
            }
        }
        if (worldRows.Count == 0) GUI.Label(new Rect(10, 10, view.width - 20, 40), "Nothing matches.", dim);
        GUI.EndScrollView();
    }
    WorldNode dragFrom;

    GUIStyle tagStyle, noteStyle;
    GUIStyle TagStyle => tagStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 1) };
    GUIStyle NoteStyle => noteStyle ??= new GUIStyle(dimLine ?? EditorStyles.miniLabel) { richText = true };

    static bool Accepts(WorldNode n, UnityEngine.Object[] objects)
    {
        if (n.kind == WorldKind.Stage && n.biome != null && n.poolUsed) return objects.Any(o => o is GameObject g && g.GetComponent<DungeonRoomTemplate>() != null || AssetStudioWorld.IsEnemy(o));
        if (n.kind == WorldKind.Place) return objects.Any(o => o is TableLevelData);
        return false;
    }

    void Drop(WorldNode n, UnityEngine.Object[] objects)
    {
        if (n.kind == WorldKind.Stage)
        {
            // Rooms go in its pool, enemies in what lives there.
            int rooms = objects.Count(o => o is GameObject g && AssetStudioWorld.AddToPool(n.biome, g));
            int enemies = objects.Count(o => o is GameObject g && AssetStudioWorld.AddEnemy(n.biome, g));
            Toast(rooms + enemies == 0 ? "Already there." : (rooms > 0 ? $"Put {rooms} room{(rooms == 1 ? "" : "s")} in {n.biome.name}'s pool. " : "") + (enemies > 0 ? $"{enemies} enem{(enemies == 1 ? "y" : "ies")} now live in {n.biome.name}." : ""));
        }
        else if (n.kind == WorldKind.Place && objects.OfType<TableLevelData>().FirstOrDefault() is TableLevelData level)
        {
            AssetStudioWorld.SetPlaceLevel((CampaignEncounter)n.asset, level);
            Toast($"{((CampaignEncounter)n.asset).displayName} now plays {level.name}.");
        }
        RefreshWorld();
    }

    void Pick(WorldNode n)
    {
        if (n == null) return;
        if (n.kind == WorldKind.Action) { StartForm(WorldPane.NewLevel); return; }
        worldPicked = n;
        worldPickedKey = n?.key;
        worldPane = WorldPane.Picked;
        worldPaneScroll = Vector2.zero;
        if (n.hasChildren && n.kind != WorldKind.Header && n.kind != WorldKind.Group && !n.open) ToggleOpen(n.key);
        else if (n.kind == WorldKind.Header || n.kind == WorldKind.Group) ToggleOpen(n.key);
        ShowAsset(n.asset);
    }

    // The preview and inspector follow the picked thing: its library entry when it has one, else the asset itself.
    // A base biome is shown itself (the library entry of its file is its level's).
    void ShowAsset(UnityEngine.Object asset)
    {
        if (asset == null) { Select(null); return; }
        if (asset is DungeonBiome b && AssetStudioWorld.IsBase(b)) { Select(null); inspected = b; Repaint(); return; }
        string path = AssetDatabase.GetAssetPath(asset);
        var entry = categories.SelectMany(c => c.entries).FirstOrDefault(e => e.path == path);
        if (entry != null) { Select(entry); return; }
        Select(null);
        // A map template's inspector is its component's (the map painter and its settings), not the bare prefab's.
        inspected = asset is GameObject go && go.TryGetComponent<DungeonMapTemplate>(out var map) ? map : asset;
        Repaint();
    }

    void StartForm(WorldPane pane)
    {
        worldPane = pane;
        formName = "";
        var picked = worldPicked;
        formLevel = picked?.level ?? World.levels.FirstOrDefault(l => !l.tool && l.data.IsDungeon)?.data;
        formBiome = picked?.biome;
        if (pane == WorldPane.NewLevel) { formLevel = World.levels.FirstOrDefault(l => l.data.name == "Dungeon1")?.data ?? formLevel; formLayout = 0; formRooms = 3; }
        formAct = picked != null && picked.act > 0 ? picked.act : 1;
        formFloors = 2; formBoss = false; formOwnBiomes = true; formUseMap = false;
        if (pane == WorldPane.NewMap) formRooms = 2;
        if (pane == WorldPane.NewLevel) StartNewLevel();
        GUI.FocusControl(null);
    }

    // ── The middle: what the picked thing is made of ─────────────────

    // Draws the World's middle panel; a room shows its usage over the usual preview (returns false so it still draws).
    bool DrawWorldMiddle(ref Rect r)
    {
        if (worldPane == WorldPane.Picked && worldPicked != null && worldPicked.kind == WorldKind.Room && selected != null)
        {
            var room = worldPicked.asset as GameObject;
            var uses = World.UsesOf(room);
            var strip = new Rect(r.x, r.y, r.width, 64);
            GUI.Box(strip, GUIContent.none);
            GUI.Label(new Rect(strip.x + 10, strip.y + 4, strip.width - 20, 16), "WHERE THIS ROOM IS", gold);
            string text = uses.Count == 0 ? "Nobody uses it yet. Drag it onto a biome in the tree, or use Put in a pool." :
                string.Join("   ", uses.GroupBy(u => u.level).Select(g => $"{g.Key.name}: " + string.Join(", ", g.Select(u => u.biome != null ? $"{u.biome.name} ({u.role.ToLowerInvariant()})" : u.role.ToLowerInvariant()))));
            GUI.Label(new Rect(strip.x + 10, strip.y + 20, strip.width - 150, 40), text, dim);
            if (GUI.Button(new Rect(strip.xMax - 136, strip.y + 6, 126, 24), new GUIContent("Play from this room", "A new run that opens in this room. Its other tests are under TRY IT on the right."))) Toast(GameLauncher.PlayRoom(room));
            if (GUI.Button(new Rect(strip.xMax - 136, strip.y + 34, 126, 24), "Put in a pool..."))
            {
                var menu = new GenericMenu();
                // The pools a run plays: each level's stages, or its base biome when it has none.
                foreach (var (group, levels) in World.Groups(false))
                    foreach (var l in levels)
                        foreach (var st in l.RunStages.Where(x => x.biome != null))
                            menu.AddItem(new GUIContent($"{group}/{l.data.name}/{st.biome.name}"), false, () => { AssetStudioWorld.AddToPool(st.biome, room); Toast($"Put {room.name} in {st.biome.name}'s pool."); RefreshWorld(); });
                if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("No level has a biome to put it in"));
                menu.ShowAsContext();
            }
            r.yMin += 70;
            return false;
        }
        GUI.Box(r, GUIContent.none);
        var inner = new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16);
        GUILayout.BeginArea(inner);
        worldPaneScroll = GUILayout.BeginScrollView(worldPaneScroll);
        try
        {
            // Edits to the campaign manager and realm map prefabs (and the places and events made with them) aren't on
            // Ctrl+Z: this puts the files back.
            if (AssetStudioWorld.PrefabUndo.Last is string last && GUILayout.Button("Undo: " + last, EditorStyles.miniButton))
            { Toast(AssetStudioWorld.PrefabUndo.UndoLast()); worldPicked = null; RefreshLibrary(); GUIUtility.ExitGUI(); }
            switch (worldPane)
            {
                case WorldPane.NewLevel: NewLevelForm(); break;
                case WorldPane.NewBiome: NewBiomeForm(); break;
                case WorldPane.NewRoom: NewRoomForm(); break;
                case WorldPane.NewPlace: NewPlaceForm(); break;
                case WorldPane.NewEvent: NewEventForm(); break;
                case WorldPane.NewMap: NewMapForm(); break;
                case WorldPane.Tidy: TidyPane(); break;
                default: PickedPane(); break;
            }
        }
        finally { GUILayout.EndScrollView(); GUILayout.EndArea(); }
        return true;
    }

    void Title(string text, string sub = null)
    {
        GUILayout.Label(text, titleStyle);
        if (!string.IsNullOrEmpty(sub)) GUILayout.Label(sub, dim);
        GUILayout.Space(6);
    }

    void Section(string text) { GUILayout.Space(8); GUILayout.Label(text, gold); }

    bool Big(string label, string tip = null) => GUILayout.Button(new GUIContent(label, tip), GUILayout.Height(26));

    void PickedPane()
    {
        var n = worldPicked;
        // Its asset deleted or undone since it was picked (elsewhere): nothing to show.
        if (n != null && n.asset == null && !ReferenceEquals(n.asset, null)) { worldPicked = null; worldPickedKey = null; n = null; }
        var m = World;
        if (n == null)
        {
            Title("World", "The campaign and the levels as one tree. Pick anything on the left; New... makes a level, a biome, a room, a place or an event already filed and linked.");
            return;
        }
        switch (n.kind)
        {
            case WorldKind.Header:
                Title(n.label.Substring(0, 1) + n.label.Substring(1).ToLowerInvariant());
                if (n.key == "campaign") { if (Big("New place on the map")) StartForm(WorldPane.NewPlace); if (Big("New campaign event")) StartForm(WorldPane.NewEvent); }
                if (n.key == "levels" && Big("New level")) StartForm(WorldPane.NewLevel);
                if (n.key == "maps" && Big("New map template")) StartForm(WorldPane.NewMap);
                break;
            case WorldKind.Group: GroupPane(n); break;
            case WorldKind.Act: ActPane(n.act); break;
            case WorldKind.Place: PlacePane((CampaignEncounter)n.asset); break;
            case WorldKind.Event: EventPane((CampaignEvent)n.asset); break;
            case WorldKind.Level: LevelPane(m.LevelOf(n.level)); break;
            case WorldKind.Stage: StagePane(n); break;
            case WorldKind.Map: MapPane(((GameObject)n.asset).GetComponent<DungeonMapTemplate>()); break;
            case WorldKind.Note when n.asset is GameObject g && g.GetComponent<DungeonMapTemplate>() != null: MapPane(g.GetComponent<DungeonMapTemplate>()); break;
            case WorldKind.Defaults: DefaultsPane(n.asset as TableLevelDefaults); break;
            default: Title(n.label, n.note); break;
        }
    }

    // A group of levels (an act, Not on the map, Other) or the shared biomes: what is in it, and New level there.
    void GroupPane(WorldNode n)
    {
        var m = World;
        if (n.key == "sharedbiomes")
        {
            Title("Shared biomes", "Biome files several levels run (a change shows in each), or none. A biome only one level runs is under that level.");
            foreach (var b in m.SharedBiomes)
            {
                var users = m.LevelsOf(b).Where(x => !x.tool).Select(x => x.data.name).ToList();
                if (GUILayout.Button($"{b.name}   <color=#a9a49a>{(users.Count == 0 ? "no level runs it" : string.Join(", ", users))}</color>", cell)) PickBiome(b);
            }
            return;
        }
        var group = m.Groups(worldShow == 3).FirstOrDefault(g => "lgroup:" + g.group == n.key);
        string sub = n.act > 0 ? $"The levels the Act {n.act} places play (a level played in several acts is under the first), in the campaign's order."
            : n.label == AssetStudioWorld.NotOnMapGroup ? "Levels in the catalog that no place on the campaign map plays yet: give one a place."
            : "Test levels and levels the catalog doesn't list (the intro's practice board, the debug arena, pockets).";
        Title(n.label, sub);
        if (group.levels != null)
            foreach (var l in group.levels)
            {
                string kind = l.data.IsBoard ? "board" : l.data.IsDungeon ? "walled" : "town";
                if (GUILayout.Button($"{l.data.name}   <color=#a9a49a>{kind}{(l.data.IsDungeon ? " · " + l.data.RunSummary : "")}</color>" + (l.problems.Count > 0 ? $"   <color=#e0a85a>{l.problems.Count} to look at</color>" : ""), cell)) PickLevel(l.data);
            }
        if (Big(n.act > 0 ? $"New level in Act {n.act}" : "New level")) { StartForm(WorldPane.NewLevel); if (n.act > 0) formAct = n.act; }
    }

    void DefaultsPane(TableLevelDefaults d)
    {
        Title("Shared defaults", "What every dungeon level shares: the plain chest and its sounds, the merchant and his stock, breakables, features, wall lights, room shapes, the room event pieces (its Room events tab: shrine, spike plate, ambush gate and puff, their sounds, snoring) and the run's HUD pieces. A biome adds its own breakables, features, wall lights and shapes on top.");
        if (d == null) { GUILayout.Label($"<color=#e0a85a>Missing: {AssetStudioWorld.DefaultsPath}. Levels then have no chests, merchant, breakables or HUD pieces.</color>", new GUIStyle(dim) { richText = true, wordWrap = true }); return; }
        GUILayout.Label("Merchant: " + (d.merchantPrefab != null ? d.merchantPrefab.name : "<color=#e0a85a>none (no level has a merchant)</color>") + "   ·   Plain chest: " + (d.chestPrefab != null ? d.chestPrefab.name : "<color=#e0a85a>none</color>"), new GUIStyle(dim) { richText = true });
        GUILayout.Label("Edit it in the inspector on the right. A change here reaches every level at once.", dim);
    }

    void ActPane(int act)
    {
        var m = World;
        Title("Act " + act, "Places open when their act does; events show once, the first time the map is seen after it.");
        Section("PLACES");
        foreach (var p in m.places.Where(p => p.act == act))
        {
            string problem = PlaceProblem(m, p);
            if (GUILayout.Button($"{p.displayName}   <color=#a9a49a>{(p.level != null ? p.level.name : p.kind.ToString())}{(p.Locked ? " · shut: " + ShutText(p) : "")}</color>" + (problem != null ? $"   <color=#e0a85a>{problem}</color>" : ""), cell)) PickAsset(p);
        }
        if (Big("New place in Act " + act)) { StartForm(WorldPane.NewPlace); formAct = act; }
        Section("EVENTS");
        foreach (var e in m.events.Where(e => e.afterAct == act)) if (GUILayout.Button(e.title, cell)) PickAsset(e);
        if (Big("New event after Act " + act)) { StartForm(WorldPane.NewEvent); formAct = act; }
    }

    void PlacePane(CampaignEncounter p)
    {
        var m = World;
        Title(p.displayName, $"A place on the campaign map · id {p.id}");
        string problem = PlaceProblem(m, p);
        GUILayout.Label(problem == null ? "<color=#6fd46a>✔ On the map and in the campaign manager's list.</color>" : $"<color=#e0a85a>{problem}</color>", new GUIStyle(dim) { richText = true });
        if (!m.placesOnMap.Contains(p) && Big("Put it on the map")) { Toast(AssetStudioWorld.PutOnMap(p)); RefreshWorld(); }
        if (!m.listedPlaces.Contains(p) && Big("Add it to the campaign manager's list")) { AssetStudioWorld.ListInManager(p, null); Toast("Listed."); RefreshWorld(); }
        Section("WHAT IT IS");
        int act = Mathf.Max(1, EditorGUILayout.DelayedIntField(new GUIContent("Act", "Any act from 1: the map covers each act up to the highest a place has."), p.act));
        if (act != p.act)
        {
            AssetStudioWorld.SetPlaceAct(p, act);
            // Its point and file can follow it to the new act.
            if (m.placesOnMap.Contains(p) && EditorUtility.DisplayDialog("Move to Act " + act, $"Move {p.displayName} near the Act {act} places on the map, and its file to Campaign/Encounters/Act {act}? Its roads stay as they are: check them in the Realm map prefab.", "Move it", "Leave it where it is"))
                Toast(AssetStudioWorld.MoveToAct(p));
            RefreshWorld();
            GUIUtility.ExitGUI();
        }
        var grouped = GroupedLevels();
        var levels = grouped.Select(g => g.level).ToList();
        GUILayout.BeginHorizontal();
        int at = levels.IndexOf(p.level) + 1;
        int pick = EditorGUILayout.Popup("Plays", at, new[] { "No level (not made yet)" }.Concat(grouped.Select(g => g.path)).ToArray());
        if (pick != at) { AssetStudioWorld.SetPlaceLevel(p, pick == 0 ? null : levels[pick - 1]); RefreshWorld(); }
        var kind = (CampaignEncounterKind)EditorGUILayout.EnumPopup(p.kind, GUILayout.Width(100));
        if (kind != p.kind) { AssetStudioWorld.SetPlaceKind(p, kind); RefreshWorld(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("Or drag a level from the tree onto this place. Picking a level makes it a Level place, no level a placeholder. Its name, card line and the Dungeon Master's line are in the inspector.", dim);
        ShutSection(p);
        Section("ON ITS DISC");
        int props = AssetStudioWorld.PropCount(p);
        GUILayout.Label(props < 0 ? "Not on the map yet." : props == 0 ? "No props yet: an empty base. Add some, or copy another place's." : $"{props} prop{(props == 1 ? "" : "s")}. Move them in the Realm map prefab (Points > {p.displayName}).", dim);
        if (props >= 0)
        {
            GUILayout.BeginHorizontal();
            if (Big("Copy the props of..."))
            {
                var menu = new GenericMenu();
                foreach (var other in m.places.Where(o => o != p && m.placesOnMap.Contains(o))) menu.AddItem(new GUIContent($"Act {other.act}/{other.displayName}"), false, () => { Toast(AssetStudioWorld.CopyProps(p, other)); RefreshWorld(); });
                menu.ShowAsContext();
            }
            if (Big("Add a prop..."))
            {
                var menu = new GenericMenu();
                foreach (var prop in AssetStudioWorld.MapProps()) menu.AddItem(new GUIContent(prop.Replace("SM_", "").Replace("_Optimized", "").Replace("_", " ")), false, () => { Toast(AssetStudioWorld.AddProp(p, prop)); RefreshWorld(); });
                menu.ShowAsContext();
            }
            if (props > 0 && Big("Clear props") && EditorUtility.DisplayDialog("Clear props", $"Take every prop off {p.displayName}'s disc?", "Clear", "Keep")) { Toast(AssetStudioWorld.ClearProps(p)); RefreshWorld(); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
        }
        if (p.level != null)
        {
            Section("ITS LEVEL");
            if (Big("Open " + p.level.name)) PickLevel(p.level);
        }
        TestSection(p);
    }

    // Shut until deaths somewhere (CampaignEncounter.deathsToOpen): off the map, road and all, until the player has died
    // often enough in another place, then it rises into view with its line; its hint is said once while it is still shut
    // (shutHint). Edited through the asset's own properties (undoable).
    void ShutSection(CampaignEncounter p)
    {
        Section("SHUT UNTIL");
        var so = new SerializedObject(p);
        so.Update();
        var deaths = so.FindProperty("deathsToOpen");
        if (deaths == null) return;
        EditorGUILayout.PropertyField(deaths, new GUIContent("Deaths to open", "0: it opens with its act. Else it stays off the map (its road too) until the player has died this many times in the place below."));
        if (deaths.intValue > 0)
        {
            if (so.FindProperty("deathsIn") is SerializedProperty inPlace) EditorGUILayout.PropertyField(inPlace, new GUIContent("Deaths in", "The place whose deaths count (campaign.deaths.<its id>)."));
            if (so.FindProperty("openedLine") is SerializedProperty opened) EditorGUILayout.PropertyField(opened, new GUIContent("Said as it rises"), true);
            if (so.FindProperty("shutHint") is SerializedProperty hint) EditorGUILayout.PropertyField(hint, new GUIContent("Hint while shut", "Said once, back in the room, after the first death counted toward it while it is still shut."), true);
        }
        if (so.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(p);
            // Saved once typing in a line is done, at once for the numbers and the place.
            if (!EditorGUIUtility.editingTextField) AssetDatabase.SaveAssetIfDirty(p);
            RefreshWorld();
        }
        string state = p.Locked ? char.ToUpperInvariant(ShutText(p)[0]) + ShutText(p).Substring(1) + "." : p.deathsToOpen > 0 ? "No place to die in is set: it opens with its act." : "Opens with its act.";
        GUILayout.Label(state, dim);
    }

    void EventPane(CampaignEvent e)
    {
        var m = World;
        Title(e.title, $"A campaign event · id {e.id} · {e.choices?.Length ?? 0} choice{(e.choices?.Length == 1 ? "" : "s")}");
        GUILayout.Label(m.listedEvents.Contains(e) ? "<color=#6fd46a>✔ In the campaign manager's events: it will show.</color>" : "<color=#e0a85a>Not in the campaign manager's events: it never shows.</color>", new GUIStyle(dim) { richText = true });
        if (!m.listedEvents.Contains(e) && Big("Add it to the campaign manager's events")) { AssetStudioWorld.ListInManager(null, e); Toast("Listed."); RefreshWorld(); }
        int act = Mathf.Max(1, EditorGUILayout.DelayedIntField("Shows after Act", e.afterAct));
        if (act != e.afterAct) { AssetStudioWorld.SetEventAct(e, act); RefreshWorld(); }
        GUILayout.Label("Its picture, text and choices (what each costs, gives and says) are in the inspector on the right.", dim);
        TestSection(e);
    }

    void LevelPane(AssetStudioWorld.Level l)
    {
        if (l == null) return;
        var d = l.data;
        Title(d.name, $"{d.kind} · {(d.IsDungeon ? $"{d.FloorCount} floor{(d.FloorCount == 1 ? "" : "s")} · {d.layoutMode} layout" : "")} · {l.path}");
        // Just made with a New level flow: what to do next, right here.
        if (justMade == d && d != null)
        {
            GUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("<color=#6fd46a>✔ Made.</color> " + (d.mapTemplate != null ? $"Build each room under its Room child in {d.mapTemplate.name} (open in Prefab Mode), then play it." : "Fill its biomes' pools and play it."), new GUIStyle(dim) { richText = true });
            GUILayout.BeginHorizontal();
            if (Big("Play it", "Generate its first floor and play it (the toolbar's Dungeon button with this level picked).")) Toast(GameLauncher.PlayLevel(d));
            if (d.mapTemplate != null && Big("Open " + d.mapTemplate.name)) AssetDatabase.OpenAsset(d.mapTemplate.gameObject);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        if (l.problems.Count == 0 && !l.tool) GUILayout.Label("<color=#6fd46a>✔ Nothing to look at.</color>", new GUIStyle(dim) { richText = true });
        foreach (var p in l.problems) GUILayout.Label("<color=#e0a85a>• " + p + "</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        if (!l.inCatalog && !l.tool && Big("Add it to the level catalog")) { AssetStudioWorld.AddToCatalog(d); RefreshWorld(); }
        TestSection(d);
        if (Big("New place playing it")) { StartForm(WorldPane.NewPlace); formLevel = d; formName = d.displayName ?? d.name; formAct = l.act > 0 ? l.act : 1; }
        if (l.places.Count > 0) { Section("ON THE MAP"); foreach (var p in l.places) if (GUILayout.Button($"Act {p.act} · {p.displayName}", cell)) PickAsset(p); }

        if (!d.IsDungeon) return;
        Section("THE RUN (top floor first)");
        if (!string.IsNullOrEmpty(d.RunSummary)) GUILayout.Label(d.RunSummary, new GUIStyle(dim) { wordWrap = true });
        if (l.Staged)
            foreach (var st in l.stages)
            {
                int i = st.index;
                if (d.stages == null || i < 0 || i >= d.stages.Length || d.stages[i] == null) continue;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"Floors {st.firstFloor}{(st.lastFloor > st.firstFloor ? "-" + st.lastFloor : "")}   {(st.biome != null ? st.biome.name : "<color=#e0a85a>no biome</color>")}" + (st.boss ? "   <color=#a9a49a>boss</color>" : ""), cell)) { PickStage(d, i); }
                int floors = EditorGUILayout.DelayedIntField(d.stages[i].floors, GUILayout.Width(36));
                bool boss = GUILayout.Toggle(d.stages[i].boss, "boss", GUILayout.Width(50));
                if (floors != d.stages[i].floors || boss != d.stages[i].boss) SetStage(d, i, floors, boss);
                if (GUILayout.Button("▲", GUILayout.Width(24))) { AssetStudioWorld.MoveStage(d, i, -1); RefreshWorld(); }
                if (GUILayout.Button("▼", GUILayout.Width(24))) { AssetStudioWorld.MoveStage(d, i, 1); RefreshWorld(); }
                bool last = d.stages.Count(s => s != null && s.biome != null) == 1 && st.biome != null;
                if (GUILayout.Button("✕", GUILayout.Width(24)) && EditorUtility.DisplayDialog("Remove stage", $"Take {(st.biome != null ? st.biome.name : "this stage")} off {d.name}'s run? The biome itself stays." + (last ? $" It is the last stage: its {Mathf.Max(1, d.stages[i].floors)} floor{(d.stages[i].floors == 1 ? "" : "s")} then run on {d.name}'s base biome." : ""), "Remove", "Keep")) { RemoveStage(d, i); GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
            }
        else if (d.baseBiome == null)
        {
            GUILayout.Label("<color=#e0a85a>No stages and no base biome: its floors have nothing to run on.</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
            if (Big("Make the base biome", "Its base biome, inside its file, with the level's own settings.")) { d.MakeBaseBiome(); AssetDatabase.SaveAssets(); RefreshLibrary(); GUIUtility.ExitGUI(); }
        }
        else
        {
            // No stages: every floor runs on the base biome. Floors and its boss are the level's.
            var b = d.baseBiome;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Floors 1{(d.floors > 1 ? "-" + d.floors : "")}   {b.name}   <color=#a9a49a>base biome</color>" + (d.bossOnLastFloor && AssetStudioWorld.HasGuardian(b) ? "   <color=#a9a49a>boss</color>" : ""), cell)) PickStage(d, -1);
            int floors = EditorGUILayout.DelayedIntField(d.floors, GUILayout.Width(36));
            bool boss = GUILayout.Toggle(d.bossOnLastFloor, new GUIContent("boss", "The base biome's guardian waits on the last floor."), GUILayout.Width(50));
            if (floors != d.floors || boss != d.bossOnLastFloor) SetBaseRun(d, floors, boss);
            GUILayout.EndHorizontal();
            if (d.bossOnLastFloor && !AssetStudioWorld.HasGuardian(b)) GUILayout.Label("Its base biome has no guardian set up, so no boss waits (its Guardian tab).", dim);
            GUILayout.Label("No stages: every floor runs on its base biome (enemies, loot, look, room pool). Add a stage to run biomes in turn instead.", dim);
        }
        GUILayout.BeginHorizontal();
        if (Big("New biome stage")) { StartForm(WorldPane.NewBiome); formLevel = d; }
        if (Big("Add an existing biome..."))
        {
            var menu = new GenericMenu();
            foreach (var b in World.biomes) menu.AddItem(new GUIContent(b.name), false, () => { AssetStudioWorld.AddStage(d, b); RefreshWorld(); });
            menu.ShowAsContext();
        }
        GUILayout.EndHorizontal();

        if (d.layoutMode == DungeonLayoutMode.Authored)
        {
            Section("LAYOUT (hand-painted)");
            var map = (DungeonMapTemplate)EditorGUILayout.ObjectField(new GUIContent("Map template", "A prefab that holds the whole map: its rooms, passages and doors painted on it, each room built under its Room child. Empty: the level's own text plan."), d.mapTemplate, typeof(DungeonMapTemplate), false);
            if (map != d.mapTemplate) { AssetStudioWorld.SetMapTemplate(d, map); RefreshWorld(); }
            if (d.mapTemplate != null)
            {
                GUILayout.Label($"Its floor plan and rooms come from {d.mapTemplate.name}: paint and build them there.", dim);
                GUILayout.BeginHorizontal();
                if (Big("Show " + d.mapTemplate.name)) PickAsset(d.mapTemplate.gameObject);
                if (Big("Open it to paint and build")) AssetDatabase.OpenAsset(d.mapTemplate.gameObject);
                GUILayout.EndHorizontal();
            }
            else
            {
                AuthoredRooms(d);
                if (Big("New map template for " + d.name, "A prefab holding this level's whole map, each room built under its own child, previewed with real walls in Prefab Mode.")) NewMapFor(d);
            }
        }
        else
        {
            Section("LAYOUT");
            GUILayout.Label($"Generated: about {d.roomCount} rooms a floor. Its rooms come from each biome's pool; the starting room and shop are the level's.", dim);
            GUILayout.BeginHorizontal();
            if (Big("Make it rooms in a row instead", "A fixed layout: rooms side by side, entrance to exit; you pick each room's interior below.") && ConfirmLayout(d, 3)) { AssetStudioWorld.MakeRoomsInARow(d, 3); RefreshWorld(); }
            if (Big("Give it a map template", "A fixed layout held in a prefab: paint the map and build each room under its Room child.")) NewMapFor(d);
            GUILayout.EndHorizontal();
        }
        Section("ITS OWN ROOMS");
        RoomField(d, d, "Starting room", d.startingRoom, v => d.startingRoom = v);
        // The shop room is on the base biome's Level tab.
        if (d.baseBiome != null) RoomField(d, d.baseBiome, "Shop room", d.baseBiome.shopRoom, v => d.baseBiome.shopRoom = v);
        if (Big("New room for " + d.name)) { StartForm(WorldPane.NewRoom); formLevel = d; formBiome = null; }

        if (d.baseBiome != null)
        {
            var b = d.baseBiome;
            Section("BASE BIOME (inside the level's file)");
            GUILayout.Label($"Style {(b.defaultStyle != null ? b.defaultStyle.name : "none")} · {b.roomStyles?.Length ?? 0} room style{(b.roomStyles?.Length == 1 ? "" : "s")} · kit models {(b.kitModels ? "on" : "off")} · {b.chestTiers?.Length ?? 0} chest tier{(b.chestTiers?.Length == 1 ? "" : "s")} · shop {(b.shopRoom != null ? b.shopRoom.name : "a quiet side room")}"
                + (d.IsBoard ? " · board light " + Mathf.RoundToInt(b.boardLightIntensity) : ""), new GUIStyle(dim) { wordWrap = true });
            GUILayout.Label(l.Staged ? "What every floor shares (its Level tab and room features); its stages add their own lists on top." : "What every floor shares and, with no stages, what lives there too.", dim);
            if (Big("Open the base biome")) PickStage(d, -1);
        }
    }

    // A level without stages: its floor count and whether the base biome's guardian waits on the last floor.
    void SetBaseRun(TableLevelData d, int floors, bool boss)
    {
        Undo.RecordObject(d, "Floors");
        d.floors = Mathf.Max(1, floors); d.bossOnLastFloor = boss;
        EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d);
        RefreshWorld();
    }

    // A painted layout's rooms in order: what each is, its interior, and whether the merchant stands in it.
    void AuthoredRooms(TableLevelData d)
    {
        Section("ROOMS (hand-painted layout, entrance first)");
        var rooms = d.authoredRooms ?? Array.Empty<TableLevelData.AuthoredRoom>();
        for (int i = 0; i < rooms.Length; i++)
        {
            var a = rooms[i];
            if (a == null) continue;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{i}", GUILayout.Width(16));
            string label = EditorGUILayout.DelayedTextField(a.label, GUILayout.Width(110));
            var role = (DungeonGenerator.RoomRole)EditorGUILayout.EnumPopup(a.role, GUILayout.Width(90));
            var template = (GameObject)EditorGUILayout.ObjectField(a.template, typeof(GameObject), false, GUILayout.MinWidth(120));
            bool merchant = GUILayout.Toggle(a.merchant, new GUIContent("merchant", "The merchant stands in this room."), GUILayout.Width(80));
            GUILayout.EndHorizontal();
            if (label != a.label || role != a.role || template != a.template || merchant != a.merchant) SetAuthoredRoom(d, i, label, role, template, merchant);
        }
        GUILayout.Label("Room 0 without an interior uses the starting room. The last room has the stairs out. Drag rooms from the tree onto a field.", dim);
        GUILayout.BeginHorizontal();
        if (layoutRoomsFor != d) { layoutRoomsFor = d; layoutRooms = Mathf.Clamp(rooms.Length, 2, 8); }
        layoutRooms = EditorGUILayout.IntSlider("Rooms", layoutRooms, 2, 8);
        if (GUILayout.Button("Lay out again", GUILayout.Width(110)) && ConfirmLayout(d, layoutRooms))
        { AssetStudioWorld.MakeRoomsInARow(d, layoutRooms); RefreshWorld(); }
        GUILayout.EndHorizontal();
    }

    // Laying a level out as rooms in a row replaces its floor plan: always asked, in plain words about what goes.
    static bool ConfirmLayout(TableLevelData d, int count)
    {
        bool painted = AssetStudioWorld.HasHandPaintedPlan(d);
        int had = d.authoredRooms?.Length ?? 0;
        string what = painted
            ? $"This replaces {d.name}'s hand-painted floor plan with {count} rooms in a row and empties its content map (the enemies, items and breakables painted on it)."
            : d.layoutMode == DungeonLayoutMode.Authored ? $"Lay {d.name} out as {count} rooms in a row?" + (count < had ? $" Rooms {count} to {had - 1} go." : "")
            : $"{d.name} stops generating its floors and becomes {count} rooms in a row.";
        return EditorUtility.DisplayDialog("Lay out again", what + " Ctrl+Z undoes it.", painted ? "Replace it" : "Lay out", "Cancel");
    }

    void NewMapFor(TableLevelData d)
    {
        StartForm(WorldPane.NewMap);
        formLevel = d; formUseMap = true; formName = d.name + " map";
        formRooms = Mathf.Clamp(d.mapTemplate == null && d.layoutMode == DungeonLayoutMode.Authored ? d.authoredRooms?.Length ?? 2 : 2, 2, 8);
    }

    void MapPane(DungeonMapTemplate map)
    {
        if (map == null) return;
        var m = World;
        var rooms = map.Rooms();
        var users = m.MapLevels(map);
        Title(map.name, $"A map template · {rooms.Length} room{(rooms.Length == 1 ? "" : "s")} · {AssetDatabase.GetAssetPath(map)}");
        var problems = map.Problems();
        if (problems.Count == 0) GUILayout.Label("<color=#6fd46a>✔ Ready to play.</color>", new GUIStyle(dim) { richText = true });
        foreach (var pr in problems) GUILayout.Label("<color=#e0a85a>• " + pr + "</color>", new GUIStyle(dim) { richText = true, wordWrap = true });
        GUILayout.Label(users.Count == 0 ? "No level plays it yet." : "Played by " + string.Join(", ", users.Select(u => u.name)) + ".", dim);
        TestSection(map.gameObject);
        Section("ROOMS (room 0 is where the player starts, the last has the stairs out)");
        for (int i = 0; i < rooms.Length; i++)
        {
            var r = rooms[i];
            GUILayout.Label($"{DungeonMapTemplate.RoomMark(i)}   {r.label}   <color=#a9a49a>{r.role.ToString().ToLowerInvariant()}{(r.merchant ? " · merchant" : "")}{(r.template == null ? " · furnished by the generator" : " · hand-built")}</color>", new GUIStyle(dim) { richText = true });
        }
        GUILayout.Label("Paint rooms, passages and doors on the map in its inspector, press Sync rooms, and build each room under its Room child.", dim);
        GUILayout.BeginHorizontal();
        if (Big("Open it to paint and build")) AssetDatabase.OpenAsset(map.gameObject);
        if (Big("Use it for a level..."))
        {
            var menu = new GenericMenu();
            foreach (var (path, level) in GroupedLevels())
                menu.AddItem(new GUIContent(path), level.mapTemplate == map, () => { AssetStudioWorld.SetMapTemplate(level, map); Toast($"{level.name} plays {map.name}."); RefreshWorld(); });
            menu.ShowAsContext();
        }
        GUILayout.EndHorizontal();
        foreach (var u in users) if (GUILayout.Button("Open " + u.name, cell)) PickLevel(u);
    }

    void SetStage(TableLevelData d, int i, int floors, bool boss)
    {
        Undo.RecordObject(d, "Stage");
        d.stages[i].floors = Mathf.Max(1, floors); d.stages[i].boss = boss;
        EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d);
        RefreshWorld();
    }

    void RemoveStage(TableLevelData d, int i) { Toast(AssetStudioWorld.RemoveStage(d, i)); RefreshWorld(); }

    void SetAuthoredRoom(TableLevelData d, int i, string label, DungeonGenerator.RoomRole role, GameObject template, bool merchant)
    {
        var a = d.authoredRooms[i];
        if (template != null && template.GetComponent<DungeonRoomTemplate>() == null) { Toast(template.name + " is not a room template."); template = a.template; }
        Undo.RecordObject(d, "Authored room");
        a.label = label; a.role = role; a.template = template; a.merchant = merchant;
        if (merchant && d.merchantPrefab == null) Toast($"No merchant prefab in the shared level defaults ({AssetStudioWorld.DefaultsPath.Replace("Assets/Game/", "")}, Chests & shop tab): nobody will stand there.");
        EditorUtility.SetDirty(d); AssetDatabase.SaveAssetIfDirty(d);
        RefreshWorld();
    }

    // A room field of a level, set on owner (the level, or its base biome for the shop room) with Undo.
    void RoomField(TableLevelData d, UnityEngine.Object owner, string label, GameObject value, Action<GameObject> set)
    {
        var v = (GameObject)EditorGUILayout.ObjectField(label, value, typeof(GameObject), false);
        if (v == value) return;
        if (v != null && v.GetComponent<DungeonRoomTemplate>() == null) { Toast(v.name + " is not a room template."); return; }
        Undo.RecordObject(owner, label);
        set(v);
        EditorUtility.SetDirty(owner); EditorUtility.SetDirty(d);
        AssetDatabase.SaveAssetIfDirty(d);
        RefreshWorld();
    }

    // A stage row: a stage of a level's run, a level's base biome (stage -1), or a biome file on its own (no level).
    void StagePane(WorldNode n)
    {
        var m = World;
        if (n.level == null) { BiomePane(null, null, n.biome ?? n.asset as DungeonBiome); return; }
        var l = m.LevelOf(n.level);
        var st = l == null ? null : n.stage < 0 ? l.baseStage : l.stages.FirstOrDefault(s => s.index == n.stage);
        if (st == null) { Title(n.label, "Not on this level's run any more."); return; }
        BiomePane(l, st, st.biome);
    }

    // What a biome is made of: its tests, room pool, what lives there and its loot. A level's base biome also shows its
    // level-wide settings; under stages only those (its own pool, enemies and loot don't play there).
    void BiomePane(AssetStudioWorld.Level l, AssetStudioWorld.Stage st, DungeonBiome b)
    {
        var m = World;
        bool isBase = st != null && st.IsBase;
        bool runs = l == null || !isBase || !l.Staged;
        var others = b != null ? m.LevelsOf(b).Where(x => !x.tool && x != l).Select(x => x.data.name).ToList() : new List<string>();
        string floors = st != null ? $"Floor{(st.lastFloor > st.firstFloor ? "s" : "")} {st.firstFloor}{(st.lastFloor > st.firstFloor ? "-" + st.lastFloor : "")}" : "";
        string sub = l == null ? "A biome file · " + (others.Count == 0 ? "no level runs it" : "run by " + string.Join(", ", others))
            : isBase && runs ? $"{floors} of {l.data.name} · its base biome, inside the level's file"
            : isBase ? $"{l.data.name}'s base biome, inside the level's file: what every floor shares. Its own enemies, loot and room pool only play on a level without stages."
            : $"{floors} of {l.data.name}" + (others.Count > 0 ? " · shared with " + string.Join(", ", others) : "");
        Title(b != null ? b.name : "No biome", sub);
        if (b == null) return;
        if (l != null && runs) StageTests(l, st);
        else if (l == null) TestSection(b);
        if (runs) BiomeContent(l, b);
        if (isBase) LevelWide(l.data, b);
        if (l == null && others.Count > 0)
        {
            Section("RUN BY");
            foreach (var u in m.LevelsOf(b).Where(x => !x.tool)) if (GUILayout.Button(u.data.name, cell)) PickLevel(u.data);
        }
    }

    // The base biome's Level tab, in short; edited in the inspector.
    void LevelWide(TableLevelData d, DungeonBiome b)
    {
        Section("LEVEL-WIDE (its Level tab, every floor of " + d.name + ")");
        string Names(DungeonWeightedPrefab[] list) => list == null || list.Length == 0 ? "the shared plain chest" : string.Join(", ", list.Where(x => x != null && x.prefab != null).Select(x => $"{x.prefab.name} {x.weight:0.##}"));
        GUILayout.Label($"Default style: {(b.defaultStyle != null ? b.defaultStyle.name : "none")}   ·   room styles {b.roomStyles?.Length ?? 0}, corridor styles {b.corridorStyles?.Length ?? 0}   ·   kit models {(b.kitModels ? "on" : "off")}", new GUIStyle(dim) { wordWrap = true });
        GUILayout.Label($"Torches {b.torchesPerRoom.x}-{b.torchesPerRoom.y} a room   ·   dark room light {(b.darkRoomLight != null ? b.darkRoomLight.name : "none")}   ·   breakables {b.destructiblesPerRoom.x}-{b.destructiblesPerRoom.y} extra a room", new GUIStyle(dim) { wordWrap = true });
        GUILayout.Label("Chests: " + Names(b.chestTiers), new GUIStyle(dim) { wordWrap = true });
        RoomField(d, b, "Shop room", b.shopRoom, v => b.shopRoom = v);
        if (d.IsBoard) GUILayout.Label($"Board: edge {(b.boardEdgeMaterial != null ? b.boardEdgeMaterial.name : "none")}, grid {(b.boardGridMaterial != null ? b.boardGridMaterial.name : "none")}, light {Mathf.RoundToInt(b.boardLightIntensity)}", new GUIStyle(dim) { wordWrap = true });
        GUILayout.Label("Its Level and Rooms tabs (room features) are in the inspector on the right. The room event pieces are every level's: Shared defaults, Room events tab.", dim);
    }

    void BiomeContent(AssetStudioWorld.Level l, DungeonBiome b)
    {
        Section("ROOM POOL (hand-built rooms its floors are made of)");
        var pool = b.roomTemplates ?? Array.Empty<DungeonBiome.RoomTemplateChoice>();
        for (int i = 0; i < pool.Length; i++)
        {
            var c = pool[i];
            if (c == null) continue;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(c.template != null ? c.template.name : "<color=#e0a85a>empty</color>", cell, GUILayout.MinWidth(140))) { if (c.template != null) PickAsset(c.template); }
            GUILayout.Label("weight", dim, GUILayout.Width(40));
            float w = EditorGUILayout.DelayedFloatField(c.weight, GUILayout.Width(40));
            var kinds = (DungeonRoomRoles)EditorGUILayout.EnumFlagsField(c.rooms, GUILayout.Width(120));
            GUILayout.Label("from floor", dim, GUILayout.Width(58));
            int from = EditorGUILayout.DelayedIntField(c.fromFloor, GUILayout.Width(30));
            if (GUILayout.Button("✕", GUILayout.Width(24))) { AssetStudioWorld.RemoveFromPool(b, i); RefreshWorld(); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            if (w != c.weight || kinds != c.rooms || from != c.fromFloor) { Undo.RecordObject(b, "Pool entry"); c.weight = Mathf.Max(0, w); c.rooms = kinds; c.fromFloor = Mathf.Max(1, from); EditorUtility.SetDirty(b); AssetDatabase.SaveAssetIfDirty(b); }
        }
        // Drop zone: rooms from the tree or the Project window.
        var drop = GUILayoutUtility.GetRect(10, 44, GUILayout.ExpandWidth(true));
        GUI.Box(drop, "Drop rooms here (from the tree or the Project window)", new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter });
        var ev = Event.current;
        if (drop.Contains(ev.mousePosition) && (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && DragAndDrop.objectReferences.Any(o => o is GameObject g && g.GetComponent<DungeonRoomTemplate>() != null))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); int n = DragAndDrop.objectReferences.Count(o => o is GameObject g && AssetStudioWorld.AddToPool(b, g)); Toast($"Put {n} room{(n == 1 ? "" : "s")} in {b.name}'s pool."); RefreshWorld(); }
            ev.Use();
        }
        GUILayout.BeginHorizontal();
        if (Big("Add a room..."))
        {
            var menu = new GenericMenu();
            foreach (var room in World.rooms.Where(r => !pool.Any(c => c != null && c.template == r)))
            {
                string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(room)).Replace('\\', '/').Replace(AssetStudioWorld.LevelsRoot + "/", "");
                menu.AddItem(new GUIContent(folder.Replace('/', ' ') + "/" + room.name), false, () => { AssetStudioWorld.AddToPool(b, room); RefreshWorld(); });
            }
            menu.ShowAsContext();
        }
        if (Big("New room in this pool")) { StartForm(WorldPane.NewRoom); formLevel = l?.data ?? World.LevelsOf(b).FirstOrDefault(x => !x.tool)?.data; formBiome = b; }
        GUILayout.EndHorizontal();
        EnemyList(b);
        Section("ALSO HERE");
        GUILayout.Label("Loot: " + (b.loot != null ? b.loot.name : "none") + (b.guardian != null && b.guardian.enabled && b.guardian.boss != null ? "   ·   Guardian: " + b.guardian.boss.name : ""), dim);
        GUILayout.Label("Loot, look and the guardian are edited in the inspector on the right.", dim);
    }

    // What lives in a biome, edited like its room pool: weight, from floor, remove, and enemy prefabs dropped in (from
    // the library's Enemies, the tree's stage rows take them too, or the Project window). Ctrl+Z undoes each change.
    void EnemyList(DungeonBiome b)
    {
        Section("WHAT LIVES HERE (weight is how common; from floor is the biome's floor it first comes on)");
        var spawns = b.enemies ?? Array.Empty<DungeonBiome.Spawn>();
        for (int i = 0; i < spawns.Length; i++)
        {
            var s = spawns[i];
            if (s == null) continue;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(s.enemy != null ? s.enemy.name : "<color=#e0a85a>empty</color>", cell, GUILayout.MinWidth(140)) && s.enemy != null) ShowAsset(s.enemy);
            GUILayout.Label("weight", dim, GUILayout.Width(40));
            float w = EditorGUILayout.DelayedFloatField(s.weight, GUILayout.Width(40));
            GUILayout.Label("from floor", dim, GUILayout.Width(58));
            int from = EditorGUILayout.DelayedIntField(s.fromFloor, GUILayout.Width(30));
            if (GUILayout.Button("✕", GUILayout.Width(24))) { AssetStudioWorld.RemoveEnemy(b, i); RefreshWorld(); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            if (w != s.weight || from != s.fromFloor) { AssetStudioWorld.SetEnemy(b, i, w, from); RefreshWorld(); }
        }
        if (spawns.Length == 0) GUILayout.Label("<color=#e0a85a>Nothing lives here yet: its rooms stay empty.</color>", new GUIStyle(dim) { richText = true });
        var drop = GUILayoutUtility.GetRect(10, 36, GUILayout.ExpandWidth(true));
        GUI.Box(drop, "Drop enemy prefabs here (from the Project window)", new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter });
        var ev = Event.current;
        if (drop.Contains(ev.mousePosition) && (ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && DragAndDrop.objectReferences.Any(AssetStudioWorld.IsEnemy))
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                int n = DragAndDrop.objectReferences.Count(o => o is GameObject g && AssetStudioWorld.AddEnemy(b, g));
                Toast(n > 0 ? $"{n} enem{(n == 1 ? "y" : "ies")} now live in {b.name}." : "Already there.");
                RefreshWorld();
            }
            ev.Use();
        }
        if (Big("Add an enemy..."))
        {
            var menu = new GenericMenu();
            var enemies = categories.Where(c => c.name == "Enemies").SelectMany(c => c.entries).ToList();
            foreach (var e in enemies)
            {
                var go = e.Load() as GameObject;
                if (go == null || spawns.Any(s => s != null && s.enemy == go)) continue;
                string folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(e.path));
                menu.AddItem(new GUIContent(folder + "/" + e.name), false, () => { AssetStudioWorld.AddEnemy(b, go); RefreshWorld(); });
            }
            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Every enemy in the library lives here already"));
            menu.ShowAsContext();
        }
    }

    void PickAsset(UnityEngine.Object asset)
    {
        if (asset == null) return;
        if (asset is DungeonBiome biome) { PickBiome(biome); return; }
        // A level: its tree row (its group opened so the row shows, picked under the same key as in the tree).
        if (asset is TableLevelData level && World.LevelOf(level) is AssetStudioWorld.Level found)
        {
            string key = "level:" + found.path;
            if (worldOpen.Add("levels") | worldOpen.Add("lgroup:" + found.Group)) EditorPrefs.SetString(WorldOpenKey, string.Join("|", worldOpen));
            Pick(new WorldNode { key = key, label = level.name, kind = WorldKind.Level, asset = level, level = level, act = found.act, hasChildren = true, open = worldOpen.Contains(key) });
            return;
        }
        var n = worldRows.FirstOrDefault(r => r.asset == asset) ?? new WorldNode { key = "asset:" + asset.GetHashCode(), label = asset.name, asset = asset,
            kind = asset is CampaignEncounter ? WorldKind.Place : asset is CampaignEvent ? WorldKind.Event : asset is TableLevelData ? WorldKind.Level
                : asset is GameObject g && g.GetComponent<DungeonMapTemplate>() != null ? WorldKind.Map
                : asset is GameObject r && r.GetComponent<DungeonRoomTemplate>() != null ? WorldKind.Room
                : asset is TableLevelDefaults ? WorldKind.Defaults : WorldKind.Note,
            level = asset as TableLevelData };
        Pick(n);
    }

    void PickLevel(TableLevelData level) => PickAsset(level);

    // A stage of a level's run, or its base biome (index -1).
    void PickStage(TableLevelData level, int index)
    {
        var l = World.LevelOf(level);
        var st = index < 0 ? l?.baseStage : l?.stages.FirstOrDefault(s => s.index == index);
        string key = index < 0 ? "base:" + l?.path : $"stage:{l?.path}:{index}";
        Pick(new WorldNode { key = key, label = st?.biome != null ? st.biome.name : "Stage", kind = WorldKind.Stage, asset = st?.biome, level = level, biome = st?.biome, stage = index, poolUsed = l == null || index >= 0 || !l.Staged });
    }

    // A biome: its level's stage when one level runs it (a base biome: its level's), else the biome on its own.
    void PickBiome(DungeonBiome b)
    {
        if (b == null) return;
        if (AssetStudioWorld.OwnerOf(b) is TableLevelData owner) { PickStage(owner, -1); return; }
        var users = World.LevelsOf(b).Where(x => !x.tool).ToList();
        var st = users.Count == 1 ? users[0].stages.FirstOrDefault(s => s.biome == b) : null;
        if (st != null) { PickStage(users[0].data, st.index); return; }
        Pick(new WorldNode { key = "biome:" + AssetDatabase.GetAssetPath(b), label = b.name, kind = WorldKind.Stage, asset = b, biome = b, stage = -2 });
    }

    // ── New forms ────────────────────────────────────────────────────

    string NameField(string label) { GUI.SetNextControlName("formName"); return formName = EditorGUILayout.TextField(label, formName); }

    // The dungeon levels in their groups (Act 1, Act 2, ..., Not on the map, Other), as "Act 1/Dungeon1" for popups and
    // menus (a slash makes a submenu).
    List<(string path, TableLevelData level)> GroupedLevels()
    {
        var list = new List<(string, TableLevelData)>();
        foreach (var (group, levels) in World.Groups(false))
            foreach (var l in levels) if (l.data.IsDungeon) list.Add((group + "/" + l.data.name, l.data));
        return list;
    }

    TableLevelData LevelPopup(string label, TableLevelData current, bool none = false)
    {
        var grouped = GroupedLevels();
        var levels = grouped.Select(g => g.level).ToList();
        int at = levels.IndexOf(current) + (none ? 1 : 0);
        var names = (none ? new[] { "None" } : Array.Empty<string>()).Concat(grouped.Select(g => g.path)).ToArray();
        int pick = EditorGUILayout.Popup(label, Mathf.Max(0, at), names);
        return none ? (pick == 0 ? null : levels[pick - 1]) : levels.Count > 0 ? levels[Mathf.Clamp(pick, 0, levels.Count - 1)] : null;
    }

    void Made(UnityEngine.Object made, string message)
    {
        Toast(message);
        RefreshLibrary();
        if (made != null) PickAsset(made); else worldPane = WorldPane.Picked;
    }

    // The third New level flow: a copy of a level as it is (NewLevelForm in AssetStudioWindow.NewLevel.cs has the others).
    void CopyLevelForm()
    {
        GUILayout.Label("A copy of a level you like (its look, layout, rules and merchant), in its own folder (Levels/<name>), in the level catalog. Then give it biomes, rooms and a place on the map.", dim);
        NameField("Name");
        formLevel = LevelPopup("Start from", formLevel);
        formOwnBiomes = EditorGUILayout.Toggle(new GUIContent("Its own biomes", "On: copies of the template's biomes, to change freely. Off: shares them (a change shows in both levels)."), formOwnBiomes);
        formLayout = EditorGUILayout.Popup("Layout", formLayout, new[] { "Like the template", "Rooms in a row (hand-painted)" });
        if (formLayout == 1) formRooms = EditorGUILayout.IntSlider("Rooms", formRooms, 2, 8);
        GUILayout.Space(8);
        if (Big("Make the level")) MakeLevel();
    }

    void MakeLevel()
    {
        var level = AssetStudioWorld.NewLevel(formName, formLevel, formOwnBiomes, out var message);
        if (level != null && formLayout == 1) { AssetStudioWorld.MakeRoomsInARow(level, formRooms); message += $" Laid out as {formRooms} rooms in a row."; }
        Made(level, message);
    }

    void NewBiomeForm()
    {
        Title("New biome stage", "A copy of a biome (its look, enemies, loot and rooms), added at the end of a level's run for some floors.");
        NameField("Name");
        formLevel = LevelPopup("Level", formLevel);
        var biomes = World.biomes;
        int at = Mathf.Max(0, biomes.IndexOf(formBiome));
        formBiome = biomes.Count > 0 ? biomes[EditorGUILayout.Popup("Start from", at, biomes.Select(b => b.name).ToArray())] : null;
        formFloors = EditorGUILayout.IntSlider("Floors", formFloors, 1, 6);
        formBoss = EditorGUILayout.Toggle("Boss on its last floor", formBoss);
        GUILayout.Space(8);
        if (Big("Make the biome")) MakeBiome();
    }

    void MakeBiome()
    {
        var b = AssetStudioWorld.NewBiome(formLevel, formName, formBiome, formFloors, formBoss, out var msg);
        Made(b != null ? (UnityEngine.Object)formLevel : null, msg);
    }

    void NewRoomForm()
    {
        Title("New room", "A blank 7 x 7 room in the level's Rooms folder (or Levels/Shared/Rooms), put in a biome's pool if you pick one. Open it to paint its footprint and drag kit pieces in.");
        NameField("Name");
        formLevel = LevelPopup("For level", formLevel, true);
        // The pools its run plays: its stages', or its base biome's when it has none (and a biome picked from elsewhere).
        var biomes = formLevel != null ? formLevel.RunBiomes().Distinct().ToList() : new List<DungeonBiome>();
        if (formBiome != null && !biomes.Contains(formBiome)) biomes.Add(formBiome);
        int at = biomes.IndexOf(formBiome) + 1;
        int pick = EditorGUILayout.Popup("Into the pool of", at, new[] { "No pool (place it yourself)" }.Concat(biomes.Select(b => b.name)).ToArray());
        formBiome = pick == 0 ? null : biomes[pick - 1];
        GUILayout.Space(8);
        if (Big("Make the room")) MakeRoom();
        if (worldPicked?.asset is GameObject made && made.GetComponent<DungeonRoomTemplate>() != null && Big("Open " + made.name + " to build it")) AssetDatabase.OpenAsset(made);
    }

    void MakeRoom() { var room = AssetStudioWorld.NewRoom(formName, formLevel, formBiome, out var msg); Made(room, msg); }

    void NewMapForm()
    {
        Title("New map template", "A whole small map in one prefab: rooms in a row to start from, a Room child per room. Open it to paint the map as you like (rooms, passages, doors), press Sync rooms, and build each room under its child.");
        NameField("Name");
        formLevel = LevelPopup("For level", formLevel, true);
        formRooms = EditorGUILayout.IntSlider("Rooms", formRooms, 2, 8);
        if (formLevel != null) formUseMap = EditorGUILayout.Toggle(new GUIContent($"{formLevel.name} plays it", "The level's layout becomes this map (Authored, Map Template)."), formUseMap);
        GUILayout.Label(formLevel != null ? $"Saved in {formLevel.name}'s folder." : "Saved in Levels/Shared/Maps.", dim);
        GUILayout.Space(8);
        if (Big("Make the map")) MakeMap();
        if (worldPicked?.asset is GameObject made && made.GetComponent<DungeonMapTemplate>() != null && Big("Open " + made.name + " to paint it")) AssetDatabase.OpenAsset(made);
    }

    void MakeMap()
    {
        if (formUseMap && formLevel != null && formLevel.layoutMode == DungeonLayoutMode.Authored && formLevel.mapTemplate == null && AssetStudioWorld.HasHandPaintedPlan(formLevel)
            && !EditorUtility.DisplayDialog("New map template", $"{formLevel.name} has a hand-painted text plan. Playing the new map instead leaves that plan unused (it stays on the level). Go on?", "Make the map", "Cancel")) return;
        var map = AssetStudioWorld.NewMapTemplate(formName, formLevel, formRooms, formUseMap, out var msg);
        Made(map != null ? map.gameObject : null, msg);
    }

    void NewPlaceForm()
    {
        Title("New place on the campaign map", "A place in an act: its asset, a point on the map near the act's other places (move it in the Realm map prefab), and its entry in the campaign manager's list.");
        NameField("Name");
        formAct = Mathf.Max(1, EditorGUILayout.IntField("Act", formAct));
        formLevel = LevelPopup("Plays", formLevel, true);
        GUILayout.Space(8);
        if (Big("Make the place")) MakePlace();
    }

    void MakePlace() { var p = AssetStudioWorld.NewPlace(formName, formAct, formLevel, out var msg); Made(p, msg); }

    void NewEventForm()
    {
        Title("New campaign event", "A picture, a few lines and choices, shown once the first time the map is seen after its act opens. Made with one Leave choice; write the rest in the inspector.");
        NameField("Title");
        formAct = Mathf.Max(1, EditorGUILayout.IntField("Shows after Act", formAct));
        GUILayout.Space(8);
        if (Big("Make the event")) MakeEvent();
    }

    void MakeEvent() { var e = AssetStudioWorld.NewEvent(formName, formAct, out var msg); Made(e, msg); }

    void TidyPane()
    {
        Title("Tidy folders", "Files follow the World: a room or biome one level uses goes in that level's folder (Rooms, Biomes), one several levels use in Levels/Shared. References are kept (Unity moves by GUID). Paths a tool writes in code stay put.");
        if (tidyMoves == null) return;
        if (tidyMoves.Count == 0) GUILayout.Label("<color=#6fd46a>✔ Everything is where it belongs.</color>", new GUIStyle(dim) { richText = true });
        foreach (var mv in tidyMoves)
        {
            GUILayout.BeginHorizontal();
            mv.on = GUILayout.Toggle(mv.on, "", GUILayout.Width(18));
            GUILayout.Label($"{System.IO.Path.GetFileNameWithoutExtension(mv.from)}   <color=#a9a49a>{System.IO.Path.GetDirectoryName(mv.from).Replace('\\', '/').Replace("Assets/Game/", "")} → {System.IO.Path.GetDirectoryName(mv.to).Replace('\\', '/').Replace("Assets/Game/", "")} · {mv.why}</color>", new GUIStyle(cell) { wordWrap = true });
            GUILayout.EndHorizontal();
        }
        int on = tidyMoves.Count(x => x.on);
        if (on > 0 && Big($"Move {on} file{(on == 1 ? "" : "s")}")
            && EditorUtility.DisplayDialog("Tidy folders", $"Move {on} file{(on == 1 ? "" : "s")}? References are kept (Unity moves by GUID). Move back puts them where they were.", $"Move {on}", "Cancel"))
        {
            tidyResult = AssetStudioWorld.ApplyTidy(tidyMoves, out var done);
            tidyFrom = done.Select(d => d.from).ToList(); tidyTo = done.Select(d => d.to).ToList();
            Debug.Log("[Asset Studio] Tidy folders: " + tidyResult + "\n" + string.Join("\n", done.Select(d => d.from + " -> " + d.to)));
            Toast(tidyResult);
            RefreshLibrary(); tidyMoves = AssetStudioWorld.TidyPlan(World, out tidyKept);
            GUIUtility.ExitGUI();
        }
        if (tidyFrom.Count > 0)
        {
            Section("LAST TIDY");
            if (!string.IsNullOrEmpty(tidyResult)) GUILayout.Label(tidyResult, new GUIStyle(dim) { wordWrap = true });
            if (Big($"Move back {tidyFrom.Count} file{(tidyFrom.Count == 1 ? "" : "s")}", "Every file the last Tidy moved goes back to where it was (one moved again since stays).")
                && EditorUtility.DisplayDialog("Move back", $"Put the {tidyFrom.Count} file{(tidyFrom.Count == 1 ? "" : "s")} the last Tidy moved back where they were?", "Move back", "Cancel"))
            {
                var done = tidyFrom.Select((from, i) => (from, to: tidyTo[i])).ToList();
                string back = AssetStudioWorld.MoveBack(done);
                tidyFrom.Clear(); tidyTo.Clear(); tidyResult = null;
                Toast(back);
                RefreshLibrary(); tidyMoves = AssetStudioWorld.TidyPlan(World, out tidyKept);
                GUIUtility.ExitGUI();
            }
        }
        if (tidyKept != null && tidyKept.Count > 0)
        {
            Section("LEFT WHERE THEY ARE");
            foreach (var k in tidyKept) GUILayout.Label(k.Replace("Assets/Game/", ""), dim);
        }
    }

    // ── For tests (an eval) ──────────────────────────────────────────
    public void TestWorld() { OpenWorld(); world = null; BuildRows(); }
    public string TestWorldRows() => string.Join("\n", worldRows.Select(r => new string(' ', r.depth * 2) + r.label + (string.IsNullOrEmpty(r.note) ? "" : "  (" + r.note + ")")));
    public void TestOpen(params string[] keys) { foreach (var k in keys) worldOpen.Add(k); BuildRows(); }
    public string TestPick(string label) { var n = worldRows.FirstOrDefault(r => r.label == label || r.label.EndsWith(label)); if (n == null) return "no row " + label; Pick(n); BuildRows(); return n.kind + " " + n.key; }
    public void TestForm(int pane) { StartForm((WorldPane)pane); }
    public string TestWorldKeys() => string.Join(" | ", worldRows.Select(r => r.key));
    // The level panel's controls: a stage's floors and boss, its remove button, and a painted room's row.
    TableLevelData TestLevel(string name) => World.levels.FirstOrDefault(l => l.data.name == name)?.data;
    public void TestStage(string level, int i, int floors, bool boss) => SetStage(TestLevel(level), i, floors, boss);
    public void TestRemoveStage(string level, int i) => RemoveStage(TestLevel(level), i);
    public string TestRoomRow(string level, int i, string label, int role, string template, bool merchant)
    {
        var d = TestLevel(level);
        var t = template == null ? null : World.rooms.FirstOrDefault(r => r.name == template);
        SetAuthoredRoom(d, i, label, (DungeonGenerator.RoomRole)role, t, merchant);
        return notice;
    }
    public void TestLayOutAgain(string level, int rooms) { var d = TestLevel(level); AssetStudioWorld.MakeRoomsInARow(d, rooms); RefreshWorld(); }
    public bool TestHandPainted(string level) => AssetStudioWorld.HasHandPaintedPlan(TestLevel(level));
    public string TestUndo() { var r = AssetStudioWorld.PrefabUndo.UndoLast(); RefreshLibrary(); return r; }
    // The place panel's prop buttons.
    public string TestAddProp(string place, string prop) { var p = World.places.First(x => x.displayName == place); var r = AssetStudioWorld.AddProp(p, prop); Toast(r); RefreshWorld(); return r; }
    public string TestPane() => $"{worldPane} · picked {worldPicked?.kind} {worldPicked?.label}";
    // Fill the open New form and press its Make button (what the form's fields and button do, nothing else).
    public string TestMake(string name, string level = null, string biome = null, int act = 1, int layout = 0, int rooms = 3, bool ownBiomes = true, int floors = 2, bool boss = false, bool useMap = false)
    {
        formUseMap = useMap;
        formName = name;
        formLevel = level == null ? null : World.levels.FirstOrDefault(l => l.data.name == level)?.data;
        formBiome = biome == null ? null : World.biomes.FirstOrDefault(b => b.name == biome);
        formAct = act; formLayout = layout; formRooms = rooms; formOwnBiomes = ownBiomes; formFloors = floors; formBoss = boss;
        switch (worldPane)
        {
            case WorldPane.NewLevel: MakeLevel(); break;
            case WorldPane.NewBiome: MakeBiome(); break;
            case WorldPane.NewRoom: MakeRoom(); break;
            case WorldPane.NewPlace: MakePlace(); break;
            case WorldPane.NewEvent: MakeEvent(); break;
            case WorldPane.NewMap: MakeMap(); break;
        }
        return notice;
    }
}

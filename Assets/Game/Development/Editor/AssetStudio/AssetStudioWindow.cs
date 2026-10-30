using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Asset Studio (the toolbar's Asset Studio button, Tools > LoomRoom > Asset Studio, or the desktop shortcut): the game's
// whole content library in one window, with Play game to start the game straight from it. Left, the
// categories (AssetStudioCatalog); next, a searchable list (name, permanent ast_ ID, path, kind; an exact ID finds its
// asset in any category; arrow keys move); middle, the preview: a turntable for anything 3D (drag to orbit, wheel to
// zoom, F to frame, auto-spin), audio with its waveform and Play/Stop/Loop, textures point-filtered at whole zooms;
// right, the asset's own inspector, its ID and path, and "Ask Main": a request about this asset, saved to the same
// outbox as the game's F9 prompt (DevReportStore) with a picture of the preview, for the Main session to pick up.
// Browsing never changes an asset; edits are made in the inspector shown, exactly as in Unity's own.
public partial class AssetStudioWindow : EditorWindow
{
    [MenuItem("Tools/LoomRoom/Asset Studio")]
    public static AssetStudioWindow Open()
    {
        var w = GetWindow<AssetStudioWindow>("Asset Studio");
        w.minSize = new Vector2(1280, 720);
        return w;
    }

    // The library, kept through recompiles and Play (the window's serialized state): only files changed since are read
    // again (AssetStudioCatalog.Apply); it is built whole once per Editor session and by Refresh library.
    [SerializeField] List<AssetStudioCatalog.Category> categories;
    [SerializeField] int categoryIndex;
    string search = "";
    List<AssetStudioCatalog.Entry> shown = new();
    AssetStudioCatalog.Entry selected;
    // The picked entry and World node, by ID and key, picked again after a recompile.
    [SerializeField] string selectedId, worldPickedKey;
    Vector2 listScroll, inspectorScroll, categoryScroll;
    string askText = "";
    int kindFilter;                       // 0: every kind in the category
    string[] kinds = { "All kinds" };
    [SerializeField] int itemSort;        // the Items list's order (ItemSorts)
    static readonly string[] ItemSorts = { "By name", "By main number", "By type", "By price", "By tier" };
    [SerializeField] int roomSort;        // the Rooms list's order (RoomSorts)
    static readonly string[] RoomSorts = { "By name", "By where they are", "By kind" };
    readonly List<string> recent = new();  // last picked, newest first (the Recent list)
    // Which view the window shows, one at a time: a category's list, or one of the views above the categories.
    enum StudioView { Category, Recent, Requests, Everything, World, Dialogue, Levels, Dungeon, Substats }
    [SerializeField] StudioView view;
    // Leaving the Dialogue view writes back what was typed there.
    StudioView studioView { get => view; set { if (value != view) CommitDialogue(true); view = value; } }
    const string BuiltKey = "LoomRoom.Studio.Built.2";   // a new number when the categories change: the library is built afresh
    bool showingRecent => studioView == StudioView.Recent;
    bool showingRequests => studioView == StudioView.Requests;
    bool showingAll => studioView == StudioView.Everything;
    bool showingWorld => studioView == StudioView.World;
    bool showingDialogue => studioView == StudioView.Dialogue;
    const string RecentKey = "LoomRoom.Studio.Recent", InGameOnlyKey = "LoomRoom.Studio.InGameOnly";
    bool inGameOnly;
    List<DevReportStore.Entry> requestList = new();
    DevReportStore.Entry request;          // the request picked in the Requests list
    Texture2D requestShot;
    double nextRequestRefresh;
    List<(string path, AssetStudioCatalog.Entry entry)> usedBy;   // Where used, for the selected asset
    string usedByFor;
    string notice = "";
    double noticeUntil;

    // Preview state
    AssetStudioPreview preview;
    UnityEngine.Object inspected;

    const float CategoryWidth = 200, ListWidth = 320, InspectorWidth = 500, Pad = 8;

    static readonly Color Gold = new(.91f, .74f, .38f);
    GUIStyle titleStyle, dim, gold, cell, cellSelected, dimLine;

    void OnEnable()
    {
        titleContent = new GUIContent("Asset Studio");
        inGameOnly = EditorPrefs.GetBool(InGameOnlyKey, false);
        recent.Clear();
        recent.AddRange(EditorPrefs.GetString(RecentKey, "").Split('|').Where(x => x.StartsWith("ast_")));
        preview ??= new AssetStudioPreview();
        // Built whole once per Editor session; after a recompile or Play only what changed is read again.
        if (categories == null || categories.Count == 0 || !SessionState.GetBool(BuiltKey, false)) Refresh(false);
        else { ApplyLibraryChanges(); categoryIndex = Mathf.Clamp(categoryIndex, 0, categories.Count - 1); if (categories[categoryIndex].hidden) categoryIndex = 0; Filter(); }
        if (selected == null && !string.IsNullOrEmpty(selectedId) && Find(selectedId) is AssetStudioCatalog.Entry keep) Select(keep);
        if (studioView == StudioView.World && !string.IsNullOrEmpty(worldPickedKey)) worldRepick = worldPickedKey;
        RefreshRequests();
        wantsMouseMove = true;
        EditorApplication.update += Tick;
        AssetStudioCatalog.Changed += MarkStale;
        EditorApplication.projectChanged += OnProjectChanged;
        Undo.undoRedoPerformed += OnUndoRedo;
        ObjectChangeEvents.changesPublished += OnObjectChanges;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    void OnDisable()
    {
        CommitDialogue(true);
        EditorApplication.update -= Tick;
        AssetStudioCatalog.Changed -= MarkStale;
        EditorApplication.projectChanged -= OnProjectChanged;
        Undo.undoRedoPerformed -= OnUndoRedo;
        ObjectChangeEvents.changesPublished -= OnObjectChanges;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.SceneManagement.EditorSceneManager.sceneOpened -= OnSceneOpened;
        preview?.Dispose();
        preview = null;
        AssetStudioPreview.StopAudio();
    }

    void OnLostFocus() => CommitDialogue(true);

    void Tick()
    {
        if (preview != null && preview.WantsRepaint) Repaint();
        if (staleAt > 0 && EditorApplication.timeSinceStartup >= staleAt) { staleAt = 0; CatchUp(); }
    }

    // ── Keeping up with changes made elsewhere (Ctrl+Z, an inspector, New..., Tidy, files saved) ──
    // Each change only marks the views stale; they are read again once things settle (staleAt), so a burst of changes
    // costs one catch-up.
    double staleAt;
    bool staleUndo, staleScenes, staleProject;
    readonly HashSet<string> stalePaths = new();

    void MarkStale() => staleAt = EditorApplication.timeSinceStartup + .4;
    void OnUndoRedo() { staleUndo = true; MarkStale(); }
    void OnProjectChanged() { staleProject = true; MarkStale(); }
    // Play started or stopped, or a scene opened: the scene objects that held lines are other objects now.
    void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode || change == PlayModeStateChange.EnteredPlayMode) { staleScenes = true; MarkStale(); }
    }
    void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, UnityEditor.SceneManagement.OpenSceneMode mode) { staleScenes = true; MarkStale(); }

    void OnObjectChanges(ref ObjectChangeEventStream stream)
    {
        for (int i = 0; i < stream.length; i++)
        {
            switch (stream.GetEventType(i))
            {
                case ObjectChangeKind.ChangeAssetObjectProperties:
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var args);
                    string path = AssetDatabase.GUIDToAssetPath(args.guid);
                    if (!string.IsNullOrEmpty(path)) stalePaths.Add(path);
                    break;
                case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                case ObjectChangeKind.ChangeGameObjectStructure:
                case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                case ObjectChangeKind.ChangeScene:
                    staleScenes = true;
                    break;
            }
        }
        MarkStale();
    }

    void CatchUp()
    {
        var changed = ApplyLibraryChanges();
        if (changed != null) stalePaths.UnionWith(changed);
        // Assets changed: the World model, the tomes and Where used are read again when next needed. Scene edits
        // alone only touch the Dialogue view's scene lines.
        if (staleUndo || staleProject || stalePaths.Count > 0)
        {
            world = null;
            tomes = null;
            AssetStudioUsage.Forget();
            usedBy = null; usedByFor = null;
        }
        RecollectDialogue(staleUndo, stalePaths, staleScenes || staleUndo);
        staleUndo = staleScenes = staleProject = false;
        stalePaths.Clear();
        Repaint();
    }

    // The files changed since the library was built, sorted in (or the whole library again when a folder moved).
    List<string> ApplyLibraryChanges()
    {
        if (categories == null || !AssetStudioCatalog.HasChanges) return null;
        var changes = AssetStudioCatalog.TakeChanges();
        if (changes == null) { Refresh(false); return null; }
        AssetStudioCatalog.Apply(categories, changes);
        Filter();
        // The picked entry was made again: keep showing it (the preview stays as it is).
        if (selected != null) { var again = Find(selected.id); if (again == null) Select(null); else selected = again; }
        return changes;
    }

    AssetStudioCatalog.Entry Find(string id) => categories?.SelectMany(c => c.entries).FirstOrDefault(e => e.id == id);

    // The whole library again. reselect: the picked entry is shown afresh (its preview built again).
    void Refresh(bool reselect = true)
    {
        string keep = selected?.id ?? selectedId;
        AssetStudioCatalog.ClearChanges();
        categories = AssetStudioCatalog.Build();
        SessionState.SetBool(BuiltKey, true);
        world = null;   // the World view rebuilds from the assets too
        tomes = null;
        AssetStudioUsage.Forget();
        categoryIndex = Mathf.Clamp(categoryIndex, 0, categories.Count - 1);
        if (categories.Count > 0 && categories[categoryIndex].hidden) categoryIndex = 0;
        Filter();
        if (keep == null) return;
        var e = Find(keep);
        if (reselect || e == null || selected == null) Select(e); else selected = e;
    }

    // The game's own UI look (StudioSkin): gold PixelOperator titles, off-white text, grey stone rows with a gold edge on
    // the picked one.
    void Styles()
    {
        if (titleStyle != null && dimLine != null && titleStyle.font != null) return;
        titleStyle = StudioSkin.Title;
        dim = StudioSkin.Faint;
        dimLine = StudioSkin.FaintLine;
        gold = StudioSkin.Section;
        cell = StudioSkin.Row;
        cellSelected = StudioSkin.RowOn;
    }

    static Texture2D Tex(Color c) { var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; t.SetPixel(0, 0, c); t.Apply(); return t; }

    void Filter()
    {
        string s = search.Trim().ToLowerInvariant();
        // An exact permanent ID finds its asset wherever it is.
        if (s.StartsWith("ast_"))
        {
            var exact = categories.SelectMany(c => c.entries).Where(e => e.id == s).ToList();
            if (exact.Count > 0) { shown = exact; return; }
        }
        var all = categories.SelectMany(c => c.entries);
        List<AssetStudioCatalog.Entry> source;
        if (showingRecent) source = recent.Select(id => all.FirstOrDefault(e => e.id == id)).Where(e => e != null).ToList();
        else if (showingAll) source = all.ToList();
        else source = categories.Count > 0 ? categories[categoryIndex].entries : new List<AssetStudioCatalog.Entry>();
        if (inGameOnly) source = source.Where(e => e.inGame).ToList();
        if (RoomsShown)
        {
            // Rooms by the places that use them: a room several levels use is under each of them.
            var where = RoomPlaces();
            kinds = new[] { "Every place" }.Concat(where.order.Where(k => source.Any(e => where.Of(e).Contains(k)))).ToArray();
            kindFilter = Mathf.Clamp(kindFilter, 0, kinds.Length - 1);
            if (kindFilter > 0) source = source.Where(e => where.Of(e).Contains(kinds[kindFilter])).ToList();
        }
        else if (!showingRecent)
        {
            kinds = new[] { "All kinds" }.Concat(source.Select(e => e.kind).Distinct().OrderBy(k => k)).ToArray();
            kindFilter = Mathf.Clamp(kindFilter, 0, kinds.Length - 1);
            if (kindFilter > 0) source = source.Where(e => e.kind == kinds[kindFilter]).ToList();
        }
        shown = string.IsNullOrEmpty(s) ? source : source.Where(e => s.Split(' ').All(w => e.Search.Contains(w))).ToList();
        if (ItemsShown && itemSort > 0)
        {
            var items = shown.Where(e => !string.IsNullOrEmpty(e.itemSlot));
            IOrderedEnumerable<AssetStudioCatalog.Entry> ordered = itemSort switch
            {
                1 => items.OrderByDescending(e => e.itemMain),
                2 => items.OrderBy(e => AssetStudioCatalog.ItemKindOrder(e.itemSlot)).ThenBy(e => e.itemTier).ThenByDescending(e => e.itemMain),
                3 => items.OrderByDescending(e => e.itemPrice),
                _ => items.OrderBy(e => e.itemTier).ThenBy(e => AssetStudioCatalog.ItemKindOrder(e.itemSlot)),
            };
            shown = ordered.ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase).Concat(shown.Where(e => string.IsNullOrEmpty(e.itemSlot))).ToList();
        }
        // Rooms by where they are: in campaign order of the first place that uses each, then by name.
        if (RoomsShown && roomSort == 1)
        {
            var where = RoomPlaces();
            int First(AssetStudioCatalog.Entry e) { var p = where.Of(e); return p.Count == 0 ? int.MaxValue : where.order.IndexOf(p[0]); }
            shown = shown.OrderBy(First).ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        // Rooms by kind: Scripted, Active, Visual, then the unsorted (DungeonRoomTemplate.kind), each by name.
        if (RoomsShown && roomSort == 2)
            shown = shown.OrderBy(e => AssetStudioRoomKinds.Rank(e.Load() as GameObject)).ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Which places use each room (from the World model: pools, starting rooms, shops, arenas, authored rooms, maps,
    // portals), named "The Crypt (Dungeon1)", or the level's name when no place plays it; in campaign order.
    class RoomWhere
    {
        public readonly List<string> order = new();
        public readonly Dictionary<string, List<string>> byPath = new();
        public List<string> Of(AssetStudioCatalog.Entry e) => byPath.TryGetValue(e.path, out var l) ? l : None;
        static readonly List<string> None = new() { NoPlace };
    }
    const string NoPlace = "No level uses it";
    [NonSerialized] RoomWhere roomWhere;
    [NonSerialized] AssetStudioWorld.Model roomWhereFor;

    RoomWhere RoomPlaces()
    {
        var model = World;
        if (roomWhere != null && roomWhereFor == model) return roomWhere;
        var where = new RoomWhere();
        string Label(AssetStudioWorld.Level l) =>
            l.places.Count > 0 ? string.Join(", ", l.places.Select(p => p.displayName)) + $" ({l.data.name})" : l.data.name;
        var levels = model.Groups(false).SelectMany(g => g.levels).ToList();
        foreach (var l in levels) where.order.Add(Label(l));
        where.order.Add(NoPlace);
        foreach (var room in model.rooms)
        {
            var labels = model.LevelsOf(room).Select(d => model.LevelOf(d)).Where(l => l != null)
                .OrderBy(l => levels.IndexOf(l)).Select(Label).Distinct().ToList();
            where.byPath[AssetDatabase.GetAssetPath(room)] = labels.Count > 0 ? labels : new List<string> { NoPlace };
        }
        roomWhereFor = model;
        return roomWhere = where;
    }

    // Shows an entry in its category's list; one in a category kept off the sidebar (places, events, house rules) is
    // found in Everything by its ID.
    void ShowEntry(AssetStudioCatalog.Entry e)
    {
        int index = categories.FindIndex(c => c.name == e.category);
        kindFilter = 0; listScroll = Vector2.zero;
        if (index < 0 || categories[index].hidden) { studioView = StudioView.Everything; search = e.id; }
        else { studioView = StudioView.Category; categoryIndex = index; search = ""; }
        Filter(); Select(e);
    }

    bool RoomsShown => studioView == StudioView.Category && categories != null && categoryIndex < categories.Count && categories[categoryIndex].name == "Rooms";
    bool SortShown => ItemsShown || RoomsShown;

    // The Items category is on screen: its rows show each item's main number, slot and price, and can be sorted by them.
    bool ItemsShown => studioView == StudioView.Category && categories != null && categoryIndex < categories.Count && categories[categoryIndex].name == "Items";

    void Select(AssetStudioCatalog.Entry e)
    {
        selected = e;
        selectedId = e?.id;
        AssetStudioPreview.StopAudio();
        if (e != null) { recent.Remove(e.id); recent.Insert(0, e.id); if (recent.Count > 20) recent.RemoveAt(recent.Count - 1); EditorPrefs.SetString(RecentKey, string.Join("|", recent)); }
        usedBy = null; usedByFor = null;
        if (e == null) { preview?.SetTarget(null); inspected = null; return; }
        var asset = e.Load();
        preview?.SetTarget(asset);
        var target = InspectorTarget(e, asset);
        inspected = target;
        Repaint();
    }

    // What the inspector shows: a character's data inside its prefab, a room's template, otherwise the asset itself.
    static UnityEngine.Object InspectorTarget(AssetStudioCatalog.Entry e, UnityEngine.Object asset)
    {
        if (asset is GameObject go)
        {
            var data = AssetDatabase.LoadAllAssetsAtPath(e.path).OfType<CharacterData>().FirstOrDefault();
            if (data != null) return data;
            var template = go.GetComponent<DungeonRoomTemplate>();
            if (template != null) return template;
            if (go.TryGetComponent<DungeonMapTemplate>(out var map)) return map;
            var mb = go.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null);
            return mb != null ? mb : go;
        }
        return asset;
    }

    void Toast(string text) { notice = text; noticeUntil = EditorApplication.timeSinceStartup + 4; }

    // ── Right-click on an asset (the library's lists, the World tree): Show in Project, Copy ID, Delete... ──

    void AssetMenu(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        string guid = AssetDatabase.AssetPathToGUID(path);
        var menu = new GenericMenu();
        // A room: its kind first (Scripted, Active, Visual).
        if (AssetDatabase.LoadMainAssetAtPath(path) is GameObject room && room.GetComponent<DungeonRoomTemplate>() != null)
        {
            AssetStudioRoomKinds.AddTo(menu, room, () => { Filter(); Repaint(); });
            menu.AddSeparator("");
        }
        menu.AddItem(new GUIContent("Show in Project"), false, () => { var a = AssetDatabase.LoadMainAssetAtPath(path); Selection.activeObject = a; EditorGUIUtility.PingObject(a); });
        menu.AddItem(new GUIContent("Copy ID"), false, () => { EditorGUIUtility.systemCopyBuffer = AssetStudioCatalog.IdFor(guid); Toast("Copied " + AssetStudioCatalog.IdFor(guid)); });
        menu.AddSeparator("");
        string blocked = DeleteBlocked(path);
        if (blocked == null) menu.AddItem(new GUIContent("Delete..."), false, () => DeleteAsset(path));
        else menu.AddDisabledItem(new GUIContent("Delete (" + blocked + ")"));   // no slash in the reason: it would make a submenu
        menu.ShowAsContext();
    }

    // Paths named in code (a builder or a loader reads or writes them there), read once per compile.
    [NonSerialized] HashSet<string> namedInCode;

    // Why this file can't be deleted from here, or null. Only the game's own content: never anything outside Assets/Game
    // (Synty, the packs, plugins), code, a scene in the build, what a Resources folder holds (code loads it by name:
    // TableLevels, Table level defaults, DungeonLighting) or a file a tool names in code.
    string DeleteBlocked(string path)
    {
        if (!path.StartsWith("Assets/Game/")) return "not the game's own content";
        if (AssetDatabase.IsValidFolder(path)) return "a folder";
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".cs" || ext == ".shader" || ext == ".hlsl" || ext == ".asmdef" || ext == ".dll") return "code";
        if (EditorBuildSettings.scenes.Any(s => s.path == path)) return "a scene in the build";
        if (path.Contains("/Resources/")) return "in a Resources folder, code loads it by name";
        namedInCode ??= AssetStudioWorld.PinnedPaths();
        if (namedInCode.Contains(path)) return "a tool names this file in code";
        return null;
    }

    // Asks first (what uses it), then moves the file to the system's recycle bin (it can be got back from there).
    void DeleteAsset(string path)
    {
        string why = DeleteBlocked(path);
        if (why != null) { Toast("Not deleted: " + why + "."); return; }
        string name = Path.GetFileNameWithoutExtension(path), guid = AssetDatabase.AssetPathToGUID(path);
        var users = AssetStudioUsage.Find(guid, categories).Where(u => u.path != path).ToList();
        string Name((string path, AssetStudioCatalog.Entry entry) u) => u.entry != null ? u.entry.name : Path.GetFileNameWithoutExtension(u.path);
        string use = users.Count == 0 ? "Nothing else in the game uses it."
            : $"Used by {users.Count} asset{(users.Count == 1 ? "" : "s")}: {string.Join(", ", users.Take(5).Select(u => Name(u)))}{(users.Count > 5 ? $" and {users.Count - 5} more" : "")}. {(users.Count == 1 ? "It" : "They")} will lose it.";
        if (!EditorUtility.DisplayDialog("Delete " + name, $"{name}\n{path}\n\n{use}\n\nIt goes to the recycle bin, so it can be got back from there.", "Delete", "Cancel")) return;
        CommitDialogue(true);
        // Let go of it first: the preview's copy, a painting on it, the inspector.
        if (selected != null && selected.path == path) Select(null);
        if (inspected != null && AssetDatabase.GetAssetPath(inspected) == path) inspected = null;
        if (!AssetDatabase.MoveAssetToTrash(path)) { Toast("Could not delete " + name + "."); return; }
        AssetStudioCatalog.Apply(categories, new[] { path });
        Filter();
        world = null; tomes = null;
        AssetStudioUsage.Forget();
        usedBy = null; usedByFor = null;
        Toast($"Moved {name} to the recycle bin.");
        Repaint();
    }

    void OnGUI()
    {
        using (StudioSkin.Use()) DrawWindow();
    }

    void DrawWindow()
    {
        Styles();
        if (categories == null) Refresh();
        HandleKeys();

        var full = new Rect(0, 0, position.width, position.height);
        EditorGUI.DrawRect(full, StudioSkin.Back);
        var top = new Rect(Pad, Pad, full.width - Pad * 2, 52);
        DrawTop(top);
        float y = top.yMax + Pad, h = full.height - y - 26;
        var catRect = new Rect(Pad, y, CategoryWidth, h);
        var listRect = new Rect(catRect.xMax + Pad, y, ListWidth, h);
        var inspRect = new Rect(full.width - InspectorWidth - Pad, y, InspectorWidth, h);
        var previewRect = new Rect(listRect.xMax + Pad, y, inspRect.xMin - listRect.xMax - Pad * 2, h);
        DrawCategories(catRect);
        inspectorWanted = null;
        if (showingDialogue) DrawDialogue(new Rect(listRect.x, y, full.width - listRect.x - Pad, h));
        else if (showingSubstats) DrawSubstats(new Rect(listRect.x, y, full.width - listRect.x - Pad, h));   // AssetStudioWindow.Substats
        else
        {
            DrawList(listRect);
            DrawPreview(previewRect);
            DrawInspector(inspRect);
        }
        if (Event.current.type == EventType.Repaint || Event.current.type == EventType.Layout) PlaceInspector();
        int total = categories.Sum(c => c.entries.Count);
        GUI.Label(new Rect(Pad, full.height - 22, full.width - Pad * 2, 18),
            EditorApplication.timeSinceStartup < noticeUntil ? notice : $"{shown.Count} shown · {total} entries in the library · The Table Awaits", dim);
    }

    void DrawTop(Rect r)
    {
        GUI.Label(new Rect(r.x, r.y - 4, 260, 20), "THE TABLE AWAITS", gold);
        GUI.Label(new Rect(r.x, r.y + 12, 340, 40), "Asset Studio", titleStyle);
        float bx = r.xMax;
        Button(ref bx, r, "Export catalog", () =>
        {
            string file = EditorUtility.SaveFilePanel("Export catalog", Application.dataPath + "/..", "asset_studio_catalog", "json");
            if (!string.IsNullOrEmpty(file)) Toast("Exported " + AssetStudioCatalog.Export(categories, file));
        });
        Button(ref bx, r, "Refresh library", () => { Refresh(); Toast("Library refreshed."); });
        Button(ref bx, r, EditorApplication.isPlaying ? "Stop game" : "Play game", () => { if (EditorApplication.isPlaying) EditorApplication.isPlaying = false; else GameLauncher.Play(); });
        Button(ref bx, r, "Requests to Main", () => { studioView = StudioView.Requests; RefreshRequests(); });
    }

    static void Button(ref float right, Rect row, string label, Action click)
    {
        float w = GUI.skin.button.CalcSize(new GUIContent(label)).x + 24;
        right -= w;
        if (GUI.Button(new Rect(right, row.y + 8, w, 36), label)) click();
        right -= 6;
    }

    int Count(AssetStudioCatalog.Category c) => inGameOnly ? c.entries.Count(e => e.inGame) : c.entries.Count;

    void DrawCategories(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x + 8, r.y + 6, r.width, 16), "LIBRARY", gold);
        // Everything in the folders, or only what the game uses (green check marks show which is which either way).
        var toggleRect = new Rect(r.x + 8, r.yMax - 46, r.width - 16, 20);
        bool only = EditorGUI.ToggleLeft(toggleRect, new GUIContent("Only what's in the game", "Hide assets that only sit in the folders: nothing the build's scenes or Resources lead to uses them."), inGameOnly);
        if (only != inGameOnly) { inGameOnly = only; EditorPrefs.SetBool(InGameOnlyKey, only); Filter(); listScroll = Vector2.zero; }
        GUI.Label(new Rect(r.x + 8, r.yMax - 24, r.width - 16, 18), "<color=#6fd46a>✔</color> = used in the game", new GUIStyle(dim) { richText = true });
        r.height -= 50;
        var view = new Rect(0, 0, r.width - 16, (categories.Count + 8) * 30 + 12);
        categoryScroll = GUI.BeginScrollView(new Rect(r.x, r.y + 26, r.width, r.height - 26), categoryScroll, view);
        // Dungeon: one dungeon the way a run goes, made, tuned and played in one place (AssetStudioWindow.Dungeon).
        if (GUI.Toggle(new Rect(4, 2, view.width - 4, 28), showingDungeon, "  Dungeon   <color=#a9a49a>the Crypt</color>", showingDungeon ? cellSelected : cell) && !showingDungeon) OpenDungeon();
        if (GUI.Toggle(new Rect(4, 32, view.width - 4, 28), showingRecent, $"  Recent   <color=#a9a49a>{recent.Count}</color>", showingRecent ? cellSelected : cell) && !showingRecent)
        { studioView = StudioView.Recent; search = ""; listScroll = Vector2.zero; Filter(); GUI.FocusControl(null); }
        int open = requestList.Count(q => q.status != DevReportStore.Status.Done && q.status != DevReportStore.Status.Failed);
        if (GUI.Toggle(new Rect(4, 62, view.width - 4, 28), showingRequests, $"  Requests to Main   <color=#a9a49a>{open}</color>", showingRequests ? cellSelected : cell) && !showingRequests)
        { studioView = StudioView.Requests; RefreshRequests(); GUI.FocusControl(null); }
        int total = categories.Sum(c => Count(c));
        if (GUI.Toggle(new Rect(4, 92, view.width - 4, 28), showingAll, $"  Everything   <color=#a9a49a>{total}</color>", showingAll ? cellSelected : cell) && !showingAll)
        { studioView = StudioView.Everything; kindFilter = 0; listScroll = Vector2.zero; Filter(); GUI.FocusControl("search"); }
        // The World: the campaign and the levels as one tree, with New and Tidy (AssetStudioWindow.World).
        if (GUI.Toggle(new Rect(4, 122, view.width - 4, 28), showingWorld, "  Campaign   <color=#a9a49a>acts, places</color>", showingWorld ? cellSelected : cell) && !showingWorld) OpenWorld();
        // Levels: one page per level, its run, rooms and look (AssetStudioWindow.Levels).
        if (GUI.Toggle(new Rect(4, 152, view.width - 4, 28), showingLevels, $"  Levels   <color=#a9a49a>{World.levels.Count(x => !x.tool)}</color>", showingLevels ? cellSelected : cell) && !showingLevels) OpenLevels();
        // Dialogue: every Dungeon Master line, by where it plays (AssetStudioWindow.Dialogue).
        if (GUI.Toggle(new Rect(4, 182, view.width - 4, 28), showingDialogue, $"  Dialogue   <color=#a9a49a>{(dialogue != null ? dialogue.Count + " lines" : "DM lines")}</color>", showingDialogue ? cellSelected : cell) && !showingDialogue) OpenDialogue();
        // Substats: what rolled gear can get, by item type, edited in place (AssetStudioWindow.Substats).
        if (GUI.Toggle(new Rect(4, 212, view.width - 4, 28), showingSubstats, $"  Substats   <color=#a9a49a>{SubstatCount} on gear</color>", showingSubstats ? cellSelected : cell) && !showingSubstats) OpenSubstats();
        for (int i = 0, line = 0; i < categories.Count; i++)
        {
            var c = categories[i];
            if (c.hidden) continue;
            var row = new Rect(4, 12 + (line++ + 8) * 30, view.width - 4, 28);
            bool on = studioView == StudioView.Category && i == categoryIndex;
            if (GUI.Toggle(row, on, $"  {c.name}   <color=#a9a49a>{Count(c)}</color>", on ? cellSelected : cell) && !on)
            {
                studioView = StudioView.Category; categoryIndex = i; kindFilter = 0; search = ""; listScroll = Vector2.zero; Filter(); GUI.FocusControl(null);
            }
        }
        GUI.EndScrollView();
    }

    void DrawList(Rect r)
    {
        if (showingRequests) { DrawRequestList(r); return; }
        if (showingWorld) { DrawWorldTree(r); return; }
        if (showingLevels) { DrawLevelList(r); return; }
        if (showingDungeon) { DrawDungeonList(r); return; }
        GUI.Box(r, GUIContent.none);
        GUI.SetNextControlName("search");
        string s = EditorGUI.TextField(new Rect(r.x + 6, r.y + 6, r.width - 12, 22), search, EditorStyles.toolbarSearchField);
        if (s != search) { search = s; Filter(); listScroll = Vector2.zero; }
        if (showingRecent) GUI.Label(new Rect(r.x + 8, r.y + 32, r.width - 16, 40), "What you looked at last, newest first.", dim);
        else
        {
            float kindWidth = SortShown ? (r.width - 16) / 2 : r.width - 12;
            int k = EditorGUI.Popup(new Rect(r.x + 6, r.y + 32, kindWidth, 20), kindFilter, kinds);
            if (k != kindFilter) { kindFilter = k; Filter(); listScroll = Vector2.zero; }
            if (ItemsShown)
            {
                int sort = EditorGUI.Popup(new Rect(r.x + 10 + kindWidth, r.y + 32, kindWidth, 20), itemSort, ItemSorts);
                if (sort != itemSort) { itemSort = sort; Filter(); listScroll = Vector2.zero; }
            }
            else if (RoomsShown)
            {
                int sort = EditorGUI.Popup(new Rect(r.x + 10 + kindWidth, r.y + 32, kindWidth, 20), roomSort, RoomSorts);
                if (sort != roomSort) { roomSort = sort; Filter(); listScroll = Vector2.zero; }
            }
            if (showingAll) GUI.Label(new Rect(r.x + 8, r.y + 56, r.width - 16, 34), "The whole library: type to search names, IDs, paths and kinds in every category.", dim);
            else if (categories.Count > 0) GUI.Label(new Rect(r.x + 8, r.y + 56, r.width - 16, 34), categories[categoryIndex].description, dim);
        }
        const float rowH = 44;
        var area = new Rect(r.x, r.y + 96, r.width, r.height - 100);
        var view = new Rect(0, 0, r.width - 16, Mathf.Max(shown.Count * rowH, area.height - 2));
        listScroll = GUI.BeginScrollView(area, listScroll, view);
        int first = Mathf.Max(0, (int)(listScroll.y / rowH) - 1), last = Mathf.Min(shown.Count, first + (int)(area.height / rowH) + 3);
        for (int i = first; i < last; i++)
        {
            var e = shown[i];
            var row = new Rect(2, i * rowH, view.width - 2, rowH - 2);
            bool on = e == selected;
            if (on) GUI.Box(row, GUIContent.none, cellSelected);
            var loaded = e.Load();
            // An item shows its own bag icon; anything else Unity's preview of it.
            Texture icon = loaded is ItemData item && item.icon != null ? item.icon.texture : AssetPreview.GetAssetPreview(loaded) ?? AssetPreview.GetMiniThumbnail(loaded);
            if (icon != null) GUI.DrawTexture(new Rect(row.x + 4, row.y + 3, rowH - 8, rowH - 8), icon, ScaleMode.ScaleToFit);
            if (e.inGame) DrawCheck(new Rect(row.x + rowH - 18, row.y + rowH - 20, 15, 15));
            // A room's kind in the icon's top corner (right-click the row to set it).
            if (RoomsShown && loaded is GameObject roomGo && roomGo.GetComponent<DungeonRoomTemplate>() != null) AssetStudioRoomKinds.Badge(new Rect(row.x + 2, row.y + 1, 16, 16), roomGo);
            GUI.Label(new Rect(row.x + rowH + 2, row.y + 3, row.width - rowH, 18), e.name, EditorStyles.boldLabel);
            GUI.Label(new Rect(row.x + rowH + 2, row.y + 21, row.width - rowH, 16), (showingAll || showingRecent ? e.category + " · " : "") + e.kind + (string.IsNullOrEmpty(e.description) ? "" : " · " + e.description), dimLine);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                Select(e); GUI.FocusControl(null);
                if (Event.current.button == 1) AssetMenu(e.path);   // right-click: Show in Project, Copy ID, Delete...
                Event.current.Use();
            }
        }
        if (shown.Count == 0) GUI.Label(new Rect(10, 10, view.width - 20, 40), search.Length > 0 ? "Nothing matches that search." : "Nothing in this category.", dim);
        GUI.EndScrollView();
        if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
    }

    static GUIStyle checkStyle;
    // The green check mark in an icon's corner: this asset is used in the game.
    static void DrawCheck(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(.08f, .3f, .1f, .95f));
        checkStyle ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 11, normal = { textColor = new Color(.45f, .95f, .4f) }, padding = new RectOffset(0, 0, 0, 1) };
        GUI.Label(r, "✔", checkStyle);
    }

    void DrawPreview(Rect r)
    {
        if (showingRequests) { DrawRequest(r); return; }
        if (showingWorld && DrawWorldMiddle(ref r)) return;
        if (showingLevels) { DrawLevelPage(r); return; }
        if (showingDungeon) { DrawDungeonPage(r); return; }
        GUI.Box(r, GUIContent.none);
        if (selected == null) { GUI.Label(new Rect(r.x + 16, r.y + 16, r.width - 32, 40), "Pick something on the left.", dim); return; }
        GUI.Label(new Rect(r.x + 14, r.y + 10, r.width - 28, 40), selected.name, titleStyle);
        GUI.Label(new Rect(r.x + 14, r.y + 50, r.width - 28, 18), $"{selected.category} · {selected.kind}", dim);
        float controls = preview.ControlsHeight;
        // The preview on top, the page under it (AssetStudioWindow.Pages): about two fifths for a model or a clip,
        // the page the rest. Painting takes the whole column.
        bool page = HasPage(selected) && !preview.Painting;
        float top = r.y + 74, bottom = r.yMax - 10;
        float stageHeight = page ? Mathf.Max(220, (bottom - top) * .42f - controls) : bottom - top - controls - 6;
        var stage = new Rect(r.x + 12, top, r.width - 24, stageHeight);
        preview.Draw(stage, this);
        var transport = new Rect(r.x + 12, stage.yMax + 6, r.width - 24, controls);
        preview.DrawControls(transport);
        if (page) DrawPage(new Rect(r.x + 10, transport.yMax + 8, r.width - 20, bottom - transport.yMax - 8));
    }

    void DrawInspector(Rect r)
    {
        if (showingRequests) { DrawRequestDetails(r); return; }
        GUI.Box(r, GUIContent.none);
        // The World can show an asset the library doesn't list: just its inspector.
        if (selected == null) { if ((showingWorld || showingLevels || showingDungeon) && inspected != null) inspectorWanted = new Rect(r.x + 4, r.y + 8, r.width - 8, r.height - 12); return; }
        float y = r.y + 8;
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "DETAILS", gold); y += 18;
        if (!string.IsNullOrEmpty(selected.description)) { float dh = dim.CalcHeight(new GUIContent(selected.description), r.width - 16); GUI.Label(new Rect(r.x + 8, y, r.width - 16, dh), selected.description, dim); y += dh + 4; }
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), selected.id, gold); y += 18;
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), selected.inGame ? "<color=#6fd46a>✔ Used in the game</color>" : "<color=#c9a46a>Only in the folders: nothing in the game uses it</color>", new GUIStyle(dim) { richText = true }); y += 18;
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 30), selected.path, dim); y += 30;
        float bw = (r.width - 24) / 3;
        if (GUI.Button(new Rect(r.x + 8, y, bw, 24), "Copy ID")) { EditorGUIUtility.systemCopyBuffer = selected.id; Toast("Copied " + selected.id); }
        if (GUI.Button(new Rect(r.x + 12 + bw, y, bw, 24), "Copy path")) { EditorGUIUtility.systemCopyBuffer = selected.path; Toast("Copied the path."); }
        if (GUI.Button(new Rect(r.x + 16 + bw * 2, y, bw, 24), "Show in Project")) { var a = selected.Load(); Selection.activeObject = a; EditorGUIUtility.PingObject(a); }
        y += 28;
        if (selected.Load() is GameObject && GUI.Button(new Rect(r.x + 8, y, r.width - 16, 24), "Open prefab")) AssetDatabase.OpenAsset(selected.Load());
        if (selected.Load() is GameObject) y += 28;

        y = DrawActions(r, y);

        // Ask Main about this asset: the request goes to the outbox with the asset's ID and a picture of the preview.
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "ASK MAIN ABOUT THIS", gold); y += 18;
        askText = EditorGUI.TextArea(new Rect(r.x + 8, y, r.width - 16, 54), askText, new GUIStyle(EditorStyles.textArea) { wordWrap = true }); y += 58;
        if (GUI.Button(new Rect(r.x + 8, y, r.width - 16, 26), "Send to Main")) AskMain();
        y += 32;

        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "INSPECTOR", gold); y += 18;
        var area = new Rect(r.x + 4, y, r.width - 8, r.yMax - y - 4);
        // Unity's own inspector element, laid over this spot: it draws every field exactly as the Inspector window
        // does (drawing the editor by hand here put Min and Range fields on top of each other).
        inspectorWanted = inspected != null ? area : (Rect?)null;
    }

    // ── The hosted inspector (UI Toolkit, over the IMGUI panel) ──
    UnityEngine.UIElements.ScrollView inspectorHost;
    UnityEngine.Object inspectorBound;
    Rect? inspectorWanted;

    void PlaceInspector()
    {
        if (inspectorHost == null)
        {
            inspectorHost = new UnityEngine.UIElements.ScrollView(UnityEngine.UIElements.ScrollViewMode.Vertical);
            inspectorHost.style.position = UnityEngine.UIElements.Position.Absolute;
            // The game's font in the inspector too (StudioSkin); its fields keep Unity's own shapes.
            if (StudioSkin.Body != null) inspectorHost.style.unityFontDefinition = UnityEngine.UIElements.FontDefinition.FromFont(StudioSkin.Body);
            inspectorHost.style.fontSize = StudioSkin.Size;
            inspectorHost.style.color = StudioSkin.Text;
            var sheet = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.StyleSheet>("Assets/Game/Development/Editor/AssetStudio/Studio Inspector.uss");
            if (sheet != null) inspectorHost.styleSheets.Add(sheet);
            // Inspectors drawn in IMGUI (Odin's, older editors) draw after the window: each is wrapped in the skin too.
            inspectorHost.schedule.Execute(SkinImgui).Every(300);
            rootVisualElement.Add(inspectorHost);
        }
        var want = showingRequests ? null : inspectorWanted;
        if (want == null) { if (inspectorHost.style.display != UnityEngine.UIElements.DisplayStyle.None) inspectorHost.style.display = UnityEngine.UIElements.DisplayStyle.None; return; }
        if (inspectorBound != inspected || inspectorHost.childCount == 0)
        {
            inspectorHost.Clear();
            inspectorBound = inspected;
            if (inspected != null) inspectorHost.Add(new UnityEditor.UIElements.InspectorElement(inspected));
            inspectorHost.scrollOffset = Vector2.zero;
        }
        var a = want.Value;
        if (inspectorHost.style.display != UnityEngine.UIElements.DisplayStyle.Flex) inspectorHost.style.display = UnityEngine.UIElements.DisplayStyle.Flex;
        if (inspectorHost.style.left.value.value != a.x) inspectorHost.style.left = a.x;
        if (inspectorHost.style.top.value.value != a.y) inspectorHost.style.top = a.y;
        if (inspectorHost.style.width.value.value != a.width) inspectorHost.style.width = a.width;
        if (inspectorHost.style.height.value.value != a.height) inspectorHost.style.height = a.height;
    }

    void SkinImgui()
    {
        if (inspectorHost == null) return;
        UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.IMGUIContainer>(inspectorHost).ForEach(c =>
        {
            if (c.userData as string == "studio skin" || c.onGUIHandler == null) return;
            var draw = c.onGUIHandler;
            c.onGUIHandler = () => { using (StudioSkin.Use()) draw(); };
            c.userData = "studio skin";
            c.MarkDirtyRepaint();
        });
    }

    // Each spell's tome (the item that casts it), read once per library refresh: the panel is drawn every frame.
    [NonSerialized] Dictionary<SpellDefinition, ItemData> tomes;
    [NonSerialized] string stateFor, stateText;
    [NonSerialized] DateTime stateWritten;
    ItemData TomeFor(SpellDefinition spell)
    {
        if (tomes == null)
        {
            tomes = new Dictionary<SpellDefinition, ItemData>();
            foreach (var g in AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Game" }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(g));
                if (item != null && item.spell != null && !tomes.ContainsKey(item.spell)) tomes[item.spell] = item;
            }
        }
        return tomes.TryGetValue(spell, out var tome) ? tome : null;
    }

    // What can be done with this kind of asset in the game, and where it is used. Returns the next free y.
    float DrawActions(Rect r, float y)
    {
        var asset = selected.Load();
        var actions = new List<(string label, string tip, Action run)>();
        ItemData giveItem = asset as ItemData;
        if (asset is SpellDefinition spell) giveItem = TomeFor(spell);
        if (giveItem != null) actions.Add((EditorApplication.isPlaying ? "Give to table player" : "Try it in the arena", "Into the table player's bag now, or start the Debug arena with it in the bag.", () => Toast(GameLauncher.GiveItem(giveItem))));
        if (asset is ItemData source) actions.Add(("Duplicate as new item", "A copy with its own save ID, in the same folder, added to the item catalog (as the Item Database does). It opens here to edit.", () =>
        {
            var copy = ItemDatabaseAuthoring.Duplicate(source);
            ItemDatabaseAuthoring.SyncCatalog();
            ApplyLibraryChanges();   // the copy, sorted in from the watcher's notes (not the whole library again)
            var e = categories.SelectMany(c => c.entries).FirstOrDefault(x => x.path == AssetDatabase.GetAssetPath(copy));
            studioView = StudioView.Category; categoryIndex = categories.FindIndex(c => c.name == "Items"); kindFilter = 0; search = ""; Filter(); Select(e);
            Toast($"Made {copy.name}. Rename it in the inspector; its icon renders on its own.");
        }));
        actions.AddRange(TestActions(asset));
        if (asset is Texture2D) actions.Add(("Open in image editor", "Opens the PNG in your default image editor. Unity reimports it when saved.", () => EditorUtility.OpenWithDefaultApp(selected.path)));
        bool listing = usedBy != null && usedByFor == selected.id;
        actions.Add((listing ? "Hide where used" : "Where used", "Every prefab, scene and asset that points at this one.", () =>
        {
            if (usedBy != null && usedByFor == selected.id) { usedBy = null; usedByFor = null; return; }
            usedBy = AssetStudioUsage.Find(selected.guid, categories); usedByFor = selected.id;
        }));

        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "TRY IT", gold); y += 18;
        float w = (r.width - 20) / 2;
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            var b = new Rect(r.x + 8 + (i % 2) * (w + 4), y + (i / 2) * 28, w, 24);
            if (GUI.Button(b, new GUIContent(a.label, a.tip))) a.run();
        }
        y += ((actions.Count + 1) / 2) * 28 + 2;

        if (usedBy != null && usedByFor == selected.id)
        {
            if (usedBy.Count == 0) { GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "Nothing in the game points at it.", dim); y += 18; }
            int shownCount = Mathf.Min(usedBy.Count, 6);
            for (int i = 0; i < shownCount; i++)
            {
                var u = usedBy[i];
                string label = (u.entry != null ? u.entry.name : System.IO.Path.GetFileNameWithoutExtension(u.path)) + "  <color=#a9a49a>" + System.IO.Path.GetExtension(u.path) + "</color>";
                if (GUI.Button(new Rect(r.x + 8, y, r.width - 16, 20), label, cell))
                {
                    if (u.entry != null) ShowEntry(u.entry);
                    else { var o = AssetDatabase.LoadMainAssetAtPath(u.path); Selection.activeObject = o; EditorGUIUtility.PingObject(o); }
                }
                y += 21;
            }
            if (usedBy.Count > shownCount) { GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), $"... and {usedBy.Count - shownCount} more", dim); y += 18; }
            y += 4;
        }
        return y;
    }

    // ── Requests to Main (the outbox) ─────────────────────────────────

    void RefreshRequests()
    {
        requestList = DevReportStore.List(200);
        nextRequestRefresh = EditorApplication.timeSinceStartup + 3;
        if (request != null) request = requestList.FirstOrDefault(q => q.id == request.id) ?? request;
    }

    void DrawRequestList(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        if (EditorApplication.timeSinceStartup >= nextRequestRefresh) RefreshRequests();
        GUI.Label(new Rect(r.x + 8, r.y + 8, r.width - 16, 40), "What you asked Main for, from the game (F9) and from here, newest first. Main answers with a status and a note.", dim);
        const float rowH = 52;
        var area = new Rect(r.x, r.y + 52, r.width, r.height - 52);
        var view = new Rect(0, 0, r.width - 16, Mathf.Max(requestList.Count * rowH, area.height - 2));
        listScroll = GUI.BeginScrollView(area, listScroll, view);
        for (int i = 0; i < requestList.Count; i++)
        {
            var q = requestList[i];
            var row = new Rect(2, i * rowH, view.width - 2, rowH - 2);
            if (request != null && q.id == request.id) GUI.Box(row, GUIContent.none, cellSelected);
            string colour = q.status switch { DevReportStore.Status.Done => "#8fd18b", DevReportStore.Status.Failed => "#ff8a7a", DevReportStore.Status.Saved => "#c9c2b8", _ => "#ffd27a" };
            string first = q.message.Split('\n')[0];
            GUI.Label(new Rect(row.x + 6, row.y + 3, row.width - 12, 18), $"<color={colour}>{DevReportStore.ShortStatus(q)}</color>  <color=#a9a49a>{q.created.ToLocalTime():d MMM HH:mm}{(q.assetName != null && q.assetName.Length > 0 ? " · " + q.assetName : "")}</color>", cell);
            GUI.Label(new Rect(row.x + 6, row.y + 22, row.width - 12, 26), first, dim);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)) { PickRequest(q); Event.current.Use(); }
        }
        if (requestList.Count == 0) GUI.Label(new Rect(10, 10, view.width - 20, 40), "No requests yet. Press F9 in the game, or Send to Main on an asset.", dim);
        GUI.EndScrollView();
    }

    void PickRequest(DevReportStore.Entry q)
    {
        request = q;
        if (requestShot != null) DestroyImmediate(requestShot);
        requestShot = null;
        string png = System.IO.Path.Combine(q.folder, "screenshot.png");
        if (System.IO.File.Exists(png)) { requestShot = new Texture2D(2, 2) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave }; requestShot.LoadImage(System.IO.File.ReadAllBytes(png)); }
        Repaint();
    }

    void DrawRequest(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        if (request == null) { GUI.Label(new Rect(r.x + 16, r.y + 16, r.width - 32, 40), "Pick a request on the left.", dim); return; }
        GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 24, 30), "Request " + request.id.Substring(0, 8), titleStyle);
        GUI.Label(new Rect(r.x + 12, r.y + 36, r.width - 24, 16), $"{request.created.ToLocalTime():d MMM yyyy HH:mm} · {DevReportStore.ShortStatus(request)}", dim);
        var stage = new Rect(r.x + 8, r.y + 58, r.width - 16, r.height - 66);
        EditorGUI.DrawRect(stage, new Color(.09f, .09f, .11f));
        if (requestShot != null) GUI.DrawTexture(stage, requestShot, ScaleMode.ScaleToFit);
        else GUI.Label(new Rect(stage.x + 12, stage.y + 12, stage.width, 20), "No screenshot with this one.", dim);
    }

    void DrawRequestDetails(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        if (request == null) return;
        float y = r.y + 8;
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "WHAT YOU ASKED", gold); y += 18;
        var wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
        float h = wrap.CalcHeight(new GUIContent(request.message), r.width - 16);
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, h), request.message, wrap); y += h + 8;
        if (!string.IsNullOrEmpty(request.assetName)) { GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "About: " + request.assetName, dim); y += 18; }
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "MAIN", gold); y += 18;
        string note = string.IsNullOrEmpty(request.note) ? (request.status == DevReportStore.Status.Saved ? "Not picked up yet." : "No note yet.") : request.note;
        h = wrap.CalcHeight(new GUIContent(note), r.width - 16);
        GUI.Label(new Rect(r.x + 8, y, r.width - 16, h), note, wrap); y += h + 10;
        float bw = (r.width - 20) / 2;
        if (GUI.Button(new Rect(r.x + 8, y, bw, 24), "Open its folder")) EditorUtility.RevealInFinder(System.IO.Path.Combine(request.folder, "report.json"));
        if (GUI.Button(new Rect(r.x + 12 + bw, y, bw, 24), "Copy its ID")) { EditorGUIUtility.systemCopyBuffer = request.id; Toast("Copied " + request.id); }
        y += 30;
        string state = System.IO.Path.Combine(request.folder, "game-state.json");
        if (System.IO.File.Exists(state))
        {
            var written = System.IO.File.GetLastWriteTimeUtc(state);
            if (stateFor != state || stateWritten != written) { stateFor = state; stateWritten = written; stateText = System.IO.File.ReadAllText(state); }
            GUI.Label(new Rect(r.x + 8, y, r.width - 16, 16), "GAME STATE", gold); y += 18;
            var area = new Rect(r.x + 4, y, r.width - 8, r.yMax - y - 4);
            GUILayout.BeginArea(area);
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            EditorGUILayout.SelectableLabel(stateText, EditorStyles.textArea, GUILayout.ExpandHeight(true), GUILayout.MinHeight(area.height - 10));
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }

    void AskMain()
    {
        var problem = DevReportStore.Validate(askText);
        if (problem != null) { Toast(problem); return; }
        try
        {
            var png = preview.CapturePng();
            var state = $"{{\n  \"schema_version\": 1,\n  \"context\": \"studio\",\n  \"selected\": {{ \"id\": \"{selected.id}\", \"name\": {Json(selected.name)}, \"category\": {Json(selected.category)}, \"kind\": {Json(selected.kind)}, \"path\": {Json(selected.path)} }}\n}}";
            var id = DevReportStore.Save(askText, png, state, "studio", new DevReportStore.AssetRef { id = selected.id, name = selected.name, path = selected.path, category = selected.category });
            askText = "";
            GUI.FocusControl(null);
            Toast($"Saved for Main ({id.Substring(0, 8)}). It shows under Requests to Main.");
            RefreshRequests();
        }
        catch (Exception ex) { Toast("Could not save: " + ex.Message); }
    }

    static string Json(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    void HandleKeys()
    {
        var e = Event.current;
        if (showingWorld || showingDialogue || showingDungeon || showingSubstats) return;   // the World's tree and the Dungeon's pages are clicked, not stepped through; Dialogue is typed in
        if (!EditorGUIUtility.editingTextField && preview != null && preview.HandlePaintKey(e)) return;
        if (e.type != EventType.KeyDown || GUI.GetNameOfFocusedControl() == "search" && e.keyCode != KeyCode.DownArrow && e.keyCode != KeyCode.UpArrow) return;
        if (EditorGUIUtility.editingTextField && GUI.GetNameOfFocusedControl() != "search") return;
        int i = shown.IndexOf(selected);
        if (e.keyCode == KeyCode.DownArrow && shown.Count > 0) { Select(shown[Mathf.Min(shown.Count - 1, i + 1)]); ScrollTo(shown.IndexOf(selected)); e.Use(); }
        else if (e.keyCode == KeyCode.UpArrow && shown.Count > 0) { Select(shown[Mathf.Max(0, i - 1)]); ScrollTo(shown.IndexOf(selected)); e.Use(); }
        else if (e.keyCode == KeyCode.F) { preview.Frame(); e.Use(); }
        else if (e.keyCode == KeyCode.Space) { preview.TogglePlay(); e.Use(); }
    }

    void ScrollTo(int index)
    {
        const float rowH = 44;
        float y = index * rowH;
        if (y < listScroll.y) listScroll.y = y;
        else if (y + rowH > listScroll.y + position.height - 140) listScroll.y = y + rowH - (position.height - 140);
    }

    // For tests (an eval): pick a category and an entry by name.
    public void TestShow(string category, string name)
    {
        categoryIndex = Mathf.Max(0, categories.FindIndex(c => c.name == category));
        search = ""; Filter();
        Select(shown.FirstOrDefault(e => e.name == name) ?? shown.FirstOrDefault());
    }
    public string TestSummary() => string.Join(", ", categories.Select(c => $"{c.name} {c.entries.Count}"));
    public AssetStudioPreview TestPreview => preview;
    public void TestAsk(string text) { askText = text; AskMain(); }
    public int TestWhereUsed() { usedBy = AssetStudioUsage.Find(selected.guid, categories); usedByFor = selected.id; return usedBy.Count; }
    public string TestFirstUse() => usedBy != null && usedBy.Count > 0 ? usedBy[0].path : "";
    public int TestRecent() => recent.Count;
    public void TestKind(int k) { kindFilter = k; Filter(); }
    public int TestShownCount => shown.Count;
    public string[] TestKinds => kinds;
    public int TestEverything(string text) { studioView = StudioView.Everything; search = text; Filter(); return shown.Count; }
    public string TestSelectedPath => selected != null ? selected.path : "";
    public string TestInGame() { string r = ""; foreach (var c in categories) r += $"{c.name} {c.entries.Count(e => e.inGame)}/{c.entries.Count}, "; return r; }
    public string TestRequests() { studioView = StudioView.Requests; RefreshRequests(); if (requestList.Count > 0) PickRequest(requestList[0]); return requestList.Count + " requests, first " + (request != null ? request.message.Split('\n')[0] + " / " + request.note + " / shot " + (requestShot != null ? requestShot.width + "x" + requestShot.height : "none") : "-"); }
}

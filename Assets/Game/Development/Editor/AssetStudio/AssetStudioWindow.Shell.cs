using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The window's own frame (the user's UX review picks, 2026-11-15): what sits in the top bar and how the columns behave,
// the same in every view.
//   Back and Forward: every jump (a name clicked in a table, Show..., Where used, a search result, a sidebar row) is a
//   step on a trail; Back returns to where you were, its pick and its scroll (Alt+Left, Alt+Right, the mouse's side
//   buttons). A place is a view and what is open in it (a category and its pick, a Dungeon page, a World pick, a tab),
//   not every click on a page.
//   Find anything: one search over the library's entries, the views and their pages, and the settings by name ("tome
//   share" finds the Loot page, "tavern" the tavern page and the keeper). Ctrl+F puts the cursor in it; the arrows and
//   Enter pick, Escape closes. The settings are a hand-kept list (Settings below): a new page or a setting moved to
//   another page is one line there.
//   Unsaved scene: assets save at once, but what lives on the room's managers (the Stats tracks, the quests, Balance's
//   managers, Tome Share, lines on scene objects) is kept only when the scene is saved. One chip says so, in every view,
//   with Save on it; the views no longer each say it their own way.
//   Columns: the list's and the inspector's edges are dragged, and the inspector folds away (its edge button; folded by
//   itself in a window under 1500 wide until you open it), so the page in the middle keeps its room on a small screen.
public partial class AssetStudioWindow
{
    // ── Columns ──

    [SerializeField] float listWidth = 320, inspectorWidth = 500;
    [SerializeField] int inspectorFold;   // 0: by the window's width, 1: open, 2: folded
    const float FoldedWidth = 28, MinList = 240, MaxList = 560, MinInspector = 340, MaxInspector = 820, MinMiddle = 420, FoldBelow = 1500;
    bool InspectorFolded => inspectorFold == 2 || inspectorFold == 0 && position.width < FoldBelow;
    float ListWidth => Mathf.Clamp(listWidth, MinList, MaxList);
    float InspectorWidth => InspectorFolded ? FoldedWidth
        : Mathf.Clamp(inspectorWidth, MinInspector, Mathf.Max(MinInspector, Mathf.Min(MaxInspector, position.width - CategoryWidth - ListWidth - MinMiddle - Pad * 5)));

    // The gap between two columns, dragged: sign +1 widens the column on its left, -1 the one on its right.
    void Splitter(Rect gap, ref float width, int sign, float min, float max)
    {
        EditorGUIUtility.AddCursorRect(gap, MouseCursor.ResizeHorizontal);
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (e.button == 0 && gap.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id) { width = Mathf.Clamp(width + e.delta.x * sign, min, max); e.Use(); Repaint(); }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
            case EventType.Repaint:
                if (GUIUtility.hotControl == id || gap.Contains(e.mousePosition)) EditorGUI.DrawRect(new Rect(gap.center.x - 1, gap.y + 8, 2, gap.height - 16), new Color(StudioSkin.Gold.r, StudioSkin.Gold.g, StudioSkin.Gold.b, .55f));
                break;
        }
    }

    // The inspector folded away: a strip with a button that opens it.
    void DrawFoldedInspector(Rect r)
    {
        GUI.Box(r, GUIContent.none);
        if (GUI.Button(new Rect(r.x + 3, r.y + 6, r.width - 6, 26), new GUIContent("«", "Show the details and the inspector."))) { inspectorFold = 1; Repaint(); }
    }

    // The button on the open inspector's top right corner that folds it away.
    void FoldButton(Rect r)
    {
        if (GUI.Button(new Rect(r.xMax - 34, r.y + 6, 26, 22), new GUIContent("»", "Fold the details and the inspector away: more room for the page."))) { inspectorFold = 2; Repaint(); }
    }

    // ── The unsaved scene ──

    // Right of where: the chip and its Save button, when the open scene has unsaved changes. Returns the x left of it.
    float SaveChip(Rect row, float right)
    {
        if (EditorApplication.isPlaying) return right;
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isDirty) return right;
        const float buttonWidth = 74;
        right -= buttonWidth;
        if (GUI.Button(new Rect(right, row.y + 8, buttonWidth, 36), new GUIContent("Save", $"Save {scene.name}.unity. What you change on the room's managers (stats, quests, balance, lines on scene objects) is kept only when the scene is saved; assets save at once.")))
        {
            CommitDialogue(true);
            Toast(EditorSceneManager.SaveScene(scene) ? $"Saved {scene.name}." : $"Could not save {scene.name}.");
        }
        right -= 8;
        var text = new GUIContent($"<color=#e0a85a>●</color>  {scene.name} scene: unsaved changes");
        var style = new GUIStyle(StudioSkin.Label) { richText = true, wordWrap = false, alignment = TextAnchor.MiddleRight };
        float w = style.CalcSize(text).x + 4;
        right -= w;
        GUI.Label(new Rect(right, row.y + 8, w, 36), text, style);
        return right - 14;
    }

    // ── Back and Forward ──

    class Place
    {
        public StudioView view;
        public int category, dialogueTab, unlockMode;
        public string selectedId, dungeonPath, dungeonPage, worldKey, unlockPicked, balanceTab;
        public UnityEngine.Object inspected;
        public Vector2 scroll;
        public string Key;
    }
    readonly List<Place> trail = new();
    int trailAt = -1, trailHold;

    Place Here()
    {
        var p = new Place { view = studioView, selectedId = selected?.id, inspected = inspected };
        switch (studioView)
        {
            case StudioView.Category: p.category = categoryIndex; p.scroll = listScroll; p.Key = $"c|{categoryIndex}|{selected?.id}"; break;
            case StudioView.Dungeon: p.dungeonPath = dungeonPath; p.dungeonPage = dungeonPage; p.scroll = dungeonPageScroll; p.Key = $"d|{dungeonPath}|{dungeonPage}|{worldPane}"; break;
            case StudioView.World: p.worldKey = worldPickedKey; p.scroll = levelPageScroll; p.Key = $"w|{worldPickedKey}|{worldPane}"; break;
            case StudioView.Unlockables: p.unlockPicked = unlockPicked; p.unlockMode = unlockMode; p.scroll = unlockPageScroll; p.Key = $"u|{unlockPicked}|{unlockMode}"; break;
            case StudioView.Balance: p.balanceTab = balanceTab; p.scroll = balanceScroll; p.Key = "b|" + balanceTab; break;
            case StudioView.Dialogue: p.dialogueTab = dialogueTab; p.Key = $"t|{dialogueTab}|{(dialogueOpen != null ? dialogueOpen.GetEntityId().ToString() : "")}"; break;
            case StudioView.Stats: p.scroll = statsScroll; p.Key = "s"; break;
            case StudioView.Quests: p.scroll = questScroll; p.Key = "q"; break;
            default: p.Key = studioView.ToString(); break;
        }
        return p;
    }

    // Once a frame: a new place is a new step on the trail; the same place keeps its pick and scroll up to date.
    void TrackPlace()
    {
        if (Event.current.type != EventType.Layout) return;
        var here = Here();
        if (trailAt >= 0 && trailAt < trail.Count && (trail[trailAt].Key == here.Key || trailHold > 0))
        {
            // Just went Back or Forward: the place settles over a few frames (a World pick is made once its tree is drawn).
            if (trailHold > 0) trailHold--;
            trail[trailAt] = here;
            return;
        }
        if (trailAt < trail.Count - 1) trail.RemoveRange(trailAt + 1, trail.Count - trailAt - 1);
        trail.Add(here);
        if (trail.Count > 80) trail.RemoveAt(0);
        trailAt = trail.Count - 1;
    }

    bool CanGo(int step) => trailAt + step >= 0 && trailAt + step < trail.Count;

    void Go(int step)
    {
        if (!CanGo(step)) return;
        trailAt += step;
        var p = trail[trailAt];
        trailHold = 4;
        studioView = p.view;
        switch (p.view)
        {
            case StudioView.Category:
                categoryIndex = Mathf.Clamp(p.category, 0, categories.Count - 1); kindFilter = 0; search = ""; Filter();
                Select(p.selectedId != null ? Find(p.selectedId) : null);
                listScroll = p.scroll;
                break;
            case StudioView.Dungeon:
                dungeonPath = p.dungeonPath; dungeonPage = p.dungeonPage; worldPane = WorldPane.Picked; dungeonPageScroll = p.scroll;
                ShowAgain(p);
                break;
            case StudioView.World:
                worldPane = WorldPane.Picked; worldPickedKey = p.worldKey; worldRepick = p.worldKey; levelPageScroll = p.scroll;
                break;
            case StudioView.Unlockables: unlockPicked = p.unlockPicked; unlockMode = p.unlockMode; unlockPageScroll = p.scroll; ShowAgain(p); break;
            case StudioView.Balance: balanceTab = p.balanceTab; balanceScroll = p.scroll; break;
            case StudioView.Dialogue: dialogueTab = p.dialogueTab; FilterDialogue(); break;
            case StudioView.Stats: statsScroll = p.scroll; break;
            case StudioView.Quests: questScroll = p.scroll; break;
        }
        GUI.FocusControl(null);
        Repaint();
    }

    // What was shown on the right at that place.
    void ShowAgain(Place p)
    {
        var e = p.selectedId != null ? Find(p.selectedId) : null;
        if (e != null) Select(e);
        else if (p.inspected != null) { Select(null); inspected = p.inspected; }
    }

    // Alt+Left and Alt+Right, and the mouse's side buttons.
    void TrailKeys()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.alt && (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow)) { Go(e.keyCode == KeyCode.LeftArrow ? -1 : 1); e.Use(); }
        else if (e.type == EventType.MouseDown && (e.button == 3 || e.button == 4)) { Go(e.button == 3 ? -1 : 1); e.Use(); }
    }

    // ── Find anything ──

    class Found { public string title, where; public Action go; public int rank; }
    string findText = "";
    [NonSerialized] string foundFor;
    [NonSerialized] List<Found> found = new();
    int foundAt;
    bool findOpen;
    Rect findField, findPanel;
    const float FoundRow = 26;
    const int FoundMost = 14;

    // The settings and where each is edited. Kept by hand: a setting that moves to another page is one line here.
    IEnumerable<(string name, string words, string where, Action go)> Settings()
    {
        Action Page(string page) => () => { OpenDungeon(); GoTo(page); };
        Action Bal(string tab) => () => { OpenBalance(); balanceTab = tab; };
        yield return ("Tome share", "spell tomes share of gear drops", "Dungeon > Loot", Page("loot"));
        yield return ("Loot weight and tier of an item", "drop table items floors tomes weights", "Dungeon > Loot", Page("loot"));
        yield return ("Gear by floor", "tier bronze iron steel which gear drops on each floor", "Dungeon > Loot", Page("loot"));
        yield return ("Chest tiers", "wood iron gold chest supply how many rolls", "Dungeon > Loot", Page("loot"));
        yield return ("Starting chest", "first room chest potion consumable", "Dungeon > Loot", Page("loot"));
        yield return ("Tavern every", "which floors have a tavern merchant chance keeper", "Dungeon > The tavern", Page("tavern"));
        yield return ("Hearth heal", "rest by the fire health back", "Dungeon > The tavern", Page("tavern"));
        yield return ("Tavern food and prices", "dish stew broth price keeper wares rumours", "Dungeon > The tavern", Page("tavern"));
        yield return ("Enemies on a floor", "who lives where weight from floor spawn", "Dungeon > Who lives where", Page("enemies"));
        yield return ("Rooms a floor can build", "room pool hand-built templates how common weight", "Dungeon > Rooms", Page("rooms"));
        yield return ("Room events", "ambush shrine trap sleeping pack dark rooms pits chances", "Dungeon > Room events", Page("events"));
        yield return ("Fights and hand-built rooms per floor", "encounter chance template chance quiet rooms", "Dungeon > Room events", Page("events"));
        yield return ("Enemy health and damage growth", "per floor growth balance harder deeper", "Dungeon > Difficulty", Page("difficulty"));
        yield return ("Difficulty jump, Damage x and elites of a stage", "stage extra floors elite chance", "Dungeon > Difficulty", Page("difficulty"));
        yield return ("Boss health and damage", "guardian crypt lord multipliers boss fight beam volley phase two", "Dungeon > The boss", Page("boss"));
        yield return ("The floor being built", "reveal cascade map burns entrance slam lamps", "Dungeon > The way in", Page("entering"));
        yield return ("Starting room and floor plan", "first room layout rooms in a row map template door", "Dungeon > The way in", Page("entering"));
        yield return ("The room's light while a level is played", "bedroom lighting tint brightness board light default style", "Dungeon > Look", Page("look"));
        yield return ("Music of a floor", "background music volume ambience boss fight track tavern", "Dungeon > Music and sound", Page("sound"));
        yield return ("What the Dungeon Master says in a dungeon", "lines on the way in kill backstab new floor boss fight", "Dungeon > The Dungeon Master", Page("words"));
        yield return ("Armour: how much of a hit it stops", "armor curve common legendary set stops flat rule", "Balance > Armour", Bal("Armour"));
        yield return ("Weapon damage by tier", "sword mace damage spread crit speed base damage strength swing", "Balance > Weapons", Bal("Weapons"));
        yield return ("Critical hits", "crit chance damage most chance cap", "Balance > Weapons", Bal("Weapons"));
        yield return ("Rarity bonus and properties", "uncommon rare unique legendary base bonus item numbers by tier", "Balance > Items", Bal("Items"));
        yield return ("Hits to kill and hits to die", "enemy numbers by floor sword mace player health", "Balance > Enemies", Bal("Enemies"));
        yield return ("Every enemy's damage and health", "enemy base damage health whole game progression manager", "Balance > Enemies", Bal("Enemies"));
        yield return ("How often gear drops", "drop chance kill chest breakable multiplier enemy drops", "Balance > Drops", Bal("Drops"));
        yield return ("Rarity chances", "uncommon rare unique chance per floor rarity multiplier merchants", "Balance > Drops", Bal("Drops"));
        yield return ("Gold and potions from kills and chests", "gold amount recovery chance gold multiplier", "Balance > Drops", Bal("Drops"));
        yield return ("Spoils", "spoils chance kill chest lesser greater grand", "Balance > Drops", Bal("Drops"));
        yield return ("Attributes: what a point gives", "strength dexterity constitution intelligence base levels bonus", "Stats", OpenStats);
        yield return ("Skills: ranks, bonuses, milestones", "swords maces blocking sorcery thaumaturgy athletics stealth trading rank 100", "Stats", OpenStats);
        yield return ("What each class starts with", "health mana stamina starting ranks warrior wizard cleric necromancer rogue per level", "Stats", OpenStats);
        yield return ("The sheet's prices", "first level price multiplier spoils train cost", "Stats", OpenStats);
        yield return ("Substats on gear", "properties rolled weight value by floor item types rarity count", "Stats > Substats", OpenSubstats);
        yield return ("Quest texts", "quest log tasks intro guide", "Quests", OpenQuests);
        yield return ("Every Dungeon Master line", "dialogue lines mumble voice in person", "Dialogue", OpenDialogue);
        yield return ("Conversations", "boss talk tavern keeper boxes answers replies portrait", "Dialogue > Conversations", () => { OpenDialogue(); dialogueTab = 1; FilterDialogue(); });
        yield return ("House rules", "rule one two three four given quote", "Dialogue > House rules", () => { OpenDialogue(); dialogueTab = 3; FilterDialogue(); });
        yield return ("Places on the campaign map", "acts encounters shut until deaths rumour map props new level", "World", OpenWorld);
        yield return ("What a player unlocks", "classes places abilities trophies room items revive", "Unlockables", OpenUnlockables);
    }

    void BuildFound()
    {
        string q = findText.Trim().ToLowerInvariant();
        if (q == foundFor) return;
        foundFor = q;
        found = new List<Found>();
        foundAt = 0;
        if (q.Length < 2) return;
        var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        // 0: the name starts with what was typed, 1: a word of the name does, 2: the words are somewhere in it.
        int Rank(string name, string all)
        {
            string n = name.ToLowerInvariant();
            if (!words.All(w => all.Contains(w))) return -1;
            if (n.StartsWith(q)) return 0;
            return words.All(w => n.Contains(w)) ? 1 : 2;
        }
        // The Dungeon's pages (the picked dungeon's) and the views.
        var d = DungeonLevel;
        var l = d != null ? World.LevelOf(d) : null;
        void Page(string title, string words2, string where, Action go, int bias = 0)
        {
            int r = Rank(title, (title + " " + words2 + " " + where).ToLowerInvariant());
            if (r >= 0) found.Add(new Found { title = title, where = where, go = go, rank = r * 10 + bias });
        }
        if (l != null)
        {
            string name = DungeonLabel(d);
            foreach (var (key, title, extra) in new[] { ("timeline", "Floor 1 to the last", "the run every floor side by side"), ("overview", "Overview", "problems floors"), ("entering", "The way in", "map build first room"),
                ("tavern", "The tavern", "keeper food rumours hearth merchant shop"), ("boss", "The boss", "guardian fight"), ("enemies", "Who lives where", "enemies"), ("rooms", "Rooms", "room pool"),
                ("events", "Room events", "ambush shrine trap"), ("loot", "Loot", "drops chests gear tomes"), ("difficulty", "Difficulty", "balance health damage"), ("words", "The Dungeon Master", "lines dialogue"),
                ("sound", "Music and sound", "audio"), ("look", "Look", "light style") })
            { string k = key; Page(title, extra, "Dungeon · " + name, () => { OpenDungeon(); GoTo(k); }); }
            foreach (var st in l.RunStages.Where(s => s.biome != null))
            { string k = "floor:" + st.index; var b = st.biome; Page($"{FloorsLabel(st)} · {b.name}", b.displayName + " floor stage biome", "Dungeon · " + name, () => { OpenDungeon(); GoTo(k, b); }); }
        }
        foreach (var (name, words2, where, go) in Settings()) Page(name, words2, "Setting · " + where, go, 1);
        // The library.
        foreach (var c in categories)
            foreach (var e in c.entries)
            {
                int r = Rank(e.name, e.Search);
                if (r < 0) continue;
                var entry = e;
                found.Add(new Found { title = e.name, where = (c.hidden ? "" : c.name + " · ") + e.kind, go = () => ShowEntry(entry), rank = r * 10 + 2 });
            }
        found = found.OrderBy(f => f.rank).ThenBy(f => f.title.Length).ThenBy(f => f.title, StringComparer.OrdinalIgnoreCase).Take(FoundMost).ToList();
    }

    void Pick(Found f)
    {
        findOpen = false; findText = ""; foundFor = null; found = new List<Found>();
        GUI.FocusControl(null);
        f.go();
        Repaint();
    }

    // Before anything else is drawn: the keys and clicks that belong to the search and its list, so nothing under the
    // list takes them.
    void FindEvents()
    {
        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.control || e.command) && e.keyCode == KeyCode.F) { EditorGUI.FocusTextInControl("find"); findOpen = true; e.Use(); return; }
        bool typing = GUI.GetNameOfFocusedControl() == "find";
        if (typing && !findOpen && findText.Length > 0) findOpen = true;
        if (!findOpen) return;
        BuildFound();
        if (e.type == EventType.KeyDown && typing)
        {
            if (e.keyCode == KeyCode.DownArrow) { foundAt = Mathf.Min(found.Count - 1, foundAt + 1); e.Use(); }
            else if (e.keyCode == KeyCode.UpArrow) { foundAt = Mathf.Max(0, foundAt - 1); e.Use(); }
            else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && foundAt >= 0 && foundAt < found.Count) { var f = found[foundAt]; e.Use(); Pick(f); }
            else if (e.keyCode == KeyCode.Escape) { findOpen = false; findText = ""; foundFor = null; GUI.FocusControl(null); e.Use(); }
        }
        else if (e.type == EventType.MouseDown)
        {
            if (findPanel.Contains(e.mousePosition) && found.Count > 0)
            {
                int row = Mathf.FloorToInt((e.mousePosition.y - findPanel.y - 6) / FoundRow);
                e.Use();
                if (row >= 0 && row < found.Count) Pick(found[row]);
            }
            else if (!findField.Contains(e.mousePosition)) findOpen = false;
        }
        else if (e.type == EventType.MouseMove && findPanel.Contains(e.mousePosition))
        {
            int row = Mathf.FloorToInt((e.mousePosition.y - findPanel.y - 6) / FoundRow);
            if (row >= 0 && row < found.Count && row != foundAt) { foundAt = row; Repaint(); }
        }
    }

    // The field, in the top bar.
    void FindField(Rect r)
    {
        findField = r;
        GUI.SetNextControlName("find");
        string typed = EditorGUI.TextField(r, findText, EditorStyles.toolbarSearchField);
        if (typed != findText) { findText = typed; findOpen = true; }
        if (findText.Length == 0 && GUI.GetNameOfFocusedControl() != "find")
            GUI.Label(new Rect(r.x + 22, r.y, r.width - 24, r.height), "Find anything: an item, a page, a setting   (Ctrl+F)", new GUIStyle(dimLine) { alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(.55f, .53f, .5f) } });
    }

    // Last of all, over the columns: what was found.
    void FindList()
    {
        if (!findOpen || findText.Trim().Length < 2) { findPanel = default; return; }
        BuildFound();
        int rows = Mathf.Max(1, found.Count);
        findPanel = new Rect(findField.x, findField.yMax + 4, Mathf.Max(findField.width, 620), rows * FoundRow + 12);
        GUI.Box(findPanel, GUIContent.none);
        EditorGUI.DrawRect(new Rect(findPanel.x + 5, findPanel.y + 5, findPanel.width - 10, findPanel.height - 10), new Color(.11f, .11f, .125f));
        if (found.Count == 0) { GUI.Label(new Rect(findPanel.x + 14, findPanel.y + 6, findPanel.width - 28, FoundRow), "Nothing found.", dim); return; }
        var whereStyle = new GUIStyle(dimLine) { alignment = TextAnchor.MiddleRight };
        var titleLine = new GUIStyle(dimLine) { alignment = TextAnchor.MiddleLeft, normal = { textColor = StudioSkin.Text } };
        for (int i = 0; i < found.Count; i++)
        {
            var row = new Rect(findPanel.x + 6, findPanel.y + 6 + i * FoundRow, findPanel.width - 12, FoundRow);
            if (i == foundAt) GUI.Box(row, GUIContent.none, cellSelected);
            float whereWidth = Mathf.Min(280, whereStyle.CalcSize(new GUIContent(found[i].where)).x + 6);
            GUI.Label(new Rect(row.x + 10, row.y, row.width - whereWidth - 24, row.height), Fit(found[i].title, titleLine, row.width - whereWidth - 24), titleLine);
            GUI.Label(new Rect(row.xMax - whereWidth - 8, row.y, whereWidth, row.height), Fit(found[i].where, whereStyle, whereWidth), whereStyle);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Asset Studio's Dialogue view: every line of dialogue in the game in one list (AssetStudioDialogue finds them: the
// Dungeon Master's lines, the conversations in Dialogue/Conversations with their speakers, answers and replies set in
// under their box, and the Dialogue Database's talks), grouped by where it plays and in the order it is said, edited
// right there. Tabs along the top split them by kind (the Dungeon Master's lines, the conversations in dialogue boxes,
// the talks at the table, the house rules, and all of them; the user, 2026-11-11), each with its own search, filter
// (where it plays, which speaker or which talk), sort, empty lines only and place in the list; each line can be moved, added after or removed in its list, said in Play mode, or its
// owner selected; CSV out and in for editing many in a spreadsheet. Edits land where the line lives (undoable); a
// line in the open scene leaves the scene unsaved until Save scene.
public partial class AssetStudioWindow
{
    List<AssetStudioDialogue.Line> dialogue;
    List<AssetStudioDialogue.Line> dialogueShown = new();
    // ── Tabs: one per kind of dialogue, each keeping its own search, filter, sort and scroll ──
    enum DialogueKindTab { DungeonMaster, Conversations, TableTalks, HouseRules, All }
    static readonly string[] DialogueTabNames = { "Dungeon Master", "Conversations", "Table talks", "House rules", "All" };
    static readonly string[] DialogueTabTips =
    {
        "What the Dungeon Master says in his feed: remarks, the intro, level and room lines, his lines in fights.",
        "Dialogue boxes with a speaker, answers and replies: a boss's intro, the tavern keeper (Dialogue/Conversations).",
        "The talks with the Dungeon Master at the table and in the room (the Dialogue Database).",
        "The house rules on the door, and his lines giving and quoting them.",
        "Every line of dialogue in the game.",
    };
    int dialogueTab;
    [System.NonSerialized] string[] dialogueSearches;
    [System.NonSerialized] int[] dialogueGroups, dialogueSortBy;
    [System.NonSerialized] bool[] dialogueEmpties;
    [System.NonSerialized] Vector2[] dialogueScrolls;
    [System.NonSerialized] List<string> dialogueGroupKeys = new();   // the current tab's filter choices (0 = every one)
    // Conversations are listed one per data asset (a boss's, the tavern keeper's); the one opened is shown line by line.
    [System.NonSerialized] UnityEngine.Object dialogueOpen;
    bool ConversationList => (DialogueKindTab)dialogueTab == DialogueKindTab.Conversations && dialogueOpen == null;
    // A tab's size: lines, or in Conversations the conversations.
    int TabCount(int t) => t == (int)DialogueKindTab.Conversations
        ? dialogue.Where(l => InTab(l, t)).Select(l => l.owner).Distinct().Count()
        : dialogue.Count(l => InTab(l, t));
    void DialogueTabState()
    {
        int n = DialogueTabNames.Length;
        dialogueGroupKeys ??= new List<string>();
        if (dialogueSearches != null && dialogueSearches.Length == n) return;
        dialogueSearches = Enumerable.Repeat("", n).ToArray(); dialogueGroups = new int[n]; dialogueSortBy = new int[n];
        dialogueEmpties = new bool[n]; dialogueScrolls = new Vector2[n];
        dialogueTab = Mathf.Clamp(dialogueTab, 0, n - 1);
    }
    string dialogueSearch { get { DialogueTabState(); return dialogueSearches[dialogueTab]; } set { DialogueTabState(); dialogueSearches[dialogueTab] = value; } }
    int dialogueGroup { get { DialogueTabState(); return dialogueGroups[dialogueTab]; } set { DialogueTabState(); dialogueGroups[dialogueTab] = value; } }
    int dialogueSort { get { DialogueTabState(); return dialogueSortBy[dialogueTab]; } set { DialogueTabState(); dialogueSortBy[dialogueTab] = value; } }
    bool dialogueEmptyOnly { get { DialogueTabState(); return dialogueEmpties[dialogueTab]; } set { DialogueTabState(); dialogueEmpties[dialogueTab] = value; } }
    Vector2 dialogueScroll { get { DialogueTabState(); return dialogueScrolls[dialogueTab]; } set { DialogueTabState(); dialogueScrolls[dialogueTab] = value; } }

    // Which tab a line belongs to (All holds every line).
    static DialogueKindTab TabOf(AssetStudioDialogue.Line l) =>
        l.owner is PixelCrushers.DialogueSystem.DialogueDatabase ? DialogueKindTab.TableTalks
        : l.owner is HouseRule || l.owner is RuleManager ? DialogueKindTab.HouseRules
        : l.Conversation || l.owner is BossDialogueData || l.owner is TavernKeeperDialogueData ? DialogueKindTab.Conversations
        : DialogueKindTab.DungeonMaster;
    static bool InTab(AssetStudioDialogue.Line l, int tab) => tab == (int)DialogueKindTab.All || (int)TabOf(l) == tab;
    bool ByOwnList => (DialogueKindTab)dialogueTab is DialogueKindTab.Conversations or DialogueKindTab.TableTalks;
    // What the tab's filter picks by: the speaker in Conversations, the talk in Table talks, where it plays otherwise.
    string GroupKey(AssetStudioDialogue.Line l) => (DialogueKindTab)dialogueTab switch
    {
        DialogueKindTab.Conversations => l.ownerName,
        DialogueKindTab.TableTalks => l.field,
        _ => l.category,
    };
    string GroupAll => (DialogueKindTab)dialogueTab switch { DialogueKindTab.Conversations => "Every speaker", DialogueKindTab.TableTalks => "Every talk", _ => "Everywhere" };
    // Typed text not written back yet, by line (LineId: its owner and property path, so it outlives reading the owner's
    // lines again after a change elsewhere).
    readonly Dictionary<string, string> dialoguePending = new();
    float dialogueWidth;
    readonly List<float> dialogueHeights = new();
    static readonly string[] DialogueSorts = { "Where it plays", "Text A-Z", "Longest first", "Category" };
    // Not kept across a recompile (the window keeps its serializable fields): made again with the current look.
    [System.NonSerialized] GUIStyle dialogueArea, dialogueHeader, dialogueWhen, dialogueSmall;

    void OpenDialogue()
    {
        studioView = StudioView.Dialogue;
        if (dialogue == null) RescanDialogue();
        GUI.FocusControl(null);
    }

    void RescanDialogue()
    {
        CommitDialogue(true);
        dialogue = AssetStudioDialogue.Scan();
        FilterDialogue();
    }

    void FilterDialogue()
    {
        DialogueTabState();
        IEnumerable<AssetStudioDialogue.Line> q = (dialogue ?? new List<AssetStudioDialogue.Line>()).Where(l => InTab(l, dialogueTab)).ToList();
        // An open conversation: its own lines only.
        if ((DialogueKindTab)dialogueTab == DialogueKindTab.Conversations && dialogueOpen != null) q = q.Where(l => l.owner == dialogueOpen).ToList();
        // The filter's choices: the tab's own speakers or talks as found, or its places in the game's order.
        var keys = q.Select(GroupKey).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        if (!ByOwnList) keys = keys.OrderBy(k => { int i = System.Array.IndexOf(AssetStudioDialogue.CategoryOrder, k); return i < 0 ? 999 : i; }).ToList();
        string picked = dialogueGroup > 0 && dialogueGroup - 1 < dialogueGroupKeys.Count ? dialogueGroupKeys[dialogueGroup - 1] : null;
        dialogueGroupKeys = keys;
        dialogueGroup = picked != null && keys.Contains(picked) ? keys.IndexOf(picked) + 1 : 0;
        if (dialogueGroup > 0) { string g = keys[dialogueGroup - 1]; q = q.Where(l => GroupKey(l) == g); }
        if (dialogueEmptyOnly) q = q.Where(l => string.IsNullOrWhiteSpace(l.text));
        if (!string.IsNullOrWhiteSpace(dialogueSearch))
        {
            var words = dialogueSearch.ToLowerInvariant().Split(' ').Where(w => w.Length > 0).ToArray();
            q = q.Where(l => { string s = ((l.text ?? "") + " " + l.Where + " " + l.category + " " + (l.when ?? "")).ToLowerInvariant(); return words.All(w => s.Contains(w)); });
        }
        int Cat(AssetStudioDialogue.Line l) => System.Array.IndexOf(AssetStudioDialogue.CategoryOrder, l.category);
        q = dialogueSort switch
        {
            1 => q.OrderBy(l => l.text ?? "", System.StringComparer.OrdinalIgnoreCase),
            2 => q.OrderByDescending(l => (l.text ?? "").Length),
            3 => q.OrderBy(Cat).ThenBy(l => l.text ?? "", System.StringComparer.OrdinalIgnoreCase),
            _ => q.OrderBy(Cat).ThenBy(l => l.source).ThenBy(l => l.ownerName).ThenBy(l => l.order),
        };
        dialogueShown = q.ToList();
        dialogueWidth = -1;   // heights again
    }

    // Typed text is written back when the line loses focus, and all of it when the view, the window's focus or the
    // filter changes (all), not on every key.
    void CommitDialogue(bool all)
    {
        if (dialoguePending.Count == 0) return;
        string focused = all ? null : GUI.GetNameOfFocusedControl();
        foreach (var kv in dialoguePending.ToList())
        {
            if (!all && focused == "dlg " + kv.Key) continue;
            var line = dialogue?.FirstOrDefault(l => LineId(l) == kv.Key);
            if (line != null && line.owner != null && kv.Value != line.text) AssetStudioDialogue.SetText(line, kv.Value);
            dialoguePending.Remove(kv.Key);
            dialogueWidth = -1;
        }
    }
    static string LineId(AssetStudioDialogue.Line l) => (l.owner != null ? l.owner.GetHashCode().ToString() : "") + "|" + l.path;
    static string DialogueControl(AssetStudioDialogue.Line l) => "dlg " + LineId(l);

    // Lines read again for the owners that changed elsewhere (an inspector, Ctrl+Z, a saved asset): all, or those whose
    // asset is in paths, and the open scenes' when scenes. Scene objects gone (a scene closed or opened again, Play
    // started or stopped) take their lines with them, and the open scenes are looked through again for the objects now
    // there.
    void RecollectDialogue(bool all, ICollection<string> paths, bool scenes)
    {
        if (dialogue == null) return;
        var owners = dialogue.Select(l => l.owner).Distinct().Where(o => all || o == null
            || (EditorUtility.IsPersistent(o) ? paths != null && paths.Contains(AssetDatabase.GetAssetPath(o)) : scenes)).ToList();
        bool lost = owners.Any(o => o == null);
        if (owners.Count == 0 && !scenes) return;
        foreach (var owner in owners)
        {
            dialogue.RemoveAll(l => l.owner == owner);
            if (owner != null) AssetStudioDialogue.Collect(owner, dialogue);
        }
        if (scenes || lost) AssetStudioDialogue.CollectScenes(dialogue);
        FilterDialogue();
    }

    void DialogueStyles()
    {
        if (dialogueArea != null) return;
        dialogueArea = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13, padding = new RectOffset(6, 6, 5, 5) };
        dialogueArea.normal.textColor = dialogueArea.focused.textColor = dialogueArea.hover.textColor = new Color(.93f, .91f, .87f);
        dialogueHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, richText = true };
        dialogueWhen = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, richText = true, normal = { textColor = new Color(.66f, .64f, .6f) } };
        dialogueSmall = new GUIStyle(EditorStyles.miniButton) { fontSize = 11, padding = new RectOffset(2, 2, 1, 1) };
    }

    const float DialogueButtons = 310;   // the controls at the right of each line
    // PortraitSpeaker, by number. Character (PortraitSpeaker.Other): anyone else, shown by the box's own model (the tavern keeper).
    static readonly GUIContent[] Speakers = { new("Boss", "The fight's boss, in its box."), new("Dungeon Master", "Said in his own feed, not in the box."), new("Character", "Anyone else (the tavern keeper, a dungeon NPC): the box shows the model set on it.") };

    // A conversation's answers sit in under their box and the replies under their answer.
    static float Indent(AssetStudioDialogue.Line l) => l.Conversation ? l.depth * 26 : 0;
    static string RowLabel(AssetStudioDialogue.Line l) => l.kind switch
    {
        AssetStudioDialogue.Kind.Answer => "ans",
        AssetStudioDialogue.Kind.Box when l.depth > 0 => "reply",
        _ => l.index >= 0 ? (l.index + 1) + "." : "–",
    };

    // Conversations as a list, one per data asset: its name, kind, size and first words; a click opens it.
    void DrawConversationList(Rect r)
    {
        var bar = new Rect(r.x + 8, r.y + 42, r.width - 16, 22);
        GUI.SetNextControlName("dialogue search");
        string s = EditorGUI.TextField(new Rect(bar.x, bar.y, 260, 20), dialogueSearch, EditorStyles.toolbarSearchField);
        if (s != dialogueSearch) { dialogueSearch = s; dialogueScroll = Vector2.zero; }
        var words = (dialogueSearch ?? "").ToLowerInvariant().Split(' ').Where(w => w.Length > 0).ToArray();
        var groups = dialogue.Where(l => InTab(l, (int)DialogueKindTab.Conversations)).GroupBy(l => l.owner)
            .Where(g => words.Length == 0 || words.All(w => (g.Key.name + " " + string.Join(" ", g.Select(l => l.text))).ToLowerInvariant().Contains(w)))
            .OrderBy(g => g.Key.name).ToList();
        GUI.Label(new Rect(bar.x, bar.y + 24, bar.width, 18), $"{groups.Count} conversations · click one to open it · each is one asset in Dialogue/Conversations", dim);
        var list = new Rect(r.x + 4, r.y + 86, r.width - 8, r.height - 90);
        const float rowH = 64;
        dialogueScroll = GUI.BeginScrollView(list, dialogueScroll, new Rect(0, 0, list.width - 16, groups.Count * (rowH + 6) + 8));
        float y = 4;
        foreach (var g in groups)
        {
            var row = new Rect(6, y, list.width - 30, rowH);
            bool hover = row.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(row, hover ? new Color(.2f, .19f, .17f) : new Color(.15f, .15f, .16f));
            EditorGUI.DrawRect(new Rect(row.x, row.yMax - 1, row.width, 1), new Color(.32f, .31f, .29f));
            string kind = g.Key is BossDialogueData ? "Boss" : g.Key is TavernKeeperDialogueData ? "Tavern keeper" : g.Key.GetType().Name;
            int boxes = g.Count(l => l.kind == AssetStudioDialogue.Kind.Box), answers = g.Count(l => l.kind == AssetStudioDialogue.Kind.Answer);
            string first = g.FirstOrDefault(l => l.kind == AssetStudioDialogue.Kind.Box && !string.IsNullOrWhiteSpace(l.text))?.text
                ?? g.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.text))?.text ?? "";
            GUI.Label(new Rect(row.x + 10, row.y + 6, row.width - 20, 20), $"<color=#e8c46a>{g.Key.name}</color>", dialogueHeader);
            GUI.Label(new Rect(row.x + 10, row.y + 25, row.width - 20, 16), $"{kind} · {g.Count()} lines" + (boxes > 0 ? $" · {boxes} boxes" : "") + (answers > 0 ? $" · {answers} answers" : ""), dialogueWhen);
            GUI.Label(new Rect(row.x + 10, row.y + 42, row.width - 20, 18), first.Length > 140 ? first.Substring(0, 140) + "…" : first, dim);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                dialogueOpen = g.Key; dialogueScrolls[dialogueTab] = Vector2.zero; FilterDialogue(); Event.current.Use(); GUI.FocusControl(null);
            }
            y += rowH + 6;
        }
        GUI.EndScrollView();
        if (Event.current.type == EventType.MouseMove) Repaint();
    }

    // The view: the area right of the library column.
    void DrawDialogue(Rect r)
    {
        DialogueStyles();
        if (dialogue == null) RescanDialogue();
        CommitDialogue(false);
        GUI.Box(r, GUIContent.none);

        // ── Tabs: one per kind of dialogue ──
        DialogueTabState();
        float tx = r.x + 8;
        for (int t = 0; t < DialogueTabNames.Length; t++)
        {
            int count = TabCount(t);
            var label = new GUIContent($"{DialogueTabNames[t]}  {count}", DialogueTabTips[t]);
            float w = StudioSkin.Tab.CalcSize(label).x + 18;
            if (GUI.Toggle(new Rect(tx, r.y + 8, w, 26), dialogueTab == t, label, StudioSkin.Tab) && dialogueTab != t)
            { CommitDialogue(true); dialogueTab = t; FilterDialogue(); GUI.FocusControl(null); }
            tx += w + 4;
        }
        if (ConversationList) { DrawConversationList(r); return; }

        // ── Toolbar ──
        var bar = new Rect(r.x + 8, r.y + 42, r.width - 16, 22);
        float x = bar.x;
        GUI.SetNextControlName("dialogue search");
        string s = EditorGUI.TextField(new Rect(x, bar.y, 260, 20), dialogueSearch, EditorStyles.toolbarSearchField);
        if (s != dialogueSearch) { dialogueSearch = s; FilterDialogue(); dialogueScroll = Vector2.zero; }
        x += 268;
        if (dialogueOpen != null)
        {
            // An open conversation: back to the list, its name, and the asset itself.
            if (GUI.Button(new Rect(x, bar.y - 1, 120, 22), new GUIContent("‹ Conversations", "Back to the list of conversations."))) { CommitDialogue(true); dialogueOpen = null; FilterDialogue(); GUI.FocusControl(null); return; }
            GUI.Label(new Rect(x + 128, bar.y, 220, 20), $"<color=#e8c46a>{dialogueOpen.name}</color>", dialogueHeader);
            x += 128 + Mathf.Min(220, dialogueHeader.CalcSize(new GUIContent(dialogueOpen.name)).x) + 8;
            if (GUI.Button(new Rect(x, bar.y - 1, 64, 22), new GUIContent("Asset", "Select the conversation's asset in the Project window."))) { Selection.activeObject = dialogueOpen; EditorGUIUtility.PingObject(dialogueOpen); }
            x += 72;
        }
        else
        {
            var inTab = dialogue.Where(l => InTab(l, dialogueTab)).ToList();
            var cats = new[] { $"{GroupAll} ({inTab.Count})" }.Concat(dialogueGroupKeys.Select(k => $"{k} ({inTab.Count(l => GroupKey(l) == k)})")).ToArray();
            int cat = EditorGUI.Popup(new Rect(x, bar.y, 190, 20), dialogueGroup, cats);
            if (cat != dialogueGroup) { CommitDialogue(true); dialogueGroup = cat; FilterDialogue(); dialogueScroll = Vector2.zero; }
            x += 198;
        }
        GUI.Label(new Rect(x, bar.y, 34, 20), "Sort", dim);
        // Conversations and talks read in order: the first sort says so there.
        var sorts = (string[])DialogueSorts.Clone();
        if (ByOwnList) sorts[0] = "In order";
        int sort = EditorGUI.Popup(new Rect(x + 32, bar.y, 140, 20), dialogueSort, sorts);
        if (sort != dialogueSort) { dialogueSort = sort; FilterDialogue(); }
        x += 180;
        bool empty = EditorGUI.ToggleLeft(new Rect(x, bar.y, 110, 20), "Empty only", dialogueEmptyOnly);
        if (empty != dialogueEmptyOnly) { dialogueEmptyOnly = empty; FilterDialogue(); dialogueScroll = Vector2.zero; }
        float bx = bar.xMax;
        void Btn(string label, string tip, System.Action click)
        {
            float w = GUI.skin.button.CalcSize(new GUIContent(label)).x + 14;
            bx -= w;
            if (GUI.Button(new Rect(bx, bar.y - 1, w, 22), new GUIContent(label, tip))) click();
            bx -= 6;
        }
        Btn("Import CSV", "Bring back the text (and delivery) of lines edited in a spreadsheet: rows are matched by their key column.", () =>
        {
            string file = EditorUtility.OpenFilePanel("Import dialogue", Application.dataPath + "/..", "csv");
            if (!string.IsNullOrEmpty(file)) { CommitDialogue(true); Toast("Imported: " + AssetStudioDialogue.ImportCsv(dialogue, file)); FilterDialogue(); }
        });
        Btn("Export CSV", "The lines shown, with where they play, to edit in a spreadsheet and import again.", () =>
        {
            string file = EditorUtility.SaveFilePanel("Export dialogue", Application.dataPath + "/..", "dialogue", "csv");
            if (!string.IsNullOrEmpty(file)) { CommitDialogue(true); Toast("Exported " + AssetStudioDialogue.ExportCsv(dialogueShown, file)); }
        });
        Btn("Rescan", "Find the lines again (after lines were added in an inspector, or a scene was opened).", () => { RescanDialogue(); Toast($"{dialogue.Count} lines."); });
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.isDirty && !EditorApplication.isPlaying) Btn("Save scene", $"Lines in {scene.name} are saved with the scene.", () => { CommitDialogue(true); EditorSceneManager.SaveScene(scene); });

        GUI.Label(new Rect(bar.x, bar.y + 24, bar.width, 18),
            $"{dialogueShown.Count} of {dialogue.Count} lines · edits go straight into the asset or scene they live in (Ctrl+Z undoes) · {{killer}} and the like are filled in by the game", dim);

        // ── Lines ──
        var list = new Rect(r.x + 4, r.y + 86, r.width - 8, r.height - 90);
        float textWidth = list.width - 16 - 52 - DialogueButtons - 12;
        bool grouped = dialogueSort == 0;
        // Heights (headers in "Where it plays", wrapped text), worked out when the width or the list changes.
        if (!Mathf.Approximately(dialogueWidth, textWidth))
        {
            dialogueWidth = textWidth;
            dialogueHeights.Clear();
            string lastCat = null, lastField = null;
            foreach (var l in dialogueShown)
            {
                float h = 0;
                if (grouped && l.category != lastCat) { h += 30; lastCat = l.category; lastField = null; }
                string fieldKey = l.Group;
                if (!grouped || fieldKey != lastField) { h += 20 + (string.IsNullOrEmpty(l.when) ? 0 : dialogueWhen.CalcHeight(new GUIContent(l.when), textWidth + DialogueButtons)); lastField = fieldKey; }
                string t = dialoguePending.TryGetValue(LineId(l), out var pend) ? pend : l.text;
                h += Mathf.Max((l.arrayPath != null ? 46 : 26) + (AssetStudioDialogue.HasVoice(l) ? 22 : 0), dialogueArea.CalcHeight(new GUIContent(string.IsNullOrEmpty(t) ? " " : t), textWidth - Indent(l))) + 6;
                dialogueHeights.Add(h);
            }
        }
        float total = dialogueHeights.Sum() + 8;
        dialogueScroll = GUI.BeginScrollView(list, dialogueScroll, new Rect(0, 0, list.width - 16, total));
        float y = 4, top = dialogueScroll.y - 60, bottom = dialogueScroll.y + list.height + 60;
        string cat2 = null, field2 = null;
        AssetStudioDialogue.Line changed = null; int action = 0;   // 1 up, 2 down, 3 add, 4 remove
        for (int i = 0; i < dialogueShown.Count && i < dialogueHeights.Count; i++)
        {
            var l = dialogueShown[i];
            float h = dialogueHeights[i];
            bool visible = y + h >= top && y <= bottom;
            float cy = y;
            if (grouped && l.category != cat2)
            {
                if (visible) GUI.Label(new Rect(6, cy + 6, list.width - 30, 22), $"<color=#e8c46a>{l.category.ToUpperInvariant()}</color>", dialogueHeader);
                cy += 30; cat2 = l.category; field2 = null;
            }
            string fieldKey = l.Group;
            if (!grouped || fieldKey != field2)
            {
                if (visible)
                {
                    // A conversation's rows say their own place (Line 3 › Answer 1) when sorted otherwise.
                    bool place = !string.IsNullOrEmpty(l.context) && (!l.Conversation || !grouped);
                    GUI.Label(new Rect(10, cy + 2, list.width - 40, 18), $"{l.ownerName}  <color=#a9a49a>›</color>  <b>{l.field}</b>" + (place ? $"  <color=#a9a49a>› {l.context}</color>" : "") + (grouped ? "" : $"  <color=#8a857c>({l.category})</color>"), new GUIStyle(dialogueWhen) { fontSize = 12, normal = { textColor = new Color(.86f, .84f, .8f) } });
                    if (!string.IsNullOrEmpty(l.when)) GUI.Label(new Rect(10, cy + 19, textWidth + DialogueButtons, dialogueWhen.CalcHeight(new GUIContent(l.when), textWidth + DialogueButtons)), l.when, dialogueWhen);
                }
                cy += 20 + (string.IsNullOrEmpty(l.when) ? 0 : dialogueWhen.CalcHeight(new GUIContent(l.when), textWidth + DialogueButtons));
                field2 = fieldKey;
            }
            if (visible)
            {
                float rowH = h - (cy - y) - 6;
                float indent = Indent(l);
                GUI.Label(new Rect(14 + indent, cy + 4, 40, 18), RowLabel(l), dim);
                var area = new Rect(52 + indent, cy, textWidth - indent, rowH);
                EditorGUI.DrawRect(area, new Color(.13f, .13f, .14f));
                EditorGUI.DrawRect(new Rect(area.x, area.yMax - 1, area.width, 1), new Color(.32f, .31f, .29f));
                string current = dialoguePending.TryGetValue(LineId(l), out var p) ? p : l.text ?? "";
                GUI.SetNextControlName(DialogueControl(l));
                string typed = EditorGUI.TextArea(area, current, dialogueArea);
                if (typed != current) { dialoguePending[LineId(l)] = typed; dialogueWidth = -1; }
                float bx2 = area.xMax + 8;
                if (l.rich)
                {
                    // With a voice the mumble is not played (DungeonMaster: a voice takes its place).
                    GUI.enabled = l.voice == null;
                    bool m = GUI.Toggle(new Rect(bx2, cy + 3, 72, 20), l.mumble, new GUIContent(" Mumble", l.voice == null ? "Gibberish mumble when the line appears." : "Its voice plays instead."));
                    GUI.enabled = true;
                    bool ip = GUI.Toggle(new Rect(bx2 + 76, cy + 3, 80, 20), l.inPerson, new GUIContent(" In person", "Said across the table (paced for reading, he gestures), not as the voice in your head."));
                    if (m != l.mumble || ip != l.inPerson) AssetStudioDialogue.SetDelivery(l, m, ip);
                }
                else if (l.kind == AssetStudioDialogue.Kind.Box)
                {
                    int who = EditorGUI.Popup(new Rect(bx2, cy + 3, 130, 20), l.speaker, Speakers);
                    GUI.SetNextControlName("dlg name " + LineId(l));
                    string name = EditorGUI.DelayedTextField(new Rect(bx2 + 134, cy + 3, 114, 18), l.speakerName ?? "");
                    if (string.IsNullOrEmpty(name) && GUI.GetNameOfFocusedControl() != "dlg name " + LineId(l))
                        GUI.Label(new Rect(bx2 + 137, cy + 3, 110, 18), new GUIContent("own name", "The name on the box. Empty: the speaker's own (the boss's name, or Dungeon Master)."), dialogueWhen);
                    if (who != l.speaker || name != (l.speakerName ?? "")) AssetStudioDialogue.SetSpeaker(l, who, name);
                }
                // A recorded voice, played in place of the mumble (his lines) or the typing blips (a box). An AudioClip
                // dropped here gets an AudioData of its own (AssetStudioDialogue.VoiceFor).
                if (AssetStudioDialogue.HasVoice(l))
                {
                    float vy = cy + (l.arrayPath != null ? 46 : 24);
                    string instead = l.rich ? "his mumble" : "the typing blips";
                    GUI.Label(new Rect(bx2, vy, 40, 18), new GUIContent("Voice", $"A recorded voice, played instead of {instead}. Drop an AudioClip or an AudioData on the field; empty: {instead}."), dialogueWhen);
                    var at = new Rect(bx2 + 40, vy, 208, 18);
                    // An AudioClip dragged onto the field (the field itself only takes AudioData).
                    var ev = Event.current;
                    if ((ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) && at.Contains(ev.mousePosition)
                        && DragAndDrop.objectReferences.Length == 1 && DragAndDrop.objectReferences[0] is AudioClip dropped)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        if (ev.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); AssetStudioDialogue.SetVoice(l, dropped); }
                        ev.Use();
                    }
                    var picked = (AudioData)EditorGUI.ObjectField(at, l.voice as AudioData, typeof(AudioData), false);
                    if (picked != l.voice) AssetStudioDialogue.SetVoice(l, picked);
                }
                if (AssetStudioDialogue.CanAddChild(l))
                {
                    bool box = l.kind == AssetStudioDialogue.Kind.Box;
                    var at = box ? new Rect(bx2 + 114, cy + 24, 44, 20) : new Rect(bx2, cy + 3, 56, 20);
                    if (GUI.Button(at, box ? new GUIContent("+ ans", "An answer for the player to pick after this box.") : new GUIContent("+ reply", "A box said after this answer."), dialogueSmall)) { changed = l; action = 5; }
                }
                float b = bx2 + 250;
                if (!l.Conversation)
                {
                    GUI.enabled = EditorApplication.isPlaying && DungeonMaster.HasInstance;
                    if (GUI.Button(new Rect(b, cy + 2, 22, 20), new GUIContent("▶", "Play mode: the Dungeon Master says it now."), dialogueSmall))
                        DungeonMaster.Say(new DungeonMaster.Line { text = current, mumble = l.mumble || !l.rich, inPerson = l.inPerson, voice = l.voice as AudioData });
                    GUI.enabled = true;
                }
                if (GUI.Button(new Rect(b + 24, cy + 2, 22, 20), new GUIContent("◎", "Select the object that holds this line."), dialogueSmall))
                { if (l.owner is Component c) Selection.activeObject = c.gameObject; else Selection.activeObject = l.owner; EditorGUIUtility.PingObject(l.owner); }
                if (l.arrayPath != null)
                {
                    var row2 = cy + 24;
                    if (GUI.Button(new Rect(bx2, row2, 22, 20), new GUIContent("↑", "Earlier in its list."), dialogueSmall)) { changed = l; action = 1; }
                    if (GUI.Button(new Rect(bx2 + 24, row2, 22, 20), new GUIContent("↓", "Later in its list."), dialogueSmall)) { changed = l; action = 2; }
                    if (GUI.Button(new Rect(bx2 + 48, row2, 40, 20), new GUIContent("+ line", "A new empty line after this one, in the same list."), dialogueSmall)) { changed = l; action = 3; }
                    if (GUI.Button(new Rect(bx2 + 90, row2, 22, 20), new GUIContent("✕", "Remove this line from its list."), dialogueSmall)) { changed = l; action = 4; }
                }
            }
            y += h;
        }
        GUI.EndScrollView();

        // List changes after drawing: the owner's lines are read again (their places moved).
        if (changed != null)
        {
            CommitDialogue(true);
            if (action == 4 && !EditorUtility.DisplayDialog("Remove line", $"Remove \"{changed.text}\" from {changed.Where}?", "Remove", "Keep")) return;
            if (action == 1) AssetStudioDialogue.Move(changed, changed.index - 1);
            else if (action == 2) AssetStudioDialogue.Move(changed, changed.index + 1);
            else if (action == 3) AssetStudioDialogue.AddAfter(changed);
            else if (action == 4) AssetStudioDialogue.Remove(changed);
            else if (action == 5) AssetStudioDialogue.AddChild(changed);
            var owner = changed.owner;
            dialogue.RemoveAll(l => l.owner == owner);
            AssetStudioDialogue.Collect(owner, dialogue);
            FilterDialogue();
            GUI.FocusControl(null);
        }
    }
}

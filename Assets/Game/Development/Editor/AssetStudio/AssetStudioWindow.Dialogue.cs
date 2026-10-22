using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Asset Studio's Dialogue view: every Dungeon Master line in the game in one list (AssetStudioDialogue finds them),
// grouped by where it plays and in the order it is said, edited right there. Search, a filter by where it plays,
// sorting, empty lines only; each line can be moved, added after or removed in its list, said in Play mode, or its
// owner selected; CSV out and in for editing many in a spreadsheet. Edits land where the line lives (undoable); a
// line in the open scene leaves the scene unsaved until Save scene.
public partial class AssetStudioWindow
{
    List<AssetStudioDialogue.Line> dialogue;
    List<AssetStudioDialogue.Line> dialogueShown = new();
    string dialogueSearch = "";
    int dialogueCategory;    // 0 = all, else CategoryOrder[i - 1]
    int dialogueSort;        // DialogueSorts
    bool dialogueEmptyOnly;
    Vector2 dialogueScroll;
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
        IEnumerable<AssetStudioDialogue.Line> q = dialogue ?? new List<AssetStudioDialogue.Line>();
        if (dialogueCategory > 0) { string c = AssetStudioDialogue.CategoryOrder[dialogueCategory - 1]; q = q.Where(l => l.category == c); }
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
    // asset is in paths, and the open scenes' when scenes.
    void RecollectDialogue(bool all, ICollection<string> paths, bool scenes)
    {
        if (dialogue == null) return;
        var owners = dialogue.Select(l => l.owner).Distinct().Where(o => all || o == null
            || (EditorUtility.IsPersistent(o) ? paths != null && paths.Contains(AssetDatabase.GetAssetPath(o)) : scenes)).ToList();
        if (owners.Count == 0) return;
        foreach (var owner in owners)
        {
            dialogue.RemoveAll(l => l.owner == owner);
            if (owner != null) AssetStudioDialogue.Collect(owner, dialogue);
        }
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

    const float DialogueButtons = 220;   // the controls at the right of each line

    // The view: the area right of the library column.
    void DrawDialogue(Rect r)
    {
        DialogueStyles();
        if (dialogue == null) RescanDialogue();
        CommitDialogue(false);
        GUI.Box(r, GUIContent.none);

        // ── Toolbar ──
        var bar = new Rect(r.x + 8, r.y + 8, r.width - 16, 22);
        float x = bar.x;
        GUI.SetNextControlName("dialogue search");
        string s = EditorGUI.TextField(new Rect(x, bar.y, 260, 20), dialogueSearch, EditorStyles.toolbarSearchField);
        if (s != dialogueSearch) { dialogueSearch = s; FilterDialogue(); dialogueScroll = Vector2.zero; }
        x += 268;
        var cats = new[] { "Everywhere" }.Concat(AssetStudioDialogue.CategoryOrder.Select(c => $"{c} ({dialogue.Count(l => l.category == c)})")).ToArray();
        int cat = EditorGUI.Popup(new Rect(x, bar.y, 190, 20), dialogueCategory, cats);
        if (cat != dialogueCategory) { CommitDialogue(true); dialogueCategory = cat; FilterDialogue(); dialogueScroll = Vector2.zero; }
        x += 198;
        GUI.Label(new Rect(x, bar.y, 34, 20), "Sort", dim);
        int sort = EditorGUI.Popup(new Rect(x + 32, bar.y, 140, 20), dialogueSort, DialogueSorts);
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
        var list = new Rect(r.x + 4, r.y + 52, r.width - 8, r.height - 56);
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
                string fieldKey = l.ownerName + "|" + l.field + "|" + l.context;
                if (!grouped || fieldKey != lastField) { h += 20 + (string.IsNullOrEmpty(l.when) ? 0 : dialogueWhen.CalcHeight(new GUIContent(l.when), textWidth + DialogueButtons)); lastField = fieldKey; }
                string t = dialoguePending.TryGetValue(LineId(l), out var pend) ? pend : l.text;
                h += Mathf.Max(l.arrayPath != null ? 46 : 26, dialogueArea.CalcHeight(new GUIContent(string.IsNullOrEmpty(t) ? " " : t), textWidth)) + 6;
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
            string fieldKey = l.ownerName + "|" + l.field + "|" + l.context;
            if (!grouped || fieldKey != field2)
            {
                if (visible)
                {
                    GUI.Label(new Rect(10, cy + 2, list.width - 40, 18), $"{l.ownerName}  <color=#a9a49a>›</color>  <b>{l.field}</b>" + (string.IsNullOrEmpty(l.context) ? "" : $"  <color=#a9a49a>› {l.context}</color>") + (grouped ? "" : $"  <color=#8a857c>({l.category})</color>"), new GUIStyle(dialogueWhen) { fontSize = 12, normal = { textColor = new Color(.86f, .84f, .8f) } });
                    if (!string.IsNullOrEmpty(l.when)) GUI.Label(new Rect(10, cy + 19, textWidth + DialogueButtons, dialogueWhen.CalcHeight(new GUIContent(l.when), textWidth + DialogueButtons)), l.when, dialogueWhen);
                }
                cy += 20 + (string.IsNullOrEmpty(l.when) ? 0 : dialogueWhen.CalcHeight(new GUIContent(l.when), textWidth + DialogueButtons));
                field2 = fieldKey;
            }
            if (visible)
            {
                float rowH = h - (cy - y) - 6;
                GUI.Label(new Rect(14, cy + 4, 40, 18), l.index >= 0 ? (l.index + 1) + "." : "–", dim);
                var area = new Rect(52, cy, textWidth, rowH);
                EditorGUI.DrawRect(area, new Color(.13f, .13f, .14f));
                EditorGUI.DrawRect(new Rect(area.x, area.yMax - 1, area.width, 1), new Color(.32f, .31f, .29f));
                string current = dialoguePending.TryGetValue(LineId(l), out var p) ? p : l.text ?? "";
                GUI.SetNextControlName(DialogueControl(l));
                string typed = EditorGUI.TextArea(area, current, dialogueArea);
                if (typed != current) { dialoguePending[LineId(l)] = typed; dialogueWidth = -1; }
                float bx2 = area.xMax + 8;
                if (l.rich)
                {
                    bool m = GUI.Toggle(new Rect(bx2, cy + 3, 72, 20), l.mumble, new GUIContent(" Mumble", "Gibberish mumble when the line appears."));
                    bool ip = GUI.Toggle(new Rect(bx2 + 76, cy + 3, 80, 20), l.inPerson, new GUIContent(" In person", "Said across the table (paced for reading, he gestures), not as the voice in your head."));
                    if (m != l.mumble || ip != l.inPerson) AssetStudioDialogue.SetDelivery(l, m, ip);
                }
                float b = bx2 + 160;
                GUI.enabled = EditorApplication.isPlaying && DungeonMaster.HasInstance;
                if (GUI.Button(new Rect(b, cy + 2, 22, 20), new GUIContent("▶", "Play mode: the Dungeon Master says it now."), dialogueSmall))
                    DungeonMaster.Say(new DungeonMaster.Line { text = current, mumble = l.mumble || !l.rich, inPerson = l.inPerson, voice = l.voice as AudioData });
                GUI.enabled = true;
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
            var owner = changed.owner;
            dialogue.RemoveAll(l => l.owner == owner);
            AssetStudioDialogue.Collect(owner, dialogue);
            FilterDialogue();
            GUI.FocusControl(null);
        }
    }
}

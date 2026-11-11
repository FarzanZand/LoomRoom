using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The Item Database (Tools > Item Database): every ItemData asset in one window. Laid out again on 2026-11-11 (the user:
// the list was messy and the window hard to use): one toolbar (New, Save, Tools); on the left a search and filters over
// the items grouped by kind (Relics, Swords, Maces, Shields, each armour slot, Trinkets, Spell tomes, Scrolls,
// Consumables, Spoils, Keys, Other), each group folding, its items sorted by tier, each row the icon in a slot with its
// rarity's bar, the name in its rarity colour, its tier and main number; arrow keys move through it and its edge drags to
// resize. On the right the picked item: a header card (big icon, name, type line, its file, its actions in one row, the
// game's own tooltip beside it), its problems, the relic panel for a relic (ItemDatabaseWindow.Relics.cs), then all its
// fields (its Odin inspector, folding). The list's layout lives in ItemDatabaseWindow.List.cs.
public partial class ItemDatabaseWindow : EditorWindow
{
    List<ItemData> items = new();
    readonly Dictionary<ItemData, List<string>> issues = new();
    [SerializeField] ItemData selected;
    Editor inspector;
    Vector2 detailScroll;
    static GUIStyle tooltipStyle, titleStyle, subtitleStyle, linkStyle, sectionStyle, cardStyle;

    [MenuItem("Tools/Item Database", false, 0)]
    public static void ShowWindow()
    {
        var window = GetWindow<ItemDatabaseWindow>("Item Database");
        window.minSize = new Vector2(900, 500);
    }
    void OnEnable()
    {
        wantsMouseMove = true;
        if (listWidth < 300f) listWidth = 380f;   // a window open before the list could be resized
        Rebuild(); Undo.undoRedoPerformed += Rebuild; EditorApplication.projectChanged += Rebuild;
    }
    void OnDisable()
    {
        Undo.undoRedoPerformed -= Rebuild; EditorApplication.projectChanged -= Rebuild;
        if (inspector != null) DestroyImmediate(inspector);
    }
    void Rebuild()
    {
        items = ItemDatabaseAuthoring.All(); issues.Clear();
        foreach (var item in items) issues[item] = ItemDatabaseAuthoring.Issues(item, items);
        Repaint();
    }
    void Select(ItemData item)
    {
        if (selected == item && inspector != null) return;
        selected = item; detailScroll = Vector2.zero;
        if (inspector != null) DestroyImmediate(inspector);
        inspector = item != null ? Editor.CreateEditor(item) : null;
    }

    void OnGUI()
    {
        Styles();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            Toolbar();
            // From the window's size, not a layout rect: those are placeholders while Unity measures, and the details'
            // layout must come out the same in every pass.
            float top = EditorStyles.toolbar.fixedHeight > 0f ? EditorStyles.toolbar.fixedHeight : 21f;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorGUI.HelpBox(new Rect(0, top, position.width, 30f), "Exit Play mode to edit the item database.", MessageType.Info);
                top += 30f;
            }
            var body = new Rect(0, top, position.width, position.height - top);
            if (tableView) { TableView(body); if (Event.current.type == EventType.MouseMove) Repaint(); return; }
            listWidth = Mathf.Clamp(listWidth, 260f, Mathf.Max(260f, body.width - 420f));
            var listRect = new Rect(body.x, body.y, listWidth, body.height);
            var split = new Rect(listRect.xMax, body.y, 5f, body.height);
            var detailRect = new Rect(split.xMax, body.y, body.width - split.xMax, body.height);
            // The details first: a click or key in the list picks another item, and the details must be laid out and drawn
            // for the same item within one event.
            GUILayout.BeginArea(detailRect);
            Details(detailRect.width);
            GUILayout.EndArea();
            ListPanel(listRect);
            Splitter(split);
        }
        if (Event.current.type == EventType.MouseMove) Repaint();
    }

    void Styles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 18, fixedHeight = 0, wordWrap = false, richText = true };
        subtitleStyle = new GUIStyle(EditorStyles.label) { fontSize = 12, richText = true };
        subtitleStyle.normal.textColor = new Color(.72f, .72f, .72f);
        linkStyle = new GUIStyle(EditorStyles.miniLabel) { richText = false };
        linkStyle.normal.textColor = new Color(.5f, .62f, .78f);
        linkStyle.hover.textColor = new Color(.7f, .82f, 1f);
        sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
        sectionStyle.normal.textColor = new Color(.85f, .74f, .45f);
        cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(8, 8, 6, 6) };
        tooltipStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 12, padding = new RectOffset(10, 10, 8, 8) };
        tooltipStyle.normal.textColor = new Color(.86f, .86f, .86f);
        listStyles = null;
    }

    // ── Toolbar ──

    void Toolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(new GUIContent("New", "A new item of a type, or a Relic with the look of a piece of gear."), EditorStyles.toolbarDropDown, GUILayout.Width(60))) NewMenu();
            if (GUILayout.Button(new GUIContent("Save", "Write every changed item to disk."), EditorStyles.toolbarButton, GUILayout.Width(50)))
            { ItemDatabaseAuthoring.Save(); Rebuild(); ShowNotification(new GUIContent("Items saved")); }
            if (GUILayout.Button("Tools", EditorStyles.toolbarDropDown, GUILayout.Width(60))) ToolsMenu();
            GUILayout.Space(12);
            // List: one item at a time. Table: a group of items side by side, every number editable (ItemDatabaseWindow.Table.cs).
            int view = GUILayout.Toolbar(tableView ? 1 : 0, new[] { new GUIContent("List", "One item at a time, all its fields."), new GUIContent("Table", "A kind or a tier side by side, every stat editable.") }, EditorStyles.toolbarButton, GUILayout.Width(110));
            if ((view == 1) != tableView) { if (view == 1) OpenTable(); else tableView = false; GUI.FocusControl(null); }
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{items.Count} items", EditorStyles.miniLabel);
        }
    }
    void NewMenu()
    {
        var menu = new GenericMenu();
        foreach (ItemType type in Enum.GetValues(typeof(ItemType)))
        {
            var captured = type;
            menu.AddItem(new GUIContent("Item/" + ObjectNames.NicifyVariableName(type.ToString())), false, () =>
            {
                var item = ItemDatabaseAuthoring.Create(captured);
                ItemDatabaseAuthoring.SyncCatalog();
                ClearFilters();
                Rebuild(); Select(item); revealSelected = true;
            });
        }
        AddRelicItems(menu, "Relic with the look of/");
        menu.ShowAsContext();
    }
    void ToolsMenu()
    {
        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Generate icons of the shown items"), false, () => GenerateIcons(Visible().ToList()));
        menu.AddItem(new GUIContent("Move the shown items to their type folders"), false, Organize);
        menu.AddItem(new GUIContent("Sync the runtime catalog"), false, () =>
            ShowNotification(new GUIContent($"Catalog updated: {ItemDatabaseAuthoring.SyncCatalog()} items. Save the scene.")));
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Authoring guide"), false, () => EditorUtility.OpenWithDefaultApp(Path.GetFullPath("Docs/Item-database.md")));
        menu.ShowAsContext();
    }

    // ── The picked item ──

    void Details(float width)
    {
        if (selected == null)
        {
            GUILayout.Space(40);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(24);
                using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(520)))
                {
                    GUILayout.Label("Pick an item on the left", titleStyle);
                    GUILayout.Space(6);
                    EditorGUILayout.LabelField("These are the real item assets that loot, shops, equipment and pickups use: a change here applies everywhere.", EditorStyles.wordWrappedLabel);
                }
            }
            return;
        }
        if (inspector == null || inspector.target != selected) Select(selected);
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        Header(selected, width);
        if (issues.TryGetValue(selected, out var warnings) && warnings.Count > 0)
            using (new EditorGUILayout.VerticalScope(new GUIStyle { margin = new RectOffset(8, 8, 0, 4) }))
                foreach (string warning in warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        if (selected.rarity == ItemRarity.Relic) RelicPanel(selected);

        // Every field, the item's own inspector.
        bool relic = selected.rarity == ItemRarity.Relic;
        string key = relic ? "LoomRoom.ItemDatabase.AllFieldsRelic" : "LoomRoom.ItemDatabase.AllFields";
        bool open = EditorPrefs.GetBool(key, !relic);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(8);
            bool next = EditorGUILayout.Foldout(open, relic ? "All fields" : "Fields", true, new GUIStyle(EditorStyles.foldoutHeader) { fontStyle = FontStyle.Bold });
            if (next != open) EditorPrefs.SetBool(key, open = next);
        }
        if (open)
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(12, 10, 2, 12) }))
            {
                float labelWidth = EditorGUIUtility.labelWidth; bool wide = EditorGUIUtility.wideMode;
                EditorGUIUtility.labelWidth = 170f; EditorGUIUtility.wideMode = true;
                EditorGUI.BeginChangeCheck(); inspector.OnInspectorGUI();
                if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(selected); issues[selected] = ItemDatabaseAuthoring.Issues(selected, items); }
                EditorGUIUtility.labelWidth = labelWidth; EditorGUIUtility.wideMode = wide;
            }
        EditorGUILayout.EndScrollView();
    }

    // The header card: big icon, name in its rarity colour, what it is, its file, its actions; the game's tooltip beside it.
    void Header(ItemData item, float width)
    {
        bool tooltipBeside = width > 900f;
        using (new EditorGUILayout.HorizontalScope(cardStyle))
        {
            Rect slot = GUILayoutUtility.GetRect(84, 84, GUILayout.Width(84), GUILayout.Height(84));
            DrawSlot(slot, item, 6f);
            GUILayout.Space(12);
            using (new EditorGUILayout.VerticalScope())
            {
                GUILayout.Label(item.itemName, NameStyle(titleStyle, item));
                GUILayout.Label(Subtitle(item), subtitleStyle);
                string path = AssetDatabase.GetAssetPath(item);
                Rect link = GUILayoutUtility.GetRect(new GUIContent(path), linkStyle);
                EditorGUIUtility.AddCursorRect(link, MouseCursor.Link);
                if (GUI.Button(link, new GUIContent(path, "Show it in the Project window."), linkStyle)) { Selection.activeObject = item; EditorGUIUtility.PingObject(item); }
                GUILayout.Space(8);
                Actions(item);
            }
            if (tooltipBeside)
            {
                GUILayout.Space(12);
                TooltipCard(item, GUILayout.Width(Mathf.Min(360f, width * .34f)));
            }
        }
        if (!tooltipBeside)
            using (new EditorGUILayout.HorizontalScope()) { GUILayout.Space(8); TooltipCard(item); GUILayout.Space(8); }
    }

    static string Subtitle(ItemData item)
    {
        var parts = new List<string>();
        if (item.rarity != ItemRarity.Common) parts.Add($"<color=#{ColorUtility.ToHtmlStringRGB(NameColour(item))}>{item.RarityName}</color>");
        parts.Add(item.TypeLine());
        if (item.tier > 0) parts.Add("Tier " + item.tier);
        if (item.value > 0) parts.Add(item.value + " gold");
        if (ItemDatabaseAuthoring.IsArchived(item)) parts.Add("<color=#d08a4a>archived</color>");
        return string.Join("   ·   ", parts);
    }

    void Actions(ItemData item)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Duplicate", "A copy of it to change."), GUILayout.Width(80)))
            { Select(ItemDatabaseAuthoring.Duplicate(item)); ItemDatabaseAuthoring.SyncCatalog(); Rebuild(); revealSelected = true; GUIUtility.ExitGUI(); }
            using (new EditorGUI.DisabledScope(item.iconSource != null && item.iconSource.custom))
                if (GUILayout.Button(new GUIContent("Make icon", "Render its bag icon from its model."), GUILayout.Width(80))) GenerateIcons(new List<ItemData> { item });
            bool archived = ItemDatabaseAuthoring.IsArchived(item);
            if (GUILayout.Button(new GUIContent(archived ? "Restore" : "Archive", archived ? "Back to its type folder." : "Out of the game's catalog; what points at it keeps working."), GUILayout.Width(70)))
            {
                if (archived) ItemDatabaseAuthoring.Move(item, ItemDatabaseAuthoring.Destination(item));
                else if (EditorUtility.DisplayDialog("Archive item", "Move this asset to _Archive? Existing loot/scene references remain valid. It is excluded from future catalog syncs. Remove it from loot tables separately if it should no longer drop.", "Archive", "Cancel"))
                    ItemDatabaseAuthoring.Move(item, ItemDatabaseAuthoring.Root + "/_Archive/" + Path.GetFileName(AssetDatabase.GetAssetPath(item)));
                Rebuild(); GUIUtility.ExitGUI();
            }
            string path = AssetDatabase.GetAssetPath(item);
            if (!archived && path.StartsWith(ItemDatabaseAuthoring.Root + "/", StringComparison.Ordinal) && path != ItemDatabaseAuthoring.Destination(item)
                && GUILayout.Button(new GUIContent("Move to its folder", ItemDatabaseAuthoring.Destination(item)), GUILayout.Width(120)))
            { ItemDatabaseAuthoring.Move(item, ItemDatabaseAuthoring.Destination(item)); Rebuild(); GUIUtility.ExitGUI(); }
            GUILayout.FlexibleSpace();
            var old = GUI.backgroundColor; GUI.backgroundColor = new Color(1f, .5f, .45f);
            bool delete = GUILayout.Button(new GUIContent("Delete", "To the Recycle Bin, after saying what still uses it."), GUILayout.Width(64));
            GUI.backgroundColor = old;
            if (delete) { Delete(item); GUIUtility.ExitGUI(); }
        }
    }

    // The tooltip as the game builds it, on a dark card.
    void TooltipCard(ItemData item, params GUILayoutOption[] options)
    {
        string text = $"<size=14><b><color=#{ColorUtility.ToHtmlStringRGB(NameColour(item))}>{item.itemName}</color></b></size>\n" + item.BuildTooltip();
        var content = new GUIContent(text);
        float w = options.Length > 0 ? Mathf.Min(360f, position.width * .3f) : position.width - listWidth - 40f;
        float h = tooltipStyle.CalcHeight(content, Mathf.Max(120f, w));
        Rect r = GUILayoutUtility.GetRect(content, tooltipStyle, options.Concat(new[] { GUILayout.Height(h) }).ToArray());
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(r, new Color(.11f, .1f, .1f));
            DrawFrame(r, new Color(.36f, .32f, .26f));
        }
        GUI.Label(r, content, tooltipStyle);
    }

    static void DrawFrame(Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
    }

    // An item's icon in a dark slot with its rarity's colour along the left edge, the way a bag slot reads.
    static void DrawSlot(Rect r, ItemData item, float pad)
    {
        if (Event.current.type != EventType.Repaint) return;
        EditorGUI.DrawRect(r, new Color(.13f, .13f, .14f));
        DrawFrame(r, new Color(.27f, .27f, .29f));
        if (item.rarity != ItemRarity.Common) EditorGUI.DrawRect(new Rect(r.x, r.y, Mathf.Max(3f, r.width * .04f), r.height), NameColour(item));
        var sprite = item.icon;
        if (sprite == null || sprite.texture == null) return;
        var inner = new Rect(r.x + pad, r.y + pad, r.width - pad * 2, r.height - pad * 2);
        Rect sr = sprite.rect;
        float aspect = sr.width / Mathf.Max(1f, sr.height);
        Rect fit = aspect >= 1f ? new Rect(inner.x, inner.center.y - inner.width / aspect / 2f, inner.width, inner.width / aspect)
                                : new Rect(inner.center.x - inner.height * aspect / 2f, inner.y, inner.height * aspect, inner.height);
        var t = sprite.texture;
        GUI.DrawTextureWithTexCoords(fit, t, new Rect(sr.x / t.width, sr.y / t.height, sr.width / t.width, sr.height / t.height));
    }

    // The rarity's colour, a little lighter so it reads on the editor's grey; Common in the editor's own text colour.
    static Color NameColour(ItemData item) => item.rarity == ItemRarity.Common ? EditorStyles.label.normal.textColor : Color.Lerp(item.RarityColour, Color.white, .2f);
    static readonly Dictionary<(GUIStyle, Color), GUIStyle> tinted = new();
    static GUIStyle NameStyle(GUIStyle style, ItemData item)
    {
        var c = NameColour(item);
        if (!tinted.TryGetValue((style, c), out var s)) { s = new GUIStyle(style); s.normal.textColor = s.hover.textColor = c; tinted[(style, c)] = s; }
        return s;
    }

    // ── Helpers kept from before ──

    void GenerateIcons(List<ItemData> chosen)
    {
        var problems = new List<string>();
        int made = ItemIconRenderer.GenerateAll(chosen, problems);
        Rebuild();
        if (problems.Count > 0)
            EditorUtility.DisplayDialog("Generate icons", $"{made} icon(s) made. No icon for:\n\n" + string.Join("\n", problems.Take(20)) + (problems.Count > 20 ? $"\n...and {problems.Count - 20} more." : ""), "OK");
        else ShowNotification(new GUIContent($"{made} icon(s) made"));
    }

    // Lists what still uses the item, then moves it to the trash (recoverable from the OS).
    void Delete(ItemData item)
    {
        if (item == null) return;
        var uses = ItemDatabaseAuthoring.References(item);
        string message = $"Delete '{item.itemName}'?\n\nThe asset goes to the Recycle Bin and is removed from the runtime catalog.";
        if (uses.Count > 0)
            message += $"\n\nStill used by {uses.Count} asset(s); those references will become empty:\n" +
                       string.Join("\n", uses.Take(12).Select(Path.GetFileNameWithoutExtension)) + (uses.Count > 12 ? $"\n...and {uses.Count - 12} more." : "") +
                       "\n\nArchive keeps them working instead.";
        int choice = uses.Count > 0
            ? EditorUtility.DisplayDialogComplex("Delete item", message, "Delete", "Cancel", "Archive instead")
            : EditorUtility.DisplayDialog("Delete item", message, "Delete", "Cancel") ? 0 : 1;
        if (choice == 1) return;
        if (choice == 2)
        {
            ItemDatabaseAuthoring.Move(item, ItemDatabaseAuthoring.Root + "/_Archive/" + Path.GetFileName(AssetDatabase.GetAssetPath(item)));
            Rebuild(); return;
        }
        string name = item.itemName;
        if (selected == item) Select(null);
        if (!ItemDatabaseAuthoring.Delete(item)) { EditorUtility.DisplayDialog("Delete item", "Could not delete " + name + ".", "OK"); return; }
        Rebuild();
        ShowNotification(new GUIContent($"Deleted {name}. Save the scene to keep the catalog change."));
    }

    void Organize()
    {
        var moves = Visible().Where(i => !ItemDatabaseAuthoring.IsArchived(i) && AssetDatabase.GetAssetPath(i).StartsWith(ItemDatabaseAuthoring.Root + "/", StringComparison.Ordinal) && AssetDatabase.GetAssetPath(i) != ItemDatabaseAuthoring.Destination(i)).ToList();
        if (moves.Count == 0) { ShowNotification(new GUIContent("Shown items are already organized")); return; }
        string preview = string.Join("\n", moves.Take(12).Select(i => i.name + " -> " + ItemDatabaseAuthoring.Folder(i.itemType)));
        if (moves.Count > 12) preview += $"\n...and {moves.Count - 12} more.";
        if (!EditorUtility.DisplayDialog("Organize " + moves.Count + " items", preview + "\n\nGUIDs and references are preserved. Only shown, active items inside Items/Data will move.", "Move assets", "Cancel")) return;
        foreach (var item in moves) ItemDatabaseAuthoring.Move(item, ItemDatabaseAuthoring.Destination(item));
        ItemDatabaseAuthoring.Save(); Rebuild();
    }
}

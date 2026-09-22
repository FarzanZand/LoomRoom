using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class ItemDatabaseWindow : EditorWindow
{
    List<ItemData> items = new();
    readonly Dictionary<ItemData, List<string>> issues = new();
    [SerializeField] ItemData selected;
    Editor inspector;
    string search = "", tag = "";
    int typeFilter, slotFilter;
    bool showArchived, issuesOnly, tooltip;
    Vector2 listScroll, detailScroll;
    static readonly string[] Types = new[] { "All types" }.Concat(Enum.GetNames(typeof(ItemType))).ToArray();
    static readonly string[] Slots = new[] { "All slots" }.Concat(Enum.GetNames(typeof(EquipmentSlot))).ToArray();

    [MenuItem("Tools/Item Database")]
    public static void ShowWindow()
    {
        var window = GetWindow<ItemDatabaseWindow>("Item Database");
        window.minSize = new Vector2(900, 500);
    }
    void OnEnable() { Rebuild(); Undo.undoRedoPerformed += Rebuild; EditorApplication.projectChanged += Rebuild; }
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
    IEnumerable<ItemData> Visible() => items.Where(i => i != null &&
        (showArchived || !ItemDatabaseAuthoring.IsArchived(i)) &&
        (!issuesOnly || issues[i].Count > 0) &&
        (typeFilter == 0 || i.itemType.ToString() == Types[typeFilter]) &&
        (slotFilter == 0 || i.canBeEquipped && i.equipSlot.ToString() == Slots[slotFilter]) &&
        (string.IsNullOrEmpty(tag) || i.tag == tag) &&
        (string.IsNullOrWhiteSpace(search) || (i.itemName + " " + i.tag + " " + i.itemType + " " + AssetDatabase.GetAssetPath(i))
            .IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0));
    void OnGUI()
    {
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            Toolbar();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(310))) List();
                using (new EditorGUILayout.VerticalScope()) Details();
            }
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorGUILayout.HelpBox("Exit Play mode to edit the item database.", MessageType.Info);
    }
    void Toolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("New item", EditorStyles.toolbarDropDown, GUILayout.Width(85)))
            {
                var menu = new GenericMenu();
                foreach (ItemType type in Enum.GetValues(typeof(ItemType)))
                { var captured = type; menu.AddItem(new GUIContent(type.ToString()), false, () => { Select(ItemDatabaseAuthoring.Create(captured)); Rebuild(); }); }
                menu.ShowAsContext();
            }
            if (GUILayout.Button("Save items", EditorStyles.toolbarButton, GUILayout.Width(85))) { ItemDatabaseAuthoring.Save(); Rebuild(); ShowNotification(new GUIContent("Items saved")); }
            if (GUILayout.Button("Organize shown", EditorStyles.toolbarButton, GUILayout.Width(115))) Organize();
            if (GUILayout.Button(new GUIContent("Sync runtime catalog", "Add all active items to InventoryManager's name lookup catalog. Save the scene afterwards."), EditorStyles.toolbarButton, GUILayout.Width(145)))
                ShowNotification(new GUIContent($"Catalog updated: {ItemDatabaseAuthoring.SyncCatalog()} items. Save the scene."));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Authoring guide", EditorStyles.toolbarButton)) EditorUtility.OpenWithDefaultApp(Path.GetFullPath("Docs/Item-database.md"));
        }
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(130));
            typeFilter = EditorGUILayout.Popup(typeFilter, Types, GUILayout.Width(115));
            slotFilter = EditorGUILayout.Popup(slotFilter, Slots, GUILayout.Width(115));
            var tags = new[] { "All tags" }.Concat(items.Select(i => i.tag).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t)).ToArray();
            int tagIndex = Array.IndexOf(tags, tag);
            int next = EditorGUILayout.Popup(Mathf.Max(0, tagIndex), tags, GUILayout.Width(110)); tag = next == 0 ? "" : tags[next];
            issuesOnly = GUILayout.Toggle(issuesOnly, "Needs attention", EditorStyles.toolbarButton);
            showArchived = GUILayout.Toggle(showArchived, "Archived", EditorStyles.toolbarButton);
        }
    }
    void List()
    {
        var visible = Visible().ToList();
        EditorGUILayout.LabelField($"{visible.Count} shown / {items.Count} items", EditorStyles.miniLabel);
        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        foreach (var item in visible)
        {
            Rect row = GUILayoutUtility.GetRect(290, 52, GUILayout.ExpandWidth(true));
            if (selected == item) EditorGUI.DrawRect(row, new Color(.22f, .37f, .48f));
            var icon = item.icon != null ? AssetPreview.GetAssetPreview(item.icon) ?? AssetPreview.GetMiniThumbnail(item.icon) : null;
            if (icon != null) GUI.DrawTexture(new Rect(row.x + 5, row.y + 5, 40, 40), icon, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(row.x + 52, row.y + 5, row.width - 57, 22), (issues[item].Count > 0 ? "! " : "") + item.itemName, EditorStyles.boldLabel);
            string subtitle = item.itemType + (item.canBeEquipped ? " / " + item.equipSlot : "") + (ItemDatabaseAuthoring.IsArchived(item) ? " / ARCHIVED" : "");
            GUI.Label(new Rect(row.x + 52, row.y + 27, row.width - 57, 20), subtitle, EditorStyles.miniLabel);
            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition)) { Select(item); GUI.FocusControl(null); Event.current.Use(); }
        }
        EditorGUILayout.EndScrollView();
    }
    void Details()
    {
        if (selected == null)
        {
            GUILayout.Space(30);
            EditorGUILayout.LabelField("Select an item to edit", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These are the real ItemData assets used by loot tables, equipment and pickups. Changes apply everywhere that references an item. Use tags to group content without moving files.", MessageType.Info);
            return;
        }
        if (inspector == null || inspector.target != selected) Select(selected);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("Locate asset", EditorStyles.toolbarButton)) { Selection.activeObject = selected; EditorGUIUtility.PingObject(selected); }
            if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton)) { Select(ItemDatabaseAuthoring.Duplicate(selected)); Rebuild(); }
            if (GUILayout.Button("Move to type folder", EditorStyles.toolbarButton)) { ItemDatabaseAuthoring.Move(selected, ItemDatabaseAuthoring.Destination(selected)); Rebuild(); }
            bool archived = ItemDatabaseAuthoring.IsArchived(selected);
            if (GUILayout.Button(archived ? "Restore" : "Archive", EditorStyles.toolbarButton))
            {
                if (archived) ItemDatabaseAuthoring.Move(selected, ItemDatabaseAuthoring.Destination(selected));
                else if (EditorUtility.DisplayDialog("Archive item", "Move this asset to _Archive? Existing loot/scene references remain valid. It is excluded from future catalog syncs. Remove it from loot tables separately if it should no longer drop.", "Archive", "Cancel"))
                    ItemDatabaseAuthoring.Move(selected, ItemDatabaseAuthoring.Root + "/_Archive/" + Path.GetFileName(AssetDatabase.GetAssetPath(selected)));
                Rebuild();
            }
        }
        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(selected), EditorStyles.miniLabel, GUILayout.Height(20));
        if (issues.TryGetValue(selected, out var warnings)) foreach (string warning in warnings) EditorGUILayout.HelpBox(warning, MessageType.Warning);
        string pickup = selected.pickupVisualPrefab != null ? selected.pickupVisualPrefab.name : selected.worldPrefab != null ? selected.worldPrefab.name : "Equipment loot pouch (InventoryManager default)";
        EditorGUILayout.LabelField("Pickup visual", pickup);
        tooltip = EditorGUILayout.Foldout(tooltip, "Tooltip preview", true);
        if (tooltip) EditorGUILayout.HelpBox(selected.BuildTooltip(), MessageType.None);
        EditorGUI.BeginChangeCheck(); inspector.OnInspectorGUI();
        if (EditorGUI.EndChangeCheck()) { EditorUtility.SetDirty(selected); issues[selected] = ItemDatabaseAuthoring.Issues(selected, items); }
        EditorGUILayout.EndScrollView();
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

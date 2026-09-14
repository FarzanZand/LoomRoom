using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Overview + authoring window for every ItemData asset in the project.
// Reads/writes the assets directly through AssetDatabase — the .asset files under
// Assets/Items ARE the database. Anything under a _Archive folder is hidden by default.
public class ItemDatabaseWindow : EditorWindow
{
    const string ItemFolder = "Assets/Items";

    const float WFold    = 16f;
    const float WIcon    = 40f;
    const float WTag     = 100f;
    const float WType    = 100f;
    const float WSlot    = 90f;
    const float WStack   = 50f;
    const float WEffects = 60f;
    const float WButtons = 130f;

    enum SortColumn { Name, Tag, Type, Slot, Stack }

    List<ItemData> items = new();
    readonly Dictionary<ItemData, Editor> editors = new();
    readonly HashSet<ItemData> expanded = new();

    SortColumn sortColumn = SortColumn.Name;
    bool sortAscending = true;
    string search = "";
    Vector2 scroll;
    bool showArchived;
    int archivedCount;

    static bool IsArchived(string assetPath) => assetPath.Contains("/_Archive/");

    [MenuItem("Tools/Item Database")]
    static void ShowWindow()
    {
        var w = GetWindow<ItemDatabaseWindow>("Item Database");
        w.minSize = new Vector2(720, 300);
    }

    void OnEnable()  => Rebuild();
    void OnFocus()   => Rebuild();
    void OnDisable() => ClearEditors();

    void Rebuild()
    {
        items = new List<ItemData>();
        archivedCount = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (IsArchived(path))
            {
                archivedCount++;
                if (!showArchived) continue;
            }
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item != null) items.Add(item);
        }
        Sort();
        foreach (var dead in editors.Keys.Where(k => k == null || !items.Contains(k)).ToList())
        {
            if (editors[dead] != null) DestroyImmediate(editors[dead]);
            editors.Remove(dead);
        }
    }

    void ClearEditors()
    {
        foreach (var e in editors.Values) if (e != null) DestroyImmediate(e);
        editors.Clear();
    }

    void Sort()
    {
        System.Comparison<ItemData> cmp = sortColumn switch
        {
            SortColumn.Tag   => (a, b) => string.Compare(a.tag, b.tag, System.StringComparison.OrdinalIgnoreCase),
            SortColumn.Type  => (a, b) => a.itemType.CompareTo(b.itemType),
            SortColumn.Slot  => (a, b) => (a.canBeEquipped ? (int)a.equipSlot : -1).CompareTo(b.canBeEquipped ? (int)b.equipSlot : -1),
            SortColumn.Stack => (a, b) => a.maxStackSize.CompareTo(b.maxStackSize),
            _                => (a, b) => string.Compare(a.itemName, b.itemName, System.StringComparison.OrdinalIgnoreCase),
        };
        items.Sort((a, b) => sortAscending ? cmp(a, b) : cmp(b, a));
    }

    void OnGUI()
    {
        DrawToolbar();
        DrawHeader();

        scroll = EditorGUILayout.BeginScrollView(scroll);
        string q = search.Trim().ToLowerInvariant();
        foreach (var item in items.ToList())
        {
            if (item == null) continue;
            if (q.Length > 0 &&
                !(item.itemName ?? "").ToLowerInvariant().Contains(q) &&
                !(item.tag ?? "").ToLowerInvariant().Contains(q) &&
                !item.itemType.ToString().ToLowerInvariant().Contains(q))
                continue;
            DrawRow(item);
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("New Item", EditorStyles.toolbarButton, GUILayout.Width(80))) CreateItem();
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) Rebuild();
        GUILayout.Space(8);
        search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160));
        GUILayout.FlexibleSpace();
        bool wasArchived = showArchived;
        showArchived = GUILayout.Toggle(showArchived, $"Archived ({archivedCount})", EditorStyles.toolbarButton);
        if (wasArchived != showArchived) Rebuild();
        GUILayout.Label($"{items.Count} items", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Space(WFold + WIcon);
        HeaderButton("Name",  SortColumn.Name,  0f, expand: true);
        HeaderButton("Tag",   SortColumn.Tag,   WTag);
        HeaderButton("Type",  SortColumn.Type,  WType);
        HeaderButton("Slot",  SortColumn.Slot,  WSlot);
        HeaderButton("Stack", SortColumn.Stack, WStack);
        GUILayout.Label("Effects", EditorStyles.toolbarButton, GUILayout.Width(WEffects));
        GUILayout.Space(WButtons);
        EditorGUILayout.EndHorizontal();
    }

    void HeaderButton(string label, SortColumn col, float width, bool expand = false)
    {
        string arrow = sortColumn == col ? (sortAscending ? " ▲" : " ▼") : "";
        var opts = expand ? new[] { GUILayout.ExpandWidth(true) } : new[] { GUILayout.Width(width) };
        if (GUILayout.Button(label + arrow, EditorStyles.toolbarButton, opts))
        {
            if (sortColumn == col) sortAscending = !sortAscending;
            else { sortColumn = col; sortAscending = true; }
            Sort();
        }
    }

    void DrawRow(ItemData item)
    {
        EditorGUILayout.BeginHorizontal();

        bool open = expanded.Contains(item);
        bool nowOpen = GUILayout.Toggle(open, GUIContent.none, EditorStyles.foldout, GUILayout.Width(WFold));
        if (nowOpen != open) { if (nowOpen) expanded.Add(item); else expanded.Remove(item); }

        var iconRect = GUILayoutUtility.GetRect(WIcon, 20, GUILayout.Width(WIcon));
        if (item.icon != null) GUI.DrawTexture(iconRect, item.icon.texture, ScaleMode.ScaleToFit);

        EditorGUI.BeginChangeCheck();
        string newName = EditorGUILayout.TextField(item.itemName, GUILayout.ExpandWidth(true));
        string newTag  = EditorGUILayout.TextField(item.tag, GUILayout.Width(WTag));
        var newType    = (ItemType)EditorGUILayout.EnumPopup(item.itemType, GUILayout.Width(WType));
        if (item.canBeEquipped)
        {
            var newSlot = (EquipmentSlot)EditorGUILayout.EnumPopup(item.equipSlot, GUILayout.Width(WSlot));
            if (newSlot != item.equipSlot) { Undo.RecordObject(item, "Edit Item"); item.equipSlot = newSlot; EditorUtility.SetDirty(item); }
        }
        else GUILayout.Label("—", GUILayout.Width(WSlot));
        int newStack = EditorGUILayout.IntField(item.maxStackSize, GUILayout.Width(WStack));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(item, "Edit Item");
            item.itemName = newName; item.tag = newTag; item.itemType = newType; item.maxStackSize = Mathf.Max(1, newStack);
            EditorUtility.SetDirty(item);
        }

        GUILayout.Label((item.effects?.Length ?? 0).ToString(), GUILayout.Width(WEffects));

        if (GUILayout.Button("Select", GUILayout.Width(55))) { Selection.activeObject = item; EditorGUIUtility.PingObject(item); }
        if (GUILayout.Button("Dup", GUILayout.Width(35))) Duplicate(item);
        if (GUILayout.Button("Del", GUILayout.Width(35)) &&
            EditorUtility.DisplayDialog("Delete item", $"Delete {item.itemName}? This cannot be undone.", "Delete", "Cancel"))
        {
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item));
            Rebuild();
            EditorGUILayout.EndHorizontal();
            return;
        }

        EditorGUILayout.EndHorizontal();

        if (expanded.Contains(item))
        {
            if (!editors.TryGetValue(item, out var ed) || ed == null)
            {
                ed = Editor.CreateEditor(item);
                editors[item] = ed;
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.BeginVertical("box");
            ed.OnInspectorGUI();
            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel--;
        }
    }

    void CreateItem()
    {
        var item = CreateInstance<ItemData>();
        item.itemName = "New Item";
        string path = AssetDatabase.GenerateUniqueAssetPath($"{ItemFolder}/NewItem.asset");
        AssetDatabase.CreateAsset(item, path);
        AssetDatabase.SaveAssets();
        Rebuild();
        expanded.Add(item);
        Selection.activeObject = item;
    }

    void Duplicate(ItemData src)
    {
        string path = AssetDatabase.GetAssetPath(src);
        string dst  = AssetDatabase.GenerateUniqueAssetPath(path);
        AssetDatabase.CopyAsset(path, dst);
        AssetDatabase.SaveAssets();
        Rebuild();
    }
}

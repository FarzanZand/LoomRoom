using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// Turns the table player's bag into a 10 x 4 Diablo grid and lays the inventory screen out to match:
// small cells with an item layer drawn over them (ItemGridUI), and the equipment slots arranged as a
// paper doll, each sized to the items it takes. Safe to run again.
public static class HeathenInventorySetup
{
    const int Columns = 10, Rows = 4;
    const float Cell = 54f, Gap = 4f, Unit = 44f, UnitGap = 4f;

    // Edits to prefab instances (the cells, the slots) only survive a save as recorded overrides.
    static void Record(Object o) { if (o != null && PrefabUtility.IsPartOfPrefabInstance(o)) PrefabUtility.RecordPrefabInstancePropertyModifications(o); }

    [MenuItem("Tools/LoomRoom/UI/Set Up Diablo Inventory")]
    public static string Run()
    {
        var log = new System.Text.StringBuilder();

        // The bag: 40 cells, 10 wide.
        string playerPath = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
        var root = PrefabUtility.LoadPrefabContents(playerPath);
        foreach (var inv in root.GetComponentsInChildren<Inventory>(true))
        {
            if (inv.role != InventoryRole.Bag) continue;
            var so = new SerializedObject(inv);
            so.FindProperty("slotCount").intValue = Columns * Rows;
            so.FindProperty("gridWidth").intValue = Columns;
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("bag grid on " + inv.name);
        }
        PrefabUtility.SaveAsPrefabAsset(root, playerPath);
        PrefabUtility.UnloadPrefabContents(root);

        var ui = Object.FindAnyObjectByType<InventoryUI>(FindObjectsInactive.Include);
        var screen = ui.transform.root.GetComponentInChildren<AdventureMenuTabs>(true).transform;
        var panel = (RectTransform)screen.Find("InventoryPanel");
        var grid = (RectTransform)panel.Find("Grid");

        // Cells.
        var layout = grid.GetComponent<GridLayoutGroup>();
        layout.cellSize = new Vector2(Cell, Cell);
        layout.spacing = new Vector2(Gap, Gap);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = Columns;
        Record(layout);
        grid.anchorMin = grid.anchorMax = new Vector2(.5f, 1f);
        grid.pivot = new Vector2(.5f, 1f);
        grid.sizeDelta = new Vector2(Columns * Cell + (Columns - 1) * Gap, Rows * Cell + (Rows - 1) * Gap);
        grid.anchoredPosition = new Vector2(0f, -70f);
        var template = grid.GetChild(0).gameObject;
        while (grid.childCount < Columns * Rows)
        {
            var copy = Object.Instantiate(template, grid);
            copy.name = "Slot_" + (grid.childCount - 1);
        }
        while (grid.childCount > Columns * Rows) Object.DestroyImmediate(grid.GetChild(grid.childCount - 1).gameObject);
        var slots = new List<ItemSlotUI>();
        for (int i = 0; i < grid.childCount; i++)
        {
            var cell = grid.GetChild(i);
            cell.name = "Slot_" + i;
            foreach (RectTransform child in cell)
            {
                child.anchorMin = Vector2.zero; child.anchorMax = Vector2.one; child.pivot = new Vector2(.5f, .5f);
                child.anchoredPosition = Vector2.zero;
                child.sizeDelta = child.name == "Dungeon frame" ? Vector2.zero : new Vector2(-6f, -6f);
                Record(child);
            }
            Record(cell);
            slots.Add(cell.GetComponent<ItemSlotUI>());
        }
        var uiSo = new SerializedObject(ui);
        var list = uiSo.FindProperty("slots");
        list.arraySize = slots.Count;
        for (int i = 0; i < slots.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        uiSo.ApplyModifiedPropertiesWithoutUndo();
        log.AppendLine($"{slots.Count} cells");

        // The item layer over the cells.
        var items = (RectTransform)panel.Find("Items");
        if (items == null)
        {
            items = new GameObject("Items", typeof(RectTransform)).GetComponent<RectTransform>();
            items.SetParent(panel, false);
        }
        items.SetSiblingIndex(grid.GetSiblingIndex() + 1);
        items.anchorMin = grid.anchorMin; items.anchorMax = grid.anchorMax; items.pivot = grid.pivot;
        items.anchoredPosition = grid.anchoredPosition; items.sizeDelta = grid.sizeDelta;
        var view = (RectTransform)items.Find("Item view");
        if (view == null)
        {
            view = new GameObject("Item view", typeof(RectTransform)).GetComponent<RectTransform>();
            view.SetParent(items, false);
            var honed = new GameObject("Honed fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            honed.transform.SetParent(view, false);
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(view, false);
            var cellStack = template.transform.Find("StackCount")?.GetComponent<TextMeshProUGUI>();
            var stack = Object.Instantiate(cellStack != null ? cellStack.gameObject : new GameObject("Stack", typeof(RectTransform), typeof(TextMeshProUGUI)), view);
            stack.name = "Stack";
            var srt = (RectTransform)stack.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 0f);
            srt.anchoredPosition = new Vector2(-3f, 2f);
            stack.SetActive(false);
        }
        foreach (var graphic in view.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        view.gameObject.SetActive(false);
        var gridUi = items.GetComponent<ItemGridUI>(); if (gridUi == null) gridUi = items.gameObject.AddComponent<ItemGridUI>();
        var gso = new SerializedObject(gridUi);
        gso.FindProperty("template").objectReferenceValue = view;
        var cells = gso.FindProperty("cells");
        cells.arraySize = slots.Count;
        for (int i = 0; i < slots.Count; i++) cells.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        gso.ApplyModifiedPropertiesWithoutUndo();
        ui.itemGrid = gridUi;
        EditorUtility.SetDirty(ui);

        // The panel around them.
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, 370f);
        var hint = (RectTransform)panel.Find("InventoryHint");
        if (hint != null) hint.anchoredPosition = new Vector2(hint.anchoredPosition.x, -334f);

        // The paper doll: (slot object, cells wide, cells high, centre).
        var doll = screen.Find("Equipment and stats");
        var places = new Dictionary<string, (int w, int h, Vector2 at)>
        {
            { "HELM",      (2, 2, new Vector2(0f, 22f)) },
            { "TRINKET 1", (1, 1, new Vector2(-74f, 46f)) },
            { "TRINKET 2", (1, 1, new Vector2(74f, 46f)) },
            { "ARMOR",     (2, 3, new Vector2(0f, -100f)) },
            { "PANTS",     (2, 2, new Vector2(0f, -222f)) },
            { "MAINHAND",  (2, 4, new Vector2(-140f, -48f)) },
            { "OFFHAND",   (2, 4, new Vector2(140f, -48f)) },
            { "GLOVES",    (2, 2, new Vector2(-140f, -194f)) },
            { "BOOTS",     (2, 2, new Vector2(140f, -194f)) },
        };
        foreach (var pair in places)
        {
            var slot = (RectTransform)doll.Find(pair.Key);
            if (slot == null) { log.AppendLine("no slot " + pair.Key); continue; }
            var size = new Vector2(pair.Value.w * Unit + (pair.Value.w - 1) * UnitGap, pair.Value.h * Unit + (pair.Value.h - 1) * UnitGap);
            slot.sizeDelta = size;
            slot.anchoredPosition = pair.Value.at;
            foreach (RectTransform child in slot)
            {
                child.anchorMin = child.anchorMax = new Vector2(.5f, .5f);
                switch (child.name)
                {
                    case "Frame": child.sizeDelta = size; child.anchoredPosition = Vector2.zero; break;
                    case "Honed fill": child.sizeDelta = size - Vector2.one * 6f; child.anchoredPosition = Vector2.zero; break;
                    case "Item icon":
                        child.sizeDelta = size - Vector2.one * 12f; child.anchoredPosition = Vector2.zero;
                        if (child.TryGetComponent<Image>(out var image)) image.preserveAspect = true;
                        break;
                    case "Slot label":
                        child.anchorMin = child.anchorMax = new Vector2(.5f, 0f); child.pivot = new Vector2(.5f, 0f);
                        child.sizeDelta = new Vector2(Mathf.Max(size.x, 92f), 20f); child.anchoredPosition = new Vector2(0f, pair.Value.h == 1 ? -18f : 3f);
                        if (child.TryGetComponent<TMP_Text>(out var text)) { text.fontSize = Mathf.Min(text.fontSize, 16f); text.alpha = .55f; Record(text); }
                        // A one-cell trinket slot has no room for its name.
                        child.gameObject.SetActive(pair.Value.h > 1);
                        break;
                }
                Record(child);
                if (child.TryGetComponent<Image>(out var img)) Record(img);
            }
            Record(slot);
        }
        foreach (var (name, y) in new[] { ("Food regeneration", -276f), ("Controls", -298f) })
        {
            var t = (RectTransform)doll.Find(name);
            if (t != null) t.anchoredPosition = new Vector2(t.anchoredPosition.x, y);
        }
        log.AppendLine("paper doll laid out");

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(ui.gameObject.scene);
        return log.ToString();
    }
}

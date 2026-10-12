using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Diablo bag: draws each item once across the cells it takes, over the grid of ItemSlotUI cells, and while an
// item is dragged over the grid tints the cells it would land on (green fits, red doesn't). The item view is
// authored in the scene (template, inactive); this clones it per item and lays it over the cells.
public class ItemGridUI : MonoBehaviour
{
    [SerializeField] RectTransform template;
    [SerializeField] List<ItemSlotUI> cells = new();
    [SerializeField] Color fitsTint = new(.2f, .55f, .25f, .9f);
    [SerializeField] Color blockedTint = new(.6f, .16f, .14f, .9f);
    [Tooltip("Every cell of the item under the pointer (not while dragging).")]
    [SerializeField] Color hoverTint = new(.95f, .85f, .55f, 1f);
    [SerializeField, Min(0)] float iconPadding = 6f;

    Inventory bag;
    readonly List<RectTransform> views = new();
    readonly List<ItemSlotUI> tinted = new();

    public void Bind(Inventory inventory) { bag = inventory; Refresh(); }

    public void Refresh()
    {
        if (template == null) return;
        template.gameObject.SetActive(false);
        // The cells must be where the layout puts them before items are laid over them.
        if (cells.Count > 0 && cells[0] != null && cells[0].transform.parent is RectTransform cellGrid) LayoutRebuilder.ForceRebuildLayoutImmediate(cellGrid);
        int used = 0;
        if (bag != null && bag.IsGrid)
            for (int i = 0; i < bag.SlotCount && i < cells.Count; i++)
            {
                var stack = bag[i];
                if (stack == null || stack.IsEmpty) continue;
                Place(View(used++), stack, i);
            }
        for (int i = used; i < views.Count; i++) views[i].gameObject.SetActive(false);
    }

    RectTransform View(int index)
    {
        while (views.Count <= index)
        {
            var view = Instantiate(template, template.parent);
            view.name = "Item " + views.Count;
            views.Add(view);
        }
        views[index].gameObject.SetActive(true);
        return views[index];
    }

    // Over the item's cells: from its top-left cell to its bottom-right cell.
    void Place(RectTransform view, ItemStack stack, int anchor)
    {
        var size = bag.SizeOf(stack.item);
        var at = bag.CellOf(anchor);
        int last = Mathf.Min(cells.Count - 1, (at.y + size.y - 1) * bag.GridWidth + at.x + size.x - 1);
        var first = cells[anchor].GetComponent<RectTransform>();
        var end = cells[last].GetComponent<RectTransform>();
        var parent = (RectTransform)view.parent;
        Vector3 a = parent.InverseTransformPoint(first.TransformPoint(first.rect.min)), b = parent.InverseTransformPoint(first.TransformPoint(first.rect.max));
        Vector3 c = parent.InverseTransformPoint(end.TransformPoint(end.rect.min)), d = parent.InverseTransformPoint(end.TransformPoint(end.rect.max));
        float left = Mathf.Min(a.x, c.x), right = Mathf.Max(b.x, d.x), bottom = Mathf.Min(a.y, c.y), top = Mathf.Max(b.y, d.y);
        view.anchorMin = view.anchorMax = new Vector2(.5f, .5f);
        view.pivot = new Vector2(.5f, .5f);
        view.anchoredPosition = new Vector2((left + right) * .5f, (bottom + top) * .5f) - (Vector2)parent.rect.center;
        view.sizeDelta = new Vector2(right - left, top - bottom);

        var icon = view.Find("Icon")?.GetComponent<Image>();
        if (icon != null)
        {
            icon.sprite = stack.item.icon;
            icon.enabled = stack.item.icon != null;
            icon.preserveAspect = true;
            UprightIcon(icon.rectTransform, view.sizeDelta - Vector2.one * iconPadding * 2f, size);
        }
        var honed = view.Find("Honed fill")?.GetComponent<Image>();
        if (honed != null)
        {
            honed.enabled = stack.item.honed > 0;
            if (honed.enabled) honed.color = UIManager.HasInstance ? UIManager.Instance.honed : UIManager.DefaultHoned;
            honed.rectTransform.sizeDelta = view.sizeDelta - Vector2.one * 4f;
        }
        var count = view.Find("Stack")?.GetComponent<TMP_Text>();
        if (count != null) { count.text = stack.count > 1 ? $"x{stack.count}" : ""; count.gameObject.SetActive(stack.count > 1); }
    }

    // Item icons are drawn on the diagonal (a sword from bottom left to top right). In a tall, narrow
    // footprint the icon is turned upright and sized to the height, like Diablo's long weapons.
    public static void UprightIcon(RectTransform icon, Vector2 area, Vector2Int cells)
    {
        bool tall = cells.x == 1 && cells.y >= 2;
        // Generated icons (Items/Icons/Generated) already have the footprint's shape and stand upright.
        var image = icon.GetComponent<Image>();
        if (tall && image != null && image.sprite != null && image.sprite.rect.height > image.sprite.rect.width * 1.2f) tall = false;
        icon.localEulerAngles = tall ? new Vector3(0f, 0f, 45f) : Vector3.zero;
        float side = tall ? area.y / 1.41421f : Mathf.Min(area.x, area.y);
        icon.sizeDelta = tall ? new Vector2(side, side) : area;
    }

    // The footprint under the pointer while an item is dragged; otherwise every cell of the item under the pointer.
    void Update()
    {
        foreach (var cell in tinted) if (cell != null) { cell.PreviewTint = null; cell.Refresh(); }
        tinted.Clear();
        if (bag == null || !bag.IsGrid || Mouse.current == null) return;
        Vector2 pointer = Mouse.current.position.ReadValue();
        int hovered = -1;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i] != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)cells[i].transform, pointer, null)) { hovered = i; break; }
        if (hovered < 0) return;
        if (!ItemDragHandler.HasInstance || !ItemDragHandler.Instance.IsDragging) { HoverFootprint(hovered); return; }
        var drag = ItemDragHandler.Instance;
        var item = drag.Item;
        if (item == null) return;
        var size = bag.SizeOf(item);
        var grab = drag.SourceContainer == bag ? drag.GrabOffset : Vector2Int.zero;
        var at = bag.CellOf(hovered) - grab;
        at.x = Mathf.Clamp(at.x, 0, bag.GridWidth - size.x);
        at.y = Mathf.Clamp(at.y, 0, bag.GridHeight - size.y);
        int anchor = at.y * bag.GridWidth + at.x;
        var hits = bag.Overlaps(item, anchor, drag.SourceContainer == bag ? drag.SourceIndex : -1);
        bool ok = hits.Count <= 1 && ItemSlotUI.Allowed(drag, bag, anchor, out _);
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                int index = (at.y + y) * bag.GridWidth + at.x + x;
                if (index >= cells.Count || cells[index] == null) continue;
                cells[index].PreviewTint = ok ? fitsTint : blockedTint;
                cells[index].Refresh();
                tinted.Add(cells[index]);
            }
    }

    void HoverFootprint(int hovered)
    {
        int anchor = cells[hovered].Anchor;
        var stack = anchor >= 0 ? bag[anchor] : null;
        if (stack == null || stack.IsEmpty) return;
        var size = bag.SizeOf(stack.item);
        var at = bag.CellOf(anchor);
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                int index = (at.y + y) * bag.GridWidth + at.x + x;
                if (index >= cells.Count || cells[index] == null) continue;
                cells[index].PreviewTint = hoverTint;
                cells[index].Refresh();
                tinted.Add(cells[index]);
            }
    }
}

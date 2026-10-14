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

    // The grid lights up every cell of the item under the pointer itself: a cell's own button tint would darken the
    // one cell under the pointer on top of that (and keep the last clicked cell dim).
    // It also grows the item as one piece (Pop), so a cell's own grow-on-hover (UIInteractionFeedback) is switched off.
    void Awake()
    {
        foreach (var cell in cells)
        {
            if (cell == null) continue;
            if (cell.TryGetComponent<Selectable>(out var button)) button.transition = Selectable.Transition.None;
            if (cell.TryGetComponent<UIInteractionFeedback>(out var feedback)) feedback.enabled = false;
        }
    }

    public void Refresh()
    {
        if (template == null) return;
        EndPop(true);   // the cells and pictures are about to be laid out again
        viewAt.Clear();
        template.gameObject.SetActive(false);
        // The cells must be where the layout puts them before items are laid over them.
        if (cells.Count > 0 && cells[0] != null && cells[0].transform.parent is RectTransform cellGrid) LayoutRebuilder.ForceRebuildLayoutImmediate(cellGrid);
        int used = 0;
        if (bag != null && bag.IsGrid)
            for (int i = 0; i < bag.SlotCount && i < cells.Count; i++)
            {
                var stack = bag[i];
                if (stack == null || stack.IsEmpty) continue;
                var view = View(used++);
                Place(view, stack, i);
                viewAt[i] = view;
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
        var fill = view.Find("Honed fill")?.GetComponent<Image>();
        if (fill != null)
        {
            ItemData.ShowSlotFill(fill, stack.item);
            fill.rectTransform.sizeDelta = view.sizeDelta - Vector2.one * 4f;
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
        if (bag == null || !bag.IsGrid || Mouse.current == null) { Pop(-1, false); return; }
        Vector2 pointer = Mouse.current.position.ReadValue();
        int hovered = -1;
        for (int i = 0; i < cells.Count; i++)
            if (cells[i] != null && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)cells[i].transform, pointer, null)) { hovered = i; break; }
        bool dragging = ItemDragHandler.HasInstance && ItemDragHandler.Instance.IsDragging;
        Pop(hovered, dragging);
        if (hovered < 0) return;
        if (!dragging) { HoverFootprint(hovered); return; }
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
        if (stack == null || stack.IsEmpty)
        {
            // An empty cell lights up on its own.
            cells[hovered].PreviewTint = hoverTint;
            cells[hovered].Refresh();
            tinted.Add(cells[hovered]);
            return;
        }
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

    // ── Hover pop ─────────────────────────────────────────────────────
    // The item under the pointer grows as one piece: all its cells and its picture, about the middle of its footprint
    // (an empty cell on its own), and settles back when the pointer leaves.

    sealed class PopGroup
    {
        public readonly List<(Transform part, Vector3 position, Vector3 scale)> parts = new();
        public Transform space;   // the cells' parent, which the middle is kept in
        public Vector3 middle;
        public float amount = 1f;
        public int key;
    }

    readonly Dictionary<int, RectTransform> viewAt = new();   // anchor cell -> the item's picture
    readonly List<PopGroup> settling = new();
    PopGroup popped;
    int popKey = int.MinValue;

    void Pop(int hovered, bool dragging)
    {
        int key = int.MinValue;
        if (hovered >= 0 && !dragging)
        {
            int anchor = cells[hovered].Anchor;
            var stack = anchor >= 0 ? bag[anchor] : null;
            key = stack != null && !stack.IsEmpty ? anchor : -1 - hovered;
        }
        if (key != popKey)
        {
            EndPop(false);
            popKey = key;
            if (key != int.MinValue)
            {
                // Back over one still settling: it grows again from where it is.
                popped = settling.Find(g => g.key == key);
                if (popped != null) settling.Remove(popped);
                else popped = Group(key, hovered);
            }
        }
        var settings = UIFeedbackSettings.Shared;
        float duration = settings != null ? settings.hoverDuration : .1f;
        float step = Time.unscaledDeltaTime / Mathf.Max(.01f, duration);
        if (popped != null)
        {
            bool pressed = Mouse.current != null && (Mouse.current.leftButton.isPressed || Mouse.current.rightButton.isPressed);
            float target = settings != null ? (pressed ? settings.pressScale : settings.hoverScale) : 1.035f;
            popped.amount = Mathf.MoveTowards(popped.amount, target, step * Mathf.Max(.02f, Mathf.Abs(target - 1f)));
            Apply(popped);
        }
        for (int i = settling.Count - 1; i >= 0; i--)
        {
            var g = settling[i];
            g.amount = Mathf.MoveTowards(g.amount, 1f, step * Mathf.Max(.02f, Mathf.Abs(g.amount - 1f) + .02f));
            Apply(g);
            if (Mathf.Approximately(g.amount, 1f)) settling.RemoveAt(i);
        }
    }

    PopGroup Group(int key, int hovered)
    {
        var group = new PopGroup { key = key };
        int first, last;
        if (key >= 0)
        {
            var size = bag.SizeOf(bag[key].item);
            var at = bag.CellOf(key);
            first = key; last = Mathf.Min(cells.Count - 1, (at.y + size.y - 1) * bag.GridWidth + at.x + size.x - 1);
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                {
                    int index = (at.y + y) * bag.GridWidth + at.x + x;
                    if (index < cells.Count && cells[index] != null) group.parts.Add((cells[index].transform, cells[index].transform.localPosition, cells[index].transform.localScale));
                }
            if (viewAt.TryGetValue(key, out var view) && view != null) group.parts.Add((view, view.localPosition, view.localScale));
        }
        else
        {
            first = last = hovered;
            group.parts.Add((cells[hovered].transform, cells[hovered].transform.localPosition, cells[hovered].transform.localScale));
        }
        // A part still settling from another item (the bag changed under the pointer) is put back first, so its resting
        // place is what is remembered.
        for (int i = settling.Count - 1; i >= 0; i--)
            if (settling[i].parts.Exists(a => group.parts.Exists(b => b.part == a.part)))
            {
                settling[i].amount = 1f; Apply(settling[i]); settling.RemoveAt(i);
                for (int k = 0; k < group.parts.Count; k++) { var t = group.parts[k].part; group.parts[k] = (t, t.localPosition, t.localScale); }
            }
        group.space = cells[first].transform.parent;
        group.middle = (cells[first].transform.localPosition + cells[last].transform.localPosition) * .5f;
        var settings = UIFeedbackSettings.Shared; settings?.Hover();
        return group;
    }

    static void Apply(PopGroup g)
    {
        if (g.space == null) return;
        var middle = g.space.TransformPoint(g.middle);
        foreach (var (part, position, scale) in g.parts)
        {
            if (part == null || part.parent == null) continue;
            var local = part.parent.InverseTransformPoint(middle);
            part.localScale = scale * g.amount;
            part.localPosition = local + (position - local) * g.amount;
        }
    }

    void EndPop(bool instant)
    {
        if (popped != null)
        {
            if (instant) { popped.amount = 1f; Apply(popped); }
            else settling.Add(popped);
        }
        popped = null; popKey = int.MinValue;
        if (instant) { foreach (var g in settling) { g.amount = 1f; Apply(g); } settling.Clear(); }
    }

    void OnDisable() => EndPop(true);
}

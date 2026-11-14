using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The merchant screen. Left: the merchant's wares, supplies (consumables) above equipment. Right: the
// player's pack like the inventory: the hotbar row, then the bag's whole grid, each bag item drawn once across the
// cells it takes (bagItemTemplate, cloned per item, like ItemGridUI). Below: the hovered item. A ware is bought with
// either mouse button or by dragging it onto the pack; a pack item is sold with a right click or by dragging it onto
// the wares, and dragged onto another cell of the pack it moves there as in the bag (the drop zones are the areas the
// rows and the cells cover).
// Rows and cells are authored in the scene; this only fills them.
public class ShopUI : Singleton<ShopUI>
{
    [SerializeField] GameObject root;
    [SerializeField] TMP_Text title;
    [SerializeField] TMP_Text gold;
    [SerializeField] Image detailsIcon;
    [SerializeField] TMP_Text detailsName;
    [SerializeField] TMP_Text details;
    [SerializeField] TMP_Text detailsPrice;
    [SerializeField] TMP_Text feedback;
    [SerializeField] Button closeButton;
    [Header("Wares")]
    [SerializeField] ShopRowUI[] supplyRows;
    [SerializeField] GameObject suppliesEmpty;
    [SerializeField] ShopRowUI[] gearRows;
    [SerializeField] GameObject gearEmpty;
    [Header("Pack")]
    [Tooltip("The hotbar's cells, then the bag's cells row by row (its grid width across).")]
    [SerializeField] ShopSlotUI[] packSlots;
    [SerializeField, Min(0), Tooltip("How many of Pack Slots are the hotbar row.")] int hotbarCells = 6;
    [SerializeField, Tooltip("A bag item over its cells (inactive; children Icon, Honed fill, Stack). Empty: each item's icon sits in its top-left cell.")]
    RectTransform bagItemTemplate;
    [SerializeField, Min(0)] float iconPadding = 4f;
    [Tooltip("The tavern keeper's tabs (Wares, Food, Rumours); hidden for a plain merchant.")]
    [SerializeField] TavernTabsUI tavern;
    [Tooltip("Price line of a ware the player can't afford.")]
    [SerializeField] Color tooDearColor = new(.69f, .353f, .306f);
    // Brighter than the bag's: the pack's grey cell sprite darkens a tint more (the bag's colours were hard to see here).
    [Tooltip("Pack cells a dragged pack item would land on, when it fits there.")]
    [SerializeField] Color fitsTint = new(.45f, 1f, .5f, 1f);
    [Tooltip("... and when it doesn't.")]
    [SerializeField] Color blockedTint = new(1f, .38f, .32f, 1f);

    Merchant merchant;
    Vector4[] pads;
    readonly List<ShopSlotUI> tinted = new();
    Player player;
    bool open;
    readonly List<(Inventory container, int slot)> packMap = new();
    readonly List<RectTransform> bagViews = new();
    public bool IsOpen => open;

    protected override void Awake()
    {
        base.Awake();
        if (root != null) root.SetActive(false);
        closeButton?.onClick.AddListener(Close);
    }

    void Start()
    {
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed += OnCancel;
    }

    // Disabled or destroyed while open: give back the Menu state and drop the pack subscriptions.
    void OnDisable() => Close();

    protected override void OnDestroy()
    {
        Close();
        base.OnDestroy();
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= OnCancel;
    }

    void OnCancel() { if (open) Close(); }

    public void Open(Merchant m, Player p)
    {
        if (root == null || m == null || p == null) return;
        if (!open && GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        open = true;
        merchant = m; player = p;
        root.SetActive(true);
        if (title != null) title.text = m.merchantName;
        if (feedback != null) feedback.text = "";
        ShowDetails(null);
        if (player.Bag != null) player.Bag.Changed += Refresh;
        if (player.Hotbar != null) player.Hotbar.Changed += Refresh;
        if (player.Wallet != null) player.Wallet.Changed += OnGold;
        Refresh();
        // The pack's cells answer across the gaps between them, so a big bag item has no dead spots (ItemGridUI). Measured
        // here, with the screen up and laid out: at Awake the cells did not stand where they are drawn yet.
        if (packSlots != null) pads = ItemGridUI.CoverGaps(packSlots);
        if (tavern != null) tavern.Bind(m, p);
        // With a pad, a ware is selected at once (selecting shows it in the details, so the mouse keeps the help text).
        if (InputManager.PadConnected) SelectStart();
    }

    // Nothing selected and a pad's d-pad or an arrow key pressed: select a ware.
    void Update()
    {
        if (!open) return;
        // The wares, the pack's cells, the tabs and Leave: linked by where they sit, so keys and a pad move between them
        // (the rows and cells had no navigation of their own) and stay on this screen.
        if (root != null) { root.GetComponentsInChildren(false, focusable); MenuFocus.LinkNearest(focusable); MenuFocus.Tint(focusable); }
        if (InputManager.MenuInputStarted && InputManager.NothingSelectedIn(root != null ? root.transform : null)) SelectStart();
        Preview();
    }
    readonly System.Collections.Generic.List<Selectable> focusable = new();

    // The first ware, else the first pack cell, else Close, so Submit buys or sells.
    void SelectStart()
    {
        Selectable pick = FirstSelectable(supplyRows);
        if (pick == null) pick = FirstSelectable(gearRows);
        if (pick == null) pick = FirstSelectable(packSlots);
        if (pick == null) pick = closeButton;
        if (pick != null) pick.Select();
    }

    static Selectable FirstSelectable<T>(T[] parts) where T : Component
    {
        if (parts == null) return null;
        foreach (var p in parts)
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            var s = p.GetComponentInChildren<Selectable>();
            if (s != null && s.IsInteractable()) return s;
        }
        return null;
    }

    // A bought ware's row can go away under the selection: pick again so a pad isn't left with nothing.
    void KeepSelection()
    {
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events == null || !InputManager.PadConnected) return;
        var selected = events.currentSelectedGameObject;
        if (selected != null && !selected.activeInHierarchy) SelectStart();
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (player != null)
        {
            if (player.Bag != null) player.Bag.Changed -= Refresh;
            if (player.Hotbar != null) player.Hotbar.Changed -= Refresh;
            if (player.Wallet != null) player.Wallet.Changed -= OnGold;
        }
        // A ware or cell left selected would stay selected while hidden.
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null && root != null && events.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(root.transform)
            && !events.alreadySelecting) events.SetSelectedGameObject(null);
        if (tavern != null) tavern.Unbind();
        if (root != null) root.SetActive(false);
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        merchant = null; player = null;
    }

    void OnGold(int total, int delta) => Refresh();

    int Coins => player != null && player.Wallet != null ? player.Wallet.Gold : 0;

    void Refresh()
    {
        if (!open || merchant == null || player == null) return;
        if (gold != null) gold.text = $"{Coins} {merchant.Currency}";

        var wares = merchant.Wares;
        int supply = 0, gear = 0;
        for (int i = 0; i < wares.Count; i++)
        {
            var rows = wares[i].item.IsConsumable ? supplyRows : gearRows;
            int at = wares[i].item.IsConsumable ? supply++ : gear++;
            if (rows == null || at >= rows.Length) continue;
            int index = i;
            var ware = wares[i];
            rows[at].Bind(ware.item, ware.count, $"{ware.price}", Coins >= ware.price, () => Buy(index), hovered => ShowDetails(hovered, true));
        }
        ClearFrom(supplyRows, supply);
        ClearFrom(gearRows, gear);
        if (suppliesEmpty != null) suppliesEmpty.SetActive(supply == 0);
        if (gearEmpty != null) gearEmpty.SetActive(gear == 0);

        // Each cell answers for the item over it: the hotbar slot, or the bag item whose footprint covers the cell.
        packMap.Clear();
        var bag = player.Bag;
        for (int i = 0; i < (packSlots?.Length ?? 0); i++)
        {
            if (i < hotbarCells) packMap.Add((player.Hotbar, player.Hotbar != null && i < player.Hotbar.SlotCount ? i : -1));
            else { int cell = i - hotbarCells; packMap.Add((bag, bag != null && cell < bag.SlotCount ? bag.OwnerAt(cell) : -1)); }
        }
        bool drawBag = bagItemTemplate != null && bag != null && bag.IsGrid;
        for (int i = 0; i < packMap.Count; i++)
        {
            // Every cell shows, empty ones too, so the pack keeps the inventory grid's shape.
            packSlots[i].gameObject.SetActive(true);
            var (container, slot) = packMap[i];
            var item = container != null && slot >= 0 ? container.ItemAt(slot) : null;
            if (item == null) { packSlots[i].Clear(); continue; }
            int index = i;
            // Bag items are drawn over their cells (DrawBag); the cells only take the clicks.
            bool inBag = i >= hotbarCells;
            packSlots[i].Bind(item, item.IsScroll ? container.ChargesAt(slot) : container.CountAt(slot), merchant.CanSell(player, item, out _), () => Sell(index), hovered => ShowDetails(hovered, false),
                drawItem: !(inBag && drawBag) && (!inBag || slot == i - hotbarCells));
        }
        DrawBag(drawBag);
        if (tavern != null) tavern.AfterShopRefresh();
    }

    // One view per bag item, from its top-left cell to its bottom-right cell (ItemGridUI.Place does the same in the bag).
    void DrawBag(bool draw)
    {
        int used = 0;
        if (draw)
        {
            var bag = player.Bag;
            bagItemTemplate.gameObject.SetActive(false);
            var parent = (RectTransform)bagItemTemplate.parent;
            if (packSlots.Length > hotbarCells && packSlots[hotbarCells].transform.parent is RectTransform grid) LayoutRebuilder.ForceRebuildLayoutImmediate(grid);
            for (int anchor = 0; anchor < bag.SlotCount && hotbarCells + anchor < packSlots.Length; anchor++)
            {
                var stack = bag[anchor];
                if (stack == null || stack.IsEmpty) continue;
                var size = bag.SizeOf(stack.item);
                var at = bag.CellOf(anchor);
                int last = Mathf.Min(bag.SlotCount - 1, (at.y + size.y - 1) * bag.GridWidth + at.x + size.x - 1);
                last = Mathf.Min(last, packSlots.Length - 1 - hotbarCells);
                var first = (RectTransform)packSlots[hotbarCells + anchor].transform;
                var end = (RectTransform)packSlots[hotbarCells + last].transform;
                Vector3 a = parent.InverseTransformPoint(first.TransformPoint(first.rect.min)), b = parent.InverseTransformPoint(first.TransformPoint(first.rect.max));
                Vector3 c = parent.InverseTransformPoint(end.TransformPoint(end.rect.min)), d = parent.InverseTransformPoint(end.TransformPoint(end.rect.max));
                float left = Mathf.Min(a.x, c.x), right = Mathf.Max(b.x, d.x), bottom = Mathf.Min(a.y, c.y), top = Mathf.Max(b.y, d.y);
                var view = BagView(used++);
                view.anchorMin = view.anchorMax = view.pivot = new Vector2(.5f, .5f);
                view.anchoredPosition = new Vector2((left + right) * .5f, (bottom + top) * .5f) - parent.rect.center;
                view.sizeDelta = new Vector2(right - left, top - bottom);
                bool available = merchant.CanSell(player, stack.item, out _);
                var icon = view.Find("Icon")?.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = stack.item.icon; icon.enabled = icon.sprite != null; icon.preserveAspect = true;
                    icon.color = available ? Color.white : new Color(1, 1, 1, .4f);
                    ItemGridUI.UprightIcon(icon.rectTransform, view.sizeDelta - Vector2.one * iconPadding * 2f, size);
                }
                var fill = view.Find("Honed fill")?.GetComponent<Image>();
                if (fill != null) { ItemData.ShowSlotFill(fill, stack.item); fill.rectTransform.sizeDelta = view.sizeDelta - Vector2.one * 4f; }
                // A scroll shows the charges it has left (when more than one), as in the bag.
                int n = stack.item.IsScroll ? bag.ChargesAt(anchor) : stack.count;
                var count = view.Find("Stack")?.GetComponent<TMP_Text>();
                if (count != null) { count.text = n > 1 ? $"x{n}" : ""; count.gameObject.SetActive(n > 1); }
            }
        }
        for (int i = used; i < bagViews.Count; i++) bagViews[i].gameObject.SetActive(false);
    }

    RectTransform BagView(int index)
    {
        while (bagViews.Count <= index)
        {
            var view = Instantiate(bagItemTemplate, bagItemTemplate.parent);
            view.name = "Item " + bagViews.Count;
            // The cells under it take the clicks and the hover.
            foreach (var g in view.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            bagViews.Add(view);
        }
        bagViews[index].gameObject.SetActive(true);
        return bagViews[index];
    }

    static void ClearFrom(ShopRowUI[] rows, int from)
    {
        for (int i = from; i < (rows?.Length ?? 0); i++) rows[i].Clear();
    }

    void Buy(int index)
    {
        if (merchant == null) return;
        merchant.Buy(index, player, out string message);
        Say(message);
        // The item under the mouse has gone or changed: its tooltip goes until the mouse moves onto something again.
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        ShowDetails(null);
        Refresh();
        KeepSelection();
    }

    void Sell(int index)
    {
        if (merchant == null || index >= packMap.Count) return;
        var (container, slot) = packMap[index];
        if (container == null || slot < 0) return;
        merchant.Sell(player, container, slot, out string message);
        Say(message);
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        ShowDetails(container.ItemAt(slot), false);
        Refresh();
    }

    // The tavern's tabs: fill the wares again (back on the Wares tab), and a line in the feedback spot.
    public void RefreshWares() => Refresh();
    // The line at the foot while nothing is hovered: the wares' own, or a tavern tab's (null puts the wares' back).
    const string WaresHelp = "Hover over an item to see it. Click a ware or drag it to your bag to buy it. Right click something in your bag or drag it to the wares to sell it.";
    string help;
    public void SetHelp(string text) { help = text; ShowDetails(null); }
    public void Feedback(string message) => Say(message);

    void Say(string message)
    {
        if (feedback != null && !string.IsNullOrEmpty(message)) feedback.text = message;
    }

    // ── Dragging between the wares and the pack ─────────────────────

    public enum DropZone { None, Wares, Pack }
    readonly Vector3[] corners = new Vector3[4];

    // A ware: the ghost follows the pointer (ItemDragHandler's, as in the inventory); false when there is no shop open.
    public bool BeginDrag(ItemData item, Image from)
    {
        if (!open || item == null) return false;
        var size = from != null ? from.rectTransform.rect.size : new Vector2(64, 64);
        if (ItemDragHandler.HasInstance) ItemDragHandler.Instance.BeginGhost(item, size);
        return true;
    }

    // A pack item: held by the grabbed cell, from its own stack, so a drop on the pack moves it as the bag does (the user,
    // 2026-11-14: items can be moved about in the shop too) and a drop on the wares sells it.
    public bool BeginPackDrag(ShopSlotUI slot)
    {
        int i = packSlots != null ? System.Array.IndexOf(packSlots, slot) : -1;
        if (!open || i < 0 || i >= packMap.Count || !ItemDragHandler.HasInstance) return false;
        var (container, anchor) = packMap[i];
        var item = container != null && anchor >= 0 ? container.ItemAt(anchor) : null;
        if (item == null) return false;
        // The cell's size on the canvas the ghost is drawn on (the pack's cells are scaled).
        var rt = (RectTransform)slot.transform;
        rt.GetWorldCorners(corners);
        var canvas = slot.GetComponentInParent<Canvas>();
        float k = canvas != null ? Mathf.Max(.0001f, canvas.rootCanvas.transform.lossyScale.x) : 1f;
        var cell = new Vector2(corners[2].x - corners[0].x, corners[2].y - corners[0].y) / k;
        var size = container.SizeOf(item);
        var grab = container.IsGrid ? container.CellOf(i - hotbarCells) - container.CellOf(anchor) : Vector2Int.zero;
        ItemDragHandler.Instance.BeginFrom(container, anchor, item, new Vector2(cell.x * size.x, cell.y * size.y), cell, grab);
        return true;
    }

    public DropZone EndDrag(Vector2 screen)
    {
        var drag = ItemDragHandler.HasInstance ? ItemDragHandler.Instance : null;
        var zone = !open ? DropZone.None : Covers(packSlots, screen) ? DropZone.Pack
            : Covers(supplyRows, screen) || Covers(gearRows, screen) ? DropZone.Wares : DropZone.None;
        // A pack item let go on a cell of the pack: moved there by the bag's rules (swaps, refusals and all).
        if (zone == DropZone.Pack && drag != null && drag.IsDragging && drag.SourceContainer != null)
        {
            int i = PackCellAt(screen);
            Inventory container = null; int cell = -1;
            if (i >= 0) (container, cell) = CellAt(i);
            if (container != null && cell >= 0 && cell < container.SlotCount) ItemSlotUI.DropOnto(drag, container, cell);
        }
        drag?.End();
        ClearTints();
        return zone;
    }

    // Pack cell i as a container and its cell: the hotbar's, then the bag's row by row.
    (Inventory container, int cell) CellAt(int i) => player == null ? (null, -1) : i < hotbarCells ? (player.Hotbar, i) : (player.Bag, i - hotbarCells);

    // The pack cell under a screen point (each cell answers across the gaps round it), -1 for none.
    int PackCellAt(Vector2 screen)
    {
        for (int i = 0; i < (packSlots?.Length ?? 0); i++)
            if (packSlots[i] != null && packSlots[i].gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)packSlots[i].transform, screen, null, pads != null && i < pads.Length ? pads[i] : Vector4.zero))
                return i;
        return -1;
    }

    // While a pack item is dragged over the pack, the cells it would land on: green fits, red doesn't (as in the bag).
    void Preview()
    {
        ClearTints();
        var drag = ItemDragHandler.HasInstance ? ItemDragHandler.Instance : null;
        if (drag == null || !drag.IsDragging || drag.SourceContainer == null || drag.Item == null || Mouse.current == null) return;
        int i = PackCellAt(Mouse.current.position.ReadValue());
        if (i < 0) return;
        var (dst, cell) = CellAt(i);
        if (dst == null || cell < 0 || cell >= dst.SlotCount) return;
        if (!dst.IsGrid) { TintCell(i, dst.Accepts(drag.Item) && ItemSlotUI.Allowed(drag, dst, cell, out _)); return; }
        int anchor = ItemSlotUI.TargetFor(drag, dst, cell);
        var size = dst.SizeOf(drag.Item);
        var hits = dst.Overlaps(drag.Item, anchor, drag.SourceContainer == dst ? drag.SourceIndex : -1);
        bool ok = dst.Accepts(drag.Item) && hits.Count <= 1 && ItemSlotUI.Allowed(drag, dst, anchor, out _);
        var at = dst.CellOf(anchor);
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                int index = hotbarCells + (at.y + y) * dst.GridWidth + at.x + x;
                if (index < packSlots.Length) TintCell(index, ok);
            }
    }

    void TintCell(int i, bool ok)
    {
        if (packSlots[i] == null) return;
        packSlots[i].Tint(ok ? fitsTint : blockedTint);
        tinted.Add(packSlots[i]);
    }

    void ClearTints()
    {
        foreach (var s in tinted) if (s != null) s.Tint(null);
        tinted.Clear();
    }

    // Inside the box round every element (empty rows and cells count: the zone keeps its shape), with a margin.
    bool Covers<T>(T[] parts, Vector2 screen) where T : Component
    {
        if (parts == null) return false;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in parts)
        {
            if (p == null) continue;
            ((RectTransform)p.transform).GetWorldCorners(corners);
            foreach (var c in corners) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y); }
        }
        const float margin = 24f;
        return screen.x >= minX - margin && screen.x <= maxX + margin && screen.y >= minY - margin && screen.y <= maxY + margin;
    }

    // The hovered item: big icon, name, its tooltip, and what it costs or fetches here.
    // The box under the wares holds one line: what the item does (the first sentence of its first line), not its whole
    // tooltip, which was cut to "..."; the tooltip beside the row says the rest.
    static string Summary(ItemData item)
    {
        string text = item.BuildTooltip(true, false, false);
        int cut = text.IndexOf('\n');
        if (cut > 0) text = text.Substring(0, cut);
        cut = text.IndexOf(". ", System.StringComparison.Ordinal);   // "4-6 Weapon damage. Your own damage, ..." -> its first sentence
        return cut > 0 ? text.Substring(0, cut) : text;
    }

    void ShowDetails(ItemData item, bool ware = false)
    {
        bool any = item != null;
        if (detailsIcon != null) { detailsIcon.sprite = any ? item.icon : null; detailsIcon.enabled = detailsIcon.sprite != null; }
        if (detailsName != null) detailsName.text = any ? item.itemName : "";
        if (details != null) details.text = any ? Summary(item) : help ?? WaresHelp;
        if (detailsPrice == null || merchant == null) return;
        if (!any) { detailsPrice.text = ""; return; }
        if (ware)
        {
            int price = merchant.PriceOf(item);
            detailsPrice.text = Coins >= price ? $"Buy for {price} {merchant.Currency}" : $"<color=#{ColorUtility.ToHtmlStringRGB(tooDearColor)}>Costs {price} {merchant.Currency}</color>";
        }
        else detailsPrice.text = merchant.CanSell(player, item, out string reason) ? $"Sells for {merchant.SellPriceOf(item)} {merchant.Currency}" : reason;
    }
}

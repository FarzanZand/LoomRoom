using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The merchant screen. Left: the merchant's wares, supplies (consumables) above equipment. Right: the
// player's pack like the inventory: the hotbar row, then the bag's whole grid, each bag item drawn once across the
// cells it takes (bagItemTemplate, cloned per item, like ItemGridUI). Below: the hovered item.
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
    [Tooltip("Price line of a ware the player can't afford.")]
    [SerializeField] Color tooDearColor = new(.69f, .353f, .306f);

    Merchant merchant;
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
        if (root != null) root.SetActive(false);
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
            packSlots[i].Bind(item, container.CountAt(slot), merchant.CanSell(player, item, out _), () => Sell(index), hovered => ShowDetails(hovered, false),
                drawItem: !(inBag && drawBag) && (!inBag || slot == i - hotbarCells));
        }
        DrawBag(drawBag);
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
                var count = view.Find("Stack")?.GetComponent<TMP_Text>();
                if (count != null) { count.text = stack.count > 1 ? $"x{stack.count}" : ""; count.gameObject.SetActive(stack.count > 1); }
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
        ShowDetails(null);
        Refresh();
    }

    void Sell(int index)
    {
        if (merchant == null || index >= packMap.Count) return;
        var (container, slot) = packMap[index];
        if (container == null || slot < 0) return;
        merchant.Sell(player, container, slot, out string message);
        Say(message);
        ShowDetails(container.ItemAt(slot), false);
        Refresh();
    }

    void Say(string message)
    {
        if (feedback != null && !string.IsNullOrEmpty(message)) feedback.text = message;
    }

    // The hovered item: big icon, name, its tooltip, and what it costs or fetches here.
    void ShowDetails(ItemData item, bool ware = false)
    {
        bool any = item != null;
        if (detailsIcon != null) { detailsIcon.sprite = any ? item.icon : null; detailsIcon.enabled = detailsIcon.sprite != null; }
        if (detailsName != null) detailsName.text = any ? item.itemName : "";
        if (details != null) details.text = any ? item.BuildTooltip() : "Hover over an item to see it. Click a ware to buy it; right-click something in your pack to sell it.";
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

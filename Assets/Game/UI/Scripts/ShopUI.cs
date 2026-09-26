using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The merchant screen. Left: the merchant's wares. Right: the player's bag and hotbar.
// Both lists use authored rows and page through when there are more items than rows.
public class ShopUI : Singleton<ShopUI>
{
    [SerializeField] GameObject root;
    [SerializeField] TMP_Text title;
    [SerializeField] TMP_Text gold;
    [SerializeField] TMP_Text details;
    [SerializeField] TMP_Text feedback;
    [SerializeField] Button closeButton;
    [Header("Buy")]
    [SerializeField] ShopRowUI[] buyRows;
    [SerializeField] Button buyPrev, buyNext;
    [SerializeField] TMP_Text buyPage;
    [Header("Sell")]
    [SerializeField] ShopRowUI[] sellRows;
    [SerializeField] Button sellPrev, sellNext;
    [SerializeField] TMP_Text sellPage;

    Merchant merchant;
    Player player;
    int buyOffset, sellOffset;
    bool open;
    readonly List<(Inventory container, int slot)> sellable = new();
    public bool IsOpen => open;

    protected override void Awake()
    {
        base.Awake();
        if (root != null) root.SetActive(false);
        closeButton?.onClick.AddListener(Close);
        buyPrev?.onClick.AddListener(() => Page(ref buyOffset, -1, buyRows));
        buyNext?.onClick.AddListener(() => Page(ref buyOffset, 1, buyRows));
        sellPrev?.onClick.AddListener(() => Page(ref sellOffset, -1, sellRows));
        sellNext?.onClick.AddListener(() => Page(ref sellOffset, 1, sellRows));
    }

    void Start()
    {
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed += OnCancel;
    }

    protected override void OnDestroy()
    {
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
        buyOffset = sellOffset = 0;
        root.SetActive(true);
        if (title != null) title.text = m.merchantName;
        if (feedback != null) feedback.text = "";
        if (details != null) details.text = "";
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

    void Page(ref int offset, int direction, ShopRowUI[] rows)
    {
        int size = rows != null ? Mathf.Max(1, rows.Length) : 1;
        offset = Mathf.Max(0, offset + direction * size);
        Refresh();
    }

    void Refresh()
    {
        if (!open || merchant == null || player == null) return;
        int coins = player.Wallet != null ? player.Wallet.Gold : 0;
        if (gold != null) gold.text = $"{coins} {merchant.Currency}";

        var wares = merchant.Wares;
        buyOffset = Clamp(buyOffset, wares.Count, buyRows);
        for (int i = 0; i < (buyRows?.Length ?? 0); i++)
        {
            int index = buyOffset + i;
            var row = buyRows[i];
            if (index >= wares.Count) { row.Clear(); continue; }
            var ware = wares[index];
            row.Bind(ware.item, ware.count, $"{ware.price}", coins >= ware.price, () => Buy(index), ShowDetails);
        }
        PageLabel(buyPage, buyPrev, buyNext, buyOffset, wares.Count, buyRows);

        sellable.Clear();
        Collect(player.Hotbar);
        Collect(player.Bag);
        sellOffset = Clamp(sellOffset, sellable.Count, sellRows);
        for (int i = 0; i < (sellRows?.Length ?? 0); i++)
        {
            int index = sellOffset + i;
            var row = sellRows[i];
            if (index >= sellable.Count) { row.Clear(); continue; }
            var (container, slot) = sellable[index];
            var item = container.ItemAt(slot);
            bool allowed = merchant.CanSell(player, item, out _);
            row.Bind(item, container.CountAt(slot), $"+{merchant.SellPriceOf(item)}", allowed, () => Sell(index), ShowDetails);
        }
        PageLabel(sellPage, sellPrev, sellNext, sellOffset, sellable.Count, sellRows);
    }

    void Collect(Inventory container)
    {
        if (container == null) return;
        for (int i = 0; i < container.SlotCount; i++)
            if (container.ItemAt(i) != null) sellable.Add((container, i));
    }

    static int Clamp(int offset, int count, ShopRowUI[] rows)
    {
        int size = rows != null ? Mathf.Max(1, rows.Length) : 1;
        int last = count <= 0 ? 0 : (count - 1) / size * size;
        return Mathf.Clamp(offset, 0, last);
    }

    static void PageLabel(TMP_Text label, Button prev, Button next, int offset, int count, ShopRowUI[] rows)
    {
        int size = rows != null ? Mathf.Max(1, rows.Length) : 1;
        int pages = Mathf.Max(1, Mathf.CeilToInt(count / (float)size));
        if (label != null) label.text = pages > 1 ? $"{offset / size + 1} / {pages}" : "";
        if (prev != null) prev.gameObject.SetActive(pages > 1);
        if (next != null) next.gameObject.SetActive(pages > 1);
        if (prev != null) prev.interactable = offset > 0;
        if (next != null) next.interactable = offset + size < count;
    }

    void Buy(int index)
    {
        if (merchant == null) return;
        merchant.Buy(index, player, out string message);
        Say(message);
        Refresh();
    }

    void Sell(int index)
    {
        if (merchant == null || index >= sellable.Count) return;
        var (container, slot) = sellable[index];
        merchant.Sell(player, container, slot, out string message);
        Say(message);
        Refresh();
    }

    void Say(string message)
    {
        if (feedback != null && !string.IsNullOrEmpty(message)) feedback.text = message;
    }

    void ShowDetails(ItemData item)
    {
        if (details == null) return;
        details.text = item == null ? "" : $"<b>{item.itemName}</b>\n{item.BuildTooltip()}";
    }
}

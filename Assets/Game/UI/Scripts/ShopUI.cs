using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The merchant screen. Left: the merchant's wares, supplies (consumables) above equipment. Right: the
// player's pack as a grid like the inventory (hotbar row first, then the bag). Below: the hovered item.
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
    [SerializeField] ShopSlotUI[] packSlots;

    Merchant merchant;
    Player player;
    bool open;
    readonly List<(Inventory container, int slot)> packMap = new();
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

        packMap.Clear();
        Map(player.Hotbar);
        Map(player.Bag);
        for (int i = 0; i < (packSlots?.Length ?? 0); i++)
        {
            if (i >= packMap.Count) { packSlots[i].gameObject.SetActive(false); continue; }
            packSlots[i].gameObject.SetActive(true);
            var (container, slot) = packMap[i];
            var item = container.ItemAt(slot);
            if (item == null) { packSlots[i].Clear(); continue; }
            int index = i;
            packSlots[i].Bind(item, container.CountAt(slot), merchant.CanSell(player, item, out _), () => Sell(index), hovered => ShowDetails(hovered, false));
        }
    }

    static void ClearFrom(ShopRowUI[] rows, int from)
    {
        for (int i = from; i < (rows?.Length ?? 0); i++) rows[i].Clear();
    }

    void Map(Inventory container)
    {
        if (container == null) return;
        for (int i = 0; i < container.SlotCount; i++) packMap.Add((container, i));
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
        if (details != null) details.text = any ? item.BuildTooltip() : "Hover over an item to see it. Click a ware to buy it, or something in your pack to sell it.";
        if (detailsPrice == null || merchant == null) return;
        if (!any) { detailsPrice.text = ""; return; }
        if (ware)
        {
            int price = merchant.PriceOf(item);
            detailsPrice.text = Coins >= price ? $"Buy for {price} {merchant.Currency}" : $"<color=#B05A4E>Costs {price} {merchant.Currency}</color>";
        }
        else detailsPrice.text = merchant.CanSell(player, item, out string reason) ? $"Sells for {merchant.SellPriceOf(item)} {merchant.Currency}" : reason;
    }
}

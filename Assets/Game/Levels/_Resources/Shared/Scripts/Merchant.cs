using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A dungeon trader. Stock is rolled once, the first time the player talks to them: a few
// staples plus rolls from a loot table. Prices come from ItemData.value (CurrencyManager
// fallback), scaled by depth and this merchant's markup. Things the player sells are
// added to the stock at the merchant's price.
public class Merchant : MonoBehaviour, IInteractable
{
    public string merchantName = "Wandering Merchant";
    [TextArea(1, 2)]
    public string[] greetings =
    {
        "\"Coin for wares, friend. The dead don't haggle, but I do.\"",
        "\"Down here? Everything's for sale. Everything.\"",
        "\"Mind the blood on the goods. It washes out. Mostly.\"",
    };
    [Tooltip("Always stocked, e.g. food.")]
    public ItemData[] staples = new ItemData[0];
    [Min(1)] public int stapleCount = 3;
    [Tooltip("Rolled as Chest rewards for the rest of the stock. The generator sets this from the level when empty.")]
    public DungeonLootTable stockTable;
    [Min(0)] public int stockRolls = 5;
    [Range(.5f, 3f)] public float priceMultiplier = 1.2f;
    [Range(0f, .5f), Tooltip("Price increase per floor beyond the first.")]
    public float pricePerFloor = .1f;
    public bool buysItems = true;
    public AudioClip tradeClip;
    [Range(0, 1)] public float tradeVolume = .9f;

    [HideInInspector] public int seed;
    [HideInInspector] public int floorNumber = 1;

    public class Ware
    {
        public ItemData item;
        public int count;
        public int price;
    }

    readonly List<Ware> wares = new();
    bool stocked;
    public IReadOnlyList<Ware> Wares { get { EnsureStock(); return wares; } }
    public string Currency => CurrencyManager.HasInstance ? CurrencyManager.Instance.currencyName : "gold";

    public string Prompt => $"Trade with the {merchantName}";
    public bool CanInteract(Character who) => DungeonFeatureUtility.TablePlayer(who) != null;

    public void Interact(Character who)
    {
        var player = DungeonFeatureUtility.TablePlayer(who);
        if (player == null) return;
        EnsureStock();
        if (greetings != null && greetings.Length > 0) MessageLog.Post(greetings[Random.Range(0, greetings.Length)], MessageKind.Lore);
        if (ShopUI.HasInstance) ShopUI.Instance.Open(this, player);
        else Debug.LogWarning("[Merchant] No ShopUI in the scene.", this);
    }

    void EnsureStock()
    {
        if (stocked) return;
        stocked = true;
        var rng = new System.Random(seed);
        if (staples != null)
            foreach (var item in staples) Add(item, stapleCount);
        if (stockTable != null)
            for (int i = 0; i < stockRolls; i++)
                foreach (var drop in stockTable.RollDrops(rng, floorNumber, DungeonLootSource.Chest))
                    Add(drop.item, drop.quantity);
    }

    void Add(ItemData item, int count)
    {
        if (item == null || count <= 0) return;
        foreach (var w in wares) if (w.item == item) { w.count += count; return; }
        wares.Add(new Ware { item = item, count = count, price = PriceOf(item) });
    }

    public int PriceOf(ItemData item)
    {
        int basePrice = CurrencyManager.HasInstance ? CurrencyManager.Instance.BuyPrice(item) : Mathf.Max(1, item.value);
        return Mathf.Max(1, Mathf.CeilToInt(basePrice * priceMultiplier * (1f + pricePerFloor * Mathf.Max(0, floorNumber - 1))));
    }

    public int SellPriceOf(ItemData item) =>
        CurrencyManager.HasInstance ? CurrencyManager.Instance.SellPrice(item) : Mathf.Max(1, item.value / 3);

    public bool Buy(int index, Player player, out string message)
    {
        EnsureStock();
        message = null;
        if (index < 0 || index >= wares.Count || player == null) return false;
        var ware = wares[index];
        var wallet = player.Wallet;
        if (wallet == null || !wallet.CanAfford(ware.price)) { message = $"You cannot afford the {ware.item.itemName}."; return false; }
        if (!InventoryManager.HasInstance || !InventoryManager.Instance.Pickup(ware.item, player, 1, playSound: false))
        {
            message = "Your pack is full.";
            return false;
        }
        wallet.TrySpend(ware.price);
        if (--ware.count <= 0) wares.RemoveAt(index);
        PlayTrade();
        message = $"You buy the {ware.item.itemName} for {ware.price} {Currency}.";
        MessageLog.Post(message, MessageKind.Loot);
        return true;
    }

    public bool CanSell(Player player, ItemData item, out string reason)
    {
        reason = null;
        if (!buysItems) { reason = $"The {merchantName} isn't buying."; return false; }
        if (item == null) return false;
        int owned = (player.Bag != null ? player.Bag.TotalCount(item) : 0) + (player.Hotbar != null ? player.Hotbar.TotalCount(item) : 0);
        if (player.Equipment != null && player.Equipment.IsEquipped(item) && owned <= 1) { reason = "Unequip it first."; return false; }
        return true;
    }

    public bool Sell(Player player, Inventory container, int slot, out string message)
    {
        message = null;
        var item = container != null ? container.ItemAt(slot) : null;
        if (player == null || item == null) return false;
        if (!CanSell(player, item, out message)) return false;
        int price = SellPriceOf(item);
        container.Consume(slot);
        player.Wallet?.Add(price);
        Add(item, 1);
        PlayTrade();
        message = $"You sell the {item.itemName} for {price} {Currency}.";
        MessageLog.Post(message, MessageKind.Loot);
        return true;
    }

    void PlayTrade()
    {
        var clip = tradeClip != null ? tradeClip : CurrencyManager.HasInstance ? CurrencyManager.Instance.pickupClip : null;
        if (clip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(clip, tradeVolume, .05f);
    }

    [Button, HideInEditorMode]
    void Restock() { stocked = false; wares.Clear(); EnsureStock(); }
}

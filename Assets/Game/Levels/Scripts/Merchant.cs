using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A dungeon trader. Stock is rolled once, the first time the player talks to them: a couple of
// consumables (from the staples and the loot table) and a few pieces of gear from the loot table,
// each a different item. Prices come from ItemData.value (CurrencyManager fallback), scaled by
// depth and this merchant's markup. What the player sells is not restocked.
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
    [Tooltip("Consumables this merchant may offer, alongside any the loot table rolls.")]
    public ItemData[] staples = new ItemData[0];
    [Min(0), Tooltip("Different consumables on offer.")] public int consumableCount = 2;
    [Min(1), Tooltip("How many of each consumable.")] public int stapleCount = 2;
    [Min(0), Tooltip("Different pieces of gear (weapons, shields, armor, trinkets, tomes) on offer.")] public int gearCount = 5;
    [Tooltip("Rolled as Chest rewards for the stock. The generator sets this from the level when empty.")]
    public LootSource stockTable;
    [Range(.5f, 3f)] public float priceMultiplier = 1.2f;
    [Range(0f, .5f), Tooltip("Price increase per floor beyond the first.")]
    public float pricePerFloor = .1f;
    public bool buysItems = true;
    public const string ShopTradeKey = "shopTrade";
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
    public IReadOnlyList<Ware> Wares { get { EnsureStock(); foreach (var w in wares) w.price=PriceOf(w.item); return wares; } }
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
        var consumables = new List<ItemData>();
        var gear = new List<ItemData>();
        if (staples != null) foreach (var item in staples) if (item != null && !consumables.Contains(item)) consumables.Add(item);
        // Roll the table until there is enough distinct gear, with a cap so a thin table can't loop forever.
        for (int i = 0; stockTable != null && i < 60 && gear.Count < gearCount; i++)
            foreach (var drop in stockTable.RollDrops(rng, floorNumber, DungeonLootSource.Chest))
            {
                var list = drop.item == null ? null : drop.item.IsConsumable ? consumables : gear;
                if (list != null && !list.Contains(drop.item) && (list == consumables || gear.Count < gearCount)) list.Add(drop.item);
            }
        for (int i = 0; i < consumableCount && consumables.Count > 0; i++)
        {
            int pick = rng.Next(consumables.Count);
            Add(consumables[pick], stapleCount);
            consumables.RemoveAt(pick);
        }
        foreach (var item in gear) Add(item, 1);
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
        float haggle = Trader != null ? Trader.BuyPriceMultiplier : 1;
        // Never cheaper than the merchant pays, so buying and selling back can't make money.
        return Mathf.Max(SellPriceOf(item) + 1, Mathf.CeilToInt(basePrice * priceMultiplier * (1f + pricePerFloor * Mathf.Max(0, floorNumber - 1)) * haggle));
    }

    public int SellPriceOf(ItemData item)
    {
        int basePrice = CurrencyManager.HasInstance ? CurrencyManager.Instance.SellPrice(item) : Mathf.Max(1, item.value / 3);
        return Mathf.Max(1, Mathf.FloorToInt(basePrice * (Trader != null ? Trader.SellPriceMultiplier : 1)));
    }

    // Trading and Charisma of whoever is shopping (the active player).
    static AdventurerProgress Trader => PlayerManager.HasInstance && PlayerManager.Instance.Active != null ? PlayerManager.Instance.Active.GetComponent<AdventurerProgress>() : null;

    public bool Buy(int index, Player player, out string message)
    {
        EnsureStock();
        message = null;
        if (index < 0 || index >= wares.Count || player == null) return false;
        var ware = wares[index];
        ware.price = PriceOf(ware.item);
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
        player.GetComponent<AdventurerProgress>()?.Practise(AdventureSkill.Trading, SkillAction.Buy);
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
        PlayTrade();
        message = $"You sell the {item.itemName} for {price} {Currency}.";
        MessageLog.Post(message, MessageKind.Loot);
        player.GetComponent<AdventurerProgress>()?.Practise(AdventureSkill.Trading, SkillAction.Sell);
        return true;
    }

    void PlayTrade()
    {
        // The UI Library's "shopTrade" entry when there is one; otherwise this merchant's own clip or the coin sound.
        if (AudioManager.HasInstance && AudioManager.Instance.HasUI(ShopTradeKey)) { AudioManager.Instance.PlayUI(ShopTradeKey); return; }
        var clip = tradeClip != null ? tradeClip : CurrencyManager.HasInstance ? CurrencyManager.Instance.pickupClip : null;
        if (clip != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(clip, tradeVolume, .05f);
    }

    [Button, HideInEditorMode]
    void Restock() { stocked = false; wares.Clear(); EnsureStock(); }
}

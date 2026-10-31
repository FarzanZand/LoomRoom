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
        "\"Coin for goods. Have a look.\"",
        "\"I buy and I sell. Take your time.\"",
        "\"Fair prices, this far down.\"",
    };
    [Tooltip("Consumables this merchant may offer, alongside any the loot table rolls.")]
    public ItemData[] staples = new ItemData[0];
    [Min(0), Tooltip("Different consumables on offer.")] public int consumableCount = 2;
    [Min(1), Tooltip("How many of each consumable.")] public int stapleCount = 2;
    [Min(0), Tooltip("Different pieces of gear (weapons, shields, armor, trinkets, tomes) on offer. The shop screen shows six.")] public int gearCount = 6;
    [Tooltip("At least one spell tome among the gear.")] public bool alwaysTome = true;
    [Tooltip("At least one weapon from this floor's drop table among the gear, Uncommon or better.")] public bool alwaysWeapon = true;
    [Tooltip("Rolled as Chest rewards for the stock. The generator sets this from the level when empty.")]
    public LootSource stockTable;
    [Range(.5f, 3f)] public float priceMultiplier = 1.2f;
    [Range(0f, .5f), Tooltip("Price increase per floor beyond the first.")]
    public float pricePerFloor = .1f;
    public bool buysItems = true;
    public const string ShopTradeKey = "shopTrade";
    public AudioClip tradeClip;
    [Range(0, 1)] public float tradeVolume = .9f;
    [Tooltip("Music Library key (AudioManager) played while the table player is near. Empty: no shop music.")]
    public string musicKey = "shop";
    [Min(0), Tooltip("Metres from the merchant at which the shop music fades in.")]
    public float musicRadius = 8f;
    [Min(0), Tooltip("Metres at which the floor's own music comes back (a little further than Music Radius, so walking along the edge doesn't flip it back and forth).")]
    public float musicLeaveRadius = 11f;
    [Min(0)] public float musicFade = 1.5f;

    // The floor's music while the shop's plays: put back, from where it was, when the player walks off.
    bool shopMusicOn;
    AudioClip floorMusic;
    float floorMusicTime, floorMusicVolume;
    bool floorMusicLoops;

    void Update()
    {
        if (string.IsNullOrEmpty(musicKey) || !AudioManager.HasInstance || !PlayerManager.HasInstance) return;
        var player = PlayerManager.Instance.ActiveKind == PlayerKind.Table ? PlayerManager.Instance.Active : null;
        float distance = player != null && player.IsAlive ? Vector3.Distance(player.transform.position, transform.position) : float.MaxValue;
        if (!shopMusicOn && distance <= musicRadius) StartShopMusic();
        else if (shopMusicOn && distance > musicLeaveRadius) StopShopMusic();
    }

    // As with RoomMusic: the floor's track only comes back while the player is still on the table.
    void OnDisable()
    {
        if (!shopMusicOn) return;
        if (PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table) StopShopMusic(); else shopMusicOn = false;
    }

    void StartShopMusic()
    {
        var audio = AudioManager.Instance;
        floorMusic = audio.CurrentMusic; floorMusicTime = audio.CurrentMusicTime;
        floorMusicVolume = audio.CurrentMusicVolume; floorMusicLoops = audio.CurrentMusicLoops;
        shopMusicOn = audio.CrossfadeMusic(musicKey, true, musicFade);
    }

    void StopShopMusic()
    {
        shopMusicOn = false;
        if (!AudioManager.HasInstance) return;
        if (floorMusic != null) AudioManager.Instance.CrossfadeMusicAt(floorMusic, floorMusicTime, floorMusicLoops, musicFade, floorMusicVolume);
        else AudioManager.Instance.StopMusic(musicFade);
    }

    [HideInInspector] public int seed;
    [HideInInspector] public int floorNumber = 1;

    public class Ware
    {
        public ItemData item;
        public int count;
        public int price;
    }

    readonly List<Ware> wares = new();
    // What is on the table now, without stocking it (ItemData.PruneVariants keeps these).
    public void AddStockTo(HashSet<ItemData> items) { foreach (var w in wares) if (w.item != null) items.Add(w.item); }
    bool stocked;
    public IReadOnlyList<Ware> Wares { get { EnsureStock(); foreach (var w in wares) w.price=PriceOf(w.item); return wares; } }
    public string Currency => CurrencyManager.HasInstance ? CurrencyManager.Instance.currencyName : "gold";

    public string Prompt => $"Trade with the {merchantName.ToLowerInvariant()}";
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
        // Every merchant has a spell tome: when none came up, one takes the last place (a random tome of a tier this
        // floor's gear reaches).
        if (alwaysTome && !gear.Exists(i => i != null && i.spell != null) && InventoryManager.HasInstance)
        {
            int top = LootSource.FloorGear?.Invoke(floorNumber)?.HighestTier ?? 1;
            var tomes = InventoryManager.Instance.itemCatalog.FindAll(i => i != null && i.spell != null && i.tier > 0 && i.tier <= Mathf.Max(1, top) && InventoryManager.Instance.CanDropLoot(i));
            if (tomes.Count > 0)
            {
                if (gear.Count >= gearCount && gear.Count > 0) gear.RemoveAt(gear.Count - 1);
                gear.Add(tomes[rng.Next(tomes.Count)]);
            }
        }
        // And a weapon of the floor's: when none came up, the table is rolled again for one, which takes the place of the
        // last piece that isn't the tome. It is Uncommon or better (below).
        ItemData weapon = gear.Find(i => i != null && i.itemType == ItemType.Weapon);
        if (alwaysWeapon && weapon == null && stockTable != null)
        {
            for (int i = 0; i < 40 && weapon == null; i++)
                foreach (var drop in stockTable.RollDrops(rng, floorNumber, DungeonLootSource.Chest))
                    if (drop.item != null && drop.item.itemType == ItemType.Weapon && !gear.Contains(drop.item)) { weapon = drop.item; break; }
            if (weapon != null)
            {
                int replace = gear.FindLastIndex(i => i != null && i.spell == null);
                if (gear.Count >= gearCount && replace >= 0) gear.RemoveAt(replace);
                gear.Add(weapon);
            }
        }
        // The merchant's gear rolls its own rarity, from the plain item, at InventoryManager's Merchant Rarity times the floor's chances.
        float boost = InventoryManager.HasInstance ? InventoryManager.Instance.merchantRarity : 1.3f;
        // Keen eye (Trading 25): Uncommon, Rare and Unique gear twice as often again.
        var buyer = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var trader = buyer != null ? buyer.GetComponent<AdventurerProgress>() : null;
        if (trader != null && trader.FirstMilestone(AdventureSkill.Trading)) boost *= 2f;
        foreach (var item in gear)
        {
            var offered = item.IsGear ? DungeonLootProfile.RollRarity(item.BaseItem, rng, floorNumber, boost) : item;
            // The floor's weapon is never Common.
            if (alwaysWeapon && item == weapon && offered.rarity == ItemRarity.Common && ItemPropertyTable.Current != null)
                offered = item.BaseItem.Rolled(ItemRarity.Uncommon, ItemPropertyTable.Current.Roll(item.BaseItem, ItemRarity.Uncommon, Mathf.Max(1, floorNumber), rng));
            Add(offered, 1);
        }
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
            message = "Your bag is full.";
            return false;
        }
        wallet.TrySpend(ware.price);
        if (--ware.count <= 0) wares.RemoveAt(index);
        CoopWorld.WareBought(this, ware.item);
        PlayTrade();
        message = $"You buy the {ware.item.itemName} for {ware.price} {Currency}.";
        MessageLog.Post(message, MessageKind.Loot);
        Practise(player, SkillAction.Buy);
        return true;
    }

    // Co-op with shared loot: a teammate bought one of this ware.
    public void Sold(string saveId)
    {
        EnsureStock();
        int i = wares.FindIndex(w => w.item != null && w.item.saveId == saveId);
        if (i < 0) return;
        if (--wares[i].count <= 0) wares.RemoveAt(i);
        if (ShopUI.HasInstance) ShopUI.Instance.RefreshWares();
    }

    public bool CanSell(Player player, ItemData item, out string reason)
    {
        reason = null;
        if (!buysItems) { reason = $"The {merchantName} isn't buying."; return false; }
        if (item == null) return false;
        if (item.itemType == ItemType.Spoils) { reason = "Spoils go to the tally, not the till."; return false; }
        int owned = (player.Bag != null ? player.Bag.TotalCount(item) : 0) + (player.Hotbar != null ? player.Hotbar.TotalCount(item) : 0);
        // A held item stays in the hotbar, so its last copy there is the one in hand; worn items leave the bag, so
        // any copy still in it is a spare.
        if (player.Equipment != null && Equipment.IsHand(item.equipSlot) && player.Equipment.IsEquipped(item) && owned <= 1) { reason = "Unequip it first."; return false; }
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
        if (player.Wallet != null) player.Wallet.Add(price);
        PlayTrade();
        message = $"You sell the {item.itemName} for {price} {Currency}.";
        MessageLog.Post(message, MessageKind.Loot);
        Practise(player, SkillAction.Sell);
        return true;
    }

    static void Practise(Player player, SkillAction action)
    {
        var progress = player.GetComponent<AdventurerProgress>();
        if (progress != null) progress.Practise(AdventureSkill.Trading, action);
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

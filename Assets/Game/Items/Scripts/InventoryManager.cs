using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Item-system configuration and the shared operations that need it: picking up,
// dropping, using. The containers themselves live on each player (Inventory,
// Equipment); this only decides the rules.
public class InventoryManager : Singleton<InventoryManager>
{
    public event System.Action<ItemData, Player, int> ItemPickedUp;
    [Header("Pickup")]
    [Tooltip("Honour the item's Equip On Pickup flag.")]
    public bool allowEquipOnPickup = true;
    [Tooltip("Use the shared AudioManager SFX library entry for every pickup, including items with their own audio.")]
    public bool useSharedPickupSound = true;
    public string pickupSoundKey = "genericPickupSound";
    [Tooltip("Played when an item is picked up and the item has no pickup audio of its own.")]
    public AudioData defaultPickupAudio;
    [Tooltip("Pickup visual used when an item has neither a pickup visual nor a world mesh assigned.")]
    public GameObject defaultPickupVisual;
    [Tooltip("A few twinkles on every item lying on the floor, so pickups stand out. Fitted to the item's size.")]
    public GameObject pickupSparkle;
    [Header("Loot beams")]
    [Tooltip("A pillar of light over loot lying on the floor, coloured by what it is (LootBeam).")]
    public bool lootBeams = true;
    public Material beamMaterial;
    [Min(.1f)] public float beamHeight = 1.1f;
    [Min(.01f)] public float beamWidth = .07f;
    public Color beamNormal = new(.95f, .9f, .78f, .24f);

    [Header("Chests")]
    [Tooltip("The chime a chest plays when it opens, one per chest tier (DungeonContainer.stingAudio).")]
    public bool chestOpenChime;

    [Header("Drops by source")]
    [Title("Enemy kill"), InlineProperty, HideLabel, Tooltip("What an enemy drops when killed, in every biome (a loot profile with Own Drop Chances on, like a boss's reward, uses its own).")]
    public SourceDrops enemyDrops = new() { goldChance = .3f, goldMin = 1, goldMax = 4, gearChance = .3f, recoveryChance = .16f };
    [Title("Breakable"), InlineProperty, HideLabel, Tooltip("What a barrel, crate, urn or other breakable drops, in every biome.")]
    public SourceDrops breakableDrops = new() { goldChance = .25f, goldMin = 1, goldMax = 2, gearChance = .05f, recoveryChance = .12f };
    [Title("Chest"), InlineProperty, HideLabel, Tooltip("What a chest, bookshelf, grave or loot on the floor gives, in every biome (iron and gold chests roll it again, DungeonContainer.extraRolls).")]
    public SourceDrops chestDrops = new() { goldChance = .6f, goldMin = 4, goldMax = 10, gearChance = 1f, recoveryChance = .7f };

    // The drop chances of a kind of source (Floor counts as a chest).
    public SourceDrops DropsFor(DungeonLootSource source) => source == DungeonLootSource.Enemy ? enemyDrops : source == DungeonLootSource.Barrel ? breakableDrops : chestDrops;

    [Header("Rarity")]
    [Tooltip("The properties rolled gear can have, their slots and their values by floor (Items/Data/Item Properties).")]
    public ItemPropertyTable itemProperties;
    [Tooltip("Gear rarities above Plain, worst first: properties, bonus on the main number, price, and chance by floor. Plain is whatever is left. Every loot source and merchant rolls from here.")]
    [ListDrawerSettings(ShowFoldout = false, ListElementLabelName = "rarity")]
    public List<RarityStep> rarities = new()
    {
        new RarityStep { rarity = ItemRarity.Fine, properties = new(1, 1), baseBonus = .2f, priceMultiplier = 1.6f, chance = .25f, chancePerFloor = 0f },
        new RarityStep { rarity = ItemRarity.Superior, properties = new(2, 2), baseBonus = .4f, priceMultiplier = 2.5f, chance = .15f, chancePerFloor = 0f },
        new RarityStep { rarity = ItemRarity.Masterwork, properties = new(3, 3), baseBonus = .6f, priceMultiplier = 4f, chance = .08f, chancePerFloor = 0f },
    };
    [Min(0), Tooltip("Times every rarity chance for a merchant's gear (a better pick than the floor's loot; its price follows the rarity).")] public float merchantRarity = 1.3f;

    [ShowInInspector, ReadOnly, MultiLineProperty(4), LabelText("Chests by floor"), Tooltip("Rarity of chest gear on each floor, with the whole-game multiplier.")]
    string RarityPreview
    {
        get
        {
            var lines = new List<string>();
            float scale = rarityChanceMultiplier * (chestDrops != null ? chestDrops.rarityMultiplier : 1f);
            for (int floor = 1; floor <= 4; floor++)
            {
                float rest = 1f, total = 0f; string parts = "";
                foreach (var s in rarities) if (s != null) total += s.ChanceOn(floor) * scale;
                float fit = total > 1f ? 1f / total : 1f;
                for (int i = rarities.Count - 1; i >= 0; i--)
                {
                    var step = rarities[i];
                    if (step == null) continue;
                    float p = Mathf.Min(rest, step.ChanceOn(floor) * scale * fit); rest -= p;
                    parts = $"  {step.rarity} {p * 100f:0.#}%" + parts;
                }
                lines.Add($"Floor {floor}: Plain {rest * 100f:0.#}%" + parts);
            }
            return string.Join("\n", lines);
        }
    }

    // A rarity's step, or null for Plain and the rarities that are not rolled (tomes, Relics, spoils).
    public static RarityStep StepFor(ItemRarity rarity)
    {
        if (!HasInstance || Instance.rarities == null) return null;
        foreach (var step in Instance.rarities) if (step != null && step.rarity == rarity) return step;
        return null;
    }

    // The next rarity up from this one (the shrine's raise), or Plain when there is none.
    public static ItemRarity NextRarity(ItemRarity rarity)
    {
        if (!HasInstance || Instance.rarities == null) return ItemRarity.Plain;
        var list = Instance.rarities;
        if (rarity == ItemRarity.Plain) return list.Count > 0 && list[0] != null ? list[0].rarity : ItemRarity.Plain;
        for (int i = 0; i < list.Count - 1; i++) if (list[i] != null && list[i].rarity == rarity && list[i + 1] != null) return list[i + 1].rarity;
        return ItemRarity.Plain;
    }

    // The rarity multiplier of a loot source (Drops by source).
    public static float RaritySourceScale(DungeonLootSource source) => HasInstance && Instance.DropsFor(source) != null ? Instance.DropsFor(source).rarityMultiplier : 1f;

    // One rarity for a piece of gear on a floor, the best step first; each chance is times scale (the whole-game
    // multiplier is the caller's to include).
    public ItemRarity RollRarity(System.Random random, int floor, float scale)
    {
        double roll = random.NextDouble();
        // Boosted chances that add up past 100% share it in their own proportions (no rarity is crowded out).
        float total = 0f;
        foreach (var step in rarities) if (step != null) total += step.ChanceOn(floor) * scale;
        float fit = total > 1f ? 1f / total : 1f;
        float sum = 0f;
        for (int i = rarities.Count - 1; i >= 0; i--)
        {
            var step = rarities[i];
            if (step == null) continue;
            sum += step.ChanceOn(floor) * scale * fit;
            if (roll < sum) return step.rarity;
        }
        return ItemRarity.Plain;
    }

    [Header("Loot balance (whole game)")]
    [Min(0), Tooltip("Times every chance of gear coming out above Plain, in every loot source and merchant (the shrine's raise is not affected). 1 = as set under Rarity.")]
    public float rarityChanceMultiplier = 1f;
    [Min(0), Tooltip("Times every chance of an item dropping from enemies and breakables: gear and recovery chances of loot profiles, an enemy's drop chance and pool chances of hand-listed tables. Chests keep their own. 1 = as set on each table.")]
    public float dropChanceMultiplier = 1f;
    [Min(0), Tooltip("Times the chance of an enemy dropping an item (gear and recovery from every biome's loot profile and hand-listed tables), on top of Drop Chance Multiplier. 0 = enemies drop no items (gold and spoils still drop). 1 = as set on each table.")]
    public float enemyDropMultiplier = 1f;
    [Min(0), Tooltip("Times every amount of gold any loot gives. 1 = as set on each table.")]
    public float goldMultiplier = 1f;

    // The whole-game multipliers, safe to read with no manager (1 then).
    public static float RarityChanceScale => HasInstance ? Instance.rarityChanceMultiplier : 1f;
    public static float DropChanceScale => HasInstance ? Instance.dropChanceMultiplier : 1f;
    // The drop multiplier for one loot source: the whole-game one, times the enemy one for kills.
    // Drop Chance Multiplier reaches what enemies and breakables drop; chests (the boss's reward too) keep their own chances.
    public static float DropChanceFor(DungeonLootSource source) =>
        (source == DungeonLootSource.Enemy || source == DungeonLootSource.Barrel ? DropChanceScale : 1f)
        * (source == DungeonLootSource.Enemy && HasInstance ? Instance.enemyDropMultiplier : 1f);
    public static float GoldScale => HasInstance ? Instance.goldMultiplier : 1f;

    [Header("Drop")]
    [Tooltip("Prefab with a WorldItem used when items are dropped or spawned from data.")]
    [Required] public GameObject pickupPrefab;
    public float dropDistance = 1.5f;
    public float dropHeight   = 0.5f;

    [Header("Loot duplicates")]
    [Tooltip("Exclude tomes already in the dungeon player's bag, hotbar or equipment from loot rolls.")]
    public bool preventDuplicateTomes = true;
    [Tooltip("Exclude exact weapon, shield and armor items already owned from loot rolls. Different items of the same type remain eligible.")]
    public bool preventDuplicateEquipment;

    public bool CanDropLoot(ItemData item)
    {
        if (item == null) return false;
        bool filtered = item.spell != null ? preventDuplicateTomes
            : preventDuplicateEquipment && (item.itemType == ItemType.Weapon || item.itemType == ItemType.Shield || item.itemType == ItemType.Equipment);
        if (!filtered) return true;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player == null) return true;
        // Any variant (Fine, Masterwork) of the item counts as owning it.
        bool Owns(Inventory inventory) { if (inventory == null) return false; for (int i = 0; i < inventory.SlotCount; i++) { var stack = inventory[i]; if (stack != null && !stack.IsEmpty && stack.item.BaseItem == item.BaseItem) return true; } return false; }
        if (Owns(player.Bag) || Owns(player.Hotbar)) return false;
        if (player.Equipment != null)
            foreach (EquipmentSlot slot in System.Enum.GetValues(typeof(EquipmentSlot)))
                if (player.Equipment.Get(slot) != null && player.Equipment.Get(slot).BaseItem == item.BaseItem) return false;
        return true;
    }

    [Header("Catalog")]
    [Tooltip("Every item the runtime may need to look up by name (dialogue GiveItem, saves).")]
    [ListDrawerSettings(ShowFoldout = true)]
    public List<ItemData> itemCatalog = new();

    public Player    ActivePlayer    => PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
    public Inventory ActiveBag       => ActivePlayer != null ? ActivePlayer.Bag : null;
    public Inventory ActiveHotbar    => ActivePlayer != null ? ActivePlayer.Hotbar : null;
    public Equipment ActiveEquipment => ActivePlayer != null ? ActivePlayer.Equipment : null;

    // ── Pickup ────────────────────────────────────────────────────────

    public bool Pickup(ItemData item, Player player, int count = 1, bool preferHotbar = false, bool playSound = true, bool announcePickup = true)
    {
        if (item == null || player == null) return false;

        bool added = false;
        // Consumables and spell tomes go to the hotbar while it has room, so they are one key away.
        bool hotbarFirst = item.directToHotbar || preferHotbar || item.IsConsumable || item.spell != null;
        if (hotbarFirst && player.Hotbar != null && player.Hotbar.Accepts(item))
            added = player.Hotbar.TryAdd(item, count);
        if (!added && player.Bag != null)
            added = player.Bag.TryAdd(item, count);

        if (!added)
        {
            NotificationUI.Show("Inventory full");
            return false;
        }

        if (playSound) item.PlayPickupSound(useSharedPickupSound ? pickupSoundKey : null, defaultPickupAudio);

        ItemEffectProcessor.Fire(item, EffectTrigger.OnPickup, EffectContext.For(player, item));

        if (allowEquipOnPickup && item.equipOnPickup && player.Equipment != null &&
            player.Equipment.CanEquip(item) && !player.Equipment.Has(player.Equipment.SlotFor(item)))   // a trinket takes whichever trinket slot is free
            player.Equipment.Equip(item, playSound);

        if (announcePickup) ItemPickedUp?.Invoke(item, player, count);
        return true;
    }

    // ── Use ───────────────────────────────────────────────────────────

    // Use a consumable sitting in a container slot: eaten or drunk in the right hand (or used at once, by its
    // ItemData.AnimationOnUse), then the hand item held before comes back.
    public void Use(Inventory container, int index)
    {
        var item = container?.ItemAt(index);
        if (item == null || !item.IsConsumable) return;
        var player = container.GetComponentInParent<Player>();
        if (player == null || player.Equipment == null) { item.Use(player); container.Consume(index); return; }
        player.Equipment.Consume(item, () => TakeAndUse(player, item, container, index));
    }

    // When it takes effect: one is taken from where it was (or, moved meanwhile, from the hotbar or bag) and used.
    static void TakeAndUse(Player player, ItemData item, Inventory container, int index)
    {
        bool taken = container != null && container.ItemAt(index) == item;
        if (taken) container.Consume(index);
        if (!taken) taken = player.Hotbar != null && player.Hotbar.RemoveOne(item);
        if (!taken) taken = player.Bag != null && player.Bag.RemoveOne(item);
        if (taken) item.Use(player);
    }

    // A hotbar key: gear is taken in hand or put away again, a consumable that can't be held is used.
    public void Activate(Player player, Inventory container, int index)
    {
        var item = container?.ItemAt(index);
        if (item == null || player == null) return;
        var eq = player.Equipment;
        // While eating or drinking nothing else is taken in hand or used.
        if (eq != null && eq.IsUsingItem) return;
        if (item.IsConsumable) { Use(container, index); return; }
        if (eq == null || !eq.CanEquip(item)) return;
        if (eq.IsEquipped(item)) eq.Unequip(item);
        else eq.Equip(item);
    }

    // Use the consumable the player is currently holding in a hand.
    public bool UseHeld(Player player, EquipmentSlot slot = EquipmentSlot.RightHand)
    {
        var item = player?.Equipment?.Get(slot);
        if (item == null || !item.IsConsumable) return false;

        if (player.Equipment.IsUsingItem) return true;
        player.Equipment.Consume(item, () => CompleteHeldUse(player, slot, item));
        return true;
    }

    void CompleteHeldUse(Player player, EquipmentSlot slot, ItemData item)
    {
        if (player == null || player.Equipment.Get(slot) != item) return;
        // Recheck ownership when an animation completes: the stack may have been moved or dropped.
        bool consumed=player.Hotbar!=null && player.Hotbar.RemoveOne(item);
        if(!consumed)consumed=player.Bag!=null && player.Bag.RemoveOne(item);
        player.Equipment.Unequip(slot);
        if(consumed)item.Use(player);
    }

    // ── Spawn / drop ──────────────────────────────────────────────────

    public WorldItem Spawn(ItemData item, Vector3 position, Quaternion rotation)
    {
        if (item == null || pickupPrefab == null) return null;
        var go = Instantiate(pickupPrefab, position, rotation);
        var wi = go.GetComponent<WorldItem>();
        if (wi == null) { Debug.LogError("[InventoryManager] Pickup prefab has no WorldItem.", pickupPrefab); Destroy(go); return null; }
        wi.Init(item);
        return wi;
    }

    public WorldItem Spawn(ItemData item, Vector3 position) => Spawn(item, position, Quaternion.identity);

    public WorldItem DropFromPlayer(ItemData item, Player player)
    {
        if (item == null) return null;
        if (player == null) player = ActivePlayer;
        if (player == null) return null;
        Transform t = player.transform;
        Vector3 forward = player.Look != null ? player.Look.YawTransform.forward : t.forward;
        var pos = t.position + forward * dropDistance + Vector3.up * dropHeight;
        var pickup = Spawn(item, pos);
        var level = TableManager.Loader;
        if (pickup != null && player.kind == PlayerKind.Table && level != null && level.Dungeon != null)
            pickup.transform.SetParent(level.Dungeon.transform, true);
        return pickup;
    }

    // ── Lookup ────────────────────────────────────────────────────────

    public ItemData FindItem(string itemName)
    {
        if (string.IsNullOrEmpty(itemName)) return null;
        foreach (var i in itemCatalog)
            if (i != null && (i.itemName == itemName || i.name == itemName)) return i;
        return null;
    }

#if UNITY_EDITOR
    // The editor assembly owns the sync (ItemDatabaseAuthoring.SyncCatalog); runtime code cannot reference it.
    [Button("Populate Catalog From Project")]
    void PopulateCatalog() => UnityEditor.EditorApplication.ExecuteMenuItem("Tools/LoomRoom/Sync Item Catalog");
#endif
}

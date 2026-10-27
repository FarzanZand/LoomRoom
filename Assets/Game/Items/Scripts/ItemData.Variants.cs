using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Variants of an item asset: a rarity with its rolled properties (Uncommon, Rare, Unique, Legendary), on one runtime copy of the asset.
// Each distinct variant is one shared object, so the bag, hotbar and equipment treat it like any other item. It is saved
// as its id: "iron_sword" or "iron_sword~3:crit=7,speed=11".
public partial class ItemData
{
    // The item's main number: a weapon's damage, a shield's or worn armour's armour (trinkets have none).
    public StatType? MainStatType => IsWeapon ? StatType.AttackDamage : (itemType == ItemType.Shield || itemType == ItemType.Equipment) && canBeEquipped && equipSlot != EquipmentSlot.Trinket1 && equipSlot != EquipmentSlot.Trinket2 ? StatType.Armor : null;
    // The rolled properties of a rolled copy (empty on assets).
    [System.NonSerialized] public RolledProperty[] rolls;
    // How many of statModifiers come from the asset; the rest are the rolled properties.
    [System.NonSerialized] int baseModifiers = -1;
    public int BaseModifierCount => baseModifiers >= 0 ? baseModifiers : statModifiers != null ? statModifiers.Length : 0;
    public bool IsRolledModifier(int index) => index >= BaseModifierCount;

    static readonly Dictionary<string, ItemData> variants = new();

    // Enter Play Mode without a domain reload keeps statics: drop the last session's copies.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetVariants()
    {
        // They are runtime copies (DontSave): the last session's would stay in memory with no key to find them by.
        foreach (var copy in variants.Values) if (copy != null) Destroy(copy);
        variants.Clear();
    }

    // At each run start the rolled copies nobody holds any more (the last run's drops, shop stock and bags) are destroyed,
    // so the cache doesn't grow with every drop for the whole session. Held means in a bag, hotbar or hand, lying on the
    // floor, carried by an enemy, on a merchant's table or in the bag kept for the way back to the town.
    public static int PruneVariants()
    {
        if (variants.Count == 0) return 0;
        var held = new HashSet<ItemData>();
        foreach (var inventory in FindObjectsByType<Inventory>(FindObjectsInactive.Include))
            for (int i = 0; i < inventory.SlotCount; i++) if (inventory.ItemAt(i) is ItemData item) held.Add(item);
        foreach (var equipment in FindObjectsByType<Equipment>(FindObjectsInactive.Include))
            foreach (var item in equipment.EquippedItems) if (item != null) held.Add(item);
        foreach (var lying in FindObjectsByType<WorldItem>(FindObjectsInactive.Include)) if (lying.Item != null) held.Add(lying.Item);
        foreach (var drop in FindObjectsByType<DungeonLootDrop>(FindObjectsInactive.Include)) if (drop.carriedItem != null) held.Add(drop.carriedItem);
        foreach (var merchant in FindObjectsByType<Merchant>(FindObjectsInactive.Include)) merchant.AddStockTo(held);
        // A chest's promised prize (the Skeleton Party's Legendary, a boss chest), rolled before it is opened.
        foreach (var chest in FindObjectsByType<DungeonContainer>(FindObjectsInactive.Include))
        {
            if (chest.guaranteedItem != null) held.Add(chest.guaranteedItem);
            if (chest.guaranteedItems != null) foreach (var item in chest.guaranteedItems) if (item != null) held.Add(item);
        }
        foreach (var loader in FindObjectsByType<TableLevelLoader>(FindObjectsInactive.Include)) loader.AddTownItemsTo(held);
        var gone = new List<string>();
        foreach (var pair in variants) if (pair.Value == null || !held.Contains(pair.Value)) gone.Add(pair.Key);
        int destroyed = 0;
        foreach (var key in gone) { var copy = variants[key]; variants.Remove(key); if (copy != null) { Destroy(copy); destroyed++; } }
        return destroyed;
    }

    // The same item rolled to a rarity with these properties.
    public ItemData Rolled(ItemRarity rarityTo, RolledProperty[] properties) => Variant(BaseItem, rarityTo, properties);

    public static ItemData Variant(ItemData asset, ItemRarity rarity, RolledProperty[] properties)
    {
        if (asset == null) return null;
        var root = asset.BaseItem;
        bool rolled = rarity != root.rarity || (properties != null && properties.Length > 0);
        if (!rolled) return root;
        string key = root.saveId + RollKey(rarity, properties);
        if (variants.TryGetValue(key, out var copy) && copy != null) return copy;

        copy = Instantiate(root);
        copy.hideFlags = HideFlags.DontSave;
        copy.name = root.name + key.Substring(root.saveId?.Length ?? 0);
        copy.baseItem = root;
        copy.rarity = rarity;
        copy.rolls = properties ?? System.Array.Empty<RolledProperty>();
        copy.saveId = key;

        // The rarity's bonus on the main number (a weapon's damage, a shield's or armour's armour), RaisedMain. Then rolled
        // properties join the item's stat modifiers after its own.
        var table = ItemPropertyTable.Current;
        var step = InventoryManager.StepFor(rarity);
        var modifiers = new List<StatModifierEntry>(copy.statModifiers ?? System.Array.Empty<StatModifierEntry>());
        copy.baseModifiers = modifiers.Count;
        var main = copy.MainStatType;
        if (step != null && main.HasValue && step.baseBonus > 0f)
            for (int i = 0; i < modifiers.Count; i++)
            {
                var m = modifiers[i];
                if (m == null || m.stat != main.Value || m.type != ModifierType.Flat) continue;
                modifiers[i] = new StatModifierEntry { stat = m.stat, type = m.type, value = RaisedMain(m.value, rarity) };
            }
        foreach (var roll in copy.rolls)
        {
            var def = table != null ? table.Find(roll.id) : null;
            if (def == null) continue;
            modifiers.Add(new StatModifierEntry { stat = def.stat, type = ModifierType.Flat, value = roll.value * def.statPerPoint });
        }
        copy.statModifiers = modifiers.ToArray();
        if (step != null) copy.value = Mathf.RoundToInt(copy.value * step.priceMultiplier);

        if (copy.rolls.Length > 0 && table == null) Debug.LogError($"[Items] No ItemPropertyTable (InventoryManager.itemProperties): {key} has no rolled stats.");
        // Without the InventoryManager a rolled rarity has no bonus or price yet: don't keep that copy.
        else if (InventoryManager.HasInstance) variants[key] = copy;
        return copy;
    }

    // A main number raised to a rarity: plus the step's bonus share (InventoryManager > Rarity), rounded to the nearest,
    // whole numbers to whole, others to a tenth. A whole number is also at least 1 above the step below it, so every
    // rarity beats the last (a 4-damage sword: Uncommon 5, Rare 6, Unique 7, Legendary 8).
    static float RaisedMain(float value, ItemRarity rarity)
    {
        bool whole = Mathf.Abs(value - Mathf.Round(value)) < .001f;
        float Raise(RarityStep s) => whole ? Mathf.Floor(value * (1f + s.baseBonus) + .5f) : Mathf.Floor(value * (1f + s.baseBonus) * 10f + .5f) / 10f;
        var steps = InventoryManager.HasInstance ? InventoryManager.Instance.rarities : null;
        float below = value;
        if (steps != null)
            foreach (var s in steps)   // worst first
            {
                if (s == null || s.baseBonus <= 0f) continue;
                float raised = Raise(s);
                if (whole && value > 0f) raised = Mathf.Max(raised, below + 1f);
                if (s.rarity == rarity) return raised;
                below = raised;
            }
        var own = InventoryManager.StepFor(rarity);
        return own != null ? Raise(own) : value;
    }

    static string RollKey(ItemRarity rarity, RolledProperty[] properties)
    {
        var parts = new List<string>();
        if (properties != null) foreach (var p in properties) parts.Add(p.id + "=" + p.value.ToString("0.##", CultureInfo.InvariantCulture));
        return "~" + (int)rarity + ":" + string.Join(",", parts);
    }

    // A saved id back to its item: "iron_sword" or a rolled "iron_sword~3:crit=7,speed=11". A "+1" or "+2" on the end
    // (Honed gear, retired) is dropped: old saves load the item without it.
    public static ItemData FromSaveId(string id, IEnumerable<ItemData> catalog)
    {
        if (string.IsNullOrEmpty(id) || catalog == null) return null;
        int plus = id.LastIndexOf('+');
        if (plus > 0 && int.TryParse(id.Substring(plus + 1), out _)) id = id.Substring(0, plus);
        string roll = null;
        int tilde = id.IndexOf('~');
        if (tilde > 0) { roll = id.Substring(tilde + 1); id = id.Substring(0, tilde); }
        ItemData root = null;
        foreach (var item in catalog) if (item != null && item.saveId == id) { root = item; break; }
        if (root == null) return null;
        if (roll == null) return root;

        int colon = roll.IndexOf(':');
        // A retired number from older saves: 1 (the tomes' old Rare) is Rare.
        int r = -1;
        if (colon > 0 && int.TryParse(roll.Substring(0, colon), out r)) r = r == 1 ? (int)ItemRarity.Rare : r;
        var rarity = System.Enum.IsDefined(typeof(ItemRarity), r) ? (ItemRarity)r : root.rarity;
        var properties = new List<RolledProperty>();
        if (colon >= 0 && colon < roll.Length - 1)
            foreach (var part in roll.Substring(colon + 1).Split(','))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (float.TryParse(part.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    properties.Add(new RolledProperty(part.Substring(0, eq), v));
            }
        return Variant(root, rarity, properties.ToArray());
    }
}

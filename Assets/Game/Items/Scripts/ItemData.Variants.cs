using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// Variants of an item asset: a rarity with its rolled properties (Fine, Masterwork) and Honed +1/+2, on one runtime copy
// of the asset. Each distinct variant is one shared object, so the bag, hotbar and equipment treat it like any other
// item. It is saved as its id: "iron_sword", "iron_sword+1", "iron_sword~3:crit=7,speed=11" or "iron_sword~2:kill=2+1".
public partial class ItemData
{
    // The rolled properties of a Fine or Masterwork copy (empty on assets).
    [System.NonSerialized] public RolledProperty[] rolls;
    // How many of statModifiers come from the asset; the rest are the rolled properties.
    [System.NonSerialized] int baseModifiers = -1;
    public int BaseModifierCount => baseModifiers >= 0 ? baseModifiers : statModifiers != null ? statModifiers.Length : 0;
    public bool IsRolledModifier(int index) => index >= BaseModifierCount;

    static readonly Dictionary<string, ItemData> variants = new();

    // Enter Play Mode without a domain reload keeps statics: drop the last session's copies.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetVariants() => variants.Clear();

    // The same item honed to a level (0 = not honed), keeping its rarity and rolls.
    public ItemData Honed(int level) => Variant(BaseItem, rarity, rolls, level);

    // The same item rolled to a rarity with these properties, keeping its honed level.
    public ItemData Rolled(ItemRarity rarityTo, RolledProperty[] properties) => Variant(BaseItem, rarityTo, properties, honed);

    public static ItemData Variant(ItemData asset, ItemRarity rarity, RolledProperty[] properties, int honedLevel)
    {
        if (asset == null) return null;
        var root = asset.BaseItem;
        honedLevel = root.CanBeHoned ? Mathf.Clamp(honedLevel, 0, MaxHoned) : 0;
        bool rolled = rarity != root.rarity || (properties != null && properties.Length > 0);
        if (!rolled && honedLevel == 0) return root;
        string key = root.saveId + (rolled ? RollKey(rarity, properties) : "") + (honedLevel > 0 ? "+" + honedLevel : "");
        if (variants.TryGetValue(key, out var copy) && copy != null) return copy;

        copy = Instantiate(root);
        copy.hideFlags = HideFlags.DontSave;
        copy.name = root.name + key.Substring(root.saveId?.Length ?? 0);
        copy.baseItem = root;
        copy.honed = honedLevel;
        copy.rarity = rarity;
        copy.rolls = properties ?? System.Array.Empty<RolledProperty>();
        copy.saveId = key;

        // Honed: +1/+2 on the main stat (trinkets: their bonuses x1.5/x2), as before.
        if (honedLevel > 0)
        {
            copy.itemName = $"Honed {root.itemName} +{honedLevel}";
            copy.value = Mathf.RoundToInt(root.value * (1 + .5f * honedLevel));
            bool trinket = root.equipSlot == EquipmentSlot.Trinket1 || root.equipSlot == EquipmentSlot.Trinket2;
            if (copy.statModifiers != null)
                foreach (var m in copy.statModifiers)
                {
                    if (m == null || m.type != ModifierType.Flat || m.value <= 0) continue;
                    if (trinket) m.value = Mathf.Round(m.value * (1 + .5f * honedLevel));
                    else if (m.stat == (root.IsWeapon ? StatType.AttackDamage : StatType.Armor)) m.value += honedLevel;
                }
        }

        // Rolled properties join the item's stat modifiers after its own.
        var table = ItemPropertyTable.Current;
        var modifiers = new List<StatModifierEntry>(copy.statModifiers ?? System.Array.Empty<StatModifierEntry>());
        copy.baseModifiers = modifiers.Count;
        foreach (var roll in copy.rolls)
        {
            var def = table != null ? table.Find(roll.id) : null;
            if (def == null) continue;
            modifiers.Add(new StatModifierEntry { stat = def.stat, type = ModifierType.Flat, value = roll.value * def.statPerPoint });
        }
        copy.statModifiers = modifiers.ToArray();
        if (rarity == ItemRarity.Fine) copy.value = Mathf.RoundToInt(copy.value * (table != null ? table.fineValue : 1.6f));
        else if (rarity == ItemRarity.Masterwork) copy.value = Mathf.RoundToInt(copy.value * (table != null ? table.masterworkValue : 4f));

        if (copy.rolls.Length > 0 && table == null) Debug.LogError($"[Items] No ItemPropertyTable (InventoryManager.itemProperties): {key} has no rolled stats.");
        else variants[key] = copy;
        return copy;
    }

    static string RollKey(ItemRarity rarity, RolledProperty[] properties)
    {
        var parts = new List<string>();
        if (properties != null) foreach (var p in properties) parts.Add(p.id + "=" + p.value.ToString("0.##", CultureInfo.InvariantCulture));
        return "~" + (int)rarity + ":" + string.Join(",", parts);
    }

    // A saved id back to its item: "iron_sword", a honed "iron_sword+1", a rolled "iron_sword~3:crit=7,speed=11" (honed: "...+1").
    public static ItemData FromSaveId(string id, IEnumerable<ItemData> catalog)
    {
        if (string.IsNullOrEmpty(id) || catalog == null) return null;
        int level = 0, plus = id.LastIndexOf('+');
        if (plus > 0 && int.TryParse(id.Substring(plus + 1), out level)) id = id.Substring(0, plus);
        string roll = null;
        int tilde = id.IndexOf('~');
        if (tilde > 0) { roll = id.Substring(tilde + 1); id = id.Substring(0, tilde); }
        ItemData root = null;
        foreach (var item in catalog) if (item != null && item.saveId == id) { root = item; break; }
        if (root == null) return null;
        if (roll == null) return root.Honed(level);

        int colon = roll.IndexOf(':');
        var rarity = colon > 0 && int.TryParse(roll.Substring(0, colon), out int r) && System.Enum.IsDefined(typeof(ItemRarity), r) ? (ItemRarity)r : root.rarity;
        var properties = new List<RolledProperty>();
        if (colon >= 0 && colon < roll.Length - 1)
            foreach (var part in roll.Substring(colon + 1).Split(','))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (float.TryParse(part.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    properties.Add(new RolledProperty(part.Substring(0, eq), v));
            }
        return Variant(root, rarity, properties.ToArray(), level);
    }
}

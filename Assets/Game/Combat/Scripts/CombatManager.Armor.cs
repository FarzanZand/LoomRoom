using Sirenix.OdinInspector;
using UnityEngine;

// How armour cuts the hits the player takes (the user, 2026-11-04: the step from no armour to some on the first floors was
// too big; then: a full Common set of the floor's level stops about 30%, a full Legendary set about 50%). Scaled: a share
// of the hit set by how the armour compares with the full-set armour of the gear usual on the floor the fight is on (the
// level's Gear by floor row, each tier's share times that tier's plain set). The share's odds, cut / (1 - cut), are
// (armour / K)^P, with K and P worked out so the usual set stops Target Cut and the same set at Legendary (its armour
// raised by the Legendary step's base bonus, InventoryManager > Rarity) stops Legendary Cut: less stops less, more stops
// more, never past Armor Effectiveness. Flat: each point takes Armor Per Point off the hit (the rule before). Blocked
// hits and the enemies' own armour keep the flat rule. ArmorCut is what the UI shows.
public partial class CombatManager
{
    public enum ArmorRule { Flat = 0, Scaled = 1 }

    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Armor")]
    [Tooltip("How armour cuts the hits the player takes. Scaled: a share of each hit that depends on the floor (a set of the floor's usual gear stops Target Cut). Flat: each point takes Armor Per Point off the hit, the rule before 2026-11-04. Blocked hits and enemies' armour always use Flat.")]
    public ArmorRule playerArmorRule = ArmorRule.Scaled;
    [TabGroup("Tabs", "Hits"), FoldoutGroup("Tabs/Hits/Armor")]
    [Range(.05f, .6f), Tooltip("Scaled rule: the share of a hit a full set of the floor's usual gear stops at Common (the level's Gear by floor row for the floor the fight is on). Worn armour only: the shield counts when blocking.")]
    public float armorTargetCut = .30f;
    [Range(.05f, .65f), Tooltip("Scaled rule: the share of a hit the same set stops at Legendary. Equal to Target Cut or lower, armour scales straight (armour / (K + armour)).")]
    public float armorLegendaryCut = .50f;

    public static bool ScaledPlayerArmor => !HasInstance || Instance.playerArmorRule == ArmorRule.Scaled;
    // The floor the table player is on (1 outside a run).
    public static int CurrentFloor => TableManager.Loader != null ? Mathf.Max(1, TableManager.Loader.FloorNumber) : 1;
    public static float CurrentArmorK => ArmorK(CurrentFloor);

    // The share of a physical hit the player's armour stops on a floor, 0 to Armor Effectiveness. The flat rule depends on
    // the hit's size: give it, or the answer is 0.
    public static float ArmorCut(float armour, int floor, float hit = 0f)
    {
        if (armour <= 0f) return 0f;
        float cap = HasInstance ? Instance.armorEffectiveness : .65f;
        if (ScaledPlayerArmor)
        {
            float odds = Mathf.Pow(armour / ArmorK(floor), ArmorPower);
            return Mathf.Min(cap, odds / (1f + odds));
        }
        return hit > 0f ? Mathf.Min(cap * hit, armour * ArmorPerPoint) / hit : 0f;
    }

    // The armour that stops half a hit on this floor (the curve's K).
    public static float ArmorK(int floor) => Mathf.Max(1f, UsualSetArmor(floor) * Mathf.Pow(Odds(TargetCut), -1f / ArmorPower));

    static float TargetCut => HasInstance ? Instance.armorTargetCut : .3f;
    static float LegendaryCut => HasInstance ? Instance.armorLegendaryCut : .5f;
    static float Odds(float cut) { cut = Mathf.Clamp(cut, .01f, .95f); return cut / (1f - cut); }
    // How much a Legendary piece's armour is raised over the plain one's (1.75 with the Legendary step's +75%).
    static float LegendaryArmor
    {
        get
        {
            if (InventoryManager.HasInstance && InventoryManager.Instance.rarities != null)
                foreach (var step in InventoryManager.Instance.rarities) if (step != null && step.rarity == ItemRarity.Legendary) return 1f + Mathf.Max(0f, step.baseBonus);
            return 1.75f;
        }
    }
    // The curve's steepness: the power that takes the usual set's Target Cut to Legendary Cut at Legendary's armour (1.51
    // with 30%, 50% and +75%); 1 when Legendary Cut is no more than Target Cut.
    public static float ArmorPower
    {
        get
        {
            float a = Odds(TargetCut), b = Odds(LegendaryCut), r = LegendaryArmor;
            return b > a && r > 1.001f ? Mathf.Clamp(Mathf.Log(b / a) / Mathf.Log(r), .25f, 4f) : 1f;
        }
    }

    // A full set (helm, body armour, gloves, boots, leggings) of the floor's usual gear: each tier's share of the floor's Gear
    // by floor row times that tier's set. A level with no table counts tier 1.
    public static float UsualSetArmor(int floor)
    {
        var row = LootSource.FloorGear?.Invoke(Mathf.Max(1, floor));
        if (row == null) return TierSetArmor(1);
        var shares = row.Shares;
        float total = 0f, armour = 0f;
        for (int i = 0; i < shares.Length; i++) { if (shares[i] <= 0f) continue; total += shares[i]; armour += shares[i] * TierSetArmor(i + 1); }
        return total > 0f ? armour / total : TierSetArmor(1);
    }

    static readonly EquipmentSlot[] SetSlots = { EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Gloves, EquipmentSlot.Boots, EquipmentSlot.Legs };
    float[] tierSets;
    // The armour of a plain (Common) full set of a tier, read from the item catalog once (Leather 18, Iron 44, Steel 64...).
    public static float TierSetArmor(int tier)
    {
        tier = Mathf.Clamp(tier, 1, LootFloorTiers.Count);
        if (!HasInstance) return 18f * tier;
        var sets = Instance.tierSets;
        if (sets == null || sets.Length != LootFloorTiers.Count + 1)
        {
            sets = Instance.tierSets = new float[LootFloorTiers.Count + 1];
            var catalog = InventoryManager.HasInstance ? InventoryManager.Instance.itemCatalog : null;
            for (int t = 1; t <= LootFloorTiers.Count; t++)
                foreach (var slot in SetSlots)
                {
                    float best = 0f;
                    if (catalog != null)
                        foreach (var item in catalog)
                        {
                            if (item == null || item.tier != t || item.rarity != ItemRarity.Common || item.itemType != ItemType.Equipment || item.equipSlot != slot || item.statModifiers == null) continue;
                            float value = 0f;
                            foreach (var m in item.statModifiers) if (m != null && m.stat == StatType.Armor && m.type == ModifierType.Flat) value += m.value;
                            best = Mathf.Max(best, value);
                        }
                    sets[t] += best;
                }
            // A tier with no armour in the catalog takes the one below it (never 0, which would make any armour stop everything).
            for (int t = 1; t <= LootFloorTiers.Count; t++) if (sets[t] <= 0f) sets[t] = t > 1 ? sets[t - 1] : 18f;
        }
        return sets[tier];
    }
}

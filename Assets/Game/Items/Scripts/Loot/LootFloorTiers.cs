using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One row of a level's gear-by-floor table (TableLevelData.gearByFloor): from this floor on (until the next row), how
// likely each gear tier is and how often a drop comes one tier better (rarity is on InventoryManager > Rarity). Every loot profile that
// follows the floor tiers (DungeonLootProfile.followFloorTiers) rolls its gear from here instead of its own range.
[Serializable]
public class LootFloorTiers
{
    [Min(1), Tooltip("The first floor (counting from the top of the run) this row applies to; it lasts until the next row's floor.")]
    public int fromFloor = 1;
    [HorizontalGroup("Tiers", LabelWidth = 50), Min(0), LabelText("Bronze"), Tooltip("Tier 1 (Bronze, Leather): its share of gear drops, in percent (the shares are weights; they need not add up to 100).")]
    public float tier1 = 100f;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Iron"), Tooltip("Tier 2 (Iron).")]
    public float tier2;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Steel"), Tooltip("Tier 3 (Steel).")]
    public float tier3;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Crystal"), Tooltip("Tier 4 (Crystal).")]
    public float tier4;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Grave"), Tooltip("Tier 5 (Grave: armour and shields only so far).")]
    public float tier5;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Gilded"), Tooltip("Tier 6 (Gilded: armour and shields only so far).")]
    public float tier6;
    [Range(0, 1), Tooltip("Chance a gear drop comes one tier better than rolled.")]
    public float upgradeChance;

    public string Label
    {
        get
        {
            float total = Mathf.Max(.0001f, tier1 + tier2 + tier3 + tier4 + tier5 + tier6);
            string Share(string name, float w) => w > 0 ? $" {name} {Mathf.RoundToInt(w / total * 100)}%" : "";
            return $"Floor {fromFloor}+:" + Share("Bronze", tier1) + Share("Iron", tier2) + Share("Steel", tier3) + Share("Crystal", tier4) + Share("Grave", tier5) + Share("Gilded", tier6);
        }
    }

    // The tier for one gear drop, by the weights (1 when they are all zero).
    public int Roll(System.Random random)
    {
        float total = tier1 + tier2 + tier3 + tier4 + tier5 + tier6;
        if (total <= 0f) return 1;
        double roll = random.NextDouble() * total;
        if ((roll -= tier1) < 0) return 1;
        if ((roll -= tier2) < 0) return 2;
        if ((roll -= tier3) < 0) return 3;
        if ((roll -= tier4) < 0) return 4;
        if ((roll -= tier5) < 0) return 5;
        return tier6 > 0 ? 6 : tier5 > 0 ? 5 : 4;
    }

    public int LowestTier => tier1 > 0 ? 1 : tier2 > 0 ? 2 : tier3 > 0 ? 3 : tier4 > 0 ? 4 : tier5 > 0 ? 5 : tier6 > 0 ? 6 : 1;
    public int HighestTier => tier6 > 0 ? 6 : tier5 > 0 ? 5 : tier4 > 0 ? 4 : tier3 > 0 ? 3 : tier2 > 0 ? 2 : 1;
}

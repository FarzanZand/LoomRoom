using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One row of a level's gear-by-floor table (TableLevelData.gearByFloor): from this floor on (until the next row), how
// likely each gear tier is, how often a drop comes one tier better and how often it rolls Fine or Masterwork. Every loot profile that
// follows the floor tiers (DungeonLootProfile.followFloorTiers) rolls its gear from here instead of its own range.
[Serializable]
public class LootFloorTiers
{
    [Min(1), Tooltip("The first floor (counting from the top of the run) this row applies to; it lasts until the next row's floor.")]
    public int fromFloor = 1;
    [HorizontalGroup("Tiers", LabelWidth = 50), Min(0), LabelText("Bronze"), Tooltip("Tier 1 (Bronze, Leather): its share of gear drops, in percent (the four shares are weights; they need not add up to 100).")]
    public float tier1 = 100f;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Iron"), Tooltip("Tier 2 (Iron).")]
    public float tier2;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Steel"), Tooltip("Tier 3 (Steel).")]
    public float tier3;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Crystal"), Tooltip("Tier 4 (Crystal).")]
    public float tier4;
    [Range(0, 1), Tooltip("Chance a gear drop comes one tier better than rolled.")]
    public float upgradeChance;
    [Range(0, 1), Tooltip("Chance a gear drop is Fine: 1-2 rolled properties (ItemPropertyTable).")]
    public float fineChance = .3f;
    [Range(0, 1), Tooltip("Chance a gear drop is Masterwork: 3-4 rolled properties. Rolled before Fine.")]
    public float masterworkChance = .1f;

    public string Label
    {
        get
        {
            float total = Mathf.Max(.0001f, tier1 + tier2 + tier3 + tier4);
            string Share(string name, float w) => w > 0 ? $" {name} {Mathf.RoundToInt(w / total * 100)}%" : "";
            return $"Floor {fromFloor}+:" + Share("Bronze", tier1) + Share("Iron", tier2) + Share("Steel", tier3) + Share("Crystal", tier4);
        }
    }

    // The tier for one gear drop, by the four weights (1 when they are all zero).
    public int Roll(System.Random random)
    {
        float total = tier1 + tier2 + tier3 + tier4;
        if (total <= 0f) return 1;
        double roll = random.NextDouble() * total;
        if ((roll -= tier1) < 0) return 1;
        if ((roll -= tier2) < 0) return 2;
        if ((roll -= tier3) < 0) return 3;
        return 4;
    }

    public int LowestTier => tier1 > 0 ? 1 : tier2 > 0 ? 2 : tier3 > 0 ? 3 : 4;
    public int HighestTier => tier4 > 0 ? 4 : tier3 > 0 ? 3 : tier2 > 0 ? 2 : 1;
}

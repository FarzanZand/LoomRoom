using System;
using System.Linq;
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
    [HorizontalGroup("Tiers"), Min(0), LabelText("Elven"), Tooltip("Tier 4 (Elven armour, Crystal weapons and shield).")]
    public float tier4;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Warlord"), Tooltip("Tier 5 (Warlord).")]
    public float tier5;
    [HorizontalGroup("Tiers"), Min(0), LabelText("Verdant"), Tooltip("Tier 6 (Verdant).")]
    public float tier6;
    [HorizontalGroup("Tiers2", LabelWidth = 50), Min(0), LabelText("Shadow"), Tooltip("Tier 7 (Shadow).")]
    public float tier7;
    [HorizontalGroup("Tiers2"), Min(0), LabelText("Grave"), Tooltip("Tier 8 (Grave).")]
    public float tier8;
    [HorizontalGroup("Tiers2"), Min(0), LabelText("Blackened"), Tooltip("Tier 9 (Blackened).")]
    public float tier9;
    [HorizontalGroup("Tiers2"), Min(0), LabelText("Gilded"), Tooltip("Tier 10 (Gilded).")]
    public float tier10;
    [HorizontalGroup("Tiers2"), Min(0), LabelText("Dread"), Tooltip("Tier 11 (Dread).")]
    public float tier11;
    [HorizontalGroup("Tiers2"), Min(0), LabelText("Royal"), Tooltip("Tier 12 (Royal).")]
    public float tier12;
    [Range(0, 1), Tooltip("Chance a gear drop comes one tier better than rolled.")]
    public float upgradeChance;

    // The tiers' names (the armour's; tier 1's weapons and shields are Bronze), and the shares in tier order.
    public static readonly string[] Names = { "Bronze", "Iron", "Steel", "Elven", "Warlord", "Verdant", "Shadow", "Grave", "Blackened", "Gilded", "Dread", "Royal" };
    public const int Count = 12;
    public float[] Shares => new[] { tier1, tier2, tier3, tier4, tier5, tier6, tier7, tier8, tier9, tier10, tier11, tier12 };
    public void SetShare(int tier, float share)
    {
        switch (tier)
        {
            case 1: tier1 = share; break; case 2: tier2 = share; break; case 3: tier3 = share; break; case 4: tier4 = share; break;
            case 5: tier5 = share; break; case 6: tier6 = share; break; case 7: tier7 = share; break; case 8: tier8 = share; break;
            case 9: tier9 = share; break; case 10: tier10 = share; break; case 11: tier11 = share; break; case 12: tier12 = share; break;
        }
    }

    public string Label
    {
        get
        {
            var shares = Shares;
            float total = Mathf.Max(.0001f, shares.Sum());
            var label = $"Floor {fromFloor}+:";
            for (int i = 0; i < Count; i++) if (shares[i] > 0) label += $" {Names[i]} {Mathf.RoundToInt(shares[i] / total * 100)}%";
            return label;
        }
    }

    // The tier for one gear drop, by the weights (1 when they are all zero).
    public int Roll(System.Random random)
    {
        var shares = Shares;
        float total = shares.Sum();
        if (total <= 0f) return 1;
        double roll = random.NextDouble() * total;
        for (int i = 0; i < Count; i++) if ((roll -= shares[i]) < 0) return i + 1;
        return HighestTier;
    }

    public int LowestTier { get { var shares = Shares; for (int i = 0; i < Count; i++) if (shares[i] > 0) return i + 1; return 1; } }
    public int HighestTier { get { var shares = Shares; for (int i = Count - 1; i >= 0; i--) if (shares[i] > 0) return i + 1; return 1; } }
}

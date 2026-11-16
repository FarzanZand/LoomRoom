using System;
using System.Collections.Generic;
using UnityEngine;

[Flags] public enum DungeonLootSource { Enemy=1, Barrel=2, Chest=4, Floor=8, All=15 }

// Anything that can hand out rewards: a biome's tier-based DungeonLootProfile, or a hand-listed
// DungeonLootTable for special cases (a guardian's reward, a room override, a merchant's stock).
public abstract class LootSource : ScriptableObject
{
    public readonly struct Drop
    {
        public readonly ItemData item;
        public readonly int quantity;
        public Drop(ItemData item, int quantity) { this.item = item; this.quantity = quantity; }
    }

    // Character level used while a floor is generated (set by TableLevelLoader).
    public static int? GenerationLevel { get; set; }
    // How far through its biome a floor is, 0 on the first floor to 1 on the last (set by TableLevelLoader).
    public static System.Func<int, float> BiomeProgress { get; set; }
    // The level's gear-by-floor row for a floor, or null (set by TableLevelLoader).
    public static System.Func<int, LootFloorTiers> FloorGear { get; set; }

    // Enter Play Mode without a domain reload keeps statics: start each session with neither set.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { GenerationLevel = null; BiomeProgress = null; FloorGear = null; }

    public abstract List<Drop> RollDrops(System.Random random, int floor, DungeonLootSource source);

    // A roll made as if deeper than the floor it happens on (an elite's drop, a gold chest's deeper roll) is held to
    // this many tiers above the best the real floor gives (the user, 2026-11-16, after a Crystal Mace, tier 4, came
    // off an elite on floor 2, where the floor gives Bronze and Iron: Steel at most there now).
    public const int DeeperRollTiersAbove = 1;
    // The highest tier such a roll may give on this floor; 0 = no cap (the level has no Gear by floor row for it).
    public static int DeeperTierCap(int realFloor)
    {
        var row = FloorGear?.Invoke(Mathf.Max(1, realFloor));
        return row != null ? Mathf.Min(12, row.HighestTier + DeeperRollTiersAbove) : 0;
    }
    // RollDrops with every tier it gives held to Tier Cap (0 = none). A hand-listed table has no tiers to hold.
    public virtual List<Drop> RollDrops(System.Random random, int floor, DungeonLootSource source, int tierCap) => RollDrops(random, floor, source);
    public abstract int RollGold(System.Random random, int floor, DungeonLootSource source);
}

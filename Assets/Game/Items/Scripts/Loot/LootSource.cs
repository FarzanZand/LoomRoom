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

    public abstract List<Drop> RollDrops(System.Random random, int floor, DungeonLootSource source);
    public abstract int RollGold(System.Random random, int floor, DungeonLootSource source);
}

using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A biome's everyday loot. Instead of listing items, it says what kind of reward each source gives
// (gold, gear, recovery, tomes) and which gear tiers drop here. Items are picked from the item catalog
// by their tier and loot weight, so a new item joins the loot just by having a tier.
[CreateAssetMenu(menuName = "LoomRoom/Loot Profile")]
public class DungeonLootProfile : LootSource
{
    [Serializable]
    public class Rule
    {
        [Range(0, 1)] public float goldChance = .5f;
        [HorizontalGroup("Gold"), Min(0)] public int goldMin = 2;
        [HorizontalGroup("Gold"), Min(0)] public int goldMax = 8;
        [Range(0, 1), Tooltip("Chance of one piece of gear (weapon, shield or armor).")] public float gearChance;
        [Range(0, 1), Tooltip("Chance of one recovery item (potion or food).")] public float recoveryChance;
        [Range(0, 1), Tooltip("Chance of one spell tome.")] public float tomeChance;
    }

    [Title("Gear")]
    [MinMaxSlider(1, 4, true), Tooltip("Gear tiers that drop here. 1 Bronze/Leather, 2 Iron, 3 Steel, 4 Crystal. The low end is most common on the biome's first floor, the high end on its last.")]
    public Vector2Int gearTiers = new(1, 1);
    [Range(0, 1), Tooltip("Chance a gear drop is one tier better than rolled: an early upgrade.")]
    public float upgradeChance = .1f;
    [Range(0, 1), Tooltip("Chance a gear drop is honed: +1 on its main stat (damage, armor, a trinket's bonus) and a blue slot. Barony's blessing.")]
    public float honedChance = .1f;
    [Range(0, 1), Tooltip("Chance a honed drop is +2 instead of +1.")]
    public float honedTwiceChance = .2f;
    [Range(0, 1), Tooltip("Gold amounts grow by this fraction per floor beyond the first.")]
    public float goldGrowthPerFloor = .15f;

    [Title("Sources")]
    public Rule enemies = new() { goldChance = .6f, goldMin = 2, goldMax = 6, gearChance = .04f, recoveryChance = .08f, tomeChance = .01f };
    public Rule breakables = new() { goldChance = .5f, goldMin = 1, goldMax = 4, gearChance = .02f, recoveryChance = .06f };
    [Tooltip("Chests, bookshelves, graves and loot placed on the floor.")]
    public Rule chests = new() { goldChance = .6f, goldMin = 4, goldMax = 10, gearChance = .85f, recoveryChance = .7f, tomeChance = .08f };

    Rule For(DungeonLootSource source) => source == DungeonLootSource.Enemy ? enemies : source == DungeonLootSource.Barrel ? breakables : chests;

    public override int RollGold(System.Random random, int floor, DungeonLootSource source)
    {
        var rule = For(source);
        if (random == null || rule == null || random.NextDouble() >= rule.goldChance) return 0;
        float scale = 1 + goldGrowthPerFloor * Mathf.Max(0, floor - 1);
        int min = Mathf.RoundToInt(rule.goldMin * scale), max = Mathf.Max(min, Mathf.RoundToInt(rule.goldMax * scale));
        return random.Next(min, max + 1);
    }

    public override List<Drop> RollDrops(System.Random random, int floor, DungeonLootSource source)
    {
        var drops = new List<Drop>();
        var rule = For(source);
        if (random == null || rule == null) return drops;
        if (random.NextDouble() < rule.gearChance)
        {
            int tier = RollTier(random, floor);
            if (random.NextDouble() < upgradeChance) tier++;
            var gear = Pick(random, i => IsGear(i) && i.tier == tier) ?? Pick(random, i => IsGear(i) && i.tier >= gearTiers.x && i.tier <= gearTiers.y);
            if (gear != null && random.NextDouble() < honedChance) gear = gear.Honed(random.NextDouble() < honedTwiceChance ? 2 : 1);
            if (gear != null) drops.Add(new Drop(gear, 1));
        }
        if (random.NextDouble() < rule.recoveryChance)
        {
            var item = Pick(random, i => i.itemType == ItemType.Consumable && i.spell == null && i.tier <= gearTiers.y);
            if (item != null) drops.Add(new Drop(item, 1));
        }
        if (random.NextDouble() < rule.tomeChance)
        {
            var tome = Pick(random, i => i.spell != null && i.tier <= gearTiers.y);
            if (tome != null) drops.Add(new Drop(tome, 1));
        }
        return drops;
    }

    // Each tier in the range is weighted by how close it sits to the floor's progress through the biome.
    int RollTier(System.Random random, int floor)
    {
        int min = gearTiers.x, max = Mathf.Max(min, gearTiers.y);
        if (max == min) return min;
        float progress = Mathf.Clamp01(BiomeProgress?.Invoke(floor) ?? .5f), total = 0;
        var weights = new float[max - min + 1];
        for (int t = 0; t < weights.Length; t++) total += weights[t] = Mathf.Max(.1f, 1 - Mathf.Abs(t / (float)(weights.Length - 1) - progress));
        double roll = random.NextDouble() * total;
        for (int t = 0; t < weights.Length; t++) if ((roll -= weights[t]) < 0) return min + t;
        return max;
    }

    static bool IsGear(ItemData i) => i.itemType == ItemType.Weapon || i.itemType == ItemType.Shield || i.itemType == ItemType.Equipment;

    // Weighted pick among catalog items with a tier (tier 0 never drops at random).
    static ItemData Pick(System.Random random, Predicate<ItemData> match)
    {
        if (!InventoryManager.HasInstance || InventoryManager.Instance.itemCatalog == null) return null;
        float total = 0;
        foreach (var i in InventoryManager.Instance.itemCatalog) if (i != null && i.tier > 0 && match(i) && InventoryManager.Instance.CanDropLoot(i)) total += i.lootWeight;
        if (total <= 0) return null;
        double roll = random.NextDouble() * total;
        foreach (var i in InventoryManager.Instance.itemCatalog)
        {
            if (i == null || i.tier <= 0 || !match(i) || !InventoryManager.Instance.CanDropLoot(i)) continue;
            roll -= i.lootWeight;
            if (roll < 0) return i;
        }
        return null;
    }
}

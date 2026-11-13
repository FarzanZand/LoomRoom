using System.Collections.Generic;
using UnityEngine;

// The odds of each item on a floor, worked out from the same rules RollDrops rolls by (the Item Database's Loot view,
// the user, 2026-11-13: "WHERE CAN I SEE IT MYSELF?!"). Not a simulation: for each gear tier the floor can roll (the
// level's Gear by floor row, or this profile's own tiers, then the upgrade), the tome share, the tome pick (tiers up to
// the rolled tier or the floor's highest), the gear pick (the rolled tier, a kind's best below it, else the floor's whole
// range) and the recovery pick (consumables up to the floor's highest tier), each item weighted by its loot weight.
// Kept beside RollDrops: a change to the roll is a change here too.
public partial class DungeonLootProfile
{
    public class Odds
    {
        // Each item's chance within its kind of drop: of the floor's tome drops, gear drops, recovery drops.
        public readonly Dictionary<ItemData, float> tomes = new(), gear = new(), recovery = new();
        // Of every gear roll, the share that comes out a spell tome.
        public float tomeShareOfGear;
        public int lowest, highest, recoveryTop;
        public bool spellTomes;
        // Chance of each tier for a gear roll, after the upgrade (index = tier, up to 13).
        public float[] tierChance = new float[14];
        public float ChanceOf(ItemData item) =>
            tomes.TryGetValue(item, out float t) ? t : gear.TryGetValue(item, out float g) ? g : recovery.TryGetValue(item, out float r) ? r : 0f;
    }

    // row: the level's Gear by floor row for the floor (null for none); progress: how far through its biome the floor is
    // (0 first floor, 1 last), used only without a row; catalog: the items loot picks from (InventoryManager's catalog);
    // tomeShare: InventoryManager > Tome Share.
    public Odds OddsOn(LootFloorTiers row, float progress, IList<ItemData> catalog, float tomeShare)
    {
        var odds = new Odds { spellTomes = spellTomes };
        if (!followFloorTiers) row = null;
        odds.lowest = row != null ? row.LowestTier : gearTiers.x;
        odds.highest = row != null ? row.HighestTier : gearTiers.y;
        odds.recoveryTop = odds.highest;
        if (catalog == null) return odds;

        // The rolled tier, before the upgrade.
        var rolled = new float[14];
        if (row != null)
        {
            var shares = row.Shares; float total = 0f;
            foreach (float s in shares) total += s;
            if (total <= 0f) rolled[1] = 1f;
            else for (int t = 0; t < LootFloorTiers.Count; t++) rolled[t + 1] = shares[t] / total;
        }
        else
        {
            int min = gearTiers.x, max = Mathf.Max(min, gearTiers.y);
            if (max == min) rolled[Mathf.Clamp(min, 0, 12)] = 1f;
            else
            {
                float p = Mathf.Clamp01(progress), total = 0f;
                var w = new float[max - min + 1];
                for (int t = 0; t < w.Length; t++) total += w[t] = Mathf.Max(.1f, 1 - Mathf.Abs(t / (float)(w.Length - 1) - p));
                for (int t = 0; t < w.Length; t++) rolled[Mathf.Clamp(min + t, 0, 12)] += w[t] / total;
            }
        }
        float upgrade = row != null ? row.upgradeChance : upgradeChance;
        for (int t = 0; t < 14; t++) odds.tierChance[t] = rolled[t] * (1f - upgrade) + (t > 0 ? rolled[t - 1] * upgrade : 0f);

        bool Can(ItemData i) => i != null && i.tier > 0 && i.lootWeight > 0f;
        var top = TopTiers(catalog);
        float tomeTotal = 0f, gearTotal = 0f;
        for (int t = 1; t < 14; t++)
        {
            float p = odds.tierChance[t];
            if (p <= 0f) continue;
            // A tome: Tome Share of the roll, among the tomes at the rolled tier or the floor's highest, whichever is higher.
            float tomeP = 0f;
            if (spellTomes)
            {
                int cap = Mathf.Max(t, odds.highest); float weight = 0f;
                foreach (var i in catalog) if (Can(i) && IsTome(i) && i.tier <= cap) weight += i.lootWeight;
                if (weight > 0f)
                {
                    tomeP = p * tomeShare;
                    foreach (var i in catalog) if (Can(i) && IsTome(i) && i.tier <= cap) Add(odds.tomes, i, tomeP * i.lootWeight / weight);
                }
            }
            // Gear otherwise: the rolled tier (or a kind's best below it), else anything in the floor's range.
            float gearP = p - tomeP, gearWeight = 0f;
            foreach (var i in catalog) if (Can(i) && IsGear(i) && FitsTier(i, t, top)) gearWeight += i.lootWeight;
            bool fallback = gearWeight <= 0f;
            if (fallback) foreach (var i in catalog) if (Can(i) && IsGear(i) && i.tier >= odds.lowest && i.tier <= odds.highest) gearWeight += i.lootWeight;
            if (gearWeight > 0f)
            {
                foreach (var i in catalog)
                    if (Can(i) && IsGear(i) && (fallback ? i.tier >= odds.lowest && i.tier <= odds.highest : FitsTier(i, t, top)))
                        Add(odds.gear, i, gearP * i.lootWeight / gearWeight);
            }
            else gearP = 0f;
            tomeTotal += tomeP; gearTotal += gearP;
        }
        odds.tomeShareOfGear = tomeTotal + gearTotal > 0f ? tomeTotal / (tomeTotal + gearTotal) : 0f;
        Normalise(odds.tomes, tomeTotal); Normalise(odds.gear, gearTotal);

        // Recovery: a consumable (not a tome) up to the floor's highest tier.
        float recoveryWeight = 0f;
        foreach (var i in catalog) if (Can(i) && i.itemType == ItemType.Consumable && i.spell == null && i.tier <= odds.recoveryTop) recoveryWeight += i.lootWeight;
        if (recoveryWeight > 0f)
            foreach (var i in catalog) if (Can(i) && i.itemType == ItemType.Consumable && i.spell == null && i.tier <= odds.recoveryTop) odds.recovery[i] = i.lootWeight / recoveryWeight;
        return odds;
    }

    static void Add(Dictionary<ItemData, float> d, ItemData i, float p) => d[i] = (d.TryGetValue(i, out float was) ? was : 0f) + p;
    static void Normalise(Dictionary<ItemData, float> d, float total)
    {
        if (total <= 0f) { d.Clear(); return; }
        foreach (var k in new List<ItemData>(d.Keys)) d[k] /= total;
    }

    // Whether the roll treats it as a tome, as gear, or as a recovery item (the kind of drop it comes from).
    public static bool IsTomeDrop(ItemData i) => i != null && IsTome(i);
    public static bool IsGearDrop(ItemData i) => i != null && !IsTome(i) && IsGear(i);
    public static bool IsRecoveryDrop(ItemData i) => i != null && i.itemType == ItemType.Consumable && i.spell == null;
}

using UnityEngine;

// The weapon damage usual on a floor, for numbers sized by the floor's gear (the damage property, ItemPropertyDef
// sizedByFloorWeapon): the level's Gear by floor row, each tier's share times that tier's plain sword and mace.
public partial class CombatManager
{
    // The average damage of a plain (Common) sword and mace of the gear usual on this floor. A level with no table counts tier 1.
    public static float UsualWeaponDamage(int floor)
    {
        var row = LootSource.FloorGear?.Invoke(Mathf.Max(1, floor));
        if (row == null) return TierWeaponDamage(1);
        var shares = row.Shares;
        float total = 0f, damage = 0f;
        for (int i = 0; i < shares.Length; i++) { if (shares[i] <= 0f) continue; total += shares[i]; damage += shares[i] * TierWeaponDamage(i + 1); }
        return total > 0f ? damage / total : TierWeaponDamage(1);
    }

    float[] tierWeapons;
    // The average damage of a tier's plain sword and mace, read from the item catalog once (Bronze 4.5, Iron 6.5, Steel 8.5...).
    public static float TierWeaponDamage(int tier)
    {
        tier = Mathf.Clamp(tier, 1, LootFloorTiers.Count);
        if (!HasInstance) return 4f + 2f * (tier - 1);
        var known = Instance.tierWeapons;
        if (known == null || known.Length != LootFloorTiers.Count + 1)
        {
            known = Instance.tierWeapons = new float[LootFloorTiers.Count + 1];
            var catalog = InventoryManager.HasInstance ? InventoryManager.Instance.itemCatalog : null;
            for (int t = 1; t <= LootFloorTiers.Count; t++)
            {
                float sum = 0f; int count = 0;
                if (catalog != null)
                    foreach (var item in catalog)
                    {
                        if (item == null || item.tier != t || item.rarity != ItemRarity.Common || item.itemType != ItemType.Weapon || item.statModifiers == null) continue;
                        foreach (var m in item.statModifiers) if (m != null && m.stat == StatType.AttackDamage && m.type == ModifierType.Flat) { sum += m.value; count++; break; }
                    }
                known[t] = count > 0 ? sum / count : 0f;
            }
            // A tier with no weapon in the catalog takes the one below it.
            for (int t = 1; t <= LootFloorTiers.Count; t++) if (known[t] <= 0f) known[t] = t > 1 ? known[t - 1] : 4f;
        }
        return known[tier];
    }
}

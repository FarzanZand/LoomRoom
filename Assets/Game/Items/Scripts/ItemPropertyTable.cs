using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// One rolled property on a rolled item: which property (ItemPropertyDef.id) and its rolled value in the
// numbers the tooltip shows (7 for +7% critical chance). Saved inside the item's id ("iron_sword~3:crit=7").
[Serializable]
public struct RolledProperty
{
    public string id;
    public float value;
    public RolledProperty(string id, float value) { this.id = id; this.value = value; }
}

// A property gear can roll: the stat it adds, the slots it rolls on, how often, and how big by floor of the run.
[Serializable]
public class ItemPropertyDef
{
    [HorizontalGroup("Id"), Tooltip("Saved in item ids; never change one after release.")] public string id;
    [HorizontalGroup("Id"), LabelWidth(55)] public bool enabled = true;
    public StatType stat;
    [Tooltip("Stat value per rolled point: 0.01 for percent stats (7 = +7%), 1 for flat ones.")]
    public float statPerPoint = 1f;
    public GearSlots slots;
    [Min(0)] public float weight = 1f;
    [Tooltip("The rolled value on floors 1, 2, 3 and 4 of a run (Uniques roll one of theirs a floor deeper, Legendaries two).")]
    public float[] byFloor = { 1, 2, 3, 4 };
    [Tooltip("Added for each floor past the last value, for longer dungeons.")]
    public float growthPerFloor;
    [Tooltip("Sized by the floor's armour instead of By Floor: a share of one plain piece of the gear usual on the floor (CombatManager.UsualSetArmor / 5), at least 1, so a roll on the first floors can't double a Leather piece (the armour roll, 2026-11-04).")]
    public bool sizedByFloorArmor;
    [ShowIf(nameof(sizedByFloorArmor)), Range(.05f, 1f), Tooltip("The share of a plain piece one roll adds (0.25: 1 on Leather floors, 8 on floor 9's Grave).")]
    public float floorArmorShare = .25f;
    [Tooltip("Sized by the floor's weapons instead of By Floor: a share of the damage of a plain weapon usual on the floor (CombatManager.UsualWeaponDamage), whole, at least 1 (the damage roll, 2026-11-04: rarity was too strong on weapons).")]
    public bool sizedByFloorWeapon;
    [ShowIf(nameof(sizedByFloorWeapon)), Range(.05f, 1f), Tooltip("The share of a plain weapon's damage one roll adds (0.15: 1 on Bronze floors, 4 on floor 9's Grave).")]
    public float floorWeaponShare = .15f;

    public float ValueAt(int step)
    {
        if (sizedByFloorWeapon) return Mathf.Max(1f, Mathf.Round(floorWeaponShare * CombatManager.UsualWeaponDamage(Mathf.Max(0, step) + 1)));
        if (sizedByFloorArmor) return Mathf.Max(1f, Mathf.Round(floorArmorShare * CombatManager.UsualSetArmor(Mathf.Max(0, step) + 1) / 5f));
        if (byFloor == null || byFloor.Length == 0) return 0f;
        if (step < byFloor.Length) return byFloor[Mathf.Max(0, step)];
        float value = byFloor[byFloor.Length - 1] + growthPerFloor * (step - byFloor.Length + 1);
        // Whole numbers stay whole (no "Kills heal 4.5"); small values (light radius, mana regen) keep one decimal.
        bool whole = true; foreach (var v in byFloor) if (Mathf.Abs(v - Mathf.Round(v)) > .001f) whole = false;
        return whole ? Mathf.Round(value) : Mathf.Round(value * 10f) / 10f;
    }
}

// Every property rolled gear (Uncommon, Rare, Unique, Legendary) can roll (Items/Data/Item Properties). Lives on InventoryManager.
[CreateAssetMenu(menuName = "Items/Item Property Table")]
public class ItemPropertyTable : ScriptableObject
{
    // How many properties each rarity rolls and its price are on InventoryManager > Rarity. A Unique's first property is a floor deeper, a Legendary's first two.
    [Range(0, 1), Tooltip("Chance each property rolls one floor lower than the floor it drops on.")] public float lowerChance = .3f;
    [ListDrawerSettings(ShowFoldout = false, ListElementLabelName = "id")]
    public List<ItemPropertyDef> properties = new();

    public static ItemPropertyTable Current => InventoryManager.HasInstance ? InventoryManager.Instance.itemProperties : null;

    public ItemPropertyDef Find(string id)
    {
        if (properties == null || string.IsNullOrEmpty(id)) return null;
        foreach (var p in properties) if (p != null && p.id == id) return p;
        return null;
    }

    // The slot a piece of gear rolls for (None: not gear).
    public static GearSlots SlotOf(ItemData item)
    {
        if (item == null || !item.IsGear) return GearSlots.None;
        if (item.itemType == ItemType.Weapon) return GearSlots.Weapon;
        if (item.itemType == ItemType.Shield) return GearSlots.Shield;
        return item.equipSlot switch
        {
            EquipmentSlot.Body => GearSlots.Body,
            EquipmentSlot.Head => GearSlots.Helm,
            EquipmentSlot.Gloves => GearSlots.Gloves,
            EquipmentSlot.Boots => GearSlots.Boots,
            EquipmentSlot.Legs => GearSlots.Legs,
            EquipmentSlot.Trinket1 or EquipmentSlot.Trinket2 => item.TypeLine() == "Ring" ? GearSlots.Ring : GearSlots.Amulet,
            _ => GearSlots.None,
        };
    }

    // The properties of one Uncommon, Rare, Unique or Legendary drop on a floor, drawn from the random the loot uses (seeded).
    public RolledProperty[] Roll(ItemData gear, ItemRarity rarity, int floor, System.Random random)
    {
        var slot = SlotOf(gear);
        var rarityStep = InventoryManager.StepFor(rarity);
        if (slot == GearSlots.None || rarityStep == null || properties == null) return Array.Empty<RolledProperty>();
        var range = rarityStep.properties;
        int count = random.Next(range.x, Mathf.Max(range.x, range.y) + 1);
        var pool = new List<ItemPropertyDef>();
        // A stat the item already has stays off the list (a sword's crit, a ring's health), except its main number.
        var own = new HashSet<StatType>();
        if (gear.BaseItem.statModifiers != null) foreach (var m in gear.BaseItem.statModifiers) if (m != null) own.Add(m.stat);
        own.Remove(StatType.AttackDamage); own.Remove(StatType.Armor);
        foreach (var p in properties) if (p != null && p.enabled && p.weight > 0 && (p.slots & slot) != 0 && !string.IsNullOrEmpty(p.id) && !own.Contains(p.stat)) pool.Add(p);
        var rolled = new List<RolledProperty>();
        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            float total = 0; foreach (var p in pool) total += p.weight;
            double roll = random.NextDouble() * total;
            var pick = pool[pool.Count - 1];
            foreach (var p in pool) { roll -= p.weight; if (roll < 0) { pick = p; break; } }
            pool.Remove(pick);
            pool.RemoveAll(p => p.stat == pick.stat);
            int step = floor - 1 - (random.NextDouble() < lowerChance ? 1 : 0) + (rarity == ItemRarity.Unique && i == 0 ? 1 : 0) + (rarity == ItemRarity.Legendary && i < 2 ? 1 : 0);
            rolled.Add(new RolledProperty(pick.id, pick.ValueAt(Mathf.Max(0, step))));
        }
        return rolled.ToArray();
    }
}

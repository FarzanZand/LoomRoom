using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One gear rarity above Plain (InventoryManager > Rarity): how many properties it rolls, how much it adds to the
// item's main number, its price, and how likely gear comes out this rarity by floor of the run.
[Serializable]
public class RarityStep
{
    [HorizontalGroup("Top"), HideLabel] public ItemRarity rarity = ItemRarity.Fine;
    [HorizontalGroup("Top"), LabelWidth(70), MinMaxSlider(0, 6, true), Tooltip("How many properties it rolls (ItemPropertyTable).")]
    public Vector2Int properties = new(1, 1);
    [Range(0, 2), Tooltip("Added to the item's main number (a weapon's damage, a shield's or armour's armour), as a share of the plain item's: 0.2 = +20%. Whole numbers stay whole.")]
    public float baseBonus = .2f;
    [Min(0), Tooltip("Times the plain item's price.")]
    public float priceMultiplier = 1.6f;
    [Range(0, 1), Tooltip("Chance a piece of gear comes out this rarity on floor 1 of a run (before the source and whole-game multipliers).")]
    public float chance = .2f;
    [Range(0, .2f), Tooltip("Added to the chance for each floor past the first.")]
    public float chancePerFloor = .027f;

    public float ChanceOn(int floor) => Mathf.Clamp01(chance + chancePerFloor * Mathf.Max(0, floor - 1));
}

using System;
using UnityEngine;

// What one kind of loot source drops everywhere (InventoryManager > Drops by source: enemy kills, breakables, chests):
// its gold, gear and recovery chances, plus how much likelier its gear is to roll a rarity. A loot profile with
// Own Drop Chances on (a boss's reward) uses its own numbers instead.
[Serializable]
public class SourceDrops : DungeonLootProfile.Rule
{
    [Min(0), Tooltip("Times every rarity chance (InventoryManager > Rarity) for gear from this source. 1 = as set there.")]
    public float rarityMultiplier = 1f;
}

using System;
using UnityEngine;

[CreateAssetMenu(menuName = "Table/Loot Table")]
public class DungeonLootTable : ScriptableObject
{
    [Serializable] public class Entry
    {
        public ItemData item;
        [Min(0)] public float weight = 1;
    }
    public Entry[] entries;
    [Range(0, 1)] public float enemyDropChance = .7f;
    public ItemData Roll(System.Random random)
    {
        float total = 0;
        if (entries == null) return null;
        foreach (var e in entries) if (e.item != null) total += Mathf.Max(0, e.weight);
        if (total <= 0) return null;
        double roll = random.NextDouble() * total;
        foreach (var e in entries)
        {
            if (e.item == null || e.weight <= 0) continue;
            roll -= e.weight;
            if (roll <= 0) return e.item;
        }
        return null;
    }
}

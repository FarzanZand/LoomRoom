using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class DungeonLootDrop : MonoBehaviour
{
    public DungeonLootTable table;
    [Tooltip("Use this enemy's own table instead of its floor/room reward table.")] public bool overrideLevelTable;
    public ItemData carriedItem;
    public int seed;
    [Min(1)] public int floorNumber=1;
    [Tooltip("Extra gold on top of the table's gold, e.g. for bosses.")] [Min(0)] public int bonusGold;
    bool dropped;
    Character character;
    void Awake() { character = GetComponent<Character>(); character.Died += Drop; }
    void OnDestroy() { if (character != null) character.Died -= Drop; }
    void Drop()
    {
        if (dropped) return;
        dropped = true;
        var rng = new System.Random(seed);
        var drops = new List<DungeonLootTable.Drop>();
        if (carriedItem != null) drops.Add(new DungeonLootTable.Drop(carriedItem,1));
        if (table != null) drops.AddRange(table.RollDrops(rng,floorNumber,DungeonLootSource.Enemy));
        // Gold rolls after items so the item results match earlier seeds.
        int gold = bonusGold + (table != null ? table.RollGold(rng,floorNumber,DungeonLootSource.Enemy) : 0);
        if (CombatManager.HasInstance && CombatManager.Instance.lootableCorpses)
        {
            var corpse = GetComponent<DungeonCorpse>();
            if (corpse == null) corpse = gameObject.AddComponent<DungeonCorpse>();
            corpse.Fill(drops, gold);
            return;
        }
        Spill(drops, gold, transform.position, transform.parent);
    }
    public static void Spill(IReadOnlyList<DungeonLootTable.Drop> drops, int gold, Vector3 position, Transform parent)
    {
        if (drops != null && drops.Count > 0) DungeonPickup.SpawnDrops(drops, position, parent);
        if (gold > 0 && CurrencyManager.HasInstance) CurrencyManager.Instance.SpawnCoins(gold, position, parent);
    }
}

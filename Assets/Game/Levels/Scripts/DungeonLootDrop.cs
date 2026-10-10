using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Character))]
public class DungeonLootDrop : MonoBehaviour
{
    public LootSource table;
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
        var drops = new List<LootSource.Drop>();
        if (carriedItem != null && (!InventoryManager.HasInstance || InventoryManager.Instance.CanDropLoot(carriedItem))) drops.Add(new LootSource.Drop(carriedItem,1));
        // Gold rolls after items so the item results match earlier seeds.
        var (items, tableGold) = Roll(table, rng, floorNumber, DungeonLootSource.Enemy);
        drops.AddRange(items);
        int gold = bonusGold + tableGold;
        if (CombatManager.HasInstance && CombatManager.Instance.lootableCorpses)
        {
            var corpse = GetComponent<DungeonCorpse>();
            if (corpse == null) corpse = gameObject.AddComponent<DungeonCorpse>();
            corpse.Fill(drops, gold);
            return;
        }
        Spill(drops, gold, transform.position, transform.parent);
    }
    // A table's items, then its gold, from one stream (nothing from no table).
    public static (List<LootSource.Drop> items, int gold) Roll(LootSource table, System.Random rng, int floor, DungeonLootSource source)
    {
        if (table == null) return (new List<LootSource.Drop>(), 0);
        var items = table.RollDrops(rng, floor, source);
        return (items ?? new List<LootSource.Drop>(), table.RollGold(rng, floor, source));
    }
    public static void Spill(IReadOnlyList<LootSource.Drop> drops, int gold, Vector3 position, Transform parent)
    {
        if (drops != null && drops.Count > 0) DungeonPickup.SpawnDrops(drops, position, parent);
        if (gold > 0 && CurrencyManager.HasInstance) CurrencyManager.Instance.SpawnCoins(gold, position, parent);
    }
}

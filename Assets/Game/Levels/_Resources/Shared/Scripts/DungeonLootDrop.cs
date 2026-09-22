using UnityEngine;

[RequireComponent(typeof(Character))]
public class DungeonLootDrop : MonoBehaviour
{
    public DungeonLootTable table;
    [Tooltip("Use this enemy's own table instead of its floor/room reward table.")] public bool overrideLevelTable;
    public ItemData carriedItem;
    public int seed;
    [Min(1)] public int floorNumber=1;
    bool dropped;
    Character character;
    void Awake() { character = GetComponent<Character>(); character.Died += Drop; }
    void OnDestroy() { if (character != null) character.Died -= Drop; }
    void Drop()
    {
        if (dropped) return;
        dropped = true;
        var rng = new System.Random(seed);
        if (carriedItem != null) DungeonPickup.Spawn(carriedItem, transform.position, transform.parent);
        if (table != null)
            DungeonPickup.SpawnDrops(table.RollDrops(rng,floorNumber,DungeonLootSource.Enemy),transform.position,transform.parent);
    }
}

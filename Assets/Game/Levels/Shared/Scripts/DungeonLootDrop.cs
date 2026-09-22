using UnityEngine;

[RequireComponent(typeof(Character))]
public class DungeonLootDrop : MonoBehaviour
{
    public DungeonLootTable table;
    public ItemData carriedItem;
    public int seed;
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
        if (table != null && rng.NextDouble() < table.enemyDropChance)
            DungeonPickup.Spawn(table.Roll(rng), transform.position + transform.right * .45f, transform.parent);
    }
}

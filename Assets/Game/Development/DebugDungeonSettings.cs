using UnityEngine;

public class DebugDungeonSettings : ScriptableObject
{
    [Tooltip("The test arena: an authored level with one big open room.")]
    public TableLevelData level;
    public AdventurerClass startingClass;
    [Tooltip("Put in the table player's bag once the arena is built, on top of the class's own (to try other weapons).")]
    public ItemData[] startingItems = new ItemData[0];
    [Tooltip("Spawned in an arc in front of the player, each just outside its own detection radius.")]
    public GameObject[] enemies = new GameObject[0];
    [Min(0f), Tooltip("Extra distance beyond each enemy's detection radius.")]
    public float margin = 3f;
    [Tooltip("Enemies only fight back once hit.")]
    public bool retaliateOnly;
    [Tooltip("Chests set down a few metres to the player's right, one beside the other (wood, iron, gold), each with the biome's loot.")]
    public GameObject[] chests = new GameObject[0];
}

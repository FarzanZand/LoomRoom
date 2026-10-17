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
    [Tooltip("A row on the player's left, facing them: idle until hit, then they fight back.")]
    public GameObject[] fightBackRow = new GameObject[0];
    [Tooltip("A row on the player's right, past the chests, facing them: idle, never fight back, with Dummy Health (for testing hits, gore and impact).")]
    public GameObject[] dummyRow = new GameObject[0];
    [Min(1f)] public float dummyHealth = 1000f;
    [Min(2f), Tooltip("Metres to the side of the player for each row.")] public float rowDistance = 8f;
    [Min(1f), Tooltip("Metres between enemies in a row.")] public float rowSpacing = 3f;
    [Tooltip("Chests set down a few metres to the player's right, one beside the other (wood, iron, gold), each with the biome's loot.")]
    public GameObject[] chests = new GameObject[0];
}

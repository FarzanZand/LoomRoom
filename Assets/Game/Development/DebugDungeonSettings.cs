using UnityEngine;

public class DebugDungeonSettings : ScriptableObject
{
    [Tooltip("The test arena: an authored level with one big open room.")]
    public TableLevelData level;
    public AdventurerClass startingClass;
    [Tooltip("Spawned in an arc in front of the player, each just outside its own detection radius.")]
    public GameObject[] enemies = new GameObject[0];
    [Min(0f), Tooltip("Extra distance beyond each enemy's detection radius.")]
    public float margin = 3f;
    [Tooltip("Enemies only fight back once hit.")]
    public bool retaliateOnly;
}

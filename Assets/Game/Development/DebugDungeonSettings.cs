using UnityEngine;

public class DebugDungeonSettings : ScriptableObject
{
    public TableLevelData level;
    [Min(1)] public int enemyCount = 4;
    public AdventurerClass startingClass;
}

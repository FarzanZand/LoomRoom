using UnityEngine;

// A door two cells wide (the user, 2026-11-15: the bridge rooms' doors stood on one of the bridge's two cells): two
// dungeon door leaves side by side, each hinged at the doorway's outer edge (DungeonDoor.hinge Left and Right, never
// moved to the opener's left), their handles meeting in the middle, each the other's partner, so one use or one blow
// opens both, from the centre outwards and away from whoever opens them, from either side. Made from the level's own
// door by DoubleDoorSetup.Build() (an eval), so each leaf keeps everything the single door has. The generator sets one
// down across a way in two cells wide (TableLevelData.doubleDoorPrefab, DungeonGenerator.MakeDoors) and fits each
// leaf's lintel as it does a single door's.
public class DungeonDoubleDoor : MonoBehaviour
{
    [Tooltip("The leaf on the left as seen from in front (local -x), hinged on its left.")]
    public DungeonDoor left;
    [Tooltip("The leaf on the right as seen from in front (local +x), hinged on its right.")]
    public DungeonDoor right;
}

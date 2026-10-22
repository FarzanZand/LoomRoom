using Sirenix.OdinInspector;
using UnityEngine;

// One room of a map template (DungeonMapTemplate): a child of the map prefab that holds this room's interior. It sits on
// the room's centre cell (the map's Sync rooms puts it there) and carries a DungeonRoomTemplate for the room's own
// settings: style, height, light, events, the Dungeon Master's lines on the way in. Everything under it is this room's:
// props, kit pieces, sockets, enemies, chests. The generator builds it like any hand-built room.
[DisallowMultipleComponent]
public class DungeonMapRoom : MonoBehaviour
{
    [Tooltip("The room's mark on the map's grid: 0-9, then A-Z. 0 is where the player starts; the highest is the exit with the stairs.")]
    public string room = "0";
    [Tooltip("What kind of room it is (combat rooms roll enemies, treasure rooms loot, the exit has the stairs).")]
    public DungeonGenerator.RoomRole role = DungeonGenerator.RoomRole.Combat;
    [Range(0, 3), Tooltip("Board-game levels: how many layers the room is raised; stairs lead up to it.")]
    public int raised;
    [Tooltip("The merchant stands in this room (the level needs a Merchant Prefab).")]
    public bool merchant;
    [Tooltip("No hand-built interior: the generator furnishes this room like any other (room 0 then gets the level's Starting Room). What is placed under it is not used.")]
    public bool generated;

    public char Mark => string.IsNullOrEmpty(room) ? '\0' : room[0];
}

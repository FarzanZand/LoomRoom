using System;
using Sirenix.OdinInspector;
using UnityEngine;

// Serialized by integer: append only.
public enum DungeonPropPlacement
{
    AgainstWall = 0,   // back to a wall or pillar, facing into the room
    Corner      = 1,   // in an inside corner, facing into the room
    Centre      = 2,   // away from every wall
    Anywhere    = 3,   // any free cell, random quarter turn
}

// Furnishing for generated rooms, scaled by floor area. A prop is authored facing +Z (into the
// room) with its pivot at the middle of its footprint, on the floor. Props never land on the
// walkways from doorways to the room centre.
[Serializable]
public class DungeonPropRule
{
    [AssetsOnly, Required] public GameObject prefab;
    public DungeonPropPlacement placement = DungeonPropPlacement.AgainstWall;
    [Tooltip("Cells covered: X along the wall, Y into the room.")]
    public Vector2Int footprint = Vector2Int.one;
    [Range(0, 5), Tooltip("Props per 10 floor cells. A 6x8 room at 1 gets about five.")]
    public float perTenCells = .5f;
    [MinMaxSlider(0, 12, true), Tooltip("Clamp on the count per room.")]
    public Vector2Int perRoom = new Vector2Int(0, 4);
    [Range(0, 1), Tooltip("Chance a room uses this rule at all.")]
    public float chance = 1f;
    public DungeonRoomRoles rooms = DungeonRoomRoles.Combat | DungeonRoomRoles.Treasure | DungeonRoomRoles.Rest | DungeonRoomRoles.Storage | DungeonRoomRoles.Exit;
}

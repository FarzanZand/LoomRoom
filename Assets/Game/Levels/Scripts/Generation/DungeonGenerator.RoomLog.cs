using UnityEngine;

// Editor and development builds: names the room at a point for the developer prompt's game state.
public partial class DungeonGenerator
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // The room at a point, for the developer prompt's game state: "room 4 (Combat): Not a witch" or "corridor".
    public string DescribeRoomAt(Vector3 position)
    {
        if (Layout == null) return "";
        int room = Layout.RoomAt(CellOf(position));
        if (room < 0) return "corridor";
        string role = Roles != null && room < Roles.Length ? Roles[room].ToString() : "?";
        var template = templates != null && room < templates.Length ? templates[room] : null;
        return $"room {room} ({role}): {(template != null ? template.gameObject.name : "generated")}";
    }
#endif
}

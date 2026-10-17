using System.Collections.Generic;
using UnityEngine;

// The Dungeon Master says one of a room template's Enter Lines the first time the table player walks into that room
// on this floor (a pocket too: each floor and pocket is its own generator). Not while a boss fight holds his remarks.
public partial class DungeonGenerator
{
    readonly HashSet<int> greetedRooms = new();
    object greetedFloor; // the templates array, new for every floor

    void LateUpdate()
    {
        if (Layout == null || templates == null || !PlayerManager.HasInstance || DungeonMasterRemarks.Hold) return;
        var player = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (player == null || !player.isActiveAndEnabled) return;
        if (greetedFloor != templates) { greetedRooms.Clear(); greetedFloor = templates; }
        int room = Layout.RoomAt(CellOf(player.transform.position));
        if (room < 0 || room >= templates.Length || templates[room] == null || !greetedRooms.Add(room)) return;
        var line = templates[room].PickEnterLine();
        if (line != null) DungeonMaster.Say(line, null, .6f);
    }
}

using UnityEngine;

// How many enemies a hand-built room holds (the user, 2026-11-15: "Why was there 4 enemies in this room when I want max
// 1-2 on the first floor? Rooms that aren't scripted should follow standard room enemy rules like count."). A room built
// from a template keeps to the same count as a generated one: the biome's roll for a combat room (Min-Max Enemies Per
// Room), one guard in a treasure or storage room, none in any other room (rest, entrance, exit, the merchant's). The
// template's Enemy sockets, Enemy Spawners and enemies dragged in are only places to put them: when there are more than
// the roll, that many are kept, picked by the floor's seed, and the rest stay empty; generated enemies make up any
// shortfall as before. A socket's own enemy (overridePrefab: the Rat nest's rats) still decides which enemy stands there,
// not how many.
// Every floor's own roll: a floor whose biome allows at most 3 holds at most 3 (the user: "if max count is 3 in floor X
// for instance, the ossuary should only spawn 3 and not 4").
// Left out (they keep every socket): a template with Override Enemy Count Restrictions on (the scripted event rooms;
// off on every other room, the user, 2026-11-15), set pieces whose sockets are their cast (socketEnemiesOnly), ambush
// rooms (their sockets are where the waves come in), the boss stage and a guardian's arena, and authored maps
// (SpawnAuthoredContent; they never come through this).
public partial class DungeonGenerator
{
    // The floor's seed, for the pick of which sockets stay (set as the build begins).
    int roomCapSeed;

    // Cuts the room's filled Enemy sockets to at most `allowed`; returns how many were taken out.
    int CapSocketEnemies(int room, int allowed)
    {
        if (!actorSockets.TryGetValue(room, out var list)) return 0;
        if (TemplateOf(room) is DungeonRoomTemplate template && (template.overrideEnemyCountRestrictions || template.socketEnemiesOnly)) return 0;
        if (room == StageRoom || EventOf(room) == RoomEvent.Ambush) return 0;
        // A guardian's arena (the milestone's own room on the exit) keeps what its author put in it.
        if (Roles[room] == RoomRole.Exit && Milestone != null && Milestone.arena != null) return 0;
        var enemies = list.FindAll(s => s != null && s.type == DungeonSocketType.Enemy);
        if (enemies.Count <= allowed) return 0;
        // Which ones stay: an order from the floor's seed and each socket's place, so the same floor keeps the same
        // ones and no random draw of the floor is used up (every later roll stays as it was).
        int salt = Scramble(unchecked(roomCapSeed * 31 + room));
        enemies.Sort((a, b) => Key(a).CompareTo(Key(b)));
        int Key(DungeonSocket s)
        {
            var c = CellOf(s.transform.position);
            var p = s.transform.position;
            return Scramble(unchecked(salt ^ (c.x * 73856093) ^ (c.y * 19349663) ^ Mathf.RoundToInt(p.x * 7f) * 83492791 ^ Mathf.RoundToInt(p.z * 7f)));
        }
        int removed = 0;
        for (int k = allowed; k < enemies.Count; k++)
        {
            var socket = enemies[k];
            list.Remove(socket);
            if (socket.placedEnemy != null) DestroyImmediate(socket.placedEnemy);
            removed++;
        }
        return removed;
    }
}

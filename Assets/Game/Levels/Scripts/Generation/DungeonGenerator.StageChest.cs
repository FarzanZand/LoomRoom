using System.Collections.Generic;
using UnityEngine;

// A chest before the boss stage (the user, 2026-11-04): on a floor whose exit is a stage with a Prep Shield or Prep Potion
// set (DungeonStage), the last generated room on the way from the entrance to the stage gets a chest by its side nearest
// the stage. It holds a shield and a potion when the player carries no shield, else a potion, and the Dungeon Master tells
// them to take the shield up (ShieldCheckChest). Not on the stage itself: its fight begins at the top of its stairs.
public partial class DungeonGenerator
{
    int stageChestRoom = -1;

    // The last room without a template on the walk from the entrance to the stage (rooms joined by their passages), else -1.
    int StageChestRoom()
    {
        if (StageRoom < 0 || Authored) return -1;
        var stage = TemplateOf(StageRoom)?.GetComponent<DungeonStage>();
        if (stage == null || stage.prepShield == null && stage.prepPotion == null) return -1;
        int rooms = Layout.rooms.Count, width = Layout.RegionIds.GetLength(0), depth = Layout.RegionIds.GetLength(1);
        var links = new List<int>[rooms];
        for (int i = 0; i < rooms; i++) links[i] = new List<int>();
        void Join(int a, int b) { if (a == b || a < 0 || b < 0 || a >= rooms || b >= rooms || links[a].Contains(b)) return; links[a].Add(b); links[b].Add(a); }
        // Each run of passage cells (floor in no room: corridors have region ids of their own) joins every room it touches.
        var seen = new bool[width, depth];
        for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++)
        {
            if (seen[x, z] || !Layout.floor[x, z] || Layout.RoomAt(new Vector2Int(x, z)) >= 0) continue;
            var touched = new List<int>();
            var open = new Stack<Vector2Int>(); open.Push(new Vector2Int(x, z)); seen[x, z] = true;
            while (open.Count > 0)
            {
                var c = open.Pop();
                foreach (var d in DungeonLayout.Neighbours)
                {
                    var n = c + d;
                    if (!Layout.InBounds(n) || !Layout.floor[n.x, n.y]) continue;
                    int region = Layout.RoomAt(n);
                    if (region >= 0) { if (!touched.Contains(region)) touched.Add(region); continue; }
                    if (seen[n.x, n.y]) continue;
                    seen[n.x, n.y] = true; open.Push(n);
                }
            }
            for (int a = 0; a < touched.Count; a++) for (int b = a + 1; b < touched.Count; b++) Join(touched[a], touched[b]);
        }
        // Rooms that open straight into each other.
        for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++)
        {
            int region = Layout.RoomAt(new Vector2Int(x, z));
            if (region < 0) continue;
            foreach (var d in DungeonLayout.Neighbours)
            {
                var n = new Vector2Int(x + d.x, z + d.y);
                if (Layout.RoomAt(n) >= 0) Join(region, Layout.RoomAt(n));
            }
        }
        var from = new int[rooms];
        for (int i = 0; i < rooms; i++) from[i] = -2;
        var queue = new Queue<int>(); queue.Enqueue(0); from[0] = -1;
        while (queue.Count > 0) { int r = queue.Dequeue(); foreach (var n in links[r]) if (from[n] == -2) { from[n] = r; queue.Enqueue(n); } }
        if (from[StageRoom] == -2) return -1;
        for (int r = from[StageRoom]; r > 0; r = from[r]) if (TemplateOf(r) == null && r != MerchantRoom) return r;
        return -1;
    }

    // The chest on the free cell nearest the stage.
    void PlaceStageChest(int index, List<Vector2Int> available, int cursor)
    {
        var stage = TemplateOf(StageRoom)?.GetComponent<DungeonStage>();
        if (stage == null) return;
        var target = Cell(Layout.RoomCenter(StageRoom));
        int best = -1; float bestDistance = float.MaxValue;
        for (int i = cursor; i < available.Count; i++)
        {
            if (Layout.Walkways.Contains(available[i])) continue;
            float distance = (Cell(available[i]) - target).sqrMagnitude;
            if (distance < bestDistance) { bestDistance = distance; best = i; }
        }
        if (best < 0) return;
        var cell = available[best];
        available.RemoveAt(best);
        var chest = MakeContainer(Cell(cell), true, null, FaceCenter(Cell(cell), index));
        if (chest == null) return;
        chest.name = "Chest before the stage";
        var check = chest.gameObject.AddComponent<ShieldCheckChest>();
        check.shield = stage.prepShield;
        check.potion = stage.prepPotion;
    }
}

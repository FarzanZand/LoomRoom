using System.Collections.Generic;
using UnityEngine;

// Treasure that comes with a generated room's shape (the user, 2026-10-30, after a play test): a room with an inner
// walled chamber (Room in room) keeps a chest and a heap of gold in that chamber, and a big hall (Big Hall Cells or more)
// has a chest at its far end, away from its ways in. Rolled on their own seed stream, so the rest of the floor is laid
// as before. Rooms with a template, the entrance, the exit and authored maps are left alone.
public partial class DungeonGenerator
{
    const int BigHallCells = 80;
    const int HoardPiles = 8;

    void ShapeTreasure(int index, List<Vector2Int> available, int cursor, LootSource rewards)
    {
        if (Authored || index == 0 || Roles[index] == RoomRole.Exit || Layout.Shapes == null || index >= Layout.Shapes.Length) return;
        var shape = Layout.Shapes[index];
        if (shape == null) return;
        if (shape.kind == DungeonShapeKind.RoomInRoom) Hoard(index, available, cursor, rewards);
        else if (Layout.RoomCells(index).Count >= BigHallCells) FarChest(index, available, cursor, rewards);
    }

    // The inner chamber: the room's cells three or more cells in from the edge of its bounds (inside the inner wall ring).
    void Hoard(int index, List<Vector2Int> available, int cursor, LootSource rewards)
    {
        var cells = Layout.RoomCells(index);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var c in cells) { minX = Mathf.Min(minX, c.x); minY = Mathf.Min(minY, c.y); maxX = Mathf.Max(maxX, c.x); maxY = Mathf.Max(maxY, c.y); }
        var inner = new List<Vector2Int>();
        foreach (var c in cells)
            if (Mathf.Min(Mathf.Min(c.x - minX, maxX - c.x), Mathf.Min(c.y - minY, maxY - c.y)) >= 3 && !cutCells.Contains(c)) inner.Add(c);
        if (inner.Count == 0) return;
        var centre = new Vector2Int((minX + maxX) / 2, (minY + maxY) / 2);
        if (!inner.Contains(centre)) centre = inner[0];
        // Nothing else is put in the chamber.
        foreach (var c in inner) { int at = available.IndexOf(c); if (at >= cursor) available.RemoveAt(at); }
        // Facing the chamber's one way in (the gap in its wall: the room's only cell two in from the edge of its bounds).
        var facing = Quaternion.identity;
        foreach (var c in cells)
            if (Mathf.Min(Mathf.Min(c.x - minX, maxX - c.x), Mathf.Min(c.y - minY, maxY - c.y)) == 2)
            {
                var to = Cell(c) - Cell(centre); to.y = 0f;
                if (to.sqrMagnitude > .01f) { facing = Quaternion.LookRotation(Mathf.Abs(to.x) >= Mathf.Abs(to.z) ? new Vector3(Mathf.Sign(to.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(to.z))); break; }
            }
        MakeContainer(Cell(centre), true, rewards, facing);
        var rng = new System.Random(Scramble(unchecked(seed0 + 60013 * (index + 1))));
        var spots = inner.FindAll(c => c != centre);
        for (int i = spots.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (spots[i], spots[j]) = (spots[j], spots[i]); }
        int floor = Mathf.Max(1, FloorNumber);
        for (int i = 0; i < Mathf.Min(HoardPiles, spots.Count); i++)
        {
            var heap = new GameObject("Gold hoard").AddComponent<DungeonGoldCache>();
            heap.transform.SetParent(transform, false);
            heap.transform.position = Cell(spots[i]);
            heap.piles = 1; heap.goldPerPile = 5 + 2 * (floor - 1); heap.scatter = .4f;   // about 90 gold in all on floor 4
        }
    }

    // The free cell farthest from the room's ways in, preferring one against a wall.
    void FarChest(int index, List<Vector2Int> available, int cursor, LootSource rewards)
    {
        var doors = new List<Vector2Int>();
        foreach (var c in Layout.RoomCells(index)) foreach (var d in DungeonLayout.Neighbours) if (Layout.IsOpening(index, c, d)) doors.Add(c);
        if (doors.Count == 0) return;
        int best = -1; float bestScore = float.MinValue;
        for (int i = cursor; i < available.Count; i++)
        {
            var c = available[i];
            float near = float.MaxValue;
            foreach (var d in doors) near = Mathf.Min(near, (c - d).sqrMagnitude);
            bool wall = false;
            foreach (var d in DungeonLayout.Neighbours) if (!Layout.InRoom(index, c + d)) wall = true;
            float score = Mathf.Sqrt(near) + (wall ? 1.5f : 0f);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        if (best < 0) return;
        var cell = available[best];
        available.RemoveAt(best);
        MakeContainer(Cell(cell), true, rewards, FaceCenter(Cell(cell), index));
    }
}

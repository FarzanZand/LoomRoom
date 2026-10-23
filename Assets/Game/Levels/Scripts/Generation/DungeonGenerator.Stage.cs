using System.Collections.Generic;
using UnityEngine;

// The boss stage (a DungeonStage on the exit room's template, normally the biome guardian's arena): the exit room is kept
// at the edge of the grid that faces the Dungeon Master, centred on him (DungeonLayout.Grow exitAt), and built as an open
// raised platform: no walls or ceiling, a stair up from each doorway, invisible edges, and everything in the template
// lifted onto it. The boss is not spawned with the floor: BossStageEncounter has the DM set it down when the fight begins.
public partial class DungeonGenerator
{
    // The stage built on this floor (the template instance), null on other floors.
    public DungeonStage Stage { get; private set; }
    public int StageRoom { get; private set; } = -1;
    // The floor of the stage's room (the dungeon floor) and the top of the stage.
    public float StageTop => Stage != null && StageRoom >= 0 ? Cell(Layout.RoomCenter(StageRoom)).y + Stage.height : 0f;

    DungeonStage StageTemplate => !Board && Milestone != null && Milestone.arena != null ? Milestone.arena.GetComponent<DungeonStage>() : null;

    // The Dungeon Master in the room (TableManager.DM); without one the stage goes on the east edge.
    Vector3 DungeonMasterPosition()
    {
        var dm = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.transform : null;
        return dm != null ? dm.position : transform.position + Vector3.right * 1000f;
    }

    // Where the exit room (the stage) goes: edgeGap cells in from the edge of the grid facing the Dungeon Master, centred on him.
    Vector2Int? StageOrigin(DungeonShape shape)
    {
        if (StageTemplate == null) return null;
        var s = shape.Trimmed();
        int width = data.width, depth = data.depth;
        var local = DungeonMasterPosition() - transform.position;
        float cx = local.x / data.cellSize + width * .5f, cz = local.z / data.cellSize + depth * .5f;
        float dx = (cx - width * .5f) / width, dz = (cz - depth * .5f) / depth;
        int gx = Mathf.Clamp(StageTemplate.edgeGap, 0, Mathf.Max(0, width - 2 - s.width)), gz = Mathf.Clamp(StageTemplate.edgeGap, 0, Mathf.Max(0, depth - 2 - s.height));
        if (Mathf.Abs(dx) >= Mathf.Abs(dz))
            return new Vector2Int(dx >= 0 ? width - 1 - s.width - gx : 1 + gx, Mathf.Clamp(Mathf.RoundToInt(cz - s.height * .5f), 1, depth - 1 - s.height));
        return new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(cx - s.width * .5f), 1, width - 1 - s.width), dz >= 0 ? depth - 1 - s.height - gz : 1 + gz);
    }

    // The exit room when its template is a stage (in a grown or an authored layout), else -1.
    int FindStageRoom()
    {
        if (Board || Roles == null) return -1;
        for (int i = 0; i < Roles.Length; i++)
            if (Roles[i] == RoomRole.Exit && TemplateOf(i) is { } t && t.GetComponent<DungeonStage>() != null) return i;
        return -1;
    }

    bool IsStageCell(int x, int z) => StageRoom >= 0 && Layout.RegionIds[x, z] == StageRoom;

    // The platform: a block under every cell of the room (stone sides, the room's floor on top), a stair in from each
    // doorway, and invisible edges along every side that drops off. The template is lifted onto the top.
    void BuildStage(GameObject instance, int index)
    {
        var stage = instance.GetComponent<DungeonStage>();
        if (stage == null) return;
        Stage = stage; StageRoom = index;
        float h = stage.height, size = data.cellSize;
        var side = stage.sideMaterial != null ? stage.sideMaterial : data.trimMaterial;
        var root = new GameObject("Stage").transform; root.SetParent(geometry, false);
        instance.transform.position += Vector3.up * h;
        // The template is laid out with the Dungeon Master's side along its +x (its cover, lights and spots): it is
        // turned, in quarter turns about its middle, to face him wherever the stage stands.
        var toDm = DungeonMasterPosition() - instance.transform.position;
        if (new Vector2(toDm.x, toDm.z).sqrMagnitude > .01f)
        {
            float yaw = Mathf.Round(Mathf.Atan2(-toDm.z, toDm.x) * Mathf.Rad2Deg / 90f) * 90f;
            instance.transform.rotation = Quaternion.Euler(0, yaw, 0) * instance.transform.rotation;
        }

        // Stair runs: from a doorway cell inward, as long as the room allows.
        var stairs = new HashSet<Vector2Int>();
        var runs = new List<(Vector2Int door, Vector2Int inward, int length)>();
        foreach (var c in Layout.RoomCells(index))
            foreach (var d in DungeonLayout.Neighbours)
            {
                var outside = c + d;
                if (!Layout.InBounds(outside) || !Layout.floor[outside.x, outside.y] || Layout.InRoom(index, outside)) continue;
                int length = 0;
                for (int k = 0; k < stage.stairCells; k++) { var s = c - d * k; if (!Layout.InRoom(index, s)) break; stairs.Add(s); length++; }
                runs.Add((c, -d, length));
            }

        // The way on: a spiral shaft down through the stage (DungeonGenerator.ExitShaft). Its cell gets only the shaft's
        // floor; the block and top that stand over it during the fight are built apart from the stage (they are not
        // merged with it) and go when the exit shows (DungeonExit.cover).
        var shaft = PlanStageShaft(index, h, stairs, side);
        foreach (var c in Layout.RoomCells(index))
        {
            if (stairs.Contains(c)) continue;
            var p = Cell(c);
            var parent = root;
            if (c == shaft)
            {
                float floorTop = h - exitShaftDepth;
                ArchitectureBox("Shaft block", p + Vector3.up * (floorTop - .12f) * .5f, new Vector3(size, floorTop - .12f, size), side, root);
                ArchitectureBox("Shaft floor", p + Vector3.up * (floorTop - .06f), new Vector3(size, .12f, size), FloorMaterial(index, c.x, c.y), root);
                stageShaftCover = new GameObject("Stage exit cover"); stageShaftCover.transform.SetParent(transform, false);
                parent = stageShaftCover.transform;
            }
            // The block stops where the top slab starts: their sides overlapping in one plane flickered (z-fighting).
            ArchitectureBox("Stage block", p + Vector3.up * (h - .12f) * .5f, new Vector3(size, h - .12f, size), side, parent);
            ArchitectureBox("Stage top", p + Vector3.up * (h - .06f), new Vector3(size, .12f, size), FloorMaterial(index, c.x, c.y), parent);
        }
        foreach (var (door, inward, length) in runs)
        {
            float run = length * size;
            int steps = Mathf.Max(1, Mathf.CeilToInt(h / stage.stepRise));
            float depthOfStep = run / steps;
            var start = Cell(door) - new Vector3(inward.x, 0, inward.y) * size * .5f;
            var along = new Vector3(inward.x, 0, inward.y);
            for (int i = 0; i < steps; i++)
            {
                float top = h * (i + 1) / steps;
                var centre = start + along * (depthOfStep * (i + .5f)) + Vector3.up * top * .5f;
                var scale = inward.x != 0 ? new Vector3(depthOfStep, top, size) : new Vector3(size, top, depthOfStep);
                ArchitectureBox("Stage step", centre, scale, side, root);
            }
        }

        // Invisible edges: along every side of the room that drops off (not the doorways, where the corridor comes in).
        foreach (var c in Layout.RoomCells(index))
            foreach (var d in DungeonLayout.Neighbours)
            {
                var outside = c + d;
                if (Layout.InRoom(index, outside)) continue;
                if (Layout.InBounds(outside) && Layout.floor[outside.x, outside.y]) continue;
                float bottom = stairs.Contains(c) ? 0f : h;
                var edge = new GameObject("Stage edge");
                edge.transform.SetParent(root, false);
                edge.transform.position = Cell(c) + new Vector3(d.x, 0, d.y) * (size * .5f + .1f) + Vector3.up * (bottom + stage.edgeHeight * .5f);
                var box = edge.AddComponent<BoxCollider>();
                edge.AddComponent<InvisibleWall>();
                box.size = d.x != 0 ? new Vector3(.2f, stage.edgeHeight, size + .2f) : new Vector3(size + .2f, stage.edgeHeight, .2f);
            }
    }

    // Where the exit stands on the stage (shown when the boss falls).
    Vector3 StageExitPoint()
    {
        if (Stage == null) return ExitPoint;
        // The spiral shaft's cell, once planned (PlanStageShaft).
        if (exitShaft && Layout.InRoom(StageRoom, stageShaftCell)) return Cell(stageShaftCell) + Vector3.up * Stage.height;
        if (Stage.exitSpot != null) return Stage.exitSpot.position;
        return Cell(Layout.RoomCenter(StageRoom)) + Vector3.up * Stage.height;
    }

    // Where the Dungeon Master sets the boss down: the template's spot, else between the middle and his side.
    public Vector3 StageBossSpot()
    {
        if (Stage == null) return ExitPoint;
        if (Stage.bossSpot != null) return Stage.bossSpot.position;
        var centre = Cell(Layout.RoomCenter(StageRoom)) + Vector3.up * Stage.height;
        var toDm = DungeonMasterPosition() - centre; toDm.y = 0;
        // Near the edge on his side, within his reach over the table.
        return centre + (toDm.sqrMagnitude > .01f ? toDm.normalized * data.cellSize * 3f : Vector3.zero);
    }

    public Vector3 StageCentre => Stage != null ? Cell(Layout.RoomCenter(StageRoom)) + Vector3.up * Stage.height : ExitPoint;

    // The boss, set down on the stage by the Dungeon Master (BossStageEncounter).
    public Character SpawnStageBoss(Vector3 position, Quaternion rotation) => Milestone != null && Milestone.boss != null ? SpawnBoss(position, rotation) : null;
}

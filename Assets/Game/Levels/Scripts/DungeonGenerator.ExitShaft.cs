using UnityEngine;

// The way down as a real hole: on a walled floor that goes on, the exit cell is cut down into the table (TableCut, like
// a pit) and a spiral stair winds down round a stone newel into the dark, a warm glow far below. A low stone kerb rings
// the mouth, open on the side the room is entered from, with a post and a torch either side of the opening; an iron
// grate lies over the hole while the floor's guardian lives. Walking onto the first steps goes down (DungeonExit).
// Boards, boss stages, pockets, the last floor and exit rooms built from a template keep the stone stairwell.
public partial class DungeonGenerator
{
    // The exit cell's shaft, when this floor has one: its facing (toward the room's way in) and depth.
    bool exitShaft;
    Vector2Int exitShaftFacing;
    float exitShaftDepth;

    // Before the room is furnished and the floor combined: take the exit cell's flagstone away and line the hole.
    void CutExitShaft()
    {
        if (Board || data.pocket || !Descends) return;
        var cell = Layout.Exit;
        if (!Layout.InBounds(cell) || !Layout.floor[cell.x, cell.y]) return;
        int region = Layout.RegionIds[cell.x, cell.y];
        if (region < 0 || region >= Layout.rooms.Count || TemplateOf(region) != null) return;
        // Every neighbour must be floor of the same room, so the kerb stands on floor all round.
        foreach (var d in DungeonLayout.Neighbours) if (!Layout.InRoom(region, cell + d)) return;
        float floorY = Cell(cell).y, size = data.cellSize;
        float depth = Mathf.Min(2f, TableCut.SolidDepth(CellRect(cell), floorY) - .24f - .1f);
        if (depth < 1.2f) return;

        // Open toward the room's way in (the doorway cell nearest the shaft).
        Vector2Int facing = Vector2Int.down; float best = float.MaxValue;
        foreach (var c in Layout.RoomCells(region))
            foreach (var d in DungeonLayout.Neighbours)
            {
                var o = c + d;
                if (!Layout.InBounds(o) || !Layout.floor[o.x, o.y] || Layout.InRoom(region, o)) continue;
                var to = c - cell; float dist = to.sqrMagnitude;
                if (dist >= best || to == Vector2Int.zero) continue;
                best = dist;
                facing = Mathf.Abs(to.x) >= Mathf.Abs(to.y) ? new Vector2Int(System.Math.Sign(to.x), 0) : new Vector2Int(0, System.Math.Sign(to.y));
            }

        for (int i = geometry.childCount - 1; i >= 0; i--)
        {
            var t = geometry.GetChild(i);
            if (t.name == "Flagstone" && CellOf(t.position) == cell) DestroyImmediate(t.gameObject);
        }
        var parent = new GameObject("Exit shaft").transform; parent.SetParent(geometry, false);
        var centre = Cell(cell);
        foreach (var d in DungeonLayout.Neighbours)
        {
            var outward = new Vector3(d.x, 0f, d.y);
            var wallSize = d.x != 0 ? new Vector3(WallThickness, depth, size + WallThickness * 2f) : new Vector3(size + WallThickness * 2f, depth, WallThickness);
            ArchitectureBox("Shaft side", centre + outward * (size * .5f + WallThickness * .5f - .02f) + Vector3.down * (depth * .5f + .001f), wallSize,
                WallSurface(region, 0f, cell.x, cell.y, -d), parent);
        }
        ArchitectureBox("Shaft floor", centre + Vector3.down * (depth + .12f), new Vector3(size, .24f, size), FloorMaterial(region, cell.x, cell.y), parent);
        cutCells.Add(cell);
        tableHoles.Add(new TableCut.Hole { area = CellRect(cell) });
        TableCut.Set(this, tableHoles);
        exitShaft = true; exitShaftFacing = facing; exitShaftDepth = depth;
    }

    void BuildExitShaft(Transform root, DungeonExit exit)
    {
        var cell = CellOf(root.position);
        int region = Layout.RegionIds[cell.x, cell.y];
        var stone = FloorMaterial(region, cell.x, cell.y);
        var walls = WallSurface(region, 0f, cell.x, cell.y, -exitShaftFacing);
        // The caps are the room's floor stone: the style's trim (often timber) read as wooden bars round a stone hole.
        var trim = stone;
        float size = data.cellSize, depth = exitShaftDepth, half = size * .5f;
        root.rotation = Quaternion.LookRotation(new Vector3(-exitShaftFacing.x, 0, -exitShaftFacing.y));
        var block = new MaterialPropertyBlock();
        GameObject Part(string name, Vector3 local, Vector3 scale, Material material, float shade = 1f, float yaw = 0f, Transform parent = null, bool free = false)
        {
            // Built like the walls (ArchitectureBox): texture laid in world metres per architecture tile, so its pixels are
            // the same size as the room's. The root only turns in quarter turns: a box keeps no rotation of its own, its
            // size is turned instead.
            bool across = Mathf.Abs((root.rotation * Vector3.forward).x) > .5f;
            var worldSize = across && !free ? new Vector3(scale.z, scale.y, scale.x) : scale;
            var g = ArchitectureBox(name, root.TransformPoint(local), worldSize, material, parent != null ? parent : root);
            if (yaw != 0f) { g.transform.localScale = scale; g.transform.rotation = root.rotation * Quaternion.Euler(0, yaw, 0); }
            if (shade < 1f)
            {
                var r = g.GetComponent<Renderer>(); r.GetPropertyBlock(block);
                var c = Color.white * shade; c.a = 1f; block.SetColor("_BaseColor", c); block.SetColor("_Color", c); r.SetPropertyBlock(block);
            }
            return g;
        }
        // Local -z is the mouth (toward the way in), +z the back.
        // The kerb: low stone round the back and sides, its top a trim course; open at the mouth between two posts.
        const float kerb = .42f, thick = .26f;
        Part("Kerb back", new Vector3(0, kerb * .5f, half + thick * .5f), new Vector3(size + thick * 2f, kerb, thick), walls);
        Part("Kerb cap back", new Vector3(0, kerb + .05f, half + thick * .5f), new Vector3(size + thick * 2f + .08f, .1f, thick + .08f), trim);
        foreach (float x in new[] { -1f, 1f })
        {
            Part("Kerb side", new Vector3(x * (half + thick * .5f), kerb * .5f, .05f), new Vector3(thick, kerb, size + .1f), walls);
            Part("Kerb cap side", new Vector3(x * (half + thick * .5f), kerb + .05f, .05f), new Vector3(thick + .08f, .1f, size + .18f), trim);
            // Posts either side of the mouth, a torch on each.
            Part("Post", new Vector3(x * (half + thick * .5f), .8f, -half - thick * .5f), new Vector3(thick + .1f, 1.6f, thick + .1f), walls);
            Part("Post cap", new Vector3(x * (half + thick * .5f), 1.65f, -half - thick * .5f), new Vector3(thick + .2f, .1f, thick + .2f), trim);
            var torch = WallLights != null && WallLights.Length > 0 && WallLights[0] != null ? WallLights[0].prefab : null;
            if (torch != null)
            {
                var t = Instantiate(torch, root.TransformPoint(new Vector3(x * (half + thick * .5f), 1.2f, -half - thick - .12f)), root.rotation * Quaternion.Euler(0, 180f, 0), root);
                t.name = "Shaft torch";
            }
        }

        // The spiral starts at the mouth: the first step runs from the newel out to the mouth's edge, its top level with
        // the floor, so the player walks straight onto it; the rest go on round the newel and down, each step wider
        // than the gap to the next and reaching from the newel to the wall, darker the deeper.
        float newelR = .22f;
        Part("Newel", new Vector3(0, -depth * .5f + .1f, 0), new Vector3(newelR * 2f, depth + .2f, newelR * 2f), stone, .8f);
        Part("Newel cap", new Vector3(0, .15f, 0), new Vector3(newelR * 2f + .1f, .1f, newelR * 2f + .1f), stone);
        int steps = 11; float start = 0f, turn = 330f / steps;
        float rise = (depth - .3f) / (steps - 1), reach = half - .03f - newelR, r = newelR + reach * .5f;
        float width = 2f * Mathf.PI * (newelR + reach) * (turn / 360f) + .08f;   // the outer edge overlaps the next step
        for (int i = 0; i < steps; i++)
        {
            float a = start + i * turn, rad = a * Mathf.Deg2Rad;
            float top = -i * rise;
            var at = new Vector3(Mathf.Sin(rad) * r, top - .06f, -Mathf.Cos(rad) * r);
            var s = Part("Spiral step", at, new Vector3(width, .12f, reach), stone, Mathf.Lerp(1f, .4f, i / (steps - 1f)), 0f, null, true);
            s.transform.rotation = root.rotation * Quaternion.LookRotation(new Vector3(Mathf.Sin(rad), 0f, -Mathf.Cos(rad)));
        }
        // Far below: a warm glow, as if a lamp burns on the next floor.
        var glow = new GameObject("Glow below").AddComponent<Light>();
        glow.transform.SetParent(root, false);
        glow.transform.localPosition = new Vector3(0, -depth + .3f, 0);
        glow.type = LightType.Point; glow.color = new Color(1f, .55f, .25f); glow.range = depth + 1.2f; glow.intensity = 5f; glow.shadows = LightShadows.None;
        // The grate over the hole while the guardian lives.
        var gate = new GameObject("Grate"); gate.transform.SetParent(root, false);
        for (int i = -3; i <= 3; i++) Part("Bar", new Vector3(i * (size / 7f), .03f, 0), new Vector3(.06f, .06f, size + .1f), data.metalMaterial, 1f, 0f, gate.transform);
        for (int i = -2; i <= 2; i++) Part("Bar", new Vector3(0, .06f, i * (size / 5f)), new Vector3(size + .1f, .06f, .06f), data.metalMaterial, 1f, 0f, gate.transform);
        var bars = gate.AddComponent<BoxCollider>(); bars.center = new Vector3(0, .1f, 0); bars.size = new Vector3(size, .2f, size);
        exit.grate = gate; gate.SetActive(exit.Sealed);
    }
}

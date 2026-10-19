using UnityEngine;

// The way down as a real hole: on a walled floor that goes on, the exit cell is cut down into the table (TableCut, like
// a pit) and a spiral stair winds down round a stone newel into the dark, a warm glow far below. A low stone kerb rings
// the mouth, open on the side the room is entered from, with a post and a torch either side of the opening; an iron
// grate lies over the hole while the floor's guardian lives. Walking onto the first steps goes down (DungeonExit).
// On a boss stage the shaft goes down through the stage's own block instead (PlanStageShaft), covered by a plain stage
// block until the boss falls and the exit shows. Boards, pockets, the last floor and exit rooms built from a template
// keep the stone stairwell.
public partial class DungeonGenerator
{
    // The exit cell's shaft, when this floor has one: its facing (toward the room's way in) and depth.
    bool exitShaft;
    Vector2Int exitShaftFacing;
    float exitShaftDepth;
    // The boss stage's shaft cell, the block standing over it until the exit shows, and the stage's side stone.
    Vector2Int stageShaftCell = new Vector2Int(-1, -1);
    GameObject stageShaftCover;
    Material stageShaftSide;

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

        var facing = FacingToDoor(region, cell);

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

    // Open toward the room's way in (the doorway cell nearest the shaft).
    Vector2Int FacingToDoor(int region, Vector2Int cell)
    {
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
        return facing;
    }

    // The boss stage's way on (from BuildStage, before the stage is built): the shaft goes down inside the stage's own
    // block, so the table is not cut. Its cell is the template's exit spot's when it has one, else out from the middle
    // toward the Dungeon Master with two cells of stage left between it and his edge; stage all round it (not a stair).
    // The stage blocks round it are the shaft's sides; returns the cell, or null to keep the stone stairwell.
    Vector2Int? PlanStageShaft(int index, float h, System.Collections.Generic.HashSet<Vector2Int> stairs, Material side)
    {
        var cell = CellOf(StageExitPoint());
        if (Stage.exitSpot == null)
        {
            var toDm = DungeonMasterPosition() - Cell(cell);
            var d = Mathf.Abs(toDm.x) >= Mathf.Abs(toDm.z) ? new Vector2Int(System.Math.Sign(toDm.x), 0) : new Vector2Int(0, System.Math.Sign(toDm.z));
            while (d != Vector2Int.zero && Layout.InRoom(index, cell + d * 3)) cell += d;
        }
        if (!Layout.InRoom(index, cell) || stairs.Contains(cell)) return null;
        foreach (var d in DungeonLayout.Neighbours) if (!Layout.InRoom(index, cell + d) || stairs.Contains(cell + d)) return null;
        float depth = Mathf.Min(2f, h - .4f);
        if (depth < 1.2f) return null;
        exitShaft = true; exitShaftFacing = FacingToDoor(index, cell); exitShaftDepth = depth;
        stageShaftCell = cell; stageShaftSide = side;
        return cell;
    }

    void BuildExitShaft(Transform root, DungeonExit exit)
    {
        var cell = CellOf(root.position);
        int region = Layout.RegionIds[cell.x, cell.y];
        var stone = FloorMaterial(region, cell.x, cell.y);
        // On the stage the shaft's sides are the stage blocks' own stone, so the steps' sides match them.
        var walls = cell == stageShaftCell && stageShaftSide != null ? stageShaftSide : WallSurface(region, 0f, cell.x, cell.y, -exitShaftFacing);
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

        // The spiral: solid stone steps round a newel. Each is a wedge from the newel out to the shaft's walls and down
        // to the shaft's floor, and meets the next exactly, so nothing shows between them. The first is centred on the
        // mouth with its top level with the floor, so the player walks straight onto it; each next one is a step lower,
        // going round to the right, a little darker the deeper.
        float newelR = .22f;
        // The newel throws no shadow: from the player's own light it laid a dark wedge across the first step that read
        // as a crack in it.
        foreach (var n in new[] {
            // The newel stops inside its cap: with both tops at one height they flickered (z-fighting).
            Part("Newel", new Vector3(0, (-depth + .14f) * .5f, 0), new Vector3(newelR * 2f, depth + .14f, newelR * 2f), stone, .8f),
            Part("Newel cap", new Vector3(0, .15f, 0), new Vector3(newelR * 2f + .1f, .1f, newelR * 2f + .1f), stone) })
            n.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // Steps a quarter metre high, so the stair gets down in two thirds of a turn: the last step is one step above
        // the shaft's floor, which is the landing at the foot, and no step ever runs under anything (a full turn in a
        // shaft this shallow left a head's height of 1.2 m under the floor at the mouth).
        int steps = Mathf.Max(4, Mathf.RoundToInt(depth / .25f)); float start = -15f, turn = 30f;
        float rise = depth / steps;
        for (int i = 0; i < steps; i++)
        {
            // Each step's flagstones are laid on their own (shifted), so seen from above a step's edge breaks the pattern
            // instead of the stones running on across it as if it were flat floor.
            var tread = Tread(root, "Spiral step", start + i * turn, start + (i + 1) * turn, newelR - .02f, half + .01f, -i * rise, -depth - .01f, stone, walls,
                i == 0 ? Vector2.zero : new Vector2(i * .37f, i * .23f));
            // A step darker each time, so each step reads as its own band. Steps cast no shadow: the key light's
            // shadow of one step's edge fell slantwise across the next and drew diagonal lines that weren't edges.
            var rr = tread.GetComponent<Renderer>(); rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            float shade = Mathf.Pow(.82f, i);
            if (shade < 1f)
            {
                rr.GetPropertyBlock(block);
                var c = Color.white * shade; c.a = 1f; block.SetColor("_BaseColor", c); block.SetColor("_Color", c); rr.SetPropertyBlock(block);
            }
        }
        // The mouth's left of the first step would drop straight to the landing: solid stone, level with the floor and
        // down to the shaft's floor, closes it, from the first step round to the left side. Its face is the landing's
        // back wall, so nothing hangs over the foot of the stair.
        Tread(root, "Mouth block", start - 75f, start, newelR - .02f, half + .01f, 0f, -depth - .01f, stone, walls);
        // The landing at the foot, between the last step and the block: the shaft's floor in the same stone, as dark as
        // the step above it (a bright floor down there flattened the whole stair seen from above).
        float landingFrom = start + steps * turn, landingTo = start - 75f + 360f;
        var landing = Tread(root, "Landing", landingFrom, landingTo, newelR - .02f, half + .01f, -depth + .01f, -depth - .02f, stone, walls, new Vector2(.61f, .19f));
        {
            var rr = landing.GetComponent<Renderer>(); rr.GetPropertyBlock(block);
            var c = Color.white * Mathf.Pow(.82f, steps); c.a = 1f; block.SetColor("_BaseColor", c); block.SetColor("_Color", c); rr.SetPropertyBlock(block);
        }
        // Far below: a warm glow low over the landing, as if a lamp burns round the turn. It rakes up the stair, so each
        // step's face catches it and its top doesn't, and the steps read one by one.
        var glow = new GameObject("Glow below").AddComponent<Light>();
        glow.transform.SetParent(root, false);
        float ga = (landingFrom + landingTo) * .5f * Mathf.Deg2Rad;
        glow.transform.localPosition = new Vector3(Mathf.Sin(ga) * .9f, -depth + .45f, -Mathf.Cos(ga) * .9f);
        glow.type = LightType.Point; glow.color = new Color(1f, .55f, .25f); glow.range = depth + 1.5f; glow.intensity = 3f; glow.shadows = LightShadows.None;
        // The grate over the hole while the guardian lives.
        var gate = new GameObject("Grate"); gate.transform.SetParent(root, false);
        for (int i = -3; i <= 3; i++) Part("Bar", new Vector3(i * (size / 7f), .03f, 0), new Vector3(.06f, .06f, size + .1f), data.metalMaterial, 1f, 0f, gate.transform);
        for (int i = -2; i <= 2; i++) Part("Bar", new Vector3(0, .06f, i * (size / 5f)), new Vector3(size + .1f, .06f, .06f), data.metalMaterial, 1f, 0f, gate.transform);
        var bars = gate.AddComponent<BoxCollider>(); bars.center = new Vector3(0, .1f, 0); bars.size = new Vector3(size, .2f, size);
        exit.grate = gate; gate.SetActive(exit.Sealed);
    }

    // A solid wedge-shaped step from angle a0 to a1 (degrees round the root's up axis; 0 points at the mouth, local -z,
    // and the angle grows toward +x), from the newel's radius out to the square shaft's walls (edge = half-width), from
    // top down to bottom. Every face is two-sided, so no view finds a hole. Its texture is laid in world metres per
    // architecture tile, like the walls and floor (ArchitectureBox), so its texels match them.
    GameObject Tread(Transform root, string name, float a0, float a1, float inner, float edge, float top, float bottom, Material material, Material sides, Vector2 shift = default)
    {
        var angles = new System.Collections.Generic.List<float> { a0, a1 };
        for (float a = Mathf.Ceil(a0 / 5f) * 5f; a < a1; a += 5f) if (a > a0) angles.Add(a);
        for (float corner = -315f; corner <= 405f; corner += 90f) if (corner > a0 && corner < a1) angles.Add(corner);
        angles.Sort();
        Vector3 Dir(float a) { float r = a * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(r), 0f, -Mathf.Cos(r)); }
        Vector3 Outer(float a) { var d = Dir(a); return d * (edge / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z))); }
        float tile = Mathf.Max(.01f, data.architectureTileSize);
        Vector3 World(Vector3 p) => root.TransformPoint(p) - transform.position;
        var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>();
        var topTris = new System.Collections.Generic.List<int>(); var sideTris = new System.Collections.Generic.List<int>(); var t = topTris;
        // One-sided, wound to face outward (Unity's front faces are clockwise seen from the front).
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, System.Func<Vector3, Vector2> map, Vector3 outward)
        {
            int n = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
            uv.Add(map(p0)); uv.Add(map(p1)); uv.Add(map(p2)); uv.Add(map(p3));
            bool clockwise = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) > 0f;
            if (clockwise) t.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 }); else t.AddRange(new[] { n, n + 2, n + 1, n, n + 3, n + 2 });
        }
        Vector2 Flat(Vector3 p) { var w = World(p); return new Vector2(w.x, w.z) / tile + shift; }
        Vector3 Up(Vector3 p) => p + Vector3.up * top;
        Vector3 Down(Vector3 p) => p + Vector3.up * bottom;
        for (int k = 0; k < angles.Count - 1; k++)
        {
            Vector3 i0 = Dir(angles[k]) * inner, i1 = Dir(angles[k + 1]) * inner, o0 = Outer(angles[k]), o1 = Outer(angles[k + 1]);
            t = topTris; Quad(Up(i0), Up(i1), Up(o1), Up(o0), Flat, Vector3.up); t = sideTris;
            var along = World(o1) - World(o0);
            Vector2 Side(Vector3 p) { var w = World(p); return new Vector2(Mathf.Abs(along.x) > Mathf.Abs(along.z) ? w.x : w.z, w.y) / tile; }
            Quad(Up(o0), Up(o1), Down(o1), Down(o0), Side, (o0 + o1) * .5f);
        }
        t = sideTris;
        foreach (float a in new[] { a0, a1 })
        {
            Vector3 i = Dir(a) * inner, o = Outer(a);
            var dir = World(o) - World(i);
            Vector2 Riser(Vector3 p) { var w = World(p); return new Vector2(Mathf.Abs(dir.x) > Mathf.Abs(dir.z) ? w.x : w.z, w.y) / tile; }
            // The end faces point away from the wedge, round the newel.
            var tangent = Dir(a + 90f) * (a == a0 ? -1f : 1f);
            Quad(Up(i), Up(o), Down(o), Down(i), Riser, tangent);
        }
        // The inside face, against the newel.
        for (int k = 0; k < angles.Count - 1; k++)
        {
            Vector3 i0 = Dir(angles[k]) * inner, i1 = Dir(angles[k + 1]) * inner;
            var along = World(i1) - World(i0);
            Vector2 Inside(Vector3 p) { var w = World(p); return new Vector2(Mathf.Abs(along.x) > Mathf.Abs(along.z) ? w.x : w.z, w.y) / tile; }
            Quad(Up(i0), Up(i1), Down(i1), Down(i0), Inside, -(i0 + i1));
        }
        var mesh = new Mesh { name = "Spiral step" };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.subMeshCount = 2; mesh.SetTriangles(topTris, 0); mesh.SetTriangles(sideTris, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        var go = new GameObject(name); go.transform.SetParent(root, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { material, sides };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }
}

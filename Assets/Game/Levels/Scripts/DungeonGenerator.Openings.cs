using UnityEngine;

// Skylights, windows and the wall pieces around them: how each wall piece meets its neighbours at
// corners and doorways, and the tiled UVs every architecture box gets.
public partial class DungeonGenerator
{
    // An open ceiling cell keeps a frame of ceiling only along its sides that border closed ceiling,
    // so neighbouring open cells join into one skylight. A trim lip runs along the frame's inner edge.
    void Skylight(Vector2Int cell, Vector3 center, Vector3 size, float frame, Material ceilingMaterial, Material trim, System.Func<Vector2Int, bool> open)
    {
        float halfX = size.x * .5f, halfZ = size.z * .5f;
        float fx = Mathf.Min(frame, halfX), fz = Mathf.Min(frame, halfZ);
        bool l = !open(cell + Vector2Int.left), r = !open(cell + Vector2Int.right);
        bool b = !open(cell + Vector2Int.down), f = !open(cell + Vector2Int.up);
        const float lip = .1f, lipHeight = .14f;
        float lipY = center.y - size.y * .5f - lipHeight * .5f + .02f;
        void Strip(Vector3 at, Vector3 scale) => ArchitectureBox("Skylight frame", at, scale, ceilingMaterial, ceiling);
        void Lip(Vector3 at, Vector3 scale)
        {
            var lipBox = Box("Skylight trim", at, scale, trim, ceiling);
            Destroy(lipBox.GetComponent<Collider>());
        }
        if (l) { Strip(center + new Vector3(-halfX + fx * .5f, 0, 0), new Vector3(fx, size.y, size.z)); Lip(new Vector3(center.x - halfX + fx + lip * .5f, lipY, center.z), new Vector3(lip, lipHeight, size.z)); }
        if (r) { Strip(center + new Vector3(halfX - fx * .5f, 0, 0), new Vector3(fx, size.y, size.z)); Lip(new Vector3(center.x + halfX - fx - lip * .5f, lipY, center.z), new Vector3(lip, lipHeight, size.z)); }
        if (b) { Strip(center + new Vector3(0, 0, -halfZ + fz * .5f), new Vector3(size.x, size.y, fz)); Lip(new Vector3(center.x, lipY, center.z - halfZ + fz + lip * .5f), new Vector3(size.x, lipHeight, lip)); }
        if (f) { Strip(center + new Vector3(0, 0, halfZ - fz * .5f), new Vector3(size.x, size.y, fz)); Lip(new Vector3(center.x, lipY, center.z + halfZ - fz - lip * .5f), new Vector3(size.x, lipHeight, lip)); }
        // Inner corners: both sides open but the diagonal cell closed; fill the frame's corner square.
        void Corner(Vector2Int a, Vector2Int c, float sx, float sz)
        {
            if (open(cell + a) && open(cell + c) && !open(cell + a + c))
                Strip(center + new Vector3(sx * (halfX - fx * .5f), 0, sz * (halfZ - fz * .5f)), new Vector3(fx, size.y, fz));
        }
        Corner(Vector2Int.left, Vector2Int.down, -1, -1); Corner(Vector2Int.right, Vector2Int.down, 1, -1);
        Corner(Vector2Int.left, Vector2Int.up, -1, 1); Corner(Vector2Int.right, Vector2Int.up, 1, 1);
    }

    // A window in an outer wall: sill and lintel along the whole cell, jambs where the run of windows
    // ends, and a ledge on the sill. Visual only; the hidden full wall keeps the collision.
    void Window(Vector2Int cell, Vector2Int dir, float height, Material lower, Material upper, Material trim, System.Func<Vector2Int, Vector2Int, bool> windowAt)
    {
        float sill = Mathf.Min(data.windowSill, height * .4f), lintel = Mathf.Min(data.windowLintel, height * .4f);
        GameObject Piece(string name, Vector3 at, Vector3 scale, Material material)
        {
            var go = ArchitectureBox(name, at, scale, material, geometry);
            Destroy(go.GetComponent<Collider>());
            return go;
        }
        if (sill > .01f) { var (c, s) = WallPiece(cell, dir, 0, 0, sill, sill * .5f); Piece("Window sill wall", c, s, lower); }
        { var (c, s) = WallPiece(cell, dir, height - lintel, 0, lintel, height - lintel * .5f); Piece("Window lintel", c, s, upper); }
        var (ledgeCenter, ledgeScale) = WallPiece(cell, dir, sill, TrimDepth * 3, .1f, sill + .05f);
        var ledge = Box("Window ledge", ledgeCenter, ledgeScale, trim, geometry);
        Destroy(ledge.GetComponent<Collider>());
        // Jambs where the neighbouring cell along the wall has no window facing the same way.
        var end = dir.x != 0 ? Vector2Int.up : Vector2Int.right;
        var along = new Vector3(end.x, 0, end.y);
        float openHeight = height - sill - lintel;
        if (openHeight <= .01f) return;
        var (mid, scale) = WallPiece(cell, dir, sill, 0, openHeight, sill + openHeight * .5f);
        float length = dir.x != 0 ? scale.z : scale.x;
        float jamb = Mathf.Min(data.windowJamb, length * .45f);
        foreach (var side in new[] { -1, 1 })
        {
            if (windowAt(cell + end * side, dir)) continue;
            var at = mid + along * side * (length * .5f - jamb * .5f);
            var jambScale = dir.x != 0 ? new Vector3(scale.x, openHeight, jamb) : new Vector3(jamb, openHeight, scale.z);
            Piece("Window jamb", at, jambScale, lower);
        }
    }

    // ── Wall corners ─────────────────────────────────────────────────
    // Wall pieces sit flush with the cell edge on the room side and extend into the rock behind
    // it; trims also stand TrimDepth proud of the wall. Each end of a piece is lengthened or
    // shortened so neighbouring pieces meet exactly, with no overlap and no gap:
    //   inside corner:  the north/south-facing piece runs through the corner, the east/west one
    //                   stops at its face;
    //   outside corner: the north/south-facing piece reaches the corner, the east/west one starts
    //                   behind it.
    // Diagonally touching rooms retain these box colliders, but their visible meshes are
    // mitered by MiterDiagonalEnds so neither room exposes the other room's wall end.
    public const float WallThickness = .22f;
    const float TrimDepth = .04f;
    const float CeilingWallOverlap = .02f;

    // Does the cell carry a wall piece facing d at height y?
    bool WallAt(Vector2Int cell, Vector2Int d, float y)
    {
        if (!Open(cell) || y >= HeightAt(cell.x,cell.y) - .01f) return false;
        var n = cell + d;
        return !Open(n) || HeightAt(n.x,n.y) <= y + .01f;
    }

    float EndAdjust(Vector2Int cell, Vector2Int d, Vector2Int end, float y, float proud)
    {
        bool zFacing = d.y != 0;
        bool side = WallAt(cell, end, y);
        // Checkerboard: another room touches this one only at the corner; its wall shares this
        // quarter of rock, so only the north/south-facing piece reaches the corner.
        if (side && WallAt(cell + end + d, -end, y)) return zFacing ? 0 : -WallThickness;
        if (side) return zFacing ? WallThickness : -proud;
        if (!WallAt(cell + end, d, y) && WallAt(cell + end + d, -end, y))
        {
            // An opening's corner (a doorway between a room and a corridor): the room's wall keeps
            // the corner so the corridor wall's end never shows as a strip in the room's wall.
            bool room = IsRoomCell(cell), otherRoom = IsRoomCell(cell + end + d);
            if (room && !otherRoom) return zFacing ? proud : 0;
            if (!room && otherRoom) return -WallThickness;
            return zFacing ? proud : -WallThickness;
        }
        return 0;
    }

    bool IsRoomCell(Vector2Int c) => Layout.InBounds(c) && Layout.floor[c.x, c.y] && Layout.RegionIds[c.x, c.y] < Layout.rooms.Count;

    void MiterDiagonalEnds(GameObject piece, Vector2Int cell, Vector2Int direction, float probeY, float proud, bool tiled)
    {
        var along = direction.x != 0 ? Vector2Int.up : Vector2Int.right;
        float y = probeY + .001f;
        bool Diagonal(Vector2Int end) => WallAt(cell, end, y) && WallAt(cell + end + direction, -end, y);
        bool back = Diagonal(-along), front = Diagonal(along);
        if (!back && !front) return;

        var filter = piece.GetComponent<MeshFilter>();
        // Architecture meshes are shared by size and UV phase; never edit them in place.
        var mesh = Instantiate(filter.sharedMesh);
        mesh.name = "Mitered diagonal wall corner";
        cornerMeshes.Add(mesh);
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var uv = mesh.uv;
        var scale = piece.transform.localScale;
        var outward = new Vector3(direction.x, 0, direction.y);
        var tangent = new Vector3(along.x, 0, along.y);
        for (int i = 0; i < vertices.Length; i++)
        {
            bool positive = Vector3.Dot(vertices[i], tangent) > 0;
            if (positive ? !front : !back) continue;
            var end = positive ? along : -along;
            float oldEnd = EndAdjust(cell, direction, end, y, proud);
            // Inner edge reaches the room corner (including projecting trim); outer
            // edge retreats into the rock. Opposing pieces meet on a diagonal plane.
            float depth = (Vector3.Dot(vertices[i], outward) + .5f) * (WallThickness + proud);
            float newEnd = proud - depth;
            var shift = new Vector3(end.x, 0, end.y) * (newEnd - oldEnd);
            vertices[i] += new Vector3(shift.x / scale.x, 0, shift.z / scale.z);
            if (tiled)
            {
                var normal = normals[i];
                uv[i] += (Mathf.Abs(normal.y) > .5f ? new Vector2(shift.x, shift.z)
                    : Mathf.Abs(normal.x) > .5f ? new Vector2(shift.z, 0)
                    : new Vector2(shift.x, 0)) / Mathf.Max(.01f, data.architectureTileSize);
            }
        }
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
    }

    (Vector3 center, Vector3 scale) WallPiece(Vector2Int cell, Vector2Int d, float probeY, float proud, float height, float centerY)
    {
        var end = d.x != 0 ? Vector2Int.up : Vector2Int.right;
        var along = new Vector3(end.x, 0, end.y);
        var outward = new Vector3(d.x, 0, d.y);
        float y = probeY + .001f;
        float back = EndAdjust(cell, d, -end, y, proud), front = EndAdjust(cell, d, end, y, proud);
        float length = data.cellSize + back + front, thickness = WallThickness + proud;
        var center = Cell(cell) + outward * (data.cellSize * .5f + (WallThickness - proud) * .5f)
            + along * ((front - back) * .5f) + Vector3.up * centerY;
        return (center, d.x != 0 ? new Vector3(thickness, height, length) : new Vector3(length, height, thickness));
    }
    GameObject ArchitectureBox(string name, Vector3 pos, Vector3 size, Material material, Transform parent)
    {
        var go = Box(name, pos, size, material, parent);
        float tile = Mathf.Max(.01f, data.architectureTileSize);
        Vector3 relative = pos - transform.position;
        // Repeat phases are shared across adjacent grid cells, even when grid spacing differs from tile size.
        Vector3 phase = new Vector3(Mathf.Repeat(relative.x, tile), Mathf.Repeat(relative.y, tile), Mathf.Repeat(relative.z, tile));
        phase = new Vector3(Mathf.Round(phase.x*10000)/10000, Mathf.Round(phase.y*10000)/10000, Mathf.Round(phase.z*10000)/10000);
        var key = (size, phase);
        if (!architectureMeshes.TryGetValue(key, out var mesh))
        {
            mesh = Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = "Square architectural tile UVs";
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                var point = phase + Vector3.Scale(vertices[i], size);
                var normal = normals[i];
                uv[i] = Mathf.Abs(normal.y) > .5f ? new Vector2(point.x, point.z) / tile
                    : Mathf.Abs(normal.x) > .5f ? new Vector2(point.z, point.y) / tile
                    : new Vector2(point.x, point.y) / tile;
            }
            mesh.uv = uv;
            architectureMeshes.Add(key, mesh);
        }
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        return go;
    }
}

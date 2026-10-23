using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Holes in the game table under a dungeon's pits and sunken floors, so a walled floor lies flat on the table and its
// holes go down into the wood. Each piece (the tabletop, the apron under it) gets a copy of its mesh with its upward
// faces cut where a hole is open; the tabletop's box collider is swapped for a mesh collider cut through top and
// bottom, so whatever falls in drops down to the pit's own floor. A dungeon's holes come from DungeonGenerator
// (TableHoles); the table reveal opens each one as its room is built. With no hole open the original meshes are back.
public class TableCut : MonoBehaviour
{
    // A hole in the table: an area on the table in world X/Z.
    public class Hole
    {
        public Rect area;
        bool open = true;
        public bool Open { get => open; set { if (open == value) return; open = value; dirty = true; } }
    }

    [Tooltip("The table's solid parts from the top down: the tabletop first, then what lies right under it (the apron). Holes are cut into each one's upward faces; a hole goes as deep as the parts under it reach.")]
    public MeshFilter[] pieces;
    [Tooltip("The tabletop's box collider: swapped for a cut mesh collider while holes are open.")]
    public BoxCollider tableCollider;

    static TableCut instance;
    static bool dirty;
    readonly Dictionary<object, List<Hole>> owners = new();
    Mesh[] originals;
    Mesh[] cutMeshes;
    Bounds[] pieceBounds;
    Bounds colliderBounds;
    MeshCollider cutCollider;
    Mesh colliderMesh;

    public static TableCut Instance
    {
        get
        {
            if (instance == null && TableManager.HasInstance) instance = TableManager.Instance.GetComponent<TableCut>();
            return instance;
        }
    }

    // The table's full box, whatever is cut out of it (the table loader sizes dungeons by it).
    public Bounds Bounds => colliderBounds;

    void Awake()
    {
        instance = this;
        if (tableCollider == null) tableCollider = GetComponent<BoxCollider>();
        if (tableCollider != null) colliderBounds = tableCollider.bounds;
        originals = new Mesh[pieces.Length];
        cutMeshes = new Mesh[pieces.Length];
        pieceBounds = new Bounds[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i] == null) continue;
            originals[i] = pieces[i].sharedMesh;
            pieceBounds[i] = pieces[i].GetComponent<Renderer>().bounds;
        }
    }

    public static void Set(object owner, List<Hole> holes)
    {
        var cut = Instance;
        if (cut == null || !Application.isPlaying) return;
        if (holes == null || holes.Count == 0) { if (cut.owners.Remove(owner)) dirty = true; return; }
        cut.owners[owner] = holes; dirty = true;
    }

    public static void Remove(object owner)
    {
        if (instance != null && instance.owners.Remove(owner)) dirty = true;
    }

    // How far down from fromY the table is solid under the whole area: the tabletop, then each piece right under it
    // that covers the area. Infinite when there is no table (nothing would show under a hole).
    public static float SolidDepth(Rect area, float fromY)
    {
        var cut = Instance;
        if (cut == null) return float.PositiveInfinity;
        if (cut.pieceBounds == null) cut.Awake();
        float bottom = float.NaN;
        for (int i = 0; i < cut.pieceBounds.Length; i++)
        {
            var b = cut.pieceBounds[i];
            if (cut.pieces[i] == null || b.size == Vector3.zero) continue;
            bool covers = area.xMin >= b.min.x - .01f && area.xMax <= b.max.x + .01f && area.yMin >= b.min.z - .01f && area.yMax <= b.max.z + .01f;
            // A floor that isn't lying on the table (built elsewhere, a test floor) has nothing under it to show.
            if (float.IsNaN(bottom) && (!covers || Mathf.Abs(fromY - b.max.y) > 1f)) return float.PositiveInfinity;
            if (!covers) break;
            if (!float.IsNaN(bottom) && b.max.y < bottom - .05f) break;   // a gap: what is under it doesn't count
            bottom = float.IsNaN(bottom) ? b.min.y : Mathf.Min(bottom, b.min.y);
        }
        return float.IsNaN(bottom) ? float.PositiveInfinity : fromY - bottom;
    }

    void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        Rebuild();
    }

    void Rebuild()
    {
        var open = new List<Rect>();
        foreach (var list in owners.Values) foreach (var hole in list) if (hole != null && hole.Open) open.Add(hole.area);
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i] == null || originals[i] == null) continue;
            if (open.Count == 0) { pieces[i].sharedMesh = originals[i]; continue; }
            var local = LocalRects(pieces[i].transform, open);
            if (cutMeshes[i] == null) cutMeshes[i] = new Mesh { name = originals[i].name + " (cut)", indexFormat = IndexFormat.UInt32 };
            Cut(originals[i], local, cutMeshes[i], false);
            pieces[i].sharedMesh = cutMeshes[i];
        }
        if (tableCollider == null) return;
        if (open.Count == 0)
        {
            tableCollider.enabled = true;
            if (cutCollider != null) cutCollider.enabled = false;
            return;
        }
        var source = pieces.Length > 0 && pieces[0] != null && pieces[0].transform == tableCollider.transform ? originals[0] : null;
        if (source == null) return;
        if (cutCollider == null)
        {
            var go = new GameObject("Table cut collider");
            go.layer = tableCollider.gameObject.layer;
            go.transform.SetParent(tableCollider.transform, false);
            cutCollider = go.AddComponent<MeshCollider>();
            colliderMesh = new Mesh { name = "Table cut collider", indexFormat = IndexFormat.UInt32 };
        }
        Cut(source, LocalRects(tableCollider.transform, open), colliderMesh, true);
        cutCollider.sharedMesh = null;
        cutCollider.sharedMesh = colliderMesh;
        cutCollider.sharedMaterial = tableCollider.sharedMaterial;
        cutCollider.enabled = true;
        tableCollider.enabled = false;
        Physics.SyncTransforms();
    }

    static List<Rect> LocalRects(Transform t, List<Rect> world)
    {
        var result = new List<Rect>(world.Count);
        foreach (var r in world)
        {
            var a = t.InverseTransformPoint(new Vector3(r.xMin, t.position.y, r.yMin));
            var b = t.InverseTransformPoint(new Vector3(r.xMax, t.position.y, r.yMax));
            result.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.z, b.z), Mathf.Max(a.x, b.x), Mathf.Max(a.z, b.z)));
        }
        return result;
    }

    // Copies source into target with its flat upward faces (and for a collider its downward ones too) cut by the holes.
    // The faces are rebuilt as strips around the holes, their UVs carried on by the face's own mapping.
    static void Cut(Mesh source, List<Rect> holes, Mesh target, bool bothSides)
    {
        var positions = source.vertices; var normals = source.normals; var uvs = source.uv;
        bool hasNormals = normals.Length == positions.Length, hasUv = uvs.Length == positions.Length;
        var outPos = new List<Vector3>(); var outNormal = new List<Vector3>(); var outUv = new List<Vector2>();
        var subTriangles = new List<int>[source.subMeshCount];
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var tris = source.GetTriangles(s);
            var list = subTriangles[s] = new List<int>();
            // Flat faces facing up (+1) or down (-1): their vertices, to find the face's extent and UV mapping.
            var faces = new Dictionary<(int, float), List<int>>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                var a = positions[tris[i]]; var b = positions[tris[i + 1]]; var c = positions[tris[i + 2]];
                var n = Vector3.Cross(b - a, c - a).normalized;
                int facing = n.y > .999f ? 1 : n.y < -.999f && bothSides ? -1 : 0;
                if (facing != 0)
                {
                    var key = (facing, Mathf.Round(a.y * 1000f) / 1000f);
                    if (!faces.TryGetValue(key, out var verts)) faces[key] = verts = new List<int>();
                    verts.Add(tris[i]); verts.Add(tris[i + 1]); verts.Add(tris[i + 2]);
                    continue;
                }
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[i + k];
                    list.Add(outPos.Count);
                    outPos.Add(positions[v]);
                    outNormal.Add(hasNormals ? normals[v] : n);
                    outUv.Add(hasUv ? uvs[v] : Vector2.zero);
                }
            }
            foreach (var face in faces)
            {
                var verts = face.Value;
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                foreach (int v in verts) { var p = positions[v]; x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
                var map = UvMapping(positions, uvs, verts, hasUv);
                CutFace(face.Key.Item1, face.Key.Item2, x0, x1, z0, z1, holes, map, list, outPos, outNormal, outUv);
            }
        }
        target.Clear();
        target.SetVertices(outPos);
        target.SetNormals(outNormal);
        target.SetUVs(0, outUv);
        target.subMeshCount = subTriangles.Length;
        for (int s = 0; s < subTriangles.Length; s++) target.SetTriangles(subTriangles[s], s);
        if (hasUv) target.RecalculateTangents();
        target.RecalculateBounds();
    }

    // uv = origin + x * dx + z * dz, from three vertices of the face that span it.
    static (Vector2 origin, Vector2 dx, Vector2 dz) UvMapping(Vector3[] positions, Vector2[] uvs, List<int> verts, bool hasUv)
    {
        if (!hasUv) return (Vector2.zero, Vector2.zero, Vector2.zero);
        int a = verts[0];
        for (int i = 1; i < verts.Count; i++)
            for (int j = i + 1; j < verts.Count; j++)
            {
                var pa = positions[a]; var e1 = positions[verts[i]] - pa; var e2 = positions[verts[j]] - pa;
                float det = e1.x * e2.z - e1.z * e2.x;
                if (Mathf.Abs(det) < 1e-4f) continue;
                var u1 = uvs[verts[i]] - uvs[a]; var u2 = uvs[verts[j]] - uvs[a];
                // Solve [e1.x e1.z; e2.x e2.z] * [dx; dz] = [u1; u2].
                var dx = (u1 * e2.z - u2 * e1.z) / det;
                var dz = (u2 * e1.x - u1 * e2.x) / det;
                return (uvs[a] - dx * pa.x - dz * pa.z, dx, dz);
            }
        return (uvs[a], Vector2.zero, Vector2.zero);
    }

    static void CutFace(int facing, float y, float x0, float x1, float z0, float z1, List<Rect> holes, (Vector2 origin, Vector2 dx, Vector2 dz) map,
        List<int> tris, List<Vector3> outPos, List<Vector3> outNormal, List<Vector2> outUv)
    {
        var xs = new List<float> { x0, x1 }; var zs = new List<float> { z0, z1 };
        var inside = new List<Rect>();
        foreach (var h in holes)
        {
            if (h.xMax <= x0 || h.xMin >= x1 || h.yMax <= z0 || h.yMin >= z1) continue;
            var c = Rect.MinMaxRect(Mathf.Max(h.xMin, x0), Mathf.Max(h.yMin, z0), Mathf.Min(h.xMax, x1), Mathf.Min(h.yMax, z1));
            inside.Add(c); xs.Add(c.xMin); xs.Add(c.xMax); zs.Add(c.yMin); zs.Add(c.yMax);
        }
        xs.Sort(); zs.Sort();
        Dedupe(xs); Dedupe(zs);
        var normal = Vector3.up * facing;
        for (int j = 0; j < zs.Count - 1; j++)
        {
            float za = zs[j], zb = zs[j + 1];
            int run = -1;
            for (int i = 0; i <= xs.Count - 1; i++)
            {
                bool solid = i < xs.Count - 1 && !InHole(inside, (xs[i] + xs[i + 1]) * .5f, (za + zb) * .5f);
                if (solid && run < 0) run = i;
                if (!solid && run >= 0) { Quad(xs[run], xs[i], za, zb, y, normal, map, tris, outPos, outNormal, outUv); run = -1; }
            }
        }
    }

    static bool InHole(List<Rect> holes, float x, float z)
    {
        foreach (var h in holes) if (x > h.xMin && x < h.xMax && z > h.yMin && z < h.yMax) return true;
        return false;
    }

    static void Dedupe(List<float> values)
    {
        for (int i = values.Count - 1; i > 0; i--) if (values[i] - values[i - 1] < 1e-4f) values.RemoveAt(i);
    }

    static void Quad(float xa, float xb, float za, float zb, float y, Vector3 normal, (Vector2 origin, Vector2 dx, Vector2 dz) map,
        List<int> tris, List<Vector3> outPos, List<Vector3> outNormal, List<Vector2> outUv)
    {
        int start = outPos.Count;
        var corners = new[] { new Vector3(xa, y, za), new Vector3(xa, y, zb), new Vector3(xb, y, zb), new Vector3(xb, y, za) };
        foreach (var p in corners) { outPos.Add(p); outNormal.Add(normal); outUv.Add(map.origin + map.dx * p.x + map.dz * p.z); }
        // Clockwise seen from the side the face looks at.
        if (normal.y > 0) { tris.Add(start); tris.Add(start + 1); tris.Add(start + 2); tris.Add(start); tris.Add(start + 2); tris.Add(start + 3); }
        else { tris.Add(start); tris.Add(start + 2); tris.Add(start + 1); tris.Add(start); tris.Add(start + 3); tris.Add(start + 2); }
    }

    void OnDestroy()
    {
        for (int i = 0; pieces != null && originals != null && i < pieces.Length; i++)
            if (pieces[i] != null && originals[i] != null) pieces[i].sharedMesh = originals[i];
        if (cutMeshes != null) foreach (var m in cutMeshes) if (m != null) Destroy(m);
        if (colliderMesh != null) Destroy(colliderMesh);
        if (instance == this) instance = null;
    }
}

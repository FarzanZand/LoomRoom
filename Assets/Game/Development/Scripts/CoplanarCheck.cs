using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Finds faces that would flicker (z-fight) on a built floor: two opaque faces turned the same way, in one plane, that
// overlap by more than a sliver (the user, 2026-11-13: "you do this lighting bug often"; CLAUDE.md: never two surfaces
// in one place). Faces turned away from each other (boxes back to back) and faces that only meet along an edge are
// fine, and so is a face buried in another solid box (two wall pieces meeting end to end: each one's end face lies inside
// the next). Looks at every mesh renderer that is drawn under the root (merged chunks, pieces, props; characters, particles,
// lines and see-through materials left out), in world space. Each find names the pieces whose bounds hold it, drawn or
// merged away, so the source can be found. Used by DungeonStressTest.Flicker; editor only.
public static class CoplanarCheck
{
#if UNITY_EDITOR
    public class Find
    {
        public Vector3 at;           // the middle of the overlap
        public Vector3 normal;
        public float area;           // square metres overlapping (summed over the pair's triangles)
        public string a, b;          // the two renderers, by path under the root
        public string pieces;        // the pieces whose bounds hold the overlap (what it was built from)
        public Renderer ra, rb;      // the two renderers
        public string triangles;     // the first two triangles found overlapping, by their corners
        public override string ToString() => $"{area * 10000f:0} cm² at {at.x:0.00},{at.y:0.00},{at.z:0.00} facing {Facing(normal)}: {a} / {b} ({Material(ra)} / {Material(rb)}){(string.IsNullOrEmpty(pieces) ? "" : " (pieces: " + pieces + ")")} [triangles {triangles}]";
    }

    struct Tri
    {
        public int renderer;
        public Vector2 p0, p1, p2;   // in the plane's own 2D frame
        public Vector2 min, max;
        public Vector3 w0, w1, w2;   // the world corners, for reporting
    }

    // minArea: smallest overlap reported (square metres; 4 cm² by default, below a visible flicker at play distance).
    // tolerance: how far apart two planes may be and still fight (metres; depth buffers lose them under about 1 mm).
    // seen: whether a point just in front of a face can be seen at all (DungeonStressTest: not under the floor, unless in a
    // pit's hole); null takes every face that isn't buried.
    public static List<Find> Run(Transform root, float minArea = .0004f, float tolerance = .0015f, System.Func<Renderer, bool> include = null, System.Func<Vector3, bool> seen = null)
    {
        var renderers = new List<Renderer>();
        // The solid boxes a hidden face can be buried in: every box under the root, drawn or merged into a chunk.
        var solids = new List<Bounds>();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount != 24 || !Square(r.transform.rotation)) continue;
            var b = r.bounds;
            b.Expand(-.001f);   // a face on the box's own surface is not inside it
            if (b.size.x > 0 && b.size.y > 0 && b.size.z > 0) solids.Add(b);
        }
        var groups = new Dictionary<(int axis, long offset), List<Tri>>();
        var normals = new Dictionary<(int axis, long offset), Vector3>();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled || r.forceRenderingOff || !r.gameObject.activeInHierarchy) continue;
            if (include != null && !include(r)) continue;
            var filter = r.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) continue;
            var materials = r.sharedMaterials;
            int index = renderers.Count;
            renderers.Add(r);
            var vertices = mesh.vertices;
            var m = r.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = m.MultiplyPoint3x4(vertices[i]);
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var material = sub < materials.Length ? materials[sub] : null;
                if (material == null || material.renderQueue >= 2450) continue;   // see-through and alpha-tested-late materials blend, they don't fight
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(sub);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = vertices[tris[t]], b = vertices[tris[t + 1]], c = vertices[tris[t + 2]];
                    var cross = Vector3.Cross(b - a, c - a);
                    float twice = cross.magnitude;
                    if (twice < 1e-6f) continue;
                    var n = cross / twice;
                    // Planes keyed by their normal (axis-aligned ones exactly, others quantised) and their distance.
                    int axis = AxisOf(n);
                    float d = Vector3.Dot(n, a);
                    var key = (axis, (long)Mathf.Round(d / tolerance));
                    if (!groups.TryGetValue(key, out var list)) { groups[key] = list = new List<Tri>(); normals[key] = n; }
                    Basis(n, out var u, out var v);
                    var tri = new Tri { renderer = index, p0 = new Vector2(Vector3.Dot(a, u), Vector3.Dot(a, v)), p1 = new Vector2(Vector3.Dot(b, u), Vector3.Dot(b, v)), p2 = new Vector2(Vector3.Dot(c, u), Vector3.Dot(c, v)), w0 = a, w1 = b, w2 = c };
                    tri.min = Vector2.Min(tri.p0, Vector2.Min(tri.p1, tri.p2)); tri.max = Vector2.Max(tri.p0, Vector2.Max(tri.p1, tri.p2));
                    list.Add(tri);
                }
            }
        }

        // Each plane with the planes a step either side (a plane just across a rounding edge still fights).
        var pairs = new Dictionary<(int, int, int, int, int), Find>();

        foreach (var kv in groups)
        {
            var (axis, offset) = kv.Key;
            for (long step = 0; step <= 1; step++)
            {
                var other = (axis, offset + step);
                if (!groups.TryGetValue(other, out var with)) continue;
                Compare(kv.Value, with, step == 0, normals[kv.Key], renderers, pairs, minArea * .25f, solids, seen, root.position);
            }
        }

        var finds = new List<Find>();
        foreach (var f in pairs.Values) if (f.area >= minArea) finds.Add(f);
        finds.Sort((x, y) => y.area.CompareTo(x.area));
        // What each overlap was built from: every renderer (drawn or merged away) whose bounds hold it.
        var all = root.GetComponentsInChildren<MeshRenderer>(true);
        // The pieces with a face in the overlap's plane, turned its way, over its middle (merged chunks left out: their
        // pieces are named instead).
        foreach (var f in finds)
        {
            var sb = new StringBuilder();
            int n = 0;
            foreach (var r in all)
            {
                if (r.name == "Static chunk") continue;
                var bounds = r.bounds; bounds.Expand(.05f);
                if (!bounds.Contains(f.at) || !HasFaceAt(r, f.at, f.normal)) continue;
                if (n++ < 8) sb.Append(n > 1 ? ", " : "").Append(Path(r.transform, root)).Append($" [{r.bounds.min.x:0.00},{r.bounds.min.y:0.00},{r.bounds.min.z:0.00} to {r.bounds.max.x:0.00},{r.bounds.max.y:0.00},{r.bounds.max.z:0.00}]");
            }
            if (n > 8) sb.Append($" and {n - 8} more");
            f.pieces = sb.ToString();
        }
        return finds;
    }

    static void Compare(List<Tri> a, List<Tri> b, bool same, Vector3 normal, List<Renderer> renderers, Dictionary<(int, int, int, int, int), Find> pairs, float sliver, List<Bounds> solids, System.Func<Vector3, bool> seen, Vector3 origin)
    {
        // Both lists laid flat with one basis, from the floor's origin: each triangle flattened by its own normal drifted by
        // centimetres on a tilted face (normals a hair apart, times coordinates near 500), and touching faces read as overlapping.
        Basis(normal, out var bu, out var bv);
        a = Flatten(a, bu, bv, origin);
        b = same ? a : Flatten(b, bu, bv, origin);
        // A 2D grid of the second list's triangles by their bounds (half-metre cells), so only neighbours are compared.
        const float cell = .5f;
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < b.Count; i++)
        {
            var t = b[i];
            for (int x = Mathf.FloorToInt(t.min.x / cell); x <= Mathf.FloorToInt(t.max.x / cell); x++)
                for (int y = Mathf.FloorToInt(t.min.y / cell); y <= Mathf.FloorToInt(t.max.y / cell); y++)
                {
                    if (!grid.TryGetValue((x, y), out var list)) grid[(x, y)] = list = new List<int>();
                    list.Add(i);
                }
        }
        var tested = new HashSet<long>();
        for (int i = 0; i < a.Count; i++)
        {
            var t = a[i];
            tested.Clear();
            for (int x = Mathf.FloorToInt(t.min.x / cell); x <= Mathf.FloorToInt(t.max.x / cell); x++)
                for (int y = Mathf.FloorToInt(t.min.y / cell); y <= Mathf.FloorToInt(t.max.y / cell); y++)
                {
                    if (!grid.TryGetValue((x, y), out var list)) continue;
                    foreach (int j in list)
                    {
                        if (same && j <= i) continue;
                        if (!tested.Add(j)) continue;
                        var s = b[j];
                        if (s.max.x <= t.min.x || s.min.x >= t.max.x || s.max.y <= t.min.y || s.min.y >= t.max.y) continue;
                        // The same triangle drawn twice (a box face in two materials' parts of one mesh) gets the same depth
                        // both times: it never flickers.
                        if (Same(t, s)) continue;
                        float area = Overlap(t, s, out var centre);
                        if (area < sliver) continue;
                        var at = origin + bu * centre.x + bv * centre.y + normal * Vector3.Dot(normal, t.w0 - origin);
                        // Buried: just in front of the face is inside a solid box, so nothing ever sees it.
                        if (Inside(at + normal * .005f, solids) || seen != null && !seen(at + normal * .005f)) continue;
                        // One find per pair of renderers per two-metre spot (merged chunks hold many separate spots).
                        int r0 = Mathf.Min(t.renderer, s.renderer), r1 = Mathf.Max(t.renderer, s.renderer);
                        var key = (r0, r1, Mathf.FloorToInt(at.x / 2f), Mathf.FloorToInt(at.y / 2f), Mathf.FloorToInt(at.z / 2f));
                        if (!pairs.TryGetValue(key, out var f))
                            pairs[key] = f = new Find { normal = normal, at = at, a = PathOf(renderers[r0]), b = PathOf(renderers[r1]), ra = renderers[r0], rb = renderers[r1],
                                triangles = $"{t.w0:F3} {t.w1:F3} {t.w2:F3} / {s.w0:F3} {s.w1:F3} {s.w2:F3}" };
                        f.area += area;
                    }
                }
        }
    }

    // The area two triangles share, and its middle (convex clipping).
    static float Overlap(Tri t, Tri s, out Vector2 centre)
    {
        var poly = new List<Vector2> { t.p0, t.p1, t.p2 };
        var clip = new[] { s.p0, s.p1, s.p2 };
        if (Cross(clip[1] - clip[0], clip[2] - clip[0]) < 0) { var x = clip[1]; clip[1] = clip[2]; clip[2] = x; }
        for (int e = 0; e < 3 && poly.Count > 0; e++)
        {
            Vector2 a = clip[e], b = clip[(e + 1) % 3];
            var input = poly; poly = new List<Vector2>();
            for (int k = 0; k < input.Count; k++)
            {
                Vector2 p = input[k], q = input[(k + 1) % input.Count];
                bool pin = Cross(b - a, p - a) >= 0, qin = Cross(b - a, q - a) >= 0;
                if (pin) poly.Add(p);
                if (pin != qin)
                {
                    float dp = Cross(b - a, p - a), dq = Cross(b - a, q - a);
                    poly.Add(p + (q - p) * (dp / (dp - dq)));
                }
            }
        }
        // The middle of the two triangles' shared bounds: inside the overlap for the boxes this looks at, and steady where a
        // sliver of overlap makes the clipped polygon's own points unreliable.
        centre = (Vector2.Max(t.min, s.min) + Vector2.Min(t.max, s.max)) * .5f;
        if (poly.Count < 3) return 0f;
        float area = 0f;
        for (int k = 0; k < poly.Count; k++) area += Cross(poly[k], poly[(k + 1) % poly.Count]);
        return Mathf.Abs(area) * .5f;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static List<Tri> Flatten(List<Tri> list, Vector3 u, Vector3 v, Vector3 origin)
    {
        var flat = new List<Tri>(list.Count);
        foreach (var t in list)
        {
            var f = t;
            Vector2 P(Vector3 p) => new Vector2(Vector3.Dot(p - origin, u), Vector3.Dot(p - origin, v));
            f.p0 = P(t.w0); f.p1 = P(t.w1); f.p2 = P(t.w2);
            f.min = Vector2.Min(f.p0, Vector2.Min(f.p1, f.p2)); f.max = Vector2.Max(f.p0, Vector2.Max(f.p1, f.p2));
            flat.Add(f);
        }
        return flat;
    }

    static bool Same(Tri t, Tri s)
    {
        // Also the two halves of one quad (an edge in common, in one plane): they only meet along that edge, whatever a
        // tilted face's rounding makes of the clipped area.
        bool Has(Vector3 p) => (p - s.w0).sqrMagnitude < 1e-8f || (p - s.w1).sqrMagnitude < 1e-8f || (p - s.w2).sqrMagnitude < 1e-8f;
        return (Has(t.w0) ? 1 : 0) + (Has(t.w1) ? 1 : 0) + (Has(t.w2) ? 1 : 0) >= 2;
    }

    // Whether a renderer has a triangle in the plane through p facing n that covers p.
    static bool HasFaceAt(Renderer r, Vector3 p, Vector3 n)
    {
        var filter = r.GetComponent<MeshFilter>();
        var mesh = filter != null ? filter.sharedMesh : null;
        if (mesh == null || !mesh.isReadable) return false;
        var verts = mesh.vertices; var tris = mesh.triangles; var m = r.localToWorldMatrix;
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 a = m.MultiplyPoint3x4(verts[tris[t]]), b = m.MultiplyPoint3x4(verts[tris[t + 1]]), c = m.MultiplyPoint3x4(verts[tris[t + 2]]);
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < 1e-12f) continue;
            var tn = cross.normalized;
            if (Vector3.Dot(tn, n) < .999f || Mathf.Abs(Vector3.Dot(tn, p - a)) > .002f) continue;
            Vector3 v0 = c - a, v1 = b - a, v2 = p - a;
            float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1), d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
            float den = d00 * d11 - d01 * d01;
            if (Mathf.Abs(den) < 1e-12f) continue;
            float u = (d11 * d20 - d01 * d21) / den, w = (d00 * d21 - d01 * d20) / den;
            if (u >= -.02f && w >= -.02f && u + w <= 1.02f) return true;
        }
        return false;
    }

    static bool Inside(Vector3 p, List<Bounds> solids)
    {
        foreach (var b in solids) if (b.Contains(p)) return true;
        return false;
    }

    // Turned only in quarter turns, so its bounds are the box itself.
    static bool Square(Quaternion q)
    {
        var e = q.eulerAngles;
        bool Q(float a) { float m = Mathf.Repeat(a, 90f); return m < .01f || m > 89.99f; }
        return Q(e.x) && Q(e.y) && Q(e.z);
    }

    // 0-5 for the six axis directions, else a key from the normal quantised to about a degree.
    static int AxisOf(Vector3 n)
    {
        if (n.x > .9999f) return 0; if (n.x < -.9999f) return 1;
        if (n.y > .9999f) return 2; if (n.y < -.9999f) return 3;
        if (n.z > .9999f) return 4; if (n.z < -.9999f) return 5;
        int q(float f) => Mathf.RoundToInt(f * 60f) + 64;
        return 6 + (q(n.x) << 14 | q(n.y) << 7 | q(n.z));
    }

    static void Basis(Vector3 n, out Vector3 u, out Vector3 v)
    {
        u = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized;
        v = Vector3.Cross(n, u);
    }

    static string Facing(Vector3 n) =>
        n.y > .99f ? "up" : n.y < -.99f ? "down" : n.x > .99f ? "+x" : n.x < -.99f ? "-x" : n.z > .99f ? "+z" : n.z < -.99f ? "-z" : n.ToString("0.00");

    static string Material(Renderer r) => r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "-";
    static string PathOf(Renderer r) => r != null ? Path(r.transform, null) : "?";
    static string Path(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root && parts.Count < 4; x = x.parent) parts.Insert(0, x.name);
        return string.Join("/", parts);
    }
#endif
}

using System.Collections.Generic;
using UnityEngine;

// Marks a room object while a quest points at it (the character sheet's table, the symbol puzzle): a bright blue
// outline round its biggest pieces that breathes, drawn by Quest Outline.shader on copies of their meshes with
// smoothed normals (so box corners stay closed). Added and switched by the object's own script; nothing to set up.
public class QuestGlow : MonoBehaviour
{
    public Color colour = new(.45f, .8f, 1f, 1f);
    [Tooltip("Outline width in world units (the room is about 22 units to the metre).")]
    [Min(0)] public float width = .7f;
    // Flat things hung on a wall (a trophy plaque): the shell round them is hidden behind their own edge when seen from
    // below or the side, so they get one flat ring instead, round their outline as this transform's forward sees it,
    // just in front of their face (the user, 2026-11-11: the top row of the trophy wall showed half a ring).
    [Tooltip("Set by the owner's script for a flat piece on a wall: its forward points out of the wall.")]
    public Transform flatOn;

    readonly List<GameObject> shells = new();
    Material material;
    bool shown;

    public void Show(bool on)
    {
        // Built when first wanted with something to outline. Switched on while its object wasn't drawn yet (the table's
        // build hides every piece until it lands) it found nothing; once it can, the outline it builds then must show
        // (it was made switched off and stayed so: the dice cup, the user, 2026-11-11).
        if (on && shells.Count == 0) { Build(); if (shown) foreach (var s in shells) if (s != null) s.SetActive(true); }
        if (on == shown) return;
        shown = on;
        foreach (var s in shells) if (s != null) s.SetActive(on);
    }

    void Update()
    {
        if (!shown || material == null) return;
        float k = .5f + .5f * Mathf.Sin(Time.time * 3f);
        material.color = Color.Lerp(colour, Color.white, .45f * k);
        // A flat ring is built at its width: it is not pushed out along its normals.
        material.SetFloat("_Width", flatOn != null ? 0f : width * (.85f + .3f * k));
    }

    // The outline goes round the pieces that make the object's shape: its renderers at least a quarter the size of
    // the biggest (a table, not the brushes and figures on it).
    void Build()
    {
        var template = Resources.Load<Material>("Quest outline");
        if (template == null) { Debug.LogWarning("[QuestGlow] Resources/Quest outline material is missing."); return; }
        material = new Material(template);
        var renderers = new List<MeshRenderer>();
        foreach (var r in GetComponentsInChildren<MeshRenderer>(false))
            if (r.enabled && r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh != null) renderers.Add(r);
        float biggest = 0;
        foreach (var r in renderers) biggest = Mathf.Max(biggest, Volume(r.bounds));
        if (flatOn != null) { BuildFlat(renderers.FindAll(r => Volume(r.bounds) >= biggest * .25f)); return; }
        foreach (var r in renderers)
        {
            if (Volume(r.bounds) < biggest * .25f) continue;
            var mesh = Smoothed(r.GetComponent<MeshFilter>().sharedMesh);
            if (mesh == null) continue;
            var shell = new GameObject("Quest outline");
            shell.transform.SetParent(r.transform, false);
            shell.layer = r.gameObject.layer;
            shell.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = shell.AddComponent<MeshRenderer>();
            mr.sharedMaterials = Fill(material, mesh.subMeshCount);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.renderingLayerMask = r.renderingLayerMask;
            shell.SetActive(false);
            shells.Add(shell);
        }
    }

    // One flat ring round the pieces' outline as flatOn's forward sees it (their convex hull in its plane), Width wide,
    // standing a little over 1 cm (.25 room units) in front of their face so it never lies in the face's plane. Drawn both
    // ways round, so the outline shader (back faces only) shows it from either side.
    void BuildFlat(List<MeshRenderer> pieces)
    {
        Vector3 right = flatOn.right, up = flatOn.up, forward = flatOn.forward, origin = flatOn.position;
        var flat = new List<Vector2>();
        float front = float.MinValue;
        foreach (var r in pieces)
        {
            var mesh = r.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] v;
            try { v = mesh.vertices; } catch { continue; }
            var m = r.transform.localToWorldMatrix;
            foreach (var p in v)
            {
                var w = m.MultiplyPoint3x4(p) - origin;
                flat.Add(new Vector2(Vector3.Dot(w, right), Vector3.Dot(w, up)));
                front = Mathf.Max(front, Vector3.Dot(w, forward));
            }
        }
        var hull = Hull(flat);
        if (hull.Count < 3) return;
        int n = hull.Count;
        var verts = new Vector3[n * 2];
        var normals = new Vector3[n * 2];
        float depth = front + .25f;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = hull[(i + n - 1) % n], p = hull[i], b = hull[(i + 1) % n];
            // The hull runs anticlockwise: each edge's outward normal is its direction turned clockwise.
            Vector2 n1 = new Vector2(p.y - a.y, a.x - p.x).normalized, n2 = new Vector2(b.y - p.y, p.x - b.x).normalized;
            var dir = (n1 + n2).normalized;
            float k = Mathf.Max(.35f, Vector2.Dot(dir, n1));
            var outer = p + dir * (width / k);
            verts[i] = flatOn.InverseTransformPoint(origin + right * p.x + up * p.y + forward * depth);
            verts[n + i] = flatOn.InverseTransformPoint(origin + right * outer.x + up * outer.y + forward * depth);
            normals[i] = normals[n + i] = Vector3.forward;
        }
        var tris = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            tris.AddRange(new[] { i, n + i, n + j, i, n + j, j });   // one way round
            tris.AddRange(new[] { i, n + j, n + i, i, j, n + j });   // and the other
        }
        var ring = new Mesh { name = name + " flat outline", vertices = verts, normals = normals, triangles = tris.ToArray() };
        ring.RecalculateBounds();
        material.SetFloat("_Width", 0f);
        var shell = new GameObject("Quest outline");
        shell.transform.SetParent(flatOn, false);
        shell.layer = flatOn.gameObject.layer;
        shell.AddComponent<MeshFilter>().sharedMesh = ring;
        var mr = shell.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.renderingLayerMask = pieces[0].renderingLayerMask;
        shell.SetActive(false);
        shells.Add(shell);
    }

    // The convex hull of the points, anticlockwise (monotone chain).
    static List<Vector2> Hull(List<Vector2> points)
    {
        var p = new List<Vector2>(points);
        p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        var h = new List<Vector2>();
        if (p.Count < 3) return h;
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        for (int i = 0; i < p.Count; i++) { while (h.Count >= 2 && Cross(h[^2], h[^1], p[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(p[i]); }
        for (int i = p.Count - 2, t = h.Count + 1; i >= 0; i--) { while (h.Count >= t && Cross(h[^2], h[^1], p[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(p[i]); }
        h.RemoveAt(h.Count - 1);
        return h;
    }

    static Material[] Fill(Material m, int n) { var a = new Material[n]; for (int i = 0; i < n; i++) a[i] = m; return a; }
    static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;

    // A copy of the mesh whose normals point out of every face that meets at a place: the sum of the distinct face
    // normals there, so the shell is pushed out evenly round corners and edges. A vertex lying partway along another
    // face's edge (a T-junction: the coffee table's top has one a third of the way along its back edge that the back
    // face doesn't) counts that face too; summing only the vertices at the same place left it pointing straight up, and
    // the outline stepped in there (review 2026-11-12).
    static Mesh Smoothed(Mesh source)
    {
        Vector3[] v;
        try { v = source.vertices; } catch { return null; }
        var n = source.normals;
        if (n == null || n.Length != v.Length) return null;
        Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 1000f);
        Vector3Int Dir(Vector3 d) => Vector3Int.RoundToInt(d * 50f);
        var faces = new Dictionary<Vector3Int, Dictionary<Vector3Int, Vector3>>();   // place -> distinct face normals
        var place = new Dictionary<Vector3Int, Vector3>();
        void Add(Vector3Int k, Vector3 fn)
        {
            if (!faces.TryGetValue(k, out var set)) faces[k] = set = new Dictionary<Vector3Int, Vector3>();
            set[Dir(fn)] = fn;
        }
        var edges = new List<(Vector3 a, Vector3 b, Vector3 fn)>();
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
            var t = source.GetTriangles(sub);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                var fn = Vector3.Cross(b - a, c - a);
                if (fn.sqrMagnitude < 1e-12f) continue;
                fn.Normalize();
                foreach (var p in new[] { a, b, c }) { var k = Key(p); place[k] = p; Add(k, fn); }
                edges.Add((a, b, fn)); edges.Add((b, c, fn)); edges.Add((c, a, fn));
            }
        }
        // T-junctions (skipped on big meshes, where the search would take too long).
        if ((long)place.Count * edges.Count <= 4_000_000)
            foreach (var (k, p) in place)
                foreach (var (a, b, fn) in edges)
                {
                    var ab = b - a; float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-10f) continue;
                    float s = Vector3.Dot(p - a, ab) / len2;
                    if (s <= .001f || s >= .999f) continue;
                    if ((a + ab * s - p).sqrMagnitude > len2 * 1e-6f) continue;
                    Add(k, fn);
                }
        var smooth = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var sum = Vector3.zero;
            if (faces.TryGetValue(Key(v[i]), out var set)) foreach (var fn in set.Values) sum += fn;
            // Faces turned against each other (a thin double-sided card) cancel out: then the vertex's own normal.
            smooth[i] = sum.sqrMagnitude > 1e-4f ? sum.normalized : n[i].normalized;
        }
        // Box-shaped furniture (nearly all its faces facing along an axis): a shell point lying within a hair (1.6% of the
        // object's size) of the object's outer faces is set onto them. Synty's coffee table has chips about 2 cm deep cut
        // into its top's back edge, and the outline stepped in along them (review 2026-11-12); the outline's outer edge
        // now runs straight past, while the table itself keeps its chips inside it. Round things are left as they are.
        // (1.6%: its deepest chip is 2.19 cm in, its legs stand 2.5 cm in and must keep their own outline.)
        var shellPos = (Vector3[])v.Clone();
        float axisArea = 0f, allArea = 0f;
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            if (source.GetTopology(sub) != MeshTopology.Triangles) continue;
            var t = source.GetTriangles(sub);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                var cross = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                float area = cross.magnitude; if (area < 1e-12f) continue;
                allArea += area;
                var fn = cross / area;
                if (Mathf.Max(Mathf.Abs(fn.x), Mathf.Abs(fn.y), Mathf.Abs(fn.z)) > .97f) axisArea += area;
            }
        }
        if (allArea > 0f && axisArea >= allArea * .8f)
        {
            Vector3 lo = source.bounds.min, hi = source.bounds.max;
            float snap = source.bounds.size.magnitude * .016f;
            for (int i = 0; i < shellPos.Length; i++)
            {
                var q = shellPos[i];
                for (int axis = 0; axis < 3; axis++)
                {
                    if (hi[axis] - q[axis] < snap) q[axis] = hi[axis];
                    else if (q[axis] - lo[axis] < snap) q[axis] = lo[axis];
                }
                shellPos[i] = q;
            }
        }
        var mesh = Instantiate(source);
        mesh.name = source.name + " outline";
        mesh.vertices = shellPos;
        mesh.normals = smooth;
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        foreach (var s in shells) if (s != null) { Destroy(s.GetComponent<MeshFilter>().sharedMesh); Destroy(s); }
        if (material != null) Destroy(material);
    }
}

using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A road between two places on the campaign map. It draws itself over the ground from one place's base to the
// other's and rebuilds when either place moves, or its bend or width change; the map's scatter steps off it.
// The player's figure walks along it. A road is open once both its places' acts are open.
[ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CampaignPath : MonoBehaviour
{
    [Required] public CampaignPoint from, to;
    [Range(-1f, 1f), Tooltip("How far the road bows to one side (a share of its length).")]
    public float bend;
    [Min(.2f), Tooltip("Width of the road (local units).")]
    public float width = 1.1f;
    [Range(0f, 1f), Tooltip("How much the road wanders.")]
    public float wobble = .35f;
    [Min(4)] public int samples = 60;

    readonly List<Vector3> points = new();
    public IReadOnlyList<Vector3> LocalPoints { get { if (points.Count == 0) Rebuild(); return points; } }

    CampaignMap map;
    Vector3 lastFrom, lastTo; float lastBend, lastWidth, lastWobble, lastFromRadius, lastToRadius;
    Mesh built;   // this road's own mesh (never shared with a duplicate, freed with the road)
    void OnDestroy() { if (built != null) { if (Application.isPlaying) Destroy(built); else DestroyImmediate(built); } }
    CampaignMap Map => map != null ? map : map = GetComponentInParent<CampaignMap>(true);

    public bool Open(int openAct) => from != null && to != null && from.encounter != null && to.encounter != null
        && from.encounter.act <= openAct && to.encounter.act <= openAct;

    void OnEnable() => Rebuild();

    void Update()
    {
        if (Application.isPlaying || from == null || to == null) return;
        if (from.transform.localPosition != lastFrom || to.transform.localPosition != lastTo || bend != lastBend || width != lastWidth || wobble != lastWobble
            || from.radius != lastFromRadius || to.radius != lastToRadius)
        {
            Rebuild();
            if (Map != null) Map.ScatterNeedsClearing();
        }
    }

    // The road's centre line over the ground, from the edge of one base to the edge of the other.
    [Button("Rebuild road")]
    public void Rebuild()
    {
        points.Clear();
        if (from == null || to == null || Map == null) return;
        Vector3 a = from.transform.localPosition, b = to.transform.localPosition;
        lastFrom = a; lastTo = b; lastBend = bend; lastWidth = width; lastWobble = wobble; lastFromRadius = from.radius; lastToRadius = to.radius;
        var flat = b - a; flat.y = 0;
        float length = flat.magnitude;
        if (length < .01f) { GetComponent<MeshFilter>().sharedMesh = null; return; }
        var dir = flat / length; var side = new Vector3(-dir.z, 0, dir.x);
        var mid = (a + b) * .5f + side * bend * length * .5f;
        float t0 = Mathf.Clamp01(from.radius / length), t1 = 1f - Mathf.Clamp01(to.radius / length);
        // Seeded by the places' encounter ids (stable through renames; string.GetHashCode is not stable either).
        int seed = (int)((StableHash(from.encounter != null ? from.encounter.id : from.name) ^ StableHash(to.encounter != null ? to.encounter.id : to.name)) & 1023);
        for (int i = 0; i <= samples; i++)
        {
            float t = Mathf.Lerp(t0, t1, i / (float)samples);
            var p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * b;
            float w = (Mathf.PerlinNoise(t * 5f, seed) - .5f) * wobble * 2.4f * Mathf.Sin(t * Mathf.PI);
            p += side * w;
            points.Add(Map.OnGround(p));
        }
        BuildMesh();
    }

    void BuildMesh()
    {
        var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
        float along = 0;
        for (int i = 0; i < points.Count; i++)
        {
            var prev = points[Mathf.Max(0, i - 1)]; var next = points[Mathf.Min(points.Count - 1, i + 1)];
            var d = next - prev; d.y = 0; d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            var side = new Vector3(-d.z, 0, d.x) * width * .5f;
            if (i > 0) along += Vector3.Distance(points[i], points[i - 1]);
            foreach (var s in new[] { side, -side })
            {
                var p = points[i] + s;
                p.y = Map.HeightAt(p.x, p.z) + .08f;
                verts.Add(p - transform.localPosition);
                uvs.Add(new Vector2(along / (width * 2f), s == side ? 0 : 1));
            }
            if (i > 0) { int q = verts.Count - 4; tris.AddRange(new[] { q, q + 2, q + 1, q + 1, q + 2, q + 3 }); }
        }
        if (built == null) built = new Mesh { name = "Road", hideFlags = HideFlags.DontSave };
        var mesh = built;
        mesh.Clear();
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals();
        // Faces up whichever way the points were laid out.
        if (mesh.normals.Length > 0 && mesh.normals[0].y < 0) { tris.Reverse(); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); }
        GetComponent<MeshFilter>().sharedMesh = mesh;
        var r = GetComponent<MeshRenderer>();
        if (Map.roadMaterial != null && r.sharedMaterial != Map.roadMaterial) r.sharedMaterial = Map.roadMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static uint StableHash(string s) { unchecked { uint h = 2166136261; foreach (char c in s ?? "") h = (h ^ c) * 16777619; return h; } }

    // Distance from a local position to the road's centre line, across the ground.
    public float DistanceTo(Vector3 local)
    {
        var pts = LocalPoints;
        float best = float.MaxValue;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 a = new(pts[i - 1].x, pts[i - 1].z), b = new(pts[i].x, pts[i].z), p = new(local.x, local.z);
            var ab = b - a; float t = ab.sqrMagnitude > 0 ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            best = Mathf.Min(best, (a + ab * t - p).magnitude);
        }
        return best;
    }
}

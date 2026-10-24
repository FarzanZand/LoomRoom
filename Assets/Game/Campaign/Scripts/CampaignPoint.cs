using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// A place on the campaign map. Its props are its children and stand on a low round base; move the object and they
// all go with it, the base drops onto the ground under it and the roads to it follow (CampaignPath). What it is,
// what it loads and when it opens come from its encounter, edited right here.
[ExecuteAlways]
public class CampaignPoint : MonoBehaviour
{
    [InlineEditor(InlineEditorObjectFieldModes.Boxed), Tooltip("What this place is: its name, kind, level, act and lines.")]
    public CampaignEncounter encounter;
    [Min(.5f), Tooltip("Radius of the base its props stand on (local units). The roads end at its edge.")]
    public float radius = 3f;
    [Tooltip("A rough mound of earth on a stone plinth under the props.")]
    public bool showBase = true;
    [Tooltip("A soft light over the place (optional).")]
    public Light glow;

    CampaignMap map;
    Vector3 lastPosition; float lastRadius; bool lastBase;
    Transform baseRoot;
    float baseLift;
    public float Lift { get; set; }   // hover lift, in local units

    CampaignMap Map => map != null ? map : map = GetComponentInParent<CampaignMap>(true);

    void OnEnable() { Snap(); BuildBase(); }

    void Update()
    {
        if (!Application.isPlaying)
        {
            if (transform.localPosition != lastPosition || radius != lastRadius || showBase != lastBase) { Snap(); BuildBase(); }
            return;
        }
        // Hovered places rise a little; the rest settle.
        if (Mathf.Abs(baseLift - Lift) < .001f) return;
        baseLift = Mathf.Lerp(baseLift, Lift, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        if (Mathf.Abs(baseLift - Lift) < .001f) baseLift = Lift;
        var p = transform.localPosition; p.y = groundY + baseLift; transform.localPosition = p;
        // The pick box stays where the place rests (rising with it, the mouse at its edge would lose it and it would
        // drop and rise again).
        if (pick == null) pick = GetComponent<BoxCollider>();
        if (pick != null) pick.center = new Vector3(0, radius * .6f - baseLift / Mathf.Max(.001f, transform.localScale.y), 0);
    }

    float groundY;
    BoxCollider pick;

    // The base sits on the highest ground under it, so no hill pokes through.
    void Snap()
    {
        if (Map == null) return;
        var p = transform.localPosition;
        float top = Map.HeightAt(p.x, p.z);
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2;
            top = Mathf.Max(top, Map.HeightAt(p.x + Mathf.Cos(a) * radius, p.z + Mathf.Sin(a) * radius));
        }
        groundY = top + .25f;
        p.y = groundY;
        transform.localPosition = p;
        lastPosition = p; lastRadius = radius; lastBase = showBase;
        if (!Application.isPlaying)
        {
            foreach (var path in Map.Paths) if (path.from == this || path.to == this) path.Rebuild();
            Map.ScatterNeedsClearing();
        }
    }

    // The base: a rough mound of earth on a stone plinth, its edge jagged and its sides stepped, reaching down to the
    // lowest ground under it. Its mesh is made here (never saved), so it follows the radius and the ground.
    void BuildBase()
    {
        if (baseRoot == null) baseRoot = transform.Find("Base");
        if (!showBase) { if (baseRoot != null) baseRoot.gameObject.SetActive(false); return; }
        if (Map == null) return;
        if (baseRoot == null)
        {
            baseRoot = new GameObject("Base").transform;
            baseRoot.SetParent(transform, false);
        }
        baseRoot.gameObject.SetActive(true);
        // The old cylinders.
        foreach (var old in new[] { "Rim", "Top" })
        {
            var t = baseRoot.Find(old);
            if (t != null) { if (Application.isPlaying) Destroy(t.gameObject); else DestroyImmediate(t.gameObject); }
        }
        var p = transform.localPosition;
        float low = groundY;
        for (int i = 0; i < 12; i++) { float a = i / 12f * Mathf.PI * 2; low = Mathf.Min(low, Map.HeightAt(p.x + Mathf.Cos(a) * radius, p.z + Mathf.Sin(a) * radius)); }
        float depth = groundY - low + .5f;
        var mf = baseRoot.GetComponent<MeshFilter>(); if (mf == null) mf = baseRoot.gameObject.AddComponent<MeshFilter>();
        var mr = baseRoot.GetComponent<MeshRenderer>(); if (mr == null) mr = baseRoot.gameObject.AddComponent<MeshRenderer>();
        if (baseMesh != null) { if (Application.isPlaying) Destroy(baseMesh); else DestroyImmediate(baseMesh); }
        baseMesh = Plinth(radius, depth, Seed());
        mf.sharedMesh = baseMesh;
        var mats = new[] { CampaignBaseMaterials.Top, CampaignBaseMaterials.Rim };
        if (mats[0] != null && mats[1] != null) mr.sharedMaterials = mats;
    }

    Mesh baseMesh;
    void OnDestroy() { if (baseMesh != null) { if (Application.isPlaying) Destroy(baseMesh); else DestroyImmediate(baseMesh); } }

    int Seed()
    {
        unchecked
        {
            int h = 17;
            foreach (char c in encounter != null ? encounter.id ?? "" : name) h = h * 31 + c;
            return h;
        }
    }

    // Top: an uneven earth disc (submesh 0). Sides: a worn lip, then stone stepped out in two ledges down to the
    // foot (submesh 1). UVs are in local units at the ground's texel size (4 a unit with the 32 and 16 texel tiles).
    static Mesh Plinth(float radius, float depth, int seed)
    {
        const int sides = 36;
        float ox = (seed & 1023) * .173f, oz = ((seed >> 10) & 1023) * .131f;
        float Noise(float a, float f, float o) => Mathf.PerlinNoise(ox + Mathf.Cos(a) * f + o, oz + Mathf.Sin(a) * f) - .5f;
        var verts = new List<Vector3>(); var uvs = new List<Vector2>();
        var top = new List<int>(); var side = new List<int>();
        // Edge radius per side: a jagged outline.
        var edge = new float[sides + 1]; var lift = new float[sides + 1];
        for (int i = 0; i <= sides; i++)
        {
            float a = i % sides / (float)sides * Mathf.PI * 2;
            edge[i] = radius * (1f + Noise(a, 1.6f, 0) * .16f + Noise(a, 5f, 9) * .06f);
            lift[i] = Noise(a, 2.5f, 20) * .14f;
        }
        Vector3 Ring(int i, float r, float y) { float a = i % sides / (float)sides * Mathf.PI * 2; return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); }

        // Top: centre, an inner ring and the edge, gently domed and lumpy.
        verts.Add(new Vector3(0, .28f, 0)); uvs.Add(Vector2.zero);
        for (int i = 0; i <= sides; i++)
        {
            float a = i / (float)sides * Mathf.PI * 2;
            var inner = Ring(i, edge[i] * .55f, .2f + Noise(a, 3f, 40) * .22f);
            verts.Add(inner); uvs.Add(new Vector2(inner.x, inner.z) / 8f);
            var outer = Ring(i, edge[i], lift[i] * .5f);
            verts.Add(outer); uvs.Add(new Vector2(outer.x, outer.z) / 8f);
        }
        for (int i = 0; i < sides; i++)
        {
            int a = 1 + i * 2, b = a + 2;
            top.AddRange(new[] { 0, b, a });
            top.AddRange(new[] { a, b, a + 1, b, b + 1, a + 1 });
        }

        // Sides: rings from the lip down to the foot.
        var rings = new (float grow, float y, float jag)[]
        {
            (0f, 0f, 0f), (.16f, -.2f, .04f), (.2f, -depth * .35f, .12f), (.42f, -depth * .4f, .1f), (.48f, -depth * .78f, .14f), (.7f, -depth * .82f, .12f), (.78f, -depth, .1f),
        };
        int sideStart = verts.Count;
        float arcScale = radius * Mathf.PI * 2 / sides;
        for (int r = 0; r < rings.Length; r++)
            for (int i = 0; i <= sides; i++)
            {
                float a = i % sides / (float)sides * Mathf.PI * 2;
                float y = r == 0 ? lift[i] * .5f : rings[r].y + Noise(a, 4f, 60 + r * 7) * rings[r].jag * 2f;
                var v = Ring(i, edge[i] + rings[r].grow + Noise(a, 3.5f, 80 + r * 5) * rings[r].jag * 2.4f, y);
                verts.Add(v); uvs.Add(new Vector2(i * arcScale / 4f, y / 4f));
            }
        for (int r = 0; r < rings.Length - 1; r++)
            for (int i = 0; i < sides; i++)
            {
                int a = sideStart + r * (sides + 1) + i, b = a + 1, c = a + sides + 1, d = c + 1;
                side.AddRange(new[] { a, b, c, b, d, c });
            }
        var mesh = new Mesh { name = "Campaign base", hideFlags = HideFlags.DontSave, subMeshCount = 2 };
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs);
        mesh.SetTriangles(top, 0); mesh.SetTriangles(side, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // A place that can't be entered yet shows only its bare base (a clearing the roads meet at): its props and glow are
    // off and it can't be picked. Each prop's own on/off is remembered, so props switched off by hand stay off.
    readonly Dictionary<Transform, bool> propsOn = new();
    public bool PropsShown { get; private set; } = true;
    public void ShowProps(bool show)
    {
        if (show == PropsShown) return;
        PropsShown = show;
        foreach (Transform child in transform)
        {
            if (child.name == "Base") continue;
            if (!show) { propsOn[child] = child.gameObject.activeSelf; child.gameObject.SetActive(false); }
            else child.gameObject.SetActive(!propsOn.TryGetValue(child, out var on) || on);
        }
        var box = GetComponent<BoxCollider>(); if (box != null) box.enabled = show;
    }

    // A trigger the size of the place, for picking it with the mouse.
    public void EnsurePickCollider()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) { box = gameObject.AddComponent<BoxCollider>(); box.isTrigger = true; }
        box.center = new Vector3(0, radius * .6f, 0);
        box.size = new Vector3(radius * 2.2f, radius * 1.6f, radius * 2.2f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, .8f, .3f, .8f);
        var c = transform.position; float r = radius * transform.lossyScale.x;
        for (int i = 0; i < 24; i++)
        {
            float a = i / 24f * Mathf.PI * 2, b = (i + 1) / 24f * Mathf.PI * 2;
            Gizmos.DrawLine(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r, c + new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * r);
        }
    }
}

// The base's materials, found once (they live with the map's other assets).
public static class CampaignBaseMaterials
{
    static Material top, rim;
    public static Material Top => top != null ? top : top = Resources.Load<Material>("Campaign base top");
    public static Material Rim => rim != null ? rim : rim = Resources.Load<Material>("Campaign base rim");
}

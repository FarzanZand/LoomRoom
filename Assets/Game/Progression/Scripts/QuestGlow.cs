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

    readonly List<GameObject> shells = new();
    Material material;
    bool shown;

    public void Show(bool on)
    {
        if (on && shells.Count == 0) Build();
        if (on == shown) return;
        shown = on;
        foreach (var s in shells) if (s != null) s.SetActive(on);
    }

    void Update()
    {
        if (!shown || material == null) return;
        float k = .5f + .5f * Mathf.Sin(Time.time * 3f);
        material.color = Color.Lerp(colour, Color.white, .45f * k);
        material.SetFloat("_Width", width * (.85f + .3f * k));
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

    static Material[] Fill(Material m, int n) { var a = new Material[n]; for (int i = 0; i < n; i++) a[i] = m; return a; }
    static float Volume(Bounds b) => b.size.x * b.size.y * b.size.z;

    // A copy of the mesh whose normals are averaged over every vertex at the same place.
    static Mesh Smoothed(Mesh source)
    {
        Vector3[] v;
        try { v = source.vertices; } catch { return null; }
        var n = source.normals;
        if (n == null || n.Length != v.Length) return null;
        var sums = new Dictionary<Vector3Int, Vector3>();
        Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 1000f);
        for (int i = 0; i < v.Length; i++) { var k = Key(v[i]); sums[k] = (sums.TryGetValue(k, out var s) ? s : Vector3.zero) + n[i]; }
        var smooth = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) smooth[i] = sums[Key(v[i])].normalized;
        var mesh = Instantiate(source);
        mesh.name = source.name + " outline";
        mesh.normals = smooth;
        return mesh;
    }

    void OnDestroy()
    {
        foreach (var s in shells) if (s != null) { Destroy(s.GetComponent<MeshFilter>().sharedMesh); Destroy(s); }
        if (material != null) Destroy(material);
    }
}

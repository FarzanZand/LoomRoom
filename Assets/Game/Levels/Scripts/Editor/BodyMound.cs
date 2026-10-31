using UnityEditor;
using UnityEngine;

// A dead body lying as a prop (the Realms' dead dwarves) is walked over like a little hill, not blocked by (the user,
// 2026-10-30): instead of a box as tall as the body, it gets a low smooth mound collider over its footprint, gentle
// enough for the player (step 0.3 m, slope 45) and the enemies' NavMesh to climb. The mound's mesh is one shared asset,
// a dome over a unit square footprint; each body scales it to its own size. Used by Tools/realms_props.eval.cs as it
// makes the bodies and by Tools/body_mounds.eval.cs on the prefabs already made.
public static class BodyMound
{
    public const string MeshPath = "Assets/Game/Levels/Shared/Props/Body mound.asset";
    // Low enough that its steepest point (about 1.5 x height / half the footprint) stays well under 45 degrees on a body
    // 1.3 m across, the narrowest one.
    public const float Height = .3f;

    // Puts the mound on the body (its footprint the given box, in the holder's own space) and takes away every other
    // collider on it.
    public static void Apply(GameObject holder, Vector3 footprintCentre, Vector2 footprintSize)
    {
        foreach (var c in holder.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c, true);
        var old = holder.transform.Find("Mound");
        if (old != null) Object.DestroyImmediate(old.gameObject, true);
        var mound = new GameObject("Mound");
        mound.layer = holder.layer;
        mound.transform.SetParent(holder.transform, false);
        mound.transform.localPosition = new Vector3(footprintCentre.x, 0f, footprintCentre.z);
        mound.transform.localScale = new Vector3(footprintSize.x, Height, footprintSize.y);
        mound.AddComponent<MeshCollider>().sharedMesh = Mesh();
    }

    // The shared dome: x and z from -.5 to .5, height (1 + cos(pi r)) / 2 of the distance r from the middle (1 there,
    // 0 at the edge and beyond, a flat skirt in the corners), so it rises from the floor with no step. Saved readable:
    // the NavMesh built with each floor reads the collider's mesh.
    public static Mesh Mesh()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh != null) return mesh;
        const int n = 16;
        var verts = new Vector3[(n + 1) * (n + 1)];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float u = (float)x / n - .5f, v = (float)z / n - .5f;
                float r = Mathf.Min(1f, Mathf.Sqrt(u * u + v * v) * 2f);
                verts[z * (n + 1) + x] = new Vector3(u, (1f + Mathf.Cos(Mathf.PI * r)) * .5f, v);
            }
        var tris = new int[n * n * 6];
        for (int z = 0, t = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n + 1; tris[t++] = i + n + 2;
            }
        mesh = new Mesh { name = "Body mound", vertices = verts, triangles = tris };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, MeshPath);
        return mesh;
    }
}

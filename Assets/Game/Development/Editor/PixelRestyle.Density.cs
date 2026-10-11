using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Texel density: how many texture pixels cover one world unit on a renderer, so pixels can be kept the same size
// across walls, floors, props and characters.
public static partial class PixelRestyle
{
    // Median texels per world unit that material shows on this renderer (its own submeshes only), with the renderer's
    // real size: skinned meshes are measured in their current pose and scale.
    public static float TexelsPerUnit(Renderer r, Material mat)
    {
        var tex = mat != null ? MainTexture(mat) : null;
        var mf = r.GetComponent<MeshFilter>(); Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : (mf != null ? mf.sharedMesh : null);
        if (mesh == null || tex == null) return 0f;
        var mats = r.sharedMaterials; var subs = new List<int>();
        for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++) if (mats[i] == mat) subs.Add(i);
        if (subs.Count == 0) return 0f;
        Vector3[] v; Matrix4x4 m;
        if (r is SkinnedMeshRenderer skin)
        {
            var baked = new Mesh(); skin.BakeMesh(baked, true); v = baked.vertices; Object.DestroyImmediate(baked);
            m = Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
        }
        else { v = mesh.vertices; m = r.transform.localToWorldMatrix; }
        var uv = new List<Vector2>(); mesh.GetUVs(0, uv);
        if (uv.Count != v.Length) return 0f;
        var scale = mat.HasProperty("_BaseMap") ? mat.GetTextureScale("_BaseMap") : Vector2.one;
        var vals = new List<float>();
        foreach (var sub in subs)
        {
            var t = mesh.GetTriangles(sub);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                float world = Vector3.Cross(b - a, c - a).magnitude * .5f; if (world < 1e-8f) continue;
                Vector2 ua = Vector2.Scale(uv[t[i]], scale), ub = Vector2.Scale(uv[t[i + 1]], scale), uc = Vector2.Scale(uv[t[i + 2]], scale);
                float uvArea = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y)) * .5f * tex.width * tex.height;
                if (uvArea < 1e-6f) continue;
                vals.Add(Mathf.Sqrt(uvArea / world));
            }
        }
        if (vals.Count == 0) return 0f;
        vals.Sort(); return vals[vals.Count / 2];
    }

    // Median texels per world unit over the renderer's triangles (area-weighted samples), for its first texture.
    public static float TexelsPerUnit(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>(); Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : (mf != null ? mf.sharedMesh : null);
        var mat = r.sharedMaterial; var tex = mat != null ? MainTexture(mat) : null;
        if (mesh == null || tex == null) return 0f;
        var scale = mat.HasProperty("_BaseMap") ? mat.GetTextureScale("_BaseMap") : Vector2.one;
        var v = mesh.vertices; var uv = new List<Vector2>(); mesh.GetUVs(0, uv); var t = mesh.triangles;
        if (uv.Count != v.Length) return 0f;
        var m = r.transform.localToWorldMatrix; var vals = new List<float>();
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
            float world = Vector3.Cross(b - a, c - a).magnitude * .5f; if (world < 1e-8f) continue;
            Vector2 ua = Vector2.Scale(uv[t[i]], scale), ub = Vector2.Scale(uv[t[i + 1]], scale), uc = Vector2.Scale(uv[t[i + 2]], scale);
            float uvArea = Mathf.Abs((ub.x - ua.x) * (uc.y - ua.y) - (uc.x - ua.x) * (ub.y - ua.y)) * .5f * tex.width * tex.height;
            if (uvArea < 1e-6f) continue;
            vals.Add(Mathf.Sqrt(uvArea / world));
        }
        if (vals.Count == 0) return 0f;
        vals.Sort(); return vals[vals.Count / 2];
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Measures the texel size of everything shown under a scene object against its world's density, so art is checked by
// number before it is shown (run from an eval). Every submesh is measured on its own: UV area x texture size x tiling
// against its world area. Glows, flames, glass and text are left out.
public static class TexelCheck
{
    public const float Bedroom = 6.4f;

    public static string Scene(string rootPath = "World/Apartment/Main Bedroom", float target = Bedroom, float tolerance = .1f)
    {
        var root = GameObject.Find(rootPath);
        if (root == null) return "no object " + rootPath;
        var sb = new StringBuilder(); int checkedCount = 0, off = 0;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!r.TryGetComponent<MeshFilter>(out var mf) || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var mesh = mf.sharedMesh; var mats = r.sharedMaterials;
            for (int sm = 0; sm < mesh.subMeshCount && sm < mats.Length; sm++)
            {
                var m = mats[sm];
                if (m == null || Skipped(m) || !m.HasProperty("_BaseMap")) continue;
                var tex = m.GetTexture("_BaseMap"); if (tex == null) continue;
                float tpu = Measure(r.transform, mesh, sm, tex, m.GetTextureScale("_BaseMap"));
                if (tpu <= 0) continue;
                checkedCount++;
                if (Mathf.Abs(tpu / target - 1) <= tolerance) continue;
                off++; sb.AppendLine($"{tpu,7:F2}  {Path(r.transform)}  [{m.name}, {tex.width}x{tex.height}]");
            }
        }
        return $"{checkedCount} measured at {target} texels a unit, {off} more than {tolerance:P0} off" + (off > 0 ? ":\n" + sb : "");
    }

    static bool Skipped(Material m)
    {
        var n = m.name;
        return n.Contains("Glow") || n.Contains("Glass") || n.Contains("flame") || n.Contains("Flame") || n.Contains("pewter")
            || n.Contains("Character sheet") || m.shader.name.Contains("TextMesh");
    }

    public static float Measure(Transform t, Mesh mesh, int subMesh, Texture tex, Vector2 tiling)
    {
        var v = mesh.vertices; var uv = mesh.uv; if (uv.Length == 0) return 0;
        var tri = mesh.GetTriangles(subMesh); double world = 0, texels = 0;
        for (int i = 0; i < tri.Length; i += 3)
        {
            Vector3 a = t.TransformPoint(v[tri[i]]), b = t.TransformPoint(v[tri[i + 1]]), c = t.TransformPoint(v[tri[i + 2]]);
            world += Vector3.Cross(b - a, c - a).magnitude * .5;
            Vector2 p = uv[tri[i + 1]] - uv[tri[i]], q = uv[tri[i + 2]] - uv[tri[i]];
            texels += System.Math.Abs(p.x * q.y - p.y * q.x) * .5 * tex.width * tiling.x * tex.height * tiling.y;
        }
        return world <= 1e-9 ? 0 : (float)System.Math.Sqrt(texels / world);
    }

    static string Path(Transform x)
    {
        var p = x.name; int d = 0;
        while (x.parent != null && d++ < 2) { x = x.parent; p = x.name + "/" + p; }
        return p;
    }
}

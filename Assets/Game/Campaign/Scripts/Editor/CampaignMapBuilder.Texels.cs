using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// The map's texel size: fitting the tiled looks to it, and checking every renderer against it.
public static partial class CampaignMapBuilder
{
    // The road's and the bases' textures tile over their meshes: their tiling is set from a measurement so they land on
    // the map's density exactly (saved on their materials). Run after Looks (Build does).
    public static string FitTiling()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var sb = new StringBuilder();
        try
        {
            var done = new HashSet<Material>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool road = r.GetComponent<CampaignPath>() != null, baseMesh = r.name == "Base";
                if (!road && !baseMesh) continue;
                if (r.GetComponent<MeshFilter>()?.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || done.Contains(m) || !m.HasProperty("_BaseMap")) continue;
                    done.Add(m);
                    // Measure this submesh's material alone: a renderer with just it.
                    var probe = new GameObject("Tiling probe") { hideFlags = HideFlags.HideAndDontSave };
                    probe.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                    probe.transform.localScale = r.transform.lossyScale;
                    var mesh = Object.Instantiate(r.GetComponent<MeshFilter>().sharedMesh);
                    if (mesh.subMeshCount > 1) { var tris = mesh.GetTriangles(i); mesh.subMeshCount = 1; mesh.SetTriangles(tris, 0); }
                    probe.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var pr = probe.AddComponent<MeshRenderer>(); pr.sharedMaterial = m;
                    float measured = PixelRestyle.TexelsPerUnit(pr);
                    Object.DestroyImmediate(probe); Object.DestroyImmediate(mesh);
                    if (measured <= 0f) continue;
                    var scale = m.GetTextureScale("_BaseMap") * (MapTexelsPerUnit / measured);
                    m.SetTextureScale("_BaseMap", scale);
                    EditorUtility.SetDirty(m);
                    sb.Append($"{m.name} {measured:0.#} -> {MapTexelsPerUnit}; ");
                }
            }
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    // A renderer's first material measured on its own triangles (a two-material mesh, like a place's base, would
    // otherwise be measured with the first material over every triangle).
    static float MeasureFirstSubmesh(Renderer r)
    {
        if (r is not MeshRenderer || r.GetComponent<MeshFilter>().sharedMesh.subMeshCount < 2) return PixelRestyle.TexelsPerUnit(r);
        var probe = new GameObject("Texel probe") { hideFlags = HideFlags.HideAndDontSave };
        probe.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
        probe.transform.localScale = r.transform.lossyScale;
        var mesh = Object.Instantiate(r.GetComponent<MeshFilter>().sharedMesh);
        var tris = mesh.GetTriangles(0); mesh.subMeshCount = 1; mesh.SetTriangles(tris, 0);
        probe.AddComponent<MeshFilter>().sharedMesh = mesh;
        probe.AddComponent<MeshRenderer>().sharedMaterial = r.sharedMaterial;
        float t = PixelRestyle.TexelsPerUnit(probe.GetComponent<MeshRenderer>());
        Object.DestroyImmediate(probe); Object.DestroyImmediate(mesh);
        return t;
    }

    // The texel check: measures every textured renderer on the map prefab (or any other prefab given) in texels per
    // local unit and lists the ones more than 20% off the target (the map's density by default). Run from an eval before
    // calling map art done: CampaignMapBuilder.CheckTexels(), or CheckTexels("<prefab>", <texels a unit>). Glass, glows
    // and flat-coloured materials (no texture), switched-off parts and editor previews are left out.
    public static string CheckTexels(string prefabPath = null, float target = MapTexelsPerUnit, float tolerance = .2f)
    {
        prefabPath ??= PrefabPath;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return "no prefab " + prefabPath;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = Vector3.zero; instance.transform.localScale = Vector3.one;
        // The map is measured in its own units (it is laid on the table at scale 1).
        var off = new Dictionary<string, (float tpu, int count)>();
        int checkedCount = 0;
        try
        {
            // Only what is shown: switched-off parts (the enemy rig's other bodies) and editor-only previews are left out,
            // but the map's acts under the cloth are switched off on purpose and still count.
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (r.CompareTag("EditorOnly") || r.transform.parent != null && r.transform.parent.CompareTag("EditorOnly")) continue;
                if (!r.gameObject.activeSelf && r.GetComponentInParent<CampaignMap>(true) == null) continue;
                var m = r.sharedMaterial;
                if (m == null || m.name.Contains("Glass") || m.name.Contains("Glow") || r.name.Contains("Glow")) continue;
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                if (tex == null) continue;
                if (r is MeshRenderer && (r.GetComponent<MeshFilter>()?.sharedMesh == null)) continue;
                float tpu = MeasureFirstSubmesh(r);
                if (tpu <= 0f) continue;
                checkedCount++;
                if (Mathf.Abs(tpu / target - 1f) <= tolerance) continue;
                string key = $"{(r is SkinnedMeshRenderer s ? s.sharedMesh?.name : r.GetComponent<MeshFilter>().sharedMesh.name)} [{m.name}]";
                off[key] = off.TryGetValue(key, out var o) ? (o.tpu + tpu, o.count + 1) : (tpu, 1);
            }
        }
        finally { Object.DestroyImmediate(instance); }
        var sb = new StringBuilder($"{checkedCount} renderers checked against {target:0.#} texels a unit; {off.Count} kinds off: ");
        foreach (var kv in off) sb.Append($"{kv.Key} {kv.Value.tpu / kv.Value.count:0.#} x{kv.Value.count}; ");
        return sb.ToString();
    }
}

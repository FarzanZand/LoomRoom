using System.IO;
using UnityEditor;
using UnityEngine;

// The arena's figures at the dungeon world's pixel size (128 texels a unit): any shown renderer under a root that
// measures more than 10% off is baked again from its own mesh and materials (PixelRestyle.Build), measured, and baked
// again aimed by what came out until it is within 5%. Bakes go to Levels/Arena/Prefabs/Pixel and are kept.
public static partial class ArenaSetup
{
    const float WorldTexels = 128f;
    const string PixelBakes = Dir + "/Prefabs/Pixel";

    static int MatchTexels(GameObject root, string tag)
    {
        Directory.CreateDirectory(PixelBakes);
        int rebaked = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.gameObject.activeSelf) continue;
            var mf = r.GetComponent<MeshFilter>(); var skin = r as SkinnedMeshRenderer;
            var mesh = skin != null ? skin.sharedMesh : mf != null ? mf.sharedMesh : null;
            if (mesh == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_BaseMap") || r.sharedMaterial.GetTexture("_BaseMap") == null) continue;
            float measured = PixelRestyle.TexelsPerUnit(r);
            if (measured <= 0f || Mathf.Abs(measured / WorldTexels - 1f) <= .1f) continue;
            // Keyed by the source mesh and materials and the size it stands at (a cache hit is measured again below).
            float scale = r.transform.lossyScale.x;
            string name = $"{mesh.name.Replace(" (Synty Pixel)", "")} {tag} {CampaignMapBuilder.SourceKey(mesh, r.sharedMaterials)} x{scale:0.00}";
            string dir = $"{PixelBakes}/{name}";
            var bakedMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{name}.asset");
            var bakedMat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{name}.mat");
            var sourceMats = r.sharedMaterials;
            if (bakedMesh == null || bakedMat == null)
            {
                // The density the current texture gives, scaled to the target: the first aim; then measured again.
                float aim = WorldTexels;
                for (int pass = 0; pass < 4; pass++)
                {
                    (bakedMesh, bakedMat) = PixelRestyle.Build(mesh, sourceMats, skin != null ? "Characters" : "Props", name, aim, dir);
                    if (bakedMesh == null) break;
                    if (skin != null) skin.sharedMesh = bakedMesh; else mf.sharedMesh = bakedMesh;
                    r.sharedMaterials = new[] { bakedMat };
                    AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(bakedMat.GetTexture("_BaseMap")), ImportAssetOptions.ForceUpdate);
                    float now = PixelRestyle.TexelsPerUnit(r);
                    if (now <= 0f || Mathf.Abs(now / WorldTexels - 1f) < .05f) break;
                    aim *= WorldTexels / now;
                    // Bake from the original again, not from the last bake.
                    if (skin != null) skin.sharedMesh = mesh; else mf.sharedMesh = mesh;
                    r.sharedMaterials = sourceMats;
                }
            }
            if (bakedMesh == null) continue;
            if (skin != null) skin.sharedMesh = bakedMesh; else mf.sharedMesh = bakedMesh;
            r.sharedMaterials = new[] { bakedMat };
            rebaked++;
        }
        return rebaked;
    }
}

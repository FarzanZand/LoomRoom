using System.IO;
using UnityEditor;
using UnityEngine;

// The arena's props: Synty models restyled in the pixel look (PixelRestyle's plain restyle: their own colours, grain,
// baked creases) at the dungeon props' 128 texels a metre for the size they are used at, saved as prefabs in
// Levels/Arena/Prefabs. Kept once made; delete one to make it again.
public static partial class ArenaSetup
{
    const string PropDir = Dir + "/Prefabs";
    const float PropTexels = 128f;

    // A pixel copy of a Synty prefab, at the scale it will stand in the arena (its texels are sized for that).
    static GameObject Restyled(string syntyPath, string name, float scale)
    {
        if (!AssetDatabase.IsValidFolder(PropDir)) AssetDatabase.CreateFolder(Dir, "Prefabs");
        string path = $"{PropDir}/{name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var synty = AssetDatabase.LoadAssetAtPath<GameObject>(syntyPath);
        if (synty == null) { Debug.LogWarning("[Arena] no Synty prefab " + syntyPath); return null; }
        var copy = (GameObject)PrefabUtility.InstantiatePrefab(synty);
        PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        copy.name = name;
        try
        {
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var filter in copy.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null) continue;
                string meshName = filter.sharedMesh.name;
                // The arena's own bakes (the shared Art/Synty Pixel/Props copies are other tools' sources).
                string dir = $"{PropDir}/Pixel/{meshName} x{scale * filter.transform.lossyScale.x:0.00}";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{meshName}.asset");
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{meshName}.mat");
                if (mesh == null || mat == null)
                {
                    float world = scale * filter.transform.lossyScale.x;
                    (mesh, mat) = PixelRestyle.Build(filter.sharedMesh, renderer.sharedMaterials, "Props", meshName, PropTexels * world, dir);
                }
                if (mesh == null) continue;
                filter.sharedMesh = mesh;
                renderer.sharedMaterials = new[] { mat };
            }
            Directory.CreateDirectory(PropDir);
            return PrefabUtility.SaveAsPrefabAsset(copy, path);
        }
        finally { Object.DestroyImmediate(copy); }
    }
}

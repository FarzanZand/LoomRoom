using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Weapons and shields in the dirty pixel style (Docs/Pixel-texture-style.md): each item's Synty model is restyled
// with the Dirt grade, and every one is painted at the same density in the world (ItemTexelsPerUnit), so their pixels
// are the same size in the hand and on the floor. The starting sword (Bronze Sword) is SM_Wep_Sword_03.
public static partial class PixelRestyle
{
    // 0: the original restyle. 1: the dirty pixel style (stains, caked creases, chipped edges, stepped colour).
    public static float Dirt;
    // Texels per world unit for every item AND creature: the pixel size of the approved creature concepts (one painted
    // pixel ~6 px of a 1024 image across a ~1 m rat), Docs/Pixel-texture-style.md. Textures are sized exactly to it.
    public const float ItemTexelsPerUnit = 150f;
    const string StartingSword = "Assets/Game/Items/Data/Weapons/Bronze Sword.asset";
    const string StartingSwordModel = "Assets/Synty/PolygonDarkFantasy/Prefabs/Weapons/SM_Wep_Sword_03.prefab";

    [MenuItem("Tools/LoomRoom/Pixel Look/Restyle Weapons and Shields (dirty pixel)")]
    public static void RestyleItemsMenu() => Debug.Log("[PixelRestyle] " + RestyleWeaponsAndShields());

    public static string RestyleWeaponsAndShields()
    {
        var log = new System.Text.StringBuilder();
        var done = new Dictionary<string, (Mesh, Material)>();
        Dirt = 1f;
        try
        {
            foreach (var g in AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Game/Items/Data" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.Contains("_Archive")) continue;
                var item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
                if (item == null || item.worldPrefab == null || (item.itemType != ItemType.Weapon && item.itemType != ItemType.Shield)) continue;
                string prefabPath = AssetDatabase.GetAssetPath(item.worldPrefab);
                if (!prefabPath.StartsWith("Assets/Game/")) { log.AppendLine($"{item.name}: model is not a game prefab, skipped"); continue; }
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var r = mf.GetComponent<MeshRenderer>(); if (r == null || mf.sharedMesh == null) continue;
                        // The Synty original behind this model (restyled meshes keep the Synty name).
                        string synty = path == StartingSword ? StartingSwordModel : SyntyPrefabFor(mf.sharedMesh.name.Replace(" (Synty Pixel)", ""));
                        var source = synty != null ? AssetDatabase.LoadAssetAtPath<GameObject>(synty) : null;
                        var smf = source != null ? source.GetComponentInChildren<MeshFilter>(true) : null;
                        if (smf == null) { log.AppendLine($"{item.name}: no Synty source for {mf.sharedMesh.name}"); continue; }
                        var mats = smf.GetComponent<MeshRenderer>().sharedMaterials;
                        // The same density in the world, whatever the prefab's scale.
                        float scale = Mathf.Max(.01f, mf.transform.lossyScale.x);
                        string key = smf.sharedMesh.name + "|" + scale.ToString("0.###");
                        if (!done.TryGetValue(key, out var built))
                        {
                            built = Build(smf.sharedMesh, mats, item.itemType == ItemType.Shield ? "Shields" : "Weapons", smf.sharedMesh.name + " dirty", ItemTexelsPerUnit * scale);
                            done[key] = built;
                        }
                        if (built.Item1 == null) { log.AppendLine($"{item.name}: restyle failed"); continue; }
                        mf.sharedMesh = built.Item1;
                        r.sharedMaterials = new[] { built.Item2 };
                        log.AppendLine($"{item.name}: {smf.sharedMesh.name} at {ItemTexelsPerUnit * scale:0.#} texels per mesh unit");
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }
        finally { Dirt = 0f; }
        log.AppendLine(PixelLitConvert.Register());
        return log.ToString();
    }

    static string SyntyPrefabFor(string meshName)
    {
        foreach (var g in AssetDatabase.FindAssets(meshName + " t:Prefab", new[] { "Assets/Synty" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == meshName) return p;
        }
        return null;
    }
}

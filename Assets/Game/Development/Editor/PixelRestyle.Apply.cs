using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Finding the Synty models the game shows and swapping in their restyles. Only the game's own prefabs
// (item world models, level props) and scene objects are edited; Synty assets never are.
public static partial class PixelRestyle
{
    public static readonly string[] PropFolders =
        { "Assets/Game/Levels/Dungeon1/Prefabs", "Assets/Game/Levels/Dungeon1/Rooms/Templates", "Assets/Game/Levels/IntroDungeon", "Assets/Game/Levels/Shared" };

    // Item world models and level props.
    public static List<GameObject> GamePrefabs()
    {
        var list = new List<GameObject>();
        foreach (var g in AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Game/Items/Data" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g); if (p.Contains("_Archive")) continue;
            var d = AssetDatabase.LoadAssetAtPath<ItemData>(p);
            if (d.worldPrefab != null && !list.Contains(d.worldPrefab)) list.Add(d.worldPrefab);
        }
        foreach (var folder in PropFolders)
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (go != null && !list.Contains(go)) list.Add(go);
            }
        return list;
    }

    // A Synty mesh with its materials: items that share a mesh but not its colours each get their own restyle.
    public static string ComboKey(Mesh mesh, Material[] mats)
    {
        var names = new List<string>(); if (mats != null) foreach (var m in mats) names.Add(m != null ? m.name : "none");
        return mesh.name + "|" + string.Join(",", names);
    }

    public static Dictionary<string, (Mesh mesh, Material[] mats)> Combos()
    {
        var combos = new Dictionary<string, (Mesh, Material[])>();
        foreach (var go in GamePrefabs())
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh; if (m == null || !AssetDatabase.GetAssetPath(m).StartsWith("Assets/Synty")) continue;
                var r = mf.GetComponent<Renderer>(); var mats = r != null ? r.sharedMaterials : null;
                var key = ComboKey(m, mats); if (!combos.ContainsKey(key)) combos[key] = (m, mats);
            }
        return combos;
    }

    // Swaps every use of a restyled Synty mesh in the game prefabs. byCombo: ComboKey -> restyle. Returns renderers changed.
    public static int SwapIntoGame(Dictionary<string, (Mesh mesh, Material material)> byCombo)
    {
        int changed = 0;
        foreach (var go in GamePrefabs())
        {
            bool uses = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var rr = mf.GetComponent<Renderer>();
                if (mf.sharedMesh != null && byCombo.ContainsKey(ComboKey(mf.sharedMesh, rr != null ? rr.sharedMaterials : null))) { uses = true; break; }
            }
            if (!uses) continue;
            string path = AssetDatabase.GetAssetPath(go);
            if (!path.StartsWith("Assets/Game/")) { Debug.LogWarning($"[PixelRestyle] {path} is not a game prefab; give its users a copy in Assets/Game."); continue; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || !AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith("Assets/Synty")) continue;
                    var rr = mf.GetComponent<Renderer>();
                    if (!byCombo.TryGetValue(ComboKey(mf.sharedMesh, rr != null ? rr.sharedMaterials : null), out var restyle) || restyle.mesh == null) continue;
                    mf.sharedMesh = restyle.mesh;
                    if (rr != null) rr.sharedMaterials = new[] { restyle.material };
                    changed++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        return changed;
    }
}

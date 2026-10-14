using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Batch helpers for Tools/prop_structure.py: G-buffers for painted props, and a prop prefab that shows a mesh.
public static partial class PixelRestyle
{
    // For each "<mesh>": bakes Temp/Paint/Dungeon/gb/<mesh>.bin at its painted texture's size, with Synty colours.
    public static string BakeStructureBuffers(string[] meshes)
    {
        var log = new System.Text.StringBuilder();
        foreach (var m in meshes)
        {
            // The painted restyle, or the plain one where the prop was never painted.
            string d = $"{Root}/Props/{m}/{m} painted";
            if (!File.Exists(d + ".png")) d = $"{Root}/Props/{m}/{m}";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(d + ".png");
            if (tex == null) { log.AppendLine(m + ": no painted texture"); continue; }
            log.AppendLine(BakeGBuffer(d + ".asset", tex.width, tex.height, $"Temp/Paint/Dungeon/gb/{m}.bin", "synty:" + m));
        }
        File.WriteAllText("Temp/Paint/Dungeon/gb/batch.txt", log.ToString());
        return log.ToString();
    }

    // The first prefab under Assets/Game that uses this painted mesh.
    public static string PrefabUsing(string mesh)
    {
        string meshPath = $"{Root}/Props/{mesh}/{mesh} painted.asset";
        if (!File.Exists(meshPath)) meshPath = $"{Root}/Props/{mesh}/{mesh}.asset";
        foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Levels", "Assets/Game/Development" }))
        {
            var p = AssetDatabase.GUIDToAssetPath(g);
            foreach (var dep in AssetDatabase.GetDependencies(p, false)) if (dep == meshPath) return p;
        }
        return null;
    }

    public static string TurntablesFor(string[] meshes)
    {
        var log = new System.Text.StringBuilder();
        foreach (var m in meshes)
        {
            string tp = $"{Root}/Props/{m}/{m} painted.png"; if (!File.Exists(tp)) tp = $"{Root}/Props/{m}/{m}.png";
            AssetDatabase.ImportAsset(tp, ImportAssetOptions.ForceUpdate);
            var p = PrefabUsing(m);
            if (p == null) { log.AppendLine(m + ": no prefab"); continue; }
            RenderPropTurntable(p, $"Temp/Paint/Dungeon/turn/{m}.png");
            log.AppendLine(m + ": " + p);
        }
        File.WriteAllText("Temp/Paint/Dungeon/turn/batch.txt", log.ToString());
        return log.ToString();
    }
}

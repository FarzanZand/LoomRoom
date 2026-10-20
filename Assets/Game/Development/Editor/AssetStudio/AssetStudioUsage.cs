using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

// Asset Studio's "Where used": every prefab, scene, data asset, material and controller under Assets/Game that points
// straight at the asset. Unity keeps each asset's own dependencies; asking it for every candidate once and turning
// that around gives a map of who uses what (a second or two the first time, then instant until the library is
// refreshed).
public static class AssetStudioUsage
{
    static Dictionary<string, List<string>> usedBy;   // asset path -> paths that depend on it directly

    public static void Forget() => usedBy = null;

    public static List<(string path, AssetStudioCatalog.Entry entry)> Find(string guid, List<AssetStudioCatalog.Category> categories)
    {
        if (usedBy == null) Build();
        string own = AssetDatabase.GUIDToAssetPath(guid);
        var byPath = categories.SelectMany(c => c.entries).GroupBy(e => e.path).ToDictionary(g => g.Key, g => g.First());
        if (!usedBy.TryGetValue(own, out var users)) return new List<(string, AssetStudioCatalog.Entry)>();
        return users.Where(p => p != own)
            .Select(p => (p, byPath.TryGetValue(p, out var e) ? e : null))
            .OrderBy(f => f.Item2 != null ? 0 : f.p.EndsWith(".unity") ? 1 : 2).ThenBy(f => Path.GetFileName(f.p)).ToList();
    }

    static void Build()
    {
        usedBy = new Dictionary<string, List<string>>();
        var guids = AssetDatabase.FindAssets("t:Prefab t:Scene t:ScriptableObject t:Material t:AnimatorController", new[] { "Assets/Game" });
        foreach (var g in guids.Distinct())
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (path.Contains("/_Archive/")) continue;
            foreach (var dep in AssetDatabase.GetDependencies(path, false))
            {
                if (dep == path) continue;
                if (!usedBy.TryGetValue(dep, out var list)) usedBy[dep] = list = new List<string>();
                list.Add(path);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Each real place on the map is its own prefab in Map/Places, nested in the realm map, so it can be found in the Project
// window and opened alone. Its props are saved in that file; where it stands on the map, the ground under it, the roads
// and the scatter are the map's.
public static partial class CampaignMapBuilder
{
    public const string PlacesDir = MapDir + "/Places";
    public static string PlacePath(string place) => $"{PlacesDir}/{place}.prefab";

    // Turns every place that isn't a prefab yet into one (the act land markers stay plain). Safe to run again.
    public static string MakePlacePrefabs()
    {
        if (!AssetDatabase.IsValidFolder(PlacesDir)) AssetDatabase.CreateFolder(MapDir, "Places");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        var made = new List<string>();
        try
        {
            var points = root.GetComponent<CampaignMap>().points;
            foreach (Transform t in points)
            {
                if (t.GetComponent<CampaignPoint>() == null || t.name.Contains(" land (was ") || PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) continue;
                // Saved standing at its own middle; the map keeps where it stands.
                var pos = t.localPosition; var rot = t.localRotation;
                t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity;
                PrefabUtility.SaveAsPrefabAssetAndConnect(t.gameObject, PlacePath(t.name), InteractionMode.AutomatedAction);
                t.localPosition = pos; t.localRotation = rot;
                made.Add(t.name);
            }
            if (made.Count > 0) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        return made.Count > 0 ? "place prefabs: " + string.Join(", ", made) + "; " : "";
    }
}

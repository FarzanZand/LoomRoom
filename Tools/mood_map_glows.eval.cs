// Mood pass: a small warm glow at each place on the realm map (like lit windows in the mist), as a child of the place
// in Campaign/Map/Realm map.prefab, so it shows and hides with the place and the map's own light handling (off while
// the map grows, folds or a cloth is rolled back) takes it too. Safe to run again.
if (UnityEngine.Application.isPlaying) return "stop Play first";
const string path = "Assets/Game/Campaign/Map/Realm map.prefab";
var root = UnityEditor.PrefabUtility.LoadPrefabContents(path); int n = 0;
try
{
  foreach (var pt in root.GetComponentsInChildren<CampaignPoint>(true))
  {
    var old = pt.transform.Find("Place glow"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
    var go = new UnityEngine.GameObject("Place glow"); go.transform.SetParent(pt.transform, false); go.transform.localPosition = UnityEngine.Vector3.up * 1.6f / UnityEngine.Mathf.Max(.01f, pt.transform.lossyScale.y);
    var l = go.AddComponent<UnityEngine.Light>(); l.type = UnityEngine.LightType.Point; l.color = new UnityEngine.Color(1f, .6f, .28f); l.intensity = 5f; l.range = 7f; l.shadows = UnityEngine.LightShadows.None; n++;
  }
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
return $"{n} place glows";

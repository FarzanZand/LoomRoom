// Mood pass, in Campaign/Map/Realm map.prefab: a small warm glow at each place (like lit windows in the mist), a child
// of the place, so it shows and hides with it and the map's own light handling (off while the map grows, folds or a
// cloth is rolled back) takes it too; and the mist, four broad faint sheets of the main menu's drifting fog over the
// relief, a child of the map, so it is on the table only while the map is. Safe to run again.
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
  // the mist
  var map = root.GetComponent<CampaignMap>(); var oldMist = root.transform.Find("Mist"); if (oldMist) UnityEngine.Object.DestroyImmediate(oldMist.gameObject);
  var mist = new UnityEngine.GameObject("Mist").transform; mist.SetParent(root.transform, false);
  var src = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Game/UI/Main Menu/Fog/Fog low.mat");
  var quad = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Mesh>("Quad.fbx"); string dir = "Assets/Game/World/Art/Mood";
  foreach (var (h, a, sc) in new[] { (1.4f, .55f, 1f), (3.2f, .45f, 1.06f), (5.6f, .32f, .95f), (8.6f, .18f, .85f) })
  {
    string mp = $"{dir}/Map mist {h:0.#}.mat"; var mm = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(mp);
    if (!mm) { mm = new UnityEngine.Material(src); UnityEditor.AssetDatabase.CreateAsset(mm, mp); }
    mm.CopyPropertiesFromMaterial(src); mm.SetColor("_Color", new UnityEngine.Color(.9f, .91f, .93f, a)); mm.SetFloat("_TopFade", 0f);
    mm.SetVector("_Scale", new UnityEngine.Vector4(1.7f * sc, 1.3f * sc, 0, 0)); mm.SetVector("_Drift", new UnityEngine.Vector4(.008f, .003f, 0, 0));
    mm.SetFloat("_DepthFade", 3f); mm.SetFloat("_Threshold", .22f); mm.SetFloat("_Softness", .85f); mm.SetFloat("_Density", 2.2f); mm.SetVector("_EdgeFade", new UnityEngine.Vector4(.3f, .3f, 0, 0)); UnityEditor.EditorUtility.SetDirty(mm);
    var go = new UnityEngine.GameObject($"Mist {h:0.#}"); go.transform.SetParent(mist, false); go.transform.localPosition = new UnityEngine.Vector3(0, h, 0);
    go.transform.localRotation = UnityEngine.Quaternion.Euler(90, 0, 0); go.transform.localScale = new UnityEngine.Vector3(2 * map.halfX * sc, 2 * map.halfZ * sc, 1);
    go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = quad; var r = go.AddComponent<UnityEngine.MeshRenderer>(); r.sharedMaterial = mm; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
  }
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
return $"{n} place glows and the mist";

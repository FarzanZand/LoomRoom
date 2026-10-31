// A soft glow on the ceiling round each corridor lamp (as the Night lamp's glow lights the bedroom ceiling round its
// shade). The lamp's own light hangs under its diffuser and casts shadows, so the ceiling beside it stayed black, and a
// light inside the diffuser met the ceiling edge-on and lit only a tight hot ring: this is a shadowless warm spot pointing
// straight up from 10 units under the diffuser, wide enough to light the ceiling and the tops of the walls round the lamp
// and nothing below it. The lamp's child and a room light (LightingManager sceneSources), so it dims and goes out with the
// room. Safe to run again (the glow is found by name and set again).
if (UnityEngine.Application.isPlaying) return "playing";
const float GLOW = 1000f, RANGE = 90f, BELOW = 10f, ANGLE = 120f;
var sb = new System.Text.StringBuilder();
var lm = LightingManager.Instance;
UnityEditor.Undo.RecordObject(lm, "Corridor lamp glow");
var sources = new System.Collections.Generic.List<LightingManager.Source>(lm.sceneSources);
UnityEngine.Color warm; UnityEngine.ColorUtility.TryParseHtmlString("#FFC890", out warm);
foreach (var n in new[] { "World/Loop Corridors/Loop Corridor - Apartment Door", "World/Loop Corridors/Loop Corridor - East Door" })
foreach (var lampName in new[] { "Corridor lamp", "Corridor lamp far" })
{
  var lamp = UnityEngine.GameObject.Find(n)?.transform.Find(lampName);
  if (lamp == null) { sb.Append("missing " + n + "/" + lampName + "\n"); continue; }
  UnityEngine.Bounds b = default; bool any = false;
  foreach (var r in lamp.GetComponentsInChildren<UnityEngine.MeshRenderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
  var glow = lamp.Find("Corridor lamp glow");
  if (glow == null) { glow = new UnityEngine.GameObject("Corridor lamp glow").transform; glow.SetParent(lamp, false); UnityEditor.Undo.RegisterCreatedObjectUndo(glow.gameObject, "Corridor lamp glow"); }
  glow.SetPositionAndRotation(new UnityEngine.Vector3(b.center.x, b.min.y - BELOW, b.center.z), UnityEngine.Quaternion.LookRotation(UnityEngine.Vector3.up));
  var light = glow.GetComponent<UnityEngine.Light>(); if (light == null) light = glow.gameObject.AddComponent<UnityEngine.Light>();
  light.type = UnityEngine.LightType.Spot; light.spotAngle = ANGLE; light.innerSpotAngle = 60f; light.range = RANGE; light.intensity = GLOW; light.color = warm; light.shadows = UnityEngine.LightShadows.None;
  sources.RemoveAll(x => x == null || x.light == light);
  sources.Add(new LightingManager.Source { light = light, baseIntensity = GLOW, baseColor = warm });
  sb.Append($"{n.Substring(n.LastIndexOf('-') + 2)} / {lampName}: diffuser {b.min.y:F1}..{b.max.y:F1}, glow at {glow.position:F1}\n");
}
lm.sceneSources = sources.ToArray();
UnityEditor.EditorUtility.SetDirty(lm);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
return sb.ToString();

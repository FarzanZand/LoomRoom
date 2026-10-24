// His body's neck cap (one texel, under the Meshy head) shows as a pale crescent under his chin, between the beard and
// the neck: it takes the body texel nearest his beard's dark tone, so it reads as the beard's underside in shadow.
// Run after Tools/room_body_neckcap.eval.cs.
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var smr = bt.bodyB.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var m = smr.sharedMesh; var uv = m.uv; var tr = m.triangles; var mv = m.vertices; var bp = m.bindposes;
var png = new UnityEngine.Texture2D(2, 2); png.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap")))); int W = png.width, H = png.height; var px = png.GetPixels();
var target = new UnityEngine.Color(.13f, .10f, .09f); int pick = 0; float bd = 9;
for (int i = 0; i < px.Length; i++) { var c = px[i]; float dd = (c.r - target.r) * (c.r - target.r) + (c.g - target.g) * (c.g - target.g) + (c.b - target.b) * (c.b - target.b); if (dd < bd) { bd = dd; pick = i; } }
var dark = new UnityEngine.Vector2((pick % W + .5f) / W, (pick / W + .5f) / H);
int hb = System.Array.FindIndex(smr.bones, x => x.name == "head"), nb = System.Array.FindIndex(smr.bones, x => x.name == "neck_01");
var hP = bp[hb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); var nP = bp[nb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); float L = (hP - nP).magnitude;
var hit = new System.Collections.Generic.HashSet<int>(); int n = 0;
for (int t = 0; t < tr.Length; t += 3)
{
  if (!(uv[tr[t]] == uv[tr[t + 1]] && uv[tr[t]] == uv[tr[t + 2]])) continue;
  var cen = (mv[tr[t]] + mv[tr[t + 1]] + mv[tr[t + 2]]) / 3f; if ((cen - hP).magnitude > 1.3f * L) continue;
  var c = png.GetPixel((int)(uv[tr[t]].x * W), (int)(uv[tr[t]].y * H)); if (!(c.r > c.g * 1.05f && c.g > c.b)) continue;   // skin caps only
  hit.Add(tr[t]); hit.Add(tr[t + 1]); hit.Add(tr[t + 2]); n++;
}
foreach (var i in hit) uv[i] = dark; m.uv = uv; UnityEditor.EditorUtility.SetDirty(m); UnityEditor.AssetDatabase.SaveAssets();
return $"{n} cap triangles to {px[pick]}";

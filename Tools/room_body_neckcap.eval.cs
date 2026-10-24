// The flat caps closing the bodies' necks (fan patches, every corner on one UV) take a shadowed skin texel: lit from
// above they showed as a pale band under the chin.
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var s = "";
foreach (var body in new[] { bt.bodyA, bt.bodyB })
{
  var smr = body.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var m = smr.sharedMesh; var tp = UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap"));
  var png = new UnityEngine.Texture2D(2, 2); png.LoadImage(System.IO.File.ReadAllBytes(tp)); int W = png.width, H = png.height; var px = png.GetPixels();
  // the shadowed skin: skin-hued texels, the 15th percentile of brightness
  var sk = new System.Collections.Generic.List<(float, int)>();
  for (int i = 0; i < px.Length; i++) { var c = px[i]; if (c.r > c.g * 1.08f && c.g > c.b && c.r > c.b * 1.15f && c.g < c.b * 1.35f && c.grayscale > .25f) sk.Add((c.grayscale, i)); }
  sk.Sort((a, b2) => a.Item1.CompareTo(b2.Item1)); int pick = sk[sk.Count * 15 / 100].Item2; var dUv = new UnityEngine.Vector2((pick % W + .5f) / W, (pick / W + .5f) / H);
  var uv = m.uv; var tr = m.triangles; var mv = m.vertices; var bp = m.bindposes; int hb = System.Array.FindIndex(smr.bones, x => x.name == "head"), nb = System.Array.FindIndex(smr.bones, x => x.name == "neck_01");
  var hP = bp[hb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); var nP = bp[nb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); float rr = (hP - nP).magnitude * 1.3f;
  int n = 0; var hit = new System.Collections.Generic.HashSet<int>();
  for (int t = 0; t < tr.Length; t += 3) if (uv[tr[t]] == uv[tr[t + 1]] && uv[tr[t]] == uv[tr[t + 2]] && ((mv[tr[t]] + mv[tr[t + 1]] + mv[tr[t + 2]]) / 3f - hP).magnitude < rr && System.Math.Abs(UnityEngine.Vector3.Dot(UnityEngine.Vector3.Cross(mv[tr[t + 1]] - mv[tr[t]], mv[tr[t + 2]] - mv[tr[t]]).normalized, (hP - nP).normalized)) > .6f) {   // facing up the neck, not the patch on her back   // only the cap at the top of the neck
    hit.Add(tr[t]); hit.Add(tr[t + 1]); hit.Add(tr[t + 2]); n++; }
  foreach (var i in hit) uv[i] = dUv; m.uv = uv; UnityEditor.EditorUtility.SetDirty(m);
  s += $"{body.name}: {n} cap triangles to {px[pick]}\n";
}
UnityEditor.AssetDatabase.SaveAssets(); return s;

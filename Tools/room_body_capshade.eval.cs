// Her neck's cap fans (one texel each, near the neck) only ever show in the gaps under her hair, where they must read as
// deep shadow: they take a dark skin texel (the 3rd percentile of the skin's brightness). Lit edge-on, the 15th
// percentile showed as a pink streak at the nape.
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var smr = bt.bodyA.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var m = smr.sharedMesh; var uv = m.uv; var tr = m.triangles; var mv = m.vertices; var bp = m.bindposes;
var png = new UnityEngine.Texture2D(2, 2); png.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap")))); int W = png.width, H = png.height; var px = png.GetPixels();
var sk = new System.Collections.Generic.List<(float, int)>();
for (int i = 0; i < px.Length; i++) { var c = px[i]; if (c.r > c.g * 1.08f && c.g > c.b && c.r > c.b * 1.15f && c.g < c.b * 1.35f && c.grayscale > .12f) sk.Add((c.grayscale, i)); }
// the texel nearest her hair's shadow colour (the head's hair, darkened): the caps sit in the gaps under her hair, and
// where one meets a hair strand it must read as that strand
var target = new UnityEngine.Color(.30f, .16f, .11f); int pick = 0; float bd = 9;
for (int i = 0; i < px.Length; i++) { var c = px[i]; float dd = (c.r - target.r) * (c.r - target.r) + (c.g - target.g) * (c.g - target.g) + (c.b - target.b) * (c.b - target.b); if (dd < bd) { bd = dd; pick = i; } } var dark = new UnityEngine.Vector2((pick % W + .5f) / W, (pick / W + .5f) / H);
int hb = System.Array.FindIndex(smr.bones, x => x.name == "head"), nb = System.Array.FindIndex(smr.bones, x => x.name == "neck_01");
var hP = bp[hb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); var nP = bp[nb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); float L = (hP - nP).magnitude;
bool Shirt(UnityEngine.Vector2 u) { var c = png.GetPixel((int)(u.x * W), (int)(u.y * H)); return !(c.r > c.g * 1.06f && c.g > c.b && c.r > c.b * 1.15f); }
var hit = new System.Collections.Generic.HashSet<int>(); var back = new System.Collections.Generic.HashSet<int>(); int n = 0, nb2 = 0;
UnityEngine.Vector3 cen0(int t) => (mv[tr[t]] + mv[tr[t + 1]] + mv[tr[t + 2]]) / 3f;
// the shirt texel the flattened ponytail patch uses: the most common one-texel uv that is not skin
var cnt = new System.Collections.Generic.Dictionary<UnityEngine.Vector2, int>(); for (int t = 0; t < tr.Length; t += 3) if (uv[tr[t]] == uv[tr[t + 1]] && uv[tr[t]] == uv[tr[t + 2]] && Shirt(uv[tr[t]])) cnt[uv[tr[t]]] = cnt.TryGetValue(uv[tr[t]], out var c0) ? c0 + 1 : 1;
UnityEngine.Vector2? olive = null; int bc = 0; foreach (var kv in cnt) if (kv.Value > bc) { bc = kv.Value; olive = kv.Key; }
for (int t = 0; t < tr.Length; t += 3)
{
  if (!(uv[tr[t]] == uv[tr[t + 1]] && uv[tr[t]] == uv[tr[t + 2]])) continue; if (olive.HasValue && uv[tr[t]] == olive.Value) continue;   // the shirt patch on her back stays
  var d = (cen0(t) - nP) / L; if (d.magnitude > 1.6f) continue;
  if (d.z < -.25f && d.y < .4f) { if (olive.HasValue) { back.Add(tr[t]); back.Add(tr[t + 1]); back.Add(tr[t + 2]); nb2++; } continue; }   // the flattened ponytail on her back: shirt (the neck cap pass took these for caps)
  hit.Add(tr[t]); hit.Add(tr[t + 1]); hit.Add(tr[t + 2]); n++;
}
foreach (var i in hit) uv[i] = dark; foreach (var i in back) uv[i] = olive.Value; m.uv = uv; UnityEditor.EditorUtility.SetDirty(m); UnityEditor.AssetDatabase.SaveAssets();
return $"{n} cap triangles to {px[pick]}, {nb2} back patch triangles to shirt {olive}";

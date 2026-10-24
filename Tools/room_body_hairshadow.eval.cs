// Her body's leftovers round the neck (the cap fans and the skin triangles at the edge of the cut, which reach the
// shoulder by one corner) only ever show among her hair strands and in the gaps under her hair: they take a hair
// shadow colour painted as a 3 x 3 patch on an unused spot of her body texture.
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var smr = bt.bodyA.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var m = smr.sharedMesh; var tp = UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap"));
var png = new UnityEngine.Texture2D(2, 2); png.LoadImage(System.IO.File.ReadAllBytes(tp)); int W = png.width, H = png.height; var px = png.GetPixels();
var uv = m.uv; var tr = m.triangles; var mv = m.vertices; var bp = m.bindposes; var w = m.boneWeights;
int Bone(string n) => System.Array.FindIndex(smr.bones, x => x.name == n); int hb = Bone("head"), eb = Bone("eyes"), bb = Bone("eyebrows"), nb = Bone("neck_01");
var hP = bp[hb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); var nP = bp[nb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); float L = (hP - nP).magnitude;
float Wt(UnityEngine.BoneWeight b, System.Func<int, bool> f) => (f(b.boneIndex0) ? b.weight0 : 0) + (f(b.boneIndex1) ? b.weight1 : 0) + (f(b.boneIndex2) ? b.weight2 : 0) + (f(b.boneIndex3) ? b.weight3 : 0);
bool NK(int v) => Wt(w[v], i => i == hb || i == eb || i == bb || i == nb) > .5f;
bool Skin(UnityEngine.Color c) => c.r > c.g * 1.15f && c.g > c.b && c.r > c.b * 1.2f;   // the olive shirt is nearly as red as green: skin must be clearly redder
// an unused 5 x 5 spot: no triangle's uv covers it
var usedT = new bool[W * H];
for (int t = 0; t < tr.Length; t += 3) { var a = uv[tr[t]]; var b = uv[tr[t + 1]]; var c = uv[tr[t + 2]];
  int x0 = UnityEngine.Mathf.Max(0, (int)(UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, c.x)) * W) - 1), x1 = UnityEngine.Mathf.Min(W - 1, (int)(UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, c.x)) * W) + 1);
  int y0 = UnityEngine.Mathf.Max(0, (int)(UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, c.y)) * H) - 1), y1 = UnityEngine.Mathf.Min(H - 1, (int)(UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, c.y)) * H) + 1);
  for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) usedT[y * W + x] = true; }   // bounding boxes: generous, so the spot is surely free
int sx = -1, sy = -1;
for (int y = 2; y < H - 2 && sx < 0; y++) for (int x = 2; x < W - 2 && sx < 0; x++) { bool free = true; for (int dy = -2; dy <= 2 && free; dy++) for (int dx = -2; dx <= 2 && free; dx++) if (usedT[(y + dy) * W + x + dx]) free = false; if (free) { sx = x; sy = y; } }
var hairShadow = new UnityEngine.Color(.33f, .17f, .12f, 1);
{ var q = px[2 * W + 2]; if (UnityEngine.Mathf.Abs(q.r - hairShadow.r) < .01f && UnityEngine.Mathf.Abs(q.g - hairShadow.g) < .01f && UnityEngine.Mathf.Abs(q.b - hairShadow.b) < .01f) { sx = 2; sy = 2; }   // painted on an earlier run
  else { if (sx < 0) return "no free spot"; for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) px[(sy + dy) * W + sx + dx] = hairShadow;
    png.SetPixels(px); png.Apply(); System.IO.File.WriteAllBytes(tp, png.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(tp); } }
var spot = new UnityEngine.Vector2((sx + .5f) / W, (sy + .5f) / H);
// the triangles: the neck's caps (one texel each, near the neck, not the shirt patch on her back) and the skin triangles with two neck corners
var cnt = new System.Collections.Generic.Dictionary<UnityEngine.Vector2, int>(); for (int t = 0; t < tr.Length; t += 3) if (uv[tr[t]] == uv[tr[t + 1]] && uv[tr[t]] == uv[tr[t + 2]]) cnt[uv[tr[t]]] = cnt.TryGetValue(uv[tr[t]], out var c0) ? c0 + 1 : 1;
UnityEngine.Vector2 olive = default; int bc = 0; foreach (var kv in cnt) if (kv.Value > bc) { bc = kv.Value; olive = kv.Key; }
var verts = new System.Collections.Generic.List<UnityEngine.Vector3>(mv); var norms = new System.Collections.Generic.List<UnityEngine.Vector3>(m.normals); var uvs = new System.Collections.Generic.List<UnityEngine.Vector2>(uv); var ws = new System.Collections.Generic.List<UnityEngine.BoneWeight>(w);
var tris = new System.Collections.Generic.List<int>(tr); int caps = 0, edge = 0;
for (int t = 0; t < tris.Count; t += 3)
{
  int a = tris[t], b = tris[t + 1], c = tris[t + 2]; var cen = (verts[a] + verts[b] + verts[c]) / 3f; if ((cen - nP).magnitude > 1.6f * L) continue;
  bool cap = uvs[a] == uvs[b] && uvs[a] == uvs[c] && uvs[a] != olive;
  bool edgeSkin = false;
  if (!cap && a < w.Length && b < w.Length && c < w.Length && (NK(a) ? 1 : 0) + (NK(b) ? 1 : 0) + (NK(c) ? 1 : 0) >= 2) { var u = (uvs[a] + uvs[b] + uvs[c]) / 3f; edgeSkin = Skin(png.GetPixelBilinear(u.x, u.y)); }
  if (!cap && !edgeSkin) continue; if (cap) caps++; else edge++;
  for (int e = 0; e < 3; e++) { int o = tris[t + e]; tris[t + e] = verts.Count; verts.Add(verts[o]); norms.Add(norms[o]); uvs.Add(spot); ws.Add(ws[o]); }
}
m.SetVertices(verts); m.SetNormals(norms); m.SetUVs(0, uvs); m.boneWeights = ws.ToArray(); m.SetTriangles(tris, 0); m.RecalculateBounds();
UnityEditor.EditorUtility.SetDirty(m); UnityEditor.AssetDatabase.SaveAssets();
return $"spot ({sx},{sy}) of {W}x{H}; {caps} caps and {edge} edge skin triangles to hair shadow";

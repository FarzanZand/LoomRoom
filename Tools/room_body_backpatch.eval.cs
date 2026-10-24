// Her Synty ponytail was flattened onto her back (Tools/room_body_fix.eval.cs) with one shirt texel, so in the room's
// light it read as a smooth featureless slab. The patch's triangles get their own UVs, projected flat onto her back at
// the body's texel size, into a free spot of the body texture filled with a block of the shirt's own fabric copied from
// the texture in 4 x 4 blocks. Run after room_body_hairshadow.eval.cs.
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var smr = bt.bodyA.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var m = smr.sharedMesh; var uv = m.uv; var tr = m.triangles; var mv = m.vertices;
string tp = UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap"));
var png = new UnityEngine.Texture2D(2, 2); png.LoadImage(System.IO.File.ReadAllBytes(tp)); int W = png.width, Hh = png.height; var px = png.GetPixels();
// the patch: the most common one-texel uv (the flattened ponytail; the neck caps use other texels)
var cnt = new System.Collections.Generic.Dictionary<UnityEngine.Vector2, int>();
for (int t = 0; t < tr.Length; t += 3) if (uv[tr[t]] == uv[tr[t + 1]] && uv[tr[t]] == uv[tr[t + 2]]) cnt[uv[tr[t]]] = cnt.TryGetValue(uv[tr[t]], out var c0) ? c0 + 1 : 1;
UnityEngine.Vector2 olive = default; int bc = 0; foreach (var kv in cnt) if (kv.Value > bc) { bc = kv.Value; olive = kv.Key; }
var patch = new System.Collections.Generic.List<int>(); for (int t = 0; t < tr.Length; t += 3) if (uv[tr[t]] == olive && uv[tr[t + 1]] == olive && uv[tr[t + 2]] == olive) patch.Add(t);
if (patch.Count < 10) return $"no patch ({patch.Count} triangles at {olive})";
// texels per mesh unit: the body's measured density
float tpw = TexelCheck.Measure(smr.transform, m, 0, smr.sharedMaterial.GetTexture("_BaseMap"), UnityEngine.Vector2.one); float T = tpw * smr.transform.lossyScale.x;
// flat on her back: x across, y up (the mesh's bind space faces +z)
float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f; var used = new System.Collections.Generic.HashSet<int>();
foreach (var t in patch) for (int e = 0; e < 3; e++) { var p = mv[tr[t + e]]; x0 = UnityEngine.Mathf.Min(x0, p.x); x1 = UnityEngine.Mathf.Max(x1, p.x); y0 = UnityEngine.Mathf.Min(y0, p.y); y1 = UnityEngine.Mathf.Max(y1, p.y); used.Add(tr[t + e]); }
int pw = UnityEngine.Mathf.CeilToInt((x1 - x0) * T) + 1, ph = UnityEngine.Mathf.CeilToInt((y1 - y0) * T) + 1;
// a free spot: no other triangle's uv box reaches it (with 2 texels round it)
var busy = new bool[W * Hh];
for (int t = 0; t < tr.Length; t += 3)
{
  if (uv[tr[t]] == uv[tr[t + 1]] && uv[tr[t]] == uv[tr[t + 2]]) { int sx = (int)(uv[tr[t]].x * W), sy = (int)(uv[tr[t]].y * Hh); for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int xx = sx + dx, yy = sy + dy; if (xx >= 0 && yy >= 0 && xx < W && yy < Hh) busy[yy * W + xx] = true; } continue; }
  var a = uv[tr[t]]; var b = uv[tr[t + 1]]; var c = uv[tr[t + 2]];
  int bx0 = UnityEngine.Mathf.Max(0, (int)(UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, c.x)) * W) - 1), bx1 = UnityEngine.Mathf.Min(W - 1, (int)(UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, c.x)) * W) + 1);
  int by0 = UnityEngine.Mathf.Max(0, (int)(UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, c.y)) * Hh) - 1), by1 = UnityEngine.Mathf.Min(Hh - 1, (int)(UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, c.y)) * Hh) + 1);
  for (int y = by0; y <= by1; y++) for (int x = bx0; x <= bx1; x++) busy[y * W + x] = true;
}
int fx = -1, fy = -1;
for (int y = 0; y + ph + 4 <= Hh && fx < 0; y++) for (int x = 0; x + pw + 4 <= W && fx < 0; x++)
{ bool ok = true; for (int yy = y; yy < y + ph + 4 && ok; yy++) for (int xx = x; xx < x + pw + 4 && ok; xx++) if (busy[yy * W + xx]) ok = false; if (ok) { fx = x + 2; fy = y + 2; } }
if (fx < 0) return $"no free {pw + 4} x {ph + 4} spot in the {W} x {Hh} texture";
// the fabric: the run of shirt texels that size (all close to the shirt's median colour) with the most even colour
var shirt = png.GetPixel((int)(olive.x * W), (int)(olive.y * Hh));
bool Near(UnityEngine.Color q) => UnityEngine.Mathf.Abs(q.r - shirt.r) < .12f && UnityEngine.Mathf.Abs(q.g - shirt.g) < .12f && UnityEngine.Mathf.Abs(q.b - shirt.b) < .12f;
// every 4 x 4 window of shirt texels in the texture (all of them close to the shirt's colour), outside the new spot
var wins = new System.Collections.Generic.List<(int, int)>();
for (int y = 0; y + 4 <= Hh; y++) for (int x = 0; x + 4 <= W; x++)
{ bool ok = !(x + 4 > fx - 2 && x < fx + pw + 2 && y + 4 > fy - 2 && y < fy + ph + 2); for (int yy = y; yy < y + 4 && ok; yy++) for (int xx = x; xx < x + 4 && ok; xx++) if (!Near(px[yy * W + xx])) ok = false; if (ok) wins.Add((x, y)); }
if (wins.Count < 20) return $"only {wins.Count} shirt windows to copy from";
var rng = new System.Random(7);
for (int by = -1; by <= ph; by += 4) for (int bx = -1; bx <= pw; bx += 4)   // the spot, a one-texel border included, in 4 x 4 blocks of real fabric
{ var (wx, wy) = wins[rng.Next(wins.Count)]; for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) { int X = fx + bx + x, Y = fy + by + y; if (bx + x > pw || by + y > ph) continue; px[Y * W + X] = px[(wy + y) * W + wx + x]; } }
int sxB = wins.Count, syB = 0;
png.SetPixels(px); png.Apply(); System.IO.File.WriteAllBytes(tp, png.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(tp, UnityEditor.ImportAssetOptions.ForceUpdate);
foreach (var i in used) { var p = mv[i]; uv[i] = new UnityEngine.Vector2((fx + (p.x - x0) * T + .5f) / W, (fy + (p.y - y0) * T + .5f) / Hh); }
m.uv = uv; UnityEditor.EditorUtility.SetDirty(m); UnityEditor.AssetDatabase.SaveAssets();
return $"{patch.Count} patch triangles, {pw} x {ph} texels at ({fx},{fy}), fabric from {sxB} shirt windows, {T:F1} texels a mesh unit";

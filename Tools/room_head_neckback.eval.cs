// Her Meshy bust lost the back of its neck when the hair was cut to end below the jaw (on the bust it was hair), so the
// neck was open from behind and the inside of the face showed through. This adds a sleeve round the back of the neck: a
// half tube at the neck's own measured width (just inside the existing surface, so the two never fight where they meet),
// from inside the collar up into the hair, with its own UVs. The head's UVs are shrunk to UvScale of the square and the
// sleeve is unwrapped into the free strip along the top at the same texel size; Tools/room_head_neckback.py scales the
// bake to match and fills the strip with the bake's skin, so the views, painting and projection give the sleeve real,
// graded skin. Bone weights come from the nearest existing vertex. Run on the skinned head made by
// Tools/room_head_skin.eval.cs (restore it from Temp/RoomHead/backup), then room_head_neckback.py, room_head_swatches.py,
// and render, paint and project the head again.
if (UnityEngine.Application.isPlaying) return "playing";
const float UvScale = .85f;
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var sk = bt.headA.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var gt = sk.transform;
string path = UnityEditor.AssetDatabase.GetAssetPath(sk.sharedMesh); var m = sk.sharedMesh;
const int SourceVerts = 6799; if (m.vertexCount != SourceVerts) return "the head has " + m.vertexCount + " vertices, not the 6799 of the skinned bust: restore it from Temp/RoomHead/backup first";
var smr = bt.bodyA.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var hbT = System.Array.Find(smr.bones, x => x.name == "head");
float Hw = smr.bounds.size.y / 6.4f; float chinW = hbT.position.y - .24f * Hw;
var bp = m.bindposes; var nP = bp[0].inverse.MultiplyPoint(UnityEngine.Vector3.zero); var hP = bp[1].inverse.MultiplyPoint(UnityEngine.Vector3.zero);
var up = (hP - nP).normalized; var fw = gt.InverseTransformDirection(bt.transform.forward); fw -= up * UnityEngine.Vector3.Dot(fw, up); fw.Normalize(); var rt = UnityEngine.Vector3.Cross(up, fw).normalized;
float H = Hw / gt.lossyScale.y; float chin = UnityEngine.Vector3.Dot(gt.InverseTransformPoint(new UnityEngine.Vector3(hbT.position.x, chinW, hbT.position.z)) - nP, up);
var v = m.vertices; var nr = m.normals; var uv = m.uv; var bw = m.boneWeights; var tr = m.triangles;
float Hgt(UnityEngine.Vector3 p) => UnityEngine.Vector3.Dot(p - nP, up);
UnityEngine.Vector2 Polar(UnityEngine.Vector3 p) { var d = p - nP; return new UnityEngine.Vector2(UnityEngine.Vector3.Dot(d, fw), UnityEngine.Vector3.Dot(d, rt)); }
float yMin = 1e9f; foreach (var p in v) yMin = UnityEngine.Mathf.Min(yMin, Hgt(p));
float yTop = chin + .2f * H, yBot = yMin + .02f * H, yNeck = chin - .05f * H;
// the neck's half width at each height, from the vertices on its sides (60-120 degrees from the front, either side)
const int Bands = 12; var wid = new float[Bands + 1]; var wn = new int[Bands + 1];
for (int i = 0; i < v.Length; i++)
{
  float y = Hgt(v[i]); if (y < yBot || y > yNeck) continue; var q = Polar(v[i]); float ang = UnityEngine.Mathf.Abs(UnityEngine.Mathf.Atan2(q.y, q.x) * UnityEngine.Mathf.Rad2Deg); if (ang < 60 || ang > 120) continue;
  int b = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt((y - yBot) / (yNeck - yBot) * Bands), 0, Bands); wid[b] += q.magnitude; wn[b]++;
}
for (int b = 0; b <= Bands; b++) wid[b] = wn[b] > 0 ? wid[b] / wn[b] : -1;
for (int b = 0; b <= Bands; b++) if (wid[b] < 0) { int lo = b, hi = b; while (lo >= 0 && wid[lo] < 0) lo--; while (hi <= Bands && wid[hi] < 0) hi++; wid[b] = lo >= 0 && hi <= Bands ? (wid[lo] + wid[hi]) / 2 : lo >= 0 ? wid[lo] : wid[hi]; }
float Width(float y) { float t = UnityEngine.Mathf.Clamp01((y - yBot) / (yNeck - yBot)) * Bands; int b0 = UnityEngine.Mathf.Min(Bands, UnityEngine.Mathf.FloorToInt(t)); int b1 = UnityEngine.Mathf.Min(Bands, b0 + 1); return UnityEngine.Mathf.Lerp(wid[b0], wid[b1], t - b0); }
// UV size per mesh unit (the head's own texel density, after the shrink)
double wa = 0, ua = 0; for (int t = 0; t < tr.Length; t += 3) { wa += UnityEngine.Vector3.Cross(v[tr[t + 1]] - v[tr[t]], v[tr[t + 2]] - v[tr[t]]).magnitude * .5; var a2 = uv[tr[t + 1]] - uv[tr[t]]; var b2 = uv[tr[t + 2]] - uv[tr[t]]; ua += System.Math.Abs(a2.x * b2.y - a2.y * b2.x) * .5; }
float uvPerUnit = (float)System.Math.Sqrt(ua / wa) * UvScale;
const int Seg = 16, Rings = 10; const float A0 = 70, A1 = 290;
float rMax = 0; for (int ri = 0; ri <= Rings; ri++) rMax = UnityEngine.Mathf.Max(rMax, Width(UnityEngine.Mathf.Min(UnityEngine.Mathf.Lerp(yBot, yTop, ri / (float)Rings), yNeck)) * .985f);
float arcU = (A1 - A0) * UnityEngine.Mathf.Deg2Rad * rMax * uvPerUnit, hV = (yTop - yBot) * uvPerUnit;
float u0 = .01f, v0 = UvScale + .01f;
if (u0 + arcU > .99f || v0 + hV > .99f) return "the sleeve's UVs (" + arcU.ToString("F3") + " x " + hV.ToString("F3") + ") do not fit the free strip: lower UvScale";
var nv = new System.Collections.Generic.List<UnityEngine.Vector3>(v); var nn = new System.Collections.Generic.List<UnityEngine.Vector3>(nr); var nb = new System.Collections.Generic.List<UnityEngine.BoneWeight>(bw); var nt = new System.Collections.Generic.List<int>(tr);
var nu = new System.Collections.Generic.List<UnityEngine.Vector2>(); foreach (var q in uv) nu.Add(q * UvScale);
int start = nv.Count;
for (int ri = 0; ri <= Rings; ri++)
{
  float y = UnityEngine.Mathf.Lerp(yBot, yTop, ri / (float)Rings); float r = Width(UnityEngine.Mathf.Min(y, yNeck)) * .985f;
  for (int si = 0; si <= Seg; si++)
  {
    float ang = UnityEngine.Mathf.Lerp(A0, A1, si / (float)Seg) * UnityEngine.Mathf.Deg2Rad; var dir = fw * UnityEngine.Mathf.Cos(ang) + rt * UnityEngine.Mathf.Sin(ang);
    var p = nP + up * y + dir * r; nv.Add(p); nn.Add(dir.normalized);
    nu.Add(new UnityEngine.Vector2(u0 + (ang - A0 * UnityEngine.Mathf.Deg2Rad) * rMax * uvPerUnit, v0 + (y - yBot) * uvPerUnit));
    int ni = 0; float nd = 1e9f; for (int i = 0; i < v.Length; i++) { float dd = (v[i] - p).sqrMagnitude; if (dd < nd) { nd = dd; ni = i; } } nb.Add(bw[ni]);
  }
}
void Tri(int i0, int i1, int i2) { var fn = UnityEngine.Vector3.Cross(nv[i1] - nv[i0], nv[i2] - nv[i0]); if (UnityEngine.Vector3.Dot(fn, nn[i0]) < 0) { nt.Add(i0); nt.Add(i2); nt.Add(i1); } else { nt.Add(i0); nt.Add(i1); nt.Add(i2); } }   // outward: Unity's face normal is cross(b - a, c - a)
for (int ri = 0; ri < Rings; ri++) for (int si = 0; si < Seg; si++)
{ int a = start + ri * (Seg + 1) + si, b = a + 1, c = a + Seg + 1, d = c + 1; Tri(a, b, d); Tri(a, d, c); }
var mesh = UnityEngine.Object.Instantiate(m); mesh.name = m.name;
mesh.SetVertices(nv); mesh.SetNormals(nn); mesh.SetUVs(0, nu); mesh.boneWeights = nb.ToArray(); mesh.SetTriangles(nt, 0); mesh.bindposes = bp; mesh.RecalculateBounds();
UnityEditor.AssetDatabase.DeleteAsset(path); UnityEditor.AssetDatabase.CreateAsset(mesh, path); PixelRestyle.MarkReadable(mesh);
sk.sharedMesh = mesh; UnityEditor.EditorUtility.SetDirty(sk); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sk.gameObject.scene); UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/RoomHead/neckback.txt", UvScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
return "sleeve " + (Rings * Seg * 2) + " triangles from " + ((yBot - chin) / H).ToString("F2") + " to " + ((yTop - chin) / H).ToString("F2") + " head heights from the chin; UVs " + arcU.ToString("F3") + " x " + hV.ToString("F3") + " in the strip over " + UvScale;

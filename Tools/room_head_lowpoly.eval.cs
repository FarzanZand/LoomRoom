// Her head (the skinned bust with the back of the neck, Tools/room_head_neckback.eval.cs) cut down to a low-poly mesh
// like the Dungeon Master's faces. Step "export": writes Temp/RoomHead/lowpoly_in.obj (bind-space positions, UVs,
// normals) for Tools/room_head_decimate.py. Step "import" (Temp/RoomHead/lowpoly_step.txt holds the step): reads
// lowpoly_out.obj and rebuilds the skinned mesh; each new vertex takes the bone weights of the nearest old one (her
// weights blend smoothly from neck to head, so the nearest is right), the bind poses stay. Then run
// Tools/room_head_restyle.eval.cs.
if (UnityEngine.Application.isPlaying) return "playing";
var ci = System.Globalization.CultureInfo.InvariantCulture;
string step = System.IO.File.ReadAllText("Temp/RoomHead/lowpoly_step.txt").Trim();
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var sk = bt.headA.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var m = sk.sharedMesh;
var v = m.vertices; var n = m.normals; var uv = m.uv; var tr = m.triangles;
if (step == "export")
{
  var sb = new System.Text.StringBuilder();
  foreach (var p in v) sb.Append("v ").Append(p.x.ToString("R", ci)).Append(' ').Append(p.y.ToString("R", ci)).Append(' ').Append(p.z.ToString("R", ci)).Append('\n');
  foreach (var q in uv) sb.Append("vt ").Append(q.x.ToString("R", ci)).Append(' ').Append(q.y.ToString("R", ci)).Append('\n');
  foreach (var q in n) sb.Append("vn ").Append(q.x.ToString("R", ci)).Append(' ').Append(q.y.ToString("R", ci)).Append(' ').Append(q.z.ToString("R", ci)).Append('\n');
  for (int t = 0; t < tr.Length; t += 3) { sb.Append('f'); for (int e = 0; e < 3; e++) { int i = tr[t + e] + 1; sb.Append(' ').Append(i).Append('/').Append(i).Append('/').Append(i); } sb.Append('\n'); }
  System.IO.File.WriteAllText("Temp/RoomHead/lowpoly_in.obj", sb.ToString());
  return $"exported {v.Length} vertices, {tr.Length / 3} triangles";
}
// import: OBJ corners (position, uv, normal) become Unity vertices, one per distinct corner
var P = new System.Collections.Generic.List<UnityEngine.Vector3>(); var T = new System.Collections.Generic.List<UnityEngine.Vector2>(); var N = new System.Collections.Generic.List<UnityEngine.Vector3>();
var nv = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nu = new System.Collections.Generic.List<UnityEngine.Vector2>(); var nn = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nt = new System.Collections.Generic.List<int>();
var map = new System.Collections.Generic.Dictionary<string, int>();
float F(string s) => float.Parse(s, ci);
foreach (var line in System.IO.File.ReadAllLines("Temp/RoomHead/lowpoly_out.obj"))
{
  var a = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries); if (a.Length == 0) continue;
  if (a[0] == "v") P.Add(new UnityEngine.Vector3(F(a[1]), F(a[2]), F(a[3])));
  else if (a[0] == "vt") T.Add(new UnityEngine.Vector2(F(a[1]), F(a[2])));
  else if (a[0] == "vn") N.Add(new UnityEngine.Vector3(F(a[1]), F(a[2]), F(a[3])));
  else if (a[0] == "f")
  {
    if (a.Length != 4) return "not triangulated: " + line;
    for (int e = 1; e <= 3; e++)
    {
      if (!map.TryGetValue(a[e], out int idx))
      {
        var s = a[e].Split('/'); idx = nv.Count; map[a[e]] = idx;
        nv.Add(P[int.Parse(s[0]) - 1]); nu.Add(s.Length > 1 && s[1] != "" ? T[int.Parse(s[1]) - 1] : UnityEngine.Vector2.zero);
        var q = s.Length > 2 && s[2] != "" ? N[int.Parse(s[2]) - 1] : UnityEngine.Vector3.zero; nn.Add(q.sqrMagnitude > 1e-8f ? q.normalized : UnityEngine.Vector3.up);
      }
      nt.Add(idx);
    }
  }
}
var bw = m.boneWeights; var nb = new UnityEngine.BoneWeight[nv.Count];
for (int i = 0; i < nv.Count; i++) { int best = 0; float bd = 1e9f; for (int j = 0; j < v.Length; j++) { float d = (v[j] - nv[i]).sqrMagnitude; if (d < bd) { bd = d; best = j; } } nb[i] = bw[best]; }
var mesh = UnityEngine.Object.Instantiate(m); mesh.name = m.name; mesh.Clear();
mesh.SetVertices(nv); mesh.SetNormals(nn); mesh.SetUVs(0, nu); mesh.SetTriangles(nt, 0); mesh.boneWeights = nb; mesh.bindposes = m.bindposes; mesh.RecalculateBounds();
string path = UnityEditor.AssetDatabase.GetAssetPath(m).Replace(" skinned.asset", " lowpoly.asset");
UnityEditor.AssetDatabase.DeleteAsset(path); UnityEditor.AssetDatabase.CreateAsset(mesh, path); PixelRestyle.MarkReadable(mesh);
sk.sharedMesh = mesh; UnityEditor.EditorUtility.SetDirty(sk); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(sk.gameObject.scene); UnityEditor.AssetDatabase.SaveAssets();
return $"low-poly head: {nv.Count} vertices, {nt.Count / 3} triangles (was {tr.Length / 3}) at {path}";

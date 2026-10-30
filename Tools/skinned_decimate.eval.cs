// Cuts skinned meshes down in Blender and drops their blend shapes, the mesh assets kept in place (same GUID, so every
// prefab still points at them). Temp/Decimate/job.txt: the step on its first line, then one mesh asset path a line.
//   strip  : only the blend shapes go (no visible change; a Sidekick skeleton carried 68 unused face shapes).
//   export : writes Temp/Decimate/<n>_in.obj (positions, UVs, normals in the mesh's own space) per mesh, for
//            blender -b -P Tools/room_head_decimate.py -- <n>_in.obj <n>_out.obj <target triangles>
//   import : reads <n>_out.obj and rebuilds each mesh: one vertex per distinct corner, each taking the bone weights of
//            the nearest old vertex (weights blend smoothly over a body, so the nearest is right), the bind poses kept,
//            one submesh, no blend shapes. UVs come through Blender, so the restyle's painting still fits.
// Writes Temp/Decimate/report.txt. Edit mode only.
if (UnityEngine.Application.isPlaying) return "playing";
var ci = System.Globalization.CultureInfo.InvariantCulture;
var job = System.IO.File.ReadAllLines("Temp/Decimate/job.txt");
string step = job[0].Trim();
var report = new System.Text.StringBuilder();
for (int j = 1; j < job.Length; j++)
{
  string path = job[j].Trim(); if (path.Length == 0) continue;
  var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path);
  if (m == null) { report.Append($"MISSING {path}\n"); continue; }
  string n = System.IO.Path.GetFileNameWithoutExtension(path);
  int beforeV = m.vertexCount, beforeT = (int)(m.GetIndexCount(0) / 3), shapes = m.blendShapeCount;
  if (step == "strip")
  {
    m.ClearBlendShapes();
    UnityEditor.EditorUtility.SetDirty(m);
    report.Append($"{n}: {shapes} blend shapes dropped, {beforeV} vertices\n");
    continue;
  }
  if (step == "export")
  {
    var v = m.vertices; var nr = m.normals; var uv = m.uv; var tr = m.triangles;
    var sb = new System.Text.StringBuilder();
    foreach (var p in v) sb.Append("v ").Append(p.x.ToString("R", ci)).Append(' ').Append(p.y.ToString("R", ci)).Append(' ').Append(p.z.ToString("R", ci)).Append('\n');
    foreach (var q in uv) sb.Append("vt ").Append(q.x.ToString("R", ci)).Append(' ').Append(q.y.ToString("R", ci)).Append('\n');
    foreach (var q in nr) sb.Append("vn ").Append(q.x.ToString("R", ci)).Append(' ').Append(q.y.ToString("R", ci)).Append(' ').Append(q.z.ToString("R", ci)).Append('\n');
    for (int t = 0; t < tr.Length; t += 3) { sb.Append('f'); for (int e = 0; e < 3; e++) { int i = tr[t + e] + 1; sb.Append(' ').Append(i).Append('/').Append(i).Append('/').Append(i); } sb.Append('\n'); }
    System.IO.File.WriteAllText($"Temp/Decimate/{n}_in.obj", sb.ToString());
    report.Append($"{n}: exported {beforeV} vertices, {tr.Length / 3} triangles\n");
    continue;
  }
  // import
  string objPath = $"Temp/Decimate/{n}_out.obj";
  if (!System.IO.File.Exists(objPath)) { report.Append($"{n}: no {objPath}\n"); continue; }
  var P = new System.Collections.Generic.List<UnityEngine.Vector3>(); var T = new System.Collections.Generic.List<UnityEngine.Vector2>(); var N = new System.Collections.Generic.List<UnityEngine.Vector3>();
  var nv = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nu = new System.Collections.Generic.List<UnityEngine.Vector2>(); var nn = new System.Collections.Generic.List<UnityEngine.Vector3>();
  var tris = new System.Collections.Generic.List<int>();
  var map = new System.Collections.Generic.Dictionary<string, int>();
  float F(string s) => float.Parse(s, ci);
  foreach (var line in System.IO.File.ReadAllLines(objPath))
  {
    var a = line.Split(' ', System.StringSplitOptions.RemoveEmptyEntries); if (a.Length == 0) continue;
    if (a[0] == "v") P.Add(new UnityEngine.Vector3(F(a[1]), F(a[2]), F(a[3])));
    else if (a[0] == "vt") T.Add(new UnityEngine.Vector2(F(a[1]), F(a[2])));
    else if (a[0] == "vn") N.Add(new UnityEngine.Vector3(F(a[1]), F(a[2]), F(a[3])));
    else if (a[0] == "f")
      for (int e = 1; e <= 3; e++)
      {
        if (!map.TryGetValue(a[e], out int idx))
        {
          var k = a[e].Split('/');
          idx = nv.Count; map[a[e]] = idx;
          nv.Add(P[int.Parse(k[0]) - 1]);
          nu.Add(k.Length > 1 && k[1].Length > 0 ? T[int.Parse(k[1]) - 1] : UnityEngine.Vector2.zero);
          nn.Add(k.Length > 2 && k[2].Length > 0 ? N[int.Parse(k[2]) - 1] : UnityEngine.Vector3.up);
        }
        tris.Add(idx);
      }
  }
  // Weights from the nearest old vertex, found through a coarse grid.
  var oldV = m.vertices; var oldW = m.boneWeights; var bind = m.bindposes;
  float cell = .05f;
  var grid = new System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, System.Collections.Generic.List<int>>();
  UnityEngine.Vector3Int Key(UnityEngine.Vector3 p) => new UnityEngine.Vector3Int(UnityEngine.Mathf.FloorToInt(p.x / cell), UnityEngine.Mathf.FloorToInt(p.y / cell), UnityEngine.Mathf.FloorToInt(p.z / cell));
  for (int i = 0; i < oldV.Length; i++) { var key = Key(oldV[i]); if (!grid.TryGetValue(key, out var list)) grid[key] = list = new System.Collections.Generic.List<int>(); list.Add(i); }
  var weights = new UnityEngine.BoneWeight[nv.Count];
  for (int i = 0; i < nv.Count; i++)
  {
    var key = Key(nv[i]); int best = -1; float bestD = float.MaxValue;
    for (int r = 1; r <= 8 && best < 0; r++)
      for (int x = -r; x <= r; x++) for (int y = -r; y <= r; y++) for (int z = -r; z <= r; z++)
        if (grid.TryGetValue(key + new UnityEngine.Vector3Int(x, y, z), out var list))
          foreach (int o in list) { float d = (oldV[o] - nv[i]).sqrMagnitude; if (d < bestD) { bestD = d; best = o; } }
    if (best < 0) for (int o = 0; o < oldV.Length; o++) { float d = (oldV[o] - nv[i]).sqrMagnitude; if (d < bestD) { bestD = d; best = o; } }
    weights[i] = oldW[best];
  }
  m.Clear();
  m.indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
  m.SetVertices(nv); m.SetUVs(0, nu); m.SetNormals(nn);
  m.subMeshCount = 1; m.SetTriangles(tris, 0);
  m.boneWeights = weights; m.bindposes = bind;
  m.RecalculateBounds(); m.RecalculateTangents();
  UnityEditor.EditorUtility.SetDirty(m);
  report.Append($"{n}: {beforeV} -> {nv.Count} vertices, {beforeT} -> {tris.Count / 3} triangles, {shapes} blend shapes dropped\n");
}
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/Decimate/report.txt", report.ToString());
return "ok";

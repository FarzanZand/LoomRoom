// Body meshes for the Meshy heads: the Synty head (and the woman's hair) cut away, every hole left closed with a fan,
// and the neck slimmed toward its own axis (Synty necks carry Synty's oversized heads). Afterwards run, in this order,
// Tools/room_body_neckcap.eval.cs, room_body_capshade.eval.cs, room_body_hairshadow.eval.cs and room_body_capshade_man.eval.cs (her neck's caps and edge
// triangles otherwise show as pale flaps and slivers among her hair and a pink slab on her back).
if (UnityEngine.Application.isPlaying) return "playing";
var cfg = System.IO.File.ReadAllText("C:/Users/Farza/AppData/Local/Temp/claude/C--Game-projects-LoomRoom/79e85251-42df-4f8e-85b0-cecba42a4de5/scratchpad/face/bodyfix.txt").Trim().Split('|');
float neckF = float.Parse(cfg[0]); string only = cfg.Length > 1 ? cfg[1] : "";   // "A" or "B": rebuild that body alone
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var log = new System.Text.StringBuilder();
foreach (var (bodyGo, name, cutHair) in new[] { (bt.bodyA, "Character_Daughter_01", true), (bt.bodyB, "Character_Father_02", false) })
{
  if ((only == "A" && !cutHair) || (only == "B" && cutHair)) continue;
  var smr = bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>();
  string dir = "Assets/Game/Art/Synty Pixel/Characters/" + name + " (Characters)/";
  var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(dir + name + ".asset");
  var tp0 = dir + name + ".png"; var ti0 = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(tp0); if (!ti0.isReadable) { ti0.isReadable = true; ti0.SaveAndReimport(); }
  var tex0 = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(tp0); UnityEngine.Texture2D tex = cutHair ? tex0 : null;
  int Bone(string n) { for (int i = 0; i < smr.bones.Length; i++) if (smr.bones[i].name == n) return i; return -1; }
  int hb = Bone("head"), eb = Bone("eyes"), bb = Bone("eyebrows"), nb = Bone("neck_01");
  var w = mesh.boneWeights; var bp = mesh.bindposes; var verts = new System.Collections.Generic.List<UnityEngine.Vector3>(mesh.vertices); var norms = new System.Collections.Generic.List<UnityEngine.Vector3>(mesh.normals);
  var uvs = new System.Collections.Generic.List<UnityEngine.Vector2>(mesh.uv); var ws = new System.Collections.Generic.List<UnityEngine.BoneWeight>(w);
  float Wt(UnityEngine.BoneWeight b, System.Func<int, bool> f) => (f(b.boneIndex0) ? b.weight0 : 0) + (f(b.boneIndex1) ? b.weight1 : 0) + (f(b.boneIndex2) ? b.weight2 : 0) + (f(b.boneIndex3) ? b.weight3 : 0);
  bool H(int v) => Wt(w[v], i => i == hb || i == eb || i == bb) > .5f;
  bool NK(int v) => Wt(w[v], i => i == hb || i == eb || i == bb || i == nb) > .5f;
  bool Hair(UnityEngine.Color c) => c.r > .45f && c.r > c.g * 1.45f && c.g > c.b * 1.05f;
  var tris = new System.Collections.Generic.List<int>(); var hairTris = new System.Collections.Generic.List<int>(); int cutH = 0, cutR = 0;
  var src = mesh.triangles;
  for (int i = 0; i < src.Length; i += 3) { int a = src[i], b = src[i + 1], c = src[i + 2];
    if (H(a) && H(b) && H(c)) { cutH++; continue; }
    if (cutHair && NK(a) && NK(b) && NK(c)) { var u0 = (uvs[a] + uvs[b] + uvs[c]) / 3f; var cn = tex0.GetPixelBilinear(u0.x, u0.y); if (cn.r > cn.g * 1.05f && cn.g > cn.b && cn.r > cn.b * 1.12f) { cutH++; continue; } }   // her neck: the Meshy head brings its own
    if (tex != null) { var u = (uvs[a] + uvs[b] + uvs[c]) / 3f; if (Hair(tex.GetPixelBilinear(u.x, u.y))) { cutR++; hairTris.Add(a); hairTris.Add(b); hairTris.Add(c); continue; } }
    tris.Add(a); tris.Add(b); tris.Add(c); }
  // close the holes: directed boundary edges (used once) chained into loops; each loop gets a fan to its centre.
  // Vertices split by the unwrap share a position, so edges are matched by position.
  int[] Grow(int[] arr, int n, int val) { if (arr.Length < n) System.Array.Resize(ref arr, System.Math.Max(n, arr.Length * 2)); arr[n - 1] = val; return arr; }
  var key = new System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, int>(); var canon = new int[verts.Count];
  for (int i = 0; i < verts.Count; i++) { var k = UnityEngine.Vector3Int.RoundToInt(verts[i] * 10000f); if (!key.TryGetValue(k, out var c0)) { key[k] = i; c0 = i; } canon[i] = c0; }
  // her hair below the head (the ponytail and the hair over her upper back) is flattened onto her back, not cut away:
  // the vertices kept triangles use stay put, the rest are averaged with their neighbours until they form a smooth skin
  // across, and those triangles take one shirt texel (flat), so no hole is left to patch
  if (hairTris.Count > 0)
  {
    var fixedP = new System.Collections.Generic.HashSet<int>(); var keptV = new System.Collections.Generic.Dictionary<int, int>();
    foreach (var t in tris) { fixedP.Add(canon[t]); keptV[canon[t]] = t; }
    var adj = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>();
    for (int i = 0; i < hairTris.Count; i += 3) for (int e = 0; e < 3; e++) { int a = canon[hairTris[i + e]], b = canon[hairTris[i + (e + 1) % 3]];
      if (!adj.ContainsKey(a)) adj[a] = new System.Collections.Generic.HashSet<int>(); if (!adj.ContainsKey(b)) adj[b] = new System.Collections.Generic.HashSet<int>(); adj[a].Add(b); adj[b].Add(a); }
    var pos = new System.Collections.Generic.Dictionary<int, UnityEngine.Vector3>(); foreach (var k in adj.Keys) pos[k] = verts[k];
    var free = new System.Collections.Generic.List<int>(); foreach (var k in adj.Keys) if (!fixedP.Contains(k)) free.Add(k);
    for (int it = 0; it < 600; it++) { var nxt = new System.Collections.Generic.Dictionary<int, UnityEngine.Vector3>();
      foreach (var k in free) { var sum = UnityEngine.Vector3.zero; foreach (var n in adj[k]) sum += pos[n]; nxt[k] = sum / adj[k].Count; }
      foreach (var kv in nxt) pos[kv.Key] = kv.Value; }
    var spineAxis = (bp[nb].inverse.MultiplyPoint(UnityEngine.Vector3.zero) - bp[Bone("spine_01")].inverse.MultiplyPoint(UnityEngine.Vector3.zero)).normalized;
    int low = -1; float ly = 1e9f; foreach (var k in adj.Keys) if (keptV.ContainsKey(k)) { float y = UnityEngine.Vector3.Dot(verts[k], spineAxis); if (y < ly) { ly = y; low = k; } }
    var shirt = uvs[keptV[low]];
    var nsum = new System.Collections.Generic.Dictionary<int, UnityEngine.Vector3>();
    for (int i = 0; i < hairTris.Count; i += 3) { var p0 = pos[canon[hairTris[i]]]; var fn = UnityEngine.Vector3.Cross(pos[canon[hairTris[i + 1]]] - p0, pos[canon[hairTris[i + 2]]] - p0);
      for (int e = 0; e < 3; e++) { int k = canon[hairTris[i + e]]; nsum[k] = (nsum.TryGetValue(k, out var v0) ? v0 : UnityEngine.Vector3.zero) + fn; } }
    for (int i = 0; i < hairTris.Count; i += 3) for (int e = 0; e < 3; e++) { int ov = hairTris[i + e], k = canon[ov];
      tris.Add(verts.Count); verts.Add(pos[k]); uvs.Add(shirt); ws.Add(ws[ov]); norms.Add(keptV.ContainsKey(k) ? norms[keptV[k]] : nsum[k].normalized); canon = Grow(canon, verts.Count, k); }
    log.Append($"  hair flattened: {hairTris.Count / 3} tris, {free.Count} verts moved; ");
  }
  var count = new System.Collections.Generic.Dictionary<(int, int), int>();
  for (int i = 0; i < tris.Count; i += 3) for (int e = 0; e < 3; e++) { int a = canon[tris[i + e]], b = canon[tris[i + (e + 1) % 3]]; var kk = a < b ? (a, b) : (b, a); count[kk] = count.TryGetValue(kk, out var n) ? n + 1 : 1; }
  var next = new System.Collections.Generic.Dictionary<int, int>();
  for (int i = 0; i < tris.Count; i += 3) for (int e = 0; e < 3; e++) { int a = canon[tris[i + e]], b = canon[tris[i + (e + 1) % 3]]; var kk = a < b ? (a, b) : (b, a); if (count[kk] == 1) next[a] = b; }
  var used = new System.Collections.Generic.HashSet<int>(); int loops = 0, fan = 0;
  foreach (var start in new System.Collections.Generic.List<int>(next.Keys))
  {
    if (used.Contains(start)) continue; var loop = new System.Collections.Generic.List<int>(); int cur = start;
    while (!used.Contains(cur) && next.ContainsKey(cur)) { used.Add(cur); loop.Add(cur); cur = next[cur]; }
    if (cur != start || loop.Count < 3) continue;
    var cen = UnityEngine.Vector3.zero; foreach (var v in loop) cen += verts[v]; cen /= loop.Count;
    // one flat colour for the patch: the edge vertex whose texel is nearest the edge's median colour
    var cols = new System.Collections.Generic.List<UnityEngine.Color>(); foreach (var v in loop) cols.Add(tex0.GetPixelBilinear(uvs[v].x, uvs[v].y));
    float Med(System.Func<UnityEngine.Color, float> f) { var l = cols.ConvertAll(x => f(x)); l.Sort(); return l[l.Count / 2]; }
    var med = new UnityEngine.Color(Med(x => x.r), Med(x => x.g), Med(x => x.b)); int bi = 0; float bd = 9;
    for (int i = 0; i < loop.Count; i++) { var d0 = cols[i] - med; float dd = d0.r * d0.r + d0.g * d0.g + d0.b * d0.b; if (dd < bd) { bd = dd; bi = i; } }
    if (cutHair) { float ly = 1e9f; for (int i = 0; i < loop.Count; i++) { float y = UnityEngine.Vector3.Dot(verts[loop[i]], (bp[nb].inverse.MultiplyPoint(UnityEngine.Vector3.zero) - bp[Bone("spine_01")].inverse.MultiplyPoint(UnityEngine.Vector3.zero)).normalized); if (y < ly) { ly = y; bi = i; } } }   // her back: the shirt at the patch's lowest edge
    var shirtUv = uvs[loop[bi]];
    // the fan's centre is skinned to the loop's average weights: bound to one edge vertex's bones it was pulled out into a spike once posed
    var wsum = new System.Collections.Generic.Dictionary<int, float>();
    foreach (var v in loop) { var bw0 = ws[v]; foreach (var (bi2, wt) in new[] { (bw0.boneIndex0, bw0.weight0), (bw0.boneIndex1, bw0.weight1), (bw0.boneIndex2, bw0.weight2), (bw0.boneIndex3, bw0.weight3) }) if (wt > 0) wsum[bi2] = (wsum.TryGetValue(bi2, out var q0) ? q0 : 0) + wt; }
    var top4 = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, float>>(wsum); top4.Sort((x, y) => y.Value.CompareTo(x.Value)); while (top4.Count < 4) top4.Add(new System.Collections.Generic.KeyValuePair<int, float>(0, 0)); float tsum = top4[0].Value + top4[1].Value + top4[2].Value + top4[3].Value;
    var cenW = new UnityEngine.BoneWeight { boneIndex0 = top4[0].Key, weight0 = top4[0].Value / tsum, boneIndex1 = top4[1].Key, weight1 = top4[1].Value / tsum, boneIndex2 = top4[2].Key, weight2 = top4[2].Value / tsum, boneIndex3 = top4[3].Key, weight3 = top4[3].Value / tsum };
    // skin texel on the edge (the neck): the edge vertex most like skin; each fan triangle is skin where it meets skin, else shirt
    bool SkinC(UnityEngine.Color c) => c.r > c.g * 1.06f && c.g > c.b && c.r > c.b * 1.2f && (c.r + c.g + c.b) / 3f > .42f;
    int si = -1; float sb = -1; for (int i = 0; i < loop.Count; i++) if (SkinC(cols[i])) { float l = cols[i].r + cols[i].g; if (si < 0 || UnityEngine.Mathf.Abs(l - 1.45f) < sb) { sb = UnityEngine.Mathf.Abs(l - 1.45f); si = i; } }
    var skinUv = si >= 0 ? uvs[loop[si]] : shirtUv; int skinTris = 0;
    for (int i = 0; i < loop.Count; i++)
    {
      int j = (i + 1) % loop.Count; bool sk = si >= 0 && (SkinC(cols[i]) || SkinC(cols[j])); var uvT = sk ? skinUv : shirtUv; if (sk) skinTris++;
      var p0 = verts[loop[j]]; var p1 = verts[loop[i]]; var n = UnityEngine.Vector3.Cross(p1 - p0, cen - p0).normalized;
      if (UnityEngine.Vector3.Dot(n, bp[hb].inverse.MultiplyPoint(UnityEngine.Vector3.zero) - cen) < 0) n = -n;   // the cap on the neck faces up, toward the head
      int b0 = verts.Count;
      verts.Add(p0); verts.Add(p1); verts.Add(cen); for (int k = 0; k < 3; k++) { norms.Add(n); uvs.Add(uvT); }
      ws.Add(ws[loop[j]]); ws.Add(ws[loop[i]]); ws.Add(cenW);
      tris.Add(b0); tris.Add(b0 + 1); tris.Add(b0 + 2); fan++;
    }
    log.Append($"  loop {loop.Count} edges, skin tris {skinTris}; ");
    loops++;
  }
  // slim the neck: neck-weighted vertices move toward the neck's axis (bind space), by their neck weight
  var np = bp[nb].inverse.MultiplyPoint(UnityEngine.Vector3.zero); var hp = bp[hb].inverse.MultiplyPoint(UnityEngine.Vector3.zero);
  var axis = (hp - np).normalized; int slim = 0;
  for (int i = 0; i < verts.Count; i++) { float nw = Wt(ws[i], j => j == nb) + .6f * Wt(ws[i], j => j == hb); if (nw <= .05f) continue; nw = UnityEngine.Mathf.Clamp01(nw);
    var d = verts[i] - np; var along = axis * UnityEngine.Vector3.Dot(d, axis); var perp = d - along;
    verts[i] = np + along + perp * (1f - (1f - (cutHair ? .45f : neckF)) * nw); slim++; }   // her neck slimmer still: it sits inside the Meshy head's own neck
  var cut = UnityEngine.Object.Instantiate(mesh); cut.name = name + " No Face";
  cut.SetVertices(verts); cut.SetNormals(norms); cut.SetUVs(0, uvs); cut.boneWeights = ws.ToArray(); cut.bindposes = bp;
  cut.subMeshCount = 1; cut.SetTriangles(tris, 0); cut.RecalculateBounds();
  string p = dir + name + " No Face.asset"; UnityEditor.AssetDatabase.DeleteAsset(p); UnityEditor.AssetDatabase.CreateAsset(cut, p); PixelRestyle.MarkReadable(cut);
  smr.sharedMesh = cut; UnityEditor.EditorUtility.SetDirty(smr);
  log.Append($"{name}: head tris {cutH}, hair {cutR}, holes closed {loops} ({fan} tris), neck verts slimmed {slim}\n");
}
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bt.gameObject.scene); UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();

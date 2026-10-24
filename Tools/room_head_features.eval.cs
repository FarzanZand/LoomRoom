// Eyes and brows of the room player heads as a pixel-art overlay (run after Tools/room_head_grade.eval.cs):
// the head's triangles round the eyes and brows are copied a hair's breadth out from the face as "Face features", with
// one pixel grid laid straight across the face from the front at the body's texel size, so a brow or an eye never breaks
// at a seam of the head's own texture. The eyes take their shape from where the swatches found the eye openings
// (white, a two-texel iris, a one-texel pupil); the brows are tapered arches over the eyes, two texels thick at the inner
// end and one at the tail. Everything else in the overlay is clear (alpha clipped).
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var log = new System.Text.StringBuilder();
foreach (var (headGo, bodyGo, name, who) in new[] { (bt.headA, bt.bodyA, "Room Head Woman", "woman"), (bt.headB, bt.bodyB, "Room Head Man", "man") })
{
  bool hw = headGo.activeSelf, bw = bodyGo.activeSelf; headGo.SetActive(true); bodyGo.SetActive(true);
  var old = headGo.transform.Find("Face features"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
  var smr = bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>();
  float tpu = PixelRestyle.TexelsPerUnit(smr);
  var clsT = new UnityEngine.Texture2D(2, 2); clsT.LoadImage(System.IO.File.ReadAllBytes($"Temp/RoomHead/sw_{who}_cls.png")); int N = clsT.width; var clp = clsT.GetPixels32();
  var smallT = new UnityEngine.Texture2D(2, 2); smallT.LoadImage(System.IO.File.ReadAllBytes($"Temp/RoomHead/sw_{who}_small.png")); var smp = smallT.GetPixels32();
  var hm = headGo.GetComponent<UnityEngine.MeshFilter>().sharedMesh; var ht = headGo.transform; var v = hm.vertices; var nr = hm.normals; var uv = hm.uv; var tr = hm.triangles;
  var hb = headGo.GetComponent<UnityEngine.Renderer>().bounds; var c0 = hb.center; float H = hb.size.y; var f = bt.transform.forward; var rg = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, f).normalized;
  UnityEngine.Vector3 Rel(UnityEngine.Vector3 w) { var d = w - c0; return new UnityEngine.Vector3(UnityEngine.Vector3.Dot(d, f) / H, d.y / H, UnityEngine.Vector3.Dot(d, rg) / H); }
  // positions of the texels of a class (front of the face only)
  System.Collections.Generic.List<UnityEngine.Vector3> Of(System.Func<int, bool> want)
  {
    var list = new System.Collections.Generic.List<UnityEngine.Vector3>();
    for (int t = 0; t < tr.Length; t += 3)
    {
      UnityEngine.Vector2 a = uv[tr[t]] * N, b = uv[tr[t + 1]] * N, c = uv[tr[t + 2]] * N; float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (UnityEngine.Mathf.Abs(den) < 1e-9f) continue;
      for (int y = (int)UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, c.y)); y <= (int)UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, c.y)); y++)
      for (int x = (int)UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, c.x)); x <= (int)UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, c.x)); x++)
      { if (x < 0 || y < 0 || x >= N || y >= N || !want(clp[y * N + x].r)) continue;
        float w1 = ((b.y - c.y) * (x + .5f - c.x) + (c.x - b.x) * (y + .5f - c.y)) / den, w2 = ((c.y - a.y) * (x + .5f - c.x) + (a.x - c.x) * (y + .5f - c.y)) / den, w3 = 1 - w1 - w2;
        if (w1 < 0 || w2 < 0 || w3 < 0) continue; var rl = Rel(ht.TransformPoint(v[tr[t]] * w1 + v[tr[t + 1]] * w2 + v[tr[t + 2]] * w3)); if (rl.x > .25f) list.Add(rl); }
    }
    return list;
  }
  var eyes = Of(k => k >= 2 && k <= 4); var ys = eyes.ConvertAll(e => e.y); ys.Sort(); float medY = ys[ys.Count / 2];
  eyes = eyes.FindAll(e => e.y > medY - .05f && e.y < medY + .025f);
  var brows = Of(k => k == 5).FindAll(e => e.y > medY + .03f && e.y < medY + .16f); var bys = brows.ConvertAll(e => e.y); bys.Sort();
  float browY = float.Parse(System.IO.File.ReadAllText($"Temp/RoomHead/browY_{who}.txt"), System.Globalization.CultureInfo.InvariantCulture);   // measured by room_head_grade.eval.cs
  UnityEngine.Vector2 eL = default, eR = default; int nL = 0, nR = 0; foreach (var e in eyes) { if (e.z < 0) { eL += new UnityEngine.Vector2(e.z, e.y); nL++; } else { eR += new UnityEngine.Vector2(e.z, e.y); nR++; } } eL /= UnityEngine.Mathf.Max(1, nL); eR /= UnityEngine.Mathf.Max(1, nR);
  // the overlay's pixel grid: across the face from the front, the body's texel size
  float z0 = -.3f, z1 = .3f, y0 = medY - .06f, y1 = browY + .05f;
  int TW = UnityEngine.Mathf.Max(8, UnityEngine.Mathf.RoundToInt((z1 - z0) * H * tpu)), TH = UnityEngine.Mathf.Max(4, UnityEngine.Mathf.RoundToInt((y1 - y0) * H * tpu));
  var px = new UnityEngine.Color[TW * TH]; for (int i = 0; i < px.Length; i++) px[i] = new UnityEngine.Color(0, 0, 0, 0);
  (int, int) Cell(UnityEngine.Vector3 r) => (UnityEngine.Mathf.FloorToInt((r.z - z0) / (z1 - z0) * TW), UnityEngine.Mathf.FloorToInt((r.y - y0) / (y1 - y0) * TH));
  // eyes: each eye's cells by where its texels are, then white, a two-cell iris and a one-cell pupil at its centre
  var irisL = new System.Collections.Generic.List<UnityEngine.Color32>();
  for (int i = 0; i < smp.Length; i++) { if (clp[i].r == 3) irisL.Add(smp[i]); }
  UnityEngine.Color irisC = irisL.Count > 0 ? (UnityEngine.Color)irisL[irisL.Count / 2] : new UnityEngine.Color(.36f, .25f, .17f); irisC.a = 1;
  UnityEngine.Color white = new UnityEngine.Color(.86f, .82f, .76f, 1), pupil = new UnityEngine.Color(.09f, .06f, .05f, 1);
  var browList = new System.Collections.Generic.List<UnityEngine.Color32>(); for (int i = 0; i < smp.Length; i++) if (clp[i].r == 5) browList.Add(smp[i]);
  UnityEngine.Color browC = browList.Count > 0 ? (UnityEngine.Color)browList[browList.Count / 2] : new UnityEngine.Color(.2f, .12f, .08f); browC.a = 1;
  UnityEngine.Color lash = browC * .75f; lash.a = 1;
  // pixel-art templates, columns from the inner corner outward, rows from the top: an eye with a lash line above it and a
  // tapered, arched brow; W white, I iris, P pupil, L lash, B brow
  string[] eyeT = { ".LLLL", "WIPIW", ".WIW." };
  string[] browT = { ".BBBBBB.", "BBB....B" };
  void Stamp(string[] tpl, int innerX, int topY, int outward)
  {
    for (int r = 0; r < tpl.Length; r++) for (int k = 0; k < tpl[r].Length; k++)
    {
      char ch = tpl[r][k]; if (ch == '.') continue; int x = innerX + k * outward, y = topY - r; if (x < 0 || y < 0 || x >= TW || y >= TH) continue;
      px[y * TW + x] = ch == 'W' ? white : ch == 'I' ? irisC : ch == 'P' ? pupil : ch == 'L' ? lash : browC;
    }
  }
  foreach (var side in new[] { -1, 1 })
  {
    var sideEyes = eyes.FindAll(e => System.Math.Sign(e.z) == side); if (sideEyes.Count == 0) continue;
    float mz = 0, my = 0; foreach (var e in sideEyes) { mz += e.z; my += e.y; } mz /= sideEyes.Count; my /= sideEyes.Count;
    var (cx, cy) = Cell(new UnityEngine.Vector3(0, my, mz));
    int outward = side;   // cells run with z, so outward is +x on the +z side and -x on the -z side
    int innerX = cx - 2 * outward;   // the eye is five cells wide, centred on the eye
    Stamp(eyeT, innerX, cy + 1, outward);
    var (_, by) = Cell(new UnityEngine.Vector3(0, browY, mz));
    Stamp(browT, innerX - outward, by + 1, outward);
    log.Append($"eye {side} at cell {cx},{cy}, brow row {by}; ");
  }
  // the overlay mesh: head triangles in the band, pushed out a little, UVs from the grid
  var nv = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nn = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nu = new System.Collections.Generic.List<UnityEngine.Vector2>(); var nt = new System.Collections.Generic.List<int>();
  float push = .006f * H / ht.lossyScale.x;
  for (int t = 0; t < tr.Length; t += 3)
  {
    var ctr = Rel(ht.TransformPoint((v[tr[t]] + v[tr[t + 1]] + v[tr[t + 2]]) / 3f));
    if (ctr.x < .2f || ctr.y < y0 - .01f || ctr.y > y1 + .01f || ctr.z < z0 || ctr.z > z1) continue;
    for (int k = 0; k < 3; k++) { int i = tr[t + k]; var p = v[i] + nr[i] * push; var r = Rel(ht.TransformPoint(p)); nv.Add(p); nn.Add(nr[i]); nu.Add(new UnityEngine.Vector2((r.z - z0) / (z1 - z0), (r.y - y0) / (y1 - y0))); nt.Add(nv.Count - 1); }
  }
  var mesh = new UnityEngine.Mesh { name = name + " features" }; mesh.SetVertices(nv); mesh.SetNormals(nn); mesh.SetUVs(0, nu); mesh.SetTriangles(nt, 0); mesh.RecalculateBounds();
  string dir = $"Assets/Game/Art/Synty Pixel/Characters/{name}";
  UnityEditor.AssetDatabase.DeleteAsset($"{dir}/{name} features.asset"); UnityEditor.AssetDatabase.CreateAsset(mesh, $"{dir}/{name} features.asset");
  var tex = new UnityEngine.Texture2D(TW, TH, UnityEngine.TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
  string tp = $"{dir}/{name} features.png"; System.IO.File.WriteAllBytes(tp, tex.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(tp);
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(tp); ti.filterMode = UnityEngine.FilterMode.Point; ti.mipmapEnabled = false; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; ti.wrapMode = UnityEngine.TextureWrapMode.Clamp; ti.alphaIsTransparency = true; ti.SaveAndReimport();
  string mp = $"{dir}/{name} features.mat"; var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(mp);
  if (!mat) { mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(mat, mp); }
  mat.SetTexture("_BaseMap", UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(tp)); mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", .5f); mat.EnableKeyword("_ALPHATEST_ON"); mat.SetFloat("_Smoothness", 0); mat.SetFloat("_Cull", 2); UnityEditor.EditorUtility.SetDirty(mat);
  var go = new UnityEngine.GameObject("Face features"); go.layer = headGo.layer; go.transform.SetParent(ht, false);
  go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<UnityEngine.MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
  var hmr = headGo.GetComponent<UnityEngine.MeshRenderer>(); mr.probeAnchor = hmr.probeAnchor;
  log.Append($"{name}: grid {TW}x{TH}, {nt.Count / 3} tris, eyes y {medY:F3} brows y {browY:F3}\n");
  headGo.SetActive(hw); bodyGo.SetActive(bw);
}
bt.Apply(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bt.gameObject.scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(bt.gameObject.scene); UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();

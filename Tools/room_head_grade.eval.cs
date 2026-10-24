// Run with: unity command eval_file --file Tools/room_head_grade.eval.cs (swatches from Tools/room_head_swatches.py into Temp/RoomHead/sw_woman.png, sw_man.png; trim.txt "r,g,b|r,g,b" measured on a neutral render, rendered cheek against rendered neck front).
// Room player heads graded like their restyled bodies, one sample per final texel (the swatch maps are already at the
// final size, decided by class, so edges stay crisp): skin and hair take PixelRestyle.GradeTexel (skin = the body's own
// Synty skin colour, occlusion from head and body); eyes (white, iris, pupil) and brows stay flat, placed like pixel art.
// Skin behind the ears (the scalp of a short-haired head) is hair.
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var log = new System.Text.StringBuilder();
var dirs = new UnityEngine.Vector3[PixelRestyle.GradeAoRays]; var rng = new System.Random(11);
for (int i = 0; i < dirs.Length; i++) { float u = (float)rng.NextDouble(), v = (float)rng.NextDouble(); float th = 2 * UnityEngine.Mathf.PI * u, ph = UnityEngine.Mathf.Acos(2 * v - 1); dirs[i] = new UnityEngine.Vector3(UnityEngine.Mathf.Sin(ph) * UnityEngine.Mathf.Cos(th), UnityEngine.Mathf.Cos(ph), UnityEngine.Mathf.Sin(ph) * UnityEngine.Mathf.Sin(th)); }
UnityEngine.Texture2D Readable(UnityEngine.Texture t) { var rt = UnityEngine.RenderTexture.GetTemporary(t.width, t.height, 0, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB); UnityEngine.Graphics.Blit(t, rt); var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt; var r = new UnityEngine.Texture2D(t.width, t.height, UnityEngine.TextureFormat.RGBA32, false); r.ReadPixels(new UnityEngine.Rect(0, 0, t.width, t.height), 0, 0); r.Apply(); UnityEngine.RenderTexture.active = prev; UnityEngine.RenderTexture.ReleaseTemporary(rt); return r; }
foreach (var (headGo, bodyGo, name, synty, prefab) in new[] {
  (bt.headA, bt.bodyA, "Room Head Woman", "Character_Daughter_01", "SM_Chr_Daughter_01"),
  (bt.headB, bt.bodyB, "Room Head Man", "Character_Father_02", "SM_Chr_Father_02") })
{
  bool hw = headGo.activeSelf, bw = bodyGo.activeSelf; headGo.SetActive(true); bodyGo.SetActive(true);
  var smr = bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>();
  // the body's Synty skin swatch: the atlas colour under the source mesh's neck vertices
  var pf = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"Assets/Synty/PolygonTown/Prefabs/Characters/{prefab}.prefab");
  var sr = System.Array.Find(pf.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true), r => r.sharedMesh && r.sharedMesh.name == synty);
  var sm = sr.sharedMesh; var smat = sr.sharedMaterial; var atlasT = smat.HasProperty("_BaseMap") && smat.GetTexture("_BaseMap") ? smat.GetTexture("_BaseMap") : smat.HasProperty("_Albedo_Map") && smat.GetTexture("_Albedo_Map") ? smat.GetTexture("_Albedo_Map") : smat.mainTexture;
  var atlas = Readable(atlasT); var tint = smat.HasProperty("_BaseColor") ? smat.GetColor("_BaseColor") : UnityEngine.Color.white;
  int neck = System.Array.FindIndex(sr.bones, b => b.name == "neck_01"); var bw0 = sm.boneWeights; var suv = sm.uv;
  var counts = new System.Collections.Generic.Dictionary<UnityEngine.Color32, int>();
  for (int i = 0; i < suv.Length; i++) { UnityEngine.Color32 c = atlas.GetPixelBilinear(suv[i].x, suv[i].y); if (!(c.r > c.g * 1.08f && c.g > c.b && c.r > c.b * 1.15f && (c.r + c.g + c.b) / 3f > 110)) continue; counts[c] = counts.TryGetValue(c, out var n0) ? n0 + 1 : 1; }   // the body's skin: its most common skin-coloured swatch
  UnityEngine.Color32 best = default; int bn = -1; foreach (var kv in counts) if (kv.Value > bn) { bn = kv.Value; best = kv.Key; }
  UnityEngine.Color skinSwatch = (UnityEngine.Color)best * tint; skinSwatch.a = 1;
  // occlusion probes: the head and the posed body on the probe layer, in world space
  var probes = new System.Collections.Generic.List<UnityEngine.GameObject>();
  UnityEngine.GameObject Probe(UnityEngine.Mesh m, UnityEngine.Transform like) { var g = new UnityEngine.GameObject("grade probe") { hideFlags = UnityEngine.HideFlags.HideAndDontSave, layer = PixelRestyle.GradeProbeLayer }; g.transform.SetPositionAndRotation(like.position, like.rotation); g.transform.localScale = like.lossyScale; g.AddComponent<UnityEngine.MeshCollider>().sharedMesh = m; probes.Add(g); return g; }
  var baked = new UnityEngine.Mesh(); smr.BakeMesh(baked); Probe(baked, smr.transform);
  var hm = headGo.GetComponent<UnityEngine.MeshFilter>().sharedMesh; Probe(hm, headGo.transform);
  UnityEngine.Physics.SyncTransforms();
  float worldScale = (smr.transform.TransformPoint(UnityEngine.Vector3.right) - smr.transform.position).magnitude;
  float size = smr.sharedMesh.bounds.size.magnitude, aoDist = UnityEngine.Mathf.Clamp(size * .08f, .015f, .3f) * worldScale;
  // swatch maps at the final size: colours and classes (0 skin, 1 hair and the rest, 2 eye white, 3 iris, 4 pupil, 5 brow)
  string who = name.Contains("Woman") ? "woman" : "man";
  var smallT = new UnityEngine.Texture2D(2, 2); smallT.LoadImage(System.IO.File.ReadAllBytes($"Temp/RoomHead/sw_{who}_small.png"));
  var clsT = new UnityEngine.Texture2D(2, 2); clsT.LoadImage(System.IO.File.ReadAllBytes($"Temp/RoomHead/sw_{who}_cls.png"));
  int N = smallT.width, S2 = N * 2;   // graded at 2x and averaged within each texel, as Build does
  var smp = smallT.GetPixels32(); var clp = clsT.GetPixels32();
  // PNG rows load bottom-up in Unity, like UVs: index [y * N + x] with y from the bottom matches uv * N
  string outPath = $"Assets/Game/Art/Synty Pixel/Characters/{name}/{name} pixel.png";
  var hairCount = new System.Collections.Generic.Dictionary<UnityEngine.Color32, int>();
  for (int i = 0; i < smp.Length; i++) if (clp[i].r == 1) hairCount[smp[i]] = hairCount.TryGetValue(smp[i], out var hc) ? hc + 1 : 1;
  UnityEngine.Color32 hairMain = default; int hn = -1; foreach (var kv in hairCount) if (kv.Value > hn) { hn = kv.Value; hairMain = kv.Key; }
  var hbnd = headGo.GetComponent<UnityEngine.Renderer>().bounds; var hc0 = hbnd.center; float hH = hbnd.size.y; var fwdH = bt.transform.forward; int scalp = 0, browCleared = 0, browBand = 0; bool dbg = System.IO.File.Exists("Temp/RoomHead/debug.txt"), dbg2 = System.IO.File.Exists("Temp/RoomHead/debug2.txt");
  // the eyes, found on the head: eye-class texels in the face's eye band (pale bits higher up, on the brows, are not
  // eyes); each eye's centre is the mean of its texels, and the iris and pupil are drawn round it on the face
  var vv = hm.vertices; var uvv = hm.uv; var trr = hm.triangles; var rgt = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, fwdH).normalized;
  UnityEngine.Vector3 Rel(UnityEngine.Vector3 w) { var d = w - hc0; return new UnityEngine.Vector3(UnityEngine.Vector3.Dot(d, fwdH) / hH, d.y / hH, UnityEngine.Vector3.Dot(d, rgt) / hH); }
  var eyeRel = new System.Collections.Generic.List<UnityEngine.Vector3>();
  for (int t = 0; t < trr.Length; t += 3)
  {
    UnityEngine.Vector2 a = uvv[trr[t]] * N, b = uvv[trr[t + 1]] * N, c = uvv[trr[t + 2]] * N; float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (UnityEngine.Mathf.Abs(den) < 1e-9f) continue;
    for (int y = (int)UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, c.y)); y <= (int)UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, c.y)); y++)
    for (int x = (int)UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, c.x)); x <= (int)UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, c.x)); x++)
    { if (x < 0 || y < 0 || x >= N || y >= N) continue; int k = clp[y * N + x].r; if (k < 2 || k > 4) continue;
      float w1 = ((b.y - c.y) * (x + .5f - c.x) + (c.x - b.x) * (y + .5f - c.y)) / den, w2 = ((c.y - a.y) * (x + .5f - c.x) + (a.x - c.x) * (y + .5f - c.y)) / den, w3 = 1 - w1 - w2;
      if (w1 < 0 || w2 < 0 || w3 < 0) continue; var rl = Rel(headGo.transform.TransformPoint(vv[trr[t]] * w1 + vv[trr[t + 1]] * w2 + vv[trr[t + 2]] * w3)); if (rl.x > .25f) eyeRel.Add(rl); }
  }
  var ys0 = eyeRel.ConvertAll(e => e.y); ys0.Sort(); float medY = ys0.Count > 0 ? ys0[ys0.Count / 2] : 0f;
  UnityEngine.Vector2 eL = default, eR = default; int nL = 0, nR = 0;
  foreach (var e in eyeRel) if (e.y > medY - .05f && e.y < medY + .025f) { if (e.z < 0) { eL += new UnityEngine.Vector2(e.y, e.z); nL++; } else { eR += new UnityEngine.Vector2(e.y, e.z); nR++; } }
  if (nL > 0) eL /= nL; if (nR > 0) eR /= nR;
  var irisList = new System.Collections.Generic.List<UnityEngine.Color32>(); for (int i = 0; i < smp.Length; i++) if (clp[i].r == 3) irisList.Add(smp[i]);
  UnityEngine.Color irisC = irisList.Count > 0 ? (UnityEngine.Color)irisList[irisList.Count / 2] : new UnityEngine.Color(.36f, .25f, .17f);
  UnityEngine.Color whiteC = new UnityEngine.Color(.86f, .82f, .76f), pupilC = new UnityEngine.Color(.09f, .06f, .05f);
  const float rIris = .03f, rPupil = .013f;
  log.Append($"eyes at y {medY:F3}, left {eL} ({nL}), right {eR} ({nR}); ");
  // brows: drawn on the head like the eyes, a tapered arch over each eye, two texels thick at the inner end and one at
  // the tail, in a darker shade of the hair; their height from the brow marks the swatches found
  var browYs = new System.Collections.Generic.List<float>();
  for (int t = 0; t < trr.Length; t += 3)
  {
    UnityEngine.Vector2 a = uvv[trr[t]] * N, b = uvv[trr[t + 1]] * N, c = uvv[trr[t + 2]] * N; float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (UnityEngine.Mathf.Abs(den) < 1e-9f) continue;
    for (int y = (int)UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, c.y)); y <= (int)UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, c.y)); y++)
    for (int x = (int)UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, c.x)); x <= (int)UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, c.x)); x++)
    { if (x < 0 || y < 0 || x >= N || y >= N || clp[y * N + x].r != 5) continue;
      float w1 = ((b.y - c.y) * (x + .5f - c.x) + (c.x - b.x) * (y + .5f - c.y)) / den, w2 = ((c.y - a.y) * (x + .5f - c.x) + (a.x - c.x) * (y + .5f - c.y)) / den, w3 = 1 - w1 - w2;
      if (w1 < 0 || w2 < 0 || w3 < 0) continue; var rl = Rel(headGo.transform.TransformPoint(vv[trr[t]] * w1 + vv[trr[t + 1]] * w2 + vv[trr[t + 2]] * w3)); if (rl.x > .25f && rl.y > medY + .03f && rl.y < medY + .16f) browYs.Add(rl.y); }
  }
  if (browYs.Count <= 10)   // no brow marks found on the face: the old brows are the dark texels above the eyes
  {
    for (int t = 0; t < trr.Length; t += 3)
    {
      UnityEngine.Vector2 a = uvv[trr[t]] * N, b = uvv[trr[t + 1]] * N, c = uvv[trr[t + 2]] * N; float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (UnityEngine.Mathf.Abs(den) < 1e-9f) continue;
      for (int y = (int)UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, c.y)); y <= (int)UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, c.y)); y++)
      for (int x = (int)UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, c.x)); x <= (int)UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, c.x)); x++)
      { if (x < 0 || y < 0 || x >= N || y >= N || clp[y * N + x].r != 1 || ((UnityEngine.Color)smp[y * N + x]).grayscale > .25f) continue;
        float w1 = ((b.y - c.y) * (x + .5f - c.x) + (c.x - b.x) * (y + .5f - c.y)) / den, w2 = ((c.y - a.y) * (x + .5f - c.x) + (a.x - c.x) * (y + .5f - c.y)) / den, w3 = 1 - w1 - w2;
        if (w1 < 0 || w2 < 0 || w3 < 0) continue; var rl = Rel(headGo.transform.TransformPoint(vv[trr[t]] * w1 + vv[trr[t + 1]] * w2 + vv[trr[t + 2]] * w3));
        if (rl.x > .3f && rl.y > medY + .03f && rl.y < medY + .13f && System.Math.Abs(rl.z) > .05f && System.Math.Abs(rl.z) < .24f) browYs.Add(rl.y); }
    }
  }
  browYs.Sort(); float browY = browYs.Count > 10 ? browYs[browYs.Count / 2] : medY + .085f;
  System.IO.File.WriteAllText($"Temp/RoomHead/browY_{who}.txt", browY.ToString(System.Globalization.CultureInfo.InvariantCulture));
  var browList = new System.Collections.Generic.List<UnityEngine.Color32>(); for (int i = 0; i < smp.Length; i++) if (clp[i].r == 5) browList.Add(smp[i]);
  UnityEngine.Color browC = browList.Count > 0 ? (UnityEngine.Color)browList[browList.Count / 2] : (UnityEngine.Color)hairMain * .7f; browC.a = 1;
  bool InBrow(UnityEngine.Vector3 r)
  {
    var ec = r.z < 0 ? eL : eR; float sgn = ec.y < 0 ? -1 : 1; float u = (r.z - ec.y) * sgn;   // outward from the eye's centre
    if (u < -.06f || u > .075f) return false;
    float tt = (u + .06f) / .135f; float yc = browY + .012f * UnityEngine.Mathf.Sin(UnityEngine.Mathf.PI * UnityEngine.Mathf.Min(1f, tt * 1.25f));
    float half = UnityEngine.Mathf.Lerp(.021f, .009f, tt);
    return UnityEngine.Mathf.Abs(r.y - yc) < half;
  }
  log.Append($"brows at y {browY:F3} ({browYs.Count}); ");
  var acc = new UnityEngine.Color[S2 * S2]; var cov = new bool[S2 * S2]; var isSkin = new bool[S2 * S2]; var flat = new bool[S2 * S2];
  var v = hm.vertices; var nr = hm.normals; var uv = hm.uv; var tr = hm.triangles; var ht = headGo.transform; int graded = 0;
  for (int t = 0; t < tr.Length; t += 3)
  {
    int i0 = tr[t], i1 = tr[t + 1], i2 = tr[t + 2]; UnityEngine.Vector2 a = uv[i0] * S2, b = uv[i1] * S2, c = uv[i2] * S2;
    float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y); if (UnityEngine.Mathf.Abs(den) < 1e-9f) continue;
    int x0 = UnityEngine.Mathf.Max(0, (int)UnityEngine.Mathf.Floor(UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(b.x, c.x)))), x1 = UnityEngine.Mathf.Min(S2 - 1, (int)UnityEngine.Mathf.Ceil(UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(b.x, c.x))));
    int y0 = UnityEngine.Mathf.Max(0, (int)UnityEngine.Mathf.Floor(UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(b.y, c.y)))), y1 = UnityEngine.Mathf.Min(S2 - 1, (int)UnityEngine.Mathf.Ceil(UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(b.y, c.y))));
    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
    {
      int idx = y * S2 + x; if (cov[idx]) continue; float px = x + .5f, py = y + .5f;
      float w1 = ((b.y - c.y) * (px - c.x) + (c.x - b.x) * (py - c.y)) / den, w2 = ((c.y - a.y) * (px - c.x) + (a.x - c.x) * (py - c.y)) / den, w3 = 1 - w1 - w2;
      if (w1 < -.12f || w2 < -.12f || w3 < -.12f) continue;   // a little past each edge, so the texels the GPU samples at an island's rim hold that island's colour
      var lp = v[i0] * w1 + v[i1] * w2 + v[i2] * w3; var ln = (nr[i0] * w1 + nr[i1] * w2 + nr[i2] * w3).normalized;
      var wp = ht.TransformPoint(lp); var wn = ht.TransformDirection(ln).normalized;
      int tIdx = (y / 2) * N + x / 2; int cls = clp[tIdx].r; UnityEngine.Color baseC;
      if (cls == 0 && UnityEngine.Vector3.Dot(wp - hc0, fwdH) < -.12f * hH && wp.y > hc0.y - .3f * hH) { cls = 1; baseC = hairMain; scalp++; }   // behind the ears: scalp under the hair
      else baseC = cls == 0 ? skinSwatch : (UnityEngine.Color)smp[tIdx];
      var rel = Rel(wp);
      if (rel.x > .2f && System.Math.Abs(rel.y - browY) < .045f) browBand++;
      if (dbg2 && rel.x > .2f && System.Math.Abs(rel.y - browY) < .045f) { acc[idx] = cls == 1 ? UnityEngine.Color.red : cls == 0 ? UnityEngine.Color.blue : UnityEngine.Color.yellow; cov[idx] = true; flat[idx] = true; continue; }
      if (cls == 1 && rel.x > .25f && System.Math.Abs(rel.y - browY) < .045f) { var ecb = rel.z < 0 ? eL : eR; float ub = (rel.z - ecb.y) * (ecb.y < 0 ? -1 : 1); if (ub > -.075f && ub < .09f) { cls = 0; baseC = skinSwatch; browCleared++; if (dbg) { acc[idx] = UnityEngine.Color.green; cov[idx] = true; flat[idx] = true; continue; } } }   // the old brows under the overlay's: skin
      if (cls >= 2) { cls = 0; baseC = skinSwatch; }   // eyes and brows are the Face features overlay (Tools/room_head_features.eval.cs); under it, skin
      if (cls == 1 && rel.x > .25f && rel.y > medY + .025f && rel.y < medY + .17f && !smp[tIdx].Equals(hairMain) && ((UnityEngine.Color)smp[tIdx]).grayscale > .3f) { cls = 0; baseC = skinSwatch; }   // pale bits on the brow ridge: skin
      if (cls >= 2 && cls <= 4)
      {
        if (rel.x < .25f || rel.y < medY - .05f || rel.y > medY + .025f) { cls = 0; baseC = skinSwatch; }   // not in the eye band: skin
        else { var ec = rel.z < 0 ? eL : eR; float dd = (new UnityEngine.Vector2(rel.y, rel.z) - ec).magnitude; baseC = dd < rPupil ? pupilC : dd < rIris ? irisC : whiteC; }
      }
      else if (rel.x > .22f && (cls == 0 || cls == 5 || (cls == 1 && !smp[tIdx].Equals(hairMain) && rel.y > medY + .025f && rel.y < medY + .17f)))
      {
        if (cls != 0) { cls = 0; baseC = skinSwatch; }   // brows are the Face features overlay; marks and stray bits on the brow ridge: skin
      }
      else if (cls == 5) { cls = 0; baseC = skinSwatch; }
      if (cls >= 2) { acc[idx] = baseC; cov[idx] = true; flat[idx] = true; graded++; continue; }   // eyes and brows: flat, as placed
      float ao = PixelRestyle.Occlusion(wp, wn, dirs, aoDist);
      var bp = smr.transform.InverseTransformPoint(wp); var bn2 = smr.transform.InverseTransformDirection(wn).normalized;
      acc[idx] = PixelRestyle.GradeTexel(baseC, bp, bn2, ao, size); cov[idx] = true; isSkin[idx] = cls == 0; graded++;
    }
  }
  foreach (var g in probes) UnityEngine.Object.DestroyImmediate(g); UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(atlas);
  // the face's skin to the body's: the restyle grade on the body came out darker and redder than on the head (its own
  // occlusion and crease dirt), so the head's skin texels are scaled until their median matches the body's skin median
  var bodyPng = new UnityEngine.Texture2D(2, 2); bodyPng.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap"))));
  bool Sk(UnityEngine.Color c) => c.r > c.g * 1.12f && c.g > c.b && c.g < c.b * 1.3f && c.r > c.b * 1.15f && (c.r + c.g + c.b) / 3f > .35f;   // skin, not khaki or wood (their green runs well above their blue)
  float Med(System.Collections.Generic.List<float> l) { l.Sort(); return l.Count > 0 ? l[l.Count * 6 / 10] : 1; }   // the upper middle: open skin, not the creases
  // the body's own neck: texels under its triangles weighted to the neck (the skin the face meets)
  var br = new System.Collections.Generic.List<float>(); var bg = new System.Collections.Generic.List<float>(); var bb = new System.Collections.Generic.List<float>();
  { // by position: the body's vertices round the middle of the neck (between the neck bone and the head bone), read at their UVs
    var bk = new UnityEngine.Mesh(); smr.BakeMesh(bk); var bv = bk.vertices; var bnrm = bk.normals; var buv = smr.sharedMesh.uv; var fwd = bt.transform.forward;
    var nbT = System.Array.Find(smr.bones, x => x.name == "neck_01"); var hbT = System.Array.Find(smr.bones, x => x.name == "head");
    var mid = (nbT.position + hbT.position) * .5f; float rad = (hbT.position - nbT.position).magnitude * .9f;
    for (int i = 0; i < bv.Length; i++)
    {
      var w = smr.transform.TransformPoint(UnityEngine.Vector3.Scale(bv[i], new UnityEngine.Vector3(1 / smr.transform.lossyScale.x, 1 / smr.transform.lossyScale.y, 1 / smr.transform.lossyScale.z)));
      if ((w - mid).magnitude > rad) continue;
      if (UnityEngine.Vector3.Dot(smr.transform.TransformDirection(bnrm[i]).normalized, fwd) < .4f) continue;   // the front of the neck, as it is seen
      var c = bodyPng.GetPixelBilinear(buv[i].x, buv[i].y); if (!(c.r > c.b * 1.05f)) continue;   // not the grey collar
      br.Add(c.r); bg.Add(c.g); bb.Add(c.b);
    }
    UnityEngine.Object.DestroyImmediate(bk); }
  log.Append($"neck texels {br.Count}; "); if (br.Count < 20) throw new System.Exception("too few neck texels to match the skin: " + br.Count);
  var hr = new System.Collections.Generic.List<float>(); var hg = new System.Collections.Generic.List<float>(); var hb = new System.Collections.Generic.List<float>();
  for (int i = 0; i < acc.Length; i++) if (cov[i] && isSkin[i]) { hr.Add(acc[i].r); hg.Add(acc[i].g); hb.Add(acc[i].b); }
  var kS = new UnityEngine.Color(Med(br) / Med(hr), Med(bg) / Med(hg), Med(bb) / Med(hb), 1);
  // a trim measured on the neutral render (rendered cheek against rendered neck front), so the faces match what is seen
  { var tr2 = System.IO.File.ReadAllText("Temp/RoomHead/trim.txt").Trim().Split('|')[name.Contains("Woman") ? 0 : 1].Split(',');
    kS = new UnityEngine.Color(kS.r * float.Parse(tr2[0]), kS.g * float.Parse(tr2[1]), kS.b * float.Parse(tr2[2]), 1); }
  for (int i = 0; i < acc.Length; i++) if (cov[i] && isSkin[i]) acc[i] = new UnityEngine.Color(UnityEngine.Mathf.Clamp01(acc[i].r * kS.r), UnityEngine.Mathf.Clamp01(acc[i].g * kS.g), UnityEngine.Mathf.Clamp01(acc[i].b * kS.b), 1);
  log.Append($"skin matched to body x{kS}; ");
  // each texel the mean of its covered 2x2 (one class per texel, so no two classes are mixed); then pad the islands
  var outC = new UnityEngine.Color[N * N]; var oc = new bool[N * N];
  for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
  {
    // eyes and brows: the colour most of the texel's samples have (never a blend of white, iris and pupil); the rest averaged
    var votes = new System.Collections.Generic.Dictionary<UnityEngine.Color, int>(); int nf = 0;
    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { int i = (y * 2 + dy) * S2 + x * 2 + dx; if (cov[i] && flat[i]) { votes[acc[i]] = votes.TryGetValue(acc[i], out var vn) ? vn + 1 : 1; nf++; } }
    if (nf >= 2) { UnityEngine.Color bc = default; int bv = -1; foreach (var kv in votes) if (kv.Value > bv) { bv = kv.Value; bc = kv.Key; } outC[y * N + x] = bc; oc[y * N + x] = true; continue; }
    UnityEngine.Color s = default; int n = 0; for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { int i = (y * 2 + dy) * S2 + x * 2 + dx; if (cov[i] && !flat[i]) { s += acc[i]; n++; } }
    if (n == 0) for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { int i = (y * 2 + dy) * S2 + x * 2 + dx; if (cov[i]) { s += acc[i]; n++; } }
    if (n > 0) { outC[y * N + x] = s / n; oc[y * N + x] = true; }
  }
  for (int pass = 0; pass < 8; pass++) { var add = new System.Collections.Generic.List<(int, UnityEngine.Color)>();
    for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { if (oc[y * N + x]) continue; for (int k2 = 0; k2 < 8; k2++) { int dx = new[] { 1, -1, 0, 0, 1, -1, 1, -1 }[k2], dy = new[] { 0, 0, 1, -1, 1, 1, -1, -1 }[k2]; int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= N || yy >= N || !oc[yy * N + xx]) continue; add.Add((y * N + x, outC[yy * N + xx])); break; } }   // the nearest graded texel, never a mix of two islands
    foreach (var (i, col) in add) { outC[i] = col; oc[i] = true; } }
  for (int i = 0; i < outC.Length; i++) outC[i].a = 1;
  var tex = new UnityEngine.Texture2D(N, N, UnityEngine.TextureFormat.RGBA32, false); tex.SetPixels(outC); tex.Apply();
  System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(outPath, UnityEditor.ImportAssetOptions.ForceUpdate);
  log.Append($"brow band {browBand}, cleared {browCleared}, browY {browY:F3}; scalp texels {scalp}; {name}: skin swatch {skinSwatch} (from {bn} neck verts), graded {graded} subtexels at {N}px, size {size:F2}, ao {aoDist:F3}\n");
  headGo.SetActive(hw); bodyGo.SetActive(bw);
}
bt.Apply(); System.IO.File.WriteAllText("Temp/RoomHead/grade.log", log.ToString()); return log.ToString();

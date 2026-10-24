// Painted-head pipeline, step 3 (run from an eval, after Tools/room_head_views.eval.cs and Tools/room_head_paint.py):
// the five paintings in Temp/RoomHead/paint are projected back onto each room player head at the painting's texel size
// (FaceDetail x the body's). Each texel takes the view that faces it most squarely and can see it (a ray toward that
// view must not hit the head first); skin and hair then take the restyle's own grade (PixelRestyle.GradeTexel:
// grain, crease dirt, occlusion from head and body) like the body, while eyes and brows stay as painted. The skin is
// matched to the body's neck. Skin behind the ears (the scalp under short hair) is hair. Removes the old Face features.
if (UnityEngine.Application.isPlaying) return "playing";
const float FaceDetail = 1.5f;
var onlyWho = System.IO.File.Exists("Temp/RoomHead/only.txt") ? System.IO.File.ReadAllText("Temp/RoomHead/only.txt").Trim() : "";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var log = new System.Text.StringBuilder();
var gridInfo = new System.Collections.Generic.Dictionary<string, (int G, float region, UnityEngine.Vector3 centre)>(); var icu = System.Globalization.CultureInfo.InvariantCulture;
foreach (var line in System.IO.File.ReadAllLines("Temp/RoomHead/grid.txt")) { var p = line.Split(' '); if (p.Length == 6) gridInfo[p[0]] = (int.Parse(p[1]), float.Parse(p[2], icu), new UnityEngine.Vector3(float.Parse(p[3], icu), float.Parse(p[4], icu), float.Parse(p[5], icu))); }
var dirs = new UnityEngine.Vector3[PixelRestyle.GradeAoRays]; var rng = new System.Random(11);
for (int i = 0; i < dirs.Length; i++) { float u = (float)rng.NextDouble(), v = (float)rng.NextDouble(); float th = 2 * UnityEngine.Mathf.PI * u, ph = UnityEngine.Mathf.Acos(2 * v - 1); dirs[i] = new UnityEngine.Vector3(UnityEngine.Mathf.Sin(ph) * UnityEngine.Mathf.Cos(th), UnityEngine.Mathf.Cos(ph), UnityEngine.Mathf.Sin(ph) * UnityEngine.Mathf.Sin(th)); }
foreach (var (headGo, bodyGo, name, who) in new[] { (bt.headA, bt.bodyA, "Room Head Woman", "woman"), (bt.headB, bt.bodyB, "Room Head Man", "man") })
{
  if (onlyWho != "" && who != onlyWho) continue;   // Temp/RoomHead/only.txt: "woman" or "man" projects that head alone
  bool hw = headGo.activeSelf, bw = bodyGo.activeSelf; headGo.SetActive(true); bodyGo.SetActive(true);
  var headBone = System.Array.Find(bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>().bones, x => x.name == "head");
  foreach (var par in new[] { headGo.transform, headBone }) { var oldF = par.Find("Face features"); if (oldF) UnityEngine.Object.DestroyImmediate(oldF.gameObject); }
  var smr = bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var mr = headGo.GetComponent<UnityEngine.Renderer>(); var ht = headGo.transform;
  var hm = headGo.TryGetComponent<UnityEngine.MeshFilter>(out var hmf) ? hmf.sharedMesh : headGo.GetComponent<UnityEngine.SkinnedMeshRenderer>().sharedMesh; var v = hm.vertices; var nr = hm.normals; var uv = hm.uv; var tr = hm.triangles;
  // (a skinned head is worked on in its bind pose, the pose room_head_views.eval.cs renders it in)
  var b = mr.bounds; var c = b.center; float hH = b.size.y; var f = bt.transform.forward; var rg = UnityEngine.Vector3.Cross(UnityEngine.Vector3.up, f).normalized;
  var (G, region, cView) = gridInfo[who]; c = cView;   // the centre the views were rendered on (a skinned head's bounds can move between runs)
  // the views, framed exactly as room_head_views.eval.cs rendered them
  var names = new[] { "front", "right", "back", "left", "top" };
  var vdir = new[] { f, rg, -f, -rg, UnityEngine.Vector3.up }; var vup = new[] { UnityEngine.Vector3.up, UnityEngine.Vector3.up, UnityEngine.Vector3.up, UnityEngine.Vector3.up, -f };
  var vR = new UnityEngine.Vector3[5]; var vU = new UnityEngine.Vector3[5]; var pc = new UnityEngine.Color32[5][]; var pk = new UnityEngine.Color32[5][];
  for (int k = 0; k < 5; k++)
  {
    var rot = UnityEngine.Quaternion.LookRotation(-vdir[k], vup[k]); vR[k] = rot * UnityEngine.Vector3.right; vU[k] = rot * UnityEngine.Vector3.up;
    var t1 = new UnityEngine.Texture2D(2, 2); t1.LoadImage(System.IO.File.ReadAllBytes($"Temp/RoomHead/paint/{who}_{names[k]}.png")); pc[k] = t1.GetPixels32();
    var t2 = new UnityEngine.Texture2D(2, 2); t2.LoadImage(System.IO.File.ReadAllBytes($"Temp/RoomHead/paint/{who}_{names[k]}_cls.png")); pk[k] = t2.GetPixels32();
  }
  // colour and class at a point seen from view k: the painted texel there, or the nearest painted one within two
  bool Sample(int k, UnityEngine.Vector3 w, out UnityEngine.Color col, out int cls)
  {
    var d = w - c; float px = (UnityEngine.Vector3.Dot(d, vR[k]) / region + .5f) * G, py = (UnityEngine.Vector3.Dot(d, vU[k]) / region + .5f) * G;
    int x0 = (int)px, y0 = (int)py; col = default; cls = 0;
    for (int rad = 0; rad <= 2; rad++)
      for (int dy = -rad; dy <= rad; dy++) for (int dx = -rad; dx <= rad; dx++)
      {
        if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != rad) continue; int x = x0 + dx, y = y0 + dy; if (x < 0 || y < 0 || x >= G || y >= G) continue;
        var p = pc[k][y * G + x]; if (p.a < 128) continue; col = p; cls = pk[k][y * G + x].r; return true;
      }
    return false;
  }
  // probes for visibility and occlusion: the head, and the posed body
  var probes = new System.Collections.Generic.List<UnityEngine.GameObject>();
  UnityEngine.GameObject Probe(UnityEngine.Mesh m, UnityEngine.Transform like) { var g = new UnityEngine.GameObject("probe") { hideFlags = UnityEngine.HideFlags.HideAndDontSave, layer = PixelRestyle.GradeProbeLayer }; g.transform.SetPositionAndRotation(like.position, like.rotation); g.transform.localScale = like.lossyScale; g.AddComponent<UnityEngine.MeshCollider>().sharedMesh = m; probes.Add(g); return g; }
  var baked = new UnityEngine.Mesh(); smr.BakeMesh(baked); Probe(baked, smr.transform); var headProbe = Probe(hm, ht); UnityEngine.Physics.SyncTransforms();
  var headCol = headProbe.GetComponent<UnityEngine.MeshCollider>();
  float worldScale = (smr.transform.TransformPoint(UnityEngine.Vector3.right) - smr.transform.position).magnitude;
  float size = smr.sharedMesh.bounds.size.magnitude, aoDist = UnityEngine.Mathf.Clamp(size * .08f, .015f, .3f) * worldScale;
  // texture size: the painting's texel size on this head's UVs
  string outPath = $"Assets/Game/Art/Synty Pixel/Characters/{name}/{name} pixel.png";
  double wa = 0, ua = 0; for (int t = 0; t < tr.Length; t += 3) { var p0 = ht.TransformPoint(v[tr[t]]); wa += UnityEngine.Vector3.Cross(ht.TransformPoint(v[tr[t + 1]]) - p0, ht.TransformPoint(v[tr[t + 2]]) - p0).magnitude * .5; var a2 = uv[tr[t + 1]] - uv[tr[t]]; var b2 = uv[tr[t + 2]] - uv[tr[t]]; ua += System.Math.Abs(a2.x * b2.y - a2.y * b2.x) * .5; }
  float tpu = G / region; int N = UnityEngine.Mathf.CeilToInt((float)(tpu * System.Math.Sqrt(wa / ua))), S2 = N * 2;
  UnityEngine.Vector3 Rel(UnityEngine.Vector3 w) { var d = w - c; return new UnityEngine.Vector3(UnityEngine.Vector3.Dot(d, f) / hH, d.y / hH, UnityEngine.Vector3.Dot(d, rg) / hH); }
  // the hair colour most used in the back view (for the scalp behind the ears)
  var hc = new System.Collections.Generic.Dictionary<UnityEngine.Color32, int>(); for (int i = 0; i < pc[2].Length; i++) if (pc[2][i].a > 128 && pk[2][i].r == 2) hc[pc[2][i]] = hc.TryGetValue(pc[2][i], out var n0) ? n0 + 1 : 1;
  UnityEngine.Color32 hairMain = default; int hn = -1; foreach (var kv in hc) if (kv.Value > hn) { hn = kv.Value; hairMain = kv.Key; }
  var sc = new System.Collections.Generic.Dictionary<UnityEngine.Color32, int>(); for (int i = 0; i < pc[0].Length; i++) if (pc[0][i].a > 128 && pk[0][i].r == 1) sc[pc[0][i]] = sc.TryGetValue(pc[0][i], out var s0) ? s0 + 1 : 1;
  UnityEngine.Color32 skinFill32 = default; int sn = -1; foreach (var kv in sc) if (kv.Value > sn) { sn = kv.Value; skinFill32 = kv.Key; } UnityEngine.Color skinFill = skinFill32;
  var bakeT = new UnityEngine.Texture2D(2, 2); bakeT.LoadImage(System.IO.File.ReadAllBytes($"Temp/RoomHead/sw_{who}_cls.png")); int BN = bakeT.width; var bakeCls = bakeT.GetPixels32(); int fixedKind = 0;
  bool SampleKind(int k, UnityEngine.Vector3 w, int kind, out UnityEngine.Color col)
  {
    var d = w - c; int x0 = (int)((UnityEngine.Vector3.Dot(d, vR[k]) / region + .5f) * G), y0 = (int)((UnityEngine.Vector3.Dot(d, vU[k]) / region + .5f) * G); col = default;
    for (int rad = 0; rad <= 4; rad++) for (int dy = -rad; dy <= rad; dy++) for (int dx = -rad; dx <= rad; dx++)
    { if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)) != rad) continue; int x = x0 + dx, y = y0 + dy; if (x < 0 || y < 0 || x >= G || y >= G) continue;
      if (pc[k][y * G + x].a > 128 && pk[k][y * G + x].r == kind) { col = pc[k][y * G + x]; return true; } }
    return false;
  }
  int ffx0 = G, ffx1 = -1, ffy0 = G, ffy1 = -1;
  for (int y = 0; y < G; y++) for (int x = 0; x < G; x++) { int k = pk[0][y * G + x].r; if (k < 3 || pc[0][y * G + x].a < 128) continue; ffx0 = System.Math.Min(ffx0, x); ffx1 = System.Math.Max(ffx1, x); ffy0 = System.Math.Min(ffy0, y); ffy1 = System.Math.Max(ffy1, y); }
  bool InFeatures(UnityEngine.Vector3 w) { var d = w - c; float fx = (UnityEngine.Vector3.Dot(d, vR[0]) / region + .5f) * G, fy = (UnityEngine.Vector3.Dot(d, vU[0]) / region + .5f) * G; return UnityEngine.Vector3.Dot(d, f) > 0 && fx >= ffx0 - 4 && fx <= ffx1 + 4 && fy >= ffy0 - 4 && fy <= ffy1 + 4; }
  // skin or not for every pixel of the full-size bake (as Tools/room_head_swatches.py decides it)
  var bakeFullT = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>($"Assets/Game/Meshy/RoomHead{(who == "woman" ? "Woman" : "Man")}/{name} baked.png");
  var bft = new UnityEngine.Texture2D(2, 2); bft.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(bakeFullT))); int FW = bft.width; var bpx = bft.GetPixels();
  var fullSkin = new bool[bpx.Length];
  { bool Sk0(UnityEngine.Color q) => q.r > q.g * 1.04f && q.g > q.b * .98f && q.r > q.b * 1.1f && (q.r + q.g + q.b) / 3f > .3f;
    UnityEngine.Vector2 Ch(UnityEngine.Color q) { float t = q.r + q.g + q.b + 1e-6f; return new UnityEngine.Vector2(q.r / t, q.g / t); }
    var cx = new System.Collections.Generic.List<float>(); var cy = new System.Collections.Generic.List<float>(); var lm = new System.Collections.Generic.List<float>();
    foreach (var q in bpx) if (Sk0(q)) { var ch = Ch(q); cx.Add(ch.x); cy.Add(ch.y); lm.Add((q.r + q.g + q.b) / 3f); }
    cx.Sort(); cy.Sort(); lm.Sort(); var med = new UnityEngine.Vector2(cx[cx.Count / 2], cy[cy.Count / 2]); float medL = lm[lm.Count / 2];
    for (int i = 0; i < bpx.Length; i++) { var q = bpx[i]; var ch = Ch(q); float d = UnityEngine.Mathf.Abs(ch.x - med.x) + UnityEngine.Mathf.Abs(ch.y - med.y); float l = (q.r + q.g + q.b) / 3f;
      fullSkin[i] = (Sk0(q) && d < .045f) || (d < .1f && l > .2f && l < medL * 1.02f && q.r >= q.b); } }
  // skin enclosed by hair: most rays out from it hit the head within a short distance
  UnityEngine.Color hairShade = (UnityEngine.Color)hairMain * .45f; hairShade.a = 1; int enclosedN = 0;
  float enclosed(UnityEngine.Vector3 p0, UnityEngine.Vector3 n0)
  {
    if (UnityEngine.Vector3.Dot(n0, f) > .5f) return 0f;   // the face itself
    int hitN = 0, tot = 0; var o = p0 + n0 * (hH * .004f);
    foreach (var d0 in dirs) { var d = UnityEngine.Vector3.Dot(d0, n0) < 0 ? -d0 : d0; tot++;
      if (headCol.Raycast(new UnityEngine.Ray(o, d), out var hh, hH * .25f)) { var hu = hh.textureCoord; if (!fullSkin[UnityEngine.Mathf.Clamp((int)(hu.y * FW), 0, FW - 1) * FW + UnityEngine.Mathf.Clamp((int)(hu.x * FW), 0, FW - 1)]) hitN++; } }   // only hair overhead counts
    float frac = hitN / (float)tot; float lo = Rel(p0).y < -.2f ? .25f : .6f, hi = lo + .4f;   // the neck takes shadow sooner than the face
    float t = UnityEngine.Mathf.Clamp01((frac - lo) / (hi - lo)); return t * t * (3 - 2 * t);
  }
  var acc = new UnityEngine.Color[S2 * S2]; var cov = new bool[S2 * S2]; var flat = new bool[S2 * S2]; var isSkin = new bool[S2 * S2]; int[] used = new int[5]; int scalp = 0, unseen = 0, innerHair = 0;
  for (int rp = 0; rp < 2; rp++)   // pass 0: texels whose centre lies on the triangle; pass 1: a one-texel rim where nothing painted yet (never a margin that scales with the triangle)
  for (int t = 0; t < tr.Length; t += 3)
  {
    int i0 = tr[t], i1 = tr[t + 1], i2 = tr[t + 2]; UnityEngine.Vector2 a = uv[i0] * S2, bb = uv[i1] * S2, cc = uv[i2] * S2;
    float den = (bb.y - cc.y) * (a.x - cc.x) + (cc.x - bb.x) * (a.y - cc.y); if (UnityEngine.Mathf.Abs(den) < 1e-9f) continue;
    int x0 = UnityEngine.Mathf.Max(0, (int)UnityEngine.Mathf.Floor(UnityEngine.Mathf.Min(a.x, UnityEngine.Mathf.Min(bb.x, cc.x))) - 2), x1 = UnityEngine.Mathf.Min(S2 - 1, (int)UnityEngine.Mathf.Ceil(UnityEngine.Mathf.Max(a.x, UnityEngine.Mathf.Max(bb.x, cc.x))) + 2);
    int y0 = UnityEngine.Mathf.Max(0, (int)UnityEngine.Mathf.Floor(UnityEngine.Mathf.Min(a.y, UnityEngine.Mathf.Min(bb.y, cc.y))) - 2), y1 = UnityEngine.Mathf.Min(S2 - 1, (int)UnityEngine.Mathf.Ceil(UnityEngine.Mathf.Max(a.y, UnityEngine.Mathf.Max(bb.y, cc.y))) + 2);
    var fn = ht.TransformDirection(UnityEngine.Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0])).normalized;
    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
    {
      int idx = y * S2 + x; if (cov[idx]) continue; float px = x + .5f, py = y + .5f;
      float w1 = ((bb.y - cc.y) * (px - cc.x) + (cc.x - bb.x) * (py - cc.y)) / den, w2 = ((cc.y - a.y) * (px - cc.x) + (a.x - cc.x) * (py - cc.y)) / den, w3 = 1 - w1 - w2;
      if (rp == 0) { if (w1 < -1e-4f || w2 < -1e-4f || w3 < -1e-4f) continue; }
      else
      {
        var q = new UnityEngine.Vector2(px, py);
        float SegD(UnityEngine.Vector2 p0, UnityEngine.Vector2 p1) { var e = p1 - p0; float tt = UnityEngine.Mathf.Clamp01(UnityEngine.Vector2.Dot(q - p0, e) / UnityEngine.Mathf.Max(1e-9f, e.sqrMagnitude)); return (p0 + e * tt - q).magnitude; }
        if (UnityEngine.Mathf.Min(SegD(a, bb), UnityEngine.Mathf.Min(SegD(bb, cc), SegD(cc, a))) > 1.6f) continue;
        w1 = UnityEngine.Mathf.Clamp01(w1); w2 = UnityEngine.Mathf.Clamp01(w2); float ws = w1 + w2; if (ws > 1) { w1 /= ws; w2 /= ws; } w3 = 1 - w1 - w2;   // sample the nearest point of the triangle
      }
      var lp = v[i0] * w1 + v[i1] * w2 + v[i2] * w3; var wp = ht.TransformPoint(lp); var wn = ht.TransformDirection((nr[i0] * w1 + nr[i1] * w2 + nr[i2] * w3).normalized).normalized;
      // views by how squarely they face this point, the first that can see it
      var order = new System.Collections.Generic.List<int> { 0, 1, 2, 3, 4 }; order.Sort((p, q) => UnityEngine.Vector3.Dot(fn, vdir[q]).CompareTo(UnityEngine.Vector3.Dot(fn, vdir[p])));
      UnityEngine.Color col = default; int cls = 0; bool got = false;
      foreach (var k in order)
      {
        if (UnityEngine.Vector3.Dot(fn, vdir[k]) < -.05f) break;
        var origin = wp + vdir[k] * (hH * .004f);
        if (headCol.Raycast(new UnityEngine.Ray(origin, vdir[k]), out var hit, region * 3)) continue;   // hidden from this view
        if (Sample(k, wp, out col, out cls)) { got = true; used[k]++; break; }
      }
      // the inside of a hair curtain (its face turned in toward the head's middle, at the cheeks and either side of the neck)
      // is hair, whatever the bake has there (its rays reached the skin behind) and whether or not a view sees it; only on
      // her long hair (his is short, and the backs of his ears face in too)
      { var fh = new UnityEngine.Vector3(fn.x, 0, fn.z); var rh = new UnityEngine.Vector3(wp.x - c.x, 0, wp.z - c.z);
        if (who == "woman" && fh.magnitude > .5f && rh.magnitude > 1e-4f && UnityEngine.Vector3.Dot(fh.normalized, rh.normalized) < -.3f && Rel(wp).x < .25f)
        { if (!SampleKind(order[0], wp, 2, out col)) col = hairMain; cls = 2; got = true; innerHair++; goto graded; } }
      if (!got) { unseen++; continue; }
      // hair or skin is the head's own business: where the painting and the bake disagree (a thin hair spike seen against
      // the forehead, skin at a hairline) the bake decides, and the colour comes from the nearest painted texel of that kind
      { var tuv = uv[i0] * w1 + uv[i1] * w2 + uv[i2] * w3; int bxi = UnityEngine.Mathf.Clamp((int)(tuv.x * FW), 0, FW - 1), byi = UnityEngine.Mathf.Clamp((int)(tuv.y * FW), 0, FW - 1);
        int want = fullSkin[byi * FW + bxi] ? 1 : 2;   // skin or hair from the full-size bake at this very spot
        if (InFeatures(wp) && UnityEngine.Vector3.Dot(fn, f) > .3f) want = 0;   // round the eyes and brows the painting decides: the bake has the Meshy brows and eye bags there, which made dark blobs
        if (want != 0 && (cls == 1 || cls == 2) && cls != want) { if (!SampleKind(order[0], wp, want, out col)) col = want == 2 ? (UnityEngine.Color)hairMain : skinFill; cls = want; fixedKind++; } }
      var rel = Rel(wp);
      if (fn.y < -.5f && rel.x > -.15f)   // the underside of the chin and jaw: no view sees it square; the front painting's colour there, in shadow
      { var d0 = wp - c; int fx = (int)((UnityEngine.Vector3.Dot(d0, vR[0]) / region + .5f) * G), fy = (int)((UnityEngine.Vector3.Dot(d0, vU[0]) / region + .5f) * G);
        { int[] kn = new int[3]; UnityEngine.Color[] kc = new UnityEngine.Color[3]; bool[] kg = new bool[3];   // the kind most of the jaw just above has (his beard, her skin), so a beard's underside is beard
          for (int dy2 = 0; dy2 <= 6; dy2++) { int yy2 = fy + dy2; if (fx < 0 || fx >= G || yy2 < 0 || yy2 >= G) continue; var q = pc[0][yy2 * G + fx]; int qk = pk[0][yy2 * G + fx].r; if (q.a > 128 && (qk == 1 || qk == 2)) { kn[qk]++; if (!kg[qk]) { kc[qk] = q; kg[qk] = true; } } }
          int bestK = kn[2] > kn[1] ? 2 : 1; if (kg[bestK]) { cls = bestK; col = kc[bestK]; }   /* the grade's occlusion shades it */ } }
      if (cls == 1 && rel.x < -.12f && rel.y > -.3f) { cls = 2; col = hairMain; scalp++; }   // behind the ears: scalp under the hair
      if (cls >= 3) { bool onHair = bakeCls[UnityEngine.Mathf.Clamp((int)((uv[i0] * w1 + uv[i1] * w2 + uv[i2] * w3).y * BN), 0, BN - 1) * BN + UnityEngine.Mathf.Clamp((int)((uv[i0] * w1 + uv[i1] * w2 + uv[i2] * w3).x * BN), 0, BN - 1)].r == 1; if (onHair) { cls = 2; if (!SampleKind(order[0], wp, 2, out col)) col = hairMain; } else { cls = 1; col = skinFill; } }
      graded:
      float ao = PixelRestyle.Occlusion(wp, wn, dirs, aoDist);
      float hairOver = cls == 1 ? enclosed(wp, wn) : 0f; if (hairOver > 0) enclosedN++;   // skin under the hair is in its shadow: darkened after grading, more the more hair covers it
      acc[idx] = PixelRestyle.GradeTexel(col, smr.transform.InverseTransformPoint(wp), smr.transform.InverseTransformDirection(wn).normalized, ao, size);
      if (hairOver > 0) acc[idx] = UnityEngine.Color.Lerp(acc[idx], hairShade, hairOver * .85f);
      cov[idx] = true; isSkin[idx] = cls == 1;
    }
  }
  UnityEngine.Matrix4x4? toHead = null;   // mesh space -> head bone space at bind, for a skinned head
  if (headGo.TryGetComponent<UnityEngine.SkinnedMeshRenderer>(out var hsk2)) { int hi = System.Array.FindIndex(hsk2.bones, x => x.name == "head"); toHead = hsk2.sharedMesh.bindposes[hi]; }
  // Face features: the eyes and brows on a thin copy of the face, mapped straight onto the front painting's grid, so
  // every painted texel of an eye lands as painted (resampled onto the head's own texels a one-texel pupil doubles or
  // drops out). Only eye and brow texels are opaque.
  {
    int fx0 = G, fx1 = -1, fy0 = G, fy1 = -1;
    for (int y = 0; y < G; y++) for (int x = 0; x < G; x++) { int k = pk[0][y * G + x].r; if (k < 3 || pc[0][y * G + x].a < 128) continue; fx0 = System.Math.Min(fx0, x); fx1 = System.Math.Max(fx1, x); fy0 = System.Math.Min(fy0, y); fy1 = System.Math.Max(fy1, y); }
    fx0 -= 2; fx1 += 2; fy0 -= 2; fy1 += 2;
    var opx = new UnityEngine.Color32[G * G];
    for (int i = 0; i < opx.Length; i++) { var q = pc[0][i]; opx[i] = pk[0][i].r >= 3 && q.a > 128 ? q : new UnityEngine.Color32(q.r, q.g, q.b, 0); }
    var nv = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nn = new System.Collections.Generic.List<UnityEngine.Vector3>(); var nu = new System.Collections.Generic.List<UnityEngine.Vector2>(); var nt = new System.Collections.Generic.List<int>();
    float push = .007f * hH / ht.lossyScale.x;
    UnityEngine.Vector2 Fuv(UnityEngine.Vector3 w) { var d = w - c; return new UnityEngine.Vector2(UnityEngine.Vector3.Dot(d, vR[0]) / region + .5f, UnityEngine.Vector3.Dot(d, vU[0]) / region + .5f); }
    for (int t = 0; t < tr.Length; t += 3)
    {
      var wc = ht.TransformPoint((v[tr[t]] + v[tr[t + 1]] + v[tr[t + 2]]) / 3f); var fuv = Fuv(wc) * G;
      if (fuv.x < fx0 || fuv.x > fx1 || fuv.y < fy0 || fuv.y > fy1) continue;
      var fn2 = ht.TransformDirection(UnityEngine.Vector3.Cross(v[tr[t + 1]] - v[tr[t]], v[tr[t + 2]] - v[tr[t]])).normalized; if (UnityEngine.Vector3.Dot(fn2, f) < .5f) continue;   // turned further from the front, a front-grid texel stretches into a long wedge (brow ends)
      if (headCol.Raycast(new UnityEngine.Ray(wc + f * (hH * .004f), f), out var hh, region * 3)) continue;   // hidden from the front (under hair)
      for (int k = 0; k < 3; k++)
      {
        int i = tr[t + k]; var pw = ht.TransformPoint(v[i]) + ht.TransformDirection(nr[i]).normalized * (.007f * hH);
        // in the head bone's space, the eyes turn with the head; from the mesh's own bind pose, never the live bone (its pose moves in the editor)
        nv.Add(ht.InverseTransformPoint(pw)); nn.Add(nr[i]);   // in the head object's own space (a skinned head's overlay is skinned to the head bone below)
        nu.Add(Fuv(ht.TransformPoint(v[i]))); nt.Add(nv.Count - 1);
      }
    }
    var fm = new UnityEngine.Mesh { name = name + " features" }; fm.SetVertices(nv); fm.SetNormals(nn); fm.SetUVs(0, nu); fm.SetTriangles(nt, 0); fm.RecalculateBounds();
    string dir = $"Assets/Game/Art/Synty Pixel/Characters/{name}";
    UnityEditor.AssetDatabase.DeleteAsset($"{dir}/{name} features.asset"); UnityEditor.AssetDatabase.CreateAsset(fm, $"{dir}/{name} features.asset");
    var ft = new UnityEngine.Texture2D(G, G, UnityEngine.TextureFormat.RGBA32, false); ft.SetPixels32(opx); ft.Apply();
    string fp = $"{dir}/{name} features.png"; System.IO.File.WriteAllBytes(fp, ft.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(fp);
    var fti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(fp); fti.filterMode = UnityEngine.FilterMode.Point; fti.mipmapEnabled = false; fti.npotScale = UnityEditor.TextureImporterNPOTScale.None; fti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; fti.wrapMode = UnityEngine.TextureWrapMode.Clamp; fti.alphaIsTransparency = false; fti.SaveAndReimport();
    string fmp = $"{dir}/{name} features.mat"; var fmat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(fmp);
    if (!fmat) { fmat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(fmat, fmp); }
    fmat.SetTexture("_BaseMap", UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(fp)); fmat.SetFloat("_AlphaClip", 1); fmat.SetFloat("_Cutoff", .5f); fmat.EnableKeyword("_ALPHATEST_ON"); fmat.SetFloat("_Smoothness", 0); fmat.SetFloat("_Cull", 2); UnityEditor.EditorUtility.SetDirty(fmat);
    // under the head object, so it shows and hides with it (both bodies share one skeleton); a skinned head's overlay is
    // skinned to the head bone with the head mesh's own bind pose, so the eyes turn with the head
    var fgo = new UnityEngine.GameObject("Face features"); fgo.layer = headGo.layer; fgo.transform.SetParent(ht, false);
    UnityEngine.Renderer fmr;
    if (toHead.HasValue)
    { var hbIdx = new UnityEngine.BoneWeight[fm.vertexCount]; for (int i = 0; i < hbIdx.Length; i++) hbIdx[i] = new UnityEngine.BoneWeight { boneIndex0 = 0, weight0 = 1 };
      fm.boneWeights = hbIdx; fm.bindposes = new[] { toHead.Value }; UnityEditor.EditorUtility.SetDirty(fm); UnityEditor.AssetDatabase.SaveAssets();
      var fsk = fgo.AddComponent<UnityEngine.SkinnedMeshRenderer>(); fsk.sharedMesh = fm; fsk.bones = new[] { headBone }; fsk.rootBone = headBone; fsk.updateWhenOffscreen = true; fmr = fsk; }
    else { fgo.AddComponent<UnityEngine.MeshFilter>().sharedMesh = fm; fmr = fgo.AddComponent<UnityEngine.MeshRenderer>(); }
    fmr.sharedMaterial = fmat; fmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fmr.probeAnchor = mr.probeAnchor;
    log.Append($"features {nt.Count / 3} tris; ");
  }
  foreach (var g in probes) UnityEngine.Object.DestroyImmediate(g); UnityEngine.Object.DestroyImmediate(baked);
  // skin to the body's neck (front of the neck, as it is seen)
  var bodyPng = new UnityEngine.Texture2D(2, 2); bodyPng.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap"))));
  float Med(System.Collections.Generic.List<float> l) { l.Sort(); return l.Count > 0 ? l[l.Count * 6 / 10] : 1; }
  var br = new System.Collections.Generic.List<float>(); var bg = new System.Collections.Generic.List<float>(); var bb2 = new System.Collections.Generic.List<float>();
  { var bk = new UnityEngine.Mesh(); smr.BakeMesh(bk); var bv = bk.vertices; var bnrm = bk.normals; var buv = smr.sharedMesh.uv;
    var nbT = System.Array.Find(smr.bones, x => x.name == "neck_01"); var hbT = System.Array.Find(smr.bones, x => x.name == "head"); var mid = (nbT.position + hbT.position) * .5f; float rad = (hbT.position - nbT.position).magnitude * .9f;
    var inv = new UnityEngine.Vector3(1 / smr.transform.lossyScale.x, 1 / smr.transform.lossyScale.y, 1 / smr.transform.lossyScale.z);
    for (int i = 0; i < bv.Length; i++) { var w = smr.transform.TransformPoint(UnityEngine.Vector3.Scale(bv[i], inv)); if ((w - mid).magnitude > rad) continue; if (UnityEngine.Vector3.Dot(smr.transform.TransformDirection(bnrm[i]).normalized, f) < .4f) continue;
      var cc2 = bodyPng.GetPixelBilinear(buv[i].x, buv[i].y); if (!(cc2.r > cc2.b * 1.05f)) continue; br.Add(cc2.r); bg.Add(cc2.g); bb2.Add(cc2.b); }
    UnityEngine.Object.DestroyImmediate(bk); }
  var hr = new System.Collections.Generic.List<float>(); var hg = new System.Collections.Generic.List<float>(); var hb2 = new System.Collections.Generic.List<float>();
  for (int i = 0; i < acc.Length; i++) if (cov[i] && isSkin[i]) { hr.Add(acc[i].r); hg.Add(acc[i].g); hb2.Add(acc[i].b); }
  var kS = new UnityEngine.Color(Med(br) / Med(hr), Med(bg) / Med(hg), Med(bb2) / Med(hb2), 1);
  { var trim = System.IO.File.Exists("Temp/RoomHead/trim2.txt") ? System.IO.File.ReadAllText("Temp/RoomHead/trim2.txt").Trim().Split('|')[who == "woman" ? 0 : 1].Split(',') : new[] { "1", "1", "1" };
    kS = new UnityEngine.Color(kS.r * float.Parse(trim[0], System.Globalization.CultureInfo.InvariantCulture), kS.g * float.Parse(trim[1], System.Globalization.CultureInfo.InvariantCulture), kS.b * float.Parse(trim[2], System.Globalization.CultureInfo.InvariantCulture), 1); }
  for (int i = 0; i < acc.Length; i++) if (cov[i] && isSkin[i]) acc[i] = new UnityEngine.Color(UnityEngine.Mathf.Clamp01(acc[i].r * kS.r), UnityEngine.Mathf.Clamp01(acc[i].g * kS.g), UnityEngine.Mathf.Clamp01(acc[i].b * kS.b), 1);
  // down to the final size: eyes and brows by the colour most samples have, the rest averaged; then pad the islands
  var outC = new UnityEngine.Color[N * N]; var oc = new bool[N * N]; var skinK = new bool[N * N];   // skin or hair per final texel
  for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
  {
    var votes = new System.Collections.Generic.Dictionary<UnityEngine.Color, int>(); int nf = 0; UnityEngine.Color s = default; int n = 0;
    int nSk = 0, nNon = 0;
    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { int i = (y * 2 + dy) * S2 + x * 2 + dx; if (!cov[i]) continue; if (flat[i]) { votes[acc[i]] = votes.TryGetValue(acc[i], out var vn) ? vn + 1 : 1; nf++; } else if (isSkin[i]) nSk++; else nNon++; }
    bool wantSkin = nSk >= nNon;   // one kind per texel: skin and hair never blend
    for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) { int i = (y * 2 + dy) * S2 + x * 2 + dx; if (!cov[i] || flat[i] || isSkin[i] != wantSkin) continue; s += acc[i]; n++; }
    if (nf >= 2 || (nf > 0 && n == 0)) { UnityEngine.Color bc = default; int bv2 = -1; foreach (var kv in votes) if (kv.Value > bv2) { bv2 = kv.Value; bc = kv.Key; } outC[y * N + x] = bc; oc[y * N + x] = true; }
    else if (n > 0) { outC[y * N + x] = s / n; oc[y * N + x] = true; skinK[y * N + x] = wantSkin; }
  }
  for (int pass = 0; pass < 8; pass++) { var add = new System.Collections.Generic.List<(int, UnityEngine.Color)>();
    for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { if (oc[y * N + x]) continue; for (int k2 = 0; k2 < 8; k2++) { int dx = new[] { 1, -1, 0, 0, 1, -1, 1, -1 }[k2], dy = new[] { 0, 0, 1, -1, 1, 1, -1, -1 }[k2]; int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= N || yy >= N || !oc[yy * N + xx]) continue; add.Add((y * N + x, outC[yy * N + xx])); skinK[y * N + x] = skinK[yy * N + xx]; break; } }
    foreach (var (i, col) in add) { outC[i] = col; oc[i] = true; } }
  // small clumps: a patch of skin up to 12 texels with mostly hair round it becomes hair, and the other way round (a few
  // hair texels on an ear or a cheek, skin specks in a beard); single texels alone are not enough
  { int flipped = 0; var lab = new int[N * N];
    for (int it = 0; it < 2; it++)
    {
      System.Array.Clear(lab, 0, lab.Length); int next = 0;
      for (int s0 = 0; s0 < N * N; s0++)
      {
        if (!oc[s0] || lab[s0] != 0) continue; next++; var comp = new System.Collections.Generic.List<int> { s0 }; lab[s0] = next; bool k0 = skinK[s0];
        for (int q = 0; q < comp.Count && comp.Count <= 13; q++) { int i = comp[q], x = i % N, y = i / N;
          foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue; int j = yy * N + xx; if (!oc[j] || lab[j] != 0 || skinK[j] != k0) continue; lab[j] = next; comp.Add(j); } }
        if (comp.Count > 12) continue;
        int other = 0, same = 0; UnityEngine.Color sum = default; var inComp = new System.Collections.Generic.HashSet<int>(comp);
        foreach (var i in comp) { int x = i % N, y = i / N; for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= N || yy >= N) continue; int j = yy * N + xx; if (!oc[j] || inComp.Contains(j)) continue; if (skinK[j] != k0) { other++; sum += outC[j]; } else same++; } }
        if (other == 0 || other < (other + same) * .7f) continue;
        var col = sum / other; col.a = 1; foreach (var i in comp) { outC[i] = col; skinK[i] = !k0; } flipped += comp.Count;
      }
    }
    log.Append($"clumps flipped {flipped} texels; "); }
  // one pass of clean edges: a texel with five or more of its eight neighbours of the other kind joins them (single
  // texels jutting out of a beard's or a hairline's edge), its colour theirs
  { var cpK = (bool[])skinK.Clone(); var cpC = (UnityEngine.Color[])outC.Clone(); int edged = 0;
    for (int y = 1; y < N - 1; y++) for (int x = 1; x < N - 1; x++)
    {
      int i = y * N + x; if (!oc[i]) continue; int other = 0, tot = 0; UnityEngine.Color sum = default;
      for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { if (dx == 0 && dy == 0) continue; int j = (y + dy) * N + x + dx; if (!oc[j]) continue; tot++; if (cpK[j] != cpK[i]) { other++; sum += cpC[j]; } }
      if (tot >= 7 && other >= 5) { var col = sum / other; col.a = 1; outC[i] = col; skinK[i] = !cpK[i]; edged++; }
    }
    log.Append($"edge texels {edged}; "); }
  // stray texels: a skin-coloured texel with six or more hair neighbours takes their colour, and the other way round
  { bool SkC(UnityEngine.Color q) => q.r > q.g * 1.04f && q.g > q.b * .98f && q.r > q.b * 1.1f && q.grayscale > .33f && q.r < q.g * 1.42f;
    int fixedS = 0;
    for (int it = 0; it < 2; it++)
    {
      var cp = (UnityEngine.Color[])outC.Clone();
      for (int y = 1; y < N - 1; y++) for (int x = 1; x < N - 1; x++)
      {
        bool me = SkC(cp[y * N + x]); int same = 0, other = 0; UnityEngine.Color oc2 = default;
        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) { if (dx == 0 && dy == 0) continue; var q = cp[(y + dy) * N + x + dx]; if (SkC(q) == me) same++; else { other++; oc2 = q; } }
        if (other >= 6) { outC[y * N + x] = oc2; fixedS++; }
      }
    }
    log.Append($"stray texels {fixedS}; "); }
  for (int i = 0; i < outC.Length; i++) outC[i].a = 1;
  var tex = new UnityEngine.Texture2D(N, N, UnityEngine.TextureFormat.RGBA32, false); tex.SetPixels(outC); tex.Apply();
  System.IO.File.WriteAllBytes(outPath, tex.EncodeToPNG()); UnityEditor.AssetDatabase.ImportAsset(outPath, UnityEditor.ImportAssetOptions.ForceUpdate);
  log.Append($"enclosed skin {enclosedN}; kind fixed {fixedKind}; {name}: {N}px ({tpu:F2} a unit), views used {string.Join("/", used)}, unseen {unseen}, inner hair {innerHair}, scalp {scalp}, skin x{kS}\n");
  headGo.SetActive(hw); bodyGo.SetActive(bw);
}
bt.Apply(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bt.gameObject.scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(bt.gameObject.scene);
System.IO.File.WriteAllText("Temp/RoomHead/project.log", log.ToString());
return log.ToString();

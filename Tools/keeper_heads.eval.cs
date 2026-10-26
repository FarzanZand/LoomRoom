// The tavern keeper's heads (KeeperFace on Characters/NPCs/Dungeon merchant): run with unity command eval_file.
// 1. The merchant body (SM_Chr_Merchant_01, its pixel copy) gets a copy with every triangle weighted to the head, eyes,
//    eyebrows and neck cut away ("SM_Chr_Merchant_01 No Head"); his collar stays.
// 2. Each Meshy head (Tools/keeper_head_bake.py: Meshy/KeeperHead<X>/Keeper Head <X> obj.txt and its baked painting) is
//    restyled by PixelRestyle.Build at the body's own texel density, measured from the body (house grade: the
//    painting's colour per texel, occlusion, crease dirt, grain), and set under the head bone, centred over the neck,
//    its top `drop` below the Synty crown (negative for horns), its height `size` x the crown's height over the neck
//    bone; the neck goes on down into the collar.
// Config: Tools/keeper_heads.txt, one line per head: <Name>|<size>|<drop, m>|<turn y>|<forward offset, m>
if (UnityEngine.Application.isPlaying) return "stop Play first";
UnityEditor.AssetDatabase.Refresh();
const string PrefabPath = "Assets/Game/Characters/NPCs/Dungeon merchant.prefab";
var ci = System.Globalization.CultureInfo.InvariantCulture; var log = new System.Text.StringBuilder();
var root = UnityEditor.PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
  var smr = System.Array.Find(root.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true), s => s.name == "SM_Chr_Merchant_01");
  var kf = root.GetComponent<KeeperFace>() ?? root.AddComponent<KeeperFace>();
  var whole = kf.wholeBody != null ? kf.wholeBody : smr.sharedMesh;
  UnityEngine.Animator anim0() => root.GetComponentInChildren<UnityEngine.Animator>(true);
  // 1. the headless body
  int Bone(string n) => System.Array.FindIndex(smr.bones, b => b.name == n);
  int hb = Bone("Head"), eb = Bone("Eyes"), bb = Bone("Eyebrows"), nb = Bone("Neck");
  var w = whole.boneWeights;
  bool Cut(int i) => i == hb || i == eb || i == bb || i == nb;   // his neck too: the Meshy heads bring their own, down into the collar
  float Wt(UnityEngine.BoneWeight b) { float s = 0; if (Cut(b.boneIndex0)) s += b.weight0; if (Cut(b.boneIndex1)) s += b.weight1; if (Cut(b.boneIndex2)) s += b.weight2; if (Cut(b.boneIndex3)) s += b.weight3; return s; }
  var tris = whole.triangles; var kept = new System.Collections.Generic.List<int>(); int cut = 0;
  smr.sharedMesh = whole; var baked = new UnityEngine.Mesh(); smr.BakeMesh(baked, true); var bv = baked.vertices;
  float crown = float.MinValue;
  for (int i = 0; i < tris.Length; i += 3)
  {
    if (Wt(w[tris[i]]) > .5f && Wt(w[tris[i + 1]]) > .5f && Wt(w[tris[i + 2]]) > .5f)
    { cut++; for (int e = 0; e < 3; e++) crown = UnityEngine.Mathf.Max(crown, smr.transform.TransformPoint(bv[tris[i + e]]).y); continue; }
    kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
  }
  string bodyDir = System.IO.Path.GetDirectoryName(UnityEditor.AssetDatabase.GetAssetPath(whole)).Replace('\\', '/');
  string headlessPath = bodyDir + "/SM_Chr_Merchant_01 No Head.asset";
  // his white undershirt round the neck showed under the thinner monster necks: those triangles (pale, at the collar's
  // height) take the darkest brown in his texture instead, the shadow inside a collar, on vertices of their own
  var bodyTex0 = (UnityEngine.Texture2D)smr.sharedMaterial.GetTexture("_BaseMap");
  var bti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(bodyTex0));
  if (!bti.isReadable) { bti.isReadable = true; bti.SaveAndReimport(); bodyTex0 = (UnityEngine.Texture2D)smr.sharedMaterial.GetTexture("_BaseMap"); }
  var wuv = whole.uv; var wv = new System.Collections.Generic.List<UnityEngine.Vector3>(whole.vertices); var wn = new System.Collections.Generic.List<UnityEngine.Vector3>(whole.normals);
  var wt = new System.Collections.Generic.List<UnityEngine.Vector4>(whole.tangents); var wu = new System.Collections.Generic.List<UnityEngine.Vector2>(wuv); var wb = new System.Collections.Generic.List<UnityEngine.BoneWeight>(w);
  var darkPx = bodyTex0.GetPixels(); int darkI = 0; float darkD = 9;
  for (int i = 0; i < darkPx.Length; i++) { var c = darkPx[i]; float d = UnityEngine.Mathf.Abs(c.r - .1f) + UnityEngine.Mathf.Abs(c.g - .075f) + UnityEngine.Mathf.Abs(c.b - .055f); if (d < darkD) { darkD = d; darkI = i; } }
  var darkUv = new UnityEngine.Vector2((darkI % bodyTex0.width + .5f) / bodyTex0.width, (darkI / bodyTex0.width + .5f) / bodyTex0.height);
  var neckT = anim0().GetBoneTransform(UnityEngine.HumanBodyBones.Neck); int shirt = 0;
  for (int i = 0; i < kept.Count; i += 3)
  {
    var wp = UnityEngine.Vector3.zero; for (int e = 0; e < 3; e++) wp += smr.transform.TransformPoint(bv[kept[i + e]]); wp /= 3;
    if (wp.y < neckT.position.y - .32f || new UnityEngine.Vector2(wp.x - neckT.position.x, wp.z - neckT.position.z).magnitude > .2f) continue;   // round the neck only (his T-posed arms are at that height too)
    bool pale = false;
    foreach (var q in new[] { wuv[kept[i]], wuv[kept[i + 1]], wuv[kept[i + 2]], (wuv[kept[i]] + wuv[kept[i + 1]] + wuv[kept[i + 2]]) / 3f })
    { UnityEngine.Color.RGBToHSV(bodyTex0.GetPixelBilinear(q.x, q.y), out float hue, out float sat, out float val); if ((sat < .22f && val > .45f) || (hue < .09f && sat < .45f && val > .6f)) pale = true; }   // his shirt, and his own throat skin
    if (!pale) continue;
    for (int e = 0; e < 3; e++) { int o = kept[i + e]; kept[i + e] = wv.Count; wv.Add(wv[o]); wn.Add(wn[o]); wt.Add(wt.Count > o ? wt[o] : UnityEngine.Vector4.zero); wu.Add(darkUv); wb.Add(wb[o]); }
    shirt++;
  }
  var headless = new UnityEngine.Mesh { name = "SM_Chr_Merchant_01 No Head" };
  headless.SetVertices(wv); headless.SetNormals(wn); if (wt.Count == wv.Count) headless.SetTangents(wt); headless.SetUVs(0, wu);
  headless.boneWeights = wb.ToArray(); headless.bindposes = whole.bindposes; headless.SetTriangles(kept, 0); headless.RecalculateBounds();
  log.Append($"undershirt triangles darkened: {shirt}\n");
  if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(headlessPath) != null) UnityEditor.AssetDatabase.DeleteAsset(headlessPath);   // a fresh asset: CopySerialized over a mesh of another layout keeps stale vertices
  UnityEditor.AssetDatabase.CreateAsset(headless, headlessPath);
  log.Append($"body: {cut} head triangles cut, crown at {crown - root.transform.position.y:F3}\n");
  var bodyTex = (UnityEngine.Texture2D)smr.sharedMaterial.GetTexture("_BaseMap");
  float bodyTexels = TexelCheck.Measure(smr.transform, whole, 0, bodyTex, UnityEngine.Vector2.one);
  log.Append($"body texels a unit {bodyTexels:F1}\n");
  // 2. the heads
  var anim = root.GetComponentInChildren<UnityEngine.Animator>(true);
  var head = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Head); var neck = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Neck);
  var entries = new System.Collections.Generic.List<KeeperFace.Entry>();
  foreach (var line in System.IO.File.ReadAllLines("Tools/keeper_heads.txt"))
  {
    if (line.Trim() == "" || line.StartsWith("#")) continue;
    var a = line.Split('|'); string name = a[0]; float size = float.Parse(a[1], ci), drop = float.Parse(a[2], ci), turn = float.Parse(a[3], ci), fwd = float.Parse(a[4], ci);
    var kind = (KeeperFaceKind)System.Enum.Parse(typeof(KeeperFaceKind), name);
    string dir = $"Assets/Game/Meshy/KeeperHead{name}";
    // the head as Tools/keeper_head_bake.py wrote it ("Keeper Head <X> obj.txt", already in Unity's frame: y up, face +z),
    // one vertex per corner; made into a mesh asset here (Unity's FBX import strung these remeshes' triangles across the head)
    var P = new System.Collections.Generic.List<UnityEngine.Vector3>(); var T = new System.Collections.Generic.List<UnityEngine.Vector2>(); var N = new System.Collections.Generic.List<UnityEngine.Vector3>();
    var mv = new System.Collections.Generic.List<UnityEngine.Vector3>(); var mu = new System.Collections.Generic.List<UnityEngine.Vector2>(); var mn = new System.Collections.Generic.List<UnityEngine.Vector3>(); var mt = new System.Collections.Generic.List<int>();
    foreach (var ol in System.IO.File.ReadAllLines($"{dir}/Keeper Head {name} obj.txt"))
    {
      var q = ol.Split(' '); if (q.Length < 2) continue;
      if (q[0] == "v") P.Add(new UnityEngine.Vector3(float.Parse(q[1], ci), float.Parse(q[2], ci), float.Parse(q[3], ci)));
      else if (q[0] == "vt") T.Add(new UnityEngine.Vector2(float.Parse(q[1], ci), float.Parse(q[2], ci)));
      else if (q[0] == "vn") N.Add(new UnityEngine.Vector3(float.Parse(q[1], ci), float.Parse(q[2], ci), float.Parse(q[3], ci)));
      else if (q[0] == "f") for (int e = 1; e <= 3; e++) { var k = q[e].Split('/'); mt.Add(mv.Count); mv.Add(P[int.Parse(k[0]) - 1]); mu.Add(T[int.Parse(k[1]) - 1]); mn.Add(N[int.Parse(k[2]) - 1].normalized); }
    }
    // winding: the mirror into Unity's frame reverses it; the outward side is the one with positive volume
    double vol = 0; for (int i = 0; i < mt.Count; i += 3) vol += UnityEngine.Vector3.Dot(mv[mt[i]], UnityEngine.Vector3.Cross(mv[mt[i + 1]], mv[mt[i + 2]]));
    if (vol < 0) for (int i = 0; i < mt.Count; i += 3) { int sw = mt[i + 1]; mt[i + 1] = mt[i + 2]; mt[i + 2] = sw; }
    // smooth normals: each corner takes the average of the faces round its point that turn less than 80 degrees from its
    // own (the remesh came flat shaded, one normal a face, and facets turned from the light went near black: the ogre's
    // brow, the gecko's throat); sharper folds (frill edges, ears, horns) keep their edge
    var fnorm = new UnityEngine.Vector3[mt.Count / 3];
    for (int i = 0; i < mt.Count; i += 3) fnorm[i / 3] = UnityEngine.Vector3.Cross(mv[mt[i + 1]] - mv[mt[i]], mv[mt[i + 2]] - mv[mt[i]]);
    var atPoint = new System.Collections.Generic.Dictionary<UnityEngine.Vector3Int, System.Collections.Generic.List<int>>();
    for (int i = 0; i < mt.Count; i++) { var key = UnityEngine.Vector3Int.RoundToInt(mv[mt[i]] * 10000f); if (!atPoint.TryGetValue(key, out var l2)) atPoint[key] = l2 = new System.Collections.Generic.List<int>(); l2.Add(i / 3); }
    float cosLimit = UnityEngine.Mathf.Cos(80f * UnityEngine.Mathf.Deg2Rad);
    for (int i = 0; i < mt.Count; i++)
    {
      var own = fnorm[i / 3].normalized; var sum = UnityEngine.Vector3.zero;
      foreach (int f in atPoint[UnityEngine.Vector3Int.RoundToInt(mv[mt[i]] * 10000f)]) { var fn = fnorm[f]; if (UnityEngine.Vector3.Dot(fn.normalized, own) >= cosLimit) sum += fn; }
      mn[mt[i]] = sum.sqrMagnitude > 1e-12f ? sum.normalized : own;
    }
    var fbxMesh = new UnityEngine.Mesh { name = $"Keeper Head {name}" }; fbxMesh.SetVertices(mv); fbxMesh.SetUVs(0, mu); fbxMesh.SetNormals(mn); fbxMesh.SetTriangles(mt, 0); fbxMesh.RecalculateBounds(); fbxMesh.RecalculateTangents();
    // kept in memory only: the prefab uses the restyled copy
    var bakePath = $"{dir}/Keeper Head {name} baked.png";
    var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(bakePath);
    if (!ti.isReadable || ti.maxTextureSize < 512 || ti.textureCompression != UnityEditor.TextureImporterCompression.Uncompressed) { ti.isReadable = true; ti.maxTextureSize = 512; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; ti.SaveAndReimport(); }
    var bake = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(bakePath);
    var mb = fbxMesh.bounds; float footY = mb.min.y, topY = mb.max.y;
    var goName = "Keeper head " + name;
    var old = head.Find(goName); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
    var go = new UnityEngine.GameObject(goName); go.transform.SetParent(head, false);
    float scale = (crown - neck.position.y) * size / (topY - footY);
    go.transform.rotation = root.transform.rotation * UnityEngine.Quaternion.Euler(0, turn, 0);
    go.transform.localScale = UnityEngine.Vector3.one * scale / head.lossyScale.x;
    // the top's centre (x, y of the bounds' centre, at its highest point) lands `drop` below the Synty crown, over the
    // neck, so the face sits where the Synty face was and the neck goes down into the collar
    var topLocal = new UnityEngine.Vector3(mb.center.x, topY, mb.center.z);
    var target = new UnityEngine.Vector3(neck.position.x, crown - drop, neck.position.z) + root.transform.forward * fwd;
    go.transform.position += target - go.transform.TransformPoint(topLocal);
    // restyle at the body's density
    var srcMat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); srcMat.SetTexture("_BaseMap", bake); srcMat.SetColor("_BaseColor", UnityEngine.Color.white);
    float meshTexels = bodyTexels * go.transform.lossyScale.x;
    // a fresh mesh asset each time: Build copies over an existing one with CopySerialized, which keeps the old vertex
    // data under new triangles when the source changed (the heads came out as triangles strung across the head)
    UnityEditor.AssetDatabase.DeleteAsset($"Assets/Game/Art/Synty Pixel/Characters/Keeper Head {name}/Keeper Head {name} restyled.asset");
    var (mesh, mat) = PixelRestyle.Build(fbxMesh, new[] { srcMat }, "Characters", $"Keeper Head {name} restyled", meshTexels, $"Assets/Game/Art/Synty Pixel/Characters/Keeper Head {name}");
    UnityEngine.Object.DestroyImmediate(srcMat); UnityEngine.Object.DestroyImmediate(fbxMesh);
    if (mesh == null) { log.Append($"{name}: restyle failed\n"); continue; }
    mat.SetFloat("_Cull", 2); UnityEditor.EditorUtility.SetDirty(mat);
    go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
    var mr = go.AddComponent<UnityEngine.MeshRenderer>(); mr.sharedMaterial = mat;
    mr.probeAnchor = anim.GetBoneTransform(UnityEngine.HumanBodyBones.Hips);
    var tex = (UnityEngine.Texture2D)mat.GetTexture("_BaseMap");
    log.Append($"{name}: scale {scale:F3}, {mesh.triangles.Length / 3} triangles, texture {tex.width}x{tex.height}, {TexelCheck.Measure(go.transform, mesh, 0, tex, UnityEngine.Vector2.one):F1} texels a unit\n");
    entries.Add(new KeeperFace.Entry { kind = kind, face = go });
  }
  kf.faces = entries.ToArray(); kf.body = smr; kf.wholeBody = whole; kf.headlessBody = headless;
  kf.Apply();
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
}
catch (System.Exception ex) { log.Append("FAILED: " + ex); }
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/KeeperHeads/eval/build.txt", log.ToString());
return log.ToString();

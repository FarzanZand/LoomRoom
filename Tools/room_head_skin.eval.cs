// The woman's Meshy head keeps the bust's own neck: this turns the fitted head (a rigid mesh under the head bone, from
// fit) into a skinned mesh on the body's skeleton. The neck below the chin is stretched down until it reaches just
// inside the shirt's collar, and its vertices are weighted from the head bone (jaw and up) to neck_01 (collar), so the
// head turns on a neck that bends instead of a rigid post. Run from an eval after the head is fitted.
if (UnityEngine.Application.isPlaying) return "playing";
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include);
var headGo = bt.headA; var bodyGo = bt.bodyA; const string name = "Room Head Woman";
bool hw = headGo.activeSelf, bw = bodyGo.activeSelf; headGo.SetActive(true); bodyGo.SetActive(true);
var smr = bodyGo.GetComponent<UnityEngine.SkinnedMeshRenderer>(); var ht = headGo.transform;
var src = headGo.GetComponent<UnityEngine.MeshFilter>().sharedMesh; var mat = headGo.GetComponent<UnityEngine.Renderer>().sharedMaterial;
UnityEngine.Transform B(string n) => System.Array.Find(smr.bones, x => x.name == n);
var neck = B("neck_01"); var head = B("head"); var pelvis = B("pelvis");
float stand = smr.bounds.size.y, H = stand / 6.4f, chinY = head.position.y - .24f * H;
// the collar's top: the highest shirt texel (not skin) on the body close round the neck
var bake = new UnityEngine.Mesh(); smr.BakeMesh(bake, true); var bv = bake.vertices; var buv = smr.sharedMesh.uv;
var tex = new UnityEngine.Texture2D(2, 2); tex.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMaterial.GetTexture("_BaseMap"))));
bool Skin(UnityEngine.Color c) => c.r > c.g * 1.05f && c.g > c.b && c.r > c.b * 1.12f && c.grayscale > .3f;
var axis = new UnityEngine.Vector2(neck.position.x, neck.position.z); float collarTop = -1e9f;
for (int i = 0; i < bv.Length; i++)
{
  var w = smr.transform.TransformPoint(bv[i]); if (w.y > chinY || w.y < neck.position.y - .6f * H) continue;
  if ((new UnityEngine.Vector2(w.x, w.z) - axis).magnitude > .55f * H) continue;
  if (UnityEngine.Vector3.Dot(w - neck.position, bt.transform.forward) < 0) continue;   // the collar in front (the back has a shirt-coloured patch high up)
  if (Skin(tex.GetPixelBilinear(buv[i].x, buv[i].y))) continue; collarTop = UnityEngine.Mathf.Max(collarTop, w.y);
}
UnityEngine.Object.DestroyImmediate(bake);
// vertices in world space; stretch the neck below the chin to end .3 head heights inside the collar
var v = src.vertices; var n = src.normals; var wv = new UnityEngine.Vector3[v.Length]; float minY = 1e9f;
for (int i = 0; i < v.Length; i++) { wv[i] = ht.TransformPoint(v[i]); minY = UnityEngine.Mathf.Min(minY, wv[i].y); }
float target = collarTop - .3f * H, s = (chinY - target) / UnityEngine.Mathf.Max(.01f, chinY - minY); if (s < 1) s = 1;
for (int i = 0; i < wv.Length; i++) if (wv[i].y < chinY) wv[i].y = chinY - (chinY - wv[i].y) * s;
// the base of the neck flares out to the body's own skin round the shirt's opening (the body's neck is cut away), like a
// neck meeting the chest: per height band and direction round the neck axis, the Meshy neck is widened to reach the
// furthest body skin there
{
  var bk2 = new UnityEngine.Mesh(); smr.BakeMesh(bk2, true); var bv2 = bk2.vertices; int S = 12; float bandH = .08f * H; float top = collarTop + .1f * H;
  var need = new System.Collections.Generic.Dictionary<(int, int), float>(); var have = new System.Collections.Generic.Dictionary<(int, int), float>();
  (int, int) Key(UnityEngine.Vector3 w) { var d = new UnityEngine.Vector2(w.x - neck.position.x, w.z - neck.position.z); int a = (int)((UnityEngine.Mathf.Atan2(d.y, d.x) / (2 * UnityEngine.Mathf.PI) + .5f) * S) % S; return (a, UnityEngine.Mathf.FloorToInt((top - w.y) / bandH)); }
  float Rad(UnityEngine.Vector3 w) => new UnityEngine.Vector2(w.x - neck.position.x, w.z - neck.position.z).magnitude;
  var lists = new System.Collections.Generic.Dictionary<(int, int), System.Collections.Generic.List<float>>();
  for (int i = 0; i < bv2.Length; i++) { var w = smr.transform.TransformPoint(bv2[i]); if (w.y > top || w.y < target) continue; if (Rad(w) > .5f * H) continue; if (!Skin(tex.GetPixel((int)(buv[i].x * tex.width), (int)(buv[i].y * tex.height)))) continue; var k = Key(w); if (!lists.ContainsKey(k)) lists[k] = new System.Collections.Generic.List<float>(); lists[k].Add(Rad(w)); }
  foreach (var kv in lists) { kv.Value.Sort(); need[kv.Key] = kv.Value[(int)(kv.Value.Count * .8f)]; }   // the 80th percentile: stray texels at the edge don't pull the flare out
  for (int i = 0; i < wv.Length; i++) { if (wv[i].y > top) continue; var k = Key(wv[i]); have[k] = UnityEngine.Mathf.Max(have.TryGetValue(k, out var q) ? q : 0, Rad(wv[i])); }
  int flared = 0;
  for (int i = 0; i < wv.Length; i++)
  {
    if (wv[i].y > top) continue; var k = Key(wv[i]); if (!need.TryGetValue(k, out var nd) || !have.TryGetValue(k, out var hv) || hv < 1e-4f) continue;
    float f2 = UnityEngine.Mathf.Min(1.45f, nd * 1.04f / hv); if (f2 <= 1) continue; float t2 = UnityEngine.Mathf.Clamp01((top - wv[i].y) / (.15f * H)); f2 = 1 + (f2 - 1) * t2;   // eased in from the top of the band
    var d = new UnityEngine.Vector3(wv[i].x - neck.position.x, 0, wv[i].z - neck.position.z); wv[i] = new UnityEngine.Vector3(neck.position.x, wv[i].y, neck.position.z) + d * f2 + UnityEngine.Vector3.up * 0; flared++;
  }
  UnityEngine.Object.DestroyImmediate(bk2);
}
// the skinned object sits where the body's renderer does
var go = new UnityEngine.GameObject(headGo.name); go.layer = headGo.layer; go.transform.SetParent(smr.transform.parent, false);
go.transform.localPosition = smr.transform.localPosition; go.transform.localRotation = smr.transform.localRotation; go.transform.localScale = smr.transform.localScale;
var gt = go.transform; var nv = new UnityEngine.Vector3[v.Length]; var nn = new UnityEngine.Vector3[v.Length]; var bw2 = new UnityEngine.BoneWeight[v.Length];
float hi = chinY + .05f * H, lo = UnityEngine.Mathf.Min(collarTop, neck.position.y);   // head bone from the jaw up, neck_01 at the collar
for (int i = 0; i < v.Length; i++)
{
  nv[i] = gt.InverseTransformPoint(wv[i]); nn[i] = gt.InverseTransformDirection(ht.TransformDirection(n[i])).normalized;
  float t = UnityEngine.Mathf.Clamp01((wv[i].y - lo) / (hi - lo)); t = t * t * (3 - 2 * t);
  bw2[i] = new UnityEngine.BoneWeight { boneIndex0 = 1, weight0 = t, boneIndex1 = 0, weight1 = 1 - t };
}
var mesh = UnityEngine.Object.Instantiate(src); mesh.name = name + " skinned";
mesh.vertices = nv; mesh.normals = nn; mesh.boneWeights = bw2;
var bones = new[] { neck, head }; mesh.bindposes = new[] { neck.worldToLocalMatrix * gt.localToWorldMatrix, head.worldToLocalMatrix * gt.localToWorldMatrix };
mesh.RecalculateBounds();
string mp = $"Assets/Game/Art/Synty Pixel/Characters/{name}/{name} skinned.asset"; UnityEditor.AssetDatabase.DeleteAsset(mp); UnityEditor.AssetDatabase.CreateAsset(mesh, mp); PixelRestyle.MarkReadable(mesh);
var sk = go.AddComponent<UnityEngine.SkinnedMeshRenderer>(); sk.sharedMesh = mesh; sk.bones = bones; sk.rootBone = smr.rootBone; sk.sharedMaterial = mat;
sk.updateWhenOffscreen = true; sk.probeAnchor = pelvis; sk.shadowCastingMode = smr.shadowCastingMode;
var feat = ht.Find("Face features"); if (feat) UnityEngine.Object.DestroyImmediate(feat.gameObject);
UnityEngine.Object.DestroyImmediate(headGo); bt.headA = go; go.SetActive(hw); bodyGo.SetActive(bw); bt.Apply();
UnityEditor.EditorUtility.SetDirty(bt); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bt.gameObject.scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(bt.gameObject.scene); UnityEditor.AssetDatabase.SaveAssets();
return $"chin {chinY:F2}, collar top {collarTop:F2}, neck bottom {minY:F2} -> {target:F2} (x{s:F2}), {v.Length} verts";

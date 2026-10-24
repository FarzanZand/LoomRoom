// The room player heads made the way the Dungeon Master's faces are: the low-poly Meshy head (hers from
// Tools/room_head_lowpoly.eval.cs, his as baked) restyled by PixelRestyle.Build from Meshy's own painting (the full-detail
// bake on the head's UVs, "Room Head <X> baked.png"): fresh UVs, each texel the painting's colour at that spot, its
// occlusion, crease dirt and grain. Nothing is drawn over it: eyes, brows and lips are Meshy's, so they follow the
// mesh. Density as the DM's faces (DmTexels a unit in the world), so the face has the detail the eyes need. The old
// painted overlay (Face features) is removed. Then Tools/room_head_skin_tone.py and a review.
if (UnityEngine.Application.isPlaying) return "playing";
const float DmTexels = 40f;
var bt = UnityEngine.Object.FindAnyObjectByType<RoomBodyType>(UnityEngine.FindObjectsInactive.Include); var log = new System.Text.StringBuilder();
foreach (var (headGo, name, who) in new[] { (bt.headA, "Room Head Woman", "Woman"), (bt.headB, "Room Head Man", "Man") })
{
  bool was = headGo.activeSelf; headGo.SetActive(true);
  var r = headGo.GetComponent<UnityEngine.Renderer>();
  var src = r is UnityEngine.SkinnedMeshRenderer sk ? sk.sharedMesh : headGo.GetComponent<UnityEngine.MeshFilter>().sharedMesh;
  var bake = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>($"Assets/Game/Meshy/RoomHead{who}/{name} baked.png");
  var srcMat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); srcMat.SetTexture("_BaseMap", bake); srcMat.SetColor("_BaseColor", UnityEngine.Color.white);
  float meshTexels = DmTexels * r.transform.lossyScale.x;   // texels per mesh unit for DmTexels per world unit
  string dir = $"Assets/Game/Art/Synty Pixel/Characters/{name}";
  var (mesh, mat) = PixelRestyle.Build(src, new[] { srcMat }, "Characters", name + " restyled", meshTexels, dir);
  UnityEngine.Object.DestroyImmediate(srcMat);
  if (mesh == null) { log.Append($"{name}: restyle failed\n"); headGo.SetActive(was); continue; }
  mat.SetFloat("_Cull", 2); UnityEditor.EditorUtility.SetDirty(mat);
  if (r is UnityEngine.SkinnedMeshRenderer s2) s2.sharedMesh = mesh; else headGo.GetComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
  r.sharedMaterial = mat; UnityEditor.EditorUtility.SetDirty(r);
  var feat = headGo.transform.Find("Face features"); if (feat) UnityEngine.Object.DestroyImmediate(feat.gameObject);
  var tex = (UnityEngine.Texture2D)mat.GetTexture("_BaseMap");
  log.Append($"{name}: {mesh.triangles.Length / 3} triangles, texture {tex.width} x {tex.height}, {TexelCheck.Measure(r.transform, mesh, 0, tex, UnityEngine.Vector2.one):F1} texels a unit\n");
  headGo.SetActive(was);
}
bt.Apply(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(bt.gameObject.scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(bt.gameObject.scene);
return log.ToString();

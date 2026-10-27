// A round cast base under each class statue (World/Art/Class props/<Class> statue), safe to run again: Unity's cylinder
// cut to a disc 4.4 units across and .7 high (measured from the mesh), restyled from the miniatures' pewter at the
// bedroom's 6.4 texels a unit (measured, baked again until within 5%; shared, Class props/Statue base), the figure
// lifted onto it. Writes Temp/Posters/bases.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/World/Art/Class props";
const float TPU = 6.4f, Across = 4.4f, Thick = .7f;
bool rebuild = true;   // remake the base's mesh and texture (the statues are pointed at the new ones)
var sb = new System.Text.StringBuilder();
var dir = $"{D}/Statue base";
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder(D, "Statue base");
var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>($"{dir}/Statue base.asset");
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{dir}/Statue base.mat");
if (rebuild || mesh == null || mat == null)
{
  // old copies go first: a restyle written over an existing mesh asset keeps stale vertices
  foreach (var old in UnityEditor.AssetDatabase.FindAssets("", new[] { dir })) UnityEditor.AssetDatabase.DeleteAsset(UnityEditor.AssetDatabase.GUIDToAssetPath(old));
  // the disc at its own size, its foot at 0, so it is baked and measured at scale 1
  var cyl = UnityEngine.Resources.GetBuiltinResource<UnityEngine.Mesh>("Cylinder.fbx");
  var src = UnityEngine.Object.Instantiate(cyl);
  var v = src.vertices; var cb = cyl.bounds;
  for (int i = 0; i < v.Length; i++)
    v[i] = new UnityEngine.Vector3(v[i].x / cb.size.x * Across, (v[i].y - cb.min.y) / cb.size.y * Thick, v[i].z / cb.size.z * Across);
  src.vertices = v; src.RecalculateBounds();
  // its colour comes from the miniature's own pewter, as the statues' does
  var mini = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Progression/Room/Miniatures/Warrior Miniature.prefab").GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>().sharedMaterials;
  var probe = new UnityEngine.GameObject("Texel probe") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
  var pf = probe.AddComponent<UnityEngine.MeshFilter>(); var pr = probe.AddComponent<UnityEngine.MeshRenderer>();
  float aim = TPU, measured = 0;
  for (int pass = 0; pass < 4; pass++)
  {
    (mesh, mat) = PixelRestyle.Build(src, new[] { mini[0] }, "Props", "Statue base", aim, dir);
    if (mesh == null) break;
    pf.sharedMesh = mesh; pr.sharedMaterial = mat;
    UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
    measured = PixelRestyle.TexelsPerUnit(pr);
    if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
    aim *= TPU / measured;
  }
  UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(src);
  sb.Append($"base {measured:F2} texels a unit, {mesh.bounds.size.x:F2} across\n");
  sb.Append($"texture {UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap"))}\n");
}
foreach (var g in UnityEditor.AssetDatabase.FindAssets("statue t:Prefab", new[] { D }))
{
  var path = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  var bt = root.transform.Find("Base");
  UnityEngine.GameObject b;
  if (bt == null)
  {
    b = new UnityEngine.GameObject("Base"); b.transform.SetParent(root.transform, false);
    b.AddComponent<UnityEngine.MeshFilter>(); b.AddComponent<UnityEngine.MeshRenderer>();
    var model = root.transform.Find("Model"); if (model != null) model.localPosition += new UnityEngine.Vector3(0, Thick, 0);
    sb.Append($"{path}: base added\n");
  }
  else b = bt.gameObject;
  b.GetComponent<UnityEngine.MeshFilter>().sharedMesh = mesh; b.GetComponent<UnityEngine.MeshRenderer>().sharedMaterial = mat;
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
  UnityEditor.PrefabUtility.UnloadPrefabContents(root);
}
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/Posters/bases.txt", sb.ToString()); return "ok";

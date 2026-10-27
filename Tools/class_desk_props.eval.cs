// The class statues on the writing desk under the class posters, safe to run again. Each class's statue is its pewter
// miniature (its own character model, AdventurerClass.miniature) baked in its pose, about 40 cm tall, and restyled from
// the pewter at the bedroom's 6.4 texels a unit (measured and baked again until within 5%): World/Art/Class props/<Class>
// statue (prefab, mesh, material), set as the class's deskProp. The writing desk moves into the poster wall prefab (its
// open book left out, its candle light handed back to the Lighting Manager) and each statue is placed on it once; after
// that they are moved by hand. Writes Temp/Posters/props.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/World/Art/Class props", PrefabPath = "Assets/Game/World/Art/Class posters/Class poster wall.prefab";
const float TPU = 6.4f, Height = 9f, DeskTop = 16.02f;
var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (!UnityEditor.AssetDatabase.IsValidFolder(D)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/World/Art", "Class props");
var rules = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventureRules>("Assets/Game/Progression/Data/Adventure Rules.asset");

// ── the statues ──
foreach (var cls in rules.classes)
{
  if (cls == null || cls.miniature == null) continue;
  var path = $"{D}/{cls.name} statue.prefab";
  if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path) != null) { cls.deskProp = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path); UnityEditor.EditorUtility.SetDirty(cls); sb.Append($"{cls.name}: statue kept\n"); continue; }
  var inst = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(cls.miniature);
  inst.transform.SetPositionAndRotation(UnityEngine.Vector3.zero, UnityEngine.Quaternion.identity); inst.transform.localScale = UnityEngine.Vector3.one;
  var smr = inst.GetComponentInChildren<UnityEngine.SkinnedMeshRenderer>(true);
  var baked = new UnityEngine.Mesh(); smr.BakeMesh(baked, true);
  baked.RecalculateBounds();
  var at = inst.transform.InverseTransformPoint(smr.transform.position); var rot = UnityEngine.Quaternion.Inverse(inst.transform.rotation) * smr.transform.rotation;
  // standing height in the root's frame: the baked mesh's corners turned into it
  var bb = baked.bounds; float lo = 1e9f, hi = -1e9f;
  for (int i = 0; i < 8; i++) { var c = bb.center + UnityEngine.Vector3.Scale(bb.extents, new UnityEngine.Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)); float y = (at + rot * c).y; lo = UnityEngine.Mathf.Min(lo, y); hi = UnityEngine.Mathf.Max(hi, y); }
  float S = Height / (hi - lo);
  var dir = $"{D}/{cls.name} statue";
  if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder(D, $"{cls.name} statue");
  var probe = new UnityEngine.GameObject("Texel probe") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
  probe.transform.localScale = UnityEngine.Vector3.one * S;
  var pf = probe.AddComponent<UnityEngine.MeshFilter>(); var pr = probe.AddComponent<UnityEngine.MeshRenderer>();
  UnityEngine.Mesh mesh = null; UnityEngine.Material mat = null; float aim = TPU * S, measured = 0;
  for (int pass = 0; pass < 4; pass++)
  {
    (mesh, mat) = PixelRestyle.Build(baked, smr.sharedMaterials, "Props", $"{cls.name} statue", aim, dir);
    if (mesh == null) break;
    pf.sharedMesh = mesh; pr.sharedMaterial = mat;
    UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
    measured = PixelRestyle.TexelsPerUnit(pr);
    if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
    aim *= TPU / measured;
  }
  UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(inst);
  if (mesh == null) { sb.Append($"{cls.name}: restyle failed\n"); continue; }
  var root = new UnityEngine.GameObject($"{cls.name} statue");
  var model = new UnityEngine.GameObject("Model"); model.transform.SetParent(root.transform, false);
  model.transform.localPosition = (at - new UnityEngine.Vector3(0, lo, 0)) * S;   // feet on the root
  model.transform.localRotation = rot; model.transform.localScale = UnityEngine.Vector3.one * S;
  model.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
  model.AddComponent<UnityEngine.MeshRenderer>().sharedMaterial = mat;
  var prefab = UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
  UnityEngine.Object.DestroyImmediate(root);
  cls.deskProp = prefab; UnityEditor.EditorUtility.SetDirty(cls);
  sb.Append($"{cls.name}: statue {mesh.triangles.Length / 3} triangles, {measured:F2} texels a unit at x{S:F2}\n");
}

// ── the desk into the poster wall prefab, and the statues on it ──
var furn = UnityEngine.GameObject.Find("World/Apartment/Main Bedroom/Furnishings").transform;
var sceneDesk = furn.Find("Mood/Room/Writing desk");
var lm = UnityEngine.Object.FindAnyObjectByType<LightingManager>();
var lmSo = lm != null ? new UnityEditor.SerializedObject(lm) : null;
var lightSlots = new System.Collections.Generic.List<string>();
var wallRoot = UnityEditor.PrefabUtility.LoadPrefabContents(PrefabPath);
// The opened prefab's root doesn't stand where the scene's wall does: world places are taken through the scene's wall.
var sceneWall = furn.Find("Class poster wall");
void PlaceLocal(UnityEngine.Transform t, UnityEngine.Vector3 world, UnityEngine.Quaternion worldRot)
{
  var parentScene = t.parent == wallRoot.transform ? sceneWall : sceneWall.Find(t.parent.name);
  t.localPosition = parentScene.InverseTransformPoint(world);
  t.localRotation = UnityEngine.Quaternion.Inverse(parentScene.rotation) * worldRot;
}
var wall = wallRoot.GetComponent<ClassPosterWall>();
var desk = wallRoot.transform.Find("Writing desk");
if (desk == null && sceneDesk != null)
{
  // which Lighting Manager sources point at the desk's lights, to hand them the copies after
  if (lmSo != null)
  {
    var src = lmSo.FindProperty("sceneSources");
    for (int i = 0; i < src.arraySize; i++) { var l = src.GetArrayElementAtIndex(i).FindPropertyRelative("light"); if (l != null && l.objectReferenceValue is UnityEngine.Light li && li.transform.IsChildOf(sceneDesk)) lightSlots.Add($"{i}|{li.name}"); }
  }
  var copy = UnityEngine.Object.Instantiate(sceneDesk.gameObject, wallRoot.transform);
  copy.name = "Writing desk";
  PlaceLocal(copy.transform, sceneDesk.position, sceneDesk.rotation);
  var book = copy.transform.Find("Open book"); if (book != null) UnityEngine.Object.DestroyImmediate(book.gameObject);
  desk = copy.transform;
  sb.Append("desk moved into the prefab, open book left out\n");
}
// where each statue first stands on the desk (world): z along the desk, x out from the wall, turn from facing the room
var spots = new System.Collections.Generic.Dictionary<string, (float x, float z, float yaw)>
{
  ["Warrior"] = (-465.5f, 438.5f, -10f), ["Cleric"] = (-463f, 447.5f, 8f),
  ["Wizard"] = (-466f, 455.5f, -6f), ["Arcanist"] = (-460.5f, 460f, 14f),
};
foreach (var e in wall.posters)
{
  if (e.adventurerClass == null || e.prop != null || e.adventurerClass.deskProp == null || desk == null) continue;
  var go = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(e.adventurerClass.deskProp, desk);
  (float x, float z, float yaw) s = spots.TryGetValue(e.adventurerClass.name, out var sp) ? sp : (-464f, 450f, 0f);
  // the desk's place in the scene is the one it had before it moved (its local pose is the same under the wall)
  var deskLocal = desk.localPosition; var deskRot = desk.localRotation;
  var deskWorld = sceneWall.TransformPoint(deskLocal); var deskWorldRot = sceneWall.rotation * deskRot;
  go.transform.localPosition = UnityEngine.Quaternion.Inverse(deskWorldRot) * (new UnityEngine.Vector3(s.x, DeskTop, s.z) - deskWorld);
  go.transform.localRotation = UnityEngine.Quaternion.Inverse(deskWorldRot) * UnityEngine.Quaternion.Euler(0, 90 + s.yaw, 0);
  e.prop = go;
  sb.Append($"{e.adventurerClass.name}: statue placed\n");
}
UnityEditor.PrefabUtility.SaveAsPrefabAsset(wallRoot, PrefabPath);
UnityEditor.PrefabUtility.UnloadPrefabContents(wallRoot);

// the scene: the old desk goes, the lights it held are the prefab's now
if (lightSlots.Count > 0 && sceneDesk != null)
{
  var inst = furn.Find("Class poster wall");
  var src = lmSo.FindProperty("sceneSources");
  foreach (var slot in lightSlots)
  {
    var parts = slot.Split('|'); int i = int.Parse(parts[0]);
    UnityEngine.Light found = null;
    foreach (var l in inst.GetComponentsInChildren<UnityEngine.Light>(true)) if (l.name == parts[1] && l.transform.IsChildOf(inst.Find("Writing desk"))) found = l;
    src.GetArrayElementAtIndex(i).FindPropertyRelative("light").objectReferenceValue = found;
    sb.Append($"Lighting Manager source {i} -> {(found != null ? found.name : "MISSING")}\n");
  }
  lmSo.ApplyModifiedPropertiesWithoutUndo();
}
if (sceneDesk != null && furn.Find("Class poster wall/Writing desk") != null) { UnityEngine.Object.DestroyImmediate(sceneDesk.gameObject); sb.Append("old scene desk removed\n"); }
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
System.IO.File.WriteAllText("Temp/Posters/props.txt", sb.ToString()); return "ok";

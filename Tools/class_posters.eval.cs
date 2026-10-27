// The class poster wall over the writing desk by the bed (bedroom, west wall), safe to run again: the posters' textures
// (painted by Tools/class_poster.py into World/Art/Class posters) imported point filtered at their exact size, a material
// and a slightly bowed paper mesh per poster at the bedroom's 6.4 texels a unit, masking tape on their corners, and
// Furnishings/Class posters with a ClassPosterWall that puts each poster up in the next spot once its class is unlocked
// (every class asset gets one; a missing poster is painted from the class portrait). The old map board
// and its pinned maps are removed. Writes Temp/Posters/build.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/World/Art/Class posters";
const float TPU = 6.4f, Wall = -471.9f;
var sb = new System.Text.StringBuilder();
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var room = UnityEngine.GameObject.Find("World/Apartment/Main Bedroom");
var furn = room.transform.Find("Furnishings");

// the old map board and its maps
var desk = furn.Find("Mood/Room/Writing desk");
if (desk != null)
  for (int i = desk.childCount - 1; i >= 0; i--)
  {
    var c = desk.GetChild(i);
    if (c.name == "Map board" || c.name == "Pinned map") { sb.Append($"removed {c.name}\n"); UnityEngine.Object.DestroyImmediate(c.gameObject); }
  }

UnityEngine.Texture2D Tex(string path)
{
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
  ti.textureType = UnityEditor.TextureImporterType.Default; ti.filterMode = UnityEngine.FilterMode.Point; ti.mipmapEnabled = false;
  ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
  ti.wrapMode = UnityEngine.TextureWrapMode.Clamp; ti.sRGBTexture = true; ti.SaveAndReimport();
  return UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(path);
}
UnityEngine.Material Mat(string name, UnityEngine.Texture2D tex)
{
  var path = $"{D}/{name}.mat";
  if (UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path) == null)
    UnityEditor.AssetDatabase.CopyAsset("Assets/Game/Rules/Art/House rules sheet.mat", path);
  var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
  m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
  UnityEditor.EditorUtility.SetDirty(m); return m;
}
// Paper w x h units, front facing local -z, lying on the wall at z 0: a slight bow, and one corner lifting off it.
UnityEngine.Mesh Paper(string name, float w, float h, float bow, int curlCorner, float curl)
{
  const int nx = 7, ny = 9;
  var v = new UnityEngine.Vector3[nx * ny]; var uv = new UnityEngine.Vector2[nx * ny]; var tri = new System.Collections.Generic.List<int>();
  for (int j = 0; j < ny; j++) for (int i = 0; i < nx; i++)
  {
    float u = i / (nx - 1f), t = j / (ny - 1f);
    float z = -bow * UnityEngine.Mathf.Sin(u * UnityEngine.Mathf.PI) * UnityEngine.Mathf.Sin(t * UnityEngine.Mathf.PI);
    if (curlCorner >= 0)
    {
      float cu = curlCorner % 2, cv = curlCorner / 2; // 0 bottom-left, 1 bottom-right, 2 top-left, 3 top-right
      float d = 1 - (UnityEngine.Mathf.Abs(u - cu) + UnityEngine.Mathf.Abs(t - cv)) / .45f;
      if (d > 0) z -= curl * d * d;
    }
    v[j * nx + i] = new UnityEngine.Vector3((u - .5f) * w, (t - .5f) * h, z); uv[j * nx + i] = new UnityEngine.Vector2(u, t);
  }
  for (int j = 0; j < ny - 1; j++) for (int i = 0; i < nx - 1; i++)
  {
    int a = j * nx + i, b = a + 1, c = a + nx, e = c + 1;
    tri.AddRange(new[] { a, c, e, a, e, b });
  }
  var path = $"{D}/{name}.asset";
  UnityEditor.AssetDatabase.DeleteAsset(path);
  var m = new UnityEngine.Mesh { name = name, vertices = v, uv = uv }; m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
  UnityEditor.AssetDatabase.CreateAsset(m, path); return m;
}
UnityEngine.GameObject Part(string name, UnityEngine.Transform parent, UnityEngine.Mesh mesh, UnityEngine.Material mat)
{
  var go = new UnityEngine.GameObject(name); go.transform.SetParent(parent, false);
  go.AddComponent<UnityEngine.MeshFilter>().sharedMesh = mesh;
  var r = go.AddComponent<UnityEngine.MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
  go.isStatic = false; return go;
}

var tapeTex = Tex($"{D}/Poster tape.png");
var tapeMat = Mat("Poster tape", tapeTex);
// the tape keeps its mesh (every taped corner in the prefab points at it)
var tapeMesh = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>($"{D}/Poster tape mesh.asset") ?? Paper("Poster tape mesh", tapeTex.width / TPU, tapeTex.height / TPU, 0, -1, 0);

// The wall is a prefab (World/Art/Class posters/Class poster wall) edited by hand: made once, then later runs only add a
// poster for a class that has none (painted from its portrait when its picture is missing) and leave the rest alone.
const string PrefabPath = D + "/Class poster wall.prefab";
var oldScene = furn.Find("Class posters"); if (oldScene != null) UnityEngine.Object.DestroyImmediate(oldScene.gameObject);
bool fresh = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(PrefabPath) == null;
UnityEngine.GameObject root;
if (fresh)
{
  root = new UnityEngine.GameObject("Class poster wall");
  // the wall's own frame: x along the wall toward the bed, y up, -z out into the room
  root.transform.SetPositionAndRotation(new UnityEngine.Vector3(Wall, 46, 455), UnityEngine.Quaternion.LookRotation(UnityEngine.Vector3.left));
  root.AddComponent<ClassPosterWall>();
}
else root = UnityEditor.PrefabUtility.LoadPrefabContents(PrefabPath);
var wall = root.GetComponent<ClassPosterWall>();
// a poster going up: paper on the wall and the trophies' gold burst
if (wall.putUpClip == null) wall.putUpClip = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.AudioClip>("Assets/ThirdParty/Sound Effects Library/Items/Itm_Note_Pageturn_01.wav");
if (wall.putUpEffect == null) wall.putUpEffect = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Game/Items/Effects/Chest burst.prefab");
// using the wall (putting a carried poster up): a trigger box over the posters
if (root.transform.Find("Interaction") == null)
{
  var interaction = new UnityEngine.GameObject("Interaction");
  interaction.transform.SetParent(root.transform, false);
  interaction.transform.localPosition = new UnityEngine.Vector3(0, 1, -3);
  var box = interaction.AddComponent<UnityEngine.BoxCollider>(); box.isTrigger = true; box.size = new UnityEngine.Vector3(54, 46, 6);
  interaction.AddComponent<InteractableTrigger>();
}

// Every class gets a poster: the game's classes in their order, then any other class asset.
var rules = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventureRules>("Assets/Game/Progression/Data/Adventure Rules.asset");
var classes = new System.Collections.Generic.List<AdventurerClass>();
if (rules != null) foreach (var c in rules.classes) if (c != null && !classes.Contains(c)) classes.Add(c);
foreach (var g in UnityEditor.AssetDatabase.FindAssets("t:AdventurerClass"))
{
  var c = UnityEditor.AssetDatabase.LoadAssetAtPath<AdventurerClass>(UnityEditor.AssetDatabase.GUIDToAssetPath(g));
  if (c != null && !classes.Contains(c)) classes.Add(c);
}
var sizes = new[] { (96, 128), (84, 112), (90, 120), (82, 108) };
// per poster: bow, curl corner (-1 none, 0 bottom-left, 1 bottom-right), curl, tapes as (u, v, angle) in its 0-1 square
var looks = new (float bow, int curlAt, float curl, (float u, float v, float a)[] tapes)[]
{
  (.14f, 1, .9f, new[] { (.06f, .95f, 40f), (.94f, .95f, -40f) }),
  (.1f, 0, .7f, new[] { (.5f, .97f, 4f), (.93f, .06f, 35f) }),
  (.12f, -1, 0f, new[] { (.07f, .94f, 38f), (.93f, .94f, -42f), (.08f, .06f, -36f), (.92f, .06f, 40f) }),
  (.12f, 0, .8f, new[] { (.07f, .95f, 45f), (.94f, .94f, -38f) }),
};
// the cluster over the desk, in the wall's frame: x, y, tilt, layer (higher lies on top), look
var placed = new System.Collections.Generic.Dictionary<string, (float x, float y, float tilt, int layer, int look)>
{
  ["Warrior"] = (-15f, 4f, -2.5f, 0, 0), ["Cleric"] = (.5f, -6.5f, 3f, 2, 1),
  ["Wizard"] = (9f, 10.5f, -1.5f, 1, 2), ["Arcanist"] = (15.5f, -9f, 2f, 1, 3),
};
float right = 0; foreach (var e in wall.posters) if (e.poster != null) right = UnityEngine.Mathf.Max(right, e.poster.transform.localPosition.x);
for (int ci = 0; ci < classes.Count; ci++)
{
  var cls = classes[ci];
  bool has = false; foreach (var e in wall.posters) if (e.adventurerClass == cls && e.poster != null) has = true;
  if (has) { Tex($"{D}/{cls.name} poster.png"); sb.Append($"{cls.name}: kept\n"); continue; }
  var name = string.IsNullOrEmpty(cls.displayName) ? cls.name : cls.displayName;
  var png = $"{D}/{cls.name} poster.png";
  if (!System.IO.File.Exists(png))
  {
    if (cls.portrait == null) { sb.Append($"{cls.name}: no portrait, no poster\n"); continue; }
    var (pw, ph) = sizes[ci % sizes.Length];
    var psi = new System.Diagnostics.ProcessStartInfo("python",
      $"Tools/class_poster.py \"{UnityEditor.AssetDatabase.GetAssetPath(cls.portrait)}\" \"{png}\" {pw} {ph} \"{name.ToUpperInvariant()}\" {11 + ci}")
      { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
    using (var proc = System.Diagnostics.Process.Start(psi)) { var err = proc.StandardError.ReadToEnd(); proc.WaitForExit(); if (proc.ExitCode != 0) { sb.Append($"{cls.name}: painting failed {err}\n"); continue; } }
    UnityEditor.AssetDatabase.ImportAsset(png);
  }
  // a new class's poster goes up to the right of the others, high and low in turn: move it where you like
  (float x, float y, float tilt, int layer, int look) at0 = placed.TryGetValue(cls.name, out var pl) ? pl : (right += 15f, ci % 2 == 0 ? 6f : -8f, ci % 2 == 0 ? -2f : 2f, 0, ci % looks.Length);
  var p = looks[at0.look];
  var tex = Tex(png);
  float w = tex.width / TPU, h = tex.height / TPU;
  var mesh = Paper($"{cls.name} poster mesh", w, h, p.bow, p.curlAt, p.curl);
  var go = Part($"{cls.name} poster", root.transform, mesh, Mat($"{cls.name} poster", tex));
  go.transform.localPosition = new UnityEngine.Vector3(at0.x, at0.y, -(.1f + at0.layer * .06f));
  go.transform.localRotation = UnityEngine.Quaternion.Euler(0, 0, at0.tilt);
  int n = 0;
  foreach (var t in p.tapes)
  {
    // sit the tape on the paper where it lies (its bow), a hair in front
    var mv = mesh.vertices; float best = 1e9f, z = 0;
    var at = new UnityEngine.Vector2((t.u - .5f) * w, (t.v - .5f) * h);
    foreach (var vv in mv) { float d = (new UnityEngine.Vector2(vv.x, vv.y) - at).sqrMagnitude; if (d < best) { best = d; z = vv.z; } }
    var tp = Part($"Tape {++n}", go.transform, tapeMesh, tapeMat);
    tp.transform.localPosition = new UnityEngine.Vector3(at.x, at.y, z - .04f);
    tp.transform.localRotation = UnityEngine.Quaternion.Euler(0, 0, t.a);
  }
  wall.posters.Add(new ClassPosterWall.Poster { adventurerClass = cls, poster = go });
  sb.Append($"{cls.name}: {tex.width}x{tex.height} = {w:F2} x {h:F2} units, new\n");
}
if (fresh)
{
  root.transform.SetParent(furn, true);
  UnityEditor.PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, UnityEditor.InteractionMode.AutomatedAction);
}
else
{
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
  UnityEditor.PrefabUtility.UnloadPrefabContents(root);
  if (furn.Find("Class poster wall") == null)
  {
    var inst = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(PrefabPath), scene);
    inst.transform.SetParent(furn, true);
  }
}
UnityEditor.AssetDatabase.SaveAssets();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
System.IO.File.WriteAllText("Temp/Posters/build.txt", sb.ToString()); return "ok";

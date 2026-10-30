// The Crypt Lord (Characters/Enemies/Crypt/Crypt Lord, Dungeon1's floor 3 boss) in a Sidekick Fantasy Skeletons body of
// his own, put together from single parts: the bare skeleton (head, eyes, teeth, legs) under an ornate crowned helm with
// a crest, a spiked plate cuirass, spiked pauldrons, a long tattered cape, a plated war belt, plated arms, gauntlets,
// knee guards and sabatons. One palette for all of it (Tools/crypt_lord_palette.py): blackened iron, old gold, deep
// crimson, aged ivory bone, burning eyes. The parts are joined into one skinned mesh on the first preset's rig and avatar
// (every Sidekick part is skinned to the same skeleton in the same bind pose, so bones are matched by name), restyled in
// the pixel look at 128 texels a metre as he stands (the root's scale counted in), and put into the boss's prefab in place
// of the old body: same prefab, so the biome, the boss fight and his data keep pointing at him. He stands a little taller
// than his old body did (Bigger), measured crown to sole as he stands in game; later runs keep the height of the first.
// Run in edit mode, safe to run again. The parts come from the asset library (Tools/asset_library.py pull) when missing.
// Writes Temp/Realms/crypt_lord.txt.
if (UnityEngine.Application.isPlaying) return "playing";
const string PATH = "Assets/Game/Characters/Enemies/Crypt/Crypt Lord.prefab";
const string SK = "Assets/Synty/SidekickCharacters";
const string DIR = "Assets/Game/Art/Synty Pixel/Characters/Crypt Lord (Sidekick)";
const float TPU = 128f;
const float Bigger = 1.12f;   // his new height against his old body's
var sb = new System.Text.StringBuilder();
try {
string O = $"{SK}/Resources/Meshes/Outfits/FantasySkeletons/SK_FANT_SKTN_", B = $"{SK}/Resources/Meshes/Species/Skeleton/SK_SKTN_BASE_01_";
var parts = new System.Collections.Generic.List<string>();
foreach (var p in new[] { "01HEAD", "05EYEL", "06EYER", "36TETH", "18LEGL", "19LEGR" }) parts.Add($"{B}{p}_SN01.fbx");
foreach (var p in new[] { "06_22AHED", "07_10TORS", "03_24ABAC", "04_29ASHL", "04_30ASHR", "01_17HIPS", "02_33AKNL", "02_34AKNR",
                          "02_11AUPL", "02_12AUPR", "02_13ALWL", "02_14ALWR", "02_15HNDL", "02_16HNDR", "02_20FOTL", "02_21FOTR" }) parts.Add($"{O}{p}_SN01.fbx");
var presetPath = $"{SK}/Characters/FantasySkeletons/FantasySkeletons_01/FantasySkeletons_01.prefab";
bool missing = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(presetPath) == null;
foreach (var p in parts) missing |= UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p) == null;
if (missing)
{
  var all = new System.Collections.Generic.List<string>(parts) { presetPath };
  var psi = new System.Diagnostics.ProcessStartInfo("python", "Tools/asset_library.py pull " + string.Join(" ", all.ConvertAll(a => "\"" + a + "\""))) { UseShellExecute = false, CreateNoWindow = true };
  using (var pr = System.Diagnostics.Process.Start(psi)) pr.WaitForExit();
  UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  sb.Append("pulled the parts from the asset library\n");
}
System.IO.Directory.CreateDirectory(DIR);
// The palette.
var palPath = $"{DIR}/Crypt Lord palette.png";
{
  var psi = new System.Diagnostics.ProcessStartInfo("python", $"Tools/crypt_lord_palette.py \"{palPath}\"") { UseShellExecute = false, CreateNoWindow = true };
  using (var pr = System.Diagnostics.Process.Start(psi)) pr.WaitForExit();
  UnityEditor.AssetDatabase.ImportAsset(palPath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(palPath);
  ti.filterMode = UnityEngine.FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.mipmapEnabled = false;
  ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; ti.isReadable = true; ti.SaveAndReimport();
}
var palette = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(palPath);

// The rig: the first preset's skeleton (its own body is dropped).
var preset = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(presetPath));
preset.hideFlags = UnityEngine.HideFlags.DontSave;
UnityEngine.Mesh joined = null; UnityEngine.Transform[] rigBones = null; UnityEngine.Transform rootBone = null;
var bonesByName = new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>();
foreach (var t in preset.GetComponentsInChildren<UnityEngine.Transform>(true)) bonesByName[t.name] = t;
try
{
  // Join: every part's vertices, normals, UVs and weights, its bones remapped to one list by name; bind poses taken from
  // the parts (the same bind pose for a bone in every part).
  var boneList = new System.Collections.Generic.List<UnityEngine.Transform>(); var bindposes = new System.Collections.Generic.List<UnityEngine.Matrix4x4>();
  var index = new System.Collections.Generic.Dictionary<string, int>();
  var v = new System.Collections.Generic.List<UnityEngine.Vector3>(); var n = new System.Collections.Generic.List<UnityEngine.Vector3>();
  var uv = new System.Collections.Generic.List<UnityEngine.Vector2>(); var bw = new System.Collections.Generic.List<UnityEngine.BoneWeight>();
  var tris = new System.Collections.Generic.List<int>();
  foreach (var p in parts)
  {
    var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
    if (model == null) { sb.Append($"MISSING {p}\n"); continue; }
    foreach (var smr in model.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
    {
      var m = smr.sharedMesh; if (m == null) continue;
      var map = new int[smr.bones.Length];
      var bp = m.bindposes;
      for (int i = 0; i < smr.bones.Length; i++)
      {
        var name = smr.bones[i] != null ? smr.bones[i].name : "";
        if (!index.TryGetValue(name, out int k))
        {
          if (!bonesByName.TryGetValue(name, out var rig)) { sb.Append($"  no rig bone {name} for {System.IO.Path.GetFileName(p)}\n"); k = -1; }
          else { k = boneList.Count; boneList.Add(rig); bindposes.Add(bp[i]); index[name] = k; }
        }
        map[i] = k;
      }
      int at = v.Count;
      v.AddRange(m.vertices); n.AddRange(m.normals);
      var muv = new System.Collections.Generic.List<UnityEngine.Vector2>(); m.GetUVs(0, muv); uv.AddRange(muv);
      foreach (var w in m.boneWeights)
      {
        int R(int i) => i >= 0 && i < map.Length && map[i] >= 0 ? map[i] : 0;
        bw.Add(new UnityEngine.BoneWeight { boneIndex0 = R(w.boneIndex0), weight0 = w.weight0, boneIndex1 = R(w.boneIndex1), weight1 = w.weight1, boneIndex2 = R(w.boneIndex2), weight2 = w.weight2, boneIndex3 = R(w.boneIndex3), weight3 = w.weight3 });
      }
      for (int s = 0; s < m.subMeshCount; s++) foreach (var t in m.GetTriangles(s)) tris.Add(t + at);
      if (rootBone == null && smr.rootBone != null && bonesByName.TryGetValue(smr.rootBone.name, out var rb)) rootBone = rb;
    }
  }
  joined = new UnityEngine.Mesh { name = "Crypt Lord (Sidekick) joined", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
  joined.SetVertices(v); joined.SetNormals(n); joined.SetUVs(0, uv); joined.boneWeights = bw.ToArray();
  joined.SetTriangles(tris, 0); joined.bindposes = bindposes.ToArray(); joined.RecalculateBounds();
  rigBones = boneList.ToArray();
  sb.Append($"joined {parts.Count} parts: {v.Count} vertices, {tris.Count / 3} triangles, {rigBones.Length} bones\n");

  // Into the boss's prefab: the old body out, the rig and the joined body in, on the preset's avatar.
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(PATH);
  try
  {
    var modelT = root.transform.Find("Model");
    var anim = modelT.GetComponent<UnityEngine.Animator>();
    float oldHeight = 0; { var bb = new UnityEngine.Bounds(); bool any = false; foreach (var r in modelT.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>()) { if (!r.gameObject.activeInHierarchy) continue; if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds); } oldHeight = bb.size.y; }
    // A later run finds the Sidekick body already in: the height it was given is kept (written beside the tool's output).
    var heightFile = $"{DIR}/height.txt";
    float Height = System.IO.File.Exists(heightFile) ? float.Parse(System.IO.File.ReadAllText(heightFile), System.Globalization.CultureInfo.InvariantCulture) : oldHeight * Bigger;
    System.IO.File.WriteAllText(heightFile, Height.ToString(System.Globalization.CultureInfo.InvariantCulture));
    for (int i = modelT.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(modelT.GetChild(i).gameObject);
    var srcAnim = preset.GetComponent<UnityEngine.Animator>(); if (srcAnim != null) anim.avatar = srcAnim.avatar;
    // the preset's children (its rig and its body) move under Model; its body becomes the joined one
    var kids = new System.Collections.Generic.List<UnityEngine.Transform>(); foreach (UnityEngine.Transform c in preset.transform) kids.Add(c);
    UnityEngine.SkinnedMeshRenderer body = null;
    foreach (var c in kids)
    {
      c.SetParent(modelT, false);
      var smr = c.GetComponent<UnityEngine.SkinnedMeshRenderer>();
      if (smr != null) { if (body == null) body = smr; else UnityEngine.Object.DestroyImmediate(c.gameObject); }
    }
    body.name = "Crypt Lord body";
    body.sharedMesh = joined; body.bones = rigBones; if (rootBone != null) body.rootBone = rootBone;
    body.sharedMaterials = new[] { new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")) };
    body.sharedMaterial.SetTexture("_BaseMap", palette); body.sharedMaterial.SetColor("_BaseColor", UnityEngine.Color.white);
    // His size: Height crown to sole, set by the root's scale (the model inside stays at 1).
    root.transform.localScale = UnityEngine.Vector3.one;
    modelT.localScale = UnityEngine.Vector3.one;
    body.updateWhenOffscreen = true; var raw = body.bounds.size.y;
    float k2 = Height / UnityEngine.Mathf.Max(.1f, raw);
    root.transform.localScale = UnityEngine.Vector3.one * k2;
    sb.Append($"old body {oldHeight:F2} m; new body {raw:F2} m raw, root scale {k2:F3} -> {Height} m\n");
    // The pixel look at 128 texels a metre as he stands (measured on the skinned pose, baked again until within 5%).
    var srcMesh = body.sharedMesh; float aim = TPU * k2, measured = 0;
    var mats = new[] { body.sharedMaterial };
    for (int pass = 0; pass < 4; pass++)
    {
      UnityEditor.AssetDatabase.DeleteAsset($"{DIR}/Crypt Lord.asset");
      var (mesh, mat) = PixelRestyle.Build(srcMesh, mats, "Characters", "Crypt Lord", aim, DIR);
      if (mesh == null) break;
      body.sharedMesh = mesh; body.sharedMaterials = new[] { mat };
      UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
      measured = PixelRestyle.TexelsPerUnit(body, mat);
      if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
      aim *= TPU / measured;
    }
    body.updateWhenOffscreen = false;
    sb.Append($"restyled: {measured:F1} texels a metre\n");
    var data = root.GetComponent<Character>().data as EnemyData;
    if (data != null) { data.creatureKind = CreatureKind.Skeleton; data.gore ??= new GoreProfile(); data.gore.kind = BloodKind.BoneDust; UnityEditor.EditorUtility.SetDirty(data); }
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, PATH);
    sb.Append($"saved {PATH}: avatar {anim.avatar?.name} valid {anim.avatar != null && anim.avatar.isValid}\n");
  }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
finally { UnityEngine.Object.DestroyImmediate(preset); }
UnityEditor.AssetDatabase.SaveAssets();
} catch (System.Exception e) { sb.Append("ERR " + e); }
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/crypt_lord.txt", sb.ToString());
return sb.ToString();

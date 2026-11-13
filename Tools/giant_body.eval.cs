// The sleeping giant's body (the user, 2026-11-13): the Dungeon Realms demon (Chr_BR_Demon_01, the Bridge Troll's model) in
// the pack's colour B (Materials/Alts/PolygonDungeonRealms_01_B, pulled from the asset library), without its wings, on
// Characters/Enemies/Events/Sleeping giant.prefab at the giant's own scale, with the pack's avatar. Its pixel copy goes to
// Art/Synty Pixel/Characters/Sleeping giant at 128 texels a metre in the game (PixelRestyle.TexelsPerUnit counts the
// root's scale for a skinned body since 2026-11-13; before, the measure was divided by it here), graded like the Pit Demon's hide
// (Tools/realms_grade.py skin). Stats, weapon and everything else on the prefab are left as they are.
// Run in edit mode from an eval (give it ten minutes); safe to run again: the body is rebuilt from the Synty source.
if (UnityEngine.Application.isPlaying) return "playing";
const string GP = "Assets/Game/Characters/Enemies/Events/Sleeping giant.prefab";
const float TPU = 128f;
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var bodySrc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Synty/PolygonDungeonRealms/Prefabs/Characters/Chr_BR_Demon_01.prefab");
var matB = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>("Assets/Synty/PolygonDungeonRealms/Materials/Alts/PolygonDungeonRealms_01_B.mat");
if (bodySrc == null || matB == null) return "missing source: body " + (bodySrc != null) + " material " + (matB != null);
UnityEngine.Material Readable(UnityEngine.Material m)
{
  var copy = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")) { name = m.name };
  UnityEngine.Texture t = null; foreach (var p in new[] { "_Albedo_Map", "_Base_Texture", "_BaseMap", "_MainTex" }) if (m.HasProperty(p) && m.GetTexture(p) != null) { t = m.GetTexture(p); break; }
  copy.SetTexture("_BaseMap", t); copy.SetColor("_BaseColor", UnityEngine.Color.white);
  sb.Append("  source texture " + (t != null ? t.name : "NONE") + "\n");
  return copy;
}
const string dir = "Assets/Game/Art/Synty Pixel/Characters/Sleeping giant";
if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/Art/Synty Pixel/Characters", "Sleeping giant");
var root = UnityEditor.PrefabUtility.LoadPrefabContents(GP);
try
{
  float scale = root.transform.localScale.x;
  var model = root.transform.Find("Model");
  var anim = model.GetComponent<UnityEngine.Animator>();
  // The old skeleton and bodies out, the pack demon's skeleton and its one active body in, on its own avatar.
  for (int i = model.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(model.GetChild(i).gameObject);
  var src = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(bodySrc);
  var srcAnim = src.GetComponent<UnityEngine.Animator>();
  anim.avatar = srcAnim != null ? srcAnim.avatar : anim.avatar;
  var parts = new System.Collections.Generic.List<UnityEngine.Transform>(); foreach (UnityEngine.Transform c in src.transform) parts.Add(c);
  foreach (var c in parts)
  {
    var smr = c.GetComponent<UnityEngine.SkinnedMeshRenderer>();
    if (smr != null && !c.gameObject.activeSelf) { UnityEngine.Object.DestroyImmediate(c.gameObject); continue; }
    c.SetParent(model, false);
  }
  UnityEngine.Object.DestroyImmediate(src);
  // No wings (the user, 2026-11-13), nor any other loose attachment on its bones.
  foreach (var mr in model.GetComponentsInChildren<UnityEngine.MeshRenderer>(true)) { sb.Append("  removed " + mr.name + "\n"); UnityEngine.Object.DestroyImmediate(mr.gameObject); }
  foreach (var body in model.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
  {
    if (body.sharedMesh == null) continue;
    var keepMesh = body.sharedMesh;
    var mats = new UnityEngine.Material[body.sharedMaterials.Length]; for (int i = 0; i < mats.Length; i++) mats[i] = Readable(matB);
    float aim = TPU * scale, measured = 0; UnityEngine.Mesh outMesh = null; UnityEngine.Material outMat = null;
    for (int pass = 0; pass < 4; pass++)
    {
      UnityEditor.AssetDatabase.DeleteAsset($"{dir}/Sleeping giant.asset");
      (outMesh, outMat) = PixelRestyle.Build(keepMesh, mats, "Characters", "Sleeping giant", aim, dir);
      if (outMesh == null) break;
      body.sharedMesh = outMesh; body.sharedMaterials = new[] { outMat };
      UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(outMat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
      // Measured as the body stands, the root's scale counted in (PixelRestyle.TexelsPerUnit).
      measured = PixelRestyle.TexelsPerUnit(body, outMat);
      sb.Append($"  pass {pass}: aim {aim:F1} measured {measured:F1}\n");
      if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .05f) break;
      aim *= TPU / measured;
    }
    if (outMesh == null) { sb.Append("  BUILD FAILED\n"); continue; }
    body.updateWhenOffscreen = false; body.name = "Giant body";
    // The hide stepped into hard-edged tones, as the Pit Demon's is (Tools/realms_grade.py skin).
    var texPath = UnityEditor.AssetDatabase.GetAssetPath(outMat.GetTexture("_BaseMap"));
    var psi = new System.Diagnostics.ProcessStartInfo("python", $"Tools/realms_grade.py skin \"{texPath}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };
    using (var p = System.Diagnostics.Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(); sb.Append("  grade: " + o.Trim() + "\n"); }
    UnityEditor.AssetDatabase.ImportAsset(texPath, UnityEditor.ImportAssetOptions.ForceUpdate);
    var tex = outMat.GetTexture("_BaseMap");
    sb.Append($"  body {outMesh.triangles.Length / 3} triangles, texture {tex.width} x {tex.height}, {PixelRestyle.TexelsPerUnit(body, outMat):F1} texels a metre in the game\n");
  }
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, GP);
  sb.Append("saved, avatar " + anim.avatar.name + " scale " + scale + "\n");
}
catch (System.Exception e) { sb.Append("ERR " + e.Message + "\n" + e.StackTrace + "\n"); }
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();

// Bakes a Realms enemy's body again at 128 texels a metre as it stands in the game (the user, 2026-11-13: the Pit Demon
// and Bridge Troll were 107, the Bone Brute 116, the Forge dwarves 178 and their king 160, because the measure the first
// bake went by left the root's scale out; PixelRestyle.TexelsPerUnit counts it now). Only the body's own mesh, material
// and texture in Art/Synty Pixel/Characters/<enemy> are written, in place (the same files, so the prefab and its variants
// keep pointing at them): from the same Dungeon Realms source by the same restyle, then the same grade
// (Tools/realms_grade.py), so the look is the one it had. The prefab itself, its attachments and its weapon are not touched.
// Temp/Realms/rebake.txt names the enemies, one a line (none: every one more than 5% off). Run in edit mode; safe to run again.
if (UnityEngine.Application.isPlaying) return "playing";
const string D = "Assets/Game/Characters/Enemies/Realms";
const string SC = "Assets/Synty/PolygonDungeonRealms/Prefabs/Characters/";
const float TPU = 128f;
var sb = new System.Text.StringBuilder();
UnityEngine.Material Readable(UnityEngine.Material m)
{
  var copy = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")) { name = m.name };
  UnityEngine.Texture t = null; foreach (var p in new[] { "_Albedo_Map", "_Base_Texture", "_BaseMap", "_MainTex" }) if (m.HasProperty(p) && m.GetTexture(p) != null) { t = m.GetTexture(p); break; }
  copy.SetTexture("_BaseMap", t); copy.SetColor("_BaseColor", UnityEngine.Color.white);
  bool glow = m.HasProperty("_Enable_Emission") && m.GetFloat("_Enable_Emission") > .5f;
  var e = m.HasProperty("_Emission_Map") ? m.GetTexture("_Emission_Map") : null;
  if (glow && e != null) { copy.SetTexture("_EmissionMap", e); copy.SetColor("_EmissionColor", UnityEngine.Color.white); copy.EnableKeyword("_EMISSION"); }
  return copy;
}
// The enemy, the pack character its body is, and its grade (Tools/realms_enemies.eval.cs's own list).
var specs = new (string file, string body, string grade)[]
{
  ("Hall Skeleton", "Chr_Skeleton_01", "bones"), ("Hall Raider", "Chr_Skeleton_02", "bones"), ("Bone Brute", "Chr_Skeleton_03", "charred"),
  ("Undead Knight", "Chr_Undead_Knight_01", null), ("Forge Guard", "Chr_BR_Dwarf_Soldier_Male_01", null), ("Forge Shieldmaiden", "Chr_BR_Dwarf_Soldier_Female_01", null),
  ("Forge Miner", "Chr_BR_Dwarf_Miner_01", null), ("Forge King", "Chr_BR_Dwarf_King_01", null), ("Pit Demon", "Chr_BR_Demon_01", "skin"),
};
var only = System.IO.File.Exists("Temp/Realms/rebake.txt") ? new System.Collections.Generic.HashSet<string>(System.Array.ConvertAll(System.IO.File.ReadAllLines("Temp/Realms/rebake.txt"), l => l.Trim())) : null;
foreach (var s in specs)
{
  if (only != null && !only.Contains(s.file)) continue;
  var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{D}/{s.file}.prefab");
  var bodySrc = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>($"{SC}{s.body}.prefab");
  if (prefab == null || bodySrc == null) { sb.Append($"{s.file}: MISSING prefab or source\n"); continue; }
  var dir = $"Assets/Game/Art/Synty Pixel/Characters/{s.file}";
  // A copy of the enemy in the scene, at its own scale: the body is measured on it as it stands.
  var inst = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(prefab);
  try
  {
    int n = 0;
    foreach (var smr in inst.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
    {
      if (smr.sharedMesh == null) continue;
      string name = n == 0 ? s.file : $"{s.file} {smr.name}"; n++;
      var mat = smr.sharedMaterial;
      float before = PixelRestyle.TexelsPerUnit(smr, mat);
      if (only == null && UnityEngine.Mathf.Abs(before / TPU - 1f) <= .05f) { sb.Append($"{name}: {before:F1}, left as it is\n"); continue; }
      UnityEngine.SkinnedMeshRenderer source = null;
      foreach (var c in bodySrc.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) if (c.name == smr.name) source = c;
      if (source == null || UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMesh) != $"{dir}/{name}.asset") { sb.Append($"{name}: no source part or not the tool's mesh ({UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMesh)}), skipped\n"); continue; }
      var mats = System.Array.ConvertAll(source.sharedMaterials, m => m != null ? Readable(m) : null);
      var oldTex = mat.GetTexture("_BaseMap"); string was = $"{oldTex.width} x {oldTex.height}";
      float aim = TPU * inst.transform.lossyScale.y, measured = before;
      for (int pass = 0; pass < 5; pass++)
      {
        var (outMesh, outMat) = PixelRestyle.Build(source.sharedMesh, mats, "Characters", name, aim, dir);
        if (outMesh == null) { sb.Append($"{name}: BUILD FAILED\n"); break; }
        UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(outMat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
        smr.sharedMesh = outMesh; smr.sharedMaterials = new[] { outMat }; mat = outMat;
        measured = PixelRestyle.TexelsPerUnit(smr, outMat);
        if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .02f) break;
        aim *= TPU / measured;
      }
      if (n == 1 && s.grade != null)
      {
        var texPath = UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap"));
        var psi = new System.Diagnostics.ProcessStartInfo("python", $"Tools/realms_grade.py {s.grade} \"{texPath}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };
        using (var p = System.Diagnostics.Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(); sb.Append($"  grade {s.grade}: {o.Trim()}\n"); }
        UnityEditor.AssetDatabase.ImportAsset(texPath, UnityEditor.ImportAssetOptions.ForceUpdate);
      }
      var tex = mat.GetTexture("_BaseMap");
      sb.Append($"{name}: {before:F1} -> {measured:F1} texels a metre at scale {inst.transform.lossyScale.y:0.##} (texture {was} -> {tex.width} x {tex.height}, {smr.sharedMesh.triangles.Length / 3} triangles)\n");
    }
  }
  finally { UnityEngine.Object.DestroyImmediate(inst); }
}
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Realms"); System.IO.File.WriteAllText("Temp/Realms/rebake_log.txt", sb.ToString());
return sb.ToString();

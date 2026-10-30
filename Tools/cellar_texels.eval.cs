// The cellar furnishing (Levels/Shared/Props/Furnishing and Groups) at the dungeon's 128 texels a metre, each part
// measured where it stands in its prefab. A part drawn finer (a candle in a cluster, a jar on a shelf, a crate set smaller
// in a heap) gets its own copy of its texture with fewer texels (Tools/texel_fit.py, box filtered, in
// Levels/Shared/Props/Texel fit, named for its density so two sizes of one jar each have theirs); a part drawn coarser
// (a barrel set 1.2x in a group, a candle on a barrel) is set back toward the size its art was baked for. Then the heaps
// that reach the edge of their 2 m cell are drawn in 2 cm, so a crate's side never lies in the plane of a wall beside it.
// Safe to run again (copies are made from the original textures). Writes Temp/Realms/cellar_texels.txt. Edit mode.
if (UnityEngine.Application.isPlaying) return "playing";
const float TPU = 128f;
const string FIT = "Assets/Game/Levels/Shared/Props/Texel fit";
var sb = new System.Text.StringBuilder();
if (!UnityEditor.AssetDatabase.IsValidFolder(FIT)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/Levels/Shared/Props", "Texel fit");
string Run(string args)
{
  var psi = new System.Diagnostics.ProcessStartInfo("python", args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };
  using var p = System.Diagnostics.Process.Start(psi);
  var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(); return o.Trim();
}
UnityEngine.Material Fitted(UnityEngine.Material m, float density)
{
  // the original, also when m is already a fitted copy from an earlier run
  var name = m.name.Contains(" fit ") ? m.name.Substring(0, m.name.IndexOf(" fit ")) : m.name;
  var src = m.name.Contains(" fit ") ? UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(System.IO.File.ReadAllText($"{FIT}/{name}.source")) : m;
  var srcTex = src.GetTexture("_BaseMap"); var srcPath = UnityEditor.AssetDatabase.GetAssetPath(srcTex);
  int tag = UnityEngine.Mathf.RoundToInt(density);
  string png = $"{FIT}/{name} fit {tag}.png", mat = $"{FIT}/{name} fit {tag}.mat";
  System.IO.File.WriteAllText($"{FIT}/{name}.source", UnityEditor.AssetDatabase.GetAssetPath(src));
  sb.Append("    " + Run($"Tools/texel_fit.py \"{srcPath}\" \"{png}\" {(TPU / density).ToString(System.Globalization.CultureInfo.InvariantCulture)}") + "\n");
  UnityEditor.AssetDatabase.ImportAsset(png, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
  ti.filterMode = UnityEngine.FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
  ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
  var copy = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(mat);
  if (copy == null) { copy = new UnityEngine.Material(src); UnityEditor.AssetDatabase.CreateAsset(copy, mat); }
  else copy.CopyPropertiesFromMaterial(src);
  copy.SetTexture("_BaseMap", UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(png));
  if (copy.HasProperty("_MainTex")) copy.SetTexture("_MainTex", copy.GetTexture("_BaseMap"));
  UnityEditor.EditorUtility.SetDirty(copy);
  return copy;
}
foreach (var folder in new[] { "Assets/Game/Levels/Shared/Props/Furnishing", "Assets/Game/Levels/Shared/Props/Groups" })
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
{
  var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  var root = UnityEditor.PrefabUtility.LoadPrefabContents(path);
  bool changed = false;
  try
  {
    foreach (var r in root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
    {
      var mf = r.GetComponent<UnityEngine.MeshFilter>();
      var m = r.sharedMaterial;
      if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable || m == null || !m.HasProperty("_BaseMap") || m.GetTexture("_BaseMap") == null) continue;
      if (m.name.Contains("lame") || m.name.Contains("Glow") || m.name.Contains("Web")) continue;
      // measured against the original texture when this part already wears a fitted copy
      var orig = m.name.Contains(" fit ") ? UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(System.IO.File.ReadAllText($"{FIT}/{m.name.Substring(0, m.name.IndexOf(" fit "))}.source")) : m;
      float d = TexelCheck.Measure(r.transform, mf.sharedMesh, 0, orig.GetTexture("_BaseMap"), orig.GetTextureScale("_BaseMap"));
      if (d <= 0 || UnityEngine.Mathf.Abs(d / TPU - 1f) <= .05f) { if (m != orig) { r.sharedMaterial = orig; changed = true; } continue; }
      if (d > TPU)
      {
        r.sharedMaterial = Fitted(orig, d); changed = true;
        sb.Append($"  {System.IO.Path.GetFileNameWithoutExtension(path)}/{r.name}: {d:F0} -> fitted copy\n");
      }
      else
      {
        // set back toward its art's size, its foot where it was
        var before = r.bounds.min.y; float k = d / TPU;
        r.transform.localScale *= k;
        var after = r.bounds.min.y; r.transform.position += UnityEngine.Vector3.up * (before - after);
        changed = true;
        sb.Append($"  {System.IO.Path.GetFileNameWithoutExtension(path)}/{r.name}: {d:F0} -> scaled x{k:F2}\n");
      }
    }
    // A heap reaching its cell's edge: drawn 2 cm in from it on each side, so its side lies off a wall beside it.
    if (folder.EndsWith("Groups"))
    {
      var b = new UnityEngine.Bounds(root.transform.position, UnityEngine.Vector3.zero);
      foreach (var r in root.GetComponentsInChildren<UnityEngine.Renderer>()) b.Encapsulate(r.bounds);
      float reach = UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(b.min.x - root.transform.position.x), UnityEngine.Mathf.Abs(b.max.x - root.transform.position.x));
      if (reach > .97f)
      {
        float k = .97f / reach;
        foreach (UnityEngine.Transform c in root.transform) c.localPosition = new UnityEngine.Vector3(c.localPosition.x * k, c.localPosition.y, c.localPosition.z);
        changed = true; sb.Append($"  {System.IO.Path.GetFileNameWithoutExtension(path)}: reached {reach:F3} m to the side, drawn in\n");
      }
    }
    if (changed) UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, path);
  }
  finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
UnityEditor.AssetDatabase.SaveAssets();
System.IO.Directory.CreateDirectory("Temp/Realms");
System.IO.File.WriteAllText("Temp/Realms/cellar_texels.txt", sb.ToString());
return sb.ToString();

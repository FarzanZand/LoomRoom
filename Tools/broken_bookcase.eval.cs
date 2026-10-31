// The broken bookcase (Levels/Shared/Props/Furnishing/Broken bookcase) in the hand-made wood finish the other cellar
// furniture has (Tools/prop_structure.py: planks as faces, five shades a material, lit top edges, dark bottoms, grain,
// darker creases), from its plain pixel copy at 128 texels a unit, instead of the old five-view painting the user saw as
// AI art. Writes "<mesh> structured.png/.mat" beside the copy and points the prefab at it. Safe to run again. Edit mode.
if (UnityEngine.Application.isPlaying) return "playing";
const string Key = "SM_Prop_Bookshelf_01_Damaged_01";
const string Dir = "Assets/Game/Art/Synty Pixel/Props/" + Key;
const string Prefab = "Assets/Game/Levels/Shared/Props/Furnishing/Broken bookcase.prefab";
var sb = new System.Text.StringBuilder();
string meshPath = $"{Dir}/{Key}.asset", plainPath = $"{Dir}/{Key}.png", outPng = $"{Dir}/{Key} structured.png", outMat = $"{Dir}/{Key} structured.mat";
var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(plainPath);
var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(meshPath);
if (tex == null || mesh == null) return "missing plain copy";
System.IO.Directory.CreateDirectory("Temp/Props");
string gb = $"Temp/Props/{Key}.bin";
sb.Append(PixelRestyle.BakeGBuffer(meshPath, tex.width, tex.height, gb, "synty:" + Key) + "\n");
var psi = new System.Diagnostics.ProcessStartInfo("python", $"Tools/prop_structure.py \"{plainPath}\" \"{gb}\" \"{outPng}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = System.IO.Directory.GetCurrentDirectory() };
using (var p = System.Diagnostics.Process.Start(psi)) { sb.Append(p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd()); p.WaitForExit(); if (p.ExitCode != 0) return "painter failed: " + sb; }
UnityEditor.AssetDatabase.ImportAsset(outPng, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(outPng);
var src = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(plainPath);
ti.filterMode = src.filterMode; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.mipmapEnabled = src.mipmapEnabled;
ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; ti.isReadable = src.isReadable; ti.SaveAndReimport();
var plainMat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>($"{Dir}/{Key}.mat");
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(outMat);
if (mat == null) { mat = new UnityEngine.Material(plainMat) { name = Key + " structured" }; UnityEditor.AssetDatabase.CreateAsset(mat, outMat); }
else mat.CopyPropertiesFromMaterial(plainMat);
var structured = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(outPng);
mat.SetTexture("_BaseMap", structured); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", structured);
UnityEditor.EditorUtility.SetDirty(mat);
var root = UnityEditor.PrefabUtility.LoadPrefabContents(Prefab);
try
{
  foreach (var r in root.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
  {
    var mf = r.GetComponent<UnityEngine.MeshFilter>();
    sb.Append($"{r.name}: mesh {(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")} mats {string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.name : "null"))}\n");
    if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.name.StartsWith(Key)) continue;
    mf.sharedMesh = mesh; r.sharedMaterials = new[] { mat };
    sb.Append("  -> plain mesh, structured material\n");
  }
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, Prefab);
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/Props/bookcase.txt", sb.ToString());
return sb.ToString();

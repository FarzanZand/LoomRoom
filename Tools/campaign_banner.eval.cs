// The banner over new places on the realm map (CampaignManager.Markers): Resources/Campaign new place banner, a quad
// with Campaign/Map/New place banner.png (Tools/map_banner.py, 9 x 16 texels) at the map's 6.4 texels a unit, its foot at
// the prefab's pivot, cut out, both sides, unlit (lit, a vertical cloth under the lamp over the map went dark), point filtered, no shadow; CampaignBanner turns it to the view and bobs it.
// Also Resources/Elite glow (Characters/Enemies/Elite/Elite Glow.shader, EliteEnemy). Safe to run again. Edit mode.
var sb = new System.Text.StringBuilder();
const float Texels = 6.4f;
string png = "Assets/Game/Campaign/Map/New place banner.png";
var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
ti.textureType = UnityEditor.TextureImporterType.Default; ti.filterMode = UnityEngine.FilterMode.Point; ti.mipmapEnabled = false;
ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.alphaIsTransparency = true; ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(png);
string matPath = "Assets/Game/Campaign/Map/New place banner.mat";
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(matPath);
if (mat == null) { mat = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Unlit")); UnityEditor.AssetDatabase.CreateAsset(mat, matPath); }
if (mat.shader.name != "Universal Render Pipeline/Unlit") mat.shader = UnityEngine.Shader.Find("Universal Render Pipeline/Unlit");
mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", new UnityEngine.Color(.9f, .9f, .9f, 1));
mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", .5f); mat.EnableKeyword("_ALPHATEST_ON");
mat.SetFloat("_Cull", 0); mat.SetFloat("_Smoothness", 0); mat.SetFloat("_SpecularHighlights", 0); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
UnityEditor.EditorUtility.SetDirty(mat);
float w = tex.width / Texels, h = tex.height / Texels;
var root = new UnityEngine.GameObject("Campaign new place banner");
root.AddComponent<CampaignBanner>();
var quad = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Quad);
UnityEngine.Object.DestroyImmediate(quad.GetComponent<UnityEngine.Collider>());
quad.name = "Banner"; quad.transform.SetParent(root.transform, false);
quad.transform.localPosition = new UnityEngine.Vector3(0, h * .5f, 0); quad.transform.localScale = new UnityEngine.Vector3(w, h, 1);
var mr = quad.GetComponent<UnityEngine.MeshRenderer>(); mr.sharedMaterial = mat;
mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
string prefab = "Assets/Game/Campaign/Resources/Campaign new place banner.prefab";
System.IO.Directory.CreateDirectory("Assets/Game/Campaign/Resources");
UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefab); UnityEngine.Object.DestroyImmediate(root);
sb.Append($"banner {w:F2} x {h:F2} units -> {prefab}\n");
string glowPath = "Assets/Game/Characters/Resources/Elite glow.mat";
var glow = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(glowPath);
var shader = UnityEngine.Shader.Find("LoomRoom/Elite Glow");
if (shader == null) sb.Append("Elite Glow shader missing\n");
else
{
  if (glow == null) { glow = new UnityEngine.Material(shader) { name = "Elite glow" }; UnityEditor.AssetDatabase.CreateAsset(glow, glowPath); }
  else glow.shader = shader;
  UnityEditor.EditorUtility.SetDirty(glow); sb.Append("elite glow material " + glowPath + "\n");
}
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/Ciri/banner.txt", sb.ToString());
return sb.ToString();

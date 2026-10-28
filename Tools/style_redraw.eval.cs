// Brings Tools/style_redraw.py's textures into the project (run the script with the folder below first): the Ruins styles,
// two to a folder under Levels/Shared/Room styles/Ruins (Ruins<N> stone and Ruins<N> timber, named "<style> <surface>.png") (imported like the Tavern style's: point filter, no power-of-two
// scaling, uncompressed), a material per PNG (a copy of Tavern wall.mat, tiling 1) and the style asset, a copy of the old
// style it redraws with every material swapped for the new one. Safe to run again: it overwrites what it made.
if (UnityEngine.Application.isPlaying) return "stop Play first";
const string Root = "Assets/Game/Levels/Shared/Room styles";
const string Ruins = Root + "/Ruins";
var log = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh();
var template = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(Root + "/Tavern/Tavern wall.mat");
var styles = new (string name, string old, string unused)[] {
  ("Ruins1 timber", "Timber and Stone", null), ("Ruins2 timber", "Timber and Stone", null), ("Ruins3 timber", "Timber and Stone", null),
  ("Ruins1 stone", "_Archive/Crypt/Dungeon1 default style", null), ("Ruins2 stone", "_Archive/Crypt/Dungeon1 default style", null), ("Ruins3 stone", "_Archive/Crypt/Dungeon1 default style", null) };
UnityEngine.Material Mat(string dir, string surface, string style)
{
  string png = $"{dir}/{style} {surface}.png";
  var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(png);
  ti.filterMode = UnityEngine.FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
  ti.mipmapEnabled = true; ti.wrapMode = UnityEngine.TextureWrapMode.Repeat; ti.maxTextureSize = 2048; ti.SaveAndReimport();
  string path = $"{dir}/{style} {surface}.mat";
  var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
  if (m == null) { m = new UnityEngine.Material(template); UnityEditor.AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(template);
  var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(png);
  m.SetTexture("_BaseMap", tex); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
  m.SetTextureScale("_BaseMap", UnityEngine.Vector2.one); m.SetTextureOffset("_BaseMap", UnityEngine.Vector2.zero);
  UnityEditor.EditorUtility.SetDirty(m); return m;
}
foreach (var (name, old, _) in styles)
{
  string dir = $"{Ruins}/{name.Split(' ')[0]}";
  if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) { log.Append($"{name}: folder missing\n"); continue; }
  var oldStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>($"{Root}/{old}.asset");
  var style = UnityEngine.Object.Instantiate(oldStyle);
  if (old == "Timber and Stone")
  {
    style.bottomWall = Mat(dir, "lower", name); style.upperWall = Mat(dir, "upper", name); style.ceiling = Mat(dir, "ceiling", name); style.floor = Mat(dir, "floor", name);
    var lb = Mat(dir, "lower b", name); var ub = Mat(dir, "upper b", name); var fb = Mat(dir, "floor b", name);
    // the old variants were two per wall: one per row kind now, each the same drawing from another seed
    style.wallVariants = new DungeonSurfaceVariant[] { new DungeonSurfaceVariant { material = lb, weight = 3, rows = DungeonVariantRows.BottomOnly }, new DungeonSurfaceVariant { material = ub, weight = 3, rows = DungeonVariantRows.UpperOnly } };
    style.floorVariants = new DungeonSurfaceVariant[] { new DungeonSurfaceVariant { material = fb, weight = 6, rows = DungeonVariantRows.Any } };
  }
  else
  {
    style.bottomWall = Mat(dir, "wall", name); style.upperWall = null; style.ceiling = Mat(dir, "ceiling", name); style.floor = Mat(dir, "floor", name); style.trim = Mat(dir, "trim", name);
    var wb = Mat(dir, "wall b", name); var fb = Mat(dir, "floor b", name);
    style.wallVariants = new DungeonSurfaceVariant[] { new DungeonSurfaceVariant { material = wb, weight = 6, rows = DungeonVariantRows.Any } };
    style.floorVariants = new DungeonSurfaceVariant[] { new DungeonSurfaceVariant { material = fb, weight = 6, rows = DungeonVariantRows.Any } };
  }
  string sp = $"{dir}/{name}.asset";
  var existing = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>(sp);
  if (existing != null) { UnityEditor.EditorUtility.CopySerialized(style, existing); existing.name = name; UnityEditor.EditorUtility.SetDirty(existing); UnityEngine.Object.DestroyImmediate(style); }
  else UnityEditor.AssetDatabase.CreateAsset(style, sp);
  log.Append($"{name}: made\n");
}
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/StyleRedraw/import.txt", log.ToString());
return log.ToString();

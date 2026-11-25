// The Pianist's icons (Characters/Classes/Pianist/Icons, drawn by Tools/pianist_icons.py) imported the way the sheet's
// ability icons are (the same settings as UI/Sprites/Meta/Sheet icons/Shield bash.png) and set on the passive and the
// class action: Crescendo.asset's icon, Mood.asset's icon (on) and iconOff. Safe to run again.
const string Dir = "Assets/Game/Characters/Classes/Pianist/Icons";
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
if (!UnityEditor.AssetDatabase.IsValidFolder(Dir)) return "no " + Dir + ": run python Tools/pianist_icons.py \"" + Dir + "\" first";
var like = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath("Assets/Game/UI/Sprites/Meta/Sheet icons/Shield bash.png");
System.Func<string, Sprite> sprite = name =>
{
    string path = Dir + "/" + name + ".png";
    var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
    if (ti == null) { sb.AppendLine("missing " + path); return null; }
    ti.textureType = UnityEditor.TextureImporterType.Sprite; ti.spriteImportMode = UnityEditor.SpriteImportMode.Single;
    ti.spritePixelsPerUnit = like.spritePixelsPerUnit; ti.filterMode = like.filterMode; ti.mipmapEnabled = like.mipmapEnabled;
    ti.alphaIsTransparency = true; ti.textureCompression = like.textureCompression; ti.npotScale = like.npotScale; ti.wrapMode = like.wrapMode;
    var settings = new UnityEditor.TextureImporterSettings(); ti.ReadTextureSettings(settings);
    settings.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(settings);
    ti.SaveAndReimport();
    return UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
};
var crescendo = sprite("Crescendo"); var on = sprite("Mood on"); var off = sprite("Mood off");
System.Action<string, string, Sprite> set = (asset, field, value) =>
{
    var target = UnityEditor.AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Game/Characters/Classes/Pianist/" + asset + ".asset");
    if (target == null) { sb.AppendLine("no " + asset + ".asset yet"); return; }
    var so = new UnityEditor.SerializedObject(target); var p = so.FindProperty(field);
    if (p == null) { sb.AppendLine(asset + " has no " + field); return; }
    p.objectReferenceValue = value; so.ApplyModifiedPropertiesWithoutUndo(); UnityEditor.EditorUtility.SetDirty(target);
    sb.AppendLine(asset + "." + field + " = " + (value != null ? value.name : "none"));
};
set("Crescendo", "icon", crescendo); set("Mood", "icon", on); set("Mood", "iconOff", off);
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();

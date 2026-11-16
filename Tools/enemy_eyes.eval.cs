// Eyes that glow in the dark (the night review, 2026-11-15): the creatures and skeletons of Dungeon1's four floors have
// coloured eyes painted in their textures, but nothing on them gave off light, so in a dark room they were dark shapes.
// Each gets an emission map: its own eye texels on black, beside its texture (<name> eyes.png, <name>_eyes.png for the
// Meshy creatures; cut from the texture by colour, the masks checked on the model), imported exactly as its texture is
// (same size, point filter), and its material's emission switched on. Run as an eval (safe to run again):
//   unity command eval_file --file Tools/enemy_eyes.eval.cs
// Strength is the emission colour: how much brighter than the painted eye it glows (the scene's bloom does the rest).
var log = new System.Text.StringBuilder();
(string material, string map, float strength)[] eyes =
{
    ("Assets/Game/Characters/Enemies/Crypt/Crypt Rat/Crypt Rat.mat", "Assets/Game/Art/Meshy/Crypt_Rat_E/Crypt_Rat_E_eyes.png", 2.2f),
    ("Assets/Game/Characters/Enemies/Crypt/Cave Bat/Cave Bat.mat", "Assets/Game/Art/Meshy/Cave_Bat_B/Cave_Bat_B_eyes.png", 2.2f),
    ("Assets/Game/Characters/Enemies/Crypt/Urn Crawler/Urn Crawler.mat", "Assets/Game/Art/Meshy/Urn_Crawler_B/Urn_Crawler_B_eyes.png", 1.6f),
    ("Assets/Game/Art/Synty Pixel/Characters/Blue Hood Skeleton/Blue Hood Skeleton.mat", "Assets/Game/Art/Synty Pixel/Characters/Blue Hood Skeleton/Blue Hood Skeleton eyes.png", 2.6f),
    ("Assets/Game/Art/Synty Pixel/Characters/Green Hood Skeleton/Green Hood Skeleton.mat", "Assets/Game/Art/Synty Pixel/Characters/Green Hood Skeleton/Green Hood Skeleton eyes.png", 3f),
    ("Assets/Game/Art/Synty Pixel/Characters/Crypt Lord (Sidekick)/Crypt Lord.mat", "Assets/Game/Art/Synty Pixel/Characters/Crypt Lord (Sidekick)/Crypt Lord eyes.png", 2.6f),
    // The other skeleton soldiers (their eye cells found on the mesh by place, grown over the eye's own colour).
    ("Assets/Game/Art/Synty Pixel/Characters/Red Hood Skeleton/Red Hood Skeleton.mat", "Assets/Game/Art/Synty Pixel/Characters/Red Hood Skeleton/Red Hood Skeleton eyes.png", 3.2f),
    ("Assets/Game/Art/Synty Pixel/Characters/Helmed Skeleton/Helmed Skeleton.mat", "Assets/Game/Art/Synty Pixel/Characters/Helmed Skeleton/Helmed Skeleton eyes.png", 3f),
    ("Assets/Game/Art/Synty Pixel/Characters/Horned Skeleton/Horned Skeleton.mat", "Assets/Game/Art/Synty Pixel/Characters/Horned Skeleton/Horned Skeleton eyes.png", 4f),
    // The older skeleton bodies have modelled sockets and no painted eyes: an ember painted into the socket's texels
    // (Tools/socket_eyes.eval.cs and socket_eyes.py), green for the archer, ice blue for the warden, orange for the soldier.
    ("Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Skeleton_Ranger_01/SM_Chr_Skeleton_Ranger_01 PolygonDarkFantasy_Mat_01_A.mat", "Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Skeleton_Ranger_01/SM_Chr_Skeleton_Ranger_01 PolygonDarkFantasy_Mat_01_A eyes.png", 2.2f),
    ("Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Skeleton_HeavyArmor_01/SM_Chr_Skeleton_HeavyArmor_01 PolygonDarkFantasy_Mat_01_A.mat", "Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Skeleton_HeavyArmor_01/SM_Chr_Skeleton_HeavyArmor_01 PolygonDarkFantasy_Mat_01_A eyes.png", 2.2f),
    ("Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Skeleton_LightArmor_01/SM_Chr_Skeleton_LightArmor_01 PolygonDarkFantasy_Mat_01_A.mat", "Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Skeleton_LightArmor_01/SM_Chr_Skeleton_LightArmor_01 PolygonDarkFantasy_Mat_01_A eyes.png", 2.2f),
};
foreach (var e in eyes)
{
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(e.material);
    if (mat == null) { log.AppendLine("no material " + e.material); continue; }
    var albedo = mat.GetTexture("_BaseMap");
    string albedoPath = albedo != null ? UnityEditor.AssetDatabase.GetAssetPath(albedo) : null;
    UnityEditor.AssetDatabase.ImportAsset(e.map, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
    var to = UnityEditor.AssetImporter.GetAtPath(e.map) as UnityEditor.TextureImporter;
    var from = albedoPath != null ? UnityEditor.AssetImporter.GetAtPath(albedoPath) as UnityEditor.TextureImporter : null;
    if (to == null) { log.AppendLine("no map " + e.map); continue; }
    if (from != null)
    {
        // As its texture is imported: the same size on the model, texel for texel.
        var s = new UnityEditor.TextureImporterSettings(); from.ReadTextureSettings(s); to.SetTextureSettings(s);
        to.maxTextureSize = from.maxTextureSize; to.npotScale = from.npotScale; to.filterMode = from.filterMode;
        to.mipmapEnabled = from.mipmapEnabled; to.textureCompression = from.textureCompression;
        to.SetPlatformTextureSettings(from.GetDefaultPlatformTextureSettings());
        to.sRGBTexture = true; to.alphaSource = UnityEditor.TextureImporterAlphaSource.None;
        to.SaveAndReimport();
    }
    var map = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(e.map);
    mat.EnableKeyword("_EMISSION");
    mat.SetTexture("_EmissionMap", map);
    mat.SetColor("_EmissionColor", Color.white * e.strength);
    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;   // as PixelRestyle sets a glowing material
    UnityEditor.EditorUtility.SetDirty(mat);
    log.AppendLine(mat.name + ": " + map.width + " x " + map.height + (albedo != null ? " (texture " + albedo.width + " x " + albedo.height + ")" : "") + " x" + e.strength);
}
UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();

// Dressed walls for the cellar floors (the night review, 2026-11-15: the generated rooms of floors 1-4 were bare boxes,
// nothing on their walls but torches). Run as an eval (safe to run again):
//   python Tools/wall_banner_recolour.py                       (the banner textures, once)
//   unity command eval_file --file Tools/wall_decor.eval.cs
// Makes colourings of the dungeon's two wall banners in Levels/Shared/Props/Wall decor (Wall banner and Torn banner,
// each red, green and black: variants of Dungeon1/Realms/Props/Full height banner and Dwarf banner torn with their own
// material, the same mesh and size, so the same 128 texels a metre), then gives the six Cellar styles their pieces and
// how many (wallDecor, wallDecorPerTenFaces): banners in the style's colour and the wall chains. The generator's
// DecorateWalls leaves out a piece that would cut into what stands at the wall, never sets one beside a doorway, and a
// torch is never put on a wall face a piece took.
const string Dir = "Assets/Game/Levels/Shared/Props/Wall decor";
const string Props = "Assets/Game/Levels/Dungeon1/Realms/Props/";
var log = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);

// One colouring of a banner: piece is the new name ("Wall banner"), source the banner it is a variant of.
GameObject Banner(string piece, string source, string colour)
{
    var sourcePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Props + source + ".prefab");
    var sourceMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(Props + source + "/" + source + ".mat");
    var sourceTex = UnityEditor.AssetImporter.GetAtPath(Props + source + "/" + source + ".png") as UnityEditor.TextureImporter;
    if (sourcePrefab == null || sourceMat == null || sourceTex == null) { log.AppendLine("no " + source + " to copy"); return null; }
    string name = piece + " " + colour, texPath = Dir + "/" + name + ".png";
    var to = UnityEditor.AssetImporter.GetAtPath(texPath) as UnityEditor.TextureImporter;
    if (to == null) { log.AppendLine("no texture " + texPath + " (run Tools/wall_banner_recolour.py)"); return null; }
    // Imported exactly as the banner's own texture: the same texels on the same mesh.
    var s = new UnityEditor.TextureImporterSettings(); sourceTex.ReadTextureSettings(s); to.SetTextureSettings(s);
    to.maxTextureSize = sourceTex.maxTextureSize; to.npotScale = sourceTex.npotScale; to.filterMode = sourceTex.filterMode;
    to.mipmapEnabled = sourceTex.mipmapEnabled; to.textureCompression = sourceTex.textureCompression;
    to.SetPlatformTextureSettings(sourceTex.GetDefaultPlatformTextureSettings());
    to.SaveAndReimport();
    string matPath = Dir + "/" + name + ".mat";
    var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
    if (mat == null) { mat = new Material(sourceMat); UnityEditor.AssetDatabase.CreateAsset(mat, matPath); }
    else { mat.shader = sourceMat.shader; mat.CopyPropertiesFromMaterial(sourceMat); }
    var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
    mat.SetTexture("_BaseMap", tex); if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
    UnityEditor.EditorUtility.SetDirty(mat);
    // A variant of the banner: its material differs, nothing on a wall stops a blow or a walker, and it is flat on its
    // wall (no shadow).
    var inst = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(sourcePrefab);
    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
    {
        var mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++) if (mats[i] == sourceMat) mats[i] = mat;
        r.sharedMaterials = mats;
    }
    foreach (var c in inst.GetComponentsInChildren<Collider>(true)) c.enabled = false;
    // Hung once, under the ceiling: the long banner's own mount is Full Height, which stacks a copy on every storey of
    // a taller room's wall (right for a pilaster; a banner hanging above a banner is not). Hangs From Ceiling: the
    // generator's wall pass lifts it by a taller room's extra height, so its rod is under the cornice there too.
    var mount = inst.GetComponent<PropMount>();
    if (mount == null) mount = inst.AddComponent<PropMount>();
    mount.kind = PropMount.Kind.HangsFromCeiling;
    mount.castsNoShadow = true;
    mount.onWall = true;   // flat on its wall: the generator opens the cornice and footing behind it
    var saved = UnityEditor.PrefabUtility.SaveAsPrefabAsset(inst, Dir + "/" + name + ".prefab");
    UnityEngine.Object.DestroyImmediate(inst);
    log.AppendLine("made " + name + " (" + tex.width + " x " + tex.height + ")");
    return saved;
}
var chains = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Props + "Full height chains.prefab");

void Dress(string style, float perTen, string colour, float chainWeight)
{
    var s = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonRoomStyle>("Assets/Game/Levels/Shared/Room styles/Cellar/" + style + "/" + style + ".asset");
    if (s == null) { log.AppendLine("no style " + style); return; }
    var list = new System.Collections.Generic.List<DungeonWeightedPrefab>();
    void Add(GameObject prefab, float weight) { if (prefab != null) list.Add(new DungeonWeightedPrefab { prefab = prefab, weight = weight }); }
    Add(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Wall banner " + colour + ".prefab"), 1.2f);
    Add(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Torn banner " + colour + ".prefab"), 1f);
    Add(chains, chainWeight);
    s.wallDecor = list.ToArray(); s.wallDecorPerTenFaces = perTen;
    UnityEditor.EditorUtility.SetDirty(s);
    log.AppendLine(style + ": " + list.Count + " pieces, " + perTen + " per ten wall faces");
}
foreach (var colour in new[] { "red", "green", "black" }) { Banner("Wall banner", "Full height banner", colour); Banner("Torn banner", "Dwarf banner torn", colour); }
// Wine red on the blue and green brick (Cellar1, Cellar4), mouldy green on the tan brick (Cellar3, Cellar6), black in
// the Crypt's red and yellow brick (Cellar2, Cellar5).
foreach (var style in new[] { "Cellar1", "Cellar4" }) Dress(style, 1.2f, "red", 1f);
foreach (var style in new[] { "Cellar3", "Cellar6" }) Dress(style, 1.2f, "green", 1.2f);
foreach (var style in new[] { "Cellar2", "Cellar5" }) Dress(style, 1.2f, "black", 1.2f);
UnityEditor.AssetDatabase.SaveAssets();
return log.ToString();

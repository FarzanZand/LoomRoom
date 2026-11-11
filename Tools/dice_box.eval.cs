// The bedroom's dice box and its key (models from Tools/dice_box.py and Tools/dice_box_key.py, the box's texture from
// Tools/dice_box_paint.py): sets the models' and textures' import settings, makes their materials (copies of a room
// furniture material and of a dungeon item's, so they are lit like their neighbours) and builds the two prefabs:
// World/Art/Dice box/Dice box.prefab (root pivot mid base, front along +z, children Body and Lid, Lid's pivot on the
// back hinge line) and Items/Prefabs/Keys/Dice box key.prefab. Safe to run again. Run after either Blender script.
const string BoxDir = "Assets/Game/World/Art/Dice box", KeyDir = "Assets/Game/Items/Prefabs/Keys/Dice box key";
const string RoomMat = "Assets/Game/World/Art/Bedroom/Furniture/Room wood walnut.mat", ItemMat = "Assets/Game/Art/Synty Pixel/Items/SM_Prop_Scroll_01/SM_Prop_Scroll_01.mat";
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);

System.Action<string> texture = path =>
{
    var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
    ti.textureType = UnityEditor.TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = UnityEditor.TextureImporterAlphaSource.None;
    ti.filterMode = FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
    ti.mipmapEnabled = true; ti.mipmapFilter = UnityEditor.TextureImporterMipFilter.BoxFilter; ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 1; ti.isReadable = false;
    ti.SaveAndReimport();
};
System.Action<string> model = path =>
{
    var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(path);
    mi.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.animationType = UnityEditor.ModelImporterAnimationType.None;
    mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.isReadable = true; mi.meshCompression = UnityEditor.ModelImporterMeshCompression.Off;
    mi.importNormals = UnityEditor.ModelImporterNormals.Import; mi.importTangents = UnityEditor.ModelImporterTangents.CalculateMikk; mi.weldVertices = true;
    mi.SaveAndReimport();
};
System.Func<string, string, Texture2D, Material> material = (path, from, tex) =>
{
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
    if (m == null) { UnityEditor.AssetDatabase.CopyAsset(from, path); m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path); }
    m.SetTexture("_BaseMap", tex); m.SetTexture("_MainTex", tex); m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
    m.SetTextureScale("_MainTex", Vector2.one); m.SetTextureOffset("_MainTex", Vector2.zero); m.SetColor("_BaseColor", Color.white);
    UnityEditor.EditorUtility.SetDirty(m); return m;
};
System.Func<string, string, Mesh> mesh = (path, name) =>
{
    foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh me && me.name == name) return me;
    throw new System.Exception("no mesh " + name + " in " + path);
};
System.Func<GameObject, string, Mesh, Material, Vector3, GameObject> part = (root, name, me, mat, at) =>
{
    var g = new GameObject(name); g.transform.SetParent(root.transform, false); g.transform.localPosition = at;
    g.AddComponent<MeshFilter>().sharedMesh = me; var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
    return g;
};

// ── Box ──
texture(BoxDir + "/Dice box.png"); model(BoxDir + "/Dice box.fbx");
var boxMat = material(BoxDir + "/Dice box.mat", RoomMat, UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(BoxDir + "/Dice box.png"));
var body = mesh(BoxDir + "/Dice box.fbx", "Body"); var lid = mesh(BoxDir + "/Dice box.fbx", "Lid");
Vector3 hinge = Vector3.zero;
foreach (var t in UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BoxDir + "/Dice box.fbx").GetComponentsInChildren<Transform>())
{
    sb.Append($"fbx {t.name} pos {t.localPosition.ToString("F4")} rot {t.localEulerAngles.ToString("F1")} scale {t.localScale.ToString("F3")}; ");
    if (t.name == "Lid") hinge = t.localPosition;
}
sb.Append($"\nBody bounds {body.bounds.min.ToString("F3")}..{body.bounds.max.ToString("F3")} tris {body.triangles.Length / 3}; Lid bounds {lid.bounds.min.ToString("F3")}..{lid.bounds.max.ToString("F3")} tris {lid.triangles.Length / 3}\n");
var box = new GameObject("Dice box");
try
{
    part(box, "Body", body, boxMat, Vector3.zero); part(box, "Lid", lid, boxMat, hinge);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(box, BoxDir + "/Dice box.prefab");
}
finally { UnityEngine.Object.DestroyImmediate(box); }

// ── Key ──
texture(KeyDir + "/Dice box key.png"); model(KeyDir + "/Dice box key.fbx");
var keyMat = material(KeyDir + "/Dice box key.mat", ItemMat, UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(KeyDir + "/Dice box key.png"));
var keyMesh = mesh(KeyDir + "/Dice box key.fbx", "Dice box key");
sb.Append($"Key bounds {keyMesh.bounds.min.ToString("F4")}..{keyMesh.bounds.max.ToString("F4")} tris {keyMesh.triangles.Length / 3}\n");
var key = new GameObject("Dice box key");
try
{
    part(key, "Model", keyMesh, keyMat, Vector3.zero);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(key, "Assets/Game/Items/Prefabs/Keys/Dice box key.prefab");
}
finally { UnityEngine.Object.DestroyImmediate(key); }
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();

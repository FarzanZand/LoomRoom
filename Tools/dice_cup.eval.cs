// The dice room's leather cup (model and texture from Tools/dice_cup.py): import settings, its material (a copy of the
// Synty cup's pixel material it replaces, so it is lit as the room's other small props are) and the prefab
// Levels/Shared/Props/Events/Dice room/Dice cup.prefab, then the cup in Levels/Shared/Rooms/Scripted/Dice room.prefab
// (an instance of that prefab carrying DiceCup and its trigger) stood upright on the gambler's table at its own size.
// Safe to run again. Run after the Blender script.
const string Dir = "Assets/Game/Levels/Shared/Props/Events/Dice room";
const string Room = "Assets/Game/Levels/Shared/Rooms/Scripted/Dice room.prefab";
const string From = "Assets/Game/Levels/Shared/Props/Events/Dice room/Pixel/SM_Item_Cup_01 x1.40/SM_Item_Cup_01 x1.40.mat";
var sb = new System.Text.StringBuilder();
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);

var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(Dir + "/Dice cup/Dice cup.png");
ti.textureType = UnityEditor.TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = UnityEditor.TextureImporterAlphaSource.None;
ti.filterMode = FilterMode.Point; ti.npotScale = UnityEditor.TextureImporterNPOTScale.None; ti.textureCompression = UnityEditor.TextureImporterCompression.Uncompressed;
ti.mipmapEnabled = true; ti.mipmapFilter = UnityEditor.TextureImporterMipFilter.BoxFilter; ti.wrapMode = TextureWrapMode.Clamp; ti.anisoLevel = 1; ti.isReadable = false;
ti.SaveAndReimport();
var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(Dir + "/Dice cup/Dice cup.fbx");
mi.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.animationType = UnityEditor.ModelImporterAnimationType.None;
mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.isReadable = true; mi.meshCompression = UnityEditor.ModelImporterMeshCompression.Off;
mi.importNormals = UnityEditor.ModelImporterNormals.Import; mi.importTangents = UnityEditor.ModelImporterTangents.CalculateMikk; mi.weldVertices = true;
mi.SaveAndReimport();

var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Dice cup/Dice cup.png");
var matPath = Dir + "/Dice cup/Dice cup.mat";
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
if (mat == null)
{
    // A copy of the Synty mug's pixel material, so the cup is lit as it was.
    UnityEditor.AssetDatabase.CopyAsset(From, matPath);
    mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(matPath);
}
mat.SetTexture("_BaseMap", tex); mat.SetTexture("_MainTex", tex); mat.SetColor("_BaseColor", Color.white);
UnityEditor.EditorUtility.SetDirty(mat);
Mesh mesh = null;
foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(Dir + "/Dice cup/Dice cup.fbx")) if (o is Mesh m && m.name == "Dice cup") mesh = m;
if (mesh == null) return "no mesh";
sb.Append($"mesh {mesh.bounds.min.ToString("F3")}..{mesh.bounds.max.ToString("F3")} tris {mesh.triangles.Length / 3}\n");

// The prefab: the same object (the room's instance keeps its overrides and components), now the leather cup at scale 1.
var prefab = UnityEditor.PrefabUtility.LoadPrefabContents(Dir + "/Dice cup.prefab");
try
{
    prefab.transform.localScale = Vector3.one;
    prefab.GetComponent<MeshFilter>().sharedMesh = mesh;
    prefab.GetComponent<MeshRenderer>().sharedMaterial = mat;
    if (prefab.TryGetComponent<MeshCollider>(out var mc)) mc.sharedMesh = mesh;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(prefab, Dir + "/Dice cup.prefab");
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(prefab); }

// In the room: upright (its mouth already down), standing on the table where the mug's rim stood.
var room = UnityEditor.PrefabUtility.LoadPrefabContents(Room);
try
{
    var cup = room.GetComponentInChildren<DiceCup>(true);
    var r = cup.GetComponent<MeshRenderer>();
    float top = 0.892f;   // the gambler's table's top (the mug stood 1 cm over it)
    var table = room.transform.Find("Small table");
    if (table != null) { var b = new Bounds(); bool any = false; foreach (var tr in table.GetComponentsInChildren<Renderer>()) { if (!any) { b = tr.bounds; any = true; } else b.Encapsulate(tr.bounds); } if (any) top = b.max.y; }
    cup.transform.localScale = Vector3.one;
    cup.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
    var p = cup.transform.localPosition; cup.transform.localPosition = new Vector3(p.x, top, p.z);
    cup.GetComponent<MeshFilter>().sharedMesh = mesh; r.sharedMaterial = mat;
    var dc = cup.GetComponent<DiceCup>(); if (dc != null) dc.outlineWidth = .015f;
    var col = cup.GetComponent<BoxCollider>();
    col.center = mesh.bounds.center; col.size = new Vector3(.6f, .6f, .6f);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(room, Room);
    sb.Append($"table top {top:F3}; cup at {cup.transform.position.ToString("F3")} bounds {r.bounds.min.ToString("F3")}..{r.bounds.max.ToString("F3")}\n");
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(room); }
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();

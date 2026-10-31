// The Great bell (Levels/Shared/Props/Events/Great bell.prefab): the root is the top of the ceiling plate; on it the
// Mount (plate, iron hangers, bearing blocks; Tools/great_bell_mount.py) and the Swing child on the headstock's axle
// AXLE below, holding the Headstock, the Bell (Synty SM_Prop_Bell_Large_01) hung 1 cm up into the beam's underside and
// the Clapper inside it. Every renderer is a pixel copy at the dungeon world's 128 texels a unit (PixelRestyle.Build,
// measured and baked again until within 5%), saved in Props/Events/Great bell/. The mount's meshes are baked into the
// prefab's frame first (the importer turns the FBX's objects; the turn, rounded to whole quarters, goes into the
// vertices). The events toll turns Swing about local x. Safe to run again (it rebuilds the bakes).
const float AXLE = .40f, BEAM_H = .24f, WorldTexels = 128f;
const bool ROPE_ONLY = true;    // true: only the pull rope
const string dir = "Assets/Game/Levels/Shared/Props/Events/Great bell";
const string prefabPath = "Assets/Game/Levels/Shared/Props/Events/Great bell.prefab";
var log = new System.Text.StringBuilder();

// materials for the mount's two parts (flat colours: the restyle reads them as oak and iron)
UnityEngine.Material Flat(string name, UnityEngine.Color c)
{
    string path = $"{dir}/{name} flat.mat";
    var m = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(path);
    if (m == null) { m = new UnityEngine.Material(UnityEngine.Shader.Find("Universal Render Pipeline/Lit")); UnityEditor.AssetDatabase.CreateAsset(m, path); }
    m.SetColor("_BaseColor", c); UnityEditor.EditorUtility.SetDirty(m);
    return m;
}
var oak = Flat("Oak", new UnityEngine.Color(.36f, .24f, .14f));
var iron = Flat("Iron", new UnityEngine.Color(.22f, .21f, .20f));

var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(dir + "/Great bell mount.fbx");
mi.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.None; mi.bakeAxisConversion = true; mi.isReadable = true; mi.importAnimation = false;
mi.SaveAndReimport();
UnityEngine.GameObject model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(dir + "/Great bell mount.fbx");

// A mesh of the FBX in the prefab's frame (Mount: the root's; Headstock: the axle's), submeshes by material name.
UnityEngine.Mesh Framed(string part, out UnityEngine.Material[] mats)
{
    // a one-object FBX is its own root (the rope); otherwise the part is a child of the root
    var mf = model.name == part || model.transform.Find(part) == null ? model.GetComponent<UnityEngine.MeshFilter>() : model.transform.Find(part).GetComponent<UnityEngine.MeshFilter>();
    var r = mf.transform.rotation.eulerAngles;
    var turn = UnityEngine.Quaternion.Euler(UnityEngine.Mathf.Round(r.x / 90f) * 90f, UnityEngine.Mathf.Round(r.y / 90f) * 90f, UnityEngine.Mathf.Round(r.z / 90f) * 90f);
    var at = mf.transform.position;
    var src = mf.sharedMesh;
    var mesh = UnityEngine.Object.Instantiate(src); mesh.name = part;
    var v = src.vertices; var n = src.normals;
    for (int i = 0; i < v.Length; i++) { v[i] = at + turn * v[i]; n[i] = turn * n[i]; }
    mesh.vertices = v; mesh.normals = n; mesh.RecalculateBounds(); mesh.RecalculateTangents();
    // submeshes come in the order Blender joined the parts' materials: Oak first in both objects, then Iron
    mats = new UnityEngine.Material[mesh.subMeshCount];
    for (int i = 0; i < mats.Length; i++) mats[i] = i == 0 ? oak : iron;
    return mesh;
}

// Pixel copy at 128 texels a world unit: baked, measured on a stand-in renderer, baked again aimed by what came out.
(UnityEngine.Mesh, UnityEngine.Material) Pixel(UnityEngine.Mesh src, UnityEngine.Material[] mats, string name)
{
    var probe = new UnityEngine.GameObject("texel probe") { hideFlags = UnityEngine.HideFlags.HideAndDontSave };
    var pf = probe.AddComponent<UnityEngine.MeshFilter>(); var pr = probe.AddComponent<UnityEngine.MeshRenderer>();
    float aim = WorldTexels; UnityEngine.Mesh mesh = null; UnityEngine.Material mat = null;
    try
    {
        for (int pass = 0; pass < 4; pass++)
        {
            (mesh, mat) = PixelRestyle.Build(src, mats, "Props", name, aim, dir + "/" + name);
            if (mesh == null) break;
            UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
            pf.sharedMesh = mesh; pr.sharedMaterial = mat;
            float now = PixelRestyle.TexelsPerUnit(pr);
            log.Append($"{name} {now:0} ");
            if (now <= 0f || UnityEngine.Mathf.Abs(now / WorldTexels - 1f) < .05f) break;
            aim *= WorldTexels / now;
        }
    }
    finally { UnityEngine.Object.DestroyImmediate(probe); }
    return (mesh, mat);
}

if (!ROPE_ONLY)
{
var mountMesh = Framed("Mount", out var mountMats);
var headMesh = Framed("Headstock", out var headMats);
// Headstock: the FBX holds it round the origin already (the axle); nothing to move.
var (mountPx, mountMat) = Pixel(mountMesh, mountMats, "Bell mount");
var (headPx, headMat) = Pixel(headMesh, headMats, "Bell headstock");

var synty = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Synty/PolygonDarkFantasy/Prefabs/Props/SM_Prop_Bell_Large_01.prefab");
UnityEngine.MeshFilter bellSrc = null, clapperSrc = null;
foreach (var f in synty.GetComponentsInChildren<UnityEngine.MeshFilter>(true)) if (f.sharedMesh.name.Contains("Clapper")) clapperSrc = f; else bellSrc = f;
var (bellPx, bellMat) = Pixel(bellSrc.sharedMesh, bellSrc.GetComponent<UnityEngine.MeshRenderer>().sharedMaterials, "Bell");
var (clapperPx, clapperMat) = Pixel(clapperSrc.sharedMesh, clapperSrc.GetComponent<UnityEngine.MeshRenderer>().sharedMaterials, "Bell clapper");

var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
try
{
    UnityEngine.Transform Child(UnityEngine.Transform parent, string name, params string[] oldNames)
    {
        var t = parent.Find(name);
        if (t == null) foreach (var o in oldNames) { t = root.transform.Find(o); if (t == null) foreach (var c in root.GetComponentsInChildren<UnityEngine.Transform>(true)) if (c.name == o) t = c; if (t != null) break; }
        if (t == null) t = new UnityEngine.GameObject(name).transform;
        t.name = name; t.SetParent(parent, false);
        return t;
    }
    void Show(UnityEngine.Transform t, UnityEngine.Mesh mesh, UnityEngine.Material mat)
    {
        var f = t.GetComponent<UnityEngine.MeshFilter>(); if (f == null) f = t.gameObject.AddComponent<UnityEngine.MeshFilter>();
        var r = t.GetComponent<UnityEngine.MeshRenderer>(); if (r == null) r = t.gameObject.AddComponent<UnityEngine.MeshRenderer>();
        f.sharedMesh = mesh; r.sharedMaterials = new[] { mat };
    }
    var mount = Child(root.transform, "Mount"); mount.localPosition = UnityEngine.Vector3.zero; mount.localRotation = UnityEngine.Quaternion.identity; mount.localScale = UnityEngine.Vector3.one;
    Show(mount, mountPx, mountMat);
    var swing = Child(root.transform, "Swing"); swing.localPosition = new UnityEngine.Vector3(0, -AXLE, 0); swing.localRotation = UnityEngine.Quaternion.identity; swing.localScale = UnityEngine.Vector3.one;
    var head = Child(swing, "Headstock"); head.localPosition = UnityEngine.Vector3.zero; head.localRotation = UnityEngine.Quaternion.identity; head.localScale = UnityEngine.Vector3.one;
    Show(head, headPx, headMat);
    // the bell's top 1 cm up into the beam's underside; centred under the axle (the Synty mesh sits 1.2 cm off in z)
    var bellTop = -BEAM_H / 2f + .01f;
    var bell = Child(swing, "Bell"); bell.localPosition = new UnityEngine.Vector3(.001f, bellTop, -.012f); bell.localRotation = UnityEngine.Quaternion.identity; bell.localScale = UnityEngine.Vector3.one;
    Show(bell, bellPx, bellMat);
    var clapper = Child(swing, "Clapper", "SM_Prop_Bell_Large_01_Clapper_01"); clapper.localPosition = new UnityEngine.Vector3(0, bellTop - .5902f, 0); clapper.localRotation = UnityEngine.Quaternion.identity; clapper.localScale = UnityEngine.Vector3.one;
    Show(clapper, clapperPx, clapperMat);
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}
// The pull rope (Tools/bell_rope.py), its own prefab with the pivot at its top: Props/Events/Bell rope.prefab.
var ri = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(dir + "/Bell rope.fbx");
ri.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.None; ri.bakeAxisConversion = true; ri.isReadable = true; ri.importAnimation = false;
ri.SaveAndReimport();
model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(dir + "/Bell rope.fbx");
var ropeMesh = Framed("Bell rope", out var _);
var ropeMats = new[] { Flat("Hemp", new UnityEngine.Color(.52f, .42f, .28f)), Flat("Wool red", new UnityEngine.Color(.46f, .13f, .10f)), Flat("Wool cream", new UnityEngine.Color(.74f, .67f, .54f)) };
var (ropePx, ropeMat) = Pixel(ropeMesh, ropeMats, "Bell rope");
const string ropePath = "Assets/Game/Levels/Shared/Props/Events/Bell rope.prefab";
bool ropeExists = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(ropePath) != null;
var ropeRoot = ropeExists ? UnityEditor.PrefabUtility.LoadPrefabContents(ropePath) : new UnityEngine.GameObject("Bell rope");
try
{
    var f = ropeRoot.GetComponent<UnityEngine.MeshFilter>(); if (f == null) f = ropeRoot.AddComponent<UnityEngine.MeshFilter>();
    var r = ropeRoot.GetComponent<UnityEngine.MeshRenderer>(); if (r == null) r = ropeRoot.AddComponent<UnityEngine.MeshRenderer>();
    f.sharedMesh = ropePx; r.sharedMaterials = new[] { ropeMat };
    var cap = ropeRoot.GetComponent<UnityEngine.CapsuleCollider>(); if (cap == null) cap = ropeRoot.AddComponent<UnityEngine.CapsuleCollider>();
    var b = ropePx.bounds; cap.direction = 1; cap.center = b.center; cap.height = b.size.y; cap.radius = .06f;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(ropeRoot, ropePath);
}
finally { if (ropeExists) UnityEditor.PrefabUtility.UnloadPrefabContents(ropeRoot); else UnityEngine.Object.DestroyImmediate(ropeRoot); }
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/Events/bell.txt", log.ToString());
return log.ToString();

// The Dungeon Master's die (Tools/dm_die.py model, Tools/dm_die_texture.py texture): bakes the imported model into
// DM die.asset in the prefab's own frame (the importer turns the FBX's root by a quarter turn; that turn, rounded to a
// whole quarter, is applied to the vertices), then puts mesh and material on the Body child of Levels/Shared/Props/Events/DM die.prefab
// and removes the stand-in's pips. The prefab's other components (collider, scripts) are kept. Safe to run again.
const string dir = "Assets/Game/Levels/Shared/Props/Events/DM die";
const string prefabPath = "Assets/Game/Levels/Shared/Props/Events/DM die.prefab";
var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(dir + "/DM die.fbx");
var mf = model.GetComponentInChildren<UnityEngine.MeshFilter>();
var e = mf.transform.rotation.eulerAngles;
var turn = UnityEngine.Quaternion.Euler(UnityEngine.Mathf.Round(e.x / 90f) * 90f, UnityEngine.Mathf.Round(e.y / 90f) * 90f, UnityEngine.Mathf.Round(e.z / 90f) * 90f);
var src = mf.sharedMesh;
var mesh = UnityEngine.Object.Instantiate(src); mesh.name = "DM die";
var v = src.vertices; var n = src.normals;
for (int i = 0; i < v.Length; i++) { v[i] = turn * v[i]; n[i] = turn * n[i]; }
mesh.vertices = v; mesh.normals = n; mesh.RecalculateBounds(); mesh.RecalculateTangents();
var meshPath = dir + "/DM die.asset";
var old = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(meshPath);
// Made anew each time (copied into the old asset, the editor kept drawing the old mesh); the prefab is pointed at it below.
if (old != null) UnityEditor.AssetDatabase.DeleteAsset(meshPath);
UnityEditor.AssetDatabase.CreateAsset(mesh, meshPath);
var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Material>(dir + "/DM die.mat");
var root = UnityEditor.PrefabUtility.LoadPrefabContents(prefabPath);
try
{
    for (int i = root.transform.childCount - 1; i >= 0; i--)
        if (root.transform.GetChild(i).name == "Pip") UnityEngine.Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
    // The model goes on the Body child, unscaled (the stand-in's cube was a unit cube scaled to .6).
    var body = root.transform.Find("Body");
    if (body == null) { body = new UnityEngine.GameObject("Body").transform; body.SetParent(root.transform, false); }
    body.localPosition = UnityEngine.Vector3.zero; body.localRotation = UnityEngine.Quaternion.identity; body.localScale = UnityEngine.Vector3.one;
    var filter = body.GetComponent<UnityEngine.MeshFilter>(); if (filter == null) filter = body.gameObject.AddComponent<UnityEngine.MeshFilter>();
    var renderer = body.GetComponent<UnityEngine.MeshRenderer>(); if (renderer == null) renderer = body.gameObject.AddComponent<UnityEngine.MeshRenderer>();
    filter.sharedMesh = mesh; renderer.sharedMaterials = new[] { mat };
    var box = root.GetComponent<UnityEngine.BoxCollider>();
    if (box != null) { box.size = mesh.bounds.size; box.center = UnityEngine.Vector3.zero; }
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
UnityEditor.AssetDatabase.SaveAssets();
return $"die: turn {turn.eulerAngles}, {mesh.vertexCount} vertices, bounds {mesh.bounds.size}";

// The sleeping giant's own mace (the user, 2026-11-13): it carries the Warden's Mace, whose painted copy is made for a
// man's hand; on the giant (1.7 times a man) that was 75 texels a metre. This bakes the mace again for the size the giant
// holds it at, from the Warden's own painted mesh and material (PixelRestyle.Build, as the Arena's figures were matched),
// measured in the giant's hand and baked again until within 2% of 128, into Characters/Enemies/Events/Pixel/Giant mace;
// makes Characters/Enemies/Events/Giant mace.prefab (a variant of the Warden's Mace's world prefab with that mesh and
// material) and sets it as the giant's weapon model (EnemyWeaponLoadout.weaponModel). The weapon stays the Warden's Mace
// item: its numbers are untouched. Run in edit mode; safe to run again.
if (UnityEngine.Application.isPlaying) return "playing";
const float TPU = 128f;
const string Giant = "Assets/Game/Characters/Enemies/Events/Sleeping giant.prefab";
const string Variant = "Assets/Game/Characters/Enemies/Events/Giant mace.prefab";
const string Dir = "Assets/Game/Characters/Enemies/Events/Pixel/Giant mace";
var sb = new System.Text.StringBuilder();
var giant = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(Giant);
var loadout = giant.GetComponent<EnemyWeaponLoadout>();
if (loadout == null || loadout.weapon == null || loadout.weapon.worldPrefab == null) return "the giant has no weapon";
var own = loadout.weapon.worldPrefab;
var ownRenderer = own.GetComponentInChildren<UnityEngine.MeshRenderer>();
var srcMesh = ownRenderer.GetComponent<UnityEngine.MeshFilter>().sharedMesh; var srcMats = ownRenderer.sharedMaterials;
sb.Append($"weapon {loadout.weapon.itemName}, model {own.name}, mesh {srcMesh.name}, texture {srcMats[0].GetTexture("_BaseMap").width}\n");

// The giant in the scene with the mace in its hand, to measure it where it is held.
var inst = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(giant);
UnityEngine.Mesh mesh = null; UnityEngine.Material mat = null;
try
{
    var l = inst.GetComponent<EnemyWeaponLoadout>(); l.weaponModel = null; l.RefreshWeapon();
    var held = l.Visual != null ? l.Visual.GetComponentInChildren<UnityEngine.MeshRenderer>() : null;
    if (held == null) return "the mace is not in the giant's hand";
    var mf = held.GetComponent<UnityEngine.MeshFilter>();
    float before = PixelRestyle.TexelsPerUnit(held), aim = TPU * held.transform.lossyScale.y, measured = before;
    for (int pass = 0; pass < 5; pass++)
    {
        (mesh, mat) = PixelRestyle.Build(srcMesh, srcMats, "Props", "Giant mace", aim, Dir);
        if (mesh == null) return "bake failed";
        UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
        mf.sharedMesh = mesh; held.sharedMaterials = new[] { mat };
        measured = PixelRestyle.TexelsPerUnit(held);
        sb.Append($"  pass {pass}: aim {aim:F1} measured {measured:F1}\n");
        if (measured <= 0f || UnityEngine.Mathf.Abs(measured / TPU - 1f) < .02f) break;
        aim *= TPU / measured;
    }
    var tex = mat.GetTexture("_BaseMap");
    sb.Append($"mace in the giant's hand: {before:F1} -> {measured:F1} texels a metre (scale {held.transform.lossyScale.y:0.###}, texture {tex.width} x {tex.height}, {mesh.triangles.Length / 3} triangles)\n");
}
finally { UnityEngine.Object.DestroyImmediate(inst); }

// The giant's mace: the Warden's Mace's own world prefab (its size, its grip) wearing the bake.
var copy = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(own);
try
{
    var r = copy.GetComponentInChildren<UnityEngine.MeshRenderer>();
    r.GetComponent<UnityEngine.MeshFilter>().sharedMesh = mesh; r.sharedMaterials = new[] { mat };
    var mc = copy.GetComponentInChildren<UnityEngine.MeshCollider>(); if (mc != null) mc.sharedMesh = mesh;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(copy, Variant);
}
finally { UnityEngine.Object.DestroyImmediate(copy); }
var variant = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(Variant);

var root = UnityEditor.PrefabUtility.LoadPrefabContents(Giant);
try { root.GetComponent<EnemyWeaponLoadout>().weaponModel = variant; UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, Giant); }
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
UnityEditor.AssetDatabase.SaveAssets();
sb.Append("giant's weapon model: " + variant.name + "\n");
System.IO.Directory.CreateDirectory("Temp/Realms"); System.IO.File.WriteAllText("Temp/Realms/giant_mace_log.txt", sb.ToString());
return sb.ToString();

// Bodies for the events' enemies (Characters/Enemies/Events), each a pixel copy baked for the size it stands at (128
// texels a world unit, measured on the prefab and baked again until within 5%), in Characters/Enemies/Events/Pixel:
// - Duellist: the PolygonDungeon hero knight (plate, red plume) on its Crypt Warden rig, matched to the bones by name
//   as the Arena Champion was; bleeds; no bone burst.
// - Sleeping giant: SOURCE_GIANT on the same rig at the prefab's 1.7 scale; bleeds; no bone burst.
// - Card sharp: the PolygonDarkFantasy gravedigger's hat where its light armour helmet sat (the helmet switched off).
// - Card sharp: its skeleton body baked again for its .83 scale.
// The rig's other bodies and helmets are switched off, not removed (they belong to the base). Safe to run again: the
// body made last time is replaced. Pass WHICH below to do one only.
const float WorldTexels = 128f;
const string WHICH = "all";
const float HatTilt = 18f;   // degrees the card sharp's hat is pushed back
const string SOURCE_GIANT = "Assets/Synty/PolygonDungeon/Prefabs/Characters/SM_Chr_Goblin_WarChief_01.prefab";
const string Pixel = "Assets/Game/Characters/Enemies/Events/Pixel";
var log = new System.Text.StringBuilder();
if (!UnityEditor.AssetDatabase.IsValidFolder(Pixel)) UnityEditor.AssetDatabase.CreateFolder("Assets/Game/Characters/Enemies/Events", "Pixel");

// Bakes a renderer's source mesh and materials into a pixel copy at 128 texels a world unit where it stands.
void Bake(UnityEngine.Renderer r, UnityEngine.Mesh src, UnityEngine.Material[] mats, string name)
{
    var skin = r as UnityEngine.SkinnedMeshRenderer; var mf = r.GetComponent<UnityEngine.MeshFilter>();
    // texels a mesh unit: the world's 128 times the size the renderer stands at; then corrected by what is measured
    float aim = WorldTexels * r.transform.lossyScale.x;
    string folder = $"{Pixel}/{name}";
    for (int pass = 0; pass < 5; pass++)
    {
        var (mesh, mat) = PixelRestyle.Build(src, mats, "Characters", name, aim, folder);
        if (mesh == null) { log.Append($"{name}: bake failed; "); return; }
        UnityEditor.AssetDatabase.ImportAsset(UnityEditor.AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")), UnityEditor.ImportAssetOptions.ForceUpdate);
        if (skin != null) skin.sharedMesh = mesh; else mf.sharedMesh = mesh;
        r.sharedMaterials = new[] { mat };
        float now = PixelRestyle.TexelsPerUnit(r);
        log.Append($"{name} pass {pass}: {now:0}; ");
        if (now <= 0f || UnityEngine.Mathf.Abs(now / WorldTexels - 1f) < .05f) return;
        aim *= WorldTexels / now;
    }
}

// The active skinned body of a Synty character prefab (the PolygonDungeon prefabs hold every body, one switched on).
UnityEngine.SkinnedMeshRenderer SourceBody(string path)
{
    var g = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
    foreach (var s in g.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) if (s.gameObject.activeSelf) return s;
    return null;
}

void NewBody(string enemyPath, string sourcePath, string bodyName, bool blood)
{
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(enemyPath);
    try
    {
        var model = root.transform.Find("Model");
        foreach (var smr in model.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) if (smr.name != bodyName) smr.gameObject.SetActive(false);
        foreach (var mr in model.GetComponentsInChildren<UnityEngine.MeshRenderer>(true)) if (mr.name.Contains("Helmet")) mr.gameObject.SetActive(false);
        var old = model.Find(bodyName); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
        var source = SourceBody(sourcePath);
        var bones = new System.Collections.Generic.Dictionary<string, UnityEngine.Transform>();
        foreach (var t in model.GetComponentsInChildren<UnityEngine.Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;
        var body = new UnityEngine.GameObject(bodyName);
        body.transform.SetParent(model, false);
        var skin = body.AddComponent<UnityEngine.SkinnedMeshRenderer>();
        var mapped = new UnityEngine.Transform[source.bones.Length]; int missing = 0;
        for (int i = 0; i < mapped.Length; i++) if (source.bones[i] == null || !bones.TryGetValue(source.bones[i].name, out mapped[i])) missing++;
        skin.bones = mapped;
        skin.rootBone = bones.TryGetValue(source.rootBone != null ? source.rootBone.name : "Hips", out var rb) ? rb : bones["Hips"];
        skin.localBounds = source.localBounds; skin.updateWhenOffscreen = false;
        if (missing > 0) log.Append($"{bodyName}: {missing} bones not found; ");
        Bake(skin, source.sharedMesh, source.sharedMaterials, bodyName);
        // culling bounds from the body as it stands, with room for swings and the death throw
        var baked = new UnityEngine.Mesh(); skin.BakeMesh(baked, true);
        var toRoot = skin.rootBone.worldToLocalMatrix * skin.transform.localToWorldMatrix;
        var bv = baked.vertices; var bb = new UnityEngine.Bounds(toRoot.MultiplyPoint3x4(bv[0]), UnityEngine.Vector3.zero);
        foreach (var p in bv) bb.Encapsulate(toRoot.MultiplyPoint3x4(p));
        UnityEngine.Object.DestroyImmediate(baked);
        bb.Expand(bb.size.magnitude * .25f); skin.localBounds = bb;
        // a person's body: bleeds and is thrown, never bursts into a skeleton's bones
        foreach (var burst in root.GetComponentsInChildren<DeathBurst>(true)) UnityEngine.Object.DestroyImmediate(burst);
        var data = root.GetComponent<Character>().data;
        if (blood && data != null)
        {
            data.gore ??= new GoreProfile();
            data.gore.kind = BloodKind.Blood; data.gore.color = new UnityEngine.Color(.72f, .05f, .04f); data.gore.burstOnDeath = false;
            UnityEditor.EditorUtility.SetDirty(data);
        }
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, enemyPath);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}

void Hat(string enemyPath, string hatPath, string hatName)
{
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(enemyPath);
    try
    {
        var model = root.transform.Find("Model");
        // the hat sits where the rig's own PolygonDarkFantasy helmet sits (same pack, same attachment convention:
        // modelled round its pivot, hung under the head with the helmet's own offset and turn)
        UnityEngine.Transform helmet = null;
        foreach (var mr in model.GetComponentsInChildren<UnityEngine.MeshRenderer>(true))
            if (mr.name.Contains("Helmet")) { if (helmet == null || mr.name.Contains("LightArmor")) helmet = mr.transform; mr.gameObject.SetActive(false); }
        var anchor = helmet.parent;
        foreach (var t in model.GetComponentsInChildren<UnityEngine.Transform>(true)) if (t != null && t.name == hatName) UnityEngine.Object.DestroyImmediate(t.gameObject);
        var src = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(hatPath);
        var hat = (UnityEngine.GameObject)UnityEngine.Object.Instantiate(src, anchor, false);
        hat.name = hatName;
        hat.transform.localPosition = helmet.localPosition; hat.transform.localRotation = helmet.localRotation; hat.transform.localScale = helmet.localScale;
        // pushed back on the skull: seen from a standing player across the table, a level brim hid his whole face
        hat.transform.RotateAround(anchor.position, root.transform.right, -HatTilt);
        foreach (var c in hat.GetComponentsInChildren<UnityEngine.Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        log.Append($"hat under {anchor.name} at {helmet.localPosition}; ");
        foreach (var f in hat.GetComponentsInChildren<UnityEngine.MeshFilter>(true))
        {
            var r = f.GetComponent<UnityEngine.MeshRenderer>();
            Bake(r, f.sharedMesh, r.sharedMaterials, hatName);
        }
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, enemyPath);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}

// A body the rig already wears, baked again for the size its enemy stands at (the card sharp's skeleton at .83).
void Rebake(string enemyPath, string rendererName, string name)
{
    var root = UnityEditor.PrefabUtility.LoadPrefabContents(enemyPath);
    try
    {
        foreach (var r in root.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
            if (r.name == rendererName)
            {
                // from the Synty original (named in the bake), never from an earlier bake
                var src = r.sharedMesh; var mats = r.sharedMaterials;
                if (src.name.Contains("(Synty Pixel)") || UnityEditor.AssetDatabase.GetAssetPath(src).StartsWith(Pixel))
                    foreach (var s in UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/Synty/PolygonDarkFantasy/Prefabs/Characters/" + rendererName + ".prefab").GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
                    { src = s.sharedMesh; mats = s.sharedMaterials; }
                Bake(r, src, mats, name);
            }
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, enemyPath);
    }
    finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
}

if (WHICH == "all" || WHICH == "duellist") NewBody("Assets/Game/Characters/Enemies/Events/Duellist.prefab", "Assets/Synty/PolygonDungeon/Prefabs/Characters/SM_Chr_Hero_Knight_Male_01.prefab", "Duellist body", true);
if (WHICH == "all" || WHICH == "giant") NewBody("Assets/Game/Characters/Enemies/Events/Sleeping giant.prefab", SOURCE_GIANT, "Giant body", true);
if (WHICH == "all" || WHICH == "hat") Hat("Assets/Game/Characters/Enemies/Events/Card sharp.prefab", "Assets/Synty/PolygonDarkFantasy/Prefabs/Characters/Attachments/SM_Chr_Attach_Gravedigger_Hat_01.prefab", "Card sharp hat");
if (WHICH == "all" || WHICH == "sharpbody") Rebake("Assets/Game/Characters/Enemies/Events/Card sharp.prefab", "SM_Chr_Skeleton_LightArmor_01", "Card sharp body");
UnityEditor.AssetDatabase.SaveAssets();
System.IO.File.WriteAllText("Temp/Events/bodies.txt", log.ToString());
return log.ToString();

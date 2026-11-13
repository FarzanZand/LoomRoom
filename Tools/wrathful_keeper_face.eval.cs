// The Wrathful merchant keeps the tavern keeper's head (the user, 2026-11-13: "when you attack the tavern keep and he
// gets aggressive, the model changes. He goes from minotaur to human"). The keeper wears one of KeeperFace's heads on a
// body with the Synty head cut away; the enemy he turns into (Characters/Enemies/Crypt/Wrathful merchant) wore the whole
// Synty body, human head and all. This gives the Wrathful merchant a KeeperFace of its own: a copy of each of the
// keeper's heads under its own head bone, placed as on the keeper (both wear the same merchant mesh on the same bones,
// so a head sits the same under the head bone), its Merchant body as the body, and the same whole and headless meshes.
// ProvokableNpc hands the keeper's choice over when he turns. Safe to run again: the heads are replaced.
const string Keeper = "Assets/Game/Characters/NPCs/Dungeon merchant.prefab";
const string Wrath = "Assets/Game/Characters/Enemies/Crypt/Wrathful merchant.prefab";
var sb = new System.Text.StringBuilder();
var keeper = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Keeper);
var kf = keeper != null ? keeper.GetComponentInChildren<KeeperFace>(true) : null;
if (kf == null) return "the keeper has no KeeperFace";
Transform Head(GameObject root)
{
    foreach (var a in root.GetComponentsInChildren<Animator>(true)) if (a.isHuman && a.GetBoneTransform(HumanBodyBones.Head) != null) return a.GetBoneTransform(HumanBodyBones.Head);
    foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") return t;
    return null;
}

var root = UnityEditor.PrefabUtility.LoadPrefabContents(Wrath);
try
{
    SkinnedMeshRenderer body = null;
    foreach (var s in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.name == "Merchant body") body = s;
    if (body == null) return "the Wrathful merchant has no Merchant body";
    // The head bone its body is skinned to (the rig may have other bodies' bones of the same name).
    Transform head = null; foreach (var b in body.bones) if (b != null && b.name == "Head") head = b;
    if (head == null) head = Head(root);
    if (head == null) return "no head bone";
    var keeperHead = kf.faces.Length > 0 && kf.faces[0].face != null ? kf.faces[0].face.transform.parent : null;
    sb.Append($"keeper heads under '{(keeperHead != null ? keeperHead.name : "?")}' (scale {(keeperHead != null ? keeperHead.lossyScale.y : 0):0.###}); wrath head bone '{head.name}' (scale {head.lossyScale.y:0.###}), body mesh {body.sharedMesh.name}, same as the keeper's whole body {body.sharedMesh == kf.wholeBody}\n");

    var wf = root.GetComponent<KeeperFace>(); if (wf == null) wf = root.AddComponent<KeeperFace>();
    if (wf.faces != null) foreach (var e in wf.faces) if (e != null && e.face != null) UnityEngine.Object.DestroyImmediate(e.face);
    var entries = new System.Collections.Generic.List<KeeperFace.Entry>();
    foreach (var e in kf.faces)
    {
        if (e == null || e.face == null) continue;
        var copy = UnityEngine.Object.Instantiate(e.face, head, false);   // the same place under the head bone
        copy.name = e.face.name;
        copy.transform.localPosition = e.face.transform.localPosition; copy.transform.localRotation = e.face.transform.localRotation; copy.transform.localScale = e.face.transform.localScale;
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = body.gameObject.layer;
        foreach (var r in copy.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = body.renderingLayerMask;
        copy.SetActive(e.kind == kf.face);
        entries.Add(new KeeperFace.Entry { kind = e.kind, face = copy });
        sb.Append($"  {e.kind}: {copy.name}\n");
    }
    wf.faces = entries.ToArray(); wf.face = kf.face; wf.body = body; wf.wholeBody = kf.wholeBody; wf.headlessBody = kf.headlessBody;
    body.sharedMesh = kf.face == KeeperFaceKind.Human ? kf.wholeBody : kf.headlessBody;
    UnityEditor.PrefabUtility.SaveAsPrefabAsset(root, Wrath);
    sb.Append($"Wrathful merchant: KeeperFace with {entries.Count} heads, wearing {wf.face}\n");
}
finally { UnityEditor.PrefabUtility.UnloadPrefabContents(root); }
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();

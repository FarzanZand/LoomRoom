using UnityEditor;
using UnityEngine;

// R&D prototype (one body, true first person), branch onebody only: turns TablePlayer.prefab into one body.
// - The first-person rig (TableArmsMesh: the full Synty skeleton, the FirstPersonTable controller, the hand sockets
//   with their held items) moves out from under the camera to Body/BodyRig at the player's feet, unsquashed.
// - Its arms-only cut is deleted and the full class body (Hunter, pixel restyle) switched on.
// - Where it hung, a copy of its skeleton (TableArmsMesh, no animator, no meshes) stays for the owner's camera: OneBodyRig
//   poses it from the body and draws an arms-only cut of the body's own mesh on it. Held items stay on its hands.
// - BodyVisuals (the separate knight with its own controller) is deleted; every reference to it points at the one
//   animator.
// - The body is drawn and lit like any character; the first-person arms keep the ViewModel layer and the squash.
// - OneBodyRig keeps the body under the player, turns its upper body with the look and hides its head from the
//   owner's own camera only.
// Safe to run again: it only acts on what is still there.
public static class OneBodyBuilder
{
    const string TablePlayer = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
    const string HunterBody = "Assets/Game/Art/Synty Pixel/Characters/SM_Chr_Hunter_Male_01 (Characters)/SM_Chr_Hunter_Male_01 PolygonDarkFantasy_Mat_01_A";
    // How far above the feet the rig's root sits so its eye meets the camera as it did under the camera (the rig hung
    // 1.5 below the eye, and the eye is 1.52 above the feet).
    const float RootHeight = .02f;

    // The prefab and the table player in the open scene (Room.unity's is not a prefab instance).
    // A copy of a hierarchy's bones as plain transforms (mesh objects and what the hands hold left out).
    static Transform Bones(Transform source, Transform parent)
    {
        var t = new GameObject(source.name).transform;
        t.SetParent(parent, false);
        t.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        t.localScale = source.localScale;
        if (source.name.EndsWith("Hand_Socket")) return t;
        foreach (Transform child in source)
            if (child.GetComponent<SkinnedMeshRenderer>() == null) Bones(child, t);
        return t;
    }

    [MenuItem("Tools/LoomRoom/R&D/One Body/Build One-Body Table Player")]
    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        var prefab = PrefabUtility.LoadPrefabContents(TablePlayer);
        try
        {
            log.AppendLine("Prefab:");
            BuildOn(prefab, log);
            PrefabUtility.SaveAsPrefabAsset(prefab, TablePlayer);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var player in Object.FindObjectsByType<Player>(FindObjectsInactive.Include))
        {
            if (player.kind != PlayerKind.Table || PrefabUtility.IsPartOfPrefabAsset(player) || player.transform.parent == null) continue;
            var root = player.transform.parent.gameObject;
            log.AppendLine("Scene " + root.scene.name + ":");
            BuildOn(root, log);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(root.scene);
        }
        Debug.Log("[OneBody] " + log);
        return log.ToString();
    }

    static void BuildOn(GameObject root, System.Text.StringBuilder log)
    {
        {
            Transform Find(string name) { foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t != null && t.name == name) return t; return null; }
            var player = root.GetComponentInChildren<Player>(true);
            // The animated rig: BodyRig once built, before that the first-person TableArmsMesh with its animator.
            var rigT = Find("BodyRig");
            if (rigT == null) { var t = Find("TableArmsMesh"); if (t != null && t.GetComponent<Animator>() != null) rigT = t; }
            if (rigT == null) { log.AppendLine("No animated rig found"); return; }
            var animator = rigT.GetComponent<Animator>();

            // Body at the feet, holding the rig. It stays under the player's Controller, where the player's scripts look
            // for the rig's parts (WeaponAnimationRelay, hands); OneBodyRig places it in the world each frame.
            var body = Find("Body");
            if (body == null)
            {
                body = new GameObject("Body").transform;
                log.AppendLine("Made Body");
            }
            if (body.parent != player.transform)
            {
                body.SetParent(player.transform, false);
                body.localPosition = Vector3.zero;
                body.localRotation = Quaternion.identity;
                log.AppendLine("Body under the Controller");
            }
            if (rigT.parent != body)
            {
                rigT.SetParent(body, false);
                rigT.localPosition = new Vector3(0f, RootHeight, 0f);
                rigT.localRotation = Quaternion.identity;
                rigT.localScale = Vector3.one;
                log.AppendLine("Moved the rig under Body");
            }
            if (rigT.name != "BodyRig") { rigT.name = "BodyRig"; log.AppendLine("Renamed the rig BodyRig"); }

            // One mesh: the arms-only cut goes, the full Hunter body comes on in its pixel restyle.
            if (rigT != null)
            {
                foreach (var smr in rigT.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh != null && smr.sharedMesh.name.Contains("Hunter_Male") && smr.gameObject.activeSelf && smr.name != "SM_Chr_Hunter_Male_01")
                    { log.AppendLine("Deleted the arms cut " + smr.name); Object.DestroyImmediate(smr.gameObject); continue; }
                    if (smr.name == "SM_Chr_Hunter_Male_01")
                    {
                        smr.gameObject.SetActive(true);
                        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(HunterBody + ".asset");
                        var mat = AssetDatabase.LoadAssetAtPath<Material>(HunterBody + ".mat");
                        if (mesh != null) smr.sharedMesh = mesh;
                        if (mat != null) smr.sharedMaterial = mat;
                        smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        log.AppendLine("Full body on: " + smr.name);
                    }
                }
                foreach (var t in rigT.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
                foreach (var r in rigT.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = FirstPersonLighting.WorldLayer;
                var vm = rigT.GetComponent<ViewModel>();
                if (vm != null) { Object.DestroyImmediate(vm); log.AppendLine("Removed ViewModel"); }
            }

            // The first-person arms: a copy of the skeleton where the arms rig always hung (FirstPersonVisuals/TableArmsOffset,
            // 1.5 under the eye), with no animator and no meshes: OneBodyRig poses it from the body and cuts the arms from
            // the body's mesh at run time. Held items stay on its hands, as they always were.
            var visuals = Find("FirstPersonVisuals");
            var offset = visuals != null ? visuals.Find("TableArmsOffset") : null;
            if (visuals != null && offset == null)
            {
                offset = new GameObject("TableArmsOffset").transform;
                offset.SetParent(visuals, false);
                offset.localPosition = new Vector3(0f, -1.5f, 0f);
                log.AppendLine("Made TableArmsOffset");
            }
            var arms = offset != null ? offset.Find("TableArmsMesh") : null;
            if (offset != null && arms == null)
            {
                // Plain transforms in the shape of the body's skeleton: no components, no meshes.
                arms = Bones(rigT, offset);
                arms.name = "TableArmsMesh";
                arms.localPosition = Vector3.zero; arms.localRotation = Quaternion.identity; arms.localScale = Vector3.one;
                arms.gameObject.AddComponent<ViewModel>();
                log.AppendLine("Made the first-person arms skeleton");
            }
            // What the body's hands held (the weapon collider, held items) moves to the first-person hands as it is, so
            // every reference to it stays good. The body's hands get copies at run time.
            if (arms != null)
                foreach (var socket in rigT.GetComponentsInChildren<Transform>(true))
                {
                    if (socket == null || !socket.name.EndsWith("Hand_Socket") || socket.childCount == 0) continue;
                    Transform twin = null;
                    foreach (var t in arms.GetComponentsInChildren<Transform>(true)) if (t.name == socket.name) twin = t;
                    if (twin == null) continue;
                    for (int i = socket.childCount - 1; i >= 0; i--)
                    {
                        var child = socket.GetChild(i);
                        log.AppendLine("Moved " + child.name + " to the first-person " + socket.name);
                        child.SetParent(twin, false);
                    }
                }
            // Equipment's hand anchors: the first-person hands.
            if (arms != null)
                foreach (var eq in root.GetComponentsInChildren<Equipment>(true))
                {
                    var so = new SerializedObject(eq);
                    var list = so.FindProperty("anchors");
                    for (int i = 0; i < list.arraySize; i++)
                    {
                        var tp = list.GetArrayElementAtIndex(i).FindPropertyRelative("transform");
                        var current = tp.objectReferenceValue as Transform;
                        if (current == null || current.IsChildOf(arms)) continue;
                        Transform match = null;
                        foreach (var t in arms.GetComponentsInChildren<Transform>(true)) if (t != null && t.name == current.name) { match = t; break; }
                        if (match != null) { tp.objectReferenceValue = match; log.AppendLine("Anchor " + current.name + " on the first-person arms"); }
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

            // Spells come from the first-person hand, where the owner sees the flame (the body's twin is drawn as a shadow).
            if (arms != null)
                foreach (var caster in root.GetComponentsInChildren<PlayerSpellcasting>(true))
                {
                    if (caster.castOrigin == null || caster.castOrigin.IsChildOf(arms)) continue;
                    Transform twin = null;
                    foreach (var t in arms.GetComponentsInChildren<Transform>(true)) if (t.name == caster.castOrigin.name) { twin = t; break; }
                    if (twin == null) continue;
                    caster.castOrigin = twin;
                    EditorUtility.SetDirty(caster);
                    log.AppendLine("Spell origin on the first-person " + twin.parent.name);
                }

            // The old knight goes; its references point at the one animator.
            var knight = Find("BodyVisuals");
            if (knight != null) { Object.DestroyImmediate(knight.gameObject); log.AppendLine("Deleted BodyVisuals"); }
            if (animator != null)
            {
                foreach (var c in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (c == null) continue;
                    var so = new SerializedObject(c);
                    var it = so.GetIterator();
                    bool changed = false;
                    while (it.NextVisible(true))
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null &&
                            it.type == "PPtr<$Animator>" && (it.name == "bodyAnimator" || it.name == "animator"))
                        { it.objectReferenceValue = animator; changed = true; }
                    if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); log.AppendLine("Pointed " + c.GetType().Name + " at the one animator"); }
                }
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            // The first-person arms are drawn as they always were (the view-model squash at 70).
            foreach (var p in root.GetComponentsInChildren<PlayerViewPresentation>(true))
                if (p.viewModelFieldOfView != 70f) { p.viewModelFieldOfView = 70f; log.AppendLine("View-model squash back at 70"); }

            // The rig that keeps it together.
            var rig = body.GetComponent<OneBodyRig>();
            if (rig == null) { rig = body.gameObject.AddComponent<OneBodyRig>(); log.AppendLine("Added OneBodyRig"); }
            rig.player = player;
            rig.animator = animator;
            rig.firstPersonArms = arms;
            rig.chestLimit = 15f;
        }
    }
}

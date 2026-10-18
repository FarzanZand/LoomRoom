using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// The Animation Workbench's rigs. Three copies of the table player, made from TablePlayer.prefab and stripped to
// bones and meshes:
// - the edited body, in the open scene (not saved, far below the world), with the animator and FirstPersonTable: the
//   Animation window edits clips on it and this window samples them onto it;
// - the first-person view: the camera-attached skeleton copy with the arms cut of the body (OneBodyRig.ArmsOf) and the
//   held items, posed from the edited body and squashed to the view-model field of view, exactly as in the game;
// - the outside view: a second body posed from the edited one, the arms carried as others see them (OneBodyRig.Reach).
// The two views live in their own preview scenes, so nothing in the open scene lights or hides them.
public partial class AnimationWorkbench
{
    const string PrefabPath = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
    const string RigName = "Animation Workbench rig (not saved)";
    static readonly Vector3 RigPlace = new(0f, -3000f, 0f);
    // Where the eyes are over the body's root (ViewRig: Yaw 1 + Pitch .67 + ViewPoint -.15), and the arms under them.
    const float EyeHeight = 1.52f, ArmsBelowEyes = 1.5f;

    GameObject rigHolder;
    Animator body;
    readonly Dictionary<string, Transform> bodyBones = new();

    PreviewRenderUtility firstView, outsideView;
    Transform fpVisuals;
    GameObject fpRoot, outsideRoot;
    readonly List<(Transform from, Transform to)> fpPose = new(), outsidePose = new();
    Transform outsideBody;
    Transform[][] outsideArms;
    Transform fpRight, fpLeft, outsideRight, outsideLeft;
    float cameraFov = 85f, viewModelFov = 70f;

    PlayableGraph graph;
    AnimationLayerMixerPlayable mixer;
    readonly List<AnimationClipPlayable> layerClips = new();
    string graphSignature;

    readonly List<Mesh> cutMeshes = new();
    double nextBuild;

    bool RigReady => body != null && fpRoot != null && outsideRoot != null;

    // ── Building ─────────────────────────────────────────────────────

    void BuildRig()
    {
        DestroyRig();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { status = "TablePlayer.prefab not found"; return; }

        // The prefab is copied under an inactive holder, so none of its scripts wake (its Cinemachine camera would
        // take over the scene's camera), then stripped to bones and meshes.
        rigHolder = new GameObject(RigName) { hideFlags = HideFlags.DontSave };
        rigHolder.SetActive(false);
        var copy = Instantiate(prefab, rigHolder.transform);
        var bodyRig = Find(copy.transform, "BodyRig");
        var visuals = Find(copy.transform, "FirstPersonVisuals");
        var camera = copy.GetComponentInChildren<Unity.Cinemachine.CinemachineCamera>(true);
        if (camera != null) cameraFov = camera.Lens.FieldOfView;
        var presentation = copy.GetComponentInChildren<PlayerViewPresentation>(true);
        if (presentation != null && presentation.viewModelFieldOfView > 0f) viewModelFov = presentation.viewModelFieldOfView;
        var oneBody = copy.GetComponentInChildren<OneBodyRig>(true);
        if (oneBody != null) { armDrop = oneBody.armDrop; armTuck = oneBody.armTuck; armTuckSide = oneBody.armTuckSide; }
        if (bodyRig == null || visuals == null) { status = "TablePlayer.prefab has no BodyRig or FirstPersonVisuals"; DestroyRig(); return; }

        // The edited body: the animator and its meshes.
        bodyRig.SetParent(rigHolder.transform, false);
        bodyRig.localPosition = Vector3.zero;
        Strip(bodyRig.gameObject, keepAnimator: true);
        body = bodyRig.GetComponent<Animator>();
        body.applyRootMotion = false;
        body.fireEvents = false;   // its scripts are stripped: clip events (AttackBegin...) would find no receiver
        // The first-person skeleton copy (its own meshes are made from the body below).
        visuals.SetParent(null, false);
        DestroyImmediate(copy);
        rigHolder.transform.position = RigPlace;
        rigHolder.SetActive(true);
        bodyBones.Clear();
        foreach (var t in bodyRig.GetComponentsInChildren<Transform>(true)) bodyBones.TryAdd(t.name, t);

        // First person: FirstPersonVisuals at the eyes, the arms cut of every visible body mesh on its skeleton.
        firstView = NewPreview();
        fpRoot = new GameObject("First person");
        visuals.SetParent(fpRoot.transform, false);
        visuals.localPosition = new Vector3(0f, EyeHeight, 0f);
        Strip(visuals.gameObject, keepAnimator: false);
        fpVisuals = visuals;
        var armsMesh = Find(visuals, "TableArmsMesh");
        fpArms = armsMesh;
        var fpBones = new Dictionary<string, Transform>();
        foreach (var t in armsMesh.GetComponentsInChildren<Transform>(true)) fpBones.TryAdd(t.name, t);
        foreach (var r in body.GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (r.sharedMesh == null || !r.sharedMesh.isReadable) continue;
            var mesh = OneBodyRig.ArmsOf(r);
            if (mesh == null) continue;
            mesh.hideFlags = HideFlags.DontSave;
            cutMeshes.Add(mesh);
            var go = new GameObject(r.name + " (arms)");
            go.transform.SetParent(armsMesh, false);
            var cut = go.AddComponent<SkinnedMeshRenderer>();
            var bones = new Transform[r.bones.Length];
            for (int i = 0; i < bones.Length; i++) if (r.bones[i] != null) fpBones.TryGetValue(r.bones[i].name, out bones[i]);
            cut.bones = bones;
            cut.rootBone = r.rootBone != null && fpBones.TryGetValue(r.rootBone.name, out var rb) ? rb : armsMesh;
            cut.sharedMesh = mesh;
            cut.sharedMaterials = r.sharedMaterials;
            cut.updateWhenOffscreen = true;
        }
        fpBones.TryGetValue("RightHand_Socket", out fpRight);
        fpBones.TryGetValue("LeftHand_Socket", out fpLeft);
        Pairs(fpBones, fpPose);
        firstView.AddSingleGO(fpRoot);
        firstView.AddSingleGO(Floor());

        // Outside: a second body.
        outsideView = NewPreview();
        outsideRoot = new GameObject("Outside");
        var other = Instantiate(body.gameObject, outsideRoot.transform);
        other.name = "Body";
        DestroyImmediate(other.GetComponent<Animator>());
        outsideBody = other.transform;
        outsideBody.localPosition = Vector3.zero;
        var outBones = new Dictionary<string, Transform>();
        foreach (var t in other.GetComponentsInChildren<Transform>(true)) outBones.TryAdd(t.name, t);
        outBones.TryGetValue("RightHand_Socket", out outsideRight);
        outBones.TryGetValue("LeftHand_Socket", out outsideLeft);
        var arms = new List<Transform[]>();
        foreach (var side in new[] { "_R", "_L" })
            if (outBones.TryGetValue("Shoulder" + side, out var u) && outBones.TryGetValue("Elbow" + side, out var f) && outBones.TryGetValue("Hand" + side, out var h))
                arms.Add(new[] { u, f, h });
        outsideArms = arms.ToArray();
        Pairs(outBones, outsidePose);
        outsideView.AddSingleGO(outsideRoot);
        outsideView.AddSingleGO(Floor());

        foreach (var root in new[] { fpRoot, outsideRoot }) Prepare(root);
        RefreshHeld();
        graphSignature = null;
        status = "";
    }

    void Pairs(Dictionary<string, Transform> to, List<(Transform, Transform)> list)
    {
        list.Clear();
        foreach (var pair in to)
            if (bodyBones.TryGetValue(pair.Key, out var from) && from != body.transform && !IsHeld(pair.Value)) list.Add((from, pair.Value));
    }

    bool IsHeld(Transform t)
    {
        foreach (var socket in new[] { fpRight, fpLeft, outsideRight, outsideLeft })
            if (socket != null && t != socket && t.IsChildOf(socket)) return true;
        return false;
    }

    // Every copy is drawn in the preview scenes on the default layer, lit by their lights (the view-model layer and the
    // game's rendering layers would hide it there).
    static void Prepare(GameObject root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) { t.gameObject.layer = 0; t.gameObject.hideFlags = HideFlags.DontSave; }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.renderingLayerMask = uint.MaxValue;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    PreviewRenderUtility NewPreview()
    {
        var p = new PreviewRenderUtility();
        p.camera.nearClipPlane = .01f;
        p.camera.farClipPlane = 100f;
        p.camera.clearFlags = CameraClearFlags.SolidColor;
        p.camera.backgroundColor = new Color(.16f, .17f, .19f);
        p.camera.fieldOfView = cameraFov;
        p.ambientColor = new Color(.42f, .42f, .45f);
        p.lights[0].intensity = 1.1f;
        p.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        p.lights[1].intensity = .5f;
        p.lights[1].transform.rotation = Quaternion.Euler(20f, 150f, 0f);
        return p;
    }

    static Material floorMaterial;

    static GameObject Floor()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floor.name = "Floor";
        DestroyImmediate(floor.GetComponent<Collider>());
        floor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
        floor.transform.localScale = new Vector3(12f, 12f, 1f);
        if (floorMaterial == null)
        {
            floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
            var grid = new Texture2D(2, 2) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            Color a = new(.3f, .3f, .32f), b = new(.25f, .25f, .27f);
            grid.SetPixels(new[] { a, b, b, a });
            grid.Apply();
            floorMaterial.SetTexture("_BaseMap", grid);
            floorMaterial.SetTextureScale("_BaseMap", new Vector2(12f, 12f));
        }
        floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        floor.hideFlags = HideFlags.DontSave;
        return floor;
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    // Bones and meshes only: every script, collider, light and effect goes.
    static void Strip(GameObject root, bool keepAnimator)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var components = t.GetComponents<Component>();
            for (int i = components.Length - 1; i >= 0; i--)
            {
                var c = components[i];
                if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer) continue;
                if (keepAnimator && c is Animator) continue;
                DestroyImmediate(c);
            }
        }
    }

    void DestroyRig()
    {
        if (graph.IsValid()) graph.Destroy();
        layerClips.Clear();
        firstView?.Cleanup(); firstView = null;
        outsideView?.Cleanup(); outsideView = null;
        if (rigHolder != null) DestroyImmediate(rigHolder);
        // A rig left by an earlier session of the editor (the window closed with a domain reload).
        foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            if (go != null && go.name == RigName && go.scene.IsValid()) DestroyImmediate(go);
        foreach (var m in cutMeshes) if (m != null) DestroyImmediate(m);
        cutMeshes.Clear();
        rigHolder = null; body = null; fpRoot = null; outsideRoot = null;
    }

    // ── Held items ───────────────────────────────────────────────────

    void RefreshHeld()
    {
        if (!RigReady) return;
        // The edited body holds them too, so posing in the Scene view shows the blade.
        bodyBones.TryGetValue("RightHand_Socket", out var bodyRight);
        bodyBones.TryGetValue("LeftHand_Socket", out var bodyLeft);
        foreach (var socket in new[] { fpRight, fpLeft, outsideRight, outsideLeft, bodyRight, bodyLeft })
            if (socket != null) for (int i = socket.childCount - 1; i >= 0; i--) DestroyImmediate(socket.GetChild(i).gameObject);
        Hold(rightItem, fpRight); Hold(rightItem, outsideRight); Hold(rightItem, bodyRight);
        Hold(leftItem, fpLeft); Hold(leftItem, outsideLeft); Hold(leftItem, bodyLeft);
        Prepare(fpRoot); Prepare(outsideRoot);
        tipLocal = BladeTip(fpRight);
        trace.Clear();
    }

    // As Equipment does: the world model under the hand socket at the item's held place, turn and scale.
    static void Hold(ItemData item, Transform socket)
    {
        if (item == null || item.worldPrefab == null || socket == null) return;
        var holder = new GameObject("holder");
        holder.SetActive(false);
        var go = Instantiate(item.worldPrefab, holder.transform);
        go.name = item.itemName;
        foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true)) if (p != null) DestroyImmediate(p.gameObject);
        foreach (var l in go.GetComponentsInChildren<Light>(true)) if (l != null) DestroyImmediate(l.gameObject);
        Strip(go, keepAnimator: false);
        go.transform.SetParent(socket, false);
        go.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
        go.transform.localScale *= Mathf.Max(.01f, item.heldScale);
        DestroyImmediate(holder);
    }

    // The held item's farthest point from the hand, in the socket's space: the blade's tip for the path trace.
    Vector3? tipLocal;

    static Vector3? BladeTip(Transform socket)
    {
        if (socket == null || socket.childCount == 0) return null;
        Vector3? best = null; float far = 0f;
        foreach (var f in socket.GetComponentsInChildren<MeshFilter>(true))
        {
            if (f.sharedMesh == null) continue;
            var b = f.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = socket.InverseTransformPoint(f.transform.TransformPoint(corner));
                if (p.sqrMagnitude > far) { far = p.sqrMagnitude; best = p; }
            }
        }
        return best;
    }

    // ── Sampling ─────────────────────────────────────────────────────

    // The clip is played as the game would on its layer (that layer's mask), over each other layer's resting pose
    // (legs standing, the other arm at its idle), all in one playable graph on the edited body.
    void Sample(float time)
    {
        if (!RigReady || clip == null) return;
        string signature = clip.GetEntityId() + "|" + clipLayer + "|" + baseLayers;
        if (!graph.IsValid() || signature != graphSignature) BuildGraph(signature);
        foreach (var p in layerClips) if (p.IsValid() && p.GetAnimationClip() != clip) p.SetTime(0.0);
        if (layerClips.Count > 0) layerClips[^1].SetTime(time);
        graph.Evaluate(0f);
        sampledTime = time;
        PoseViews();
    }

    void BuildGraph(string signature)
    {
        if (graph.IsValid()) graph.Destroy();
        layerClips.Clear();
        graphSignature = signature;
        graph = PlayableGraph.Create("Animation Workbench");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "Workbench", body);
        var layers = new List<(AnimationClip clip, AvatarMask mask)>();
        if (baseLayers && controller != null)
            foreach (var layer in controller.layers)
            {
                if (layer.blendingMode != AnimatorLayerBlendingMode.Override || layer.defaultWeight <= 0f) continue;
                var rest = RestClip(layer);
                if (rest != null) layers.Add((rest, layer.avatarMask));
            }
        AvatarMask clipMask = null;
        if (controller != null && clipLayer >= 0 && clipLayer < controller.layers.Length) clipMask = controller.layers[clipLayer].avatarMask;
        layers.Add((clip, clipMask));
        mixer = AnimationLayerMixerPlayable.Create(graph, layers.Count);
        for (int i = 0; i < layers.Count; i++)
        {
            var p = AnimationClipPlayable.Create(graph, layers[i].clip);
            p.SetApplyFootIK(false);
            graph.Connect(p, 0, mixer, i);
            mixer.SetInputWeight(i, 1f);
            if (layers[i].mask != null) mixer.SetLayerMaskFromAvatarMask((uint)i, layers[i].mask);
            layerClips.Add(p);
        }
        output.SetSourcePlayable(mixer);
    }

    // What a layer shows at rest: its state named Idle (the arm holding its weapon), else its default state; a blend
    // tree gives its child nearest standing still.
    static AnimationClip RestClip(AnimatorControllerLayer layer)
    {
        AnimatorState state = null;
        foreach (var s in layer.stateMachine.states) if (s.state.name == "Idle") state = s.state;
        if (state == null) state = layer.stateMachine.defaultState;
        return state != null ? First(state.motion) : null;
    }

    static AnimationClip First(Motion motion)
    {
        if (motion is AnimationClip c) return c;
        if (motion is BlendTree tree && tree.children.Length > 0)
        {
            var best = tree.children[0];
            foreach (var child in tree.children) if (child.position.sqrMagnitude < best.position.sqrMagnitude) best = child;
            return First(best.motion);
        }
        return null;
    }

    // The two views take the edited body's pose: first person exactly as the clip has it, the outside view with the
    // arms carried as others see them.
    void PoseViews()
    {
        foreach (var (from, to) in fpPose) to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
        foreach (var (from, to) in outsidePose) to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
        // Preview scenes don't run the skinning update by themselves: without this a mesh could draw a stale pose
        // (the right arm went missing in renders).
        foreach (var root in new[] { fpRoot, outsideRoot })
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.forceMatrixRecalculationPerRender = true; }
        if (naturalArms && outsideArms != null)
            foreach (var arm in outsideArms) OneBodyRig.Reach(arm, outsideRoot.transform, armDrop, armTuck, armTuckSide);
        // The eyes look where the pitch slider says; the view model is squashed to its own field of view. A clip's
        // view curves (OneBodyRig.ViewCurves) place the arms in the view and change that field of view, as in the game.
        fpVisuals.localRotation = Quaternion.Euler(lookPitch, 0f, 0f);
        if (fpArms != null) fpArms.SetLocalPositionAndRotation(new Vector3(ViewCurve(4), ViewCurve(0), ViewCurve(1)), Quaternion.Euler(ViewCurve(2), ViewCurve(5), ViewCurve(6)));
        float model = viewModelFov + ViewCurve(3);
        float k = Mathf.Tan(cameraFov * .5f * Mathf.Deg2Rad) / Mathf.Tan(model * .5f * Mathf.Deg2Rad);
        fpVisuals.localScale = new Vector3(k, k, 1f);
    }

    float sampledTime;
    Transform fpArms;

    float ViewCurve(int index)
    {
        if (clip == null) return 0f;
        var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), OneBodyRig.ViewCurves[index]));
        return curve != null ? curve.Evaluate(sampledTime) : 0f;
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Tools > LoomRoom > Multiplayer > Build Body Animations: the clips and the controller a teammate's body plays
// (MultiplayerManager.bodyController). A teammate's arms play the first-person clips themselves: the table player's
// arms and the Synty bodies are the same skeleton (same bone names, lengths and hierarchy), so each first-person clip
// (Characters/Animations/FirstPersonPlayer) is played on a body, read back as humanoid muscles and saved as a
// humanoid clip: the swings for the right arm, the block and the bash for the left, the ready idle for both, with the
// same windup, hold and release as the arms layers of FirstPersonTable.controller. The weapons sit on the first-person
// sockets (the Coop weapon and shield grips), so the blade and the shield face where they do in first person.
// Baked clips are made once (edit them by hand after; the Overwrite item bakes them again); the controller is rebuilt
// each time. The crouch is the walk with bent legs.
public static class CoopBodyAnimations
{
    const string Folder = "Assets/Game/Multiplayer/Animations";
    const string ControllerPath = Folder + "/Coop Body.controller";
    const string FirstPerson = "Assets/Game/Characters/Animations/FirstPersonPlayer/";
    const string Locomotion = "Assets/Game/Characters/Animations/ThirdPerson/Locomotion/";
    const string Source = "Assets/Game/Characters/Animations/HumanoidController.controller";
    const string BodyPath = "Assets/Game/Multiplayer/Bodies/Warrior body.prefab";
    const float BakeRate = 30f;

    [MenuItem("Tools/LoomRoom/Multiplayer/Build Body Animations")]
    public static void Build() => Run(false);

    [MenuItem("Tools/LoomRoom/Multiplayer/Build Body Animations (Overwrite Clips)")]
    public static void BuildOverwrite() => Run(true);

    enum Side { Right, Left, Both }

    // Bent knees and hips, a forward lean, the body lowered.
    static readonly Dictionary<string, float> CrouchLegs = new()
    {
        ["Left Upper Leg Front-Back"] = .55f, ["Right Upper Leg Front-Back"] = .55f,
        ["Left Lower Leg Stretch"] = -.75f, ["Right Lower Leg Stretch"] = -.75f,
        ["Left Foot Up-Down"] = .35f, ["Right Foot Up-Down"] = .35f,
        ["Spine Front-Back"] = .3f, ["Chest Front-Back"] = .1f, ["Head Nod Down-Up"] = -.2f,
        ["RootT.y"] = -.38f,   // the body lowered, in the humanoid's own units
    };

    static void Run(bool overwrite)
    {
        Directory.CreateDirectory(Folder);
        var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(Locomotion + "Walk.anim");
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Locomotion + "Idle.anim");
        var bodyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPath);
        if (walk == null || idle == null || bodyPrefab == null) { Debug.LogError("[Coop] The Idle and Walk clips or the Warrior body are missing."); return; }

        var clips = new Dictionary<string, AnimationClip>();
        var body = Object.Instantiate(bodyPrefab);
        body.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var animator = body.GetComponentInChildren<Animator>();
            AnimationClip Bake(string fp, string name, Side side, bool loop) => BakeFirstPerson(animator, idle, fp, name, side, loop, overwrite);
            clips["ReadyRight"] = Bake("Idle", "Coop Ready (right)", Side.Right, true);
            clips["ReadyLeft"] = Bake("Idle", "Coop Ready (left)", Side.Left, true);
            clips["Windup"] = Bake("Attack_Windup_Feel", "Coop Windup", Side.Right, false);
            clips["Hold"] = Bake("Attack_Hold", "Coop Hold", Side.Right, true);
            clips["SwingA"] = Bake("Attack_Swing_A", "Coop Swing A", Side.Right, false);
            clips["SwingB"] = Bake("Attack_Swing_B", "Coop Swing B", Side.Right, false);
            clips["Heavy"] = Bake("Attack_Swing_Heavy", "Coop Swing Heavy", Side.Right, false);
            clips["Hurt"] = Bake("Player_Hurt", "Coop Hurt", Side.Right, false);
            clips["BlockRaise"] = Bake("Block_Raise", "Coop Block Raise", Side.Left, false);
            clips["Block"] = Bake("BlockIdle", "Coop Block", Side.Left, true);
            clips["Bash"] = Bake("Shield_Bash", "Coop Shield Bash", Side.Left, false);
            clips["CrouchIdle"] = CrouchIdle(animator, idle, overwrite);
            clips["CrouchWalk"] = CrouchWalk(walk, overwrite);
        }
        finally { Object.DestroyImmediate(body); }
        if (clips.Values.Any(c => c == null)) { Debug.LogError("[Coop] A first-person clip is missing; the body animations were not built."); return; }

        BuildController(clips);
        AssetDatabase.SaveAssets();
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Multiplayer/Prefabs/Multiplayer Manager.prefab");
        var m = prefab != null ? prefab.GetComponent<MultiplayerManager>() : null;
        if (m != null && m.bodyController != controller) { m.bodyController = controller; EditorUtility.SetDirty(prefab); AssetDatabase.SaveAssets(); }
        var manager = Object.FindAnyObjectByType<MultiplayerManager>(FindObjectsInactive.Include);
        if (manager != null && manager.bodyController != controller) { Undo.RecordObject(manager, "Body controller"); manager.bodyController = controller; EditorUtility.SetDirty(manager); }
        Debug.Log($"[Coop] Body animations built: {ControllerPath}");
    }

    // ── Baking ────────────────────────────────────────────────────────

    // A first-person clip played on the body (its arm bones, by path), read back as humanoid muscles at 30 frames a
    // second, the side's arm (and fingers) kept.
    static AnimationClip BakeFirstPerson(Animator animator, AnimationClip idle, string fpName, string name, Side side, bool loop, bool overwrite)
    {
        string path = $"{Folder}/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null && !overwrite) return existing;
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(FirstPerson + fpName + ".anim");
        if (source == null) { Debug.LogError($"[Coop] No first-person clip {fpName}."); return null; }

        // The first-person clip's curves are Euler rotations of the arm bones, by path; applied to the body's bones of
        // the same paths by hand (sampling a generic clip on a humanoid body moves the rest of it too).
        var bones = new List<(Transform bone, AnimationCurve x, AnimationCurve y, AnimationCurve z)>();
        foreach (var group in AnimationUtility.GetCurveBindings(source).Where(b => b.propertyName.StartsWith("localEulerAnglesRaw")).GroupBy(b => b.path))
        {
            var bone = animator.transform.Find(group.Key);
            if (bone == null) continue;
            AnimationCurve Axis(string a) { var b = group.FirstOrDefault(x => x.propertyName.EndsWith(a)); return b.path != null ? AnimationUtility.GetEditorCurve(source, b) : null; }
            bones.Add((bone, Axis(".x"), Axis(".y"), Axis(".z")));
        }
        var handler = new HumanPoseHandler(animator.avatar, animator.transform);
        var pose = new HumanPose();
        var muscles = Enumerable.Range(0, HumanTrait.MuscleCount).Where(i => Keeps(HumanTrait.MuscleName[i], side)).ToArray();
        var curves = muscles.ToDictionary(i => i, _ => new AnimationCurve());
        int frames = Mathf.Max(2, Mathf.CeilToInt(source.length * BakeRate) + 1);
        for (int f = 0; f < frames; f++)
        {
            float t = Mathf.Min(source.length, f / BakeRate);
            idle.SampleAnimation(animator.gameObject, 0f);       // the body upright, as it stands
            foreach (var (bone, x, y, z) in bones)               // the arms as in first person
            {
                var e = bone.localEulerAngles;
                bone.localRotation = Quaternion.Euler(x != null ? x.Evaluate(t) : e.x, y != null ? y.Evaluate(t) : e.y, z != null ? z.Evaluate(t) : e.z);
            }
            handler.GetHumanPose(ref pose);
            foreach (var i in muscles) curves[i].AddKey(new Keyframe(t, pose.muscles[i]));
        }
        handler.Dispose();

        var clip = existing != null ? existing : new AnimationClip();
        clip.ClearCurves();
        foreach (var pair in curves)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), HumanTrait.MuscleName[pair.Key]), pair.Value);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (existing == null) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
        return clip;
    }

    static bool Keeps(string muscle, Side side)
    {
        bool right = muscle.StartsWith("Right Shoulder") || muscle.StartsWith("Right Arm") || muscle.StartsWith("Right Forearm") || muscle.StartsWith("Right Hand") || muscle.StartsWith("RightHand.");
        bool left = muscle.StartsWith("Left Shoulder") || muscle.StartsWith("Left Arm") || muscle.StartsWith("Left Forearm") || muscle.StartsWith("Left Hand") || muscle.StartsWith("LeftHand.");
        return side == Side.Right ? right : side == Side.Left ? left : right || left;
    }

    // The crouch: idle with bent legs (whole body, so the legs and the drop come with it).
    static AnimationClip CrouchIdle(Animator animator, AnimationClip idle, bool overwrite)
    {
        string path = $"{Folder}/Coop Crouch Idle.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null && !overwrite) return existing;
        var clip = existing != null ? existing : new AnimationClip();
        clip.ClearCurves();
        foreach (var binding in AnimationUtility.GetCurveBindings(idle))
        {
            if (binding.type != typeof(Animator)) continue;
            var curve = AnimationUtility.GetEditorCurve(idle, binding);
            if (CrouchLegs.TryGetValue(binding.propertyName, out var add)) curve = Offset(curve, add);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (existing == null) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
        return clip;
    }

    // The walk with bent legs and the body lowered.
    static AnimationClip CrouchWalk(AnimationClip walk, bool overwrite)
    {
        string path = $"{Folder}/Coop Crouch Walk.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null && !overwrite) return existing;
        if (existing != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(walk), path);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Animator) || !CrouchLegs.TryGetValue(binding.propertyName, out var add)) continue;
            bool leg = binding.propertyName.Contains("Leg") || binding.propertyName.Contains("Foot");
            AnimationUtility.SetEditorCurve(clip, binding, Offset(AnimationUtility.GetEditorCurve(clip, binding), leg ? add * .8f : add));
        }
        AnimationUtility.SetAnimationEvents(clip, new AnimationEvent[0]);   // the walk's footstep events stay with the walk
        return clip;
    }

    static AnimationCurve Offset(AnimationCurve curve, float add)
    {
        var keys = curve.keys;
        for (int i = 0; i < keys.Length; i++) keys[i].value += add;
        return new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
    }

    // ── The controller ────────────────────────────────────────────────

    static void BuildController(Dictionary<string, AnimationClip> clips)
    {
        var source = AssetDatabase.LoadAssetAtPath<AnimatorController>(Source);
        if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
        var c = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var p in new[] { "MoveX", "MoveZ", "Speed", "MotionSpeed", "HurtSpeed" }) c.AddParameter(p, AnimatorControllerParameterType.Float);
        foreach (var p in new[] { "Grounded", "Crouch", "Armed", "Charge", "Block", "Dead" }) c.AddParameter(p, AnimatorControllerParameterType.Bool);
        foreach (var p in new[] { "SwingA", "SwingB", "SwingHeavy", "Bash", "CastSpell", "Hurt", "Death", "Sit", "Stand" }) c.AddParameter(p, AnimatorControllerParameterType.Trigger);
        c.parameters = c.parameters.Select(p => { if (p.name == "Grounded") p.defaultBool = true; if (p.name == "HurtSpeed" || p.name == "MotionSpeed") p.defaultFloat = 1f; return p; }).ToArray();

        // Base layer: walking (the enemies' own blend), crouching, the jump, sitting and death.
        var sm = c.layers[0].stateMachine;
        var sourceBase = source.layers[0].stateMachine;
        AnimatorState Copy(string name, Vector3 at)
        {
            var from = sourceBase.states.FirstOrDefault(s => s.state.name == name).state;
            var st = sm.AddState(name, at);
            if (from != null) { st.motion = from.motion is BlendTree tree ? CopyTree(tree, c) : from.motion; st.speed = from.speed; st.speedParameter = from.speedParameter; st.speedParameterActive = from.speedParameterActive; }
            return st;
        }
        var walk = Copy("IdleWalkRunBT", new Vector3(300, 0));
        walk.name = "Locomotion";
        sm.defaultState = walk;

        var crouch = sm.AddState("Crouch", new Vector3(300, 120));
        var crouchTree = new BlendTree { name = "Crouch", blendType = BlendTreeType.Simple1D, blendParameter = "Speed", hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(crouchTree, c);
        crouchTree.AddChild(clips["CrouchIdle"], 0f);
        crouchTree.AddChild(clips["CrouchWalk"], 1.2f);
        crouch.motion = crouchTree;
        Transition(walk, crouch, .15f, ("Crouch", true));
        Transition(crouch, walk, .15f, ("Crouch", false));

        var inAir = Copy("JumpInAir", new Vector3(600, 0));
        var land = Copy("JumpLandBT", new Vector3(600, 120));
        land.name = "JumpLand";
        foreach (var from in new[] { walk, crouch }) Transition(from, inAir, .1f, ("Grounded", false));
        Transition(inAir, land, .05f, ("Grounded", true));
        var landOut = land.AddTransition(walk); landOut.hasExitTime = true; landOut.exitTime = .5f; landOut.duration = .15f;

        var toSit = Copy("Stand To Sit", new Vector3(0, 240));
        var sitting = Copy("Sitting Idle", new Vector3(300, 240));
        var toStand = Copy("Sit To Stand", new Vector3(600, 240));
        Any(sm, toSit, "Sit", .2f);
        var settle = toSit.AddTransition(sitting); settle.hasExitTime = true; settle.exitTime = .95f; settle.duration = .1f;
        Any(sm, toStand, "Stand", .2f);
        var up = toStand.AddTransition(walk); up.hasExitTime = true; up.exitTime = .9f; up.duration = .2f;

        var death = Copy("Death1", new Vector3(0, 360));
        death.name = "Death";
        Any(sm, death, "Death", .1f);

        // The arms, as the first-person controller's RightArm and LeftArm layers: on the table (Armed) both rest in the
        // ready pose; in the room they swing with the walk.
        var right = ArmLayer(c, "Right arm", Mask("Coop right arm", Side.Right));
        var rEmpty = right.AddState("Empty", new Vector3(300, -100));
        right.defaultState = rEmpty;
        var rReady = State(right, "Ready", clips["ReadyRight"], new Vector3(300, 0));
        var windup = State(right, "Windup", clips["Windup"], new Vector3(0, 120));
        var hold = State(right, "Hold", clips["Hold"], new Vector3(0, 240));
        var swingA = State(right, "Swing A", clips["SwingA"], new Vector3(250, 240));
        var swingB = State(right, "Swing B", clips["SwingB"], new Vector3(450, 240));
        var heavy = State(right, "Swing Heavy", clips["Heavy"], new Vector3(650, 240));
        var hurt = State(right, "Hurt", clips["Hurt"], new Vector3(650, 120));
        hurt.speedParameter = "HurtSpeed"; hurt.speedParameterActive = true;
        Transition(rEmpty, rReady, .2f, ("Armed", true));
        Transition(rReady, rEmpty, .2f, ("Armed", false));
        Transition(rReady, windup, .06f, ("Charge", true));
        var held = windup.AddTransition(hold); held.hasExitTime = true; held.exitTime = 1f; held.duration = .05f;
        Transition(windup, rReady, .1f, ("Charge", false));
        Transition(hold, rReady, .1f, ("Charge", false));
        // Every release starts at once, from wherever the arm is, even over the last one.
        Any(right, heavy, "SwingHeavy", .03f, true);
        Any(right, swingA, "SwingA", .03f, true);
        Any(right, swingB, "SwingB", .03f, true);
        Any(right, hurt, "Hurt", .05f, false);
        foreach (var st in new[] { swingA, swingB, heavy, hurt })
        {
            Done(st, windup, ("Charge", true));
            Done(st, rReady, ("Armed", true));
            Done(st, rEmpty);
        }

        var left = ArmLayer(c, "Left arm", Mask("Coop left arm", Side.Left));
        var lEmpty = left.AddState("Empty", new Vector3(300, -100));
        left.defaultState = lEmpty;
        var lReady = State(left, "Ready", clips["ReadyLeft"], new Vector3(300, 0));
        var raise = State(left, "Block Raise", clips["BlockRaise"], new Vector3(0, 120));
        var block = State(left, "Block", clips["Block"], new Vector3(0, 240));
        var bash = State(left, "Shield Bash", clips["Bash"], new Vector3(300, 240));
        var cast = State(left, "Cast", AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Game/Characters/Animations/ThirdPerson/Gestures/CastSpell.anim"), new Vector3(600, 240));
        Transition(lEmpty, lReady, .2f, ("Armed", true));
        Transition(lReady, lEmpty, .2f, ("Armed", false));
        Transition(lReady, raise, .04f, ("Block", true));
        var raised = raise.AddTransition(block); raised.hasExitTime = true; raised.exitTime = 1f; raised.duration = .03f;
        Transition(raise, lReady, .1f, ("Block", false));
        Transition(block, lReady, .12f, ("Block", false));
        Any(left, bash, "Bash", .03f, true);
        Any(left, cast, "CastSpell", .08f, false);
        foreach (var st in new[] { bash, cast })
        {
            Done(st, block, ("Block", true));
            Done(st, lReady, ("Armed", true));
            Done(st, lEmpty);
        }
        EditorUtility.SetDirty(c);
    }

    static AnimatorStateMachine ArmLayer(AnimatorController c, string name, AvatarMask mask)
    {
        var machine = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, c);
        c.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1f, avatarMask = mask, blendingMode = AnimatorLayerBlendingMode.Override, stateMachine = machine });
        return machine;
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, Motion motion, Vector3 at)
    {
        var st = sm.AddState(name, at);
        st.motion = motion;
        return st;
    }

    static void Transition(AnimatorState from, AnimatorState to, float duration, params (string param, bool value)[] when)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false; t.duration = duration;
        foreach (var (param, value) in when) t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
    }

    static void Any(AnimatorStateMachine sm, AnimatorState to, string trigger, float duration, bool self = false)
    {
        var t = sm.AddAnyStateTransition(to);
        t.hasExitTime = false; t.duration = duration; t.canTransitionToSelf = self;
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
    }

    // Out of a one-shot at its end: to the first state whose condition holds (listed first, checked first).
    static void Done(AnimatorState from, AnimatorState to, params (string param, bool value)[] when)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = true; t.exitTime = .95f; t.duration = .08f;
        foreach (var (param, value) in when) t.AddCondition(value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, param);
    }

    static BlendTree CopyTree(BlendTree tree, AnimatorController owner)
    {
        var copy = new BlendTree
        {
            name = tree.name, blendType = tree.blendType, blendParameter = tree.blendParameter, blendParameterY = tree.blendParameterY,
            useAutomaticThresholds = tree.useAutomaticThresholds, minThreshold = tree.minThreshold, maxThreshold = tree.maxThreshold,
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(copy, owner);
        copy.children = tree.children.Select(ch => { if (ch.motion is BlendTree sub) ch.motion = CopyTree(sub, owner); return ch; }).ToArray();
        return copy;
    }

    // One arm and its fingers (the first-person layers' RightArm and LeftArm).
    static AvatarMask Mask(string name, Side side)
    {
        string path = $"{Folder}/{name}.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask != null) return mask;
        mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            bool on = side == Side.Right
                ? part == AvatarMaskBodyPart.RightArm || part == AvatarMaskBodyPart.RightFingers || part == AvatarMaskBodyPart.RightHandIK
                : part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.LeftHandIK;
            mask.SetHumanoidBodyPartActive(part, on);
        }
        AssetDatabase.CreateAsset(mask, path);
        return mask;
    }
}

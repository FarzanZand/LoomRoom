using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Bakes the IK light swings (CombatManager.simpleSwings off) into ordinary .anim clips in
// Characters/Animations/FirstPersonPlayer/IK swings, then wires them into FirstPersonTable.controller.
// The poses come from a blade path written below in camera space (x right, y up, z forward, metres,
// before the view-model widening): the right arm is solved toward each key with two-bone IK, every
// frame is keyed, and the result is a clip you can open and edit like any other. Rebaking overwrites
// the clips, so edit the keys below (or stop rebaking once a clip has been edited by hand).
//
// Shape: a windup you can see (0.16 s), a strike that crosses the screen in about 3 frames,
// the arm held out in the follow-through, then home to idle.
public static partial class IkSwingBaker
{
    const string Folder = "Assets/Game/Characters/Animations/FirstPersonPlayer/IK swings";
    const string Controller = "Assets/Game/Characters/Animations/FirstPersonTable.controller";
    const string TablePlayer = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
    const string IdleClip = "Assets/Game/Characters/Animations/FirstPersonPlayer/Idle.anim";
    const string GripClip = "Assets/Game/Characters/Animations/FirstPersonPlayer/Attack_Swing_A.anim";
    const float Fps = 60f;
    const float WindupLength = .16f;

    // A point on the blade's path: grip position, blade direction, and the normal of the blade's flat.
    struct Key
    {
        public float time; public Vector3 grip, blade, flat;
        public Key(float time, Vector3 grip, Vector3 blade, Vector3 flat) { this.time = time; this.grip = grip; this.blade = blade; this.flat = flat; }
    }

    class Swing
    {
        public string name;
        public Key cocked;          // end of the windup, start of the strike
        public Key[] strike;        // the strike and the held follow-through (times from the start of the release)
        public Key dip;             // the arm drops out of view before coming home
        public float home;          // when the arm is back at idle
        public float hitOpen, hitClose;
        public Vector3 elbowHint;   // where the elbow points, from the shoulder
    }

    static readonly Vector3 Up = Vector3.up, Right = Vector3.right;

    // Forehand: sword upright on the right, cuts flat from right to left, held out low on the left,
    // then the arm drops out of view and comes back up to idle.
    static readonly Swing Forehand = new()
    {
        name = "H1",
        cocked = new Key(0f, new Vector3(.26f, -.02f, .36f), new Vector3(.15f, .95f, -.25f), Right),
        strike = new[]
        {
            new Key(.000f, new Vector3(.26f, -.02f, .36f), new Vector3(.15f, .95f, -.25f), Right),
            new Key(.017f, new Vector3(.28f, -.02f, .40f), new Vector3(.85f, .35f, .40f), Up),
            new Key(.050f, new Vector3(.10f, -.05f, .47f), new Vector3(-.30f, .15f, .94f), Up),
            new Key(.083f, new Vector3(-.08f, -.08f, .45f), new Vector3(-.88f, 0f, .47f), Up),
            new Key(.133f, new Vector3(-.16f, -.11f, .41f), new Vector3(-.88f, -.28f, .38f), Up),
            new Key(.320f, new Vector3(-.17f, -.13f, .40f), new Vector3(-.85f, -.38f, .37f), Up),
        },
        dip = new Key(.42f, new Vector3(-.05f, -.40f, .28f), new Vector3(-.30f, -.60f, .75f), Up),
        home = .58f, hitOpen = .010f, hitClose = .100f,
        elbowHint = new Vector3(.35f, -.8f, -.1f),
    };

    // Backhand: cocked over the left shoulder, cuts flat from left to right, held out low on the right, then the same dip home.
    static readonly Swing Backhand = new()
    {
        name = "H2",
        cocked = new Key(0f, new Vector3(-.18f, .00f, .34f), new Vector3(-.55f, .80f, -.25f), Right),
        strike = new[]
        {
            new Key(.000f, new Vector3(-.18f, .00f, .34f), new Vector3(-.55f, .80f, -.25f), Right),
            new Key(.017f, new Vector3(-.14f, -.03f, .40f), new Vector3(-.85f, .30f, .43f), Up),
            new Key(.050f, new Vector3(.06f, -.05f, .47f), new Vector3(.30f, .15f, .94f), Up),
            new Key(.083f, new Vector3(.24f, -.08f, .44f), new Vector3(.88f, 0f, .47f), Up),
            new Key(.133f, new Vector3(.31f, -.11f, .39f), new Vector3(.88f, -.28f, .38f), Up),
            new Key(.320f, new Vector3(.32f, -.13f, .38f), new Vector3(.85f, -.38f, .37f), Up),
        },
        dip = new Key(.42f, new Vector3(.25f, -.40f, .26f), new Vector3(.30f, -.60f, .75f), Up),
        home = .58f, hitOpen = .010f, hitClose = .100f,
        elbowHint = new Vector3(.2f, -.9f, .2f),
    };

    // ── Rig ───────────────────────────────────────────────────────────

    class Rig
    {
        public GameObject instance, root, arms;  // root = the camera frame; arms = the animated mesh (clip paths are relative to it)
        public Transform shoulder, elbow, hand, socket;
        public Transform[] keyed;                // every right-arm transform the clips key
        public Dictionary<Transform, Quaternion> idle = new(), grip = new();
        public void Destroy() => Object.DestroyImmediate(instance);
    }

    static Rig BuildRig()
    {
        // A whole player instance, so the arm mesh skins to the bones we pose. FirstPersonVisuals sits on the
        // camera in play; here it is the camera frame, without the view-model widening.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TablePlayer));
        instance.hideFlags = HideFlags.DontSave;
        instance.transform.position = new Vector3(0f, -500f, 0f);
        Transform visuals = null;
        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) if (t.name == "FirstPersonVisuals") { visuals = t; break; }
        if (visuals == null) { Object.DestroyImmediate(instance); throw new System.Exception("FirstPersonVisuals not found in TablePlayer"); }
        visuals.localScale = Vector3.one;
        var rig = new Rig { instance = instance, root = visuals.gameObject };
        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) if (t.name == "TableArmsOffset") { rig.arms = t.GetComponentInChildren<Animator>(true).gameObject; break; }
        foreach (var t in rig.arms.GetComponentsInChildren<Transform>(true))
            switch (t.name)
            {
                case "Shoulder_R": rig.shoulder = t; break;
                case "Elbow_R": rig.elbow = t; break;
                case "Hand_R": rig.hand = t; break;
                case "RightHand_Socket": rig.socket = t; break;
            }
        var keyed = new List<Transform>();
        foreach (var t in rig.shoulder.parent.GetComponentsInChildren<Transform>(true)) if (t != rig.socket && !t.IsChildOf(rig.socket)) keyed.Add(t);
        rig.keyed = keyed.ToArray();
        AssetDatabase.LoadAssetAtPath<AnimationClip>(GripClip).SampleAnimation(rig.arms, 0f);
        foreach (var t in rig.keyed) rig.grip[t] = t.localRotation;
        AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip).SampleAnimation(rig.arms, 0f);
        foreach (var t in rig.keyed) rig.idle[t] = t.localRotation;
        return rig;
    }

    // Pose: the idle arm with the gripping fingers, then the shoulder, elbow and hand solved toward the key.
    static Dictionary<Transform, Quaternion> Solve(Rig rig, Key key, Vector3 hint)
    {
        foreach (var t in rig.keyed) t.localRotation = rig.idle[t];
        foreach (var t in rig.keyed) if (t != rig.shoulder && t != rig.elbow && t != rig.hand && t.IsChildOf(rig.hand)) t.localRotation = rig.grip[t];
        var cam = rig.root.transform;
        Vector3 blade = cam.TransformDirection(key.blade.normalized);
        Vector3 flat = cam.TransformDirection(Vector3.ProjectOnPlane(key.flat, key.blade).normalized);
        // The socket's up runs along the blade and its right is the flat's normal.
        Quaternion socketRot = Quaternion.LookRotation(Vector3.Cross(flat, blade), blade);
        Quaternion handRot = socketRot * Quaternion.Inverse(rig.socket.localRotation);
        Vector3 handPos = cam.TransformPoint(key.grip) - handRot * Vector3.Scale(rig.socket.localPosition, rig.hand.lossyScale);
        TwoBoneIK(rig.shoulder, rig.elbow, rig.hand, handPos, rig.shoulder.position + cam.TransformDirection(hint));
        rig.hand.rotation = handRot;
        var pose = new Dictionary<Transform, Quaternion>();
        foreach (var t in rig.keyed) pose[t] = t.localRotation;
        return pose;
    }

    // Two-bone IK (as in Animation Rigging): bend the elbow to the target's distance, aim the shoulder at it,
    // then turn the arm about the shoulder-to-target line so the elbow points at the hint.
    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        Vector3 ab = b.position - a.position, bc = c.position - b.position, ac = c.position - a.position, at = target - a.position;
        float abLen = ab.magnitude, bcLen = bc.magnitude;
        float atLen = Mathf.Clamp(at.magnitude, Mathf.Abs(abLen - bcLen) + .001f, abLen + bcLen - .001f);
        float oldAngle = TriangleAngle(ac.magnitude, abLen, bcLen), newAngle = TriangleAngle(atLen, abLen, bcLen);
        Vector3 axis = Vector3.Cross(ab, bc);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(hint - a.position, bc);
        axis.Normalize();
        b.rotation = Quaternion.AngleAxis((oldAngle - newAngle) * Mathf.Rad2Deg, axis) * b.rotation;
        a.rotation = Quaternion.FromToRotation(c.position - a.position, at) * a.rotation;
        Vector3 acNow = c.position - a.position;
        Vector3 abProj = Vector3.ProjectOnPlane(b.position - a.position, acNow), ahProj = Vector3.ProjectOnPlane(hint - a.position, acNow);
        if (abProj.sqrMagnitude > 1e-6f && ahProj.sqrMagnitude > 1e-6f) a.rotation = Quaternion.FromToRotation(abProj, ahProj) * a.rotation;
    }

    static float TriangleAngle(float a, float b, float c) => Mathf.Acos(Mathf.Clamp((b * b + c * c - a * a) / (2f * b * c), -1f, 1f));

    static Key Lerp(Key x, Key y, float t) => new(Mathf.Lerp(x.time, y.time, t), Vector3.Lerp(x.grip, y.grip, t),
        Vector3.Slerp(x.blade.normalized, y.blade.normalized, t), Vector3.Slerp(x.flat, y.flat, t));

    static Key Sample(Key[] keys, float time)
    {
        if (time <= keys[0].time) return keys[0];
        for (int i = 1; i < keys.Length; i++)
            if (time <= keys[i].time)
            {
                float t = Mathf.InverseLerp(keys[i - 1].time, keys[i].time, time);
                // The last segment is the held follow-through: settle into it.
                if (i == keys.Length - 1) t = Mathf.SmoothStep(0f, 1f, t);
                return Lerp(keys[i - 1], keys[i], t);
            }
        return keys[keys.Length - 1];
    }

    static Dictionary<Transform, Quaternion> Blend(Rig rig, Dictionary<Transform, Quaternion> a, Dictionary<Transform, Quaternion> b, float t)
    {
        var pose = new Dictionary<Transform, Quaternion>();
        foreach (var x in rig.keyed) pose[x] = Quaternion.Slerp(a[x], b[x], t);
        return pose;
    }

    // ── Clips ─────────────────────────────────────────────────────────

    static AnimationClip Write(Rig rig, string name, List<(float time, Dictionary<Transform, Quaternion> pose)> frames, bool loop, AnimationEvent[] events, string folder = Folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
        string path = $"{folder}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = Fps;
        foreach (var t in rig.keyed)
        {
            string bonePath = AnimationUtility.CalculateTransformPath(t, rig.arms.transform);
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            Vector3 previous = Vector3.zero;
            for (int i = 0; i < frames.Count; i++)
            {
                Vector3 e = frames[i].pose[t].eulerAngles;
                if (i > 0) for (int k = 0; k < 3; k++) e[k] = previous[k] + Mathf.DeltaAngle(previous[k], e[k]); // keep curves continuous
                previous = e;
                for (int k = 0; k < 3; k++) curves[k].AddKey(new Keyframe(frames[i].time, e[k]));
            }
            for (int k = 0; k < 3; k++)
            {
                for (int i = 0; i < curves[k].length; i++) AnimationUtility.SetKeyLeftTangentMode(curves[k], i, AnimationUtility.TangentMode.ClampedAuto);
                for (int i = 0; i < curves[k].length; i++) AnimationUtility.SetKeyRightTangentMode(curves[k], i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "localEulerAnglesRaw." + "xyz"[k]), curves[k]);
            }
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAnimationEvents(clip, events ?? new AnimationEvent[0]);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimationEvent Event(string function, float time) => new() { functionName = function, time = time };

    static List<(float, Dictionary<Transform, Quaternion>)> Frames(float length, System.Func<float, Dictionary<Transform, Quaternion>> pose)
    {
        var frames = new List<(float, Dictionary<Transform, Quaternion>)>();
        int count = Mathf.RoundToInt(length * Fps);
        for (int i = 0; i <= count; i++) { float t = i / Fps; frames.Add((t, pose(t))); }
        return frames;
    }

    static void BakeSwing(Rig rig, Swing swing)
    {
        var cocked = Solve(rig, swing.cocked, swing.elbowHint);
        // Windup: from idle into the cocked pose, easing in and out.
        Write(rig, $"Attack_Windup_{swing.name}", Frames(WindupLength, t => Blend(rig, rig.idle, cocked, Mathf.SmoothStep(0f, 1f, t / WindupLength))),
            false, new[] { Event("AttackBegin", 0f) });
        // Hold (charging a heavy): the cocked pose, breathing a little.
        var breath = Solve(rig, new Key(0f, swing.cocked.grip + new Vector3(0f, .012f, -.01f), swing.cocked.blade, swing.cocked.flat), swing.elbowHint);
        Write(rig, $"Attack_Hold_{swing.name}", Frames(1.2f, t => Blend(rig, cocked, breath, .5f - .5f * Mathf.Cos(t / 1.2f * Mathf.PI * 2f))), true, null);
        // Release: the strike along the path, the held follow-through, then home to idle.
        float holdEnd = swing.strike[swing.strike.Length - 1].time;
        var dipped = Solve(rig, swing.dip, swing.elbowHint);
        var last = swing.strike[swing.strike.Length - 1];
        Write(rig, $"Attack_Release_{swing.name}", Frames(swing.home, t =>
                t <= holdEnd ? Solve(rig, Sample(swing.strike, t), swing.elbowHint)
                : t <= swing.dip.time ? Solve(rig, Lerp(last, swing.dip, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(holdEnd, swing.dip.time, t))), swing.elbowHint)
                : Blend(rig, dipped, rig.idle, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(swing.dip.time, swing.home, t)))),
            false, new[] { Event("PlaySwingAudio", 0f), Event("EnableHitbox", swing.hitOpen), Event("DisableHitbox", swing.hitClose) });
    }

    [MenuItem("Tools/LoomRoom/Combat/Bake IK Swings")]
    public static void Bake()
    {
        var rig = BuildRig();
        try
        {
            BakeSwing(rig, Forehand);
            BakeSwing(rig, Backhand);
            AssetDatabase.SaveAssets();
            Debug.Log($"IK swings baked to {Folder}");
        }
        finally { rig.Destroy(); }
    }

    // ── Controller ────────────────────────────────────────────────────

    // Adds the IK swing states to the RightArm layer beside the original swings. The animator's SimpleSwings
    // bool (set from CombatManager.simpleSwings) picks which set a new swing uses; SwingIndex picks H1 or H2.
    // Safe to run again: the IK swing states are rebuilt, the original ones only gain a SimpleSwings condition.
    [MenuItem("Tools/LoomRoom/Combat/Wire IK Swings")]
    public static void Wire()
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        Param(ctrl, "SimpleSwings", AnimatorControllerParameterType.Bool);
        Param(ctrl, "SwingSpeed", AnimatorControllerParameterType.Float, 1f);
        var sm = System.Array.Find(ctrl.layers, l => l.name == "RightArm").stateMachine;
        foreach (var child in sm.states) if (child.state.name.EndsWith("_H1") || child.state.name.EndsWith("_H2")) sm.RemoveState(child.state);
        AnimatorState Find(string name) => System.Array.Find(sm.states, s => s.state.name == name).state;
        var idle = Find("Idle"); var heavy = Find("Attack_HeavyRelease"); var hurt = Find("Hurt"); var oldWindup = Find("Attack_Windup");

        // The original swings only start while SimpleSwings is on.
        foreach (var t in idle.transitions) if (t.destinationState == oldWindup) Require(t, "SimpleSwings", AnimatorConditionMode.If);
        foreach (var t in hurt.transitions) if (t.destinationState == oldWindup) Require(t, "SimpleSwings", AnimatorConditionMode.If);
        foreach (var name in new[] { "Attack_Release", "Attack_Release_B" })
            foreach (var t in Find(name).transitions) if (t.destinationState == oldWindup) Require(t, "SimpleSwings", AnimatorConditionMode.If);

        var windups = new AnimatorState[2]; var releases = new AnimatorState[2];
        for (int n = 0; n < 2; n++)
        {
            string id = "H" + (n + 1);
            windups[n] = State(sm, $"Attack_Windup_{id}", "Attack", "SwingSpeed", new Vector3(560, 120 + n * 260), oldWindup.writeDefaultValues);
            var hold = State(sm, $"Attack_Hold_{id}", "Attack", "HoldSpeed", new Vector3(820, 120 + n * 260), oldWindup.writeDefaultValues);
            releases[n] = State(sm, $"Attack_Release_{id}", n == 0 ? "AttackRelease" : "AttackReleaseAlt", "SwingSpeed", new Vector3(820, 200 + n * 260), oldWindup.writeDefaultValues);
            var windup = windups[n]; var release = releases[n];
            Transition(windup, release, .9f, .02f, ("AttackHeld", AnimatorConditionMode.IfNot, 0), ("HeavyStrike", AnimatorConditionMode.IfNot, 0));
            Transition(windup, heavy, .9f, .05f, ("AttackHeld", AnimatorConditionMode.IfNot, 0), ("HeavyStrike", AnimatorConditionMode.If, 0));
            Transition(windup, hold, .95f, .05f);
            Transition(hold, release, -1f, .03f, ("AttackHeld", AnimatorConditionMode.IfNot, 0), ("HeavyStrike", AnimatorConditionMode.IfNot, 0));
            Transition(hold, heavy, -1f, .07f, ("AttackHeld", AnimatorConditionMode.IfNot, 0), ("HeavyStrike", AnimatorConditionMode.If, 0));
        }
        for (int n = 0; n < 2; n++)
        {
            Transition(idle, windups[n], -1f, .06f, ("AttackHeld", AnimatorConditionMode.If, 0), ("SimpleSwings", AnimatorConditionMode.IfNot, 0), ("SwingIndex", AnimatorConditionMode.Equals, n));
            Transition(hurt, windups[n], -1f, .05f, ("AttackHeld", AnimatorConditionMode.If, 0), ("SimpleSwings", AnimatorConditionMode.IfNot, 0), ("SwingIndex", AnimatorConditionMode.Equals, n));
            // From the follow-through straight into the next swing (PlayerCombat holds AttackHeld back until swingInterval).
            for (int m = 0; m < 2; m++)
                Transition(releases[n], windups[m], .6f, .05f, ("AttackHeld", AnimatorConditionMode.If, 0), ("SimpleSwings", AnimatorConditionMode.IfNot, 0), ("SwingIndex", AnimatorConditionMode.Equals, m));
            Transition(releases[n], oldWindup, .6f, .05f, ("AttackHeld", AnimatorConditionMode.If, 0), ("SimpleSwings", AnimatorConditionMode.If, 0));
            Transition(releases[n], idle, .97f, .05f);
        }
        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("IK swings wired into FirstPersonTable.controller (RightArm layer)");
    }

    static void Param(AnimatorController ctrl, string name, AnimatorControllerParameterType type, float value = 0f)
    {
        if (System.Array.Exists(ctrl.parameters, p => p.name == name)) return;
        ctrl.AddParameter(new AnimatorControllerParameter { name = name, type = type, defaultFloat = value });
    }

    static AnimatorState State(AnimatorStateMachine sm, string name, string tag, string speedParameter, Vector3 position, bool writeDefaults)
    {
        var state = sm.AddState(name, position);
        state.motion = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Folder}/{name}.anim");
        state.tag = tag;
        state.speedParameterActive = true;
        state.speedParameter = speedParameter;
        state.writeDefaultValues = writeDefaults; // match the layer's other states
        return state;
    }

    static void Transition(AnimatorState from, AnimatorState to, float exitTime, float duration, params (string param, AnimatorConditionMode mode, float threshold)[] conditions)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = exitTime >= 0f;
        if (exitTime >= 0f) t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration;
        foreach (var c in conditions) t.AddCondition(c.mode, c.threshold, c.param);
    }

    static void Require(AnimatorStateTransition t, string param, AnimatorConditionMode mode)
    {
        foreach (var c in t.conditions) if (c.parameter == param) return;
        t.AddCondition(mode, 0f, param);
    }

    // Positions in camera space at idle and for the forehand's contact key, to check the solve.
    public static string Diagnose()
    {
        var rig = BuildRig();
        try
        {
            var cam = rig.root.transform;
            string P(Transform t) => $"{t.name} {cam.InverseTransformPoint(t.position):F3} scale {t.lossyScale.x:F3}";
            var sb = new System.Text.StringBuilder("idle\n");
            foreach (var t in new[] { rig.shoulder, rig.elbow, rig.hand, rig.socket }) sb.AppendLine(P(t));
            sb.AppendLine($"upper {Vector3.Distance(rig.shoulder.position, rig.elbow.position):F3} fore {Vector3.Distance(rig.elbow.position, rig.hand.position):F3} socket local {rig.socket.localPosition:F3}");
            sb.AppendLine("idle socket up " + cam.InverseTransformDirection(rig.socket.up).ToString("F2") + " right " + cam.InverseTransformDirection(rig.socket.right).ToString("F2"));
            var key = Forehand.strike[2];
            Solve(rig, key, Forehand.elbowHint);
            sb.AppendLine("solved for grip " + key.grip.ToString("F3"));
            foreach (var t in new[] { rig.shoulder, rig.elbow, rig.hand, rig.socket }) sb.AppendLine(P(t));
            sb.AppendLine("socket up " + cam.InverseTransformDirection(rig.socket.up).ToString("F2"));
            var offset = rig.arms.transform.parent;
            sb.AppendLine($"offset {offset.name} local {offset.localPosition:F3} rot {offset.localEulerAngles:F1} scale {offset.localScale:F3}; arms local {rig.arms.transform.localPosition:F3} scale {rig.arms.transform.localScale:F3}");
            return sb.ToString();
        }
        finally { rig.Destroy(); }
    }

    // ── Previews ──────────────────────────────────────────────────────

    // Renders the baked clips from the player's eye into PNG strips in Temp/IkSwings, to check the poses.
    [MenuItem("Tools/LoomRoom/Combat/Preview IK Swings")]
    public static void Preview() => RenderSheets(new[] { "Attack_Windup_H1", "Attack_Release_H1", "Attack_Windup_H2", "Attack_Release_H2" }, Folder,
        AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Weapons/Bronze Sword.asset"), "Temp/IkSwings");

    // Renders clips from the player's eye into PNG strips (8 frames a row) with an item in the right hand, placed as
    // Equipment places it. step: every how many frames (at 60 fps) one is drawn.
    public static void RenderSheets(string[] names, string folder, ItemData item, string output, int step = 1)
    {
        var rig = BuildRig();
        var camGo = new GameObject("Preview camera") { hideFlags = HideFlags.DontSave };
        try
        {
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.SetParent(rig.root.transform, false);
            cam.fieldOfView = 70f; cam.nearClipPlane = .02f; cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .2f, .24f);
            cam.cullingMask = 1 << rig.arms.layer;
            var lightGo = new GameObject("Preview light") { hideFlags = HideFlags.DontSave };
            lightGo.transform.SetParent(rig.root.transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(40f, -30f, 0f);
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            if (item != null && item.worldPrefab != null)
            {
                var obj = (GameObject)PrefabUtility.InstantiatePrefab(item.worldPrefab, rig.socket);
                obj.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
                obj.transform.localScale *= Mathf.Max(.01f, item.heldScale);
                foreach (var t in obj.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = rig.arms.layer;
            }
            // Skinning does not refresh between manual renders in edit mode: draw a baked copy of each skinned mesh.
            var baked = new List<(SkinnedMeshRenderer source, Mesh mesh, Transform copy)>();
            foreach (var smr in rig.arms.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                var copy = new GameObject(smr.name + " (baked)") { hideFlags = HideFlags.DontSave, layer = rig.arms.layer };
                copy.transform.SetParent(rig.root.transform, true);
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
                baked.Add((smr, mesh, copy.transform));
            }
            var rt = new RenderTexture(320, 180, 24); cam.targetTexture = rt;
            Directory.CreateDirectory(output);
            foreach (var name in names)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{folder}/{name}.anim");
                if (clip == null) continue;
                int count = Mathf.RoundToInt(clip.length * Fps) + 1;
                var frames = new List<Texture2D>();
                for (int i = 0; i < count; i += Mathf.Max(1, step))
                {
                    clip.SampleAnimation(rig.arms, i / Fps);
                    foreach (var (source, mesh, copy) in baked)
                    {
                        source.BakeMesh(mesh, true);
                        copy.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    }
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    frames.Add(tex);
                }
                RenderTexture.active = null;
                int cols = 8, rows = (frames.Count + cols - 1) / cols;
                var sheet = new Texture2D(cols * rt.width, rows * rt.height, TextureFormat.RGB24, false);
                for (int i = 0; i < frames.Count; i++)
                {
                    int x = i % cols * rt.width, y = (rows - 1 - i / cols) * rt.height;
                    sheet.SetPixels(x, y, rt.width, rt.height, frames[i].GetPixels());
                    Object.DestroyImmediate(frames[i]);
                }
                sheet.Apply();
                File.WriteAllBytes($"{output}/{name}.png", sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
            }
            Object.DestroyImmediate(rt);
        }
        finally { Object.DestroyImmediate(camGo); rig.Destroy(); }
    }
}

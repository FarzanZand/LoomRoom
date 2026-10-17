using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Tools > LoomRoom > Characters > Convert Unreal Sword Combo: turns the Unreal first-person sword clips
// (Characters/Animations/FirstPersonPlayer/Epic/AS_Character_SwordRightHand_{SwingRight,SwingLeft,StabForward}.FBX, the
// Unreal mannequin's skeleton) into clips for the table player's right arm (Combo/*.anim), so they look in our first
// person as they did in the Unreal game.
// The rotations and timing are the clips' own, frame by frame at 30 fps. The two skeletons point their bones along
// different axes, so a rotation is not copied as a number: once, at the Unreal Idle, each of our bones (collarbone,
// upper arm, forearm, hand) is laid along its Unreal bone (the same direction to its child, the same elbow plane, the
// hand's fingers and palm the same way); that fixes how our bone's axes sit against the Unreal bone's. Every frame,
// our bone then takes the Unreal bone's world rotation through that fixed offset, so it points exactly where the
// Unreal bone points. Our bone lengths stay ours. The fingers keep our own grip.
// First person sees the arms as the Unreal camera did: our camera sits at shoulder height, Unreal's 17 cm above the
// shoulder and 15 cm in front of it, looking 14 degrees down, 90 degrees across. Each clip carries view curves
// (OneBodyRig.ViewCurves) that put our first-person arms where the Unreal camera saw its arms: our shoulder at the
// Unreal shoulder's place in the Unreal view, the arms tilted the same, drawn through the same field of view. They
// only move the first-person arms while a combo clip plays; the rotations stay the clips' own.
// Safe to run again: it overwrites the three clips (keys, events) and nothing else.
public static partial class UnrealComboConverter
{
    const string Source = "Assets/Game/Characters/Animations/FirstPersonPlayer/Epic/AS_Character_SwordRightHand_";
    const string Output = "Assets/Game/Characters/Animations/FirstPersonPlayer/Combo";
    const string PrefabPath = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
    const string ControllerPath = "Assets/Game/Characters/Animations/FirstPersonTable.controller";

    // The Unreal first-person camera in the arms' space, fitted to the reference video's fist positions.
    static readonly Vector3 UnrealCamera = new(0f, 1.67f, .05f);
    const float UnrealCameraPitch = 14f;
    // Then the whole first-person arms turned about the eyes (degrees down, degrees to the right): what was left
    // between our renders and the reference video, the same through every frame of all three clips.
    static readonly Vector2 AimCorrection = new(13f, -12f);
    // The Unreal camera's 90 degrees across, as the vertical field of view of a 16:9 screen, and ours for the arms
    // (PlayerViewPresentation.viewModelFieldOfView): the clips carry the difference.
    const float UnrealArmsFov = 58.72f, OurArmsFov = 70f;
    // Ours: the eyes 1.50 m over the body's root (FirstPersonVisuals at 1.52 over the controller, BodyRig at .02).
    const float EyeHeight = 1.50f;

    public static readonly (string source, string clip)[] Clips =
    {
        ("SwingLeft", "Combo swing left"),
        ("SwingRight", "Combo swing right"),
        ("StabForward", "Combo stab"),
    };

    [MenuItem("Tools/LoomRoom/Characters/Convert Unreal Sword Combo")]
    public static void ConvertAll()
    {
        var log = new System.Text.StringBuilder();
        foreach (var (source, clip) in Clips) log.AppendLine(Convert(source, clip));
        log.AppendLine(Wire());
        Debug.Log("[Combo] " + log);
    }

    // The combo's states on FirstPersonTable's RightArm layer (CombatManager.swingSet Combo plays them through PlayerCombat):
    // one per hit, entered from Any State on ComboStrike with its ComboIndex (so a hit cuts the one before short), back
    // to Idle when it ends, tagged as releases so the game counts them as attacking. Kept as they are when they exist.
    // Holding attack in a combo set does the simple charged attack (PlayerCombat raises AttackHeld only then): its windup
    // takes over from any combo hit, as Attack_Release does for the next simple swing.
    static void HeavyFromCombo(AnimatorStateMachine machine, List<(string, string[])> sets)
    {
        AnimatorState windup = null;
        foreach (var st in machine.states) if (st.state.name == "Attack_Windup") windup = st.state;
        if (windup == null) return;
        foreach (var set in sets)
            foreach (var name in set.Item2)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{set.Item1}/{name}.anim");
                AnimatorState state = null;
                foreach (var st in machine.states) if (clip != null && st.state.motion == clip) state = st.state;
                if (state == null || System.Array.Exists(state.transitions, t => t.destinationState == windup)) continue;
                var t = state.AddTransition(windup);
                t.hasExitTime = false; t.duration = .08f;
                t.AddCondition(AnimatorConditionMode.If, 0f, "AttackHeld");
                t.AddCondition(AnimatorConditionMode.If, 0f, "SimpleSwings");
            }
    }

    public static string Wire()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (System.Array.FindIndex(controller.parameters, p => p.name == "ComboStrike") < 0) controller.AddParameter("ComboStrike", AnimatorControllerParameterType.Trigger);
        if (System.Array.FindIndex(controller.parameters, p => p.name == "ComboIndex") < 0) controller.AddParameter("ComboIndex", AnimatorControllerParameterType.Int);
        if (System.Array.FindIndex(controller.parameters, p => p.name == "ComboSpeed") < 0)
            controller.AddParameter(new AnimatorControllerParameter { name = "ComboSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = .7f });
        var layer = System.Array.Find(controller.layers, l => l.name == "RightArm");
        var machine = layer.stateMachine;
        AnimatorState idle = null;
        foreach (var st in machine.states) if (st.state.name == "Idle") idle = st.state;
        if (System.Array.FindIndex(controller.parameters, p => p.name == "ComboSet") < 0) controller.AddParameter("ComboSet", AnimatorControllerParameterType.Int);
        string[] tags = { "AttackReleaseAlt", "AttackRelease", "AttackRelease" };
        int added = 0;
        // ComboSet 0 the converted combo, 1 Classic, 2 Classic 2.
        var sets = new List<(string, string[])> { (Output, System.Array.ConvertAll(Clips, c => c.clip)) };
        foreach (var style in Styles) sets.Add((style.folder, System.Array.ConvertAll(ComboNames, c => StyleClip(style, c))));
        sets.Add((Combo2Folder, Combo2Names));   // ComboSet 3: Combo 2
        for (int set = 0; set < sets.Count; set++)
            for (int i = 0; i < 3; i++)
            {
                var name = sets[set].Item2[i];
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{sets[set].Item1}/{name}.anim");
                if (clip == null) continue;
                AnimatorState state = null;
                foreach (var st in machine.states) if (st.state.motion == clip) state = st.state;
                if (state == null)
                {
                    state = machine.AddState(name, new Vector3(700f + 260f * set, 400f + 70f * i, 0f));
                    state.motion = clip;
                    state.tag = tags[i];
                    var into = machine.AddAnyStateTransition(state);
                    into.AddCondition(AnimatorConditionMode.If, 0f, "ComboStrike");
                    into.AddCondition(AnimatorConditionMode.Equals, i, "ComboIndex");
                    into.hasExitTime = false; into.duration = .06f; into.canTransitionToSelf = true;
                    if (idle != null)
                    {
                        var back = state.AddTransition(idle);
                        back.hasExitTime = true; back.exitTime = .92f; back.duration = .15f;
                    }
                    added++;
                }
                // Blends: a hit takes over from the one before over 0.12 s (no pop), and eases into Idle over 0.2 s.
                foreach (var t in machine.anyStateTransitions) if (t.destinationState == state) t.duration = .12f;
                foreach (var t in state.transitions) if (t.destinationState == idle) { t.exitTime = .9f; t.duration = .2f; }
                // Its speed follows ComboSpeed (CombatManager.comboSpeed x the attack speed stat), its entry the set.
                state.speedParameterActive = true;
                state.speedParameter = "ComboSpeed";
                foreach (var t in machine.anyStateTransitions)
                    if (t.destinationState == state && System.Array.FindIndex(t.conditions, c => c.parameter == "ComboSet") < 0)
                        t.AddCondition(AnimatorConditionMode.Equals, set, "ComboSet");
            }
        HeavyFromCombo(machine, sets);
        EnsureSwingCameraParameters(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        var clips = new List<AnimationClip>();
        foreach (var set in sets) foreach (var name in set.Item2) clips.Add(AssetDatabase.LoadAssetAtPath<AnimationClip>($"{set.Item1}/{name}.anim"));
        return $"{added} combo states added to RightArm, " + WriteSwingCamera(clips);
    }

    public static string Convert(string sourceName, string clipName)
    {
        string path = Source + sourceName + ".FBX";
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        AnimationClip sourceClip = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is AnimationClip c && !c.name.StartsWith("__preview")) sourceClip = c;
        if (fbx == null || sourceClip == null) return $"{sourceName}: no source";

        var src = Object.Instantiate(fbx);
        var s = Bones(src.transform);
        var body = TargetBody(out var holder);
        var t = Bones(body);
        try
        {
            // The target at rest as in the game, recorded so each frame starts from it.
            RestPose(body);
            var restLocal = new Dictionary<Transform, Quaternion>();
            foreach (var bone in body.GetComponentsInChildren<Transform>(true)) restLocal[bone] = bone.localRotation;

            // The bones that move, Unreal -> ours, parents first.
            var pairs = new (Transform from, Transform to)[]
            {
                (s["clavicle_r"], t["Clavicle_R"]),
                (s["upperarm_r"], t["Shoulder_R"]),
                (s["lowerarm_r"], t["Elbow_R"]),
                (s["hand_r"], t["Hand_R"]),
            };
            // At the Unreal Idle, our bones laid along the Unreal bones, parents first (both skeletons stand at the
            // origin facing the same way); offset[i] takes the Unreal bone's rotation to ours.
            var idle = SourceClip(Source + "Idle.FBX");
            idle.SampleAnimation(src, 0f);
            Transform sIndex = s["index_01_r"], sPinky = s["pinky_01_r"], tIndex = Child(t["Hand_R"], "IndexFinger_01"), tPinky = Child(t["Hand_R"], "Finger_01");
            var offset = new Quaternion[pairs.Length];
            for (int i = 0; i < pairs.Length; i++)
            {
                var (from, to) = pairs[i];
                Quaternion fromFrame, toFrame;
                if (i == 3) { fromFrame = HandFrame(from, sIndex, sPinky); toFrame = HandFrame(to, tIndex, tPinky); }
                else
                {
                    // Along the bone to its child, the second axis from the arm's bend (shoulder, elbow, wrist).
                    var fromNext = pairs[i + 1].from; var toNext = pairs[i + 1].to;
                    var fromBend = Vector3.Cross(pairs[2].from.position - pairs[1].from.position, pairs[3].from.position - pairs[2].from.position);
                    var toBend = Vector3.Cross(pairs[2].to.position - pairs[1].to.position, pairs[3].to.position - pairs[2].to.position);
                    fromFrame = Quaternion.LookRotation(fromNext.position - from.position, fromBend);
                    toFrame = Quaternion.LookRotation(toNext.position - to.position, toBend);
                }
                // Our bone turned so its frame lies on the Unreal one; its children follow for the next bone.
                to.rotation = fromFrame * Quaternion.Inverse(toFrame) * to.rotation;
                offset[i] = Quaternion.Inverse(from.rotation) * to.rotation;
            }

            // The view: in the Unreal camera's frame the shoulder sits at `seen`; our arms are turned by the camera's
            // pitch (as the Unreal arms are under it) and moved so our shoulder sits there too.
            var tilt = Quaternion.Euler(-UnrealCameraPitch, 0f, 0f);
            var seen = tilt * (s["upperarm_r"].position - UnrealCamera);
            var ours = tilt * t["Shoulder_R"].position;   // our arms root sits EyeHeight under the eyes
            var view = seen - (ours - new Vector3(0f, EyeHeight, 0f));

            int frames = Mathf.RoundToInt(sourceClip.length * 30f);
            var keys = new Dictionary<Transform, List<(float time, Quaternion q)>>();
            foreach (var (_, to) in pairs) keys[to] = new List<(float, Quaternion)>();
            var handPath = new List<Vector3>();
            for (int f = 0; f <= frames; f++)
            {
                float time = f / 30f;
                sourceClip.SampleAnimation(src, time);
                foreach (var pair in restLocal) pair.Key.localRotation = pair.Value;
                // Each of our bones points where its Unreal bone points.
                for (int i = 0; i < pairs.Length; i++)
                    pairs[i].to.rotation = pairs[i].from.rotation * offset[i];
                handPath.Add(t["Hand_R"].position);
                foreach (var (_, to) in pairs) keys[to].Add((time, to.localRotation));
            }

            var clip = WriteClip(clipName, body, keys, restLocal, t, sourceClip.length);
            // The arms' root sits EyeHeight under the eyes; turning about the eyes moves it as well.
            var aim = Quaternion.Euler(AimCorrection.x, AimCorrection.y, 0f);
            var below = new Vector3(0f, -EyeHeight, 0f);
            var place = aim * (below + view) - below;
            var turned = (aim * tilt).eulerAngles;
            float A(float a) => Mathf.DeltaAngle(0f, a);
            float[] values = { place.y, place.z, A(turned.x), UnrealArmsFov - OurArmsFov, place.x, A(turned.y), A(turned.z) };
            for (int i = 0; i < values.Length; i++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), OneBodyRig.ViewCurves[i]),
                    AnimationCurve.Constant(0f, sourceClip.length, values[i]));
            EnsureParameters();
            var events = Events(handPath, sourceClip.length);
            AnimationUtility.SetAnimationEvents(clip, events);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return $"{clipName}: {frames + 1} frames, hitbox {events[2].time:0.00}-{events[3].time:0.00} s";
        }
        finally
        {
            Object.DestroyImmediate(src);
            Object.DestroyImmediate(body.gameObject);
            if (holder != null) Object.DestroyImmediate(holder);
        }
    }

    // A hand's frame: forward along the fingers, up out of the back of the hand (the same construction for both rigs).
    static Quaternion HandFrame(Transform hand, Transform index, Transform outer)
    {
        var fingers = (index.position + outer.position) * .5f - hand.position;
        var across = index.position - outer.position;
        return Quaternion.LookRotation(fingers, Vector3.Cross(fingers, across));
    }

    static Transform Child(Transform hand, string prefix)
    {
        foreach (Transform c in hand) if (c.name.StartsWith(prefix)) return c;
        return null;
    }

    // The controller's float parameters the view curves drive (0 for every other clip).
    static void EnsureParameters()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        bool changed = false;
        foreach (var name in OneBodyRig.ViewCurves)
            if (System.Array.FindIndex(controller.parameters, p => p.name == name) < 0) { controller.AddParameter(name, AnimatorControllerParameterType.Float); changed = true; }
        if (changed) { EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); }
    }

    static Dictionary<string, Transform> Bones(Transform root)
    {
        var d = new Dictionary<string, Transform>();
        foreach (var b in root.GetComponentsInChildren<Transform>(true)) d.TryAdd(b.name, b);
        return d;
    }

    static AnimationClip SourceClip(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }

    // The table player's body (BodyRig) on its own at the origin: bones, meshes and the animator.
    static Transform TargetBody(out GameObject holder)
    {
        holder = new GameObject("combo convert") { hideFlags = HideFlags.HideAndDontSave };
        holder.SetActive(false);
        var copy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), holder.transform);
        Transform body = null;
        foreach (var b in copy.GetComponentsInChildren<Transform>(true)) if (b.name == "BodyRig") body = b;
        body.SetParent(null, false);
        body.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        body.gameObject.hideFlags = HideFlags.HideAndDontSave;
        foreach (var c in body.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(c);
        Object.DestroyImmediate(copy);
        return body;
    }

    // Every override layer's resting clip (its Idle, else its default state) played once on the body.
    static void RestPose(Transform body)
    {
        var animator = body.GetComponent<Animator>();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var graph = PlayableGraph.Create("combo rest");
        try
        {
            var output = AnimationPlayableOutput.Create(graph, "rest", animator);
            var layers = new List<(AnimationClip, AvatarMask)>();
            foreach (var layer in controller.layers)
            {
                if (layer.blendingMode != AnimatorLayerBlendingMode.Override || layer.defaultWeight <= 0f) continue;
                AnimatorState state = null;
                foreach (var st in layer.stateMachine.states) if (st.state.name == "Idle") state = st.state;
                if (state == null) state = layer.stateMachine.defaultState;
                var clip = First(state != null ? state.motion : null);
                if (clip != null) layers.Add((clip, layer.avatarMask));
            }
            var mixer = AnimationLayerMixerPlayable.Create(graph, layers.Count);
            for (int i = 0; i < layers.Count; i++)
            {
                var p = AnimationClipPlayable.Create(graph, layers[i].Item1);
                graph.Connect(p, 0, mixer, i);
                mixer.SetInputWeight(i, 1f);
                if (layers[i].Item2 != null) mixer.SetLayerMaskFromAvatarMask((uint)i, layers[i].Item2);
            }
            output.SetSourcePlayable(mixer);
            graph.Evaluate(0f);
        }
        finally { graph.Destroy(); }
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

    // Rotation keys for the arm, and the right hand's fingers held at their resting grip.
    static AnimationClip WriteClip(string name, Transform body, Dictionary<Transform, List<(float time, Quaternion q)>> keys,
        Dictionary<Transform, Quaternion> rest, Dictionary<string, Transform> bones, float length)
    {
        System.IO.Directory.CreateDirectory(Output);
        string path = $"{Output}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip { name = name }; AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = 30f;
        foreach (var pair in keys) Keys(clip, body, pair.Key, pair.Value);
        foreach (var finger in bones["Hand_R"].GetComponentsInChildren<Transform>(true))
        {
            if (finger == bones["Hand_R"] || finger.name.Contains("Socket") || finger.IsChildOf(Socket(bones))) continue;
            Keys(clip, body, finger, new List<(float, Quaternion)> { (0f, rest[finger]), (length, rest[finger]) });
        }
        clip.EnsureQuaternionContinuity();
        return clip;
    }

    static Transform Socket(Dictionary<string, Transform> bones) => bones.TryGetValue("RightHand_Socket", out var s) ? s : bones["Hand_R"];

    static void Keys(AnimationClip clip, Transform body, Transform bone, List<(float time, Quaternion q)> keys)
    {
        string path = AnimationUtility.CalculateTransformPath(bone, body);
        var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        Quaternion last = keys[0].q;
        foreach (var (time, raw) in keys)
        {
            var q = Quaternion.Dot(raw, last) < 0f ? new Quaternion(-raw.x, -raw.y, -raw.z, -raw.w) : raw;
            last = q;
            curves[0].AddKey(time, q.x); curves[1].AddKey(time, q.y); curves[2].AddKey(time, q.z); curves[3].AddKey(time, q.w);
        }
        string[] names = { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        for (int i = 0; i < 4; i++) clip.SetCurve(path, typeof(Transform), names[i], curves[i]);
    }

    // The swing's events, as the other attack clips have them: AttackBegin at the start, the whoosh just before the
    // blade's fastest stretch, the hitbox open over it (the hand moving faster than half its peak speed).
    static AnimationEvent[] Events(List<Vector3> hand, float length)
    {
        var speed = new float[hand.Count];
        float peak = 0f; int at = 0;
        for (int i = 1; i < hand.Count; i++) { speed[i] = (hand[i] - hand[i - 1]).magnitude; if (speed[i] > peak) { peak = speed[i]; at = i; } }
        int open = at, close = at;
        while (open > 1 && speed[open - 1] > peak * .5f) open--;
        while (close < hand.Count - 1 && speed[close + 1] > peak * .5f) close++;
        float tOpen = (open - 1) / 30f, tClose = close / 30f;
        return new[]
        {
            new AnimationEvent { functionName = "AttackBegin", time = 0f },
            new AnimationEvent { functionName = "PlaySwingAudio", time = Mathf.Max(0f, tOpen - 2f / 30f) },
            new AnimationEvent { functionName = "EnableHitbox", time = tOpen },
            new AnimationEvent { functionName = "DisableHitbox", time = Mathf.Min(length, tClose) },
        };
    }
}

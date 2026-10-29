using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The humanoid enemies' fighting stance and footwork, from an eval: EnemyGuardSetup.Build() (safe to run again; it writes
// the clips again from the numbers below, so edit those or the clips by hand after).
// - ThirdPerson/Combat/Combat Guard pose.anim: the held stance (the idle crouched toward Attack1's first frame, the swing's
//   own start, with the arms set by Pose); Combat Guard.anim: the same, breathing (Breath).
// - Anim masks/Guard arms.mask: arms, hands and head.
// - HumanoidController: layers "Guard stance" (whole body) and "Guard arms" (the mask) under the gestures, each holding
//   Combat Guard at weight 0; EnemyGuard on the Humanoid enemy base sets their weights while the enemy fights.
// - Footwork on every humanoid fighter's own behaviour settings (casters that hold their spot are left as they are).
public static class EnemyGuardSetup
{
    const string Anim = "Assets/Game/Characters/Animations/";
    const string Controller = Anim + "HumanoidController.controller";
    const string Attack = Anim + "ThirdPerson/Combat/Attack1.anim";
    const string PosePath = Anim + "ThirdPerson/Combat/Combat Guard pose.anim";
    const string GuardPath = Anim + "ThirdPerson/Combat/Combat Guard.anim";
    const string MaskPath = Anim + "Anim masks/Guard arms.mask";
    const string ArmsPath = Anim + "ThirdPerson/Combat/Combat Guard arms additive.anim";
    const string Base = "Assets/Game/Characters/Enemies/Base/Humanoid enemy base.prefab";

    // The stance: the relaxed idle's muscles moved toward Attack1's first frame (its crouch and spread feet) by Crouch for
    // the legs, hips and spine, the arms kept from the idle, then the numbers in Pose on top ("Muscle:=set", "Muscle=added"); the arms' numbers come from
    // SolveArms (run on a Crypt Soldier in Play mode), edited by hand after.
    public static float Crouch = .5f;
    // Where the arms come from: the idle (below 0), or Attack1 at this share of its length (its ready positions).
    public static float ArmsFrom = -1f;
    public static string Pose =
        "Head Nod Down-Up:=.05;" +
        "Right Arm Down-Up:=-.269;Right Arm Front-Back:=-.622;Right Arm Twist In-Out:=.166;Right Forearm Stretch:=.672;Right Forearm Twist In-Out:=-.12;Right Hand Down-Up:=.569;Right Hand In-Out:=.441;" +
        "Left Arm Down-Up:=-.446;Left Arm Front-Back:=-.086;Left Arm Twist In-Out:=.303;Left Forearm Stretch:=.088;Left Forearm Twist In-Out:=-.413;Left Hand Down-Up:=.647;Left Hand In-Out:=-.981";
    const string Idle = Anim + "ThirdPerson/Locomotion/Idle.anim";

    // Slow waves laid over the pose (MainMenuSetTools.MakeIdle: "Muscle=amplitude@period@phase", periods dividing Seconds).
    // The stance lives: a breath (chest, shoulders, head), a knee bounce (the hips dip; the feet stay pinned by the state's
    // foot IK, so the knees flex), a slow weight shift side to side with the hips, and the weapon and off hand bobbing
    // with it, never in step with each other.
    public static string Breath =
        "Chest Front-Back=.09@4; UpperChest Front-Back=.06@4@.1; Spine Front-Back=.05@4@.2; Head Nod Down-Up=-.07@4@.15;" +
        "Left Shoulder Down-Up=.09@4@.05; Right Shoulder Down-Up=.09@4@.05;" +
        "RootT.y=.03@2@.1; RootT.x=.04@4@.35; Spine Left-Right=.08@4@.35; Chest Left-Right=-.06@4@.4;" +
        "Spine Twist Left-Right=.1@4@.6; Chest Twist Left-Right=.07@4@.65; Head Turn Left-Right=.1@4@.85;" +
        "Right Arm Down-Up=.1@2@.55; Right Arm Front-Back=.08@4@.2; Right Forearm Stretch=.1@2@.6; Right Hand Down-Up=.16@2@.7; Right Hand In-Out=.1@4@.3;" +
        "Left Arm Down-Up=.09@2@.3; Left Arm Front-Back=.07@4@.75; Left Forearm Stretch=.08@2@.35; Left Hand Down-Up=.12@2@.45";
    public const float Seconds = 4f;
    public static float SwingExit = .35f;

    // Footwork on the humanoid fighters' behaviour.
    public static float CircleSpeed = .22f, Sway = .35f, StepBack = .5f, StepBackSpeed = .25f, Pace = .75f;
    public static Vector2 SwitchEvery = new(1.8f, 3.5f);

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        Clips(); log.Append("clips; ");
        var mask = Mask(); log.Append("mask; ");
        log.Append(Layers(mask));
        log.Append(Component());
        log.Append(Footwork());
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // Only the clips again (for tuning Pose and Breath).
    public static string Clips()
    {
        WritePose();
        SwingFromGuard();
        MainMenuSetTools.MakeIdle(PosePath, GuardPath, Breath, Seconds, .2f);
        // The arms layer adds the guard on top of whatever the legs and body are doing (walking, strafing): the same clip,
        // measured against the relaxed idle's first frame, so only the difference (arms up, weapon forward) is added and
        // the walk's own arm swing and torso turn still show through.
        var arms = Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>(GuardPath));
        arms.name = System.IO.Path.GetFileNameWithoutExtension(ArmsPath);
        AnimationUtility.SetAdditiveReferencePose(arms, AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle), 0f);
        var had = AssetDatabase.LoadAssetAtPath<AnimationClip>(ArmsPath);
        if (had == null) AssetDatabase.CreateAsset(arms, ArmsPath);
        else { EditorUtility.CopySerialized(arms, had); had.name = arms.name; EditorUtility.SetDirty(had); Object.DestroyImmediate(arms); }
        AssetDatabase.SaveAssets();
        // A running animator keeps a clip's old pose until it is imported again.
        AssetDatabase.ImportAsset(PosePath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(GuardPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(ArmsPath, ImportAssetOptions.ForceUpdate);
        return "clips";
    }

    // Attack1's opening rewritten to start from the guard: its first OpeningShare of the clip (where the source held the
    // weapon low and wide before the windup) becomes a move from the guard pose straight to the source's own pose at
    // that point; everything after, the hit frame and its events included, is the source's. The source is kept as
    // "Attack1 source.anim" (made from Attack1 the first time), so this can run again.
    public static float OpeningShare = .16f;
    const string AttackSource = Anim + "ThirdPerson/Combat/Attack1 source.anim";
    static void SwingFromGuard()
    {
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackSource);
        if (source == null)
        {
            AssetDatabase.CopyAsset(Attack, AttackSource);
            source = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackSource);
        }
        var guard = PoseValues(Pose);
        var clip = Object.Instantiate(source);
        float end = OpeningShare * source.length;
        foreach (var b in AnimationUtility.GetCurveBindings(source))
        {
            if (b.type != typeof(Animator) || !guard.TryGetValue(b.propertyName, out float from)) continue;
            var curve = AnimationUtility.GetEditorCurve(source, b);
            float to = curve.Evaluate(end);
            float slope = (curve.Evaluate(end + .02f) - to) / .02f;
            var keys = new List<Keyframe> { new Keyframe(0f, from, 0f, 0f), new Keyframe(end, to, (to - from) / end, slope) };
            foreach (var k in curve.keys) if (k.time > end + 1e-4f) keys.Add(k);
            AnimationUtility.SetEditorCurve(clip, b, new AnimationCurve(keys.ToArray()));
        }
        AnimationUtility.SetAnimationEvents(clip, AnimationUtility.GetAnimationEvents(source));
        var target = AssetDatabase.LoadAssetAtPath<AnimationClip>(Attack);
        EditorUtility.CopySerialized(clip, target);
        target.name = System.IO.Path.GetFileNameWithoutExtension(Attack);
        EditorUtility.SetDirty(target);
        Object.DestroyImmediate(clip);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(Attack, ImportAssetOptions.ForceUpdate);
    }

    // The held stance (see Crouch and Pose), every muscle and the body's place keyed flat over Seconds.
    static void WritePose()
    {
        var clip = PoseClip(PoseValues(Pose));
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(PosePath);
        if (existing == null) AssetDatabase.CreateAsset(clip, PosePath);
        else { EditorUtility.CopySerialized(clip, existing); existing.name = clip.name; EditorUtility.SetDirty(existing); }
        AssetDatabase.SaveAssets();
    }

    static Dictionary<string, float> PoseValues(string pose)
    {
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle);
        var attack = AssetDatabase.LoadAssetAtPath<AnimationClip>(AttackSource) ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(Attack);
        var values = new Dictionary<string, float>();
        foreach (var b in AnimationUtility.GetCurveBindings(idle))
            if (b.type == typeof(Animator)) values[b.propertyName] = AnimationUtility.GetEditorCurve(idle, b).Evaluate(0f);
        foreach (var b in AnimationUtility.GetCurveBindings(attack))
        {
            if (b.type != typeof(Animator)) continue;
            string n = b.propertyName;
            bool lower = n.StartsWith("Root") || n.Contains("Leg") || n.Contains("Foot") || n.Contains("Toes") || n.StartsWith("Spine") || n.StartsWith("Chest") || n.StartsWith("UpperChest");
            if (!lower)
            {
                if (ArmsFrom >= 0f && !n.StartsWith("Head") && !n.StartsWith("Neck") && !n.StartsWith("Jaw") && !n.Contains("Eye"))
                    values[n] = AnimationUtility.GetEditorCurve(attack, b).Evaluate(ArmsFrom * attack.length);
                continue;
            }
            float a = AnimationUtility.GetEditorCurve(attack, b).Evaluate(0f);
            values[n] = values.TryGetValue(n, out var v) ? Mathf.Lerp(v, a, Crouch) : a;
        }
        foreach (string part in pose.Split(';'))
        {
            string p = part.Trim();
            if (p.Length == 0) continue;
            bool set = p.Contains(":=");
            var kv = set ? p.Split(new[] { ":=" }, System.StringSplitOptions.None) : p.Split('=');
            float x = float.Parse(kv[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            string key = kv[0].Trim();
            values[key] = set ? x : (values.TryGetValue(key, out var v) ? v : 0f) + x;
        }
        return values;
    }

    static AnimationClip PoseClip(Dictionary<string, float> values)
    {
        var clip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(PosePath) };
        foreach (var kv in values)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), new AnimationCurve(new Keyframe(0f, kv.Value), new Keyframe(Seconds, kv.Value)));
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    // ── The arms placed by search ────────────────────────────────────

    // Searches both arms' muscles on a model of the enemy (in Play mode or a scene) until the weapon hand sits in front of
    // the chest a little to its side with the weapon's tip pointing up and forward, and the off hand forward and lower,
    // fingers up, both elbows down and in (where two-bone IK would put them). Positions are in the model's own frame from
    // its chest: right, up, forward (metres). Returns the arms as Pose numbers ("Muscle:=v;...") and sets Pose to them.
    public static Vector3 WeaponHand = new(.14f, -.08f, .42f), OffHand = new(-.12f, -.2f, .36f);
    public static Vector3 WeaponTip = new(0f, .75f, .65f);
    public static string SolveArms(Animator animator, string keep = "Head Nod Down-Up:=.05")
    {
        string[] r = { "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out", "Right Forearm Stretch", "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out" };
        string[] l = { "Left Arm Down-Up", "Left Arm Front-Back", "Left Arm Twist In-Out", "Left Forearm Stretch", "Left Forearm Twist In-Out", "Left Hand Down-Up", "Left Hand In-Out" };
        var names = r.Concat(l).ToArray();
        var start = PoseValues(keep);
        var x = names.Select(n => start.TryGetValue(n, out var v) ? v : 0f).ToArray();
        string Numbers(float[] v)
        {
            var sb = new System.Text.StringBuilder(keep);
            for (int i = 0; i < names.Length; i++) sb.Append(';').Append(names[i]).Append(":=").Append(v[i].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }
        void Show(float[] v) => MainMenuSetTools.PreviewPose(animator, PoseClip(PoseValues(Numbers(v))), "Idle");
        Show(x);
        var t = animator.transform;
        var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        Vector3 At(Vector3 local) => chest.position + t.right * local.x + t.up * local.y + t.forward * local.z;
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        var rU = B(HumanBodyBones.RightUpperArm); var rL = B(HumanBodyBones.RightLowerArm); var rH = B(HumanBodyBones.RightHand);
        var lU = B(HumanBodyBones.LeftUpperArm); var lL = B(HumanBodyBones.LeftLowerArm); var lH = B(HumanBodyBones.LeftHand);
        var lM = B(HumanBodyBones.LeftMiddleProximal);
        // The weapon: the longest renderer under the right hand; its tip is the corner of its bounds farthest from the hand.
        Renderer weapon = rH.GetComponentsInChildren<Renderer>(true).OrderByDescending(w => w.bounds.size.magnitude).FirstOrDefault();
        Vector3 Tip()
        {
            if (weapon == null) return rH.position + t.up;
            var bb = weapon.bounds; Vector3 best = bb.center; float far = -1f;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? bb.min.x : bb.max.x, (i & 2) == 0 ? bb.min.y : bb.max.y, (i & 4) == 0 ? bb.min.z : bb.max.z);
                float d = (c - rH.position).sqrMagnitude; if (d > far) { far = d; best = c; }
            }
            return best;
        }
        Vector3 Elbow(Transform u, Transform lo, Transform h, Vector3 wrist, Vector3 pole)
        {
            float a = Vector3.Distance(u.position, lo.position), b = Vector3.Distance(lo.position, h.position);
            float c = Mathf.Clamp(Vector3.Distance(u.position, wrist), Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
            Vector3 dir = (wrist - u.position).normalized;
            float along = (a * a - b * b + c * c) / (2f * c);
            return u.position + dir * along + Vector3.ProjectOnPlane(pole - u.position, dir).normalized * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        }
        Vector3 rTarget = At(WeaponHand), lTarget = At(OffHand);
        Vector3 rElbow = Elbow(rU, rL, rH, rTarget, rU.position - t.up * .5f + t.right * .25f - t.forward * .1f);
        Vector3 lElbow = Elbow(lU, lL, lH, lTarget, lU.position - t.up * .5f - t.right * .25f - t.forward * .1f);
        float scale = Vector3.Distance(rU.position, rL.position) + Vector3.Distance(rL.position, rH.position);
        Vector3 tipDir = (t.right * WeaponTip.x + t.up * WeaponTip.y + t.forward * WeaponTip.z).normalized;
        bool place = true;
        float Cost(float[] v)
        {
            Show(v);
            float c = ((rH.position - rTarget).sqrMagnitude + (lH.position - lTarget).sqrMagnitude) / (scale * scale) * 40f
                    + ((rL.position - rElbow).sqrMagnitude + (lL.position - lElbow).sqrMagnitude) / (scale * scale) * 10f;
            if (place) return c;
            c += (1f - Vector3.Dot((Tip() - rH.position).normalized, tipDir)) * 4f;
            c += (1f - Vector3.Dot((lM.position - lH.position).normalized, (t.up * .8f + t.forward * .6f).normalized)) * 2f;
            return c;
        }
        float Search(int[] which)
        {
            float best = Cost(x);
            for (float step = .3f; step > .01f; step *= .5f)
                for (int pass = 0; pass < 3; pass++)
                {
                    bool moved = false;
                    foreach (int i in which)
                        foreach (float sign in new[] { 1f, -1f })
                        {
                            float old = x[i];
                            x[i] = Mathf.Clamp(old + sign * step, -1f, 1f);
                            float c = Cost(x);
                            if (c < best - 1e-5f) { best = c; moved = true; } else x[i] = old;
                        }
                    if (!moved) break;
                }
            return best;
        }
        place = true; Search(new[] { 0, 1, 2, 3, 7, 8, 9, 10 });
        place = false; float cost = Search(new[] { 4, 5, 6, 11, 12, 13, 0, 1, 2, 3, 7, 8, 9, 10 });
        Pose = Numbers(x);
        Debug.Log($"[Guard] arms cost {cost:0.000}: {Pose}");
        return Pose;
    }

    static AvatarMask Mask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, MaskPath); }
        var on = new HashSet<AvatarMaskBodyPart> { AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK };
        for (var part = AvatarMaskBodyPart.Root; part < AvatarMaskBodyPart.LastBodyPart; part++) mask.SetHumanoidBodyPartActive(part, on.Contains(part));
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static string Layers(AvatarMask mask)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        var guard = AssetDatabase.LoadAssetAtPath<AnimationClip>(GuardPath);
        var layers = controller.layers.ToList();
        string made = "";
        foreach (var (name, m) in new[] { ("Guard stance", (AvatarMask)null), ("Guard arms", mask) })
        {
            int at = layers.FindIndex(l => l.name == name);
            if (at < 0)
            {
                var sm = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(sm, controller);
                var state = sm.AddState("Guard");
                sm.defaultState = state;
                var layer = new AnimatorControllerLayer { name = name, stateMachine = sm, defaultWeight = 0f, blendingMode = AnimatorLayerBlendingMode.Override, avatarMask = m };
                // Under the gestures (FullBodyOverride and the arm layers come after, so a cast or a bow beats the guard).
                int gestures = layers.FindIndex(l => l.name == "FullBodyOverride");
                layers.Insert(gestures < 0 ? layers.Count : gestures, layer);
                made += name + " added; ";
            }
            var l2 = layers.First(l => l.name == name);
            l2.avatarMask = m;
            l2.blendingMode = m == null ? AnimatorLayerBlendingMode.Override : AnimatorLayerBlendingMode.Additive;
            // Foot IK pins the feet where the clip stands them (a humanoid clip keeps the body's centre of mass, so a
            // breathing spine would slide planted feet). Only on the whole-body stance: on the arms layer, whose mask
            // leaves the legs out, foot IK pulled the walking feet up toward a body it never placed (they hung 0.8 m up).
            var motion = m == null ? guard : AssetDatabase.LoadAssetAtPath<AnimationClip>(ArmsPath);
            foreach (var s in l2.stateMachine.states) if (s.state.name == "Guard") { s.state.motion = motion; s.state.writeDefaultValues = true; s.state.iKOnFeet = m == null; }
        }
        // Out of a swing the body rises from its lunge over SwingExit seconds (0.1 snapped it upright in two frames).
        foreach (var s in layers[0].stateMachine.states)
            if (s.state.name == "Attack1")
                foreach (var t in s.state.transitions) { t.duration = SwingExit; t.hasFixedDuration = true; made += "swing exit eased; "; }
        // The cast stands planted the same way: its spine bends through the cast and its feet slid with it (casters
        // shuffled while casting).
        foreach (var l in layers)
            foreach (var s in l.stateMachine.states)
                if (s.state.motion != null && s.state.motion.name == "CastSpell") { s.state.iKOnFeet = true; made += "cast feet pinned; "; }
        controller.layers = layers.ToArray();
        EditorUtility.SetDirty(controller);
        return made.Length > 0 ? made : "layers kept; ";
    }

    static string Component()
    {
        var root = PrefabUtility.LoadPrefabContents(Base);
        try
        {
            if (root.GetComponent<EnemyGuard>() != null) return "guard kept; ";
            root.AddComponent<EnemyGuard>();
            PrefabUtility.SaveAsPrefabAsset(root, Base);
            return "guard added; ";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // Every enemy on the humanoid rig that fights in melee: it circles at a speed the strafe plays at, steps in and back,
    // and steps back after a swing. Casters that hold their spot (no melee attack) keep theirs.
    static string Footwork()
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(Controller);
        var names = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Characters/Enemies" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.Contains("/_Archive/") || path.Contains("/Base/")) continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var character = prefab != null ? prefab.GetComponent<Character>() : null;
            var anim = prefab != null ? prefab.GetComponentInChildren<Animator>(true) : null;
            if (character == null || !(character.data is EnemyData data) || anim == null || anim.runtimeAnimatorController != controller) continue;
            if (data.behaviour != null) continue;   // a shared profile: tuned on its own asset
            bool melee = data.attacks != null && data.attacks.Any(a => a != null && !a.IsRanged);
            if (!melee || data.caster && !data.closesIn) continue;   // a caster that holds its spot (the Acolyte) keeps it
            var b = data.behaviourSettings;
            b.circleTarget = true;
            b.circleSpeedFraction = Mathf.Max(b.circleSpeedFraction, CircleSpeed);
            b.circleSwitchInterval = SwitchEvery;
            b.footworkPace = Pace;
            b.spacingSway = Sway;
            b.stepBackAfterSwing = StepBack;
            b.stepBackSpeedFraction = StepBackSpeed;
            EditorUtility.SetDirty(data);
            names.Add(prefab.name);
        }
        return "footwork: " + string.Join(", ", names) + "; ";
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The humanoid enemies' fighting moves and footwork, from an eval: EnemyGuardSetup.Build() (safe to run again).
// - The Mixamo sword-and-shield clips in ThirdPerson/Combat/Sword and shield, imported with their original orientation:
//   so each one travels straight along its own line (walk forward, walk back, strafe right and left, run) while the body
//   stands bladed, shield shoulder toward the target, as it was played.
// - HumanoidController: the "Guard stance" layer (whole body, under the gestures, Foot IK on) holds "Sword and shield
//   moves", a 2D blend on CombatX/CombatZ (velocity in the body's own frame per metre of its height) with the block idle
//   in the middle and each moving clip at its own measured speed (Speeds), so the feet keep pace with the body.
//   EnemyGuard on the Humanoid enemy base sets the parameters and the layer's weight while the enemy fights.
// - Attack1's opening starts from the block idle's pose (its source is kept as Attack1 source.anim).
// - Footwork on every humanoid fighter's own behaviour settings (casters that hold their spot are left as they are),
//   paced to the strafes.
public static class EnemyGuardSetup
{
    const string Anim = "Assets/Game/Characters/Animations/";
    const string Controller = Anim + "HumanoidController.controller";
    const string Attack = Anim + "ThirdPerson/Combat/Attack1.anim";
    const string Clips = Anim + "ThirdPerson/Combat/Sword and shield/";
    const string Base = "Assets/Game/Characters/Enemies/Base/Humanoid enemy base.prefab";
    const string BlockIdle = "sword and shield block idle";
    const string TreeName = "Sword and shield moves";

    // Each moving clip's travel in metres a second per metre of the body's height (measured: the root's travel over the
    // clip divided by its length, Animator.humanScale and the model's scale), the blend's position for it.
    static readonly (string clip, Vector2 at)[] Speeds =
    {
        ("sword and shield walk", new Vector2(0f, EnemyGuard.StrideForward)),
        ("sword and shield walk (2)", new Vector2(0f, -EnemyGuard.StrideBack)),
        ("sword and shield strafe", new Vector2(EnemyGuard.StrideRight, 0f)),
        ("sword and shield strafe (2)", new Vector2(-EnemyGuard.StrideLeft, 0f)),
        ("sword and shield run", new Vector2(0f, EnemyGuard.RunForward)),
    };

    public static float SwingExit = .35f;

    // Footwork on the humanoid fighters' behaviour. Pace is close to the strafes' own speed (1.14-1.29 a metre of
    // height), so circling plays them nearly whole (at half their speed the legs took half steps).
    public static float CircleSpeed = .22f, Sway = .35f, StepBack = .5f, StepBackSpeed = .25f, Pace = 1.1f;
    public static Vector2 SwitchEvery = new(1.8f, 3.5f);

    public static string Build()
    {
        var log = new System.Text.StringBuilder();
        log.Append(Import());
        SwingFromGuard(); log.Append("swing from guard; ");
        log.Append(Layers());
        log.Append(Component());
        log.Append(Footwork());
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static AnimationClip Clip(string name)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Clips + name + ".fbx"))
            if (o is AnimationClip c && !c.name.StartsWith("__")) return c;
        Debug.LogError("[Guard] no clip " + name);
        return null;
    }

    // Original orientation (a strafe goes sideways with the body bladed, not turned to face along its travel), looping.
    static string Import()
    {
        int changed = 0;
        foreach (var name in Speeds.Select(s => s.clip).Append(BlockIdle))
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(Clips + name + ".fbx");
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            bool dirty = false;
            foreach (var c in clips)
            {
                if (!c.keepOriginalOrientation || !c.loopTime || !c.loopPose) dirty = true;
                c.keepOriginalOrientation = c.loopTime = c.loopPose = true;
            }
            if (!dirty) continue;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            changed++;
        }
        return changed > 0 ? changed + " clips imported again; " : "clips kept; ";
    }

    // Attack1's opening rewritten to start from the block idle: its first OpeningShare of the clip (where the source held
    // the weapon low and wide before the windup) becomes a move from the guard's muscles straight to the source's own pose
    // at that point; everything after, the hit frame and its events included, is the source's. The body's turn (the
    // guard's bladed stance) is handed over by EnemyGuard's layer weight across the same frames. The source is kept as
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
        var idle = Clip(BlockIdle);
        var guard = new Dictionary<string, float>();
        foreach (var b in AnimationUtility.GetCurveBindings(idle))
            if (b.type == typeof(Animator) && !b.propertyName.StartsWith("Root") && !b.propertyName.Contains("Hand.") && !b.propertyName.Contains("Foot."))
                guard[b.propertyName] = AnimationUtility.GetEditorCurve(idle, b).Evaluate(0f);
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

    static string Layers()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Controller);
        string made = "";
        foreach (var p in new[] { "CombatX", "CombatZ" })
            if (!controller.parameters.Any(x => x.name == p)) { controller.AddParameter(p, AnimatorControllerParameterType.Float); made += p + " added; "; }

        // The blend tree, kept inside the controller.
        var tree = AssetDatabase.LoadAllAssetsAtPath(Controller).OfType<BlendTree>().FirstOrDefault(t => t.name == TreeName);
        if (tree == null)
        {
            tree = new BlendTree { name = TreeName, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
            made += "moves made; ";
        }
        tree.blendType = BlendTreeType.FreeformDirectional2D;
        tree.blendParameter = "CombatX";
        tree.blendParameterY = "CombatZ";
        tree.useAutomaticThresholds = false;
        var children = new List<ChildMotion> { new ChildMotion { motion = Clip(BlockIdle), position = Vector2.zero, timeScale = 1f } };
        foreach (var (name, at) in Speeds) children.Add(new ChildMotion { motion = Clip(name), position = at, timeScale = 1f });
        tree.children = children.ToArray();
        EditorUtility.SetDirty(tree);

        var layers = controller.layers.ToList();
        int stance = layers.FindIndex(l => l.name == "Guard stance");
        if (stance < 0)
        {
            var sm = new AnimatorStateMachine { name = "Guard stance", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            sm.defaultState = sm.AddState("Guard");
            // Under the gestures (FullBodyOverride and the arm layers come after, so a cast or a bow beats the guard).
            int gestures = layers.FindIndex(l => l.name == "FullBodyOverride");
            stance = gestures < 0 ? layers.Count : gestures;
            layers.Insert(stance, new AnimatorControllerLayer { name = "Guard stance", stateMachine = sm, defaultWeight = 0f });
            made += "Guard stance added; ";
        }
        var layer = layers[stance];
        layer.avatarMask = null;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        // Its IK pass turns the chest and head toward the target (EnemyGuard.Aim): the clips stand side-on.
        layer.iKPass = true;
        // Foot IK holds the feet where the clips plant them (a humanoid clip keeps the body's centre of mass, so a blend of
        // two stepping clips would slide them).
        // GuardPace plays the moves slower when the body moves slower than a whole stride (EnemyGuard.StrideMatch).
        if (!controller.parameters.Any(p => p.name == "GuardPace"))
            controller.AddParameter(new AnimatorControllerParameter { name = "GuardPace", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        foreach (var s in layer.stateMachine.states)
            if (s.state.name == "Guard")
            {
                s.state.motion = tree; s.state.writeDefaultValues = true; s.state.iKOnFeet = true;
                s.state.speedParameter = "GuardPace"; s.state.speedParameterActive = true;
            }
        layers[stance] = layer;

        // The old arms-only layer (a hand-made guard laid over the walk) is replaced by the moves.
        int arms = layers.FindIndex(l => l.name == "Guard arms");
        if (arms >= 0)
        {
            var old = layers[arms].stateMachine;
            layers.RemoveAt(arms);
            if (old != null) AssetDatabase.RemoveObjectFromAsset(old);
            made += "Guard arms removed; ";
        }

        // Out of a swing the body rises from its lunge over SwingExit seconds (0.1 snapped it upright in two frames).
        foreach (var s in layers[0].stateMachine.states)
            if (s.state.name == "Attack1")
                foreach (var t in s.state.transitions) { t.duration = SwingExit; t.hasFixedDuration = true; }
        // The cast stands planted the same way: its spine bends through the cast and its feet slid with it.
        foreach (var l in layers)
            foreach (var s in l.stateMachine.states)
                if (s.state.motion != null && s.state.motion.name == "CastSpell") s.state.iKOnFeet = true;
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

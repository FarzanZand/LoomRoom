using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// SpellStaggerSetup.Build() from an eval (the user, 2026-11-12): the flinch the Dungeon Master and the room player's
// body play when a spell from the table strikes them (SpellStagger).
// - Three humanoid muscle clips in ThirdPerson/Gestures, made once and then edited by hand freely (never overwritten):
//   Spell stagger back (chest and head thrown back, shoulders up, hands come up a little), left and right (the body
//   bent and turned that way, away from a spell that came from the other side). Each starts and ends at rest (all
//   zero), which is the additive reference, so only the recoil itself is added to whatever pose he holds.
// - In HumanoidController and ThirdPersonRoom: the parameters SpellStagger (trigger) and StaggerSide (float), and a last
//   layer "Spell stagger" (additive, weight 1) with an empty default state and the state "Spell stagger", a blend on
//   StaggerSide (-1 left, 0 back, 1 right), entered from Any State on the trigger and back to the empty state at its end.
// Safe to run again: what exists is kept.
public static class SpellStaggerSetup
{
    const string ClipDir = "Assets/Game/Characters/Animations/ThirdPerson/Gestures";
    static readonly string[] Controllers =
    {
        "Assets/Game/Characters/Animations/HumanoidController.controller",
        "Assets/Game/Characters/Animations/ThirdPersonRoom.controller",
    };
    const string LayerName = "Spell stagger";

    // The flinch over time: 0 at rest, 1 at full recoil. In fast with the hit, held a moment, eased back.
    static readonly (float t, float w)[] Envelope = { (0f, 0f), (.07f, 1f), (.15f, .9f), (.32f, .45f), (.58f, 0f) };

    public static string Build()
    {
        var back = Clip("Spell stagger back", 0f);
        var left = Clip("Spell stagger left", -1f);
        var right = Clip("Spell stagger right", 1f);
        var log = new System.Text.StringBuilder();
        foreach (var path in Controllers) log.Append(Wire(path, back, left, right)).Append("; ");
        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    // Peak muscle offsets (-1..1), measured on the Dungeon Master's rig: Front-Back is positive bending BACK (the arms
    // too: positive swings them back), Nod Down-Up positive lifts the head, Left-Right positive bends to the body's
    // right, a Twist or Turn positive turns it to its right. `side` -1 recoils to the body's left, 1 to its right,
    // 0 straight back.
    static (string muscle, float peak)[] Peaks(float side)
    {
        float back = side == 0f ? 1f : .45f;
        return new[]
        {
            ("Spine Front-Back", .12f * back), ("Chest Front-Back", .16f * back), ("UpperChest Front-Back", .12f * back),
            ("Neck Nod Down-Up", .15f * back), ("Head Nod Down-Up", .18f * back),
            ("Spine Left-Right", .2f * side), ("Chest Left-Right", .16f * side), ("UpperChest Left-Right", .1f * side),
            // A blow on one shoulder turns the chest away from it; the face turns away from the burst.
            ("Spine Twist Left-Right", -.08f * side), ("Chest Twist Left-Right", -.07f * side),
            ("Head Tilt Left-Right", .14f * side), ("Head Turn Left-Right", .22f * side),
            // The flinch: shoulders up, arms a little up and forward, elbows bent.
            ("Left Shoulder Down-Up", .25f), ("Right Shoulder Down-Up", .25f),
            ("Left Arm Down-Up", .1f), ("Right Arm Down-Up", .1f),
            ("Left Arm Front-Back", -.14f), ("Right Arm Front-Back", -.14f),
            ("Left Forearm Stretch", -.2f), ("Right Forearm Stretch", -.2f),
        };
    }

    static AnimationClip Clip(string name, float side)
    {
        string path = $"{ClipDir}/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;
        var clip = new AnimationClip { name = name, frameRate = 30f };
        foreach (var (muscle, peak) in Peaks(side))
        {
            if (Mathf.Approximately(peak, 0f)) continue;
            var curve = new AnimationCurve();
            foreach (var (t, w) in Envelope) curve.AddKey(new Keyframe(t, peak * w));
            for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            for (int i = 0; i < curve.length; i++) AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            // Flat at both ends, so it leaves rest and comes back to it smoothly.
            AnimationUtility.SetKeyLeftTangentMode(curve, 0, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(curve, curve.length - 1, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), muscle), curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    static string Wire(string path, AnimationClip back, AnimationClip left, AnimationClip right)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) return $"no {path}";
        if (!Has(controller, SpellStagger.TriggerName)) controller.AddParameter(SpellStagger.TriggerName, AnimatorControllerParameterType.Trigger);
        if (!Has(controller, SpellStagger.SideName)) controller.AddParameter(SpellStagger.SideName, AnimatorControllerParameterType.Float);
        // A layer already there keeps its states; its blend is pointed at the clips again (they may have been remade).
        foreach (var l in controller.layers)
        {
            if (l.name != LayerName) continue;
            foreach (var s in l.stateMachine.states)
                if (s.state.motion is BlendTree existing)
                {
                    existing.children = new[]
                    {
                        new ChildMotion { motion = left, threshold = -1f, timeScale = 1f },
                        new ChildMotion { motion = back, threshold = 0f, timeScale = 1f },
                        new ChildMotion { motion = right, threshold = 1f, timeScale = 1f },
                    };
                    EditorUtility.SetDirty(existing);
                }
            EditorUtility.SetDirty(controller);
            return $"{controller.name}: layer there, clips set";
        }

        var machine = new AnimatorStateMachine { name = LayerName, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(machine, controller);
        var empty = machine.AddState("Empty", new Vector3(300f, 0f));
        machine.defaultState = empty;

        var tree = new BlendTree { name = "Spell stagger", blendType = BlendTreeType.Simple1D, blendParameter = SpellStagger.SideName, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(left, -1f);
        tree.AddChild(back, 0f);
        tree.AddChild(right, 1f);
        var stagger = machine.AddState("Spell stagger", new Vector3(300f, 120f));
        stagger.motion = tree;

        var into = machine.AddAnyStateTransition(stagger);
        into.AddCondition(AnimatorConditionMode.If, 0f, SpellStagger.TriggerName);
        into.hasExitTime = false; into.duration = .03f; into.hasFixedDuration = true; into.canTransitionToSelf = true;
        var home = stagger.AddTransition(empty);
        home.hasExitTime = true; home.exitTime = 1f; home.duration = .05f; home.hasFixedDuration = true;

        controller.AddLayer(new AnimatorControllerLayer { name = LayerName, stateMachine = machine, blendingMode = AnimatorLayerBlendingMode.Additive, defaultWeight = 1f });
        EditorUtility.SetDirty(controller);
        return $"{controller.name}: layer added";
    }

    static bool Has(AnimatorController controller, string name)
    {
        foreach (var p in controller.parameters) if (p.name == name) return true;
        return false;
    }
}

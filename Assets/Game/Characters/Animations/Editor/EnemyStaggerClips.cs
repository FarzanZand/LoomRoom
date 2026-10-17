using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Tools > LoomRoom > Combat > Make Enemy Stagger Clips: directional staggers for humanoid enemies, made once from
// Enemy_Hurt (ThirdPerson/Combat) as ordinary .anim clips to edit by hand afterwards:
// - Enemy_Hurt Left / Right: the body thrown to its left or right (spine, chest and head bend and twist that way);
// - Enemy_Hurt Heavy: doubled over and reeling, held longer.
// They join HumanoidController's Base Layer as Hurt Left, Hurt Right and Hurt Heavy (tag Hurt), entered from Any
// State on the Hurt trigger with HurtDir 1, 2 or 3 (EnemyBrain sets it from the blow; 0 keeps the plain Hurt).
// Safe to run again: existing clips are kept (hand edits stay), missing states and transitions are added.
public static class EnemyStaggerClips
{
    const string Dir = "Assets/Game/Characters/Animations/ThirdPerson/Combat";
    const string ControllerPath = "Assets/Game/Characters/Animations/HumanoidController.controller";

    [MenuItem("Tools/LoomRoom/Combat/Make Enemy Stagger Clips")]
    public static void MakeMenu() => Debug.Log("[Stagger] " + Make());

    public static string Make()
    {
        var hurt = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Dir}/Enemy_Hurt.anim");
        if (hurt == null) return "no Enemy_Hurt";
        // Muscle offsets (-1..1) at the peak of the stagger. "Left-Right" muscles bend toward the character's right
        // when positive; twists turn it to its right.
        var left = Clip(hurt, "Enemy_Hurt Left", 1f, (-.55f, -.35f, -.3f, 0f));
        var right = Clip(hurt, "Enemy_Hurt Right", 1f, (.55f, .35f, .3f, 0f));
        var heavy = Clip(hurt, "Enemy_Hurt Heavy", 1.7f, (0f, 0f, 0f, .6f));
        return Wire(new[] { ("Hurt Left", left, 1), ("Hurt Right", right, 2), ("Hurt Heavy", heavy, 3) });
    }

    // A copy of Enemy_Hurt stretched to `stretch` times its length, with (side bend, twist, head tilt, forward bend)
    // added over a rise-and-fall: in fast with the blow, out slowly.
    static AnimationClip Clip(AnimationClip hurt, string name, float stretch, (float bend, float twist, float head, float fold) peak)
    {
        string path = $"{Dir}/{name}.anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing != null) return existing;
        var clip = new AnimationClip { name = name, frameRate = hurt.frameRate };
        AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(hurt));
        float length = hurt.length * stretch;
        foreach (var binding in AnimationUtility.GetCurveBindings(hurt))
        {
            var from = AnimationUtility.GetEditorCurve(hurt, binding);
            float add = 0f;
            string p = binding.propertyName;
            if (p == "Spine Left-Right" || p == "Chest Left-Right") add = peak.bend;
            else if (p == "UpperChest Left-Right") add = peak.bend * .6f;
            else if (p.EndsWith("Twist Left-Right")) add = peak.twist;
            else if (p == "Head Tilt Left-Right") add = peak.head;
            else if (p == "Head Turn Left-Right") add = -peak.twist * .8f;
            else if (p == "Spine Front-Back" || p == "Chest Front-Back") add = peak.fold;
            else if (p == "Head Nod Down-Up") add = peak.fold * .5f;
            var to = new AnimationCurve();
            int steps = Mathf.CeilToInt(length * 30f);
            for (int i = 0; i <= steps; i++)
            {
                float t = i / 30f, u = t / length;
                // Up to full in the first fifth, back out over the rest.
                float w = u < .2f ? Mathf.SmoothStep(0f, 1f, u / .2f) : Mathf.SmoothStep(1f, 0f, (u - .2f) / .8f);
                to.AddKey(t, from.Evaluate(t / stretch) + add * w);
            }
            AnimationUtility.SetEditorCurve(clip, binding, to);
        }
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    static string Wire((string state, AnimationClip clip, int dir)[] staggers)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (System.Array.FindIndex(controller.parameters, p => p.name == "HurtDir") < 0) controller.AddParameter("HurtDir", AnimatorControllerParameterType.Int);
        var machine = controller.layers[0].stateMachine;
        AnimatorState plain = null;
        foreach (var s in machine.states) if (s.state.name == "Hurt") plain = s.state;
        if (plain == null) return "no Hurt state in the Base Layer";
        AnimatorStateTransition plainIn = null;
        foreach (var t in machine.anyStateTransitions) if (t.destinationState == plain) plainIn = t;
        // The plain Hurt only for HurtDir 0.
        if (plainIn != null && System.Array.FindIndex(plainIn.conditions, c => c.parameter == "HurtDir") < 0)
            plainIn.AddCondition(AnimatorConditionMode.Equals, 0, "HurtDir");
        int added = 0;
        foreach (var (name, clip, dir) in staggers)
        {
            AnimatorState state = null;
            foreach (var s in machine.states) if (s.state.name == name) state = s.state;
            if (state != null) continue;
            state = machine.AddState(name, new Vector3(600f, 300f + 60f * dir, 0f));
            state.motion = clip;
            state.tag = plain.tag;
            state.speed = plain.speed;
            state.speedParameterActive = plain.speedParameterActive;
            state.speedParameter = plain.speedParameter;
            state.writeDefaultValues = plain.writeDefaultValues;
            var into = machine.AddAnyStateTransition(state);
            if (plainIn != null)
                foreach (var c in plainIn.conditions) if (c.parameter != "HurtDir") into.AddCondition(c.mode, c.threshold, c.parameter);
            into.AddCondition(AnimatorConditionMode.Equals, dir, "HurtDir");
            into.duration = plainIn != null ? plainIn.duration : .025f;
            into.hasExitTime = false;
            into.canTransitionToSelf = true;
            foreach (var t in plain.transitions)
            {
                var copy = t.destinationState != null ? state.AddTransition(t.destinationState) : state.AddExitTransition();
                copy.hasExitTime = t.hasExitTime; copy.exitTime = t.exitTime; copy.duration = t.duration;
                foreach (var c in t.conditions) copy.AddCondition(c.mode, c.threshold, c.parameter);
            }
            added++;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return $"{added} stagger states added";
    }
}

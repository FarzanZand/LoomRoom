using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The shield held against something that keeps pushing (the Dungeon Master's beam): a looping clip of BlockIdle's guard
// pressed back toward the body, shaking, and a LeftArm state "BlockStrain" (tag Block) entered while the BlockStrain
// bool is on (PlayerCombat.SetShieldStrain). Bake the clip once, then edit Block_Strain.anim by hand freely; Bake Clip
// overwrites it.
public static class ShieldStrainSetup
{
    const string ClipPath = "Assets/Game/Characters/Animations/FirstPersonPlayer/Block_Strain.anim";
    const string BaseClipPath = "Assets/Game/Characters/Animations/FirstPersonPlayer/BlockIdle.anim";
    const string ControllerPath = "Assets/Game/Characters/Animations/FirstPersonTable.controller";
    const string Param = "BlockStrain";

    public static void SetUpMenu() => Debug.Log("[ShieldStrain] " + SetUp());

    public static void RebakeMenu() { Debug.Log("[ShieldStrain] " + BakeClip()); SetUp(); }

    public static string SetUp()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath) == null) BakeClip();
        WireController();
        AssetDatabase.SaveAssets();
        return "clip and LeftArm state in place";
    }

    // Pressed back: local Euler degrees per left-arm bone on top of the guard. Shake: the most each bone wobbles off
    // that, re-picked every Step seconds over one Length-second loop (the last key matches the first).
    public static Vector3 Shoulder = new(3, -2, 0), Elbow = new(-5, 0, -3), Hand = new(-2, 0, 0);
    public static Vector3 ShoulderShake = new(2.5f, 2f, 1.5f), ElbowShake = new(3.5f, 1.5f, 2.5f), HandShake = new(4f, 2f, 3f);
    public static float Step = .045f, Length = .54f;

    public static string BakeClip()
    {
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseClipPath);
        if (source == null) return "no " + BaseClipPath;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        bool created = clip == null;
        if (created) clip = new AnimationClip { name = "Block_Strain" };
        clip.ClearCurves();
        int steps = Mathf.Max(2, Mathf.RoundToInt(Length / Step));
        var rng = new System.Random(7);
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            float guard = AnimationUtility.GetEditorCurve(source, binding).Evaluate(0f);
            float pressed = guard + Pick(binding, Shoulder, Elbow, Hand);
            float shake = Pick(binding, ShoulderShake, ElbowShake, HandShake);
            var keys = new Keyframe[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float wobble = i == 0 || i == steps ? 0f : ((float)rng.NextDouble() * 2f - 1f) * shake;
                keys[i] = new Keyframe(Length * i / steps, pressed + wobble);
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (created) AssetDatabase.CreateAsset(clip, ClipPath);
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        return $"baked {ClipPath}";
    }

    static float Pick(EditorCurveBinding b, Vector3 shoulder, Vector3 elbow, Vector3 hand)
    {
        if (!b.propertyName.StartsWith("localEulerAnglesRaw")) return 0f;
        int axis = b.propertyName.EndsWith(".x") ? 0 : b.propertyName.EndsWith(".y") ? 1 : 2;
        string bone = b.path.Substring(b.path.LastIndexOf('/') + 1);
        return bone switch { "Shoulder_L" => shoulder[axis], "Elbow_L" => elbow[axis], "Hand_L" => hand[axis], _ => 0f };
    }

    static void WireController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (System.Array.Find(controller.parameters, p => p.name == Param) == null)
            controller.AddParameter(Param, AnimatorControllerParameterType.Bool);
        var machine = System.Array.Find(controller.layers, l => l.name == "LeftArm").stateMachine;
        AnimatorState strain = null, idle = null, blockIdle = null, blockHit = null;
        foreach (var s in machine.states)
            switch (s.state.name)
            {
                case "BlockStrain": strain = s.state; break;
                case "Idle": idle = s.state; break;
                case "BlockIdle": blockIdle = s.state; break;
                case "BlockHit": blockHit = s.state; break;
            }
        if (strain == null)
        {
            strain = machine.AddState("BlockStrain", new Vector3(500, 340, 0));
            strain.tag = "Block";
            // Into it from the held guard, or straight out of a blocked hit's recoil.
            foreach (var from in new[] { blockIdle, blockHit })
            {
                if (from == null) continue;
                var into = from.AddTransition(strain);
                into.AddCondition(AnimatorConditionMode.If, 0, Param);
                into.AddCondition(AnimatorConditionMode.If, 0, "BlockHeld");
                into.hasExitTime = false; into.duration = .08f;
            }
            if (blockIdle != null)
            {
                var back = strain.AddTransition(blockIdle);
                back.AddCondition(AnimatorConditionMode.IfNot, 0, Param);
                back.AddCondition(AnimatorConditionMode.If, 0, "BlockHeld");
                back.hasExitTime = false; back.duration = .15f;
            }
            if (idle != null)
            {
                var down = strain.AddTransition(idle);
                down.AddCondition(AnimatorConditionMode.IfNot, 0, "BlockHeld");
                down.hasExitTime = false; down.duration = .15f;
            }
        }
        strain.motion = clip;
        EditorUtility.SetDirty(controller);
    }
}

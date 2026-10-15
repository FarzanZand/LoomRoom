using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Makes "<clip> Strike" copies of the light and heavy release clips with the strike slowed down so the blade stays on
// screen while it crosses the view, and the hitbox open during that crossing. Measured on the originals: the blade
// crossed the whole screen in about one frame, during the hitbox. The copy keeps the
// clip's length (the follow-through is a little quicker), so swing timing and transitions stay the same.
// Tools > LoomRoom > Combat > Make Strike Clips points the release states at the copies; Use Original Swing Clips
// points them back. The copies are plain .anim files: edit them by hand freely (running Make again overwrites them).
public static class StrikeClipRetimer
{
    const string Folder = "Assets/Game/Characters/Animations/FirstPersonPlayer/";
    const string ControllerPath = "Assets/Game/Characters/Animations/FirstPersonTable.controller";

    // A clip, the stretch of it that holds the crossing (old start/end), how long that stretch becomes, and where the
    // hitbox and the swing sound go in the new timing.
    struct Plan
    {
        public string clip, state; public float from, to, newTo, swing, open, close;
    }

    static readonly Plan[] Plans =
    {
        new Plan { clip = "Attack_Swing_A", state = "Attack_Release", from = .11f, to = .22f, newTo = .33f, swing = .08f, open = .13f, close = .31f },
        new Plan { clip = "Attack_Swing_B", state = "Attack_Release_B", from = .11f, to = .22f, newTo = .33f, swing = .08f, open = .13f, close = .31f },
        new Plan { clip = "Attack_Swing_Heavy", state = "Attack_HeavyRelease", from = .18f, to = .34f, newTo = .50f, swing = .14f, open = .21f, close = .47f },
    };

    [MenuItem("Tools/LoomRoom/Combat/Make Strike Clips")]
    public static void MakeMenu() => Debug.Log("[StrikeClips] " + Make());

    [MenuItem("Tools/LoomRoom/Combat/Use Original Swing Clips")]
    public static void OriginalMenu() => Debug.Log("[StrikeClips] " + Assign(false));

    public static string Make()
    {
        var made = new List<string>();
        foreach (var plan in Plans)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + plan.clip + ".anim");
            if (source == null) { made.Add("missing " + plan.clip); continue; }
            string path = Folder + plan.clip + " Strike.anim";
            var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (copy == null) { copy = new AnimationClip(); AssetDatabase.CreateAsset(copy, path); }
            copy.ClearCurves();
            copy.frameRate = source.frameRate;
            AnimationUtility.SetAnimationClipSettings(copy, AnimationUtility.GetAnimationClipSettings(source));
            float length = source.length;
            const float step = 1f / 120f;
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                var keys = new List<Keyframe>();
                for (float t = 0f; t < length + step * .5f; t += step)
                {
                    float nt = Mathf.Min(t, length);
                    keys.Add(new Keyframe(nt, curve.Evaluate(Unwarp(plan, nt, length))));
                }
                var baked = new AnimationCurve(keys.ToArray());
                for (int i = 0; i < baked.length; i++) { AnimationUtility.SetKeyLeftTangentMode(baked, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(baked, i, AnimationUtility.TangentMode.Linear); }
                AnimationUtility.SetEditorCurve(copy, binding, baked);
            }
            // Object curves (sprites, materials) keep their keys at warped times.
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source))
            {
                var frames = AnimationUtility.GetObjectReferenceCurve(source, binding);
                for (int i = 0; i < frames.Length; i++) frames[i].time = Warp(plan, frames[i].time, length);
                AnimationUtility.SetObjectReferenceCurve(copy, binding, frames);
            }
            var events = new List<AnimationEvent>();
            foreach (var e in AnimationUtility.GetAnimationEvents(source))
            {
                e.time = e.functionName == "EnableHitbox" ? plan.open : e.functionName == "DisableHitbox" ? plan.close
                    : e.functionName == "PlaySwingAudio" ? plan.swing : Warp(plan, e.time, length);
                events.Add(e);
            }
            AnimationUtility.SetAnimationEvents(copy, events.ToArray());
            EditorUtility.SetDirty(copy);
            made.Add(copy.name);
        }
        AssetDatabase.SaveAssets();
        return "made " + string.Join(", ", made) + "; " + Assign(true);
    }

    // New time -> old time: the crossing stretch is slowed, the rest after it sped up to keep the length.
    static float Unwarp(Plan p, float t, float length)
    {
        if (t <= p.from) return t;
        if (t <= p.newTo) return p.from + (t - p.from) * (p.to - p.from) / (p.newTo - p.from);
        return p.to + (t - p.newTo) * (length - p.to) / Mathf.Max(.001f, length - p.newTo);
    }

    // Old time -> new time.
    static float Warp(Plan p, float t, float length)
    {
        if (t <= p.from) return t;
        if (t <= p.to) return p.from + (t - p.from) * (p.newTo - p.from) / (p.to - p.from);
        return p.newTo + (t - p.to) * (length - p.newTo) / Mathf.Max(.001f, length - p.to);
    }

    // Points the release states at the strike copies (true) or the originals (false).
    public static string Assign(bool strike)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) return "no controller";
        int n = 0;
        foreach (var layer in controller.layers)
            foreach (var child in layer.stateMachine.states)
                foreach (var plan in Plans)
                {
                    if (child.state.name != plan.state) continue;
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + plan.clip + (strike ? " Strike" : "") + ".anim");
                    if (clip == null) continue;
                    child.state.motion = clip; n++;
                }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return $"{n} release states use the {(strike ? "strike copies" : "original clips")}";
    }
}

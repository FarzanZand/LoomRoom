using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Combo 2's third hit (Combo2 chop): the Simple tap's swing (Attack_Windup_Feel, then Attack_Swing_A Strike) remade as
// one continuous, bigger blow for the combo, without touching those clips. Every frame is a pose of theirs:
// - windup: the windup played through, then carried on the way it was going (higher, the blade further back), easing
//   into the top;
// - strike: the strike's cut from its start to its low point, the windup's extra height let go of as it falls;
// - follow-through: carried on the way the cut was going, slowing;
// - recovery: straight back to the arms' resting pose (the strike's own recovery bounces up and down again, cut);
// - framing: the combo's (the other combo hits' view curves) at both ends, so going in and out doesn't jump, and the
//   Simple swing's through the cut, so it crosses the view as it does there.
// Timed in real seconds at Play Speed (Combo Speed x the attack speed stat, as it plays), written in clip time.
public static partial class UnrealComboConverter
{
    const string ChopName = "Combo2 chop";
    const float ChopPlaySpeed = .64f;
    const float ChopFps = 60f;
    // Real seconds: windup, held at the top, the cut, the follow-through, back to rest. The windup was .19 s eased in
    // and out, so the blade stood upright for about 8 frames before the cut and read as a stall (the user, 2026-11-09):
    // now it is short and leaves at once (ChopWindupEase), slowing only into the top.
    const float ChopWindup = .12f, ChopTop = .02f, ChopCut = .14f, ChopThrough = .11f, ChopRecover = .34f;
    // Windup seconds carried on past the clip's end (as far as its last .13 s went again, times this share).
    const float ChopWindupMore = .12f;
    // How far the follow-through goes on past the cut's low point, the way its fastest stretch went (1 = not at all).
    const float ChopThroughMore = 1.5f;

    // Combo 2 (CombatManager.SwingSet.Combo2): copies of the Combo's two swings (so its states stand apart from
    // the Combo's and can be edited on their own), then Combo2 chop as the third hit (UnrealComboConverter.Combo2.cs).
    // Rebuilt in place on each run.
    public const string Combo2Folder = "Assets/Game/Characters/Animations/FirstPersonPlayer/Combo2";
    public static readonly string[] Combo2Names = { "Combo2 swing left", "Combo2 swing right", ChopName };

    // Where each swing's hitbox closes, in clip seconds. The copied Combo windows (the hand's fastest stretch) closed
    // before the blade crossed the middle of the view, at .067 and .133 s, so the player's reach (Hitbox: out from the
    // chest the way the blade is, CombatManager.playerMeleeReach) never pointed at an enemy in front: both swings missed
    // at 1.6 m and the second even at 1.2 m, where the chop landed (the user, 2026-11-09: the second swing must reach as
    // far as the first). Measured in play: the blade crosses the middle at .098 s (swing left) and .160 s (swing right),
    // and is 45 degrees past it at .122 and .222 s, so both now sweep the same arc through the middle.
    public static readonly float[] Combo2HitClose = { .122f, .222f };

    public static string MakeCombo2()
    {
        System.IO.Directory.CreateDirectory(Combo2Folder);
        var sources = new[] { $"{Output}/Combo swing left.anim", $"{Output}/Combo swing right.anim" };
        for (int i = 0; i < 2; i++)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(sources[i]);
            if (source == null) return "Combo2: missing " + sources[i];
            string path = $"{Combo2Folder}/{Combo2Names[i]}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = Object.Instantiate(source); AssetDatabase.CreateAsset(clip, path); }
            else EditorUtility.CopySerialized(source, clip);
            clip.name = Combo2Names[i];
            SetHitClose(clip, Combo2HitClose[i]);
            EditorUtility.SetDirty(clip);
        }
        AssetDatabase.SaveAssets();
        return "Combo2: 2 swings | " + MakeCombo2Chop();
    }

    // Moves the clip's DisableHitbox event (Combo2HitClose); the motion and the other events are left as they are.
    public static string SetHitClose(AnimationClip clip, float close)
    {
        var events = AnimationUtility.GetAnimationEvents(clip);
        foreach (var e in events) if (e.functionName == "DisableHitbox") e.time = Mathf.Min(clip.length, close);
        AnimationUtility.SetAnimationEvents(clip, events);
        return $"{clip.name}: hitbox closes at {close:0.000} s";
    }

    // Applies Combo2HitClose to the existing Combo2 swing clips without rebuilding them (run from an eval).
    public static string ApplyCombo2HitClose()
    {
        var report = new List<string>();
        for (int i = 0; i < 2; i++)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Combo2Folder}/{Combo2Names[i]}.anim");
            if (clip == null) { report.Add("missing " + Combo2Names[i]); continue; }
            report.Add(SetHitClose(clip, Combo2HitClose[i]));
            EditorUtility.SetDirty(clip);
        }
        AssetDatabase.SaveAssets();
        return string.Join(" | ", report);
    }

    public static string MakeCombo2Chop()
    {
        const string fp = "Assets/Game/Characters/Animations/FirstPersonPlayer";
        var windupClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{fp}/Attack_Windup_Feel.anim");
        var strikeClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{fp}/Attack_Swing_A Strike.anim");
        var idleClip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{fp}/Idle.anim");
        var framing = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Output}/Combo swing left.anim");
        if (windupClip == null || strikeClip == null || idleClip == null || framing == null) return "Combo2 chop: a source clip is missing";

        var body = TargetBody(out var holder);
        try
        {
            RestPose(body);
            var rest = new Dictionary<Transform, Quaternion>();
            foreach (var b in body.GetComponentsInChildren<Transform>(true)) rest[b] = b.localRotation;
            // The bones the Simple strike moves.
            var bones = new List<Transform>();
            foreach (var binding in AnimationUtility.GetCurveBindings(strikeClip))
            {
                var bone = body.Find(binding.path);
                if (bone != null && !bones.Contains(bone)) bones.Add(bone);
            }
            Quaternion[] Pose(AnimationClip clip, float t)
            {
                foreach (var pair in rest) pair.Key.localRotation = pair.Value;
                clip.SampleAnimation(body.gameObject, t);
                var q = new Quaternion[bones.Count];
                for (int i = 0; i < q.Length; i++) q[i] = bones[i].localRotation;
                return q;
            }
            Quaternion[] Lerp(Quaternion[] a, Quaternion[] b, float k)
            {
                var q = new Quaternion[a.Length];
                for (int i = 0; i < q.Length; i++) q[i] = Quaternion.SlerpUnclamped(a[i], b[i], k);
                return q;
            }
            // Each bone's turn from pose a to pose b, applied to pose c by weight w.
            Quaternion[] Carry(Quaternion[] from, Quaternion[] to, Quaternion[] onto, float w)
            {
                var q = new Quaternion[onto.Length];
                for (int i = 0; i < q.Length; i++)
                    q[i] = Quaternion.Slerp(Quaternion.identity, to[i] * Quaternion.Inverse(from[i]), w) * onto[i];
                return q;
            }

            float windupEnd = windupClip.length, back = .133f;
            var windupLast = Pose(windupClip, windupEnd);
            var windupBack = Pose(windupClip, windupEnd - back);
            Quaternion[] Windup(float s) => s <= windupEnd ? Pose(windupClip, s) : Lerp(windupBack, windupLast, 1f + (s - windupEnd) / back);
            var top = Windup(windupEnd + ChopWindupMore + .02f);
            // The cut ends at its low point (.175): past it the strike rises and drops again (its bounce).
            const float cutFrom = .033f, cutTo = .175f, beforeEnd = .133f;
            var cutStart = Pose(strikeClip, cutFrom);
            var throughA = Pose(strikeClip, beforeEnd);
            var throughB = Pose(strikeClip, cutTo);
            var throughEnd = Lerp(throughA, throughB, ChopThroughMore);
            var idle = Pose(idleClip, 0f);

            float tTop = ChopWindup, tCut = tTop + ChopTop, tThrough = tCut + ChopCut, tRecover = tThrough + ChopThrough, tEnd = tRecover + ChopRecover;
            float hitOpen = -1f, hitClose = -1f;
            var keys = new Dictionary<Transform, List<(float, Quaternion)>>();
            foreach (var b in bones) keys[b] = new List<(float, Quaternion)>();
            int frames = Mathf.CeilToInt(tEnd * ChopPlaySpeed * ChopFps);
            for (int f = 0; f <= frames; f++)
            {
                float clipTime = f / ChopFps, r = clipTime / ChopPlaySpeed;
                Quaternion[] pose;
                if (r < tTop) pose = Windup((windupEnd + ChopWindupMore) * ChopWindupEase(r / ChopWindup));
                else if (r < tCut) pose = Windup(windupEnd + ChopWindupMore + .02f * Smoother((r - tTop) / ChopTop));
                else if (r < tThrough)
                {
                    float u = (r - tCut) / ChopCut;
                    float m = Mathf.Lerp(cutFrom, cutTo, u);
                    // The extra windup height is let go of over the first half of the cut.
                    pose = Carry(cutStart, top, Pose(strikeClip, m), 1f - Smoother(Mathf.Clamp01(u * 2f)));
                    if (hitOpen < 0f && m >= .1f) hitOpen = clipTime;
                }
                else if (r < tRecover)
                {
                    float u = (r - tThrough) / ChopThrough;
                    pose = Lerp(throughB, throughEnd, Mathf.Sin(u * Mathf.PI * .5f));
                    if (hitClose < 0f && u >= .25f) hitClose = clipTime;
                }
                else { float u = Mathf.Clamp01((r - tRecover) / ChopRecover); pose = Lerp(throughEnd, idle, u * u * (3f - 2f * u)); }
                for (int i = 0; i < bones.Count; i++) keys[bones[i]].Add((clipTime, pose[i]));
            }

            string path = $"{Combo2Folder}/{ChopName}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) { clip = new AnimationClip { name = ChopName }; AssetDatabase.CreateAsset(clip, path); }
            clip.ClearCurves();
            clip.frameRate = ChopFps;
            foreach (var pair in keys) Keys(clip, body, pair.Key, pair.Value);
            float length = frames / ChopFps;
            // Framing: the combo's at both ends (the hits before and after keep theirs, no jump), the Simple swing's
            // (none) from the top of the windup to the start of the recovery, so the cut crosses the view as it does there.
            float plain = (tTop - .04f) * ChopPlaySpeed, framedBack = (tRecover + ChopRecover * .25f) * ChopPlaySpeed;
            foreach (var name in OneBodyRig.ViewCurves)
            {
                var source = AnimationUtility.GetEditorCurve(framing, EditorCurveBinding.FloatCurve("", typeof(Animator), name));
                float v = source != null ? source.Evaluate(0f) : 0f;
                var curve = new AnimationCurve(new Keyframe(0f, v), new Keyframe(plain, 0f), new Keyframe(framedBack, 0f), new Keyframe(length, v));
                for (int k = 0; k < curve.length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), name), curve);
            }
            clip.EnsureQuaternionContinuity();
            AnimationUtility.SetAnimationEvents(clip, new[]
            {
                new AnimationEvent { functionName = "AttackBegin", time = 0f },
                new AnimationEvent { functionName = "PlaySwingAudio", time = Mathf.Max(0f, hitOpen - 2f / 30f) },
                new AnimationEvent { functionName = "EnableHitbox", time = hitOpen },
                new AnimationEvent { functionName = "DisableHitbox", time = hitClose },
            });
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return $"{ChopName}: {frames + 1} frames ({length:0.00} s clip, {tEnd:0.00} s played), hitbox {hitOpen:0.000}-{hitClose:0.000}";
        }
        finally
        {
            Object.DestroyImmediate(body.gameObject);
            if (holder != null) Object.DestroyImmediate(holder);
        }
    }

    // Out of the last hit's pose at speed (the transition's blend softens the start), slowing into the top.
    static float ChopWindupEase(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }

    static float Smoother(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
}

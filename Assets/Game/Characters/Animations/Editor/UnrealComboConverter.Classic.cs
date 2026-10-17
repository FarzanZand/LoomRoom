using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Tools > LoomRoom > Characters > Make Classic Swings: the Classic and Classic 2 swing sets (CombatManager.SwingSet), made
// from the converted combo clips (Combo/*.anim) without touching how the arm moves: every bone rotation is the Unreal
// clip's own. Each set (a Style below) changes three things:
// - timing: the clip is played on a time warp (warps, set frame -> Combo frame, a smooth curve through the keys);
// - framing: the whole first-person arms are turned about the eyes (aims: raised, and rolled so the arc runs flatter),
//   through the clip's view curves (OneBodyRig.ViewCurves);
// - the sword hand's turn (twists), so the blade shows and leads.
// Classic: a held windup at the screen's edge, a fast cut, a long follow-through. Classic 2: no visible windup, the arm
// comes in from the edge low and flat, crosses the screen in about four frames and is gone out of the other side.
// The stab is the same in both. Running it again rebuilds both sets in place.
public static partial class UnrealComboConverter
{
    class Style
    {
        public string folder, prefix;
        public Dictionary<string, Vector2[]> warps;
        public Dictionary<string, Vector2> aims;
        public Dictionary<string, (float degrees, Vector4 frames)> twists;
        public Dictionary<string, Vector2Int> hits;
        // The arms' field of view, as for every other clip: the Unreal one drew our chunkier arm too big and too close.
        public float fovOffset;
    }

    // The stab is the same in both sets: drawn back and held, a fast thrust, held at full reach; it ends there and the
    // controller's blend takes it home to Idle (the raise after it read as a parry). Its blade turned to point ahead.
    static readonly Vector2[] StabWarp = { new(0, 6f), new(3, 9f), new(7, 14f), new(9, 18f), new(11, 20.5f), new(17, 22.5f) };

    static readonly Style[] Styles =
    {
        new Style
        {
            folder = "Assets/Game/Characters/Animations/FirstPersonPlayer/Classic", prefix = "Classic",
            // Set frame -> Combo frame (30 fps); the last key sets the clip's length. The left swing winds up with the
            // fist at the right edge and its blade out past it, then cuts across the whole screen; the backhand winds up
            // at the left edge with its blade turned out into view.
            warps = new()
            {
                ["Combo swing left"] = new[] { new Vector2(0, .5f), new Vector2(3, .6f), new Vector2(5, .8f), new Vector2(7, 1.6f),
                    new Vector2(9, 3f), new Vector2(13, 5.2f), new Vector2(18, 9f), new Vector2(22, 18f), new Vector2(28, 30f) },
                ["Combo swing right"] = new[] { new Vector2(0, 1.3f), new Vector2(3, 1.7f), new Vector2(5, 2.3f), new Vector2(7, 3.3f),
                    new Vector2(9, 4.5f), new Vector2(13, 6.4f), new Vector2(18, 9.5f), new Vector2(22, 18f), new Vector2(28, 30f) },
                ["Combo stab"] = StabWarp,
            },
            // Degrees about the eyes: x up, y roll (positive counter-clockwise on screen).
            aims = new() { ["Combo swing left"] = new Vector2(7f, -18f), ["Combo swing right"] = new Vector2(4f, 24f), ["Combo stab"] = new Vector2(8f, 0f) },
            twists = new()
            {
                ["Combo swing left"] = (-15f, new Vector4(3, 7, 11, 16)),
                ["Combo swing right"] = (60f, new Vector4(-1, 0, 5, 9)),
                ["Combo stab"] = (30f, new Vector4(5, 9, 40, 41)),
            },
            hits = new() { ["Combo swing left"] = new Vector2Int(6, 10), ["Combo swing right"] = new Vector2Int(6, 10), ["Combo stab"] = new Vector2Int(7, 11) },
        },
        new Style
        {
            folder = "Assets/Game/Characters/Animations/FirstPersonPlayer/Classic2", prefix = "Classic2",
            // No held windup: from the edge, across in about four frames, out of the other side, a short recovery.
            warps = new()
            {
                ["Combo swing left"] = new[] { new Vector2(0, .55f), new Vector2(2, .8f), new Vector2(4, 2.1f), new Vector2(6, 3.8f),
                    new Vector2(8, 5.8f), new Vector2(11, 9f), new Vector2(15, 18f), new Vector2(21, 30f) },
                ["Combo swing right"] = new[] { new Vector2(0, 1.2f), new Vector2(2, 1.9f), new Vector2(4, 3.1f), new Vector2(6, 4.4f),
                    new Vector2(8, 6.3f), new Vector2(11, 9.5f), new Vector2(15, 18f), new Vector2(21, 30f) },
                ["Combo stab"] = StabWarp,
            },
            // Lower and flatter than Classic: the sweep runs through the lower middle of the screen, nearly level.
            aims = new() { ["Combo swing left"] = new Vector2(0f, -24f), ["Combo swing right"] = new Vector2(-2f, 30f), ["Combo stab"] = new Vector2(8f, 0f) },
            twists = new()
            {
                ["Combo swing left"] = (-15f, new Vector4(1, 3, 7, 11)),
                ["Combo swing right"] = (60f, new Vector4(-1, 0, 3, 6)),
                ["Combo stab"] = (30f, new Vector4(5, 9, 40, 41)),
            },
            hits = new() { ["Combo swing left"] = new Vector2Int(2, 7), ["Combo swing right"] = new Vector2Int(2, 7), ["Combo stab"] = new Vector2Int(7, 11) },
        },
    };

    static readonly string[] ComboNames = { "Combo swing left", "Combo swing right", "Combo stab" };
    static string StyleClip(Style style, string combo) => style.prefix + combo.Substring("Combo".Length);

    [MenuItem("Tools/LoomRoom/Characters/Make Classic Swings")]
    public static void MakeClassicMenu() => Debug.Log("[Classic] " + MakeClassic());

    public static string MakeClassic()
    {
        var log = new System.Text.StringBuilder();
        foreach (var style in Styles)
        {
            System.IO.Directory.CreateDirectory(style.folder);
            foreach (var combo in ComboNames) log.Append(MakeClassic(style, combo)).Append(" | ");
        }
        log.Append(MakeCombo2()).Append(" | ");
        log.Append(Wire());
        return log.ToString();
    }

    // Combo 2 (CombatManager.SwingSet.Combo2, for maces): copies of the Combo's two swings (so its states stand apart from
    // the Combo's and can be edited on their own), then Combo2 chop as the third hit (UnrealComboConverter.Combo2.cs).
    // Rebuilt in place on each run.
    public const string Combo2Folder = "Assets/Game/Characters/Animations/FirstPersonPlayer/Combo2";
    public static readonly string[] Combo2Names = { "Combo2 swing left", "Combo2 swing right", ChopName };

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
            EditorUtility.SetDirty(clip);
        }
        AssetDatabase.SaveAssets();
        return "Combo2: 2 swings | " + MakeCombo2Chop();
    }

    static string MakeClassic(Style style, string comboName)
    {
        string classicName = StyleClip(style, comboName);
        var source = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Output}/{comboName}.anim");
        if (source == null) return comboName + ": missing";
        // Overwritten in place, so the controller's states keep pointing at it.
        string path = $"{style.folder}/{classicName}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = Object.Instantiate(source); AssetDatabase.CreateAsset(clip, path); }
        else EditorUtility.CopySerialized(source, clip);
        clip.name = classicName;
        if (!style.warps.TryGetValue(comboName, out var warp)) { EditorUtility.SetDirty(clip); AssetDatabase.SaveAssets(); return classicName + ": copied"; }

        int frames = Mathf.RoundToInt(warp[^1].x);
        // Every curve resampled on the warp, one key a frame.
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var from = AnimationUtility.GetEditorCurve(source, binding);
            var to = new AnimationCurve();
            for (int f = 0; f <= frames; f++) to.AddKey(f / 30f, from.Evaluate(Warp(warp, f) / 30f));
            for (int i = 0; i < to.length; i++) to.SmoothTangents(i, 0f);
            AnimationUtility.SetEditorCurve(clip, binding, to);
        }

        if (style.twists.TryGetValue(comboName, out var twist)) TwistHand(clip, twist.degrees, twist.frames, frames);

        // The arms turned about the eyes: their place and turn in the view curves, composed with the turn.
        float V(int i) { var c = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), OneBodyRig.ViewCurves[i])); return c != null ? c.Evaluate(0f) : 0f; }
        var aim = style.aims[comboName];
        var turn = Quaternion.Euler(-aim.x, 0f, aim.y);
        var below = new Vector3(0f, -EyeHeight, 0f);
        var place = turn * (below + new Vector3(V(4), V(0), V(1))) - below;
        var rotation = (turn * Quaternion.Euler(V(2), V(5), V(6))).eulerAngles;
        float A(float a) => Mathf.DeltaAngle(0f, a);
        float[] values = { place.y, place.z, A(rotation.x), style.fovOffset, place.x, A(rotation.y), A(rotation.z) };
        float length = frames / 30f;
        for (int i = 0; i < values.Length; i++)
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), OneBodyRig.ViewCurves[i]), AnimationCurve.Constant(0f, length, values[i]));
        clip.EnsureQuaternionContinuity();

        var hit = style.hits[comboName];
        AnimationUtility.SetAnimationEvents(clip, new[]
        {
            new AnimationEvent { functionName = "AttackBegin", time = 0f },
            new AnimationEvent { functionName = "PlaySwingAudio", time = (hit.x - 2) / 30f },
            new AnimationEvent { functionName = "EnableHitbox", time = hit.x / 30f },
            new AnimationEvent { functionName = "DisableHitbox", time = hit.y / 30f },
        });
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssets();
        return $"{classicName}: {frames + 1} frames from the combo's {Mathf.RoundToInt(source.length * 30f) + 1}, hit frames {hit.x}-{hit.y}";
    }

    // The hand turned about the body's up, weighted by a ramp (up from frames.x to .y, down from .z to .w).
    static void TwistHand(AnimationClip clip, float degrees, Vector4 window, int frames)
    {
        var body = TargetBody(out var holder);
        try
        {
            RestPose(body);
            var rest = new Dictionary<Transform, Quaternion>();
            foreach (var b in body.GetComponentsInChildren<Transform>(true)) rest[b] = b.localRotation;
            var hand = Bones(body)["Hand_R"];
            var keys = new List<(float, Quaternion)>();
            for (int f = 0; f <= frames; f++)
            {
                foreach (var pair in rest) pair.Key.localRotation = pair.Value;
                clip.SampleAnimation(body.gameObject, f / 30f);
                float w = Mathf.Min(Mathf.InverseLerp(window.x, window.y, f), 1f - Mathf.InverseLerp(window.z, window.w, f));
                w = w * w * (3f - 2f * w);
                hand.rotation = Quaternion.AngleAxis(degrees * w, body.up) * hand.rotation;
                keys.Add((f / 30f, hand.localRotation));
            }
            Keys(clip, body, hand, keys);
        }
        finally
        {
            Object.DestroyImmediate(body.gameObject);
            if (holder != null) Object.DestroyImmediate(holder);
        }
    }

    // The Combo frame for a Classic frame: a smooth curve (Catmull-Rom) through the warp's keys.
    static float Warp(Vector2[] keys, float frame)
    {
        if (frame <= keys[0].x) return keys[0].y;
        for (int i = 0; i < keys.Length - 1; i++)
            if (frame <= keys[i + 1].x)
            {
                float p0 = keys[Mathf.Max(0, i - 1)].y, p1 = keys[i].y, p2 = keys[i + 1].y, p3 = keys[Mathf.Min(keys.Length - 1, i + 2)].y;
                float u = Mathf.InverseLerp(keys[i].x, keys[i + 1].x, frame);
                return Mathf.Max(0f, .5f * (2f * p1 + (p2 - p0) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u * u + (3f * p1 - p0 - 3f * p2 + p3) * u * u * u));
            }
        return keys[^1].y;
    }
}

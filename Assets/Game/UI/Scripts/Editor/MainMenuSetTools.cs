using System.IO;
using UnityEditor;
using UnityEngine;

// Helpers for building the main menu's set by hand (Scenes/Main Menu): write a held pose clip from another clip's
// muscles plus changes, preview it on a humanoid in Play mode, and render a camera to a PNG for review.
public static class MainMenuSetTools
{
    // Shows a pose clip on a humanoid at once by swapping it in for one of its controller's clips (an override
    // controller) and updating the animator from its default state. Render with Render below: it makes skinned meshes
    // draw the bones as just posed. (Playable sampling and HumanPoseHandler don't move these bones.)
    public static void PreviewPose(Animator animator, AnimationClip clip, string replacedClip)
    {
        var controller = animator.runtimeAnimatorController is AnimatorOverrideController current ? current.runtimeAnimatorController : animator.runtimeAnimatorController;
        var overrides = new AnimatorOverrideController(controller);
        AnimationClip replaced = null;
        foreach (var c in controller.animationClips) if (c.name == replacedClip) replaced = c;
        if (replaced == null && controller.animationClips.Length > 0) replaced = controller.animationClips[0];   // the state's own clip
        overrides[replaced] = clip;
        animator.runtimeAnimatorController = overrides;
        animator.Rebind();
        animator.Update(0f);
    }

    // Builds a reaching pose: the base clip with `lean` applied, then the right arm's muscles searched so the palm sits
    // at `target`, facing up, its knuckles pointing along `fingers`, and the elbow near where two-bone IK would put it
    // (bent toward `pole`). The shoulder keeps the base pose's muscles, so it never wrenches. Muscles stay within
    // `limit` of zero, so no joint twists past what reads natural. The pose (with `extra` after it) is written into a
    // held clip at outPath, or an unsaved preview when it is null; returns the clip, posed on the animator.
    public static AnimationClip ReachPose(Animator animator, string basePath, string lean, Vector3 target, Vector3 pole,
        Vector3 fingers, string extra, string outPath, float breathe = 0f, float limit = .75f)
    {
        string[] arm = { "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out", "Right Forearm Stretch",
            "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out" };
        var start = Values(AssetDatabase.LoadAssetAtPath<AnimationClip>(basePath), 0f, lean);
        float[] x = new float[arm.Length];
        for (int i = 0; i < arm.Length; i++) { start.TryGetValue(arm[i], out x[i]); x[i] = Mathf.Clamp(x[i], -limit, limit); }

        PreviewPose(animator, MakePose(basePath, 0f, null, lean), "Menu DM reach");
        Transform upper = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform lower = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Transform index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
        Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        Transform thumb = animator.GetBoneTransform(HumanBodyBones.RightThumbProximal);
        // Where IK would put the elbow, from the shoulder and the arm's lengths.
        float palm = Vector3.Distance(middle.position, hand.position) * .5f;
        Vector3 wrist = target - fingers.normalized * palm;
        Vector3 s = upper.position;
        float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
        float c = Mathf.Clamp(Vector3.Distance(s, wrist), Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        Vector3 dir = (wrist - s).normalized;
        float along = (a * a - b * b + c * c) / (2f * c);
        Vector3 elbow = s + dir * along + Vector3.ProjectOnPlane(pole - s, dir).normalized * Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        float scale = a + b;

        string Overrides(float[] v)
        {
            var sb = new System.Text.StringBuilder(lean);
            for (int i = 0; i < arm.Length; i++) sb.Append(';').Append(arm[i]).Append(":=").Append(v[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            return sb.ToString();
        }
        bool positionOnly = false;
        float Cost(float[] v)
        {
            PreviewPose(animator, MakePose(basePath, 0f, null, Overrides(v)), "Menu DM reach");
            Vector3 f = (middle.position - hand.position).normalized;
            Vector3 p = Vector3.Cross(f, index.position - middle.position).normalized;   // the palm side of a right hand
            // A right hand turned palm up has its thumb on the body's right.
            float thumbSide = Vector3.Dot((thumb.position - hand.position).normalized, animator.transform.right);
            Vector3 palmCentre = hand.position + (middle.position - hand.position) * .5f;
            float place = (palmCentre - target).sqrMagnitude / (scale * scale) * 40f
                        + (lower.position - elbow).sqrMagnitude / (scale * scale) * 10f;
            if (positionOnly) return place;
            return place
                 + (1f - Vector3.Dot(f, fingers.normalized)) * 4f
                 + (1f - Vector3.Dot(p, Vector3.up)) * 4f
                 + (1f - thumbSide) * 1.5f;
        }
        // Stage one places the arm (shoulder to wrist) by where the palm and elbow go; stage two turns only the wrist.
        float Search(int[] which, System.Func<float[], float> cost)
        {
            float b = cost(x);
            for (float step = .3f; step > .01f; step *= .5f)
                for (int pass = 0; pass < 3; pass++)
                {
                    bool moved = false;
                    foreach (int i in which)
                        foreach (float sign in new[] { 1f, -1f })
                        {
                            float old = x[i];
                            float lim = arm[i] == "Right Forearm Twist In-Out" || arm[i].StartsWith("Right Hand") ? 1f : limit;
                            x[i] = Mathf.Clamp(old + sign * step, -lim, lim);
                            float c = cost(x);
                            if (c < b - 1e-5f) { b = c; moved = true; }
                            else x[i] = old;
                        }
                    if (!moved) break;
                }
            return b;
        }
        positionOnly = true;
        Search(new[] { 0, 1, 2, 3 }, Cost);
        positionOnly = false;
        float best = Search(new[] { 4, 5, 6 }, Cost);
        string all = Overrides(x) + (string.IsNullOrEmpty(extra) ? "" : ";" + extra);
        var clip = MakePose(basePath, 0f, outPath, all, 6f, breathe);
        PreviewPose(animator, clip, "Menu DM reach");
        PreviewPose(animator, MakePose(basePath, 0f, null, Overrides(x)), "Menu DM reach");
        {
            Vector3 f = (middle.position - hand.position).normalized;
            Vector3 p = Vector3.Cross(f, index.position - middle.position).normalized;
            Debug.Log($"[Menu] Reach pose cost {best:0.000} fingers.dir {Vector3.Dot(f, fingers.normalized):0.00} palm.up {p.y:0.00} thumb.right {Vector3.Dot((thumb.position - hand.position).normalized, animator.transform.right):0.00}: {Overrides(x)}");
        }
        return clip;
    }

    static System.Collections.Generic.Dictionary<string, float> Values(AnimationClip source, float time, string overrides)
    {
        var values = new System.Collections.Generic.Dictionary<string, float>();
        foreach (var b in AnimationUtility.GetCurveBindings(source))
            if (b.type == typeof(Animator)) values[b.propertyName] = AnimationUtility.GetEditorCurve(source, b).Evaluate(time);
        foreach (string part in (overrides ?? "").Split(';'))
        {
            string p = part.Trim();
            if (p.Length == 0) continue;
            bool replace = p.Contains(":=");
            string[] kv = replace ? p.Split(new[] { ":=" }, System.StringSplitOptions.None) : p.Split('=');
            string key = kv[0].Trim();
            float v = float.Parse(kv[1].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            values.TryGetValue(key, out float baseValue);
            values[key] = replace ? v : baseValue + v;
        }
        return values;
    }

    // Writes a held humanoid pose clip (outPath, or an unsaved preview when it is null): the base clip's muscles and body at baseTime, with `overrides`
    // ("Muscle Name=value;..." added to the base, or "Muscle Name:=value" to replace it). `breathe` adds a slow
    // chest rise and a small head sway over the loop so the held pose lives.
    public static AnimationClip MakePose(string basePath, float baseTime, string outPath, string overrides, float seconds = 5f, float breathe = 0f)
    {
        var values = Values(AssetDatabase.LoadAssetAtPath<AnimationClip>(basePath), baseTime, overrides);
        // A fresh clip every time: an edited clip keeps its old compiled pose in a playable until it is reimported.
        var clip = new AnimationClip { name = string.IsNullOrEmpty(outPath) ? "Pose preview" : Path.GetFileNameWithoutExtension(outPath) };
        foreach (var kv in values)
        {
            float v = kv.Value, swing = 0f;
            if (breathe > 0f)
            {
                if (kv.Key == "Chest Front-Back" || kv.Key == "UpperChest Front-Back") swing = breathe;
                else if (kv.Key == "Head Nod Down-Up") swing = -breathe * .6f;
                else if (kv.Key == "Head Turn Left-Right") swing = breathe * .4f;
                else if (kv.Key.Contains("Shoulder Down-Up")) swing = breathe * .5f;
            }
            var curve = swing == 0f
                ? new AnimationCurve(new Keyframe(0f, v), new Keyframe(seconds, v))
                : new AnimationCurve(new Keyframe(0f, v), new Keyframe(seconds * .5f, v + swing), new Keyframe(seconds, v));
            for (int k = 0; k < curve.length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        if (string.IsNullOrEmpty(outPath)) return clip;    // a preview only
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
        if (existing == null) AssetDatabase.CreateAsset(clip, outPath);
        else { EditorUtility.CopySerialized(clip, existing); existing.name = clip.name; EditorUtility.SetDirty(existing); clip = existing; }
        AssetDatabase.SaveAssets();
        return clip;
    }

    // Writes a looping idle at outPath: the pose clip's muscles held, with `motion` laid over them; entries for the
    // same muscle add up. "Muscle=amplitude@period@phase" is a slow sine (seconds, phase in cycles; periods should
    // divide `seconds` so the loop is seamless). "Muscle=amount@look@t0@t1@t2@t3" is a look: still until t0, eases to
    // `amount` by t1, holds until t2, eases back by t3.
    public static AnimationClip MakeIdle(string posePath, string outPath, string motion, float seconds = 12f, float keyStep = .25f)
    {
        var values = Values(AssetDatabase.LoadAssetAtPath<AnimationClip>(posePath), 0f, null);
        var waves = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<float[]>>();
        foreach (string part in motion.Split(';'))
        {
            string p = part.Trim();
            if (p.Length == 0) continue;
            string[] kv = p.Split('=');
            string[] w = kv[1].Split('@');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (!waves.TryGetValue(kv[0].Trim(), out var list)) waves[kv[0].Trim()] = list = new System.Collections.Generic.List<float[]>();
            if (w[1] == "look") list.Add(new[] { float.Parse(w[0], ci), -1f, float.Parse(w[2], ci), float.Parse(w[3], ci), float.Parse(w[4], ci), float.Parse(w[5], ci) });
            else list.Add(new[] { float.Parse(w[0], ci), float.Parse(w[1], ci), w.Length > 2 ? float.Parse(w[2], ci) : 0f });
        }
        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(outPath) };
        foreach (var kv in values)
        {
            AnimationCurve curve;
            if (!waves.TryGetValue(kv.Key, out var list)) curve = new AnimationCurve(new Keyframe(0f, kv.Value), new Keyframe(seconds, kv.Value));
            else
            {
                curve = new AnimationCurve();
                for (float t = 0f; t <= seconds + 1e-4f; t += keyStep)
                {
                    float v = kv.Value;
                    foreach (var wave in list)
                    {
                        if (wave[1] < 0f)   // a look: ease out, hold, ease back
                        {
                            float k = t < wave[2] || t > wave[5] ? 0f
                                : t < wave[3] ? Mathf.SmoothStep(0f, 1f, (t - wave[2]) / (wave[3] - wave[2]))
                                : t < wave[4] ? 1f
                                : Mathf.SmoothStep(1f, 0f, (t - wave[4]) / (wave[5] - wave[4]));
                            v += wave[0] * k;
                        }
                        else v += wave[0] * Mathf.Sin((t / wave[1] + wave[2]) * Mathf.PI * 2f);
                    }
                    curve.AddKey(t, v);
                }
                for (int k = 0; k < curve.length; k++) curve.SmoothTangents(k, 0f);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), curve);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.loopBlendOrientation = settings.loopBlendPositionY = settings.loopBlendPositionXZ = true;
        settings.keepOriginalOrientation = settings.keepOriginalPositionY = settings.keepOriginalPositionXZ = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outPath);
        if (existing == null) AssetDatabase.CreateAsset(clip, outPath);
        else { EditorUtility.CopySerialized(clip, existing); existing.name = clip.name; EditorUtility.SetDirty(existing); clip = existing; }
        AssetDatabase.SaveAssets();
        return clip;
    }

    // Renders the camera into a PNG of the given size.
    public static void Render(Camera camera, string path, int width = 1280, int height = 720)
    {
        // Skinned meshes otherwise draw with the bones as they were when the frame was skinned, not as just posed.
        var skinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        foreach (var s in skinned) s.forceMatrixRecalculationPerRender = true;
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { sRGB = true, msaaSamples = 4 });
        var previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();
        camera.targetTexture = previous;
        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);
        foreach (var s in skinned) if (s != null) s.forceMatrixRecalculationPerRender = false;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}

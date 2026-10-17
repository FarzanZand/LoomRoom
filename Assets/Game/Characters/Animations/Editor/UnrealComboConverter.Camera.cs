using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The camera's motion for each combo hit, written into its clip as three curves (SwingCamPitch, SwingCamYaw,
// SwingCamRoll, in degrees; PlayerCombat reads them as animator parameters, times CombatManager.comboCameraStrength).
// They come from the clip's own motion of the weapon's head (what the eye follows; the hand can travel the other way,
// as in the chop), so every swing moves the view its own way: the view leans after it with a little lag (a low-pass of
// its speed), so it drifts against the swing in the windup, sweeps with the cut, carries
// past the strike and settles as the hand slows.
// - yaw turns with the head's sideways travel, roll tilts into it (a cut to the left turns and leans the view left);
// - pitch dips with a downward or forward drive (a chop drops the view, a stab nods it), lifts a little on a raise.
// Kept small (sickness): each axis is capped. Written by Wire for every combo set, so every rebuild keeps them; edit the
// numbers here, or the curves by hand after the last rebuild.
public static partial class UnrealComboConverter
{
    public static readonly string[] SwingCameraCurves = { "SwingCamPitch", "SwingCamYaw", "SwingCamRoll" };
    // Degrees per metre a second of the weapon head's speed (clip time), and the most each axis may turn.
    const float CamYawGain = .25f, CamRollGain = .17f, CamPitchDownGain = .28f, CamPitchUpGain = .1f, CamForwardGain = .2f;
    // Where the weapon's head is: this far along the hand socket's up axis (a mace's or sword's length).
    const float CamHeadReach = .7f;
    const float CamYawMax = 2.5f, CamRollMax = 1.6f, CamPitchMax = 2.5f;
    // Seconds (clip time) the view lags the hand by.
    const float CamLag = .07f;

    static void EnsureSwingCameraParameters(AnimatorController controller)
    {
        foreach (var name in SwingCameraCurves)
            if (System.Array.FindIndex(controller.parameters, p => p.name == name) < 0)
                controller.AddParameter(name, AnimatorControllerParameterType.Float);
    }

    static string WriteSwingCamera(List<AnimationClip> clips)
    {
        var body = TargetBody(out var holder);
        int written = 0;
        try
        {
            RestPose(body);
            var rest = new Dictionary<Transform, Quaternion>();
            foreach (var b in body.GetComponentsInChildren<Transform>(true)) rest[b] = b.localRotation;
            var bones = Bones(body);
            var socket = Socket(bones);
            foreach (var clip in clips)
            {
                if (clip == null) continue;
                const float step = 1f / 60f;
                int frames = Mathf.Max(2, Mathf.CeilToInt(clip.length / step));
                var place = new Vector3[frames + 1];
                for (int f = 0; f <= frames; f++)
                {
                    foreach (var pair in rest) pair.Key.localRotation = pair.Value;
                    clip.SampleAnimation(body.gameObject, Mathf.Min(clip.length, f * step));
                    place[f] = body.InverseTransformPoint(socket.position + socket.up * CamHeadReach);
                }
                var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
                Vector3 velocity = Vector3.zero;
                float k = 1f - Mathf.Exp(-step / CamLag);
                for (int f = 0; f <= frames; f++)
                {
                    var raw = (place[Mathf.Min(frames, f + 1)] - place[Mathf.Max(0, f - 1)]) / (2f * step);
                    velocity = Vector3.Lerp(velocity, raw, k);
                    // Eased in over the first frames (no jump as a hit starts) and out over the last fifth (back at rest).
                    float fade = Mathf.Min(Mathf.SmoothStep(0f, 1f, f / (frames * .08f)), Mathf.Clamp01((frames - f) / (frames * .2f)));
                    float pitch = (velocity.y < 0f ? -velocity.y * CamPitchDownGain : -velocity.y * CamPitchUpGain) + Mathf.Max(0f, velocity.z) * CamForwardGain;
                    float yaw = velocity.x * CamYawGain;
                    float roll = -velocity.x * CamRollGain;
                    float t = Mathf.Min(clip.length, f * step);
                    curves[0].AddKey(t, Mathf.Clamp(pitch, -CamPitchMax, CamPitchMax) * fade);
                    curves[1].AddKey(t, Mathf.Clamp(yaw, -CamYawMax, CamYawMax) * fade);
                    curves[2].AddKey(t, Mathf.Clamp(roll, -CamRollMax, CamRollMax) * fade);
                }
                for (int i = 0; i < 3; i++)
                {
                    for (int key = 0; key < curves[i].length; key++) curves[i].SmoothTangents(key, 0f);
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), SwingCameraCurves[i]), curves[i]);
                }
                EditorUtility.SetDirty(clip);
                written++;
            }
            AssetDatabase.SaveAssets();
        }
        finally
        {
            Object.DestroyImmediate(body.gameObject);
            if (holder != null) Object.DestroyImmediate(holder);
        }
        return $"swing camera curves on {written} clips";
    }
}

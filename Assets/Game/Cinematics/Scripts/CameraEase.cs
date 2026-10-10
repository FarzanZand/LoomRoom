using System.Collections;
using UnityEngine;

// Scripted moves of a player's view (PlayerLook and PlayerCameraRig) for cutscene-like moments: the
// yaw and pitch that look at a point, an eased turn to them, and an eased zoom. Shared by
// IntroController and TableManager. Run the IEnumerators as coroutines on the caller.
public static class CameraEase
{
    // Yaw and pitch (degrees) from `eye` to `point`. A point straight above or below keeps fallbackYaw.
    public static void YawPitchTo(Vector3 eye, Vector3 point, out float yaw, out float pitch, float fallbackYaw = 0f)
    {
        var d = point - eye;
        var flat = new Vector3(d.x, 0f, d.z);
        yaw = flat.sqrMagnitude < 1e-4f ? fallbackYaw : Quaternion.LookRotation(flat).eulerAngles.y;
        pitch = Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg;
    }

    // From the view's own eye, keeping its yaw when the point is straight above or below.
    public static void YawPitchTo(PlayerLook look, Vector3 point, out float yaw, out float pitch)
        => YawPitchTo(look.PitchTransform.position, point, out yaw, out pitch, look.YawTransform.eulerAngles.y);

    // Eases the view from where it is to (yaw, pitch), ending exactly there. A height holds the eye at
    // that height (HeightOverride) throughout; null leaves it alone.
    public static IEnumerator Turn(PlayerLook look, float yaw, float pitch, float seconds, float? height = null)
    {
        if (look == null) yield break;
        float fromYaw = look.YawTransform.eulerAngles.y, fromPitch = look.Pitch;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            if (height.HasValue) look.HeightOverride = height;
            look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
            look.SetPitch(Mathf.Lerp(fromPitch, pitch, k));
            yield return null;
        }
        if (height.HasValue) look.HeightOverride = height;
        look.SetYaw(yaw);
        look.SetPitch(pitch);
    }

    // Eases the view to look at a point (aimed from where the eye is when the turn starts).
    public static IEnumerator LookAt(PlayerLook look, Vector3 point, float seconds, float? height = null)
    {
        if (look == null) yield break;
        YawPitchTo(look, point, out float yaw, out float pitch);
        yield return Turn(look, yaw, pitch, seconds, height);
    }

    // Field of view offset to `to` (negative zooms in), smoothed at both ends or linear.
    public static IEnumerator Zoom(PlayerCameraRig rig, float to, float seconds, bool smooth = true)
    {
        if (rig == null) yield break;
        float from = rig.FovOffset;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            rig.FovOffset = Mathf.Lerp(from, to, smooth ? Mathf.SmoothStep(0f, 1f, k) : k);
            yield return null;
        }
        rig.FovOffset = to;
    }
}

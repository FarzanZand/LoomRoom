#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// Review harness for motion seen from outside the player's eyes (CLAUDE.md: animations are also reviewed from the side
// and from behind). Records a take at a fixed 30 fps into Temp/Spectator/<name>/:
//   owner/NNN.png   the game view
//   side/NNN.png    a camera following the target from 90 degrees off the player's view (to its right)
//   back/NNN.png    the same from 180 degrees (looking back at the player across the target)
//   frames.csv      the target's and each tracked bone's position per frame (heights to measure)
// Started from an eval in Play mode, e.g. for the Dungeon Master's arm:
//   SpectatorCapture.Run(6f, "flick", SpectatorCapture.DungeonMasterArm(), 2.5f)
public class SpectatorCapture : MonoBehaviour
{
    const int Width = 640, Height = 640;

    Transform[] tracked;
    float distance;
    float seconds;
    string dir;

    // tracked[0] is framed; the rest are written to frames.csv as well. distance in metres from the framed point.
    public static SpectatorCapture Run(float seconds, string name, Transform[] tracked, float distance = 2.5f)
    {
        if (tracked == null || tracked.Length == 0 || tracked[0] == null) { Debug.LogWarning("[SpectatorCapture] Nothing to follow."); return null; }
        var go = new GameObject("Spectator capture");
        var capture = go.AddComponent<SpectatorCapture>();
        capture.tracked = tracked;
        capture.distance = distance;
        capture.seconds = seconds;
        capture.dir = Path.Combine(Application.dataPath, "..", "Temp", "Spectator", name);
        capture.StartCoroutine(capture.Record());
        return capture;
    }

    // The Dungeon Master's right arm: the elbow framed (the arm's middle), then the shoulder, hand and index tip.
    public static Transform[] DungeonMasterArm()
    {
        var hand = DungeonMasterReach.Hand;
        var animator = hand != null ? hand.GetComponentInParent<Animator>() : null;
        if (animator == null && TableManager.HasInstance && TableManager.Instance.DM != null) animator = TableManager.Instance.DM.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return null;
        return new[]
        {
            animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
            animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
            animator.GetBoneTransform(HumanBodyBones.RightHand),
            animator.GetBoneTransform(HumanBodyBones.RightIndexDistal) ?? animator.GetBoneTransform(HumanBodyBones.RightHand),
            animator.GetBoneTransform(HumanBodyBones.Head),
        };
    }

    IEnumerator Record()
    {
        foreach (var sub in new[] { "owner", "side", "back" }) Directory.CreateDirectory(Path.Combine(dir, sub));
        // The camera the game is seen through (the player's rig drives it through Cinemachine).
        var eye = Camera.main;
        var cam = new GameObject("Spectator camera").AddComponent<Camera>();
        if (eye != null) cam.CopyFrom(eye);
        cam.enabled = false;
        cam.cullingMask = ~(1 << LayerMask.NameToLayer("ViewModel"));
        cam.fieldOfView = 50f;
        cam.nearClipPlane = .05f;
        cam.farClipPlane = 500f;
        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var read = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        var csv = new StringBuilder("frame,time");
        for (int b = 0; b < tracked.Length; b++) csv.Append($",{Name(b)}_x,{Name(b)}_y,{Name(b)}_z");
        csv.AppendLine();

        int previous = Time.captureFramerate;
        Time.captureFramerate = 30;
        int frames = Mathf.CeilToInt(seconds * 30f);
        try
        {
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, "owner", $"{i:000}.png"), shot.EncodeToPNG());
                Destroy(shot);
                if (tracked[0] == null) break;
                // The player's view, flat: side looks across it from the right, back looks back at the player.
                var forward = eye != null ? Vector3.ProjectOnPlane(eye.transform.forward, Vector3.up).normalized : Vector3.forward;
                if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
                var focus = tracked[0].position;
                Shoot(cam, rt, read, focus, Quaternion.Euler(0f, -90f, 0f) * forward, Path.Combine(dir, "side", $"{i:000}.png"));
                Shoot(cam, rt, read, focus, -forward, Path.Combine(dir, "back", $"{i:000}.png"));
                csv.Append(i).Append(',').Append(F(Time.time));
                foreach (var t in tracked) csv.Append(',').Append(t != null ? $"{F(t.position.x)},{F(t.position.y)},{F(t.position.z)}" : ",,");
                csv.AppendLine();
            }
        }
        finally
        {
            Time.captureFramerate = previous;
            File.WriteAllText(Path.Combine(dir, "frames.csv"), csv.ToString());
            cam.targetTexture = null; rt.Release(); Destroy(cam.gameObject); Destroy(read);
            Debug.Log($"[SpectatorCapture] {frames} frames to {Path.GetFullPath(dir)}");
            Destroy(gameObject);
        }
    }

    // A camera standing at distance along `from` from the focus (a little above it), looking at it.
    void Shoot(Camera cam, RenderTexture rt, Texture2D read, Vector3 focus, Vector3 from, string path)
    {
        var at = focus + from * distance + Vector3.up * distance * .25f;
        cam.transform.SetPositionAndRotation(at, Quaternion.LookRotation(focus - at, Vector3.up));
        cam.Render();
        RenderTexture.active = rt;
        read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        read.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(path, read.EncodeToPNG());
    }

    string Name(int b) => tracked[b] != null ? tracked[b].name.Replace(',', ' ') : $"bone{b}";
    static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    void OnDestroy() { if (Time.captureFramerate == 30) Time.captureFramerate = 0; }
}
#endif

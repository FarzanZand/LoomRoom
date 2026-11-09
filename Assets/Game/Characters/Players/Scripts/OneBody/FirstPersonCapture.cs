#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

// R&D harness (one body, true first person): records a take of the table player for frame-by-frame comparison
// between master and the onebody branch. The same file runs in both: it finds the first-person animator by its object
// name (TableArmsMesh) and the body others see (master's BodyVisuals or the branch's OneBodyRig).
// Per frame, into Temp/FirstPerson/<name>/:
//   owner/NNN.png            the game view
//   arms/NNN.png             the first-person body and held items alone over black, through the owner's camera
//                            (the strict pixel check: the world's flicker and fades are left out)
//   outside/NNN.png          the player's body from 3 m (front, side or back), the owner's view-model layer left out
//   frames.csv               camera position, rotation and field of view; the arm, spine and head bones; on-screen
//                            (viewport) positions of the hands, elbows, blade base and tip, shield centre and facing;
//                            each animator layer's state, tag, time, weight and next state. Full float precision.
//   events.csv               every animation event the first-person animator passes (frame, layer, clip, function)
//   hits.csv                 every hit on an enemy (frame, target, damage, point)
// Call it in the same frame as a CombatTestBot script: the fixed clock (30 fps), Random's seed and the animator
// restart start here. freezeEnemies switches every enemy's brain off for the take (their AI draws Random each frame).
public class FirstPersonCapture : MonoBehaviour
{
    public enum View { Front = 0, Side = 1, Back = 2 }

    static readonly string[] Bones = { "Spine_01", "Spine_02", "Spine_03", "Neck", "Head",
        "Clavicle_R", "Shoulder_R", "Elbow_R", "Hand_R", "Clavicle_L", "Shoulder_L", "Elbow_L", "Hand_L" };
    const int Width = 480, Height = 600;

    int previousFramerate;
    readonly List<(int frame, Vector3 point, string target, float damage)> hits = new();
    readonly List<(Character who, System.Action<DamageInfo> handler)> listening = new();
    readonly List<Behaviour> frozen = new();

    public static FirstPersonCapture Run(float seconds, string name, int seed = 1234, View view = View.Front, bool freezeEnemies = true)
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        // The first-person arms (TableArmsMesh) and the animator that moves them: their own on master, the one body's on
        // the onebody branch (the arms there are a copy posed from the body).
        Transform armsT = null;
        if (player != null) foreach (var t in player.transform.root.GetComponentsInChildren<Transform>(true)) if (t.name == "TableArmsMesh") armsT = t;
        if (armsT == null) { Debug.LogWarning("[FirstPersonCapture] No TableArmsMesh."); return null; }
        var arms = armsT.GetComponent<Animator>();
        if (arms == null)
            foreach (var mb in player.transform.root.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "OneBodyRig") arms = mb.GetType().GetField("animator").GetValue(mb) as Animator;
        if (arms == null) { Debug.LogWarning("[FirstPersonCapture] No first-person animator."); return null; }
        var capture = new GameObject("First person capture").AddComponent<FirstPersonCapture>();
        foreach (var c in FindObjectsByType<Character>())
        {
            if (c is Player) continue;
            var who = c;
            System.Action<DamageInfo> handler = info => capture.hits.Add((Time.frameCount, info.HitPoint, who.name, info.Amount));
            c.Damaged += handler;
            capture.listening.Add((c, handler));
            if (freezeEnemies)
                foreach (var b in c.GetComponentsInChildren<Behaviour>())
                    if (b != null && b.enabled && b.GetType().Name == "EnemyBrain") { b.enabled = false; capture.frozen.Add(b); }
        }
        capture.previousFramerate = Time.captureFramerate;
        Time.captureFramerate = 30;
        Random.InitState(seed);
        for (int l = 0; l < arms.layerCount; l++) arms.Play(arms.GetCurrentAnimatorStateInfo(l).fullPathHash, l, 0f);
        capture.StartCoroutine(capture.Record(seconds, name, player, armsT, arms, view));
        return capture;
    }

    IEnumerator Record(float seconds, string name, Player player, Transform armsT, Animator arms, View view)
    {
        string dir = Path.Combine("Temp", "FirstPerson", name);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(Path.Combine(dir, "owner"));
        Directory.CreateDirectory(Path.Combine(dir, "outside"));
        Directory.CreateDirectory(Path.Combine(dir, "arms"));
        var output = PlayerManager.Instance.OutputCamera;
        // The first-person body alone: its renderers move to an empty layer for one render through the owner's camera.
        var armsCam = new GameObject("Arms camera").AddComponent<Camera>();
        armsCam.CopyFrom(output);
        armsCam.enabled = false;
        armsCam.clearFlags = CameraClearFlags.SolidColor;
        armsCam.backgroundColor = Color.black;
        armsCam.cullingMask = 1 << 31;
        var armsRt = new RenderTexture(960, 540, 24);
        armsCam.targetTexture = armsRt;
        var armsRead = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var alsoOwner = System.Type.GetType("OneBodyRig, Assembly-CSharp")?.GetField("AlsoOwner");
        alsoOwner?.SetValue(null, armsCam);
        var armsRenderers = new List<Renderer>();
        var armsLayers = new List<int>();

        // The body others see: the branch's one body, or master's BodyVisuals.
        Transform body = null;
        foreach (var mb in player.transform.root.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && mb.GetType().Name == "OneBodyRig") body = mb.transform;
        if (body == null) foreach (var t in player.transform.root.GetComponentsInChildren<Transform>(true)) if (t.name == "BodyVisuals" && t.IsChildOf(player.transform.parent)) body = t;
        var cam = new GameObject("Outside camera").AddComponent<Camera>();
        cam.CopyFrom(output);
        cam.enabled = false;
        cam.cullingMask = ~(1 << LayerMask.NameToLayer("ViewModel"));
        cam.fieldOfView = 40f;
        cam.nearClipPlane = .05f;
        var rt = new RenderTexture(Width, Height, 24);
        cam.targetTexture = rt;
        var read = new Texture2D(Width, Height, TextureFormat.RGB24, false);

        var bones = new Dictionary<string, Transform>();
        foreach (var t in armsT.GetComponentsInChildren<Transform>(true))
            if (!bones.ContainsKey(t.name) && (System.Array.IndexOf(Bones, t.name) >= 0 || t.name.EndsWith("Hand_Socket"))) bones[t.name] = t;

        var csv = new StringBuilder("frame,time,cam_px,cam_py,cam_pz,cam_rx,cam_ry,cam_rz,cam_rw,cam_fov");
        foreach (var b in Bones) csv.Append($",{b}_px,{b}_py,{b}_pz,{b}_rx,{b}_ry,{b}_rz,{b}_rw");
        foreach (var p in new[] { "hand_R", "hand_L", "elbow_R", "elbow_L", "blade_base", "blade_tip", "shield_centre", "shield_facing" })
            csv.Append($",{p}_vx,{p}_vy,{p}_vz");
        for (int l = 0; l < arms.layerCount; l++) csv.Append($",layer{l}_state,layer{l}_tag,layer{l}_time,layer{l}_weight,layer{l}_next");
        csv.AppendLine();
        var events = new StringBuilder("frame,layer,clip,function,time\n");
        var hitCsv = new StringBuilder("frame,target,damage,hit_x,hit_y,hit_z\n");
        var lastTimes = new Dictionary<(int, int), float>();

        int frames = Mathf.CeilToInt(seconds * 30f);
        for (int i = 0; i < frames; i++)
        {
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, "owner", $"{i:000}.png"), shot.EncodeToPNG());
            Destroy(shot);

            armsCam.transform.SetPositionAndRotation(output.transform.position, output.transform.rotation);
            armsCam.fieldOfView = output.fieldOfView;
            armsT.GetComponentsInChildren(false, armsRenderers);
            armsLayers.Clear();
            foreach (var r in armsRenderers) { armsLayers.Add(r.gameObject.layer); r.gameObject.layer = 31; }
            armsCam.Render();
            for (int k = 0; k < armsRenderers.Count; k++) armsRenderers[k].gameObject.layer = armsLayers[k];
            RenderTexture.active = armsRt;
            armsRead.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            armsRead.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(dir, "arms", $"{i:000}.png"), armsRead.EncodeToPNG());

            var c = output.transform;
            csv.Append(i).Append(',').Append(F(Time.time)).Append(',').Append(V(c.position)).Append(',').Append(Q(c.rotation)).Append(',').Append(F(output.fieldOfView));
            foreach (var b in Bones)
                if (bones.TryGetValue(b, out var t)) csv.Append(',').Append(V(t.position)).Append(',').Append(Q(t.rotation));
                else csv.Append(",,,,,,,");
            bones.TryGetValue("RightHand_Socket", out var rs);
            bones.TryGetValue("LeftHand_Socket", out var ls);
            bones.TryGetValue("Hand_R", out var hr); bones.TryGetValue("Hand_L", out var hl);
            bones.TryGetValue("Elbow_R", out var er); bones.TryGetValue("Elbow_L", out var el);
            Vector3 VP(Transform t) => t != null ? output.WorldToViewportPoint(t.position) : Vector3.zero;
            var shield = Centre(ls);
            foreach (var p in new[] { VP(hr), VP(hl), VP(er), VP(el), rs != null ? output.WorldToViewportPoint(rs.position) : Vector3.zero,
                         rs != null ? output.WorldToViewportPoint(Tip(rs)) : Vector3.zero, output.WorldToViewportPoint(shield.centre),
                         output.WorldToViewportPoint(shield.centre + shield.facing * .1f) })
                csv.Append(',').Append(V(p));
            for (int l = 0; l < arms.layerCount; l++)
            {
                var s = arms.GetCurrentAnimatorStateInfo(l);
                int next = arms.IsInTransition(l) ? arms.GetNextAnimatorStateInfo(l).shortNameHash : 0;
                csv.Append(',').Append(s.shortNameHash).Append(',').Append(s.tagHash).Append(',').Append(F(s.normalizedTime))
                   .Append(',').Append(F(arms.GetLayerWeight(l))).Append(',').Append(next);
                LogEvents(events, i, arms, l, s, lastTimes);
            }
            csv.AppendLine();
            foreach (var h in hits)
                if (h.frame == Time.frameCount)
                    hitCsv.Append(i).Append(',').Append(h.target.Replace(',', ' ')).Append(',').Append(F(h.damage)).Append(',').Append(V(h.point)).AppendLine();

            if (body != null)
            {
                Frame(cam, body, view);
                cam.Render();
                RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                read.Apply();
                RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, "outside", $"{i:000}.png"), read.EncodeToPNG());
            }
        }
        File.WriteAllText(Path.Combine(dir, "frames.csv"), csv.ToString());
        File.WriteAllText(Path.Combine(dir, "events.csv"), events.ToString());
        File.WriteAllText(Path.Combine(dir, "hits.csv"), hitCsv.ToString());
        cam.targetTexture = null; rt.Release(); Destroy(cam.gameObject); Destroy(read);
        alsoOwner?.SetValue(null, null);
        armsCam.targetTexture = null; armsRt.Release(); Destroy(armsCam.gameObject); Destroy(armsRead);
        Debug.Log($"[FirstPersonCapture] Captured {frames} frames to {dir}");
        Destroy(gameObject);
    }

    // Events of the clips playing on a layer that its time passed this frame (loops counted).
    static void LogEvents(StringBuilder events, int frame, Animator arms, int layer, AnimatorStateInfo state, Dictionary<(int, int), float> last)
    {
        var key = (layer, state.fullPathHash);
        float now = state.normalizedTime;
        if (!last.TryGetValue(key, out var before) || before > now) before = now - Time.deltaTime * Mathf.Abs(state.speed * state.speedMultiplier) / Mathf.Max(.0001f, state.length);
        last[key] = now;
        foreach (var info in arms.GetCurrentAnimatorClipInfo(layer))
        {
            var clip = info.clip;
            if (clip == null || clip.length <= 0f) continue;
            foreach (var e in clip.events)
            {
                float t = e.time / clip.length;
                for (float k = Mathf.Floor(before); k <= Mathf.Floor(now); k++)
                    if (k + t > before && k + t <= now)
                        events.Append(frame).Append(',').Append(layer).Append(',').Append(clip.name).Append(',').Append(e.functionName).Append(',').Append(F(e.time)).AppendLine();
            }
        }
    }

    static void Frame(Camera cam, Transform body, View view)
    {
        Vector3 local = view == View.Front ? new Vector3(.55f, .12f, 1f) : view == View.Side ? new Vector3(1f, .12f, .05f) : new Vector3(-.4f, .15f, -1f);
        var chest = body.position + Vector3.up * 1.2f;
        var yaw = Quaternion.Euler(0f, body.eulerAngles.y, 0f);
        cam.transform.position = chest + yaw * local.normalized * 3f;
        cam.transform.LookAt(chest);
    }

    // The blade's tip: the far end of what the hand holds along the socket's up (which runs along the blade).
    static Vector3 Tip(Transform socket)
    {
        Vector3 hand = socket.position, up = socket.up;
        float far = 0f;
        foreach (var f in socket.GetComponentsInChildren<MeshFilter>())
        {
            if (f.sharedMesh == null) continue;
            var b = f.sharedMesh.bounds;
            for (int k = 0; k < 8; k++)
            {
                var p = f.transform.TransformPoint(new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z));
                far = Mathf.Max(far, Vector3.Dot(p - hand, up));
            }
        }
        return hand + up * far;
    }

    static (Vector3 centre, Vector3 facing) Centre(Transform socket)
    {
        if (socket == null) return (Vector3.zero, Vector3.forward);
        foreach (var r in socket.GetComponentsInChildren<MeshRenderer>())
            return (r.bounds.center, r.transform.forward);
        return (socket.position, socket.forward);
    }

    static string F(float v) => v.ToString("G9", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"{F(v.x)},{F(v.y)},{F(v.z)}";
    static string Q(Quaternion q) => $"{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}";

    void OnDestroy()
    {
        Time.captureFramerate = previousFramerate;
        foreach (var (who, handler) in listening) if (who != null) who.Damaged -= handler;
        foreach (var b in frozen) if (b != null) b.enabled = true;
    }
}
#endif

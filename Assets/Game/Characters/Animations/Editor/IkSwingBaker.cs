using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// The IK rig behind the eat and drink clips (IkSwingBaker.Consume.cs): the right arm solved with two-bone IK toward a
// hand path written in camera space (x right, y up, z forward, metres, before the view-model widening), every frame
// keyed into an ordinary .anim clip you can open and edit. It once baked the IK light swing set too (retired 2026-11-05
// with its clips).
public static partial class IkSwingBaker
{
    const string Controller = "Assets/Game/Characters/Animations/FirstPersonTable.controller";
    const string TablePlayer = "Assets/Game/Characters/Players/Table/TablePlayer.prefab";
    const string IdleClip = "Assets/Game/Characters/Animations/FirstPersonPlayer/Idle.anim";
    const string GripClip = "Assets/Game/Characters/Animations/FirstPersonPlayer/Attack_Swing_A.anim";
    const float Fps = 60f;

    // A point on the blade's path: grip position, blade direction, and the normal of the blade's flat.
    struct Key
    {
        public float time; public Vector3 grip, blade, flat;
        public Key(float time, Vector3 grip, Vector3 blade, Vector3 flat) { this.time = time; this.grip = grip; this.blade = blade; this.flat = flat; }
    }

    // ── Rig ───────────────────────────────────────────────────────────

    class Rig
    {
        public GameObject instance, root, arms;  // root = the camera frame; arms = the animated mesh (clip paths are relative to it)
        public Transform shoulder, elbow, hand, socket;
        public Transform[] keyed;                // every right-arm transform the clips key
        public Dictionary<Transform, Quaternion> idle = new(), grip = new();
        public void Destroy() => Object.DestroyImmediate(instance);
    }

    static Rig BuildRig()
    {
        // A whole player instance, so the arm mesh skins to the bones we pose. FirstPersonVisuals sits on the
        // camera in play; here it is the camera frame, without the view-model widening.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TablePlayer));
        instance.hideFlags = HideFlags.DontSave;
        instance.transform.position = new Vector3(0f, -500f, 0f);
        Transform visuals = null;
        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) if (t.name == "FirstPersonVisuals") { visuals = t; break; }
        if (visuals == null) { Object.DestroyImmediate(instance); throw new System.Exception("FirstPersonVisuals not found in TablePlayer"); }
        visuals.localScale = Vector3.one;
        var rig = new Rig { instance = instance, root = visuals.gameObject };
        foreach (var t in instance.GetComponentsInChildren<Transform>(true)) if (t.name == "TableArmsOffset") { rig.arms = t.GetComponentInChildren<Animator>(true).gameObject; break; }
        foreach (var t in rig.arms.GetComponentsInChildren<Transform>(true))
            switch (t.name)
            {
                case "Shoulder_R": rig.shoulder = t; break;
                case "Elbow_R": rig.elbow = t; break;
                case "Hand_R": rig.hand = t; break;
                case "RightHand_Socket": rig.socket = t; break;
            }
        var keyed = new List<Transform>();
        foreach (var t in rig.shoulder.parent.GetComponentsInChildren<Transform>(true)) if (t != rig.socket && !t.IsChildOf(rig.socket)) keyed.Add(t);
        rig.keyed = keyed.ToArray();
        AssetDatabase.LoadAssetAtPath<AnimationClip>(GripClip).SampleAnimation(rig.arms, 0f);
        foreach (var t in rig.keyed) rig.grip[t] = t.localRotation;
        AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip).SampleAnimation(rig.arms, 0f);
        foreach (var t in rig.keyed) rig.idle[t] = t.localRotation;
        return rig;
    }

    // Pose: the idle arm with the gripping fingers, then the shoulder, elbow and hand solved toward the key.
    static Dictionary<Transform, Quaternion> Solve(Rig rig, Key key, Vector3 hint)
    {
        foreach (var t in rig.keyed) t.localRotation = rig.idle[t];
        foreach (var t in rig.keyed) if (t != rig.shoulder && t != rig.elbow && t != rig.hand && t.IsChildOf(rig.hand)) t.localRotation = rig.grip[t];
        var cam = rig.root.transform;
        Vector3 blade = cam.TransformDirection(key.blade.normalized);
        Vector3 flat = cam.TransformDirection(Vector3.ProjectOnPlane(key.flat, key.blade).normalized);
        // The socket's up runs along the blade and its right is the flat's normal.
        Quaternion socketRot = Quaternion.LookRotation(Vector3.Cross(flat, blade), blade);
        Quaternion handRot = socketRot * Quaternion.Inverse(rig.socket.localRotation);
        Vector3 handPos = cam.TransformPoint(key.grip) - handRot * Vector3.Scale(rig.socket.localPosition, rig.hand.lossyScale);
        TwoBoneIK(rig.shoulder, rig.elbow, rig.hand, handPos, rig.shoulder.position + cam.TransformDirection(hint));
        rig.hand.rotation = handRot;
        var pose = new Dictionary<Transform, Quaternion>();
        foreach (var t in rig.keyed) pose[t] = t.localRotation;
        return pose;
    }

    // Two-bone IK (as in Animation Rigging): bend the elbow to the target's distance, aim the shoulder at it,
    // then turn the arm about the shoulder-to-target line so the elbow points at the hint.
    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        Vector3 ab = b.position - a.position, bc = c.position - b.position, ac = c.position - a.position, at = target - a.position;
        float abLen = ab.magnitude, bcLen = bc.magnitude;
        float atLen = Mathf.Clamp(at.magnitude, Mathf.Abs(abLen - bcLen) + .001f, abLen + bcLen - .001f);
        float oldAngle = TriangleAngle(ac.magnitude, abLen, bcLen), newAngle = TriangleAngle(atLen, abLen, bcLen);
        Vector3 axis = Vector3.Cross(ab, bc);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(hint - a.position, bc);
        axis.Normalize();
        b.rotation = Quaternion.AngleAxis((oldAngle - newAngle) * Mathf.Rad2Deg, axis) * b.rotation;
        a.rotation = Quaternion.FromToRotation(c.position - a.position, at) * a.rotation;
        Vector3 acNow = c.position - a.position;
        Vector3 abProj = Vector3.ProjectOnPlane(b.position - a.position, acNow), ahProj = Vector3.ProjectOnPlane(hint - a.position, acNow);
        if (abProj.sqrMagnitude > 1e-6f && ahProj.sqrMagnitude > 1e-6f) a.rotation = Quaternion.FromToRotation(abProj, ahProj) * a.rotation;
    }

    static float TriangleAngle(float a, float b, float c) => Mathf.Acos(Mathf.Clamp((b * b + c * c - a * a) / (2f * b * c), -1f, 1f));

    static Key Lerp(Key x, Key y, float t) => new(Mathf.Lerp(x.time, y.time, t), Vector3.Lerp(x.grip, y.grip, t),
        Vector3.Slerp(x.blade.normalized, y.blade.normalized, t), Vector3.Slerp(x.flat, y.flat, t));

    static Key Sample(Key[] keys, float time)
    {
        if (time <= keys[0].time) return keys[0];
        for (int i = 1; i < keys.Length; i++)
            if (time <= keys[i].time)
            {
                float t = Mathf.InverseLerp(keys[i - 1].time, keys[i].time, time);
                // The last segment is the held follow-through: settle into it.
                if (i == keys.Length - 1) t = Mathf.SmoothStep(0f, 1f, t);
                return Lerp(keys[i - 1], keys[i], t);
            }
        return keys[keys.Length - 1];
    }

    static Dictionary<Transform, Quaternion> Blend(Rig rig, Dictionary<Transform, Quaternion> a, Dictionary<Transform, Quaternion> b, float t)
    {
        var pose = new Dictionary<Transform, Quaternion>();
        foreach (var x in rig.keyed) pose[x] = Quaternion.Slerp(a[x], b[x], t);
        return pose;
    }

    // ── Clips ─────────────────────────────────────────────────────────

    static AnimationClip Write(Rig rig, string name, List<(float time, Dictionary<Transform, Quaternion> pose)> frames, bool loop, AnimationEvent[] events, string folder)
    {
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
        string path = $"{folder}/{name}.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, path); }
        clip.ClearCurves();
        clip.frameRate = Fps;
        foreach (var t in rig.keyed)
        {
            string bonePath = AnimationUtility.CalculateTransformPath(t, rig.arms.transform);
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            Vector3 previous = Vector3.zero;
            for (int i = 0; i < frames.Count; i++)
            {
                Vector3 e = frames[i].pose[t].eulerAngles;
                if (i > 0) for (int k = 0; k < 3; k++) e[k] = previous[k] + Mathf.DeltaAngle(previous[k], e[k]); // keep curves continuous
                previous = e;
                for (int k = 0; k < 3; k++) curves[k].AddKey(new Keyframe(frames[i].time, e[k]));
            }
            for (int k = 0; k < 3; k++)
            {
                for (int i = 0; i < curves[k].length; i++) AnimationUtility.SetKeyLeftTangentMode(curves[k], i, AnimationUtility.TangentMode.ClampedAuto);
                for (int i = 0; i < curves[k].length; i++) AnimationUtility.SetKeyRightTangentMode(curves[k], i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "localEulerAnglesRaw." + "xyz"[k]), curves[k]);
            }
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAnimationEvents(clip, events ?? new AnimationEvent[0]);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimationEvent Event(string function, float time) => new() { functionName = function, time = time };

    static List<(float, Dictionary<Transform, Quaternion>)> Frames(float length, System.Func<float, Dictionary<Transform, Quaternion>> pose)
    {
        var frames = new List<(float, Dictionary<Transform, Quaternion>)>();
        int count = Mathf.RoundToInt(length * Fps);
        for (int i = 0; i <= count; i++) { float t = i / Fps; frames.Add((t, pose(t))); }
        return frames;
    }

    static void Param(AnimatorController ctrl, string name, AnimatorControllerParameterType type, float value = 0f)
    {
        if (System.Array.Exists(ctrl.parameters, p => p.name == name)) return;
        ctrl.AddParameter(new AnimatorControllerParameter { name = name, type = type, defaultFloat = value });
    }

    static void Transition(AnimatorState from, AnimatorState to, float exitTime, float duration, params (string param, AnimatorConditionMode mode, float threshold)[] conditions)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = exitTime >= 0f;
        if (exitTime >= 0f) t.exitTime = exitTime;
        t.hasFixedDuration = true;
        t.duration = duration;
        foreach (var c in conditions) t.AddCondition(c.mode, c.threshold, c.param);
    }

    // ── Previews ──────────────────────────────────────────────────────

    // Renders clips from the player's eye into PNG strips (8 frames a row) with an item in the right hand, placed as
    // Equipment places it. step: every how many frames (at 60 fps) one is drawn.
    public static void RenderSheets(string[] names, string folder, ItemData item, string output, int step = 1)
    {
        var rig = BuildRig();
        var camGo = new GameObject("Preview camera") { hideFlags = HideFlags.DontSave };
        try
        {
            var cam = camGo.AddComponent<Camera>();
            camGo.transform.SetParent(rig.root.transform, false);
            cam.fieldOfView = 70f; cam.nearClipPlane = .02f; cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .2f, .24f);
            cam.cullingMask = 1 << rig.arms.layer;
            var lightGo = new GameObject("Preview light") { hideFlags = HideFlags.DontSave };
            lightGo.transform.SetParent(rig.root.transform, false);
            lightGo.transform.localRotation = Quaternion.Euler(40f, -30f, 0f);
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            if (item != null && item.worldPrefab != null)
            {
                var obj = (GameObject)PrefabUtility.InstantiatePrefab(item.worldPrefab, rig.socket);
                obj.transform.SetLocalPositionAndRotation(item.heldPosition, Quaternion.Euler(item.heldRotation));
                obj.transform.localScale *= Mathf.Max(.01f, item.heldScale);
                foreach (var t in obj.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = rig.arms.layer;
            }
            // Skinning does not refresh between manual renders in edit mode: draw a baked copy of each skinned mesh.
            var baked = new List<(SkinnedMeshRenderer source, Mesh mesh, Transform copy)>();
            foreach (var smr in rig.arms.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                var copy = new GameObject(smr.name + " (baked)") { hideFlags = HideFlags.DontSave, layer = rig.arms.layer };
                copy.transform.SetParent(rig.root.transform, true);
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
                baked.Add((smr, mesh, copy.transform));
            }
            var rt = new RenderTexture(320, 180, 24); cam.targetTexture = rt;
            Directory.CreateDirectory(output);
            foreach (var name in names)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{folder}/{name}.anim");
                if (clip == null) continue;
                int count = Mathf.RoundToInt(clip.length * Fps) + 1;
                var frames = new List<Texture2D>();
                for (int i = 0; i < count; i += Mathf.Max(1, step))
                {
                    clip.SampleAnimation(rig.arms, i / Fps);
                    foreach (var (source, mesh, copy) in baked)
                    {
                        source.BakeMesh(mesh, true);
                        copy.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    }
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                    frames.Add(tex);
                }
                RenderTexture.active = null;
                int cols = 8, rows = (frames.Count + cols - 1) / cols;
                var sheet = new Texture2D(cols * rt.width, rows * rt.height, TextureFormat.RGB24, false);
                for (int i = 0; i < frames.Count; i++)
                {
                    int x = i % cols * rt.width, y = (rows - 1 - i / cols) * rt.height;
                    sheet.SetPixels(x, y, rt.width, rt.height, frames[i].GetPixels());
                    Object.DestroyImmediate(frames[i]);
                }
                sheet.Apply();
                File.WriteAllBytes($"{output}/{name}.png", sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
            }
            Object.DestroyImmediate(rt);
        }
        finally { Object.DestroyImmediate(camGo); rig.Destroy(); }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.ShortcutManagement;
using UnityEngine;

// Tools > LoomRoom > Characters > Animation Workbench: make and edit the table player's animation clips while seeing
// them in first person and from outside at the same time, in Edit mode.
// - Pick a clip (any clip of FirstPersonTable, or any other) and what the hands hold; the window plays it on a hidden
//   copy of the table player as the game would on its layer, over the other layers' resting pose.
// - Left: first person, through the game's 85 degree camera with the arms squashed to the 70 degree view model, as in
//   play. Right: the same body from outside (drag to orbit, scroll to zoom), arms carried as others see them.
// - Edit keys in Unity's Animation window: Select rig, then pick the clip there and press record. Both views follow
//   every change and the Animation window's time; the clip is an ordinary .anim asset.
// - The blade's path through the clip can be traced in both views; animation events show as marks on the timeline.
// - The Scene view buttons (and Shift+Alt+O, E, F) put the Scene view's camera on the edited body: as the outside view,
//   from the eyes, or framing it, to pose bones there while recording.
// - New clip makes a copy of the current one to start from; Add to controller puts it in FirstPersonTable on a layer
//   with its own trigger. In Play mode, Fire sets any trigger on the table player to try it in the game.
public partial class AnimationWorkbench : EditorWindow
{
    const string ControllerPath = "Assets/Game/Characters/Animations/FirstPersonTable.controller";
    const float Fps = 30f;

    [MenuItem("Tools/LoomRoom/Characters/Animation Workbench")]
    static void Open() => GetWindow<AnimationWorkbench>("Animation Workbench");

    AnimatorController controller;
    AnimationClip clip;
    int clipLayer = -1;
    bool baseLayers = true;
    ItemData rightItem, leftItem;
    bool naturalArms = true;
    float armDrop = 25f, armTuck = .25f, armTuckSide = .4f;
    float lookPitch;

    float time;
    bool playing, loop = true;
    float speed = 1f;
    double lastTick;

    float orbitYaw = 210f, orbitPitch = 12f, orbitDistance = 3.2f;
    bool showTrace = true;
    readonly List<(Vector3 first, Vector3 outside)> trace = new();
    AnimationClip tracedClip;

    string newTrigger = "", newTag = "";
    int newLayer = 1, fireIndex;
    string status = "";
    Vector2 scroll;

    void OnEnable()
    {
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (rightItem == null) rightItem = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Weapons/Bronze Sword.asset");
        if (leftItem == null) leftItem = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Game/Items/Data/Shields/Bronze Shield.asset");
        if (clip == null && controller != null) clip = Clips().FirstOrDefault(c => c.name.Contains("Swing")) ?? Clips().FirstOrDefault();
        if (clip != null && clipLayer < 0) clipLayer = LayerOf(clip);
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += PlayMode;
        lastTick = EditorApplication.timeSinceStartup;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= PlayMode;
        DestroyRig();
    }

    // The rig lives in the open scene: it goes before Play and comes back after.
    void PlayMode(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingEditMode) DestroyRig();
    }

    IEnumerable<AnimationClip> Clips() =>
        controller == null ? Enumerable.Empty<AnimationClip>() : controller.animationClips.Where(c => c != null).Distinct().OrderBy(c => c.name);

    // The controller layer whose states use this clip (the first found), else the right arm's.
    int LayerOf(AnimationClip c)
    {
        if (controller == null) return -1;
        var layers = controller.layers;
        for (int i = 0; i < layers.Length; i++)
            foreach (var s in layers[i].stateMachine.states)
                if (s.state.motion == c || (s.state.motion is BlendTree t && t.children.Any(ch => ch.motion == c))) return i;
        for (int i = 0; i < layers.Length; i++) if (layers[i].name == "RightArm") return i;
        return -1;
    }

    // ── Every editor frame ───────────────────────────────────────────

    void Tick()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!RigReady && EditorApplication.timeSinceStartup >= nextBuild) { nextBuild = EditorApplication.timeSinceStartup + 2.0; BuildRig(); }
        if (!RigReady || clip == null) return;
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTick);
        lastTick = now;

        // The Animation window previewing this clip owns the time; otherwise this window does.
        var window = AnimationWindowOnClip();
        if (window != null) { time = window.time; playing = false; }
        else if (playing)
        {
            time += dt * speed;
            if (time > clip.length) { if (loop) time %= Mathf.Max(clip.length, 1e-4f); else { time = clip.length; playing = false; } }
        }
        if (showTrace && tracedClip != clip) Trace();
        Sample(time);
        Repaint();
    }

    AnimationWindow AnimationWindowOnClip()
    {
        if (!EditorWindow.HasOpenInstances<AnimationWindow>()) return null;
        var w = Resources.FindObjectsOfTypeAll<AnimationWindow>().FirstOrDefault();
        return w != null && w.previewing && w.animationClip == clip ? w : null;
    }

    // The blade's tip through the whole clip, in both views (40 samples), then back to where the time was.
    void Trace()
    {
        trace.Clear();
        tracedClip = clip;
        if (!RigReady || clip == null || tipLocal == null || fpRight == null || outsideRight == null) return;
        int steps = Mathf.Max(8, Mathf.CeilToInt(clip.length * Fps));
        for (int i = 0; i <= steps; i++)
        {
            Sample(clip.length * i / steps);
            trace.Add((fpRight.TransformPoint(tipLocal.Value), outsideRight.TransformPoint(tipLocal.Value)));
        }
        Sample(time);
    }

    // ── Drawing ──────────────────────────────────────────────────────

    void OnGUI()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { PlayModeGUI(); return; }
        scroll = EditorGUILayout.BeginScrollView(scroll);
        Header();
        if (!RigReady) { EditorGUILayout.HelpBox(string.IsNullOrEmpty(status) ? "Building the rig..." : status, MessageType.Info); EditorGUILayout.EndScrollView(); return; }

        float width = position.width - 24f;
        float viewWidth = (width - 8f) * .5f, viewHeight = viewWidth * 9f / 16f;
        var row = GUILayoutUtility.GetRect(width, viewHeight + 20f);
        var left = new Rect(row.x, row.y + 18f, viewWidth, viewHeight);
        var right = new Rect(row.x + viewWidth + 8f, row.y + 18f, viewWidth, viewHeight);
        GUI.Label(new Rect(left.x, row.y, left.width, 18f), "First person (as in play)", EditorStyles.boldLabel);
        GUI.Label(new Rect(right.x, row.y, right.width, 18f), "Outside (drag to turn, scroll to zoom)", EditorStyles.boldLabel);
        DrawFirstPerson(left);
        DrawOutside(right);

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Look up/down", GUILayout.Width(90));
            lookPitch = EditorGUILayout.Slider(lookPitch, -60f, 60f, GUILayout.Width(viewWidth - 100f));
            GUILayout.Space(16f);
            if (GUILayout.Button("Front", EditorStyles.miniButtonLeft)) { orbitYaw = 180f; orbitPitch = 8f; }
            if (GUILayout.Button("Side", EditorStyles.miniButtonMid)) { orbitYaw = 270f; orbitPitch = 8f; }
            if (GUILayout.Button("Back", EditorStyles.miniButtonMid)) { orbitYaw = 0f; orbitPitch = 12f; }
            if (GUILayout.Button("Above", EditorStyles.miniButtonRight)) { orbitYaw = 200f; orbitPitch = 60f; }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(new GUIContent("Scene view", "Puts the Scene view's camera on the edited body (it lies far below the world), to pose bones there while recording."), GUILayout.Width(90));
            if (GUILayout.Button(new GUIContent("Like the outside view", "Shift+Alt+O"), EditorStyles.miniButtonLeft)) SceneViewLikeOutside();
            if (GUILayout.Button(new GUIContent("From the eyes", "Shift+Alt+E: the first-person camera's place and field of view, on the body itself"), EditorStyles.miniButtonMid)) SceneViewFromEyes();
            if (GUILayout.Button(new GUIContent("Frame the rig", "Shift+Alt+F"), EditorStyles.miniButtonRight)) SceneViewFrame();
            GUILayout.FlexibleSpace();
        }
        Timeline();
        Options();
        NewClipGUI();
        EditorGUILayout.EndScrollView();
    }

    void Header()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            var clips = Clips().ToList();
            int index = clips.IndexOf(clip);
            int picked = EditorGUILayout.Popup("Clip", index, clips.Select(c => c.name).ToArray());
            if (picked != index && picked >= 0) SetClip(clips[picked]);
            var other = (AnimationClip)EditorGUILayout.ObjectField(clip, typeof(AnimationClip), false, GUILayout.Width(180));
            if (other != clip && other != null) SetClip(other);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            var names = controller != null ? controller.layers.Select(l => l.name).ToArray() : new string[0];
            clipLayer = EditorGUILayout.Popup(new GUIContent("Plays on layer", "Its avatar mask decides which bones the clip moves, as in the game."), clipLayer, names);
            baseLayers = EditorGUILayout.ToggleLeft(new GUIContent("Other layers at rest", "Legs standing and the other arm at its idle under the clip."), baseLayers, GUILayout.Width(150));
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            rightItem = (ItemData)EditorGUILayout.ObjectField("Right hand", rightItem, typeof(ItemData), false);
            leftItem = (ItemData)EditorGUILayout.ObjectField("Left hand", leftItem, typeof(ItemData), false);
            if (EditorGUI.EndChangeCheck()) { RefreshHeld(); tracedClip = null; }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Select rig", "Selects the edited body so the Animation window can open and record its clips."), GUILayout.Width(110)))
            {
                if (body != null) Selection.activeGameObject = body.gameObject;
                // The Animation window takes the rig only once it is open and focused; the clip goes in a frame later.
                GetWindow<AnimationWindow>();
                var picked = clip;
                EditorApplication.delayCall += () =>
                {
                    if (!HasOpenInstances<AnimationWindow>() || picked == null) return;
                    var w = GetWindow<AnimationWindow>(false, null, false);
                    w.animationClip = picked;
                    w.previewing = true;
                };
            }
            if (GUILayout.Button(new GUIContent("Rebuild rig", "After the table player prefab or its body changed."), GUILayout.Width(110))) BuildRig();
            if (GUILayout.Button("Ping clip", GUILayout.Width(80)) && clip != null) EditorGUIUtility.PingObject(clip);
            if (GUILayout.Button(new GUIContent("Save sheet", "Both views at 8 moments through the clip, in one image under Temp/Animation Workbench."), GUILayout.Width(90)) && clip != null)
            {
                string path = $"Temp/Animation Workbench/{clip.name}.png";
                SaveSheet(path, 8);
                EditorUtility.RevealInFinder(path);
            }
            GUILayout.FlexibleSpace();
            if (AnimationWindowOnClip() is { } w2) GUILayout.Label(w2.recording ? "Animation window: recording" : "Animation window: previewing", EditorStyles.miniBoldLabel);
        }
    }

    // Both views, side by side, at `frames` moments through the clip (top to bottom), saved as a PNG. Public so a
    // review or a script can make one (the window's own buttons, an eval).
    public string SaveSheet(string path, int frames, int width = 640, int height = 360)
    {
        if (!RigReady && !EditorApplication.isPlayingOrWillChangePlaymode) BuildRig();
        if (!RigReady || clip == null) return "no rig or clip";
        Texture2D sheet = null;
        float kept = time;
        for (int i = 0; i < frames; i++)
        {
            float t = frames == 1 ? time : clip.length * i / (frames - 1);
            Sample(t);
            var a = Render(firstView, width, height, true);
            var b = Render(outsideView, width, height, false);
            // The preview renders at the screen's scale (a 150% display gives 960 x 540 for 640 x 360).
            int w = a.width, h = a.height;
            if (sheet == null) sheet = new Texture2D(w * 2, h * frames, TextureFormat.RGB24, false);
            int y = (frames - 1 - i) * h;
            sheet.SetPixels(0, y, w, h, a.GetPixels());
            sheet.SetPixels(w, y, w, h, b.GetPixels());
            if (showTrace)
            {
                DrawTrace(sheet, firstView.camera, true, 0, y, w, h, t);
                DrawTrace(sheet, outsideView.camera, false, w, y, w, h, t);
            }
            DestroyImmediate(a); DestroyImmediate(b);
        }
        Sample(kept);
        sheet.Apply();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
        System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
        DestroyImmediate(sheet);
        return path;
    }

    // The blade path into a sheet cell (the cameras as they were for that cell's render).
    void DrawTrace(Texture2D sheet, Camera cam, bool first, int x0, int y0, int w, int h, float at)
    {
        if (trace.Count < 2) return;
        var colour = new Color(1f, .75f, .2f);
        Vector2? last = null;
        foreach (var t in trace)
        {
            var v = cam.WorldToViewportPoint(first ? t.first : t.outside);
            if (v.z <= 0f) { last = null; continue; }
            var p = new Vector2(x0 + v.x * w, y0 + v.y * h);
            if (last != null)
            {
                int n = Mathf.CeilToInt(Vector2.Distance(last.Value, p));
                for (int k = 0; k <= n; k++) Plot(sheet, Vector2.Lerp(last.Value, p, n == 0 ? 0f : k / (float)n), colour, x0, y0, w, h, 1);
            }
            last = p;
        }
        int now = Mathf.Clamp(Mathf.RoundToInt(at / Mathf.Max(clip.length, 1e-4f) * (trace.Count - 1)), 0, trace.Count - 1);
        var c = cam.WorldToViewportPoint(first ? trace[now].first : trace[now].outside);
        if (c.z > 0f) Plot(sheet, new Vector2(x0 + c.x * w, y0 + c.y * h), Color.white, x0, y0, w, h, 4);
    }

    static void Plot(Texture2D sheet, Vector2 p, Color colour, int x0, int y0, int w, int h, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = Mathf.RoundToInt(p.x) + dx, y = Mathf.RoundToInt(p.y) + dy;
                if (x >= x0 && x < x0 + w && y >= y0 && y < y0 + h && dx * dx + dy * dy <= radius * radius + 1) sheet.SetPixel(x, y, colour);
            }
    }

    Texture2D Render(PreviewRenderUtility view, int width, int height, bool first)
    {
        PlaceCamera(view.camera, first);
        view.BeginStaticPreview(new Rect(0, 0, width, height));
        view.camera.Render();
        return view.EndStaticPreview();
    }

    void PlaceCamera(Camera cam, bool first)
    {
        if (first)
        {
            cam.fieldOfView = cameraFov;
            cam.transform.SetPositionAndRotation(fpVisuals.position, fpVisuals.rotation);
            return;
        }
        cam.fieldOfView = 40f;
        var target = outsideRoot.transform.position + Vector3.up * 1.1f;
        var turn = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        cam.transform.SetPositionAndRotation(target - turn * Vector3.forward * orbitDistance, turn);
    }

    // ── The Scene view on the edited body ────────────────────────────

    [Shortcut("Animation Workbench/Scene View Like The Outside View", KeyCode.O, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    static void ShortcutOutside() { if (Open(out var w)) w.SceneViewLikeOutside(); }
    [Shortcut("Animation Workbench/Scene View From The Eyes", KeyCode.E, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    static void ShortcutEyes() { if (Open(out var w)) w.SceneViewFromEyes(); }
    [Shortcut("Animation Workbench/Scene View Frame The Rig", KeyCode.F, ShortcutModifiers.Shift | ShortcutModifiers.Alt)]
    static void ShortcutFrame() { if (Open(out var w)) w.SceneViewFrame(); }

    static bool Open(out AnimationWorkbench window)
    {
        window = HasOpenInstances<AnimationWorkbench>() ? GetWindow<AnimationWorkbench>(false, null, false) : null;
        return window != null && window.RigReady;
    }

    // The Scene view's camera at `position` looking along `rotation`, with this field of view.
    static void PlaceSceneView(Vector3 position, Quaternion rotation, float fieldOfView, float focus)
    {
        var view = SceneView.lastActiveSceneView;
        if (view == null) return;
        view.orthographic = false;
        view.cameraSettings.fieldOfView = fieldOfView;
        // The Scene view keeps its camera `size / sin(half the field of view)` behind the pivot.
        float size = focus * Mathf.Sin(fieldOfView * .5f * Mathf.Deg2Rad);
        view.LookAtDirect(position + rotation * Vector3.forward * focus, rotation, size);
        view.Repaint();
    }

    public void SceneViewLikeOutside()
    {
        if (!RigReady) return;
        var turn = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
        var target = body.transform.position + Vector3.up * 1.1f;
        PlaceSceneView(target - turn * Vector3.forward * orbitDistance, turn, 40f, orbitDistance);
    }

    public void SceneViewFromEyes()
    {
        if (!RigReady) return;
        PlaceSceneView(body.transform.position + Vector3.up * EyeHeight, body.transform.rotation * Quaternion.Euler(lookPitch, 0f, 0f), cameraFov, .5f);
    }

    public void SceneViewFrame()
    {
        if (!RigReady) return;
        var view = SceneView.lastActiveSceneView;
        if (view == null) return;
        var bounds = new Bounds(body.transform.position + Vector3.up, Vector3.one * 2f);
        view.Frame(bounds, false);
    }

    public void SetClip(AnimationClip c)
    {
        clip = c;
        clipLayer = LayerOf(c);
        time = 0f;
        tracedClip = null;
        newTrigger = c != null ? c.name.Replace(" ", "") : "";
    }

    void DrawFirstPerson(Rect rect)
    {
        var cam = firstView.camera;
        PlaceCamera(cam, true);
        firstView.BeginPreview(rect, GUIStyle.none);
        cam.Render();
        firstView.EndAndDrawPreview(rect);
        if (showTrace) DrawTrace(rect, cam, true);
    }

    void DrawOutside(Rect rect)
    {
        var e = Event.current;
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag) { orbitYaw += e.delta.x * .5f; orbitPitch = Mathf.Clamp(orbitPitch + e.delta.y * .5f, -20f, 85f); e.Use(); }
            if (e.type == EventType.ScrollWheel) { orbitDistance = Mathf.Clamp(orbitDistance * (1f + e.delta.y * .05f), 1f, 8f); e.Use(); }
        }
        var cam = outsideView.camera;
        PlaceCamera(cam, false);
        outsideView.BeginPreview(rect, GUIStyle.none);
        cam.Render();
        outsideView.EndAndDrawPreview(rect);
        if (showTrace) DrawTrace(rect, cam, false);
    }

    void DrawTrace(Rect rect, Camera cam, bool first)
    {
        if (Event.current.type != EventType.Repaint || trace.Count < 2) return;
        var points = new List<Vector3>(trace.Count);
        foreach (var t in trace)
        {
            var v = cam.WorldToViewportPoint(first ? t.first : t.outside);
            if (v.z <= 0f) continue;
            points.Add(new Vector3(rect.x + v.x * rect.width, rect.y + (1f - v.y) * rect.height, 0f));
        }
        if (points.Count < 2) return;
        GUI.BeginClip(rect);
        for (int i = 0; i < points.Count; i++) points[i] -= new Vector3(rect.x, rect.y, 0f);
        Handles.color = new Color(1f, .75f, .2f, .9f);
        Handles.DrawAAPolyLine(2.5f, points.ToArray());
        // Where the tip is now.
        int now = Mathf.Clamp(Mathf.RoundToInt(time / Mathf.Max(clip.length, 1e-4f) * (trace.Count - 1)), 0, trace.Count - 1);
        var p = cam.WorldToViewportPoint(first ? trace[now].first : trace[now].outside);
        if (p.z > 0f) Handles.DrawSolidDisc(new Vector3(p.x * rect.width, (1f - p.y) * rect.height, 0f), Vector3.forward, 4f);
        GUI.EndClip();
    }

    void Timeline()
    {
        EditorGUILayout.Space(4);
        float length = clip != null ? clip.length : 0f;
        var window = AnimationWindowOnClip();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("|<", GUILayout.Width(28))) SetTime(0f, window);
            if (GUILayout.Button("<", GUILayout.Width(24))) SetTime(Mathf.Max(0f, Mathf.Round(time * Fps - 1f) / Fps), window);
            using (new EditorGUI.DisabledScope(window != null))
                if (GUILayout.Button(playing ? "Pause" : "Play", GUILayout.Width(56))) { playing = !playing; if (playing && time >= length) time = 0f; }
            if (GUILayout.Button(">", GUILayout.Width(24))) SetTime(Mathf.Min(length, Mathf.Round(time * Fps + 1f) / Fps), window);
            if (GUILayout.Button(">|", GUILayout.Width(28))) SetTime(length, window);
            loop = GUILayout.Toggle(loop, "Loop", GUILayout.Width(50));
            GUILayout.Label("Speed", GUILayout.Width(40));
            speed = new[] { .1f, .25f, .5f, 1f }[EditorGUILayout.Popup(System.Array.IndexOf(new[] { .1f, .25f, .5f, 1f }, speed) is var i && i >= 0 ? i : 3, new[] { "0.1x", "0.25x", "0.5x", "1x" }, GUILayout.Width(60))];
            GUILayout.Label($"frame {Mathf.RoundToInt(time * Fps)} / {Mathf.RoundToInt(length * Fps)}   {time:0.000} s / {length:0.000} s", GUILayout.Width(250));
        }
        var bar = GUILayoutUtility.GetRect(position.width - 24f, 22f);
        EditorGUI.BeginChangeCheck();
        float t = GUI.HorizontalSlider(bar, time, 0f, Mathf.Max(length, 1e-4f));
        if (EditorGUI.EndChangeCheck()) { playing = false; SetTime(t, window); }
        // Animation events as marks under the bar, named on hover.
        var events = clip != null ? AnimationUtility.GetAnimationEvents(clip) : new AnimationEvent[0];
        var marks = GUILayoutUtility.GetRect(position.width - 24f, 16f);
        foreach (var ev in events)
        {
            float x = marks.x + 5f + (marks.width - 10f) * (length > 0f ? ev.time / length : 0f);
            var mark = new Rect(x - 4f, marks.y, 8f, 14f);
            EditorGUI.DrawRect(new Rect(x - 1f, marks.y, 2f, 12f), new Color(.95f, .55f, .2f));
            GUI.Label(mark, new GUIContent("", $"{ev.functionName} at {ev.time:0.000} s (frame {Mathf.RoundToInt(ev.time * Fps)})"));
        }
        if (events.Length > 0) EditorGUILayout.LabelField("Events: " + string.Join(", ", events.Select(ev => $"{ev.functionName} @ {Mathf.RoundToInt(ev.time * Fps)}")), EditorStyles.miniLabel);
    }

    void SetTime(float t, AnimationWindow window)
    {
        time = t;
        if (window != null) window.time = t;
    }

    bool optionsOpen;

    void Options()
    {
        optionsOpen = EditorGUILayout.Foldout(optionsOpen, "View options", true);
        if (!optionsOpen) return;
        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();
        showTrace = EditorGUILayout.Toggle(new GUIContent("Blade path", "The held item's tip through the whole clip (orange), the dot where it is now."), showTrace);
        naturalArms = EditorGUILayout.Toggle(new GUIContent("Outside: arms as others see", "Off: the outside view shows the arms exactly where the clip puts them (as first person)."), naturalArms);
        using (new EditorGUI.DisabledScope(!naturalArms))
        {
            armDrop = EditorGUILayout.Slider("Arm drop", armDrop, 0f, 60f);
            armTuck = EditorGUILayout.Slider("Arm tuck", armTuck, 0f, .8f);
            armTuckSide = EditorGUILayout.Slider("Arm tuck side", armTuckSide, 0f, .8f);
        }
        if (EditorGUI.EndChangeCheck()) tracedClip = null;
        if (GUILayout.Button("Trace again", GUILayout.Width(110))) tracedClip = null;
        EditorGUI.indentLevel--;
    }

    bool newOpen;

    void NewClipGUI()
    {
        newOpen = EditorGUILayout.Foldout(newOpen, "New clip and the controller", true);
        if (!newOpen) return;
        EditorGUI.indentLevel++;
        if (GUILayout.Button(new GUIContent("New clip from this one...", "Saves a copy of the current clip (keys, events and settings) as a new .anim to edit."), GUILayout.Width(200)) && clip != null)
        {
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip)).Replace('\\', '/');
            if (!folder.StartsWith("Assets/Game")) folder = "Assets/Game/Characters/Animations/FirstPersonPlayer";
            string path = EditorUtility.SaveFilePanelInProject("New clip", clip.name + " new", "anim", "A copy of " + clip.name + " to edit.", folder);
            if (!string.IsNullOrEmpty(path))
            {
                var copy = Object.Instantiate(clip);
                AssetDatabase.CreateAsset(copy, path);
                AssetDatabase.SaveAssets();
                int layer = clipLayer;
                SetClip(AssetDatabase.LoadAssetAtPath<AnimationClip>(path));
                clipLayer = layer;
                newLayer = Mathf.Max(0, layer);
            }
        }
        if (controller != null)
        {
            bool inController = controller.animationClips.Contains(clip);
            using (new EditorGUI.DisabledScope(inController))
            {
                newLayer = EditorGUILayout.Popup("Layer", newLayer, controller.layers.Select(l => l.name).ToArray());
                newTrigger = EditorGUILayout.TextField(new GUIContent("Trigger", "The game plays it by setting this trigger on the table player's animator."), newTrigger);
                newTag = EditorGUILayout.TextField(new GUIContent("State tag", "Attack, AttackRelease, Block... (the game reads states by tag)."), newTag);
                if (GUILayout.Button(inController ? "Already in FirstPersonTable" : "Add to controller", GUILayout.Width(200))) AddToController();
            }
        }
        EditorGUI.indentLevel--;
    }

    // A state on the chosen layer playing the clip, entered from Any State on its trigger, back to the layer's default
    // state when it ends.
    void AddToController()
    {
        if (clip == null || controller == null || string.IsNullOrWhiteSpace(newTrigger)) return;
        Undo.RecordObject(controller, "Add clip to controller");
        if (!controller.parameters.Any(p => p.name == newTrigger)) controller.AddParameter(newTrigger, AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[newLayer].stateMachine;
        var state = machine.AddState(clip.name, new Vector3(500f, 60f * machine.states.Length, 0f));
        state.motion = clip;
        state.tag = newTag;
        var into = machine.AddAnyStateTransition(state);
        into.AddCondition(AnimatorConditionMode.If, 0f, newTrigger);
        into.hasExitTime = false; into.duration = .05f; into.canTransitionToSelf = false;
        if (machine.defaultState != null && machine.defaultState != state)
        {
            var back = state.AddTransition(machine.defaultState);
            back.hasExitTime = true; back.exitTime = 1f; back.duration = .1f;
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        clipLayer = newLayer;
        status = $"{clip.name} added to {controller.layers[newLayer].name} on trigger {newTrigger}";
        ShowNotification(new GUIContent(status));
    }

    // In Play mode: set any trigger on the table player's animator, to see a clip in the game.
    void PlayModeGUI()
    {
        EditorGUILayout.HelpBox("The rig is put away in Play mode. Fire a trigger on the table player to try a clip in the game.", MessageType.Info);
        if (controller == null) return;
        var triggers = controller.parameters.Where(p => p.type == AnimatorControllerParameterType.Trigger).Select(p => p.name).ToArray();
        if (triggers.Length == 0) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            fireIndex = EditorGUILayout.Popup("Trigger", Mathf.Clamp(fireIndex, 0, triggers.Length - 1), triggers);
            if (GUILayout.Button("Fire", GUILayout.Width(60)) && EditorApplication.isPlaying)
            {
                var rig = Object.FindAnyObjectByType<OneBodyRig>();
                if (rig != null && rig.animator != null) rig.animator.SetTrigger(triggers[fireIndex]);
                else ShowNotification(new GUIContent("No table player in the scene"));
            }
        }
    }
}

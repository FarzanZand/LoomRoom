using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Asset Studio's middle panel. Anything 3D (a prefab, an item's world model, a spell's projectile) stands on a
// turntable in a preview scene of its own: drag to orbit, wheel to zoom, F to frame, Spin turns it slowly. Effects
// (particle systems) play on a clock of their own that can be paused, stepped and dragged to any time, the same frame
// every time (simulated from the start). Sounds show their waveform with Play/Stop/Loop; textures are drawn
// point-filtered at whole-number zooms. Nothing here touches the asset: the scene copy is thrown away.
public class AssetStudioPreview : IDisposable
{
    enum Mode { None, Model, Audio, Texture, Icon }

    Mode mode;
    UnityEngine.Object target;
    PreviewRenderUtility util;
    GameObject instance;
    Bounds bounds;
    ParticleSystem[] particles = new ParticleSystem[0];
    float yaw = 35f, pitch = 18f, distance = 4f, fit = 4f;
    bool spin = true;
    double last;
    // Effects
    float vfxTime, vfxLength = 3f;
    bool vfxPlaying = true, vfxLoop = true;
    // Audio
    AudioClip clip;
    AudioClip[] clips = new AudioClip[0];
    bool audioLoop;
    // Texture / icon
    Texture texture;
    int zoom = 0;   // 0: fit
    int background;   // 0 dark, 1 grey, 2 light
    static readonly Color[] Backgrounds = { new(.09f, .09f, .11f), new(.32f, .32f, .34f), new(.78f, .77f, .74f) };
    static readonly string[] BackgroundNames = { "Dark", "Grey", "Light" };

    public bool WantsRepaint => mode == Mode.Model && (Painting || spin || particles.Length > 0 && vfxPlaying) || mode == Mode.Audio && AudioPlaying;

    // Painting on the model (AssetStudioPainter): left button paints, right button orbits, wheel zooms.
    readonly AssetStudioPainter painter = new();
    public bool Painting { get; private set; }
    public bool CanPaint => mode == Mode.Model && instance != null;
    public float ControlsHeight => Painting ? 184 : 86;
    public AssetStudioPainter Painter => painter;
    string paintNote;
    Vector2 lastPaintMouse;
    bool stroking;
    AssetStudioPainter.Hit? hover;

    public void SetTarget(UnityEngine.Object t)
    {
        StopPainting(true, false);
        paintNote = null;
        ClearInstance();
        target = t;
        clip = null; clips = new AudioClip[0]; texture = null; zoom = 0;
        mode = Mode.None;
        if (t == null) return;
        GameObject model = t switch
        {
            GameObject g => g,
            ItemData i => i.worldPrefab,
            SpellDefinition s => s.projectile != null ? s.projectile.gameObject : s.impact != null ? s.impact.gameObject : null,
            _ => null,
        };
        if (model != null) { BuildModel(model); return; }
        if (t is AudioClip c) { clip = c; clips = new[] { c }; mode = Mode.Audio; return; }
        if (t is AudioData data && data.clips != null && data.clips.Length > 0) { clips = data.clips.Where(x => x != null).ToArray(); clip = clips.FirstOrDefault(); mode = clip != null ? Mode.Audio : Mode.Icon; return; }
        if (t is Texture2D tex) { texture = tex; mode = Mode.Texture; return; }
        if (t is ItemData item && item.icon != null) { texture = item.icon.texture; mode = Mode.Texture; return; }
        mode = Mode.Icon;
    }

    void BuildModel(GameObject prefab)
    {
        util ??= new PreviewRenderUtility();
        util.camera.fieldOfView = 30f;
        util.camera.nearClipPlane = .01f;
        util.camera.farClipPlane = 2000f;
        util.camera.clearFlags = CameraClearFlags.SolidColor;
        // The game's volumes (filmic grade, lens effects) are global and would reach this camera too: a lens effect
        // bends the picture, so clicks miss what is under them. The preview shows the model plain.
        var camData = util.camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() ?? util.camera.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        camData.renderPostProcessing = false;
        camData.volumeLayerMask = 0;
        camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
        instance = UnityEngine.Object.Instantiate(prefab);
        instance.hideFlags = HideFlags.HideAndDontSave;
        foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null) mb.enabled = false;
        util.AddSingleGO(instance);
        particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        vfxLength = particles.Length > 0 ? Mathf.Clamp(particles.Max(p => p.main.duration + p.main.startLifetime.constantMax), .5f, 12f) : 0f;
        vfxTime = 0f; vfxPlaying = true;
        foreach (var p in particles) { p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = false; }
        Simulate();
        bounds = Measure(instance);
        AddGround();
        fit = Mathf.Max(.2f, bounds.extents.magnitude) * 3f;
        distance = fit;
        mode = Mode.Model;
        last = EditorApplication.timeSinceStartup;
    }

    // A dark plate under the model, a little wider than it, so a room's pieces or a creature's feet sit on something.
    GameObject ground;
    static Material groundMaterial;
    void AddGround()
    {
        if (groundMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            groundMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            groundMaterial.color = new Color(.2f, .2f, .23f);
            if (groundMaterial.HasProperty("_BaseColor")) groundMaterial.SetColor("_BaseColor", new Color(.2f, .2f, .23f));
        }
        ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.hideFlags = HideFlags.HideAndDontSave;
        UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
        ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        float size = Mathf.Max(bounds.size.x, bounds.size.z) * 1.4f + .2f;
        ground.transform.SetPositionAndRotation(new Vector3(bounds.center.x, bounds.min.y - .002f, bounds.center.z), Quaternion.Euler(90, 0, 0));
        ground.transform.localScale = new Vector3(size, size, 1);
        util.AddSingleGO(ground);
    }

    static Bounds Measure(GameObject go)
    {
        // Only what shows: switched-off parts (spare weapons, editor previews) would pull the framing aside.
        var renderers = go.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
        if (renderers.Length == 0) renderers = go.GetComponentsInChildren<ParticleSystemRenderer>(false).ToArray();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds? b = null;
        foreach (var r in renderers)
        {
            var rb = WorldBounds(r);
            if (b == null) b = rb; else { var x = b.Value; x.Encapsulate(rb); b = x; }
        }
        return b.Value;
    }

    // A skinned mesh in a preview scene reports bounds from its last skinning (often none yet): its mesh's own bounds,
    // placed where the renderer stands, are where it is drawn.
    static Bounds WorldBounds(Renderer r)
    {
        if (r is not SkinnedMeshRenderer s || s.sharedMesh == null) return r.bounds;
        var mb = s.sharedMesh.bounds;
        var w = new Bounds(s.transform.TransformPoint(mb.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            w.Encapsulate(s.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return w;
    }

    void Simulate()
    {
        if (particles.Length == 0) return;
        float t = vfxLoop && vfxLength > 0 ? vfxTime % vfxLength : Mathf.Min(vfxTime, vfxLength);
        // Every time from the start, so the same time is always the same frame.
        foreach (var p in particles) if (p.transform.parent == null || p.transform.parent.GetComponentInParent<ParticleSystem>() == null) p.Simulate(t, true, true, true);
    }

    void ClearInstance()
    {
        ClearLook();
        if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
        if (ground != null) UnityEngine.Object.DestroyImmediate(ground);
        instance = null; ground = null;
        particles = new ParticleSystem[0];
    }

    public void Dispose()
    {
        StopPainting(false, false, keepCopy: true);   // closing or a recompile: never a dialog
        ClearInstance();
        util?.Cleanup();
        util = null;
        StopAudio();
    }

    public void Frame() { distance = fit; yaw = 35f; pitch = 18f; zoom = 0; }
    public void TogglePlay()
    {
        if (mode == Mode.Audio) { if (AudioPlaying) StopAudio(); else PlayAudio(clip, audioLoop); }
        else if (particles.Length > 0) vfxPlaying = !vfxPlaying;
        else spin = !spin;
    }

    public void Draw(Rect r, EditorWindow window)
    {
        EditorGUI.DrawRect(r, Backgrounds[background]);
        switch (mode)
        {
            case Mode.Model: DrawModel(r); break;
            case Mode.Audio: DrawAudio(r); break;
            case Mode.Texture: DrawTexture(r); break;
            case Mode.Icon:
                var icon = AssetPreview.GetAssetPreview(target) ?? AssetPreview.GetMiniThumbnail(target);
                if (icon != null) GUI.DrawTexture(new Rect(r.center.x - 64, r.center.y - 64, 128, 128), icon, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(r.x + 12, r.yMax - 24, r.width - 24, 18), "No 3D model: its settings are in the inspector.", EditorStyles.miniLabel);
                break;
        }
    }

    void DrawModel(Rect r)
    {
        var e = Event.current;
        // The view bar sits over the picture: it takes its clicks before painting or turning can.
        if (e.type != EventType.Repaint) DrawViewBar(r);
        if (Painting) PaintInput(r, e);
        if (r.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag && (e.button == 0 && !Painting || e.button == 1 || e.button == 0 && e.alt)) { yaw += e.delta.x * .5f; pitch = Mathf.Clamp(pitch + e.delta.y * .5f, -85f, 85f); spin = false; e.Use(); }
            if (e.type == EventType.ScrollWheel) { distance = Mathf.Clamp(distance * (1f + e.delta.y * .05f), fit * .1f, fit * 6f); e.Use(); }
        }
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - last); last = now;
        if (e.type == EventType.Repaint)
        {
            if (spin) yaw += dt * 20f;
            if (particles.Length > 0 && vfxPlaying) { vfxTime += dt; Simulate(); }
            Render(r, true);
            var sizeText = $"{bounds.size.x:0.##} × {bounds.size.y:0.##} × {bounds.size.z:0.##} m";
            GUI.Label(new Rect(r.xMax - 212, r.y + 8, 200, 18), sizeText, new GUIStyle(EditorStyles.whiteMiniLabel) { alignment = TextAnchor.UpperRight });
            if (Painting) DrawPaintOverlay(r);
            DrawViewBar(r);
        }
    }

    // ── How the model is drawn: shaded, shaded with its wireframe, wireframe; texel density over any of them ──

    enum Look { Shaded, WireShaded, Wire }
    static Look look;
    static bool showDensity;
    static float densityTarget = PixelRestyle.ItemTexelsPerUnit;
    static readonly string[] LookNames = { "Shaded", "Wire + shaded", "Wireframe" };
    readonly List<GameObject> wires = new();
    readonly Dictionary<Renderer, Material[]> lookBase = new();   // each renderer's materials under the density view
    readonly List<Material> densityMaterials = new();
    Look shownLook = (Look)(-1);
    bool shownDensity;
    float shownTarget;
    float averageDensity;
    static Material wireMaterial;
    static Shader densityShader;
    const string StudioFolder = "Assets/Game/Development/Editor/AssetStudio/";
    static readonly string[] BaseMaps = { "_BaseMap", "_MainTex", "_BaseColorMap", "_Albedo_Map" };

    void DrawViewBar(Rect r)
    {
        var bar = new Rect(r.x + 8, r.y + 6, 255, 18);
        look = (Look)GUI.Toolbar(bar, (int)look, LookNames, EditorStyles.miniButton);
        var d = new Rect(bar.xMax + 6, bar.y, 86, 18);
        showDensity = GUI.Toggle(d, showDensity, new GUIContent("Texel density", "Every texel as a checker square, tinted by texels per metre: green on the target, blue fewer, red more"), EditorStyles.miniButton);
        if (!showDensity) return;
        var line = new Rect(r.x + 8, bar.yMax + 4, 400, 18);
        GUI.Label(new Rect(line.x, line.y, 44, 18), "Target", EditorStyles.whiteMiniLabel);
        float nt = EditorGUI.DelayedFloatField(new Rect(line.x + 40, line.y + 1, 44, 16), densityTarget, EditorStyles.miniTextField);
        if (nt > 0) densityTarget = nt;
        GUI.Label(new Rect(line.x + 90, line.y, 300, 18), $"per metre · this model {averageDensity:0} per metre", EditorStyles.whiteMiniLabel);
        if (Event.current.type == EventType.Repaint) DrawDensityKey(new Rect(r.x + 8, line.yMax + 4, 180, 8));
    }

    static Color DensityColour(float l)
    {
        var good = new Color(.25f, .85f, .3f);
        return l < 0 ? Color.Lerp(good, new Color(.2f, .4f, 1), -l) : Color.Lerp(good, new Color(1, .25f, .2f), l);
    }

    static void DrawDensityKey(Rect k)
    {
        for (int i = 0; i < 18; i++)
            EditorGUI.DrawRect(new Rect(k.x + i * k.width / 18, k.y, k.width / 18 + 1, k.height), DensityColour(i / 17f * 2 - 1));
        var style = new GUIStyle(EditorStyles.whiteMiniLabel);
        GUI.Label(new Rect(k.x, k.yMax, 60, 14), "x¼", style);
        GUI.Label(new Rect(k.center.x - 30, k.yMax, 60, 14), "target", new GUIStyle(style) { alignment = TextAnchor.UpperCenter });
        GUI.Label(new Rect(k.xMax - 30, k.yMax, 30, 14), "x4", new GUIStyle(style) { alignment = TextAnchor.UpperRight });
    }

    // Puts the chosen look on the preview copy, only when it changed. Called before each render.
    void ApplyLook()
    {
        if (instance == null || look == shownLook && showDensity == shownDensity && (!showDensity || shownTarget == densityTarget)) return;
        ClearLook();
        var renderers = instance.GetComponentsInChildren<Renderer>(false).Where(x => x.enabled && (x is MeshRenderer || x is SkinnedMeshRenderer)).ToArray();
        if (showDensity)
        {
            if (densityShader == null) densityShader = AssetDatabase.LoadAssetAtPath<Shader>(StudioFolder + "Studio Texel Density.shader");
            float texels = 0, area = 0;
            foreach (var rend in renderers)
            {
                var mats = rend.sharedMaterials;
                lookBase[rend] = mats;
                var swapped = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = new Material(densityShader) { hideFlags = HideFlags.HideAndDontSave };
                    var tex = BaseMap(mats[i]);
                    if (tex != null) m.SetTexture("_MainTex", tex);
                    m.SetFloat("_Target", densityTarget);
                    densityMaterials.Add(m);
                    swapped[i] = m;
                }
                rend.sharedMaterials = swapped;
                MeasureDensity(rend, mats, ref texels, ref area);
            }
            averageDensity = area > 0 ? Mathf.Sqrt(texels / area) : 0;
        }
        if (look != Look.Shaded)
        {
            if (wireMaterial == null)
                wireMaterial = new Material(AssetDatabase.LoadAssetAtPath<Shader>(StudioFolder + "Studio Wire.shader")) { hideFlags = HideFlags.HideAndDontSave };
            foreach (var rend in renderers)
            {
                var mesh = WorldMesh(rend);
                if (mesh == null) continue;
                var go = new GameObject("Studio wire") { hideFlags = HideFlags.HideAndDontSave };
                go.AddComponent<MeshFilter>().sharedMesh = Lines(mesh);
                go.AddComponent<MeshRenderer>().sharedMaterial = wireMaterial;
                util.AddSingleGO(go);
                wires.Add(go);
                UnityEngine.Object.DestroyImmediate(mesh);
                if (look == Look.Wire) rend.forceRenderingOff = true;
            }
            wireMaterial.color = look == Look.Wire ? new Color(.8f, .82f, .88f) : new Color(1f, .62f, .2f);
        }
        shownLook = look; shownDensity = showDensity; shownTarget = densityTarget;
    }

    static Texture BaseMap(Material m) => m == null ? null : BaseMaps.Where(m.HasProperty).Select(m.GetTexture).FirstOrDefault(x => x != null);

    void ClearLook()
    {
        foreach (var w in wires) if (w != null) { UnityEngine.Object.DestroyImmediate(w.GetComponent<MeshFilter>().sharedMesh); UnityEngine.Object.DestroyImmediate(w); }
        wires.Clear();
        foreach (var kv in lookBase) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
        lookBase.Clear();
        foreach (var m in densityMaterials) if (m != null) UnityEngine.Object.DestroyImmediate(m);
        densityMaterials.Clear();
        if (instance != null) foreach (var rend in instance.GetComponentsInChildren<Renderer>(true)) rend.forceRenderingOff = false;
        shownLook = (Look)(-1); shownDensity = false;
    }

    // The renderer's mesh in world space as it stands (a skinned mesh baked). Null when it can't be read.
    static Mesh WorldMesh(Renderer rend)
    {
        Mesh src; Matrix4x4 m;
        if (rend is SkinnedMeshRenderer smr && smr.sharedMesh != null)
        { src = new Mesh(); smr.BakeMesh(src, true); m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one); }
        else
        {
            var mf = rend.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return null;
            src = UnityEngine.Object.Instantiate(mf.sharedMesh); m = rend.transform.localToWorldMatrix;
        }
        src.hideFlags = HideFlags.HideAndDontSave;
        try { var v = src.vertices; for (int i = 0; i < v.Length; i++) v[i] = m.MultiplyPoint3x4(v[i]); src.vertices = v; }
        catch { UnityEngine.Object.DestroyImmediate(src); return null; }
        return src;
    }

    // Each triangle edge once, as a line mesh (vertices split at UV seams are joined first, so a seam is one line).
    static Mesh Lines(Mesh mesh)
    {
        var v = mesh.vertices;
        var weld = new Dictionary<Vector3Int, int>(); var map = new int[v.Length]; var pts = new List<Vector3>();
        for (int i = 0; i < v.Length; i++)
        {
            var k = Vector3Int.RoundToInt(v[i] * 1e4f);
            if (!weld.TryGetValue(k, out map[i])) { map[i] = pts.Count; weld[k] = pts.Count; pts.Add(v[i]); }
        }
        var edges = new HashSet<long>(); var idx = new List<int>();
        for (int sm = 0; sm < mesh.subMeshCount; sm++)
        {
            var t = mesh.GetTriangles(sm);
            for (int i = 0; i + 2 < t.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = map[t[i + e]], b = map[t[i + (e + 1) % 3]];
                    long key = (long)Mathf.Min(a, b) << 32 | (uint)Mathf.Max(a, b);
                    if (a != b && edges.Add(key)) { idx.Add(a); idx.Add(b); }
                }
        }
        var lines = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        lines.SetVertices(pts);
        lines.SetIndices(idx, MeshTopology.Lines, 0);
        return lines;
    }

    // Adds the renderer's texel and world areas, for the model's average texels per metre.
    static void MeasureDensity(Renderer rend, Material[] mats, ref float texels, ref float area)
    {
        var mesh = WorldMesh(rend);
        if (mesh == null) return;
        var v = mesh.vertices; var uv = mesh.uv;
        if (uv.Length == v.Length)
            for (int sm = 0; sm < mesh.subMeshCount && sm < mats.Length; sm++)
            {
                var tex = BaseMap(mats[sm]);
                if (tex == null) continue;
                var t = mesh.GetTriangles(sm);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    var a = uv[t[i]]; var b = uv[t[i + 1]]; var c = uv[t[i + 2]];
                    texels += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * .5f * tex.width * tex.height;
                    area += Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude * .5f;
                }
            }
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    // ── Painting ──

    public bool StartPainting()
    {
        if (!CanPaint) { paintNote = "Only a model can be painted (not a sound or a flat texture)."; return false; }
        ClearLook();   // the painter takes the model's own materials; the look goes back on over its copies
        if (!painter.Begin(instance)) { paintNote = painter.Problem; return false; }
        Painting = true; spin = false; vfxPlaying = false;
        paintNote = painter.Problem;
        return true;
    }

    // Leaves painting. ask: with unsaved changes, asks whether to save them first. rebuild: show the model again as
    // the asset has it (the preview copy still wears the painter's material copies).
    public void StopPainting(bool ask, bool rebuild = true, bool keepCopy = false)
    {
        if (!Painting) return;
        if (painter.Changed > 0 && keepCopy) paintNote = painter.KeepUnsaved();
        if (painter.Changed > 0 && ask && EditorUtility.DisplayDialog("Unsaved painting", $"{painter.Changed} texture(s) were painted and not saved.", "Save them", "Throw them away"))
            paintNote = painter.Save();
        painter.Revert();
        painter.End(false);
        Painting = false;
        hover = null; stroking = false;
        if (rebuild && instance != null && target != null)
        {
            var y = yaw; var p = pitch; var d = distance;
            ClearInstance();
            GameObject model = target switch { GameObject g => g, ItemData i => i.worldPrefab, SpellDefinition s => s.projectile != null ? s.projectile.gameObject : null, _ => null };
            if (model != null) BuildModel(model);
            yaw = y; pitch = p; distance = d; spin = false;
        }
    }

    // The preview camera is drawn with its projection set explicitly from the picture's own aspect: left to the
    // preview, a tall picture was drawn with the wrong aspect (the model about 13% smaller than the camera's numbers
    // say), so a click painted off toward the middle. The mouse ray and the GPU pick use the same projection.
    void RenderWith(float aspect)
    {
        var cam = util.camera;
        cam.projectionMatrix = Matrix4x4.Perspective(cam.fieldOfView, aspect, cam.nearClipPlane, cam.farClipPlane);
        try { util.Render(true); }
        finally { cam.ResetProjectionMatrix(); }
    }

    Matrix4x4 drawnViewProj;
    Vector3 drawnCamPos;
    float drawnFov, drawnAspect;
    bool hasDrawn;
    public string TestCamera => $"drawn fov {drawnFov} aspect {drawnAspect}; now fov {util?.camera.fieldOfView} aspect {util?.camera.aspect}";

    Ray MouseRay(Rect r, Vector2 mouse)
    {
        if (hasDrawn && util != null)
        {
            var cam0 = util.camera;
            painter.viewRight = cam0.transform.right; painter.viewUp = cam0.transform.up; painter.viewForward = cam0.transform.forward;
            var ndc = new Vector3((mouse.x - r.x) / r.width * 2 - 1, 1 - (mouse.y - r.y) / r.height * 2, 1);
            var far = drawnViewProj.inverse.MultiplyPoint(ndc);
            return new Ray(drawnCamPos, (far - drawnCamPos).normalized);
        }
        var rot = Quaternion.Euler(pitch, yaw, 0);
        var cam = util.camera;
        cam.transform.position = bounds.center + rot * new Vector3(0, 0, -distance);
        cam.transform.rotation = rot;
        cam.aspect = r.width / Mathf.Max(1, r.height);
        painter.viewRight = cam.transform.right; painter.viewUp = cam.transform.up; painter.viewForward = cam.transform.forward;
        return cam.ViewportPointToRay(new Vector3((mouse.x - r.x) / r.width, 1f - (mouse.y - r.y) / r.height, 0));
    }

    // What is under the mouse: the GPU pick says which texel is on screen there; the ray cast fills in where it is
    // on the model when the pick can't (a texel no face covers whole).
    bool FindHit(Rect r, Vector2 mouse, out AssetStudioPainter.Hit hit)
    {
        var rot = Quaternion.Euler(pitch, yaw, 0);
        var cam = util.camera;
        cam.transform.position = bounds.center + rot * new Vector3(0, 0, -distance);
        cam.transform.rotation = rot;
        cam.aspect = r.width / Mathf.Max(1, r.height);
        painter.viewRight = cam.transform.right; painter.viewUp = cam.transform.up; painter.viewForward = cam.transform.forward;
        bool rayHit = painter.Raycast(cam.ViewportPointToRay(new Vector3((mouse.x - r.x) / r.width, 1f - (mouse.y - r.y) / r.height, 0)), out var rh);
        if (!painter.Pick(cam, r, mouse, out hit)) { hit = rh; return rayHit; }
        if (hit.worldPerUv <= 0 && rayHit) { hit.point = rh.point; hit.normal = rh.normal; hit.worldPerUv = rh.worldPerUv; hit.distance = rh.distance; }
        return true;
    }

    void PaintInput(Rect r, Event e)
    {
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            hover = r.Contains(e.mousePosition) && FindHit(r, e.mousePosition, out var h) ? h : null;
        if (e.alt) return;
        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            painter.BeginStroke();
            stroking = true;
            lastPaintMouse = e.mousePosition;
            PaintAt(r, e.mousePosition);
            if (painter.tool == AssetStudioPainter.Tool.Eyedropper || painter.tool == AssetStudioPainter.Tool.Fill) { stroking = false; painter.EndStroke(); }
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && e.button == 0 && stroking)
        {
            // Dabs every couple of screen pixels along the drag, so a fast stroke stays unbroken.
            float dist = Vector2.Distance(lastPaintMouse, e.mousePosition);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / 2f));
            for (int i = 1; i <= steps; i++) PaintAt(r, Vector2.Lerp(lastPaintMouse, e.mousePosition, i / (float)steps));
            lastPaintMouse = e.mousePosition;
            e.Use();
        }
        else if (e.rawType == EventType.MouseUp && e.button == 0 && stroking)
        {
            stroking = false;
            painter.EndStroke();
            e.Use();
        }
    }

    void PaintAt(Rect r, Vector2 mouse)
    {
        if (FindHit(r, mouse, out var hit)) painter.Apply(hit);
    }

    void DrawPaintOverlay(Rect r)
    {
        var e = Event.current;
        if (hover.HasValue && r.Contains(e.mousePosition))
        {
            // The brush: a box about the size it covers on the model where the mouse is.
            var h = hover.Value;
            var cam = util.camera;
            float worldPerTexel = h.worldPerUv > 0 ? h.worldPerUv / Mathf.Sqrt(h.sheet.width * h.sheet.height) : .01f;
            var a = cam.WorldToViewportPoint(h.point);
            var b = cam.WorldToViewportPoint(h.point + cam.transform.right * worldPerTexel * painter.size);
            float px = Mathf.Clamp(Mathf.Abs(b.x - a.x) * r.width, 4, 200);
            var box = new Rect(e.mousePosition.x - px / 2, e.mousePosition.y - px / 2, px, px);
            var c = painter.tool == AssetStudioPainter.Tool.Eraser ? Color.white : (Color)painter.colour;
            EditorGUI.DrawRect(box, new Color(c.r, c.g, c.b, .4f));
            Outline(new Rect(box.x - 1, box.y - 1, box.width + 2, box.height + 2), Color.black);
            Outline(box, Color.white);
            var t = AssetStudioPainter.TexelAt(h);
            GUI.Label(new Rect(r.x + 12, r.yMax - 22, r.width - 24, 18), $"{System.IO.Path.GetFileName(h.sheet.path)} · texel {t.x}, {t.y} · {h.sheet.width} × {h.sheet.height}", EditorStyles.whiteMiniLabel);
        }
    }

    static string Hex(Color32 c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";

    // A colour square that can be clicked; the current colour gets a white frame.
    bool Swatch(Color32 c, float size)
    {
        var rr = GUILayoutUtility.GetRect(size, 18, GUILayout.Width(size), GUILayout.Height(18));
        rr.y += 1; rr.height = 16;
        bool current = c.r == painter.colour.r && c.g == painter.colour.g && c.b == painter.colour.b;
        EditorGUI.DrawRect(rr, current ? Color.white : Color.black);
        EditorGUI.DrawRect(new Rect(rr.x + 1, rr.y + 1, rr.width - 2, rr.height - 2), c);
        GUI.Label(rr, new GUIContent("", Hex(c)));
        if (Event.current.type == EventType.MouseDown && rr.Contains(Event.current.mousePosition)) { Event.current.Use(); return true; }
        return false;
    }

    static void Outline(Rect b, Color c)
    {
        EditorGUI.DrawRect(new Rect(b.x, b.y, b.width, 1), c);
        EditorGUI.DrawRect(new Rect(b.x, b.yMax - 1, b.width, 1), c);
        EditorGUI.DrawRect(new Rect(b.x, b.y, 1, b.height), c);
        EditorGUI.DrawRect(new Rect(b.xMax - 1, b.y, 1, b.height), c);
    }

    public bool HandlePaintKey(Event e)
    {
        if (!Painting || e.type != EventType.KeyDown) return false;
        bool ctrl = e.control || e.command;
        if (ctrl && (e.keyCode == KeyCode.Y || e.keyCode == KeyCode.Z && e.shift)) painter.Redo();
        else if (ctrl && e.keyCode == KeyCode.Z) painter.Undo();
        else if (ctrl && e.keyCode == KeyCode.S) paintNote = painter.Save();
        else if (ctrl) return false;
        else if (e.keyCode == KeyCode.B) painter.tool = AssetStudioPainter.Tool.Paint;
        else if (e.keyCode == KeyCode.I)
        {
            // I takes the colour under the mouse at once and keeps the tool in hand; off the model it picks the tool.
            if (hover.HasValue) { painter.colour = AssetStudioPainter.Sample(hover.Value); paintNote = "Picked " + Hex(painter.colour); }
            else painter.tool = AssetStudioPainter.Tool.Eyedropper;
        }
        else if (e.keyCode == KeyCode.E) painter.tool = AssetStudioPainter.Tool.Eraser;
        else if (e.keyCode == KeyCode.G) painter.tool = AssetStudioPainter.Tool.Fill;
        else if (e.keyCode == KeyCode.LeftBracket) painter.size = Mathf.Max(1, painter.size - 1);
        else if (e.keyCode == KeyCode.RightBracket) painter.size = Mathf.Min(32, painter.size + 1);
        else if (e.keyCode == KeyCode.Escape) StopPainting(true);
        else return false;
        e.Use();
        return true;
    }

    void DrawPaintControls()
    {
        // Three short rows, so it fits the preview column: tools and colour, size and swatches, then undo and save.
        GUILayout.BeginHorizontal();
        foreach (var (tool, label, tip) in new[] {
            (AssetStudioPainter.Tool.Paint, "Brush", "Paint with the colour (B)"),
            (AssetStudioPainter.Tool.Eyedropper, "Pick", "Take the colour under the mouse (I)"),
            (AssetStudioPainter.Tool.Eraser, "Erase", "Put the saved texture's pixels back (E)"),
            (AssetStudioPainter.Tool.Fill, "Fill", "Fill the touching pixels of the same colour (G)") })
            if (GUILayout.Toggle(painter.tool == tool, new GUIContent(label, tip), "Button", GUILayout.Width(58)) && painter.tool != tool) painter.tool = tool;
        GUILayout.Space(6);
        painter.colour = EditorGUILayout.ColorField(GUIContent.none, painter.colour, true, true, false, GUILayout.Width(64));
        if (GUILayout.Button(new GUIContent("Keep", "Keep this colour in the swatches row"), GUILayout.Width(44))) painter.AddSwatch();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent("Size", "How many pixels across the brush is ([ and ])"), GUILayout.Width(30));
        painter.size = EditorGUILayout.IntSlider(painter.size, 1, 32, GUILayout.Width(150));
        painter.round = GUILayout.Toggle(painter.round, new GUIContent("Round", "A round brush instead of a square"), "Button", GUILayout.Width(52));
        GUILayout.Space(6);
        foreach (var sw in painter.swatches.ToArray())
        {
            var rr = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(18));
            rr.y += 1; rr.height = 16;
            EditorGUI.DrawRect(rr, Color.black);
            EditorGUI.DrawRect(new Rect(rr.x + 1, rr.y + 1, rr.width - 2, rr.height - 2), sw);
            if (Event.current.type == EventType.MouseDown && rr.Contains(Event.current.mousePosition)) { painter.colour = sw; painter.tool = AssetStudioPainter.Tool.Paint; Event.current.Use(); }
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        if (painter.palette.Count > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Palette", "This model's 16 most used colours: click one to paint with it"), GUILayout.Width(44));
            foreach (var c in painter.palette)
                if (Swatch(c, 20)) { painter.colour = c; if (painter.tool != AssetStudioPainter.Tool.Fill) painter.tool = AssetStudioPainter.Tool.Paint; }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        GUI.enabled = painter.CanUndo; if (GUILayout.Button(new GUIContent("Undo", "Ctrl+Z"), GUILayout.Width(50))) painter.Undo();
        GUI.enabled = painter.CanRedo; if (GUILayout.Button(new GUIContent("Redo", "Ctrl+Y"), GUILayout.Width(50))) painter.Redo();
        GUI.enabled = painter.Changed > 0;
        if (GUILayout.Button(new GUIContent("Revert", "Throw away everything not saved"), GUILayout.Width(56))) painter.Revert();
        GUILayout.FlexibleSpace();
        GUI.backgroundColor = painter.Changed > 0 ? new Color(.55f, 1f, .55f) : Color.white;
        if (GUILayout.Button(new GUIContent(painter.Changed > 0 ? "Save to texture" : "All saved", "Write the painting into the texture file (Ctrl+S); the old file is kept in ArtRevisions"), GUILayout.Width(110))) paintNote = painter.Save();
        GUI.backgroundColor = Color.white;
        GUI.enabled = true;
        if (GUILayout.Button(new GUIContent("Done", "Stop painting (Esc)"), GUILayout.Width(50))) StopPainting(true);
        GUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(paintNote)) GUILayout.Label(paintNote, EditorStyles.miniLabel);
    }

    void Render(Rect r, bool toScreen)
    {
        if (util == null || instance == null) return;
        var rot = Quaternion.Euler(pitch, yaw, 0);
        util.camera.transform.position = bounds.center + rot * new Vector3(0, 0, -distance);
        util.camera.transform.rotation = rot;
        util.camera.backgroundColor = Backgrounds[background];
        util.lights[0].intensity = 1.6f; util.lights[0].transform.rotation = Quaternion.Euler(40f, yaw + 40f, 0);
        util.lights[1].intensity = .9f; util.lights[1].transform.rotation = Quaternion.Euler(-20f, yaw - 140f, 0);
        util.ambientColor = new Color(.5f, .5f, .53f);
        ApplyLook();
        util.BeginPreview(r, GUIStyle.none);
        RenderWith(r.width / Mathf.Max(1, r.height));
        // The camera exactly as this frame was drawn: the mouse is turned into a ray through it (MouseRay), so a
        // click hits what is on screen whatever the preview set up for the render.
        drawnViewProj = Matrix4x4.Perspective(util.camera.fieldOfView, r.width / Mathf.Max(1, r.height), util.camera.nearClipPlane, util.camera.farClipPlane) * util.camera.worldToCameraMatrix;
        drawnCamPos = util.camera.transform.position;
        drawnFov = util.camera.fieldOfView; drawnAspect = util.camera.aspect;
        hasDrawn = true;
        var tex = util.EndPreview();
        if (toScreen) GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
    }

    void DrawAudio(Rect r)
    {
        var wave = AssetPreview.GetAssetPreview(clip);
        if (wave != null) GUI.DrawTexture(new Rect(r.x + 10, r.y + r.height * .2f, r.width - 20, r.height * .6f), wave, ScaleMode.StretchToFill);
        if (AudioPlaying && clip != null && clip.length > 0)
        {
            float t = AudioPosition / clip.length;
            EditorGUI.DrawRect(new Rect(r.x + 10 + (r.width - 20) * t, r.y + r.height * .15f, 2, r.height * .7f), new Color(.91f, .74f, .38f));
        }
        GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, 18), clip != null ? $"{clip.name} · {clip.length:0.00} s · {clip.frequency} Hz · {clip.channels} ch" : "", EditorStyles.whiteMiniLabel);
    }

    void DrawTexture(Rect r)
    {
        if (texture == null) return;
        float fitScale = Mathf.Min(r.width / texture.width, r.height / texture.height);
        float scale = zoom == 0 ? (fitScale >= 1 ? Mathf.Floor(fitScale) : fitScale) : zoom;
        var size = new Vector2(texture.width * scale, texture.height * scale);
        var rect = new Rect(r.center - size / 2, size);
        var old = texture.filterMode;
        GUI.BeginClip(r);
        rect.position -= r.position;
        texture.filterMode = FilterMode.Point;
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        GUI.EndClip();
        texture.filterMode = old;
        GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, 18), $"{texture.width} × {texture.height} · {(zoom == 0 ? "fit" : zoom + "×")}", EditorStyles.whiteMiniLabel);
    }

    public void DrawControls(Rect r)
    {
        GUILayout.BeginArea(r);
        GUILayout.BeginHorizontal();
        background = EditorGUILayout.Popup(background, BackgroundNames, GUILayout.Width(80));
        switch (mode)
        {
            case Mode.Model:
                spin = GUILayout.Toggle(spin, "Spin", "Button", GUILayout.Width(60));
                if (GUILayout.Button("Frame (F)", GUILayout.Width(80))) Frame();
                if (GUILayout.Button("Front", GUILayout.Width(55))) { yaw = 180; pitch = 5; spin = false; }
                if (GUILayout.Button("Side", GUILayout.Width(50))) { yaw = 90; pitch = 5; spin = false; }
                if (GUILayout.Button("Top", GUILayout.Width(45))) { yaw = 180; pitch = 85; spin = false; }
                if (CanPaint)
                {
                    GUI.backgroundColor = Painting ? new Color(1f, .8f, .4f) : Color.white;
                    if (GUILayout.Toggle(Painting, new GUIContent("Paint", "Paint straight on the model and save it into its texture"), "Button", GUILayout.Width(55)) != Painting) { if (Painting) StopPainting(true); else StartPainting(); }
                    GUI.backgroundColor = Color.white;
                }
                break;
            case Mode.Audio:
                if (GUILayout.Button(AudioPlaying ? "Stop" : "Play (Space)", GUILayout.Width(100))) TogglePlay();
                audioLoop = GUILayout.Toggle(audioLoop, "Loop", "Button", GUILayout.Width(55));
                if (clips.Length > 1)
                {
                    int i = Array.IndexOf(clips, clip);
                    int n = EditorGUILayout.Popup(Mathf.Max(0, i), clips.Select(c => c.name).ToArray(), GUILayout.Width(200));
                    if (n != i) { StopAudio(); clip = clips[n]; }
                }
                break;
            case Mode.Texture:
                if (GUILayout.Button("Fit", GUILayout.Width(45))) zoom = 0;
                if (GUILayout.Button("1:1", GUILayout.Width(45))) zoom = 1;
                if (GUILayout.Button("−", GUILayout.Width(28))) zoom = Mathf.Max(1, (zoom == 0 ? 2 : zoom) - 1);
                if (GUILayout.Button("+", GUILayout.Width(28))) zoom = Mathf.Min(32, (zoom == 0 ? 1 : zoom) + 1);
                break;
        }
        GUILayout.EndHorizontal();
        if (Painting) DrawPaintControls();
        else if (!string.IsNullOrEmpty(paintNote) && mode == Mode.Model) GUILayout.Label(paintNote, EditorStyles.miniLabel);
        if (mode == Mode.Model && particles.Length > 0)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(vfxPlaying ? "Pause" : "Play", GUILayout.Width(60))) vfxPlaying = !vfxPlaying;
            if (GUILayout.Button("Replay", GUILayout.Width(60))) { vfxTime = 0; vfxPlaying = true; Simulate(); }
            if (GUILayout.Button("Step", GUILayout.Width(50))) { vfxPlaying = false; vfxTime += 1f / 30f; Simulate(); }
            vfxLoop = GUILayout.Toggle(vfxLoop, "Loop", "Button", GUILayout.Width(50));
            float shownTime = vfxLoop && vfxLength > 0 ? vfxTime % vfxLength : Mathf.Min(vfxTime, vfxLength);
            float t = GUILayout.HorizontalSlider(shownTime, 0, vfxLength);
            if (!Mathf.Approximately(t, shownTime)) { vfxPlaying = false; vfxTime = t; Simulate(); }
            GUILayout.Label($"{shownTime:0.00} / {vfxLength:0.00} s", GUILayout.Width(90));
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(mode switch
        {
            Mode.Model when Painting => "B brush · I pick · E erase · G fill · [ ] size · Ctrl+Z undo · Ctrl+S save · Esc done",
            Mode.Model => "Drag to orbit · wheel to zoom · F to frame · Space to pause" + (CanPaint ? " · Paint to draw on it" : ""),
            Mode.Audio => "Space to play or stop",
            Mode.Texture => "Point-filtered, whole-number zooms",
            _ => "",
        }, EditorStyles.miniLabel);
        GUILayout.EndArea();
    }

    // A picture of the preview for a request to Main (960 × 540 for a model; the texture or waveform otherwise).
    public byte[] CapturePng()
    {
        if (mode == Mode.Model && util != null && instance != null)
        {
            // Unity's own still-picture path: it brings its own target and leaves the live preview's camera as it was.
            var rot = Quaternion.Euler(pitch, yaw, 0);
            util.camera.transform.position = bounds.center + rot * new Vector3(0, 0, -distance);
            util.camera.transform.rotation = rot;
            util.camera.backgroundColor = Backgrounds[background];
            ApplyLook();
            util.lights[0].intensity = 1.6f; util.lights[0].transform.rotation = Quaternion.Euler(40f, yaw + 40f, 0);
            util.lights[1].intensity = .9f; util.lights[1].transform.rotation = Quaternion.Euler(-20f, yaw - 140f, 0);
            util.ambientColor = new Color(.5f, .5f, .53f);
            util.BeginStaticPreview(new Rect(0, 0, 960, 540));
            RenderWith(960f / 540f);
            var still = util.EndStaticPreview();
            var png = still.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(still);
            return png;
        }
        Texture src = mode == Mode.Texture ? texture : mode == Mode.Audio ? AssetPreview.GetAssetPreview(clip) : AssetPreview.GetAssetPreview(target);
        if (src == null) return null;
        var copy = RenderTexture.GetTemporary(src.width, src.height, 0);
        Graphics.Blit(src, copy);
        var p2 = RenderTexture.active; RenderTexture.active = copy;
        var t2 = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        t2.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0); t2.Apply();
        RenderTexture.active = p2; RenderTexture.ReleaseTemporary(copy);
        var bytes = t2.EncodeToPNG(); UnityEngine.Object.DestroyImmediate(t2);
        return bytes;
    }

    // ── Editor audio preview (UnityEditor.AudioUtil, internal) ──
    static readonly Type AudioUtil = typeof(Editor).Assembly.GetType("UnityEditor.AudioUtil");
    static object Call(string name, params object[] args)
    {
        var m = AudioUtil?.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
        try { return m?.Invoke(null, args); } catch { return null; }
    }
    static void PlayAudio(AudioClip c, bool loop) { if (c == null) return; StopAudio(); Call("PlayPreviewClip", c, 0, loop); }
    public static void StopAudio() => Call("StopAllPreviewClips");
    static bool AudioPlaying => Call("IsPreviewClipPlaying") is bool b && b;
    static float AudioPosition => Call("GetPreviewClipPosition") is float f ? f : 0f;

    // For tests.
    public string TestRayPaint(Color32 c, int size)
    {
        if (!Painting && !StartPainting()) return "no paint: " + paintNote;
        var r = new Rect(0, 0, 800, 600);
        var ray = MouseRay(r, r.center);
        if (!painter.Raycast(ray, out var hit)) return "ray missed";
        painter.colour = c; painter.size = size; painter.tool = AssetStudioPainter.Tool.Paint;
        painter.BeginStroke(); painter.Apply(hit); painter.EndStroke();
        var t = AssetStudioPainter.TexelAt(hit);
        return $"hit {hit.sheet.path} texel {t.x},{t.y} now {(Color32)hit.sheet.texture.GetPixel(t.x, t.y)} changed {painter.Changed} perUv {hit.worldPerUv:0.###}";
    }
    public void TestView(float y, float p) { yaw = y; pitch = p; spin = false; }
    public static void TestLook(int l, bool density) { look = (Look)l; showDensity = density; }
    public float TestDensity => averageDensity;
    public string TestMode => mode.ToString();
    public int TestParticles => particles.Length;
    public void TestPlayAudio() => PlayAudio(clip, false);
    public bool TestAudioPlaying => AudioPlaying;
    public void TestSeek(float t) { vfxPlaying = false; vfxTime = t; Simulate(); }
    public int TestAliveParticles => particles.Sum(p => p.particleCount);
}

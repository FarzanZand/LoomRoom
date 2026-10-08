using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Whole-screen presentation: per-player rendering effects, the retro pixel grid, the Pixelator (the 3D
// pixel-art look) and the black fade overlay. Scene lighting and moods belong to LightingManager.
public class ScreenManager : Singleton<ScreenManager>
{
    public Image fadeFullscreenImage;

    [Header("Rendering effects")]
    [Tooltip("Used while the Room player is active. Applies during Play mode.")]
    public ScreenEffectSettings roomEffects = ScreenEffectSettings.Inscryption();
    [Tooltip("Used while the Table player is active. Applies during Play mode.")]
    public ScreenEffectSettings tableEffects = new();
    [Min(0), Tooltip("Seconds to crossfade between the room and table effects when the active player changes.")]
    public float effectBlendSeconds = 1.5f;

    [FoldoutGroup("Retro pixel grid"), Tooltip("Master switch for the full-screen pixel grid on both players (Retro Screen pass). Off keeps colour banding and every other effect.")]
    public bool pixelationEnabled = true;
    [FoldoutGroup("Retro pixel grid"), Range(0f, 2f), Tooltip("Multiplier on pixel size for both players. 1 = the per-player Pixel Lines in the effects; 2 = pixels twice as large.")]
    public float pixelationStrength = 1f;
    [FoldoutGroup("Retro pixel grid"), ShowInInspector, ReadOnly, Tooltip("Player's choice from the settings menu. -1 = use the authored values, 0 = off.")]
    public int UserPixelLines { get; private set; } = -1;
    const string PixelLinesPref = "pixel_lines";

    [FoldoutGroup("Pixelator"), LabelText("Enabled"), OnValueChanged(nameof(ApplyPixelLook))]
    [Tooltip("Shadowglass-style 3D pixel art: Pixel Lit materials and a pixel camera. Off = the default look. Applies in Play mode and can be flipped live.")]
    public bool pixelLook;
    [FoldoutGroup("Pixelator"), LabelText("Camera"), OnValueChanged(nameof(ApplyPixelLook))]
    [Tooltip("Default = the Rendering effects above. Clean = native resolution, crisp, the library's clean effects (for when the walls keep their own pixel textures). Low Res = real low-res render, the library's low-res effects.")]
    public PixelatorCamera pixelatorCamera = PixelatorCamera.LowRes;
    [FoldoutGroup("Pixelator"), LabelText("Architecture"), OnValueChanged(nameof(ApplyPixelLook)), Tooltip("Walls, floors, ceilings and dungeon tiles.")]
    public bool pixelateArchitecture = true;
    [FoldoutGroup("Pixelator"), LabelText("Props"), OnValueChanged(nameof(ApplyPixelLook)), Tooltip("Furniture, decor, loot and everything else.")]
    public bool pixelateProps = true;
    [FoldoutGroup("Pixelator"), LabelText("Characters"), OnValueChanged(nameof(ApplyPixelLook)), Tooltip("Players' arms and held items, enemies, NPCs.")]
    public bool pixelateCharacters = true;
    [FoldoutGroup("Pixelator"), LabelText("Object Pixel Size"), Range(1, 8), OnValueChanged(nameof(ApplyPixelLook))]
    [Tooltip("Per-object pixelation: characters and props drawn at 1/N resolution inside a full-res scene. 1 = off. Not used with the Low Res camera (everything is already low-res).")]
    public int objectPixelSize = 4;
    [FoldoutGroup("Pixelator"), LabelText("Low-res Characters"), OnValueChanged(nameof(ApplyPixelLook))]
    public bool lowResCharacters = true;
    [FoldoutGroup("Pixelator"), LabelText("Low-res Props"), OnValueChanged(nameof(ApplyPixelLook))]
    public bool lowResProps;
    [FoldoutGroup("Pixelator"), LabelText("Dither Transparents"), OnValueChanged(nameof(ApplyPixelLook))]
    [Tooltip("See-through materials (slime jelly, glassy things) as a pixel screen-door dither. Off = they keep their original smooth transparency.")]
    public bool ditherTransparents;
    [FoldoutGroup("Pixelator"), LabelText("Library"), InlineEditor, OnValueChanged(nameof(ApplyPixelLook))]
    [Tooltip("Material pairs, texel scales and the camera effects per mode (Tools > LoomRoom > Pixel Look).")]
    public PixelLookLibrary pixelLookLibrary;

    PixelLook.Parts PixelatorParts => new()
    {
        camera = pixelatorCamera, architecture = pixelateArchitecture, props = pixelateProps, characters = pixelateCharacters,
        objectPixelSize = objectPixelSize, lowResCharacters = lowResCharacters, lowResProps = lowResProps,
        ditherTransparents = ditherTransparents,
    };

    public PixelLook PixelLook { get; } = new();

    [Tooltip("Blended on top while the player dies: colour drains, the edges darken.")]
    public ScreenEffectSettings deathEffects = new()
    {
        ambientOcclusion = true,
        vignette = true, vignetteIntensity = .5f, vignetteSmoothness = .45f, vignetteColor = new Color(.12f, 0f, 0f),
        colorGrading = true, saturation = -100f, contrast = 15f, colorFilter = new Color(.9f, .85f, .85f), postExposure = -.2f,
    };

    [FoldoutGroup("Renderer features"), Tooltip("The Desktop Renderer's SSAO feature, switched by the Ambient Occlusion toggles. Its quality settings live on Assets/Game/Rendering/Desktop Renderer.asset.")]
    public ScriptableRendererFeature ambientOcclusionFeature;
    [FoldoutGroup("Renderer features"), Tooltip("The Desktop Renderer's full-screen pass using Hidden/LoomRoom/Retro Screen (pixelate and colour banding).")]
    public ScriptableRendererFeature retroScreenFeature;

    [Button("Reset Room to Inscryption preset")]
    void ResetRoomEffects() => roomEffects = ScreenEffectSettings.Inscryption();

    static readonly int PixelLinesId  = Shader.PropertyToID("_RetroPixelLines");
    static readonly int ColorLevelsId = Shader.PropertyToID("_RetroColorLevels");
    static readonly int DitherId      = Shader.PropertyToID("_RetroDither");
    static readonly int StrengthId    = Shader.PropertyToID("_RetroStrength");

    bool originalAO, originalRetro, effectsInitialized;

    // Pixel Look camera: real low-res rendering through the pipeline asset, restored when it ends.
    UniversalRenderPipelineAsset lowResAsset;
    float originalRenderScale;
    UpscalingFilterSelection originalUpscaling;
    UniversalAdditionalCameraData lowResCamera;
    AntialiasingMode originalAntialiasing;
    EffectLayer roomLayer, tableLayer, deathLayer;
    float deathWeight;
    float tableWeight; // 0 = room effects, 1 = table effects
    Coroutine fadeRoutine;

    // One player's effects as a global volume. The table layer sits above the room layer, so
    // crossfading their weights blends every value, including back to the scene's own settings.
    sealed class EffectLayer
    {
        readonly Volume volume;
        readonly VolumeProfile profile;
        readonly FilmGrain grain;
        readonly Vignette vignette;
        readonly ChromaticAberration chromatic;
        readonly ColorAdjustments grading;

        public EffectLayer(Transform parent, string name, float priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = priority;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = profile;
            grain = profile.Add<FilmGrain>();
            vignette = profile.Add<Vignette>();
            chromatic = profile.Add<ChromaticAberration>();
            grading = profile.Add<ColorAdjustments>();
        }

        public void Apply(ScreenEffectSettings fx, float weight)
        {
            volume.weight = weight;
            Set(grain.type, fx.filmGrain, fx.grainType);
            Set(grain.intensity, fx.filmGrain, fx.filmGrainIntensity);
            Set(grain.response, fx.filmGrain, fx.filmGrainResponse);
            Set(vignette.intensity, fx.vignette, fx.vignetteIntensity);
            Set(vignette.smoothness, fx.vignette, fx.vignetteSmoothness);
            Set(vignette.color, fx.vignette, fx.vignetteColor);
            Set(chromatic.intensity, fx.chromaticAberration, fx.chromaticAberrationIntensity);
            Set(grading.contrast, fx.colorGrading, fx.contrast);
            Set(grading.saturation, fx.colorGrading, fx.saturation);
            Set(grading.colorFilter, fx.colorGrading, fx.colorFilter);
            Set(grading.postExposure, fx.colorGrading, fx.postExposure);
        }

        public void Destroy()
        {
            if (volume != null) { volume.enabled = false; Object.Destroy(volume.gameObject); }
            foreach (var component in profile.components) Object.Destroy(component);
            Object.Destroy(profile);
        }

        static void Set<T>(VolumeParameter<T> parameter, bool on, T value)
        {
            parameter.overrideState = on;
            if (on) parameter.value = value;
        }
    }

    // ── Lifetime ──────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        // Cutscenes hide the HUD, but must never hide the transition overlay.
        var canvas = fadeFullscreenImage != null ? fadeFullscreenImage.canvas : null;
        if (canvas != null)
        {
            var hud = canvas.GetComponentInParent<HudCanvas>();
            if (hud != null && hud.transform != canvas.transform)
                canvas.transform.SetParent(hud.transform.parent, false);
            canvas.overrideSorting = true;
            canvas.sortingOrder = 1000;
        }
        SetAlpha(0f);
    }

    void OnEnable()
    {
        // Renderer features live on a shared asset; remember their state so Play mode leaves it untouched.
        if (ambientOcclusionFeature != null) originalAO = ambientOcclusionFeature.isActive;
        if (retroScreenFeature != null) originalRetro = retroScreenFeature.isActive;

        roomLayer = new EffectLayer(transform, "Room screen effects", 10000);
        tableLayer = new EffectLayer(transform, "Table screen effects", 10001);
        deathLayer = new EffectLayer(transform, "Death screen effects", 10002);
        UserPixelLines = PlayerPrefs.GetInt(PixelLinesPref, -1);
        tableWeight = TargetTableWeight;
        effectsInitialized = true;
        ApplyRenderingEffects();
    }

    void Start() => ApplyPixelLook();

    void Update()
    {
        PixelLook.Tick();
        float step = effectBlendSeconds > 0f ? Time.unscaledDeltaTime / effectBlendSeconds : 1f;
        tableWeight = Mathf.MoveTowards(tableWeight, TargetTableWeight, step);
        ApplyRenderingEffects();
    }

    void OnDisable()
    {
        PixelLook.Set(pixelLookLibrary, false, PixelatorParts);
        if (!effectsInitialized) return;
        if (ambientOcclusionFeature != null) ambientOcclusionFeature.SetActive(originalAO);
        if (retroScreenFeature != null) retroScreenFeature.SetActive(originalRetro);
        SetPixelCamera(PixelatorCamera.Default, 0);
        roomLayer.Destroy();
        tableLayer.Destroy();
        deathLayer.Destroy();
        effectsInitialized = false;
    }

    // ── Rendering effects ─────────────────────────────────────────────

    // ── Pixelator ─────────────────────────────────────────────────────

    // Play mode only; can be flipped live from the inspector.
    void ApplyPixelLook()
    {
        if (Application.isPlaying && isActiveAndEnabled) PixelLook.Set(pixelLookLibrary, pixelLook, PixelatorParts);
    }

    PixelatorCamera PixelCamera => PixelLook.CameraMode;
    ScreenEffectSettings RoomFx => PixelCamera switch
    {
        PixelatorCamera.LowRes => PixelLook.Library.roomEffects,
        PixelatorCamera.Clean => PixelLook.Library.cleanRoomEffects,
        _ => roomEffects,
    };
    ScreenEffectSettings TableFx => PixelCamera switch
    {
        PixelatorCamera.LowRes => PixelLook.Library.tableEffects,
        PixelatorCamera.Clean => PixelLook.Library.cleanTableEffects,
        _ => tableEffects,
    };

    float TargetTableWeight =>
        PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table ? 1f : 0f;

    public void ApplyRenderingEffects()
    {
        if (!effectsInitialized) return;
        float t = Mathf.SmoothStep(0f, 1f, tableWeight);
        var room = RoomFx;
        var table = TableFx;
        roomLayer.Apply(room, 1f - t);
        tableLayer.Apply(table, t);
        deathLayer.Apply(deathEffects, deathWeight);
        SetFeature(ambientOcclusionFeature, (t < .5f ? room : table).ambientOcclusion);

        // One full-screen pass: the stronger side's settings, faded by how much of each side uses it.
        float roomRetro = UsesRetro(room) ? 1f - t : 0f;
        float tableRetro = UsesRetro(table) ? t : 0f;
        var retro = tableRetro > roomRetro ? table : room;
        float strength = roomRetro + tableRetro;
        SetFeature(retroScreenFeature, strength > .001f);
        // The Low Res camera renders at the pixel resolution itself, so the pass only bands colours.
        // Clean renders at native resolution without anti-aliasing.
        var mode = PixelCamera;
        bool lowRes = mode == PixelatorCamera.LowRes;
        SetPixelCamera(mode, lowRes ? PixelLinesFor(t < .5f ? room : table) : 0);
        Shader.SetGlobalFloat(PixelLinesId, lowRes ? 0 : PixelLinesFor(retro));
        Shader.SetGlobalFloat(ColorLevelsId, retro.colorBanding ? retro.colorLevels : 0);
        Shader.SetGlobalFloat(DitherId, retro.dither);
        Shader.SetGlobalFloat(StrengthId, strength);
    }

    bool UsesRetro(ScreenEffectSettings fx) => PixelLinesFor(fx) > 0 || fx.colorBanding;

    // Authored lines, scaled by strength, replaced by the player's own choice when set.
    float PixelLinesFor(ScreenEffectSettings fx)
    {
        if (!pixelationEnabled || !fx.pixelate || UserPixelLines == 0) return 0;
        float lines = UserPixelLines > 0 ? UserPixelLines : fx.pixelLines;
        if (pixelationStrength <= .001f) return 0;
        return Mathf.Clamp(lines / pixelationStrength, 90, 2160);
    }

    // Settings menu: -1 authored, 0 off, otherwise vertical pixel lines.
    public void SetUserPixelLines(int lines)
    {
        UserPixelLines = lines < 0 ? -1 : lines;
        PlayerPrefs.SetInt(PixelLinesPref, UserPixelLines);
        ApplyRenderingEffects();
    }

    // 0 = normal, 1 = fully drained (the death moment).
    public void SetDeathEffect(float weight)
    {
        deathWeight = Mathf.Clamp01(weight);
        ApplyRenderingEffects();
    }

    // Low Res renders the camera at about `lines` vertical pixels (an integer fraction of the
    // screen) and scales up with nearest-neighbour; Clean renders at native size. Both turn off
    // anti-aliasing. Default puts back the pipeline's and camera's own settings.
    void SetPixelCamera(PixelatorCamera mode, float lines)
    {
        if (mode != PixelatorCamera.Default)
        {
            if (lowResAsset == null)
            {
                lowResAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (lowResAsset == null) return;
                originalRenderScale = lowResAsset.renderScale;
                originalUpscaling = lowResAsset.upscalingFilter;
            }
            var cam = Camera.main;
            if (lowResCamera == null && cam != null && cam.TryGetComponent(out lowResCamera))
                originalAntialiasing = lowResCamera.antialiasing;
            if (lowResCamera != null) lowResCamera.antialiasing = AntialiasingMode.None;

            float factor = lines > 0 ? Mathf.Max(1f, Mathf.Round(Screen.height / lines)) : 1f;
            lowResAsset.renderScale = Mathf.Max(.1f, 1f / factor);
            lowResAsset.upscalingFilter = UpscalingFilterSelection.Point;
        }
        else
        {
            if (lowResAsset != null)
            {
                lowResAsset.renderScale = originalRenderScale;
                lowResAsset.upscalingFilter = originalUpscaling;
                lowResAsset = null;
            }
            if (lowResCamera != null) lowResCamera.antialiasing = originalAntialiasing;
            lowResCamera = null;
        }
    }

    static void SetFeature(ScriptableRendererFeature feature, bool on)
    {
        if (feature != null && feature.isActive != on) feature.SetActive(on);
    }

    // ── Fades ─────────────────────────────────────────────────────────

    /// Starts invisible, waits holdDuration, then fades to fully visible over fadeDuration.
    public void FadeIn(float fadeDuration, float holdDuration = 0f)
        => RunFade(FadeRoutine(0f, 1f, fadeDuration, holdDuration));

    /// Starts fully visible, waits holdDuration, then fades to invisible over fadeDuration.
    public void FadeOut(float fadeDuration, float holdDuration = 0f)
        => RunFade(FadeRoutine(1f, 0f, fadeDuration, holdDuration));

    /// Fades to black over fadeInDuration, holds for fadedDuration, then fades back over fadeOutDuration.
    public void FadeInOut(float fadeInDuration, float fadedDuration, float fadeOutDuration)
        => RunFade(FadeInOutRoutine(fadeInDuration, fadedDuration, fadeOutDuration));

    /// Steps the fade through (alpha, seconds) pairs from where it is now, easing each one: eyes opening
    /// and blinking. A negative alpha holds for its seconds.
    public void FadeSteps(Vector2[] steps) => RunFade(StepsRoutine(steps));

    IEnumerator StepsRoutine(Vector2[] steps)
    {
        float alpha = fadeFullscreenImage != null ? fadeFullscreenImage.color.a : 0f;
        foreach (var step in steps)
        {
            if (step.x < 0f) { yield return new WaitForSecondsRealtime(step.y); continue; }
            float from = alpha, elapsed = 0f;
            while (elapsed < step.y)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, step.x, Mathf.SmoothStep(0f, 1f, elapsed / step.y)));
                yield return null;
            }
            SetAlpha(alpha = step.x);
        }
    }

    public void ClearFade()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = null;
        SetAlpha(0f);
    }

    Coroutine RunFade(IEnumerator routine)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(Tracked(routine));
        return fadeRoutine;
    }

    IEnumerator Tracked(IEnumerator routine)
    {
        yield return routine;
        fadeRoutine = null;
    }

    IEnumerator FadeRoutine(float from, float to, float fadeDuration, float holdDuration)
    {
        SetAlpha(from);
        if (holdDuration > 0f) yield return new WaitForSecondsRealtime(holdDuration);
        yield return LerpAlpha(from, to, fadeDuration);
    }

    IEnumerator FadeInOutRoutine(float fadeInDuration, float fadedDuration, float fadeOutDuration)
    {
        yield return LerpAlpha(0f, 1f, fadeInDuration);
        if (fadedDuration > 0f) yield return new WaitForSecondsRealtime(fadedDuration);
        yield return LerpAlpha(1f, 0f, fadeOutDuration);
    }

    IEnumerator LerpAlpha(float from, float to, float duration)
    {
        SetAlpha(from);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetAlpha(to);
    }

    void SetAlpha(float alpha)
    {
        if (fadeFullscreenImage == null) return;
        bool visible = alpha > 0f;
        fadeFullscreenImage.gameObject.SetActive(visible);
        if (visible)
        {
            var c = fadeFullscreenImage.color;
            c.a = alpha;
            fadeFullscreenImage.color = c;
        }
    }
}

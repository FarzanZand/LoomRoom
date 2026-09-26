using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Whole-screen presentation: per-player rendering effects and the black fade overlay.
// Scene lighting and moods belong to LightingManager.
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

    [FoldoutGroup("Pixelation"), Tooltip("Master switch for the pixel grid on both players. Off keeps colour banding and every other effect.")]
    public bool pixelationEnabled = true;
    [FoldoutGroup("Pixelation"), Range(0f, 2f), Tooltip("Multiplier on pixel size for both players. 1 = the per-player Pixel Lines above; 2 = pixels twice as large.")]
    public float pixelationStrength = 1f;
    [FoldoutGroup("Pixelation"), ShowInInspector, ReadOnly, Tooltip("Player's choice from the settings menu. -1 = use the authored values, 0 = off.")]
    public int UserPixelLines { get; private set; } = -1;
    const string PixelLinesPref = "pixel_lines";

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
    ScriptableRendererFeature controlledAO, controlledRetro;
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
        controlledAO = ambientOcclusionFeature;
        if (controlledAO != null) originalAO = controlledAO.isActive;
        controlledRetro = retroScreenFeature;
        if (controlledRetro != null) originalRetro = controlledRetro.isActive;

        roomLayer = new EffectLayer(transform, "Room screen effects", 10000);
        tableLayer = new EffectLayer(transform, "Table screen effects", 10001);
        deathLayer = new EffectLayer(transform, "Death screen effects", 10002);
        UserPixelLines = PlayerPrefs.GetInt(PixelLinesPref, -1);
        tableWeight = TargetTableWeight;
        effectsInitialized = true;
        ApplyRenderingEffects();
    }

    void Update()
    {
        float step = effectBlendSeconds > 0f ? Time.unscaledDeltaTime / effectBlendSeconds : 1f;
        tableWeight = Mathf.MoveTowards(tableWeight, TargetTableWeight, step);
        ApplyRenderingEffects();
    }

    void OnDisable()
    {
        if (!effectsInitialized) return;
        if (controlledAO != null) controlledAO.SetActive(originalAO);
        if (controlledRetro != null) controlledRetro.SetActive(originalRetro);
        roomLayer.Destroy();
        tableLayer.Destroy();
        deathLayer.Destroy();
        effectsInitialized = false;
    }

    // ── Rendering effects ─────────────────────────────────────────────

    float TargetTableWeight =>
        PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table ? 1f : 0f;

    public void ApplyRenderingEffects()
    {
        if (!effectsInitialized) return;
        float t = Mathf.SmoothStep(0f, 1f, tableWeight);
        roomLayer.Apply(roomEffects, 1f - t);
        tableLayer.Apply(tableEffects, t);
        deathLayer.Apply(deathEffects, deathWeight);
        SetFeature(controlledAO, (t < .5f ? roomEffects : tableEffects).ambientOcclusion);

        // One full-screen pass: the stronger side's settings, faded by how much of each side uses it.
        float roomRetro = UsesRetro(roomEffects) ? 1f - t : 0f;
        float tableRetro = UsesRetro(tableEffects) ? t : 0f;
        var retro = tableRetro > roomRetro ? tableEffects : roomEffects;
        float strength = roomRetro + tableRetro;
        SetFeature(controlledRetro, strength > .001f);
        Shader.SetGlobalFloat(PixelLinesId, PixelLinesFor(retro));
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

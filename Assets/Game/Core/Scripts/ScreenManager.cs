using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Sirenix.OdinInspector;

public class ScreenManager : Singleton<ScreenManager>
{
    public Image fadeFullscreenImage;

    [Header("Rendering effects")]
    [Tooltip("Toggle SSAO shading to compare the speckling on equipment. Applies during Play mode.")]
    public bool ambientOcclusion = true;
    [InlineEditor, Tooltip("The active renderer's SSAO feature. Expand to edit its quality, intensity and radius.")]
    public ScriptableRendererFeature ambientOcclusionFeature;
    public bool filmGrain;
    [Range(0, 1), ShowIf("filmGrain")] public float filmGrainIntensity = .2f;
    [Range(0, 1), ShowIf("filmGrain")] public float filmGrainResponse = .8f;

    bool originalAO, effectsInitialized;
    ScriptableRendererFeature controlledAO;
    Volume effectsVolume;
    VolumeProfile effectsProfile;
    FilmGrain grain;

    void OnEnable()
    {
        controlledAO = ambientOcclusionFeature;
        if (controlledAO != null) originalAO = controlledAO.isActive;
        var effectObject = new GameObject("Screen rendering effects");
        effectObject.transform.SetParent(transform, false);
        effectsVolume = effectObject.AddComponent<Volume>();
        effectsVolume.isGlobal = true;
        effectsVolume.priority = 10000;
        effectsProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        effectsVolume.sharedProfile = effectsProfile;
        grain = effectsProfile.Add<FilmGrain>(true);
        effectsInitialized = true;
        ApplyRenderingEffects();
    }

    void Update() => ApplyRenderingEffects();

    public void ApplyRenderingEffects()
    {
        if (!effectsInitialized) return;
        if (controlledAO != null && controlledAO.isActive != ambientOcclusion)
            controlledAO.SetActive(ambientOcclusion);
        grain.intensity.Override(filmGrain ? filmGrainIntensity : 0);
        grain.response.Override(filmGrainResponse);
    }

    void OnDisable()
    {
        if (!effectsInitialized) return;
        if (controlledAO != null) controlledAO.SetActive(originalAO);
        if (effectsVolume != null) { effectsVolume.enabled = false; Destroy(effectsVolume.gameObject); }
        if (grain != null) Destroy(grain);
        if (effectsProfile != null) Destroy(effectsProfile);
        effectsInitialized = false;
    }

    /// Blend scene lighting and sky without colouring the UI.
    public void BlendToMood(SceneMood mood, float duration = 2f)
    {
        var controller = FindAnyObjectByType<LightingManager>();
        if (controller != null) controller.BlendToMood(mood, duration);
    }

    public void RestoreMood(float duration = 2f)
    {
        var controller = FindAnyObjectByType<LightingManager>();
        if (controller != null) controller.RestoreDefault();
    }

    private Coroutine fadeRoutine;

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

    public Coroutine TransitionThroughBlack(float fadeToBlack, float holdBeforeSwap,
        float holdAfterSwap, float fadeFromBlack, System.Action whileBlack)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(TransitionRoutine(fadeToBlack, holdBeforeSwap,
            holdAfterSwap, fadeFromBlack, whileBlack));
        return fadeRoutine;
    }

    IEnumerator TransitionRoutine(float fadeToBlack, float holdBeforeSwap,
        float holdAfterSwap, float fadeFromBlack, System.Action whileBlack)
    {
        float alpha = fadeFullscreenImage != null && fadeFullscreenImage.gameObject.activeSelf
            ? fadeFullscreenImage.color.a : 0f;
        yield return LerpAlpha(alpha, 1f, fadeToBlack);
        if (holdBeforeSwap > 0f) yield return new WaitForSecondsRealtime(holdBeforeSwap);
        whileBlack?.Invoke();
        // Let activation, Start and Cinemachine's LateUpdate settle behind the cover.
        yield return null;
        yield return null;
        if (holdAfterSwap > 0f) yield return new WaitForSecondsRealtime(holdAfterSwap);
        yield return LerpAlpha(1f, 0f, fadeFromBlack);
        fadeRoutine = null;
    }

    /// Starts invisible, waits holdDuration, then fades to fully visible over fadeDuration.
    public void FadeIn(float fadeDuration, float holdDuration = 0f)
        => RunFade(from: 0f, to: 1f, fadeDuration, holdDuration);

    /// Starts fully visible, waits holdDuration, then fades to invisible over fadeDuration.
    public void FadeOut(float fadeDuration, float holdDuration = 0f)
        => RunFade(from: 1f, to: 0f, fadeDuration, holdDuration);

    /// Fades to black over fadeInDuration, holds for fadedDuration, then fades back over fadeOutDuration.
    public void FadeInOut(float fadeInDuration, float fadedDuration, float fadeOutDuration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeInOutRoutine(fadeInDuration, fadedDuration, fadeOutDuration));
    }

    IEnumerator FadeInOutRoutine(float fadeInDuration, float fadedDuration, float fadeOutDuration)
    {
        yield return LerpAlpha(0f, 1f, fadeInDuration);
        if (fadedDuration > 0f) yield return new WaitForSecondsRealtime(fadedDuration);
        yield return LerpAlpha(1f, 0f, fadeOutDuration);
        fadeRoutine = null;
    }

    void RunFade(float from, float to, float fadeDuration, float holdDuration)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeRoutine(from, to, fadeDuration, holdDuration));
    }

    IEnumerator FadeRoutine(float from, float to, float fadeDuration, float holdDuration)
    {
        SetAlpha(from);

        if (holdDuration > 0f)
            yield return new WaitForSecondsRealtime(holdDuration);

        yield return LerpAlpha(from, to, fadeDuration);
        fadeRoutine = null;
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

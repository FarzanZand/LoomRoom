using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// One player's screen effects, applied by ScreenManager. Anything switched off falls back to the
// scene's own volumes.
[System.Serializable]
public class ScreenEffectSettings
{
    [Tooltip("Screen-space ambient occlusion (the renderer's SSAO feature).")]
    public bool ambientOcclusion = true;

    public bool filmGrain;
    [ShowIf("filmGrain")] public FilmGrainLookup grainType = FilmGrainLookup.Thin1;
    [ShowIf("filmGrain"), Range(0, 1)] public float filmGrainIntensity = .2f;
    [ShowIf("filmGrain"), Range(0, 1)] public float filmGrainResponse = .8f;

    [Tooltip("Draws the world in chunky low-resolution pixels. The HUD stays sharp.")]
    public bool pixelate;
    [ShowIf("pixelate"), Range(90, 1080), Tooltip("Vertical resolution of the pixel grid.")]
    public int pixelLines = 320;

    [Tooltip("Fewer colours per channel, blended with an ordered dither.")]
    public bool colorBanding;
    [ShowIf("colorBanding"), Range(2, 64)] public int colorLevels = 24;
    [ShowIf("colorBanding"), Range(0, 1)] public float dither = 1f;

    public bool vignette;
    [ShowIf("vignette"), Range(0, 1)] public float vignetteIntensity = .45f;
    [ShowIf("vignette"), Range(.01f, 1)] public float vignetteSmoothness = .4f;
    [ShowIf("vignette")] public Color vignetteColor = Color.black;

    public bool chromaticAberration;
    [ShowIf("chromaticAberration"), Range(0, 1)] public float chromaticAberrationIntensity = .15f;

    [Tooltip("Overrides these values of the scene's colour grading.")]
    public bool colorGrading;
    [ShowIf("colorGrading"), Range(-100, 100)] public float contrast;
    [ShowIf("colorGrading"), Range(-100, 100)] public float saturation;
    [ShowIf("colorGrading")] public Color colorFilter = Color.white;
    [ShowIf("colorGrading")] public float postExposure;

    // Dark, grainy, low-res cabin look in the style of Inscryption.
    public static ScreenEffectSettings Inscryption() => new()
    {
        ambientOcclusion = true,
        filmGrain = true, grainType = FilmGrainLookup.Medium3, filmGrainIntensity = .45f, filmGrainResponse = .7f,
        pixelate = true, pixelLines = 240,
        colorBanding = true, colorLevels = 20, dither = 1f,
        vignette = true, vignetteIntensity = .4f, vignetteSmoothness = .5f, vignetteColor = Color.black,
        chromaticAberration = true, chromaticAberrationIntensity = .15f,
        colorGrading = true, contrast = 20f, saturation = -25f, colorFilter = new Color(1f, .9f, .76f), postExposure = .15f,
    };
}

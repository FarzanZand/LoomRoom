using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

// Scene-owned lighting tool on ScreenManager. Presets never modify their source skybox material.
public class SceneMoodController : MonoBehaviour
{
    [Tooltip("Drag a mood preset here, then use Blend Selected Mood in Play Mode. F5 toggles it.")]
    public SceneMood selectedMood;
    [Min(0)] public float blendDuration = 2f;
    [Tooltip("Optional: uses RenderSettings.sun, or the brightest active directional light when empty.")]
    public Light mainLight;
    [Tooltip("Lights brightened by table-only mood presets.")]
    public Light[] tableLights;
    readonly System.Collections.Generic.Dictionary<Light, float> originalTableIntensities = new();
    Coroutine tableRoutine;
    Coroutine routine;
    Material originalSkybox, runtimeSkybox;
    AmbientMode originalAmbientMode;
    SphericalHarmonicsL2 originalProbe;
    float originalAmbientIntensity;
    State original;
    bool captured, previewActive;
    string skyTintProperty;

    struct State
    {
        public Color sky, light, top, horizon, ground, fog;
        public float exposure, intensity;
        public SphericalHarmonicsL2 probe;
    }

    void OnEnable()
    {
        if (InputManager.HasInstance) InputManager.Instance.DebugMoodPreviewRequested += TogglePreview;
    }
    void OnDisable()
    {
        if (tableRoutine != null) StopCoroutine(tableRoutine);
        tableRoutine = null;
        foreach (var entry in originalTableIntensities)
            if (entry.Key != null) entry.Key.intensity = entry.Value;
        originalTableIntensities.Clear();
        if (InputManager.HasInstance) InputManager.Instance.DebugMoodPreviewRequested -= TogglePreview;
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (captured) RestoreOriginal();
    }

    [Button, EnableIf("@UnityEngine.Application.isPlaying")]
    public void BlendSelectedMood() => BlendToMood(selectedMood, blendDuration);

    [Button, EnableIf("@UnityEngine.Application.isPlaying")]
    public void RestoreOriginalMood() => RestoreMood(blendDuration);

    public void TogglePreview()
    {
        if (previewActive) RestoreMood(blendDuration);
        else if (selectedMood != null)
        {
            BlendToMood(selectedMood, blendDuration);
            previewActive = true;
        }
    }

    public void BlendToMood(SceneMood mood, float duration = 2f)
    {
        if (!Application.isPlaying || mood == null) return;
        if (mood.tableLightingOnly)
        {
            previewActive = false;
            FadeTableLights(Mathf.Max(0f, mood.tableLightMultiplier), duration);
            return;
        }
        CaptureOriginal();
        previewActive = false;
        var target = new State { sky=mood.skyTint, exposure=Mathf.Max(0,mood.skyExposure),
            light=mood.lightColor, intensity=Mathf.Max(0,mood.lightIntensity),
            top=mood.ambientSky, horizon=mood.ambientHorizon, ground=mood.ambientGround, fog=mood.fogColor };
        target.probe = AmbientProbe(target.top, target.horizon, target.ground);
        BeginBlend(target, duration, false);
    }

    public void RestoreMood(float duration = 2f)
    {
        if (originalTableIntensities.Count > 0) FadeTableLights(1f, duration);
        if (!captured) return;
        previewActive = false;
        BeginBlend(original, duration, true);
    }

    void FadeTableLights(float multiplier, float duration)
    {
        if (tableRoutine != null) StopCoroutine(tableRoutine);
        if (tableLights != null)
            foreach (var light in tableLights)
                if (light != null && !originalTableIntensities.ContainsKey(light))
                    originalTableIntensities.Add(light, light.intensity);
        tableRoutine = StartCoroutine(BlendTableLights(multiplier, duration));
    }

    IEnumerator BlendTableLights(float multiplier, float duration)
    {
        var from = new System.Collections.Generic.Dictionary<Light, float>();
        foreach (var entry in originalTableIntensities)
            if (entry.Key != null) from.Add(entry.Key, entry.Key.intensity);
        double start = Time.realtimeSinceStartupAsDouble;
        while (true)
        {
            float progress = duration <= 0f ? 1f : Mathf.Clamp01((float)(Time.realtimeSinceStartupAsDouble - start) / duration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            foreach (var entry in from)
                if (entry.Key != null)
                    entry.Key.intensity = Mathf.Lerp(entry.Value, originalTableIntensities[entry.Key] * multiplier, eased);
            if (progress >= 1f) break;
            yield return null;
        }
        tableRoutine = null;
    }

    void CaptureOriginal()
    {
        if (captured) return;
        if (mainLight == null) mainLight = RenderSettings.sun;
        if (mainLight == null)
            foreach (var light in FindObjectsByType<Light>())
                if (light.isActiveAndEnabled && light.type==LightType.Directional && (mainLight==null || light.intensity>mainLight.intensity))
                    mainLight=light;
        originalSkybox=RenderSettings.skybox;
        if (originalSkybox != null)
        {
            runtimeSkybox=new Material(originalSkybox) { name=originalSkybox.name+" (Mood instance)" };
            skyTintProperty=runtimeSkybox.HasProperty("_SkyTint") ? "_SkyTint" : runtimeSkybox.HasProperty("_Tint") ? "_Tint" : null;
            RenderSettings.skybox=runtimeSkybox;
        }
        originalAmbientMode=RenderSettings.ambientMode;
        originalAmbientIntensity=RenderSettings.ambientIntensity;
        originalProbe=RenderSettings.ambientProbe;
        original=ReadCurrent();
        captured=true;
    }

    State ReadCurrent() => new State {
        sky=runtimeSkybox!=null && skyTintProperty!=null ? runtimeSkybox.GetColor(skyTintProperty) : Color.white,
        exposure=runtimeSkybox!=null && runtimeSkybox.HasProperty("_Exposure") ? runtimeSkybox.GetFloat("_Exposure") : 1f,
        light=mainLight!=null ? mainLight.color : Color.white, intensity=mainLight!=null ? mainLight.intensity : 1f,
        top=RenderSettings.ambientSkyColor, horizon=RenderSettings.ambientEquatorColor,
        ground=RenderSettings.ambientGroundColor, fog=RenderSettings.fogColor, probe=RenderSettings.ambientProbe };

    // A low-frequency sky/ground gradient for realtime ambient lighting. Interpolating SH
    // coefficients also preserves a smooth start from the scene's existing skybox lighting.
    static SphericalHarmonicsL2 AmbientProbe(Color sky, Color horizon, Color ground)
    {
        var probe=new SphericalHarmonicsL2();
        probe.AddAmbientLight(horizon.linear*.5f);
        probe.AddDirectionalLight(Vector3.up,sky.linear,.5f);
        probe.AddDirectionalLight(Vector3.down,ground.linear,.5f);
        return probe;
    }

    void BeginBlend(State target, float duration, bool restore)
    {
        if (routine!=null) StopCoroutine(routine);
        routine=null;
        var from=ReadCurrent();
        if (duration<=0f) { Apply(from,target,1);if(restore)RestoreOriginal();return; }
        routine=StartCoroutine(Blend(from,target,duration,restore));
    }
    IEnumerator Blend(State from, State target, float duration, bool restore)
    {
        float elapsed=0;
        while(elapsed<duration)
        {
            elapsed+=Time.unscaledDeltaTime;
            Apply(from,target,Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/duration)));
            yield return null;
        }
        Apply(from,target,1);
        if(restore)RestoreOriginal();
        routine=null;
    }
    void Apply(State from, State to, float t)
    {
        if(mainLight!=null){mainLight.color=Color.Lerp(from.light,to.light,t);mainLight.intensity=Mathf.Lerp(from.intensity,to.intensity,t);}
        if(runtimeSkybox!=null)
        {
            if(skyTintProperty!=null)runtimeSkybox.SetColor(skyTintProperty,Color.Lerp(from.sky,to.sky,t));
            if(runtimeSkybox.HasProperty("_Exposure"))runtimeSkybox.SetFloat("_Exposure",Mathf.Lerp(from.exposure,to.exposure,t));
        }
        RenderSettings.ambientMode=AmbientMode.Custom;
        RenderSettings.ambientSkyColor=Color.Lerp(from.top,to.top,t);
        RenderSettings.ambientEquatorColor=Color.Lerp(from.horizon,to.horizon,t);
        RenderSettings.ambientGroundColor=Color.Lerp(from.ground,to.ground,t);
        var probe=new SphericalHarmonicsL2();
        for(int channel=0;channel<3;channel++)for(int coefficient=0;coefficient<9;coefficient++)
            probe[channel,coefficient]=Mathf.Lerp(from.probe[channel,coefficient],to.probe[channel,coefficient],t);
        RenderSettings.ambientProbe=probe;
        RenderSettings.fogColor=Color.Lerp(from.fog,to.fog,t);
    }
    void RestoreOriginal()
    {
        Apply(original,original,1);
        RenderSettings.skybox=originalSkybox;
        RenderSettings.ambientMode=originalAmbientMode;
        RenderSettings.ambientIntensity=originalAmbientIntensity;
        RenderSettings.ambientProbe=originalProbe;
        if(runtimeSkybox!=null)Destroy(runtimeSkybox);
        runtimeSkybox=null;captured=false;previewActive=false;
    }
}

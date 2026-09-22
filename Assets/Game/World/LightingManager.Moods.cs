using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class LightingManager
{
    [Title("Scene moods"), InlineEditor]
    public SceneMood moodPreset;
    [Min(0f)] public float moodBlendDuration = 3f;
    [SerializeField, HideInInspector] LightingDefault savedDefault;
    [SerializeField, HideInInspector] Material baseSkybox;
    Material moodSkybox;
    bool moodActive, moodBlending;
    bool keyboardMoodPreview;
    InputManager moodInput;
    double moodStart;
    float activeMoodDuration;
    MoodState displayedMood, moodFrom, moodTo;

    [Serializable]
    public struct MoodState
    {
        public Color sky, light, top, horizon, ground, fog;
        public float exposure, intensity;
    }

    [Serializable]
    public class LightingDefault
    {
        public float tableBrightness, sceneBrightness, ambient;
        public Color tableTint, sceneTint, fog;
        public bool fogOverride, hasMood;
        public MoodState mood;
    }

    void InitializeMoods()
    {
        BindMoodInput();
        if (baseSkybox == null) baseSkybox = RenderSettings.skybox;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += BeforeSceneSave;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += AfterSceneSave;
#endif
    }

    void Start() => BindMoodInput();

    void BindMoodInput()
    {
        if (!Application.isPlaying || !InputManager.HasInstance) return;
        if (moodInput != null) moodInput.DebugMoodPreviewRequested -= ToggleMood;
        moodInput = InputManager.Instance;
        moodInput.DebugMoodPreviewRequested += ToggleMood;
    }

    [Button("Toggle Mood (F5)")]
    public void ToggleMood()
    {
        if (keyboardMoodPreview) { RestoreDefault(); return; }
        if (moodPreset == null) return;
        if (savedDefault == null) SaveCurrentAsDefault();
        BlendSelectedMood();
        keyboardMoodPreview = true;
    }

    [Button("Preview Mood"), HorizontalGroup("Mood actions")]
    public void PreviewMood() => BlendToMood(moodPreset, 0f);

    [Button("Blend to Mood"), HorizontalGroup("Mood actions")]
    public void BlendSelectedMood() => BlendToMood(moodPreset, moodBlendDuration);

    public void BlendToMood(SceneMood preset, float duration = 3f)
    {
        if (preset == null) return;
        BlendToMood(new MoodState { sky = preset.skyTint, exposure = preset.skyExposure,
            light = preset.lightColor, intensity = preset.lightIntensity,
            top = preset.ambientSky, horizon = preset.ambientHorizon,
            ground = preset.ambientGround, fog = preset.fogColor }, duration);
    }

    public void BlendToMood(MoodState settings, float duration = 3f)
    {
        keyboardMoodPreview = false;
        moodFrom = moodActive ? displayedMood : ReadBaseMood();
        moodTo = settings;
        moodActive = true;
        moodStart = Time.realtimeSinceStartupAsDouble;
        activeMoodDuration = Mathf.Max(0f, duration);
        moodBlending = true;
        Update();
    }

    [Button("Save Current as Default"), HorizontalGroup("Default actions")]
    public void SaveCurrentAsDefault()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Save default lighting");
#endif
        savedDefault = new LightingDefault { tableBrightness = brightness, tableTint = tint,
            sceneBrightness = sceneBrightness, sceneTint = sceneTint, ambient = ambientMultiplier,
            fogOverride = overrideFogColor, fog = fogColor, hasMood = moodActive,
            mood = moodActive ? displayedMood : ReadBaseMood() };
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    [Button("Restore Default"), HorizontalGroup("Default actions")]
    public void RestoreDefault()
    {
        keyboardMoodPreview = false;
        if (savedDefault == null) return;
        fading = moodBlending = false;
        brightness = savedDefault.tableBrightness; tint = savedDefault.tableTint;
        sceneBrightness = savedDefault.sceneBrightness; sceneTint = savedDefault.sceneTint;
        ambientMultiplier = savedDefault.ambient; overrideFogColor = savedDefault.fogOverride;
        fogColor = savedDefault.fog; moodActive = savedDefault.hasMood;
        displayedMood = savedDefault.mood;
        if (!moodActive) ClearMoodSky();
        Apply();
    }

    MoodState ReadBaseMood()
    {
        Light sun = RenderSettings.sun;
        float intensity = sun != null ? sun.intensity : 1f;
        Color color = sun != null ? sun.color : Color.white;
        if (sceneSources != null) foreach (var source in sceneSources)
            if (source.light != null && source.light.type == LightType.Directional)
            { intensity = source.baseIntensity; color = source.baseColor; break; }
        return new MoodState { sky = ReadSkyColor(baseSkybox), exposure = baseSkybox != null && baseSkybox.HasProperty("_Exposure") ? baseSkybox.GetFloat("_Exposure") : 1f,
            light = color, intensity = intensity, top = originalAmbientSky,
            horizon = originalAmbientHorizon, ground = originalAmbientGround, fog = originalFog };
    }

    static Color ReadSkyColor(Material sky) => sky == null ? Color.white : sky.HasProperty("_SkyTint") ? sky.GetColor("_SkyTint") : sky.HasProperty("_Tint") ? sky.GetColor("_Tint") : Color.white;

    void UpdateMood()
    {
        if (!moodBlending) return;
        float t = activeMoodDuration <= 0f ? 1f : Mathf.Clamp01((float)(Time.realtimeSinceStartupAsDouble - moodStart) / activeMoodDuration);
        float ease = Mathf.SmoothStep(0, 1, t);
        displayedMood = new MoodState { sky = Color.Lerp(moodFrom.sky, moodTo.sky, ease), exposure = Mathf.Lerp(moodFrom.exposure, moodTo.exposure, ease),
            light = Color.Lerp(moodFrom.light, moodTo.light, ease), intensity = Mathf.Lerp(moodFrom.intensity, moodTo.intensity, ease),
            top = Color.Lerp(moodFrom.top, moodTo.top, ease), horizon = Color.Lerp(moodFrom.horizon, moodTo.horizon, ease),
            ground = Color.Lerp(moodFrom.ground, moodTo.ground, ease), fog = Color.Lerp(moodFrom.fog, moodTo.fog, ease) };
        if (t >= 1f) moodBlending = false;
    }

    void ApplyMood()
    {
        if (!moodActive) return;
        if (baseSkybox != null && moodSkybox == null)
            moodSkybox = new Material(baseSkybox) { name = "Lighting Manager sky preview", hideFlags = HideFlags.HideAndDontSave };
        if (moodSkybox != null)
        {
            RenderSettings.skybox = moodSkybox;
            if (moodSkybox.HasProperty("_SkyTint")) moodSkybox.SetColor("_SkyTint", displayedMood.sky);
            else if (moodSkybox.HasProperty("_Tint")) moodSkybox.SetColor("_Tint", displayedMood.sky);
            if (moodSkybox.HasProperty("_Exposure")) moodSkybox.SetFloat("_Exposure", displayedMood.exposure);
        }
        if (sceneSources != null) foreach (var source in sceneSources)
            if (source.light != null && source.light.type == LightType.Directional)
            { source.light.color = displayedMood.light * sceneTint; source.light.intensity = displayedMood.intensity * sceneBrightness; }
        float level = sceneBrightness * ambientMultiplier;
        var top = displayedMood.top * sceneTint * level;
        var horizon = displayedMood.horizon * sceneTint * level;
        var ground = displayedMood.ground * sceneTint * level;
        RenderSettings.ambientSkyColor = top; RenderSettings.ambientEquatorColor = horizon;
        RenderSettings.ambientGroundColor = ground; RenderSettings.ambientLight = horizon;
        var probe = new SphericalHarmonicsL2();
        probe.AddAmbientLight(horizon.linear * .5f);
        probe.AddDirectionalLight(Vector3.up, top.linear, .5f);
        probe.AddDirectionalLight(Vector3.down, ground.linear, .5f);
        RenderSettings.ambientProbe = probe;
        RenderSettings.fogColor = overrideFogColor ? fogColor : displayedMood.fog;
    }

    void ClearMoodSky()
    {
        if (moodSkybox == null) return;
        if (RenderSettings.skybox == moodSkybox) RenderSettings.skybox = baseSkybox;
        if (Application.isPlaying) Destroy(moodSkybox); else DestroyImmediate(moodSkybox);
        moodSkybox = null;
    }

    void ReleaseMoodSky()
    {
        if (moodInput != null) moodInput.DebugMoodPreviewRequested -= ToggleMood;
        moodInput = null;
        keyboardMoodPreview = false;
        moodBlending = moodActive = false;
        ClearMoodSky();
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= BeforeSceneSave;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= AfterSceneSave;
#endif
    }
#if UNITY_EDITOR
    void BeforeSceneSave(UnityEngine.SceneManagement.Scene scene, string path)
    { if (scene == gameObject.scene && moodSkybox != null) RenderSettings.skybox = baseSkybox; }
    void AfterSceneSave(UnityEngine.SceneManagement.Scene scene)
    { if (scene == gameObject.scene && moodActive) ApplyMood(); }
#endif
}

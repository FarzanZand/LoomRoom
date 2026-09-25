using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

// The room's realtime lighting: a table light group and a room light group (brightness and tint),
// the table hand-raise reveal, and scene moods (LightingManager.Moods.cs). Runs in edit mode so the
// Inspector previews live.
[ExecuteAlways]
public sealed partial class LightingManager : MonoBehaviour
{
    public enum Scope { TableOnly, WholeScene }

    [Title("Lighting controls")]
    [EnumToggleButtons]
    [Tooltip("Select which controls to edit. Both sets of settings stay active when switching tabs.")]
    public Scope controlScope;
    [ShowIf("@controlScope == Scope.TableOnly"), LabelText("Table Brightness")]
    [Range(0f, 8f), OnValueChanged(nameof(Apply))]
    [Tooltip("Live brightness. 1 is the original skylight; 0 is off. Works in edit and Play mode.")]
    public float brightness = 1f;
    [ShowIf("@controlScope == Scope.TableOnly"), LabelText("Table Tint")]
    [OnValueChanged(nameof(Apply))]
    public Color tint = Color.white;
    [ShowIf("@controlScope == Scope.WholeScene"), Range(0f, 8f), OnValueChanged(nameof(Apply))]
    [Tooltip("Brightness of the room lights and environment. Does not change the table's dedicated lights.")]
    public float sceneBrightness = 1f;
    [ShowIf("@controlScope == Scope.WholeScene"), OnValueChanged(nameof(Apply))]
    public Color sceneTint = Color.white;
    [ShowIf("@controlScope == Scope.WholeScene")]
    [Range(0f, 4f)] public float ambientMultiplier = 1f;
    [ShowIf("@controlScope == Scope.WholeScene")]
    public bool overrideFogColor;
    [ShowIf("@controlScope == Scope.WholeScene && overrideFogColor")]
    public Color fogColor = Color.gray;

    [Title("Table hand-raise reveal")]
    [Range(0f, 8f)] public float startingBrightness = 1f;
    [Range(0f, 8f)] public float revealedBrightness = 3f;
    [Min(0f), Tooltip("Seconds after the ReachOut trigger before the light starts changing.")]
    public float revealDelay = 0.75f;
    [Min(0f), Tooltip("Seconds to reach the revealed brightness after ReachOut starts.")]
    public float fadeDuration = 6f;

    [Serializable]
    public class Source
    {
        public Light light;
        public float baseIntensity;
        public Color baseColor = Color.white;
    }

    [FoldoutGroup("Light references"), Tooltip("Table group: the authored table and skylight lights.")]
    public Source[] sources;
    [FoldoutGroup("Light references"), Tooltip("Room group: general room lights and the directional light. Keep a light in only one group.")]
    public Source[] sceneSources;

    // The scene's own ambient light and fog; room brightness and tint scale these.
    [SerializeField, HideInInspector] Color originalAmbientSky, originalAmbientHorizon, originalAmbientGround, originalAmbientFlat, originalFog;
    [SerializeField, HideInInspector] float originalAmbientIntensity = 1f;
    SphericalHarmonicsL2 originalProbe;
    bool sceneApplied;
    bool fading;
    float fadeFrom;
    double fadeStart;

    void OnEnable()
    {
        EnableMoods();
        originalProbe = RenderSettings.ambientProbe;
        if (Application.isPlaying && savedDefault != null) RestoreDefault();
        else if (Application.isPlaying) brightness = startingBrightness;
        Apply();
    }

    void OnDisable()
    {
        DisableMoods();
        fading = false;
        if (sceneApplied)
        {
            ApplyGroup(sceneSources, 1f, Color.white);
            ApplyAmbient(1f, Color.white, false);
        }
        sceneApplied = false;
    }

    void OnValidate() { brightness = Mathf.Max(0f, brightness); sceneBrightness = Mathf.Max(0f, sceneBrightness); }

    // ── Reveal ────────────────────────────────────────────────────────

    [Button("Show Before"), HorizontalGroup("Preview")]
    public void ShowBefore() { fading = false; brightness = startingBrightness; Apply(); }

    [Button("Show After"), HorizontalGroup("Preview")]
    public void ShowAfter() { fading = false; brightness = revealedBrightness; Apply(); }

    [Button("Preview / Play Reveal", ButtonSizes.Large)]
    public void PlayReveal()
    {
        fadeFrom = brightness;
        fadeStart = Time.realtimeSinceStartupAsDouble + Mathf.Max(0f, revealDelay);
        fading = true;
        Update();
    }

    void UpdateReveal()
    {
        if (!fading) return;
        double elapsed = Time.realtimeSinceStartupAsDouble - fadeStart;
        float t = elapsed < 0 ? 0f : fadeDuration <= 0f ? 1f : Mathf.Clamp01((float)elapsed / fadeDuration);
        brightness = Mathf.Lerp(fadeFrom, revealedBrightness, Mathf.SmoothStep(0f, 1f, t));
        if (t >= 1f) fading = false;
    }

    // ── Apply ─────────────────────────────────────────────────────────

    void Update()
    {
        UpdateMoodBlend();
        UpdateReveal();
        Apply();
#if UNITY_EDITOR
        if ((fading || moodBlending) && !Application.isPlaying)
        {
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            UnityEditor.SceneView.RepaintAll();
        }
#endif
    }

    void Apply()
    {
        ApplyGroup(sources, brightness, tint);
        ApplyGroup(sceneSources, sceneBrightness, sceneTint);
        if (moodActive) ApplyMood();
        else ApplyAmbient(sceneBrightness * ambientMultiplier, sceneTint, overrideFogColor);
        sceneApplied = true;
    }

    static void ApplyGroup(Source[] group, float level, Color color)
    {
        if (group == null) return;
        foreach (var source in group)
            if (source != null && source.light != null)
            {
                source.light.intensity = source.baseIntensity * level;
                source.light.color = source.baseColor * color;
            }
    }

    void ApplyAmbient(float level, Color color, bool fog)
    {
        RenderSettings.ambientSkyColor = originalAmbientSky * color * level;
        RenderSettings.ambientEquatorColor = originalAmbientHorizon * color * level;
        RenderSettings.ambientGroundColor = originalAmbientGround * color * level;
        RenderSettings.ambientLight = originalAmbientFlat * color * level;
        RenderSettings.ambientIntensity = originalAmbientIntensity * level;
        var probe = originalProbe;
        for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                probe[channel, coefficient] *= level * color[channel];
        RenderSettings.ambientProbe = probe;
        RenderSettings.fogColor = fog ? fogColor : originalFog;
    }
}

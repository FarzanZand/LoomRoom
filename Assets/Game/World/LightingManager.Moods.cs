using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

// Scene moods: blend the sky, directional light, ambient light and fog toward a SceneMood, and
// optionally the table/room light groups. The source skybox material is never modified.
public sealed partial class LightingManager
{
    [Title("Scene moods"), InlineEditor]
    public SceneMood moodPreset;
    [Min(0f)] public float moodBlendDuration = 3f;
    [SerializeField, HideInInspector] LightingDefault savedDefault;
    [SerializeField, HideInInspector] bool hasSavedDefault;
    // Unity never leaves savedDefault null; scenes saved before the flag existed are detected by their non-zero values.
    bool HasSavedDefault => hasSavedDefault || savedDefault != null && (savedDefault.ambient > 0f || savedDefault.sceneBrightness > 0f);
    [SerializeField, HideInInspector] Material baseSkybox;

    Material moodSkybox;
    bool moodActive, moodBlending, keyboardMoodPreview;
    InputManager moodInput;
    double moodStart;
    float activeMoodDuration;
    MoodState displayedMood, moodFrom, moodTo;
    // Group values from before the first group-overriding mood, restored when a later mood doesn't override.
    bool groupOverrideActive;
    LightGroups groupsBeforeOverride, groupsFrom, groupsTo;

    [Serializable]
    public struct MoodState
    {
        public Color sky, light, top, horizon, ground, fog;
        public float exposure, intensity;
        public bool overrideLightGroups;
        public float tableBrightness, roomBrightness;
        public Color tableTint, roomTint;
        // 1 for dungeon floors: the Lighting Manager's dungeon darkness applies. 0 for the room.
        public float darkness;
        // While a dungeon floor is dark, how much of the room's own light is taken away: 0 keeps it, 1 is black
        // with only the table lamp (TableLevelData.roomLighting).
        public float roomDark;
        // The colour the room's light takes while a table level is played; alpha 0 leaves it alone.
        public Color roomShade;

        public static MoodState LerpEnvironment(MoodState a, MoodState b, float t) => new MoodState {
            darkness = Mathf.Lerp(a.darkness, b.darkness, t), roomDark = Mathf.Lerp(a.roomDark, b.roomDark, t),
            roomShade = Color.Lerp(a.roomShade.a > 0 ? a.roomShade : Color.white, b.roomShade.a > 0 ? b.roomShade : Color.white, t),
            sky = Color.Lerp(a.sky, b.sky, t), exposure = Mathf.Lerp(a.exposure, b.exposure, t),
            light = Color.Lerp(a.light, b.light, t), intensity = Mathf.Lerp(a.intensity, b.intensity, t),
            top = Color.Lerp(a.top, b.top, t), horizon = Color.Lerp(a.horizon, b.horizon, t),
            ground = Color.Lerp(a.ground, b.ground, t), fog = Color.Lerp(a.fog, b.fog, t) };
    }

    struct LightGroups
    {
        public float table, room;
        public Color tableTint, roomTint;

        public static LightGroups Lerp(LightGroups a, LightGroups b, float t) => new LightGroups {
            table = Mathf.Lerp(a.table, b.table, t), room = Mathf.Lerp(a.room, b.room, t),
            tableTint = Color.Lerp(a.tableTint, b.tableTint, t), roomTint = Color.Lerp(a.roomTint, b.roomTint, t) };
    }

    [Serializable]
    public class LightingDefault
    {
        public float tableBrightness, sceneBrightness, ambient;
        public Color tableTint, sceneTint, fog;
        public bool fogOverride, hasMood;
        public MoodState mood;
    }

    public static MoodState FromPreset(SceneMood preset) => new MoodState {
        sky = preset.skyTint, exposure = preset.skyExposure, light = preset.lightColor, intensity = preset.lightIntensity,
        top = preset.ambientSky, horizon = preset.ambientHorizon, ground = preset.ambientGround, fog = preset.fogColor,
        overrideLightGroups = preset.overrideLightGroups, tableBrightness = preset.tableBrightness,
        roomBrightness = preset.roomBrightness, tableTint = preset.tableTint, roomTint = preset.roomTint };

    LightGroups CurrentGroups => new LightGroups { table = brightness, room = sceneBrightness, tableTint = tint, roomTint = sceneTint };

    void SetGroups(LightGroups groups)
    {
        brightness = groups.table; sceneBrightness = groups.room;
        tint = groups.tableTint; sceneTint = groups.roomTint;
    }

    // ── Lifetime ──────────────────────────────────────────────────────

    void EnableMoods()
    {
        BindMoodInput();
        if (baseSkybox == null) baseSkybox = RenderSettings.skybox;
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving += BeforeSceneSave;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaved += AfterSceneSave;
#endif
    }

    // InputManager may not exist yet during OnEnable.
    void Start() { BindMoodInput(); TagRoom(); }

    void BindMoodInput()
    {
        if (!Application.isPlaying || !InputManager.HasInstance) return;
        if (moodInput != null) moodInput.DebugMoodPreviewRequested -= ToggleMood;
        moodInput = InputManager.Instance;
        moodInput.DebugMoodPreviewRequested += ToggleMood;
    }

    void DisableMoods()
    {
        if (moodInput != null) moodInput.DebugMoodPreviewRequested -= ToggleMood;
        moodInput = null;
        keyboardMoodPreview = false;
        moodBlending = moodActive = false;
        if (roomHeld) ReleaseRoom();
        ClearMoodSky();
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaving -= BeforeSceneSave;
        UnityEditor.SceneManagement.EditorSceneManager.sceneSaved -= AfterSceneSave;
#endif
    }

    // ── Moods ─────────────────────────────────────────────────────────

    [Button("Toggle Mood (F5)")]
    public void ToggleMood()
    {
        if (keyboardMoodPreview) { RestoreDefault(); return; }
        if (moodPreset == null) return;
        if (!HasSavedDefault) SaveCurrentAsDefault();
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
        BlendToMood(FromPreset(preset), duration);
    }

    public void BlendToMood(MoodState settings, float duration = 3f)
    {
        keyboardMoodPreview = false;
        clearMoodAfterBlend = false;
        fading = false;
        var current = CurrentGroups;
        if (settings.overrideLightGroups && !groupOverrideActive) groupsBeforeOverride = current;
        groupsFrom = current;
        groupsTo = settings.overrideLightGroups
            ? new LightGroups { table = settings.tableBrightness, room = settings.roomBrightness,
                                tableTint = settings.tableTint, roomTint = settings.roomTint }
            : groupOverrideActive ? groupsBeforeOverride : current;
        groupOverrideActive = settings.overrideLightGroups;
        moodFrom = moodActive ? displayedMood : ReadBaseMood();
        moodTo = settings;
        moodActive = true;
        moodStart = Time.realtimeSinceStartupAsDouble;
        activeMoodDuration = Mathf.Max(0f, duration);
        moodBlending = true;
        Update();
    }

    // Everything the lights show now, to come back to later (the room after a table level).
    public struct Snapshot
    {
        public MoodState mood;           // includes the light groups
        public bool hadMood;             // false: the scene's own ambient and sky were showing
        public bool groupOverride;
        public MoodState groupsBefore;   // only the group fields are used
    }

    public Snapshot Capture()
    {
        var mood = moodActive ? displayedMood : ReadBaseMood();
        mood.overrideLightGroups = true;
        mood.tableBrightness = brightness; mood.roomBrightness = sceneBrightness;
        mood.tableTint = tint; mood.roomTint = sceneTint;
        return new Snapshot
        {
            mood = mood, hadMood = moodActive, groupOverride = groupOverrideActive,
            groupsBefore = new MoodState { tableBrightness = groupsBeforeOverride.table, roomBrightness = groupsBeforeOverride.room,
                                           tableTint = groupsBeforeOverride.tableTint, roomTint = groupsBeforeOverride.roomTint },
        };
    }

    public void Restore(Snapshot snapshot, float duration = 0f)
    {
        BlendToMood(snapshot.mood, duration);
        // Without a mood the scene's own sky, ambient probe and fog were showing: blend towards
        // them, then hand back to them exactly.
        clearMoodAfterBlend = !snapshot.hadMood;
        if (clearMoodAfterBlend && !moodBlending) FinishClearingMood();
        // Afterwards behave as if the intervening moods never happened.
        groupOverrideActive = snapshot.groupOverride;
        groupsBeforeOverride = new LightGroups { table = snapshot.groupsBefore.tableBrightness, room = snapshot.groupsBefore.roomBrightness,
                                                 tableTint = snapshot.groupsBefore.tableTint, roomTint = snapshot.groupsBefore.roomTint };
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
        hasSavedDefault = true;
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        if (!Application.isPlaying) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    [Button("Restore Default"), HorizontalGroup("Default actions")]
    public void RestoreDefault()
    {
        keyboardMoodPreview = false;
        if (!HasSavedDefault) return;
        fading = moodBlending = false;
        groupOverrideActive = false;
        brightness = savedDefault.tableBrightness; tint = savedDefault.tableTint;
        sceneBrightness = savedDefault.sceneBrightness; sceneTint = savedDefault.sceneTint;
        ambientMultiplier = savedDefault.ambient; overrideFogColor = savedDefault.fogOverride;
        fogColor = savedDefault.fog; moodActive = savedDefault.hasMood;
        displayedMood = savedDefault.mood;
        if (!moodActive) ClearMoodSky();
        Apply();
    }

    bool clearMoodAfterBlend;

    void FinishClearingMood()
    {
        clearMoodAfterBlend = false;
        moodActive = false;
        ClearMoodSky();
        Apply();
    }

    void UpdateMoodBlend()
    {
        if (!moodBlending) return;
        float t = activeMoodDuration <= 0f ? 1f : Mathf.Clamp01((float)(Time.realtimeSinceStartupAsDouble - moodStart) / activeMoodDuration);
        float ease = Mathf.SmoothStep(0, 1, t);
        SetGroups(LightGroups.Lerp(groupsFrom, groupsTo, ease));
        var mood = MoodState.LerpEnvironment(moodFrom, moodTo, ease);
        mood.overrideLightGroups = moodTo.overrideLightGroups;
        mood.tableBrightness = brightness; mood.roomBrightness = sceneBrightness;
        mood.tableTint = tint; mood.roomTint = sceneTint;
        displayedMood = mood;
        if (t >= 1f) moodBlending = false;
        if (t >= 1f && clearMoodAfterBlend) FinishClearingMood();
    }

    MoodState ReadBaseMood()
    {
        Light sun = RenderSettings.sun;
        float intensity = sun != null ? sun.intensity : 1f;
        Color color = sun != null ? sun.color : Color.white;
        if (sceneSources != null) foreach (var source in sceneSources)
            if (IsDirectional(source)) { intensity = source.baseIntensity; color = source.baseColor; break; }
        string tintProperty = SkyTintProperty(baseSkybox);
        return new MoodState {
            sky = tintProperty != null ? baseSkybox.GetColor(tintProperty) : Color.white,
            exposure = baseSkybox != null && baseSkybox.HasProperty("_Exposure") ? baseSkybox.GetFloat("_Exposure") : 1f,
            light = color, intensity = intensity, top = originalAmbientSky,
            horizon = originalAmbientHorizon, ground = originalAmbientGround, fog = originalFog };
    }

    // Runs after the light groups, so the directional light and ambient come from the mood.
    void ApplyMood()
    {
        if (baseSkybox != null && moodSkybox == null)
            moodSkybox = new Material(baseSkybox) { name = "Lighting Manager sky preview", hideFlags = HideFlags.HideAndDontSave };
        float dark = dungeonDarkness ? Mathf.Clamp01(displayedMood.darkness) : 0f;
        HoldRoom(dark);
        ApplyRoomDark();
        if (moodSkybox != null)
        {
            RenderSettings.skybox = moodSkybox;
            bool held = HeldSky(out var heldSkyTint, out var heldSkyExposure);
            string tintProperty = SkyTintProperty(moodSkybox);
            if (tintProperty != null) moodSkybox.SetColor(tintProperty, held ? heldSkyTint : displayedMood.sky);
            if (moodSkybox.HasProperty("_Exposure")) moodSkybox.SetFloat("_Exposure", held ? heldSkyExposure : displayedMood.exposure * (1f - RoomDark));
        }
        if (sceneSources != null) foreach (var source in sceneSources)
            if (IsDirectional(source) && !HeldSun(source.light))
            {
                source.light.color = displayedMood.light * sceneTint * RoomTint;
                float sun = displayedMood.intensity * sceneBrightness * (1f - RoomDark);
                source.light.intensity = Mathf.Lerp(sun, Mathf.Min(sun, darkSunIntensity), dark);
            }
        float level = sceneBrightness * ambientMultiplier;
        // Without a dungeon floor's darkness the ambient is the room's own: the level's Room Lighting applies to it.
        if (!roomHeld) level *= 1f - RoomDark;
        var roomColor = roomHeld ? Color.white : RoomTint;
        var top = displayedMood.top * sceneTint * roomColor * level;
        var horizon = displayedMood.horizon * sceneTint * roomColor * level;
        var ground = displayedMood.ground * sceneTint * roomColor * level;
        // Dungeon darkness: hold the preset's ambient down to darkAmbient (measured on its own colours,
        // so the Darkness preset is unchanged), keeping the preset's hues.
        float peak = Mathf.Max(displayedMood.top.maxColorComponent, Mathf.Max(displayedMood.horizon.maxColorComponent, displayedMood.ground.maxColorComponent));
        if (dark > 0f && peak > darkAmbient)
        {
            float scale = Mathf.Lerp(1f, darkAmbient / peak, dark);
            top *= scale; horizon *= scale; ground *= scale;
        }
        RenderSettings.ambientSkyColor = top; RenderSettings.ambientEquatorColor = horizon;
        RenderSettings.ambientGroundColor = ground; RenderSettings.ambientLight = horizon;
        RenderSettings.ambientIntensity = originalAmbientIntensity * level;
        RenderSettings.ambientProbe = GradientProbe(top, horizon, ground);
        RenderSettings.fogColor = overrideFogColor ? fogColor : displayedMood.fog;
        if (darknessFog && dark > .001f)
        {
            if (!sceneFogSaved) { sceneFogSaved = true; sceneFogOn = RenderSettings.fog; sceneFogMode = RenderSettings.fogMode; sceneFogStart = RenderSettings.fogStartDistance; sceneFogEnd = RenderSettings.fogEndDistance; }
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = darknessFogStart;
            RenderSettings.fogEndDistance = Mathf.Lerp(darknessFogEnd * 20f, Mathf.Max(darknessFogStart + .1f, darknessFogEnd), dark);
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, darknessFogColor, dark);
        }
        else RestoreSceneFog();
    }

    // The scene's own fog, put back when the dungeon darkness fog is no longer shown.
    bool sceneFogSaved, sceneFogOn;
    FogMode sceneFogMode;
    float sceneFogStart, sceneFogEnd;
    void RestoreSceneFog()
    {
        if (!sceneFogSaved) return;
        sceneFogSaved = false;
        RenderSettings.fog = sceneFogOn; RenderSettings.fogMode = sceneFogMode;
        RenderSettings.fogStartDistance = sceneFogStart; RenderSettings.fogEndDistance = sceneFogEnd;
    }

    void ClearMoodSky()
    {
        if (moodSkybox == null) return;
        if (RenderSettings.skybox == moodSkybox) RenderSettings.skybox = baseSkybox;
        if (Application.isPlaying) Destroy(moodSkybox); else DestroyImmediate(moodSkybox);
        moodSkybox = null;
    }

    static bool IsDirectional(Source source) =>
        source != null && source.light != null && source.light.type == LightType.Directional;

    static string SkyTintProperty(Material sky) =>
        sky == null ? null : sky.HasProperty("_SkyTint") ? "_SkyTint" : sky.HasProperty("_Tint") ? "_Tint" : null;

    // A low-frequency sky/horizon/ground gradient for realtime ambient lighting.
    static SphericalHarmonicsL2 GradientProbe(Color top, Color horizon, Color ground)
    {
        var probe = new SphericalHarmonicsL2();
        probe.AddAmbientLight(horizon.linear * .5f);
        probe.AddDirectionalLight(Vector3.up, top.linear, .5f);
        probe.AddDirectionalLight(Vector3.down, ground.linear, .5f);
        return probe;
    }

#if UNITY_EDITOR
    // Never save the temporary mood skybox into the scene.
    void BeforeSceneSave(UnityEngine.SceneManagement.Scene scene, string path)
    { if (scene == gameObject.scene && moodSkybox != null) RenderSettings.skybox = baseSkybox; }
    void AfterSceneSave(UnityEngine.SceneManagement.Scene scene)
    { if (scene == gameObject.scene && moodActive) ApplyMood(); }
#endif
}

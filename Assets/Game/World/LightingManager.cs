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
    public enum Scope { TableOnly = 0, WholeScene = 1 }

    // The enabled one in the scene (a plain scene object, not a Singleton): a cheap lookup for
    // cutscenes and loaders instead of searching the scene.
    public static LightingManager Instance { get; private set; }

    // ── Room tab: the bedroom as it is outside a level (a level changes it on its own Lighting tab while played) ──
    [TabGroup("Tabs", "Room"), PropertyOrder(-3)]
    [Title("Room lights", "The bedroom's lamps and the light everywhere in it. While a level is played its own Lighting tab dims or colours the room on top of this.")]
    [LabelText("Room Brightness"), Range(0f, 8f), OnValueChanged(nameof(Apply))]
    [Tooltip("How bright the room's lamps, sun and sky are (1 as authored). The table's two spot lights have their own control below.")]
    public float sceneBrightness = 1f;
    [TabGroup("Tabs", "Room"), PropertyOrder(-3), LabelText("Room Colour"), OnValueChanged(nameof(Apply)), Tooltip("The colour the room's lamps and ambient light take (white: as authored).")]
    public Color sceneTint = Color.white;
    [TabGroup("Tabs", "Room"), PropertyOrder(-3), LabelText("Dark Corners"), Range(0f, 4f), OnValueChanged(nameof(Apply))]
    [Tooltip("The light in the room's unlit corners (its ambient), in every mood: higher makes the room's dark less dark, 0 leaves only what the lamps reach. While no dungeon floor is dark (the intro, board levels) this is the whole scene's ambient.")]
    public float roomAmbient = 1f;

    [TabGroup("Tabs", "Room"), PropertyOrder(1), Title("Table spot lights", "The two spot lights over the table (Systems/Lighting Manager/Skylight Lighting).")]
    [LabelText("Table Spot Lights"), Range(0f, 8f), OnValueChanged(nameof(Apply))]
    [Tooltip("Their brightness: 1 as authored, 0 off.")]
    public float brightness = 1f;
    [TabGroup("Tabs", "Room"), PropertyOrder(1), LabelText("Table Spot Colour"), OnValueChanged(nameof(Apply))]
    public Color tint = Color.white;

    [TabGroup("Tabs", "Room"), PropertyOrder(2), ShowIf(nameof(SceneHasFog)), Title("Fog", "Only while the scene's fog is on (Lighting window).")]
    [LabelText("Own Fog Colour"), OnValueChanged(nameof(Apply))]
    public bool overrideFogColor;
    [TabGroup("Tabs", "Room"), PropertyOrder(2), ShowIf("@SceneHasFog() && overrideFogColor"), OnValueChanged(nameof(Apply))]
    public Color fogColor = Color.gray;
    bool SceneHasFog() => RenderSettings.fog;

    // Kept for older scenes, out of the inspector: the old Table/Whole scene switch only chose which controls showed,
    // and a second ambient slider did what Dark Corners does (left at 1).
    [HideInInspector] public Scope controlScope;
    [HideInInspector, Range(0f, 4f)] public float ambientMultiplier = 1f;

    [TabGroup("Tabs", "Room"), Title("Night light")]
    [Tooltip("The bedroom's soft night light (it replaced the skylight's moon): one of the room lights, so it dims and goes out with them (off in the intro's dark).")]
    public Light nightLight;
    [TabGroup("Tabs", "Room"), OnValueChanged(nameof(Apply)), Tooltip("The night light's colour.")]
    public Color nightLightColor = new(.6f, .72f, 1f);
    [TabGroup("Tabs", "Room"), Min(0f), OnValueChanged(nameof(Apply)), Tooltip("The night light's strength before the room's brightness and moods.")]
    public float nightLightIntensity = 320f;
    [TabGroup("Tabs", "Room"), Tooltip("The shade of the lamp the night light hangs in (the pendant over the table): it glows in the night light's colour, as bright as the light is now (dark when the room's lights are out).")]
    public Renderer nightLightShade;
    [TabGroup("Tabs", "Room"), Range(0f, 4f), OnValueChanged(nameof(Apply)), Tooltip("How brightly the shade glows at the light's full strength.")]
    public float nightShadeGlow = 1.2f;
    MaterialPropertyBlock shadeBlock;
    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    [TabGroup("Tabs", "Dungeon"), PropertyOrder(1), ShowIf(nameof(dungeonDarkness)), LabelText("Dark Corners"), Range(0f, 4f), OnValueChanged(nameof(Apply))]
    [Tooltip("Multiplies the ambient light on dark dungeon floors, after Ambient Cap: higher makes the dungeon's dark less dark (places no torch or carried light reaches). 1 = as is. The room's own Dark Corners is on the Room tab.")]
    public float dungeonAmbient = 1f;

    // The old hand-raise reveal of the table's spot lights (PlayReveal, ShowBefore, ShowAfter): nothing triggers it any
    // more, so its numbers are kept out of the inspector.
    [HideInInspector, Range(0f, 8f)] public float startingBrightness = 1f;
    [HideInInspector, Range(0f, 8f)] public float revealedBrightness = 3f;
    [HideInInspector, Min(0f)] public float revealDelay = 0.75f;
    [HideInInspector, Min(0f)] public float fadeDuration = 6f;

    [TabGroup("Tabs", "Dungeon"), PropertyOrder(0)]
    [Title("Dungeon darkness", "How dark a walled dungeon floor is where no torch or carried light reaches. Each biome's Look tab picks its colours; these hold every floor down.")]
    [OnValueChanged(nameof(Apply))]
    [Tooltip("Dungeons are dark away from light sources whatever their lighting preset: ambient light and the sun are held down, so only torches and the player's own light show the way. A biome can opt out with Lit Throughout.")]
    public bool dungeonDarkness = true;
    [TabGroup("Tabs", "Dungeon")]
    [ShowIf(nameof(dungeonDarkness)), LabelText("Ambient Cap"), PropertyOrder(1), Range(0f, 1f), OnValueChanged(nameof(Apply))]
    [Tooltip("Brightest the preset's ambient colours may be in a dungeon: how visible unlit places are. 0.11 matches the Darkness preset; 0 is pitch black.")]
    public float darkAmbient = .11f;
    [TabGroup("Tabs", "Dungeon")]
    [ShowIf(nameof(dungeonDarkness)), LabelText("Sun Cap"), PropertyOrder(2), Range(0f, 1f), OnValueChanged(nameof(Apply))]
    [Tooltip("Strongest the directional (sun) light may be in a dungeon (little reaches a walled floor anyway). 0.02 matches the Darkness preset.")]
    public float darkSunIntensity = .02f;
    [TabGroup("Tabs", "Dungeon")]
    [ShowIf(nameof(dungeonDarkness)), OnValueChanged(nameof(Apply))]
    [Tooltip("Also fade distant things into a fog colour, even where a light reaches them.")]
    public bool darknessFog;
    [TabGroup("Tabs", "Dungeon")]
    [ShowIf("@dungeonDarkness && darknessFog"), OnValueChanged(nameof(Apply))]
    public Color darknessFogColor = Color.black;
    [TabGroup("Tabs", "Dungeon")]
    [ShowIf("@dungeonDarkness && darknessFog"), Min(0f), OnValueChanged(nameof(Apply)), Tooltip("Metres from the camera where the fog starts.")]
    public float darknessFogStart = 6f;
    [TabGroup("Tabs", "Dungeon")]
    [ShowIf("@dungeonDarkness && darknessFog"), Min(0f), OnValueChanged(nameof(Apply)), Tooltip("Metres from the camera where everything is fog.")]
    public float darknessFogEnd = 24f;

    [Serializable]
    public class Source
    {
        public Light light;
        public float baseIntensity;
        public Color baseColor = Color.white;
    }

    [TabGroup("Tabs", "References"), Title("Light groups")]
    [Tooltip("Table group: the authored table and skylight lights.")]
    public Source[] sources;
    [TabGroup("Tabs", "References")]
    [Tooltip("Room group: general room lights and the directional light. Keep a light in only one group.")]
    public Source[] sceneSources;

    // The scene's own ambient light and fog; room brightness and tint scale these.
    [SerializeField, HideInInspector] Color originalAmbientSky, originalAmbientHorizon, originalAmbientGround, originalAmbientFlat, originalFog;
    [SerializeField, HideInInspector] float originalAmbientIntensity = 1f, originalReflectionIntensity = 1f;
    [TabGroup("Tabs", "Room"), Title("Reflections")]
    [SerializeField, Min(.01f), Tooltip("Sky reflections follow the ambient light: a mood whose ambient reaches this (brightest channel) reflects the sky fully, a darker one less, black not at all. Without it a dark scene still shows the bright sky on glossy materials (Pixel Lit ignores reflections, so this matters with the Pixelator off).")]
    float fullReflectionAmbient = .35f;
    SphericalHarmonicsL2 originalProbe;
    bool sceneApplied;
    bool fading;
    float fadeFrom;
    double fadeStart;

    void OnEnable()
    {
        // The table lamp's own brightness, before the intro (which shares the lamp) turns it down.
        if (lampBase < 0f && tableLamp != null) lampBase = tableLamp.intensity;
        Instance = this;
        EnableMoods();
        originalProbe = RenderSettings.ambientProbe;
        // Play starts from the inspector's values: what is set here is what the game shows. (The saved default is only
        // the Mood preview's point to go back to, Restore Default.)
        Apply();
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
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

    [TabGroup("Tabs", "Table reveal")]
    [Button("Show Before"), HorizontalGroup("Tabs/Table reveal/Preview")]
    public void ShowBefore() { fading = false; brightness = startingBrightness; Apply(); }

    [TabGroup("Tabs", "Table reveal")]
    [Button("Show After"), HorizontalGroup("Tabs/Table reveal/Preview")]
    public void ShowAfter() { fading = false; brightness = revealedBrightness; Apply(); }

    [TabGroup("Tabs", "Table reveal")]
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
        RetagRoom();
        Apply();
        ApplyLightRadius();
        ApplyBuildLight();
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
        // The night light is one of the room lights; its colour and strength are set here.
        if (nightLight != null && sceneSources != null)
            foreach (var source in sceneSources)
                if (source != null && source.light == nightLight) { source.baseColor = nightLightColor; source.baseIntensity = nightLightIntensity; }
        // Room Lighting dims the room's lights; without a dungeon floor's darkness (the intro, a board) the
        // table and skylight group belongs to the room too.
        float roomLevel = 1f - RoomDark;
        ApplyGroup(sources, brightness * (roomHeld ? 1f : roomLevel), tint * (roomHeld ? Color.white : RoomTint));
        ApplyGroup(sceneSources, sceneBrightness * roomLevel, sceneTint * RoomTint);
        GlowNightShade();
        if (moodActive) ApplyMood();
        else { HoldRoom(0f); ApplyAmbient(sceneBrightness * ambientMultiplier * roomAmbient, sceneTint, overrideFogColor); RestoreSceneFog(); }
        sceneApplied = true;
    }

    // The shade follows its light: the light's strength now against its full strength.
    void GlowNightShade()
    {
        if (nightLightShade == null || nightLight == null) return;
        float level = nightLightIntensity > 0f && nightLight.enabled && nightLight.gameObject.activeInHierarchy ? Mathf.Clamp01(nightLight.intensity / nightLightIntensity) : 0f;
        shadeBlock ??= new MaterialPropertyBlock();
        nightLightShade.GetPropertyBlock(shadeBlock);
        shadeBlock.SetColor(EmissionColorId, nightLightColor * (nightShadeGlow * level));
        nightLightShade.SetPropertyBlock(shadeBlock);
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
        RenderSettings.reflectionIntensity = originalReflectionIntensity * Mathf.Clamp01(level);
        var probe = originalProbe;
        for (int channel = 0; channel < 3; channel++)
            for (int coefficient = 0; coefficient < 9; coefficient++)
                probe[channel, coefficient] *= level * color[channel];
        RenderSettings.ambientProbe = probe;
        RenderSettings.fogColor = fog ? fogColor : originalFog;
    }
}

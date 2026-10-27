using Sirenix.OdinInspector;
using UnityEngine;

// The floor on the table lit softly while it is built and flown into. The room's lamps light only the room (TagRoom),
// so without this a floor rising on the table showed only its torches in black blocks. A copy of the night light (the
// lamp over the table) lights only the floor's own rendering layer (Table floor, given to its renderers as the reveal
// starts), at full while the floor is built and fading out as the floor's darkness blends in on the way down, so the
// floor inside looks as before. On the way back to the room the darkness lifts and it lights the floor as it sinks.
public sealed partial class LightingManager
{
    // The floor on the table (TableLevelReveal gives its renderers this rendering layer).
    public const uint TableFloorLayer = 32;

    [TabGroup("Tabs", "Table reveal"), Title("Floor light while it is built")]
    [Tooltip("While a dungeon floor is built on the table and flown into, the lamp over the table lights it softly (only the floor, never the room), fading out as the floor's darkness comes in.")]
    public bool buildLight = true;
    [TabGroup("Tabs", "Table reveal"), ShowIf(nameof(buildLight)), Range(0f, 2f)]
    [Tooltip("Its strength as a share of the night light's.")]
    public float buildLightStrength = .35f;

    Light buildLightInstance;

    void ApplyBuildLight()
    {
        if (!Application.isPlaying || nightLight == null) return;
        var loader = TableManager.Loader;
        var level = loader != null ? loader.Current : null;
        bool floor = buildLight && loader != null && loader.Dungeon != null && level != null && level.IsDungeon && !level.BoardOn(loader.FloorNumber);
        float dark = moodActive ? Mathf.Clamp01(displayedMood.darkness) : 0f;
        float share = floor ? 1f - dark : 0f;
        if (share <= .001f) { if (buildLightInstance != null) buildLightInstance.enabled = false; return; }
        if (buildLightInstance == null)
        {
            var go = new GameObject("Floor build light");
            go.transform.SetParent(nightLight.transform, false);   // a light is its fixture's child
            buildLightInstance = go.AddComponent<Light>();
            var l = buildLightInstance;
            l.type = nightLight.type; l.range = nightLight.range; l.spotAngle = nightLight.spotAngle; l.innerSpotAngle = nightLight.innerSpotAngle;
            l.shadows = nightLight.shadows; l.shadowStrength = nightLight.shadowStrength; l.shadowBias = nightLight.shadowBias; l.shadowNormalBias = nightLight.shadowNormalBias;
            FirstPersonLighting.SetLayers(l, TableFloorLayer);
        }
        buildLightInstance.enabled = true;
        buildLightInstance.color = nightLightColor;
        buildLightInstance.intensity = nightLightIntensity * sceneBrightness * buildLightStrength * share;
    }
}

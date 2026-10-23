using UnityEngine;

// A dungeon floor's light written out by hand (a biome's or level's Custom lighting).
[System.Serializable]
public class DungeonLightingSettings
{
    public Color skyTint = new Color(.4f, .5f, .62f);
    [Min(0)] public float skyExposure = .8f;
    public Color lightColor = new Color(.78f, .85f, 1f);
    [Min(0)] public float lightIntensity = .65f;
    [ColorUsage(false, true)] public Color ambientSky = new Color(.85f, .88f, .94f);
    [ColorUsage(false, true)] public Color ambientHorizon = new Color(.74f, .76f, .79f);
    [ColorUsage(false, true)] public Color ambientGround = new Color(.5f, .52f, .56f);
    [Tooltip("Used when fog is enabled by the scene.")]
    public Color fogColor = new Color(.28f, .34f, .42f);
    public LightingManager.MoodState ToState() => new LightingManager.MoodState {
        sky = skyTint, exposure = skyExposure, light = lightColor, intensity = lightIntensity,
        top = ambientSky, horizon = ambientHorizon, ground = ambientGround, fog = fogColor
    };
}

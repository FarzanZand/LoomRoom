using Sirenix.OdinInspector;
using UnityEngine;

// A lighting preset blended in by LightingManager.BlendToMood.
[CreateAssetMenu(menuName = "World/Scene Mood", fileName = "NewSceneMood")]
public class SceneMood : ScriptableObject
{
    [Header("Separate table and room lighting")]
    [Tooltip("Control the dedicated table lights independently of the room lights while this mood is active.")]
    public bool overrideLightGroups;
    [ShowIf(nameof(overrideLightGroups)), Range(0, 8)] public float tableBrightness = 2f;
    [ShowIf(nameof(overrideLightGroups))] public Color tableTint = new Color(1f, .83f, .62f);
    [ShowIf(nameof(overrideLightGroups)), Range(0, 8)] public float roomBrightness = .08f;
    [ShowIf(nameof(overrideLightGroups))] public Color roomTint = new Color(.65f, .72f, .85f);
    [Header("Sky")]
    public Color skyTint = new Color(.65f, .2f, .18f);
    [Min(0)] public float skyExposure = 1f;
    [Header("Main directional light")]
    public Color lightColor = new Color(1f, .58f, .45f);
    [Min(0)] public float lightIntensity = 1f;
    [Header("Ambient light")]
    [ColorUsage(false, true)] public Color ambientSky = new Color(.48f, .22f, .20f);
    [ColorUsage(false, true)] public Color ambientHorizon = new Color(.28f, .16f, .16f);
    [ColorUsage(false, true)] public Color ambientGround = new Color(.12f, .09f, .10f);
    [Header("Fog colour (when scene fog is enabled)")]
    public Color fogColor = new Color(.48f, .25f, .23f);
}

using UnityEngine;

[CreateAssetMenu(menuName = "World/Scene Mood", fileName = "NewSceneMood")]
public class SceneMood : ScriptableObject
{
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

using UnityEngine;

// The sun or the moon in the sky over the room's skylight (it replaced a pendant lamp that hung from the open skylight).
// It shows the moon while the sky is dark and the sun while it is bright, judged from the sky the room is showing now
// (LightingManager's moods tint and expose the skybox). The sprites always face the camera. Each body has its own light
// (children aimed down through the skylight, listed in LightingManager's scene sources, which set their strength and
// colour); only the showing body's light is on.
public class SkyBody : MonoBehaviour
{
    [SerializeField] Renderer sun, moon;
    [Tooltip("Shine down through the skylight; optional.")]
    [SerializeField] Light sunLight, moonLight;
    [Tooltip("Sky brightness (tint luminance x exposure) under which the moon shows instead of the sun.")]
    [SerializeField, Range(0f, 2f)] float nightBelow = .35f;
    [SerializeField, Min(.1f)] float checkSeconds = .5f;

    float nextCheck;

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            Face(sun, cam);
            Face(moon, cam);
        }
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + checkSeconds;
        bool night = SkyBrightness() < nightBelow;
        if (sun != null) sun.enabled = !night;
        if (moon != null) moon.enabled = night;
        if (sunLight != null) sunLight.enabled = !night;
        if (moonLight != null) moonLight.enabled = night;
    }

    static void Face(Renderer r, Camera cam)
    {
        if (r == null || !r.enabled) return;
        r.transform.rotation = Quaternion.LookRotation(r.transform.position - cam.transform.position, Vector3.up);
    }

    static float SkyBrightness()
    {
        var sky = RenderSettings.skybox;
        if (sky == null) return 1f;
        Color tint = sky.HasProperty("_SkyTint") ? sky.GetColor("_SkyTint") : sky.HasProperty("_Tint") ? sky.GetColor("_Tint") : Color.white;
        float exposure = sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f;
        return tint.grayscale * exposure;
    }
}

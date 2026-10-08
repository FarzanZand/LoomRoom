using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Eyes that glow only in the dark: the light reaching a face (ambient plus the lights that can touch
// it) is estimated each frame, and the glow fades out as the face becomes visible. On the Dungeon
// Master's eyes, so they show while he is a shape in the dark and go once the lamp reaches him.
public class GlowInDark : MonoBehaviour
{
    [Tooltip("The glowing parts.")] public Renderer[] eyes = new Renderer[0];
    [Tooltip("A renderer of the face: its rendering layers decide which lights count.")] public Renderer face;
    [ColorUsage(false, true)] public Color glow = new(1f, .78f, .4f);
    [Tooltip("Light on the face (rough units, as it appears) below which the eyes glow fully, and above which they are gone.")]
    public float fullBelow = .04f, goneAbove = .14f;
    [Min(0), Tooltip("Seconds for the glow to follow a change in light.")] public float fade = .35f;
    [Tooltip("Shows the measured light in the Inspector, to tune the thresholds.")]
    public float measured;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    readonly List<Light> lights = new();
    MaterialPropertyBlock block;
    float level = -1f, refreshAt;
    LightingManager lighting;
    readonly Vector3[] dirs = new Vector3[1];
    readonly Color[] colors = new Color[1];

    void OnEnable() { refreshAt = 0f; level = -1f; }

    void LateUpdate()
    {
        if (Time.unscaledTime >= refreshAt)
        {
            refreshAt = Time.unscaledTime + 1f;
            lights.Clear();
            lights.AddRange(FindObjectsByType<Light>(FindObjectsSortMode.None));
            if (lighting == null) lighting = FindAnyObjectByType<LightingManager>();
        }
        measured = LightOnFace();
        float target = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fullBelow, goneAbove, measured));
        level = level < 0f || fade <= 0f ? target : Mathf.MoveTowards(level, target, Time.unscaledDeltaTime / fade);
        block ??= new MaterialPropertyBlock();
        foreach (var r in eyes)
        {
            if (r == null) continue;
            r.enabled = level > .01f;
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, glow * level);
            r.SetPropertyBlock(block);
        }
    }

    // Rough brightness of the face as the camera sees it: ambient from the face's probe, plus each light
    // that reaches it (range, cone and rendering layers), all as luminance.
    float LightOnFace()
    {
        var at = transform.position;
        var normal = transform.parent != null ? -transform.parent.right : transform.forward;
        if (face != null) normal = face.transform.forward;
        uint layers = face != null ? face.renderingLayerMask : 1u;
        dirs[0] = normal;
        var probe = lighting != null && face != null ? lighting.AmbientFor(face) : RenderSettings.ambientProbe;
        probe.Evaluate(dirs, colors);
        float total = Luma(colors[0]);
        foreach (var l in lights)
        {
            if (l == null || !l.isActiveAndEnabled || l.intensity <= 0f) continue;
            var data = l.GetComponent<UniversalAdditionalLightData>();
            uint lightLayers = data != null ? data.renderingLayers : (uint)l.renderingLayerMask;
            if ((lightLayers & layers) == 0) continue;
            if (l.type == LightType.Directional)
            {
                total += Luma(l.color) * l.intensity * Mathf.Max(0f, Vector3.Dot(-l.transform.forward, normal)) * .5f + Luma(l.color) * l.intensity * .1f;
                continue;
            }
            var toLight = l.transform.position - at;
            float d2 = toLight.sqrMagnitude, range = l.range;
            if (d2 > range * range) continue;
            if (l.type == LightType.Spot && Vector3.Angle(l.transform.forward, -toLight) > l.spotAngle * .5f) continue;
            // URP's falloff: inverse square, smoothed to zero at the range.
            float fall = 1f - Mathf.Pow(d2 / (range * range), 2f); fall *= fall;
            float facing = .35f + .65f * Mathf.Max(0f, Vector3.Dot(toLight.normalized, normal));
            total += Luma(l.color) * l.intensity / Mathf.Max(d2, 1f) * fall * facing;
        }
        return total;
    }

    static float Luma(Color c) => c.r * .2126f + c.g * .7152f + c.b * .0722f;
}

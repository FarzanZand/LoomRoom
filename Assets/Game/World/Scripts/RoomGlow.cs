using System.Collections.Generic;
using UnityEngine;

// Things in the room that glow by themselves (a lantern's glass, candle flames, sconce flames, the trophy wall's empty
// outlines and name plates) go dark with the room: they follow the room's light (LightingManager.RoomLightLevel, down to
// nothing when a level's or the intro's Room Lighting is Dark). Glowing parts lose their glow and darken (so the table
// lamp lit in the dark doesn't make a lantern look on), unlit outlines and text fade out, flames stop burning. Put it on
// the prop's root; it picks up children added later (hung trophies) every second. Lights are left to the Lighting Manager.
[ExecuteAlways]
public class RoomGlow : MonoBehaviour
{
    [Tooltip("Below this share of the room's light the flames go out.")]
    [Range(0f, 1f)] public float flamesOutBelow = .1f;
    [Tooltip("How dark a glowing part's own colour gets with the room's light out (1 = unchanged).")]
    [Range(0f, 1f)] public float darkSurface = .15f;

    enum Kind { Glow, Unlit, Text }
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), BaseId = Shader.PropertyToID("_BaseColor"),
        FaceId = Shader.PropertyToID("_FaceColor"), OutlineId = Shader.PropertyToID("_OutlineColor"), UnderlayId = Shader.PropertyToID("_UnderlayColor");
    readonly List<(Renderer r, Kind kind, Material m)> parts = new();
    readonly List<ParticleSystem> flames = new();
    MaterialPropertyBlock block;
    float applied = -1f, nextScan;
    bool burning = true;

    void OnEnable() { applied = -1f; nextScan = 0f; }

    void OnDisable()
    {
        Apply(1f);
        parts.Clear();
    }

    void Scan()
    {
        parts.Clear(); flames.Clear();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            var m = r.sharedMaterial; if (m == null || m.shader == null) continue;
            string shader = m.shader.name;
            if (shader.StartsWith("TextMeshPro")) parts.Add((r, Kind.Text, m));
            else if (m.HasProperty(EmissionId) && m.IsKeywordEnabled("_EMISSION") && m.GetColor(EmissionId).maxColorComponent > .01f) parts.Add((r, Kind.Glow, m));
            else if ((shader.Contains("Unlit") || shader.Contains("Outline")) && m.HasProperty(BaseId)) parts.Add((r, Kind.Unlit, m));
        }
        GetComponentsInChildren(true, flames);
        applied = -1f;
    }

    void Update()
    {
        float now = Application.isPlaying ? Time.unscaledTime : (float)EditorTime();
        if (now >= nextScan) { nextScan = now + 1f; Scan(); }
        float level = LightingManager.Instance != null ? Mathf.Clamp01(LightingManager.Instance.RoomLightLevel) : 1f;
        if (Mathf.Abs(level - applied) < .005f) return;
        applied = level;
        Apply(level);
        bool burn = level >= flamesOutBelow;
        if (burn == burning) return;
        burning = burn;
        foreach (var p in flames)
        {
            if (p == null) continue;
            if (burn) p.Play(true); else p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    // Adds to whatever block a renderer already has (the Lighting Manager drives the night light shade's glow itself).
    void Apply(float level)
    {
        block ??= new MaterialPropertyBlock();
        var shade = LightingManager.Instance != null ? LightingManager.Instance.nightLightShade : null;
        foreach (var (r, kind, m) in parts)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            switch (kind)
            {
                case Kind.Glow:
                    if (r != shade) block.SetColor(EmissionId, m.GetColor(EmissionId) * level);
                    if (m.HasProperty(BaseId)) block.SetColor(BaseId, Scale(m.GetColor(BaseId), Mathf.Lerp(darkSurface, 1f, level), false));
                    break;
                case Kind.Unlit:
                    block.SetColor(BaseId, Scale(m.GetColor(BaseId), level, true));
                    break;
                case Kind.Text:
                    if (m.HasProperty(FaceId)) block.SetColor(FaceId, Scale(m.GetColor(FaceId), level, true));
                    if (m.HasProperty(OutlineId)) block.SetColor(OutlineId, Scale(m.GetColor(OutlineId), level, true));
                    if (m.HasProperty(UnderlayId)) block.SetColor(UnderlayId, Scale(m.GetColor(UnderlayId), level, true));
                    break;
            }
            r.SetPropertyBlock(block);
        }
    }

    static Color Scale(Color c, float k, bool alpha) => new(c.r * k, c.g * k, c.b * k, alpha ? c.a * k : c.a);

    static double EditorTime()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorApplication.timeSinceStartup;
#else
        return Time.unscaledTime;
#endif
    }
}

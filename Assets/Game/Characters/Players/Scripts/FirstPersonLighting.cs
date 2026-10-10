using System.Collections.Generic;
using UnityEngine;

// Barony-style light around the player without hot spots. The world light hangs well above the
// player's head (it casts no shadows, so ceilings don't block it), which keeps nearby enemies and
// floors from blowing out while still reaching far; a matching light below the floor lights only
// ceilings (their own rendering layer). The first-person arms and held items sit on a third layer
// that neither touches, lit by a soft fill light instead. Torches light every layer.
public class FirstPersonLighting : MonoBehaviour
{
    public const uint WorldLayer = 1, ArmsLayer = 2, CeilingLayer = 4;

    [Tooltip("The light that shows the dungeon around the player.")] public Light worldLight;
    [Tooltip("Soft light for arms and held items only.")] public Light armsLight;
    [Tooltip("Sits below the floor and lights ceilings only (the world light is above them). Follows the world light's range.")] public Light ceilingLight;
    [SerializeField, Min(.1f)] float refreshSeconds = .5f;

    float nextRefresh;
    readonly List<Renderer> renderers = new();

    void OnEnable() { Apply(); var eq = GetComponentInParent<Equipment>(); if (eq != null) eq.Changed += Apply; }
    void OnDisable() { var eq = GetComponentInParent<Equipment>(); if (eq != null) eq.Changed -= Apply; }

    // Spell hands and effects spawn under the rig at any time, so recheck now and then.
    void Update()
    {
        if (ceilingLight != null && worldLight != null) { ceilingLight.range = worldLight.range; ceilingLight.enabled = worldLight.enabled; }
        if (Time.unscaledTime >= nextRefresh) Apply();
    }

    // URP reads the layers from its additional light data; keep the Light's own field in step.
    public static void SetLayers(Light light, uint layers)
    {
        if (light == null) return;
        light.renderingLayerMask = (int)layers;
        var data = light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
        if (data == null) data = light.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
        data.renderingLayers = layers;
    }

    public void Apply()
    {
        nextRefresh = Time.unscaledTime + refreshSeconds;
        SetLayers(worldLight, WorldLayer);
        SetLayers(armsLight, ArmsLayer);
        SetLayers(ceilingLight, CeilingLayer);
        GetComponentsInChildren(true, renderers);
        foreach (var r in renderers)
            if (r.renderingLayerMask != ArmsLayer) r.renderingLayerMask = ArmsLayer;
    }
}

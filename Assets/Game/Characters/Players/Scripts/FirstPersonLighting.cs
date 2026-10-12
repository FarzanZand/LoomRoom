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
    [Tooltip("Heathen's key light: held off to the side of the view (a lantern in the off hand), it lights enemies from one side and throws their shadows across the floor. The world light above stays as a soft fill.")]
    public Light shadowLight;
    [Tooltip("The key light stays this far on the player's side of a wall between the eyes and its place (in a corridor its spot is often inside the wall, which would leave the corridor dark).")]
    [SerializeField, Min(0f)] float shadowLightWallMargin = .3f;
    [SerializeField, Min(.1f)] float refreshSeconds = .5f;

    float nextRefresh;
    Vector3 shadowLightOffset;   // where it is authored, in its parent's space
    bool shadowLightPlaced;
    readonly RaycastHit[] wallHits = new RaycastHit[8];
    readonly List<Renderer> renderers = new();

    void Awake() { if (shadowLight != null) shadowLightOffset = shadowLight.transform.localPosition; }

    void OnEnable() { shadowLightPlaced = false; Apply(); var eq = GetComponentInParent<Equipment>(); if (eq != null) eq.Changed += Apply; }
    void OnDisable() { var eq = GetComponentInParent<Equipment>(); if (eq != null) eq.Changed -= Apply; }

    // Spell hands and effects spawn under the rig at any time, so recheck now and then.
    void Update()
    {
        if (ceilingLight != null && worldLight != null) { ceilingLight.range = worldLight.range; ceilingLight.enabled = worldLight.enabled; }
        if (shadowLight != null && worldLight != null) shadowLight.enabled = worldLight.enabled;
        if (Time.unscaledTime >= nextRefresh) Apply();
    }

    void LateUpdate() => PlaceShadowLight();

    // From the eyes toward the authored spot; a wall in between brings the light in front of it.
    // It moves in quickly and eases back out, so it never flickers between the two.
    void PlaceShadowLight()
    {
        if (shadowLight == null || shadowLight.transform.parent == null) return;
        var parent = shadowLight.transform.parent;
        var eye = armsLight != null && armsLight.transform.parent != null ? armsLight.transform.parent.position : parent.position;   // the player camera
        var wanted = parent.TransformPoint(shadowLightOffset);
        var path = wanted - eye;
        float length = path.magnitude, reach = length;
        if (length > .001f)
        {
            int count = Physics.SphereCastNonAlloc(eye, .12f, path / length, wallHits, length + shadowLightWallMargin, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = wallHits[i];
                if (hit.distance <= 0f || hit.collider.attachedRigidbody != null || hit.collider.GetComponentInParent<Character>() != null) continue;
                reach = Mathf.Min(reach, Mathf.Max(0f, hit.distance - shadowLightWallMargin));
            }
        }
        var target = eye + (length > .001f ? path / length * reach : Vector3.zero);
        var current = shadowLight.transform.position;
        float speed = (target - eye).sqrMagnitude < (current - eye).sqrMagnitude ? 40f : 6f;
        shadowLight.transform.position = shadowLightPlaced ? Vector3.Lerp(current, target, 1f - Mathf.Exp(-speed * Time.deltaTime)) : target;
        shadowLightPlaced = true;
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
        SetLayers(shadowLight, WorldLayer);
        GetComponentsInChildren(true, renderers);
        foreach (var r in renderers)
            if (r.renderingLayerMask != ArmsLayer) r.renderingLayerMask = ArmsLayer;
    }
}

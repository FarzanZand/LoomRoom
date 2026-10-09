using System.Collections.Generic;
using UnityEngine;

// On the first-person arms rig: every renderer under it (the arms, and whatever is equipped into the
// hands later) goes on the ViewModel layer, which ViewModelFeature draws after the world so walls never
// cut into it. Objects with an active collider (the weapon's hitbox) keep their layer, so physics is
// unchanged; held items' own colliders are switched off by Equipment, so those items move too.
// The Pixelator leaves view model renderers at full resolution (PixelLook).
[DisallowMultipleComponent]
public class ViewModel : MonoBehaviour
{
    public const string LayerName = "ViewModel";
    [Min(.05f), Tooltip("Seconds between checks for newly equipped items.")] public float recheckSeconds = .2f;

    readonly List<Renderer> renderers = new();
    float next;

    void OnEnable() => Apply();

    void LateUpdate()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + recheckSeconds;
        Apply();
    }

    readonly List<Collider> colliders = new();

    bool HasActiveCollider(GameObject go)
    {
        go.GetComponents(colliders);
        foreach (var c in colliders) if (c.enabled) return true;
        return false;
    }

    void Apply()
    {
        int layer = LayerMask.NameToLayer(LayerName);
        if (layer < 0) return;
        GetComponentsInChildren(true, renderers);
        foreach (var r in renderers)
            if (r.gameObject.layer != layer && !HasActiveCollider(r.gameObject)) r.gameObject.layer = layer;
    }
}

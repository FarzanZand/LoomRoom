using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class DungeonGenerator
{
    const float LightClusterRadius = 3f;

    // Props set down close together each bring their own glow (a furnace by a smelting pot by a brazier: the Forge built
    // over 200 lights a floor). Shadowless point lights of about one colour within a few metres of each other become
    // one: the brightest stays on its own prop, brighter and reaching a little further, and the others are switched off
    // (the reveal leaves a switched-off light off). Left as they are: lamps with shadows (torches), what characters
    // carry, lights not in play yet (the boss stage's hidden stair, rooms a board hasn't dealt in), the boss stage's
    // fires (BossStageLights keeps their strengths and drives them) and lights on breakables (broken, a kept light would
    // leave its merged neighbours dark). A step of the build: it yields when the slice's time is up.
    IEnumerator MergeNearbyLights()
    {
        var lights = new List<Light>();
        foreach (var l in GetComponentsInChildren<Light>(true))
            if (l != null && l.isActiveAndEnabled && l.type == LightType.Point && l.shadows == LightShadows.None && Mergeable(l))
                lights.Add(l);
        lights.Sort((a, b) => b.intensity.CompareTo(a.intensity));
        var merged = new HashSet<Light>();
        foreach (var keep in lights)
        {
            if (SliceDue) { Pause(); yield return null; Resume(); }
            if (keep == null || merged.Contains(keep)) continue;
            float added = 0f, reach = 0f;
            foreach (var other in lights)
            {
                if (other == null || other == keep || merged.Contains(other)) continue;
                float distance = Vector3.Distance(keep.transform.position, other.transform.position);
                if (distance > LightClusterRadius || !Alike(keep.color, other.color)) continue;
                merged.Add(other);
                added += other.intensity;
                reach = Mathf.Max(reach, distance + other.range - keep.range);
                other.enabled = false;
                if (other.TryGetComponent<TorchFlicker>(out var otherFlicker)) otherFlicker.enabled = false;
            }
            if (added <= 0f) continue;
            merged.Add(keep);
            // Half of what the others gave: they lit from beside it, and a sum would burn out the spot under it.
            keep.intensity += added * .5f;
            keep.range += Mathf.Max(0f, reach) * .5f;
            if (keep.TryGetComponent<TorchFlicker>(out var flicker)) flicker.Rebase();
        }
    }

    static bool Mergeable(Light l) => l.GetComponentInParent<Character>(true) == null && l.GetComponentInParent<BossStageLights>(true) == null
        && l.GetComponentInParent<BossStageFire>(true) == null && l.GetComponentInParent<DungeonDestructible>(true) == null;

    static bool Alike(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < .35f;
}

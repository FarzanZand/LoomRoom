using System.Collections.Generic;
using UnityEngine;

// Burning ground left where a spell burst (a Molten Boulder): every hostile standing in it is hurt a little every half
// second until it burns out. The prefab carries the flames (a SpellVisual, stopped gently at the end); this script is
// on its root and is started with Spawn.
[RequireComponent(typeof(SpellVisual))]
public class GroundFire : MonoBehaviour
{
    [Tooltip("The radius the prefab's flames are drawn at; it is scaled to the radius it is given.")]
    [Min(.1f)] public float drawnRadius = 1.5f;

    Character owner;
    float radius, perTick, until, nextTick;
    string sourceName;
    const float Tick = .5f;

    static readonly HashSet<GroundFire> live = new();
    void OnEnable() => live.Add(this);
    void OnDisable() => live.Remove(this);
    public static void ClearAll()
    {
        foreach (var x in new List<GroundFire>(live)) if (x != null) Destroy(x.gameObject);
        live.Clear();
    }

    public static GroundFire Spawn(GroundFire prefab, Vector3 point, Character owner, float seconds, float radius, float damagePerSecond, string sourceName)
    {
        if (prefab == null) return null;
        var fire = Instantiate(prefab, GroundAreaSpell.GroundBelow(point) + Vector3.up * .03f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        fire.transform.localScale *= radius / Mathf.Max(.1f, fire.drawnRadius);
        fire.owner = owner; fire.radius = radius; fire.perTick = damagePerSecond * Tick; fire.sourceName = sourceName;
        fire.until = Time.time + seconds; fire.nextTick = Time.time + Tick;
        return fire;
    }

    void Update()
    {
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        if (Time.time >= until)
        {
            enabled = false;
            GetComponent<SpellVisual>().Stop(false);
            return;
        }
        if (Time.time < nextTick) return;
        nextTick += Tick;
        // A flat, low reach: only what stands in the flames.
        foreach (var c in SpellEffects.HostilesNear(owner, transform.position + Vector3.up * .4f, radius))
        {
            var flat = c.transform.position - transform.position; flat.y = 0f;
            if (flat.magnitude > radius || Mathf.Abs(c.transform.position.y - transform.position.y) > 1f) continue;
            SpellEffects.Strike(owner, c, perTick, c.transform.position + Vector3.up * .3f, transform.position, sourceName);
        }
    }
}

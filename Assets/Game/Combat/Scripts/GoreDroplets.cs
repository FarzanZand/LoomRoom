using System.Collections.Generic;
using UnityEngine;

// On the blood spray prefab: where a drop hits the world, GoreManager leaves a stain in this spray's colour.
// Drops that hit a creature just end there.
[RequireComponent(typeof(ParticleSystem))]
public class GoreDroplets : MonoBehaviour
{
    public Color Color { get; set; } = Color.clear;
    // A stream's own limits (GoreManager.Stream: a wound that keeps bleeding): the stains it may still leave, the share
    // of its landing drops that try to, and no stains on loose physics pieces (a stain doesn't follow one). A hit's
    // spray never sets them.
    public int StainsLeft { get; set; } = int.MaxValue;
    public float StainShare { get; set; } = 1f;
    public bool SkipBodies { get; set; }
    ParticleSystem ps;
    readonly List<ParticleCollisionEvent> events = new();

    void Awake() => ps = GetComponent<ParticleSystem>();

    void OnParticleCollision(GameObject other)
    {
        if (Color.a <= 0f || StainsLeft <= 0 || !GoreManager.HasInstance || other.GetComponentInParent<Character>() != null) return;
        if (SkipBodies && other.GetComponentInParent<Rigidbody>() != null) return;
        int count = ps.GetCollisionEvents(other, events);
        // A stain on a barrel or crate goes with it when it breaks.
        var breakable = other.GetComponentInParent<DungeonDestructible>();
        for (int i = 0; i < count && StainsLeft > 0; i++)
        {
            if (StainShare < 1f && Random.value > StainShare) continue;
            if (GoreManager.Instance.Stain(events[i].intersection, events[i].normal, Color, breakable != null ? breakable.transform : null)) StainsLeft--;
        }
    }
}

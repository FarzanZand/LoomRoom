using System.Collections.Generic;
using UnityEngine;

// On the blood spray prefab: where a drop hits the world, GoreManager leaves a stain in this spray's colour.
// Drops that hit a creature just end there.
[RequireComponent(typeof(ParticleSystem))]
public class GoreDroplets : MonoBehaviour
{
    public Color Color { get; set; } = Color.clear;
    ParticleSystem ps;
    readonly List<ParticleCollisionEvent> events = new();

    void Awake() => ps = GetComponent<ParticleSystem>();

    void OnParticleCollision(GameObject other)
    {
        if (Color.a <= 0f || !GoreManager.HasInstance || other.GetComponentInParent<Character>() != null) return;
        int count = ps.GetCollisionEvents(other, events);
        for (int i = 0; i < count; i++) GoreManager.Instance.Stain(events[i].intersection, events[i].normal, Color);
    }
}

using System.Collections.Generic;
using UnityEngine;

// Flies straight from the caster's hand and bursts on the first enemy or solid surface.
// Walls between the burst and a target block the blast. Each target is damaged once.
// Lives on Combat/Spells/_Base/Base Projectile; spell projectiles are variants that only add art.
[RequireComponent(typeof(SpellVisual))]
public class SpellProjectile : MonoBehaviour
{
    public LayerMask collisionMask = ~0;
    [Min(.01f)] public float collisionRadius = .12f;
    [Tooltip("The impact is placed this far off the surface, so it isn't half inside the wall.")]
    [Min(0)] public float surfaceOffset = .15f;

    Character owner;
    SpellDefinition spell;
    AdventurerProgress progress;
    SpellVisual visual;
    float power, age;
    bool done;

    // source: whoever cast it (the player, or an enemy caster). skills: the player's progress, null for enemies.
    public void Launch(Character source, SpellDefinition definition, float damage, AdventurerProgress skills)
    {
        owner = source; spell = definition; power = damage; progress = skills;
        visual = GetComponent<SpellVisual>();
        if (visual.loop != null && spell.flightSound != null && AudioManager.HasInstance)
        {
            AudioManager.Instance.PlaySFXLoop(visual.loop, spell.flightSound, spell.flightVolume);
        }
    }

    void Update()
    {
        if (done || spell == null) return;
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        age += Time.deltaTime;
        if (age >= spell.lifetime) { Fizzle(); return; }

        float distance = spell.speed * Time.deltaTime;
        var hits = Physics.SphereCastAll(transform.position, collisionRadius, transform.forward, distance, collisionMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (owner != null && hit.transform.IsChildOf(owner.transform)) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            // A sphere cast that starts overlapping reports a zero point; use the cast origin.
            var point = hit.distance <= 0 ? transform.position : hit.point;
            var normal = hit.distance <= 0 ? -transform.forward : hit.normal;
            Explode(point, normal);
            return;
        }
        transform.position += transform.forward * distance;
    }

    void Explode(Vector3 point, Vector3 normal)
    {
        done = true;
        var centre = point + normal * surfaceOffset;
        bool struck = Blast(owner, centre, spell.radius, power, collisionMask);
        // One practice roll per cast that hurt an enemy, however many it caught.
        if (struck) progress?.Practise(spell.Skill, SkillAction.Cast);
        if (spell.impact != null) Instantiate(spell.impact, centre, Quaternion.LookRotation(normal));
        if (spell.impactSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(spell.impactSound, centre, spell.impactVolume);
        GetComponent<SpellVisual>().Stop();
    }

    // A blast of magic damage: every character hostile to the caster within radius, once each, unless a wall is in the
    // way; crates, barrels and doors take it like any other hit. True if it hurt a character. Shared with GroundAreaSpell.
    public static bool Blast(Character owner, Vector3 centre, float radius, float power, LayerMask mask)
    {
        var seen = new HashSet<Object>();
        bool struck = false;
        foreach (var col in Physics.OverlapSphere(centre, radius, mask, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Character>();
            if (target != null)
            {
                if (target == owner || !target.IsAlive || !seen.Add(target)) continue;
                if (owner != null && !FactionRules.IsHostile(owner.Faction, target.Faction)) continue;
                var aim = col.bounds.center;
                if (Blocked(centre, aim, target.transform, mask)) continue;
                target.Stats.TakeDamage(new DamageInfo { Amount = power, Source = owner, Magic = true, HitPoint = aim, Direction = Flat(aim - centre) });
                struck = true;
                continue;
            }
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable is Component c && seen.Add(c) && !Blocked(centre, col.bounds.center, c.transform, mask))
                damageable.TakeDamage(new DamageInfo { Amount = power, Source = owner, Magic = true, HitPoint = col.bounds.center, Direction = Flat(col.bounds.center - centre) });
        }
        return struck;
    }

    void Fizzle() { done = true; GetComponent<SpellVisual>().Stop(); }

    static bool Blocked(Vector3 from, Vector3 to, Transform target, LayerMask mask) =>
        Physics.Linecast(from, to, out var cover, mask, QueryTriggerInteraction.Ignore) && !cover.transform.IsChildOf(target);

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}


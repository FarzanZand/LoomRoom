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
    [Tooltip("An impact closer than this to the camera (a hit on the player) is shown this far out instead.")]
    [Min(0)] public float nearView = 1.8f;
    [Tooltip("The impact is placed this far off the surface, so it isn't half inside the wall.")]
    [Min(0)] public float surfaceOffset = .15f;

    // A volley (FireballVolley) holds its balls in the air before letting them go: held, it neither moves nor ages.
    public bool Held { get; set; }
    // Seconds from a standstill to full speed once it moves (0: full speed at once).
    public float RampSeconds { get; set; }
    // The one character it may hurt (the Dungeon Master's spells), and the name a death is put down to.
    public Character Only { get; set; }
    // Homing: once it moves it keeps turning toward this character (degrees a second), so it can't be stepped away from.
    public Character HomingTarget { get; set; }
    public float HomingTurn { get; set; } = 540f;
    public string SourceName { get; set; }

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
        if (Held) return;
        age += Time.deltaTime;
        if (age >= spell.lifetime) { Fizzle(); return; }

        if (HomingTarget != null && HomingTarget.IsAlive)
        {
            var aim = HomingTarget.transform.position + Vector3.up * 1.1f - transform.position;
            if (aim.sqrMagnitude > .01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(aim), HomingTurn * Time.deltaTime);
        }
        float ramp = RampSeconds > 0f ? Mathf.Clamp(age / RampSeconds, .05f, 1f) : 1f;
        float distance = spell.speed * ramp * ramp * Time.deltaTime;
        var hits = Physics.SphereCastAll(transform.position, collisionRadius, transform.forward, distance, collisionMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (owner != null && hit.transform.IsChildOf(owner.transform)) continue;
            if (hit.transform.IsChildOf(transform)) continue;
            // A sphere cast that starts overlapping reports a zero point; use the cast origin.
            var point = hit.distance <= 0 ? transform.position : hit.point;
            if (TableManager.HasInstance && TableManager.Instance.DM != null && hit.transform.IsChildOf(TableManager.Instance.DM.transform)) DungeonMasterRemarks.SpellHit(owner);
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
        bool struck = Blast(owner, centre, spell.radius, power, collisionMask, Only, SourceName);
        // One practice roll per cast that hurt an enemy, however many it caught.
        if (struck) progress?.Practise(spell.Skill, SkillAction.Cast);
        if (spell.impact != null)
        {
            // A hit on the player's own face is shown a little way out along the path it came in by: around the camera
            // it would fill the whole view.
            var at = centre;
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null && Vector3.Distance(cam.position, at) < nearView) at = cam.position - transform.forward * nearView;
            Instantiate(spell.impact, at, Quaternion.LookRotation(normal));
        }
        if (spell.impactSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(spell.impactSound, centre, spell.impactVolume);
        Burst();
    }

    // The ball is gone: what travels with it (its glowing core, simulated in its own space) goes at once, or it would
    // hang in the air where it burst as a lingering orb; the trail it left in the world fades out on its own, except
    // right in front of the player's eyes (a hit on them), where its last sparks would pile up into a glowing ball.
    void Burst()
    {
        var cam = Camera.main != null ? Camera.main.transform : null;
        bool inFace = cam != null && Vector3.Distance(cam.position, transform.position) < nearView + 1.2f;
        foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            if (inFace || ps.main.simulationSpace == ParticleSystemSimulationSpace.Local) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        GetComponent<SpellVisual>().Stop();
    }

    // A blast of magic damage: every character hostile to the caster within radius, once each, unless a wall is in the
    // way; crates, barrels and doors take it like any other hit. True if it hurt a character. Shared with GroundAreaSpell.
    // only: hurt that character alone (and breakables); sourceName: what a death is put down to when there is no owner.
    public static bool Blast(Character owner, Vector3 centre, float radius, float power, LayerMask mask, Character only = null, string sourceName = null)
    {
        // A blast with no power is only a look (a teammate's copy of the host's spell): it touches nothing.
        if (power <= 0f) return false;
        var seen = new HashSet<Object>();
        bool struck = false;
        foreach (var col in Physics.OverlapSphere(centre, radius, mask, QueryTriggerInteraction.Ignore))
        {
            var target = col.GetComponentInParent<Character>();
            if (target != null)
            {
                if (target == owner || !target.IsAlive || !seen.Add(target)) continue;
                if (only != null ? target != only : owner != null && !FactionRules.IsHostile(owner.Faction, target.Faction)) continue;
                var aim = col.bounds.center;
                if (Blocked(centre, aim, target.transform, mask)) continue;
                target.Stats.TakeDamage(new DamageInfo { Amount = power, Source = owner, SourceName = sourceName, Magic = true, HitPoint = aim, Direction = Flat(aim - centre) });
                struck = true;
                continue;
            }
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable is Component c && seen.Add(c) && !Blocked(centre, col.bounds.center, c.transform, mask))
                damageable.TakeDamage(new DamageInfo { Amount = power, Source = owner, Magic = true, HitPoint = col.bounds.center, Direction = Flat(col.bounds.center - centre) });
        }
        return struck;
    }

    public void Fizzle() { done = true; Burst(); }

    static bool Blocked(Vector3 from, Vector3 to, Transform target, LayerMask mask) =>
        Physics.Linecast(from, to, out var cover, mask, QueryTriggerInteraction.Ignore) && !cover.transform.IsChildOf(target);

    static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > .0001f ? v.normalized : Vector3.forward; }
}


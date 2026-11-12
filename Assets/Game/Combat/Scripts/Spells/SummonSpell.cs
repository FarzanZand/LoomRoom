using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

[Serializable]
public class SummonSpellSettings
{
    [FoldoutGroup("Summon", Expanded = true), AssetsOnly, Tooltip("Who comes: a character prefab (an enemy's, so it fights). It takes the caster's side and the floor's scaling.")]
    public GameObject prefab;
    [FoldoutGroup("Summon"), Tooltip("The name it goes by, said as it is (\"John hits the Crypt Rat\"). Empty: its prefab's own name.")]
    public string summonName = "John";
    [FoldoutGroup("Summon"), Range(.3f, 2f), Tooltip("Its size against its prefab's (0.8: a fifth shorter). Its pixels shrink or grow with it.")]
    public float size = 1f;
    [FoldoutGroup("Summon"), Min(.5f), Tooltip("Metres in front of the caster where it comes up (nearer when a wall or a drop is in the way).")]
    public float distance = 2.5f;
    [FoldoutGroup("Summon"), Min(0f), Tooltip("Seconds the rune circle shows before it appears.")]
    public float gatherSeconds = .9f;
    [FoldoutGroup("Summon"), Min(0f), Tooltip("Seconds it takes getting up before it fights.")]
    public float riseSeconds = 1.4f;
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("The rune circle on the floor where it comes up.")]
    public SpellVisual circle;
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("The flash as it appears.")]
    public SpellVisual flash;
    [FoldoutGroup("Look")] public AudioClip gatherSound;
    [FoldoutGroup("Look"), Range(0, 1)] public float gatherVolume = .9f;
    [InlineProperty, HideLabel, Tooltip("It is a summon of its caster: how long it stays, how many at once, how it follows.")]
    public SummonSettings summon = new() { maxAtOnce = 1 };
}

// A summoning spell (Summon John): a rune circle opens on the floor in front of the caster and a set character (the
// spell's prefab, under its own name) gets up out of it to fight for the caster as a summon (Summon), lying down hidden
// while the circle burns, then rising as Raise the Fallen's skeletons do. Unlike Raise the Fallen it needs no bones.
public class SummonSpell : MonoBehaviour
{
    public static void Cast(Character caster, SpellDefinition spell, AdventurerProgress progress)
    {
        if (caster == null || spell == null || spell.summon.prefab == null) return;
        var go = new GameObject(spell.name + " (summoning)");
        var summoning = go.AddComponent<SummonSpell>();
        summoning.StartCoroutine(summoning.Run(caster, spell, progress));
    }

    IEnumerator Run(Character caster, SpellDefinition spell, AdventurerProgress progress)
    {
        var s = spell.summon;
        var forward = Facing(caster);
        var ground = Spot(caster, forward, s.distance);
        transform.position = ground;
        SpellVisual circle = s.circle != null ? Instantiate(s.circle, ground + Vector3.up * .03f, Quaternion.identity) : null;
        if (s.gatherSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(s.gatherSound, ground, s.gatherVolume);

        // Put down at once, hidden and lying still, facing the way the caster looks.
        var summon = Summon.Spawn(s.prefab, ground, forward, caster, s.summon, spell.name);
        var body = summon != null ? summon.Body : null;
        if (body == null) { if (circle != null) circle.Stop(); Destroy(gameObject); yield break; }
        if (!string.IsNullOrWhiteSpace(s.summonName)) { body.nameOverride = s.summonName; body.name = s.summonName + " (summoned)"; }
        if (!Mathf.Approximately(s.size, 1f)) body.transform.localScale *= s.size;
        var brain = body.GetComponent<EnemyBrain>();
        var renderers = new List<Renderer>();
        foreach (var r in body.GetComponentsInChildren<Renderer>()) if (r.enabled && !(r is ParticleSystemRenderer)) { r.enabled = false; renderers.Add(r); }
        var colliders = new List<Collider>();
        foreach (var c in body.GetComponentsInChildren<Collider>()) if (c.enabled && !c.isTrigger) { c.enabled = false; colliders.Add(c); }
        // Held still from the first frame, then laid down once its health is set up.
        if (brain != null) brain.enabled = false;
        yield return null;
        if (brain != null && body != null) { brain.enabled = true; brain.Sleep(); }

        for (float t = 0f; t < s.gatherSeconds; t += Time.deltaTime) yield return null;

        if (circle != null) circle.Stop();
        // Gone, or ended while the circle burned (its caster died): it must stay hidden.
        if (body == null || !body.IsAlive) { Destroy(gameObject); yield break; }
        foreach (var r in renderers) if (r != null) r.enabled = true;
        foreach (var c in colliders) if (c != null) c.enabled = true;
        if (s.flash != null) Instantiate(s.flash, ground + Vector3.up * .8f, Quaternion.identity);
        if (brain != null) brain.Wake(s.riseSeconds);
        summon.ShowAura();
        progress?.Practise(spell.Skill, SkillAction.Cast);
        Destroy(gameObject);
    }

    // Where it comes up: the distance ahead on walkable floor the caster can see, with room for a body lying and standing
    // there (nothing solid within half a metre: it came up inside a chest), a little to either side or nearer if not,
    // else at the caster's feet.
    static Vector3 Spot(Character caster, Vector3 forward, float distance)
    {
        var from = caster.transform.position;
        var eye = from + Vector3.up * 1f;
        foreach (float d in new[] { distance, distance * .7f, distance * .45f })
            foreach (float turn in new[] { 0f, -35f, 35f })
            {
                var want = from + Quaternion.Euler(0f, turn, 0f) * forward * d;
                if (!NavMesh.SamplePosition(want, out var hit, 1f, NavMesh.AllAreas)) continue;
                var p = hit.position;
                if (Mathf.Abs(p.y - from.y) > 1f) continue;
                if (Physics.Linecast(eye, p + Vector3.up * 1f, out var blocked, ~0, QueryTriggerInteraction.Ignore) && !blocked.transform.IsChildOf(caster.transform)) continue;
                if (Crowded(p, caster)) continue;
                return p;
            }
        return NavMesh.SamplePosition(from, out var under, 2f, NavMesh.AllAreas) ? under.position : GroundAreaSpell.GroundBelow(from);
    }

    // Anything solid but the floor (and the caster) within half a metre of the spot, from knee height up.
    static bool Crowded(Vector3 p, Character caster)
    {
        foreach (var c in Physics.OverlapCapsule(p + Vector3.up * .55f, p + Vector3.up * 1.4f, .5f, ~0, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(caster.transform)) return true;
        return false;
    }

    // A player where they look, anyone else where its body points.
    static Vector3 Facing(Character c)
    {
        var f = c is Player p && p.Look != null && p.Look.YawTransform != null ? p.Look.YawTransform.forward : c.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > .001f ? f.normalized : Vector3.forward;
    }
}

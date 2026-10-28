using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class RaiseSettings
{
    [FoldoutGroup("Raise the fallen", Expanded = true), Min(1f), Tooltip("Furthest from the caster a heap of bones may lie.")]
    public float reach = 10f;
    [FoldoutGroup("Raise the fallen"), AssetsOnly, Tooltip("The skeleton that stands up out of the bones.")]
    public GameObject raiseAs;
    [FoldoutGroup("Raise the fallen"), Min(.2f), Tooltip("Seconds the bones take to fly back together.")]
    public float gatherSeconds = 1.3f;
    [FoldoutGroup("Raise the fallen"), Min(0f), Tooltip("Seconds it takes getting up before it fights.")]
    public float riseSeconds = 1.4f;
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("The rune circle under the bones while they gather.")]
    public SpellVisual circle;
    [FoldoutGroup("Look"), AssetsOnly, Tooltip("The flash as the bones close into a body.")]
    public SpellVisual flash;
    [FoldoutGroup("Look")] public AudioClip gatherSound;
    [FoldoutGroup("Look"), Range(0, 1)] public float gatherVolume = .9f;
    [InlineProperty, HideLabel, Tooltip("The risen skeleton is a summon of its caster: how long it stays, how many at once, how it follows.")]
    public SummonSettings summon = new();
}

// Raise the Fallen: the bones of a burst skeleton (a BoneHeap) fly back together over a rune circle into a body, which
// gets up and fights for its caster as a summon (Summon): on the player's side for the player, on a necromancer's for
// the enemies, collapsing into bones again when its time is up or its caster dies. The body is the spell's Raise As
// prefab, set down where the bones lie.
public class RaiseTheFallen : MonoBehaviour
{
    public static void Cast(Character caster, SpellDefinition spell, BoneHeap heap, AdventurerProgress progress)
    {
        if (heap == null || spell.raise.raiseAs == null) return;
        heap.Claim();
        var go = new GameObject(spell.name + " (raising)");
        var raise = go.AddComponent<RaiseTheFallen>();
        raise.StartCoroutine(raise.Run(caster, spell, heap, progress));
    }

    IEnumerator Run(Character caster, SpellDefinition spell, BoneHeap heap, AdventurerProgress progress)
    {
        var s = spell.raise;
        var centre = heap.Centre ?? caster.transform.position;
        var ground = GroundAreaSpell.GroundBelow(centre);
        transform.position = ground;
        SpellVisual circle = s.circle != null ? Instantiate(s.circle, ground + Vector3.up * .03f, Quaternion.identity) : null;
        if (s.gatherSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX(s.gatherSound, ground, s.gatherVolume);

        // The body is put down at once, hidden and lying still, so the bones have somewhere to fly to.
        var face = caster is Player ? caster.transform.forward : caster.transform.position - ground;
        var summon = Summon.Spawn(s.raiseAs, ground, face, caster, s.summon, spell.name, heap.parent);
        var body = summon != null ? summon.Body : null;
        if (body == null) { if (circle != null) circle.Stop(); Destroy(gameObject); yield break; }
        var brain = body.GetComponent<EnemyBrain>();
        var renderers = new List<Renderer>();
        foreach (var r in body.GetComponentsInChildren<Renderer>()) if (r.enabled && !(r is ParticleSystemRenderer)) { r.enabled = false; renderers.Add(r); }
        var colliders = new List<Collider>();
        foreach (var c in body.GetComponentsInChildren<Collider>()) if (c.enabled && !c.isTrigger) { c.enabled = false; colliders.Add(c); }
        // Held still from the first frame (it would look about before it lay down), then laid down once its health is set up.
        if (brain != null) brain.enabled = false;
        yield return null;
        if (brain != null && body != null) { brain.enabled = true; brain.Sleep(); }

        // Each piece flies from where it lies to its bone in the waiting body, slowly at first, faster as it closes.
        var bones = new Dictionary<string, Transform>();
        if (body != null) foreach (var t in body.GetComponentsInChildren<Transform>()) bones.TryAdd(t.name, t);
        var chest = body != null && body.Animator != null && body.Animator.isHuman ? body.Animator.GetBoneTransform(HumanBodyBones.Chest) : null;
        var moving = new List<(Transform t, Vector3 from, Quaternion fromRot, Transform to, float delay)>();
        foreach (var p in heap.pieces)
        {
            if (p.transform == null) continue;
            if (p.transform.TryGetComponent<Rigidbody>(out var rb)) { rb.isKinematic = true; rb.detectCollisions = false; }
            Transform to = p.bone != null && bones.TryGetValue(p.bone, out var bone) ? bone : chest != null ? chest : body.transform;
            moving.Add((p.transform, p.transform.position, p.transform.rotation, to, UnityEngine.Random.Range(0f, .25f)));
        }
        for (float t = 0f; t < s.gatherSeconds; t += Time.deltaTime)
        {
            foreach (var m in moving)
            {
                if (m.t == null) continue;
                float u = Mathf.Clamp01((t / s.gatherSeconds - m.delay) / (1f - m.delay));
                float e = u * u * u;
                var target = m.to != null ? m.to.position : ground;
                var p = Vector3.Lerp(m.from, target, e);
                p.y += Mathf.Sin(u * Mathf.PI) * .35f;   // lifted off the floor as it goes
                m.t.position = p;
                m.t.rotation = Quaternion.Slerp(m.fromRot, Quaternion.identity, e);
            }
            yield return null;
        }

        // Together: the bones become the body, which gets up.
        foreach (var m in moving) if (m.t != null) Destroy(m.t.gameObject);
        if (circle != null) circle.Stop();
        if (body == null) { Destroy(gameObject); yield break; }
        foreach (var r in renderers) if (r != null) r.enabled = true;
        foreach (var c in colliders) if (c != null) c.enabled = true;
        if (s.flash != null) Instantiate(s.flash, ground + Vector3.up * .8f, Quaternion.identity);
        if (brain != null) brain.Wake(s.riseSeconds);
        summon.ShowAura();
        progress?.Practise(spell.Skill, SkillAction.Cast);
        Destroy(gameObject);
    }
}

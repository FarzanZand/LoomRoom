using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Barony-style floppy deaths. While alive nothing exists: the ragdoll is built from the
// humanoid skeleton at the moment of death, so it never interferes with animation,
// hitboxes or navigation. Non-humanoid bodies (the mite) tumble as one rigid body.
// CombatManager.ragdollDeath switches between this and the authored death animation.
[RequireComponent(typeof(Character))]
public class EnemyRagdoll : MonoBehaviour
{
    [SerializeField, Min(.1f)] float totalMass = 40f;
    [Tooltip("Seconds after death before the bodies freeze in place (saves physics time).")]
    [SerializeField, Min(.5f)] float settleSeconds = 5f;

    Character character;
    DamageInfo lastHit;
    readonly List<Rigidbody> bodies = new();
    public bool IsRagdolled { get; private set; }
    // Where the body actually lies once it has fallen (for loot and interaction).
    public Transform BodyCenter { get; private set; }

    void Awake()
    {
        character = GetComponent<Character>();
        BodyCenter = transform;
    }

    void OnEnable()
    {
        character.Damaged += Remember;
        character.Died += OnDied;
    }

    void OnDisable()
    {
        character.Damaged -= Remember;
        character.Died -= OnDied;
    }

    void Remember(DamageInfo info) => lastHit = info;

    void OnDied()
    {
        if (!CombatManager.HasInstance || !CombatManager.Instance.ragdollDeath || IsRagdolled) return;
        IsRagdolled = true;
        var anim = character.Animator;
        if (anim != null && anim.isHuman) BuildHumanoid(anim);
        else BuildRigid();

        // Anything still posing the skeleton would fight the physics.
        if (anim != null) anim.enabled = false;
        foreach (var hr in GetComponentsInChildren<HitReactionController>()) hr.enabled = false;
        foreach (var mite in GetComponentsInChildren<CryptMiteAnimation>()) mite.enabled = false;

        IgnorePlayers();
        var cm = CombatManager.Instance;
        Vector3 dir = lastHit.Direction.sqrMagnitude > .001f ? lastHit.Direction : -transform.forward;
        Vector3 impulse = dir * cm.ragdollImpulse * (lastHit.Heavy ? 1.5f : 1f) + Vector3.up * cm.ragdollUpwardImpulse;
        foreach (var rb in bodies)
            rb.AddForce(impulse * (rb.mass / Mathf.Max(.01f, totalMass)) * bodies.Count * .5f, ForceMode.Impulse);
        if (bodies.Count == 1) bodies[0].AddTorque(Random.onUnitSphere * 2f, ForceMode.Impulse);
        StartCoroutine(Settle());
    }

    IEnumerator Settle()
    {
        yield return new WaitForSeconds(settleSeconds);
        foreach (var rb in bodies) if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.isKinematic = true; }
    }

    void IgnorePlayers()
    {
        if (!PlayerManager.HasInstance) return;
        foreach (var context in PlayerManager.Instance.players)
        {
            if (context?.player == null) continue;
            foreach (var pc in context.player.GetComponentsInChildren<Collider>(true))
                foreach (var rb in bodies)
                    foreach (var c in rb.GetComponents<Collider>())
                        Physics.IgnoreCollision(pc, c, true);
        }
    }

    // ── Humanoid ──────────────────────────────────────────────────────

    void BuildHumanoid(Animator anim)
    {
        Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);
        var hips = B(HumanBodyBones.Hips);
        var spine = B(HumanBodyBones.Chest);
        if (spine == null) spine = B(HumanBodyBones.Spine);
        var head = B(HumanBodyBones.Head);
        if (hips == null || spine == null || head == null) { BuildRigid(); return; }
        BodyCenter = hips;

        float scale = transform.lossyScale.y;
        var hipsRb = Part(hips, null, .16f, BoxAround(hips, spine, .28f * scale));
        var spineRb = Part(spine, hipsRb, .2f, BoxAround(spine, head, .3f * scale));
        var headRb = Part(head, spineRb, .06f, null, .12f * scale);
        Limb(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), spineRb, .05f * scale);
        Limb(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), spineRb, .05f * scale);
        Limb(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), hipsRb, .07f * scale);
        Limb(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), hipsRb, .07f * scale);
    }

    void Limb(Transform upper, Transform lower, Transform end, Rigidbody parent, float radius)
    {
        if (upper == null || lower == null) return;
        var upperRb = Part(upper, parent, .08f, null, radius, lower);
        Part(lower, upperRb, .06f, null, radius * .85f, end);
    }

    // A bone body. Collider is a box (torso) or a capsule toward `toward` (limbs), or a sphere (head).
    Rigidbody Part(Transform bone, Rigidbody parent, float massFraction, Bounds? box, float radius = .1f, Transform toward = null)
    {
        var rb = bone.gameObject.AddComponent<Rigidbody>();
        rb.mass = totalMass * massFraction;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        if (box.HasValue)
        {
            var c = bone.gameObject.AddComponent<BoxCollider>();
            c.center = box.Value.center;
            c.size = box.Value.size;
        }
        else if (toward != null)
        {
            var c = bone.gameObject.AddComponent<CapsuleCollider>();
            Vector3 local = bone.InverseTransformPoint(toward.position);
            float length = local.magnitude;
            Vector3 axis = local / Mathf.Max(length, .0001f);
            Vector3 abs = new(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
            c.direction = abs.x > abs.y ? (abs.x > abs.z ? 0 : 2) : (abs.y > abs.z ? 1 : 2);
            float lossy = Mathf.Max(.0001f, bone.lossyScale.x);
            c.radius = radius / lossy;
            c.height = Mathf.Max(length, c.radius * 2f);
            c.center = local * .5f;
        }
        else
        {
            var c = bone.gameObject.AddComponent<SphereCollider>();
            c.radius = radius / Mathf.Max(.0001f, bone.lossyScale.x);
        }
        if (parent != null)
        {
            var joint = bone.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parent;
            joint.enablePreprocessing = false;
            joint.enableProjection = true;
            joint.lowTwistLimit = new SoftJointLimit { limit = -35f };
            joint.highTwistLimit = new SoftJointLimit { limit = 35f };
            joint.swing1Limit = new SoftJointLimit { limit = 40f };
            joint.swing2Limit = new SoftJointLimit { limit = 25f };
        }
        bodies.Add(rb);
        return rb;
    }

    // Local-space box from a bone to its child bone, with the given world thickness.
    static Bounds BoxAround(Transform from, Transform to, float thickness)
    {
        Vector3 local = from.InverseTransformPoint(to.position);
        float lossy = Mathf.Max(.0001f, from.lossyScale.x);
        var size = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
        float t = thickness / lossy;
        size = new Vector3(Mathf.Max(size.x, t), Mathf.Max(size.y, t), Mathf.Max(size.z, t));
        return new Bounds(local * .5f, size);
    }

    // ── Rigid body fallback ───────────────────────────────────────────

    void BuildRigid()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position + Vector3.up * .3f, Vector3.one * .6f);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(bounds.center);
        var s = transform.lossyScale;
        box.size = new Vector3(bounds.size.x / Mathf.Max(.0001f, s.x), bounds.size.y / Mathf.Max(.0001f, s.y), bounds.size.z / Mathf.Max(.0001f, s.z)) * .9f;
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.mass = totalMass * .3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        bodies.Add(rb);
    }
}

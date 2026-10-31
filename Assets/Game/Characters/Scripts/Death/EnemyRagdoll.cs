using System.Collections.Generic;
using UnityEngine;

// Floppy deaths. While alive nothing exists: the ragdoll is built from the
// humanoid skeleton at the moment of death, so it never interferes with animation,
// hitboxes or navigation. Non-humanoid bodies tumble as one rigid body,
// or play their death clip (deathAnimationInstead).
// CombatManager.ragdollDeath switches between this and the authored death animation.
[RequireComponent(typeof(Character))]
public class EnemyRagdoll : MonoBehaviour
{
    [SerializeField, Min(.1f)] float totalMass = 40f;
    [Tooltip("Seconds after death before the bodies freeze in place (saves physics time).")]
    [SerializeField, Min(.5f)] float settleSeconds = 5f;
    [Tooltip("Play the authored death animation instead of tumbling. For non-humanoid bodies that one rigid box fits badly: long animals like the rat, and flyers like the bat, whose clip falls to the floor (a thrown box built around the body in the air turns that fall sideways or upward).")]
    [SerializeField] bool deathAnimationInstead;
    [Tooltip("Non-humanoid bodies: keep the animator playing the death clip while the body is thrown, so it curls up and rolls over instead of freezing in the pose it died in.")]
    [SerializeField] bool deathClipWhileThrown = true;
    [Tooltip("How far this body is thrown against CombatManager's ragdoll impulse (small creatures slide far as one rigid body).")]
    [SerializeField, Range(0f, 2f)] float throwScale = 1f;

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
        // A skeleton that bursts into bones has no body left to tumble.
        if (TryGetComponent<DeathBurst>(out var burst) && burst.enabled) return;
        if (deathAnimationInstead && character.Animator != null && character.Animator.runtimeAnimatorController != null) return;
        IsRagdolled = true;
        var anim = character.Animator;
        bool thrownWithClip = deathClipWhileThrown && anim != null && !anim.isHuman && anim.runtimeAnimatorController != null;
        if (anim != null && anim.isHuman) BuildHumanoid(anim);
        else BuildRigid(thrownWithClip ? DeathClip(anim) : null);

        if (thrownWithClip) foreach (var hr in GetComponentsInChildren<HitReactionController>()) hr.enabled = false;
        else DeathPhysics.StopPosing(this, anim);
        DeathPhysics.IgnorePlayers(bodies);
        var cm = CombatManager.Instance;
        Vector3 dir = DeathPhysics.HitDirection(lastHit, transform);
        // The whole body leaves the ground together, thrown metres along the killing blow.
        Vector3 launch = (dir * cm.ragdollImpulse * (lastHit.Heavy || lastHit.Critical ? 1.4f : 1f) + Vector3.up * cm.ragdollUpwardImpulse) * throwScale;
        foreach (var rb in bodies)
            rb.linearVelocity = launch + Random.insideUnitSphere * .4f;
        if (bodies.Count == 1) bodies[0].AddTorque(Random.onUnitSphere * 2f, ForceMode.Impulse);
        StartCoroutine(DeathPhysics.Settle(bodies, settleSeconds));
        // The death clip has long finished by the time the body settles: the corpse needn't animate any more.
        if (thrownWithClip) StartCoroutine(StopAnimating(anim, settleSeconds));
    }

    static System.Collections.IEnumerator StopAnimating(Animator anim, float after)
    {
        yield return new WaitForSeconds(after);
        if (anim != null) anim.enabled = false;
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

    // The controller's death clip (by name), for bodies thrown while it plays.
    static AnimationClip DeathClip(Animator anim)
    {
        foreach (var c in anim.runtimeAnimatorController.animationClips)
            if (c != null && c.name.IndexOf("death", System.StringComparison.OrdinalIgnoreCase) >= 0) return c;
        return null;
    }

    void BuildRigid(AnimationClip lyingClip = null)
    {
        // The box fits the body as it is posed (the skinned mesh baked), in the root's space. A skinned renderer's
        // own bounds are loose (a rat's reached 0.24 m below its feet), and a box that big held dead bodies up in the air.
        // A body thrown while its death clip plays gets the box of the clip's last pose, lying down: a box of the pose it
        // died in stood the curled-up rat on its tail or its legs.
        if (lyingClip != null) lyingClip.SampleAnimation(gameObject, lyingClip.length);
        bool any = false; var bounds = new Bounds(Vector3.up * .3f, Vector3.one * .6f);
        var toRoot = transform.worldToLocalMatrix; var baked = new Mesh();
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = null; Matrix4x4 m;
            if (r is SkinnedMeshRenderer sk && sk.sharedMesh != null)
            {
                sk.BakeMesh(baked, true); mesh = baked;
                m = toRoot * Matrix4x4.TRS(sk.transform.position, sk.transform.rotation, Vector3.one);
            }
            else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null && mf.sharedMesh.isReadable)
            {
                mesh = mf.sharedMesh; m = toRoot * r.transform.localToWorldMatrix;
            }
            else continue;
            foreach (var v in mesh.vertices)
            {
                var p = m.MultiplyPoint3x4(v);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p);
            }
        }
        Destroy(baked);
        // Its walking colliders give way to the box (EnemyBrain does it too, whichever of them hears the death first).
        foreach (var c in GetComponents<Collider>()) c.enabled = false;
        var box = gameObject.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size * .9f;
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.mass = totalMass * .3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        bodies.Add(rb);
    }
}

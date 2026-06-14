using System.Collections.Generic;
using UnityEngine;

// Runs after the Animator so rotation offsets are applied on top of the current pose.
[DefaultExecutionOrder(100)]
public class HitReactionController : MonoBehaviour
{
    [SerializeField] Animator animator;

    [Header("Reaction Fallback")]
    [Tooltip("Used only when CombatManager is not present in the scene.")]
    [SerializeField] float reactionAngle = 20f;
    [SerializeField] float damping = 8f;
    [SerializeField] int influenceDepth = 3;
    [SerializeField, Range(0f, 1f)] float parentFalloff = 0.45f;

    [SerializeField] float attackSpeed = 20f;
    float ReactionAngle  => CombatManager.Instance != null ? CombatManager.Instance.hitReactionAngle         : reactionAngle;
    float Damping        => CombatManager.Instance != null ? CombatManager.Instance.hitReactionDamping        : damping;
    float AttackSpeed    => CombatManager.Instance != null ? CombatManager.Instance.hitReactionAttackSpeed    : attackSpeed;
    int   InfluenceDepth => CombatManager.Instance != null ? CombatManager.Instance.hitReactionInfluenceDepth : influenceDepth;
    float ParentFalloff  => CombatManager.Instance != null ? CombatManager.Instance.hitReactionParentFalloff  : parentFalloff;

    struct BoneReaction
    {
        public Transform bone;
        public Quaternion current;  // what is actually applied this frame
        public Quaternion target;   // peak we are moving toward
    }

    Transform[] bones;
    readonly List<BoneReaction> reactions = new List<BoneReaction>();

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        CacheBones();
    }

    void CacheBones()
    {
        if (animator == null) return;

        var list = new List<Transform>();
        if (animator.isHuman)
        {
            foreach (HumanBodyBones b in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                var t = animator.GetBoneTransform(b);
                if (t != null && !list.Contains(t)) list.Add(t);
            }
        }
        else
        {
            list.AddRange(GetComponentsInChildren<Transform>());
        }
        bones = list.ToArray();
    }

    public void ReactToHit(Vector3 contactPoint, Vector3 hitDirection)
    {
        if (CombatManager.Instance != null && !CombatManager.Instance.hitReactionEnabled) return;
        if (bones == null || bones.Length == 0) return;

        Transform closest = FindClosestBone(contactPoint);
        if (closest == null) return;

        Transform current = closest;
        float strength = 1f;
        for (int i = 0; i < InfluenceDepth && current != null; i++)
        {
            Vector3 axis = Vector3.Cross(Vector3.up, hitDirection);
            if (axis.sqrMagnitude < 0.001f) axis = current.right;

            Quaternion offset = Quaternion.AngleAxis(ReactionAngle * strength, axis);
            AddOrAccumulate(current, offset);

            current = current.parent;
            strength *= ParentFalloff;
        }
    }

    void AddOrAccumulate(Transform bone, Quaternion target)
    {
        for (int i = 0; i < reactions.Count; i++)
        {
            if (reactions[i].bone == bone)
            {
                var r = reactions[i];
                r.target = target * r.target;
                reactions[i] = r;
                return;
            }
        }
        reactions.Add(new BoneReaction { bone = bone, current = Quaternion.identity, target = target });
    }

    Transform FindClosestBone(Vector3 point)
    {
        Transform closest = null;
        float minSq = float.MaxValue;
        foreach (var b in bones)
        {
            if (b == null) continue;
            float sq = (b.position - point).sqrMagnitude;
            if (sq < minSq) { minSq = sq; closest = b; }
        }
        return closest;
    }

    void LateUpdate()
    {
        float attackStep = Time.deltaTime * AttackSpeed;
        float dampStep   = Time.deltaTime * Damping;

        for (int i = reactions.Count - 1; i >= 0; i--)
        {
            var r = reactions[i];

            // Move current toward target (attack phase), then decay target back to identity
            r.current = Quaternion.Slerp(r.current, r.target,   attackStep);
            r.target  = Quaternion.Slerp(r.target,  Quaternion.identity, dampStep);

            r.bone.rotation = r.current * r.bone.rotation;
            reactions[i] = r;

            if (Quaternion.Angle(r.current, Quaternion.identity) < 0.5f &&
                Quaternion.Angle(r.target,  Quaternion.identity) < 0.5f)
                reactions.RemoveAt(i);
        }
    }
}

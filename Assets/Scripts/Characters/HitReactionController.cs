using System.Collections.Generic;
using UnityEngine;

// Small, bounded bone offsets layered over the Animator. Never moves the character root,
// changes Animator state, disables hitboxes, or pauses the attack/AI.
[DefaultExecutionOrder(100)]
public class HitReactionController : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] float reactionAngle = 20f;
    [SerializeField] float damping = 9f;
    [SerializeField] int influenceDepth = 2;
    [SerializeField, Range(0f, 1f)] float parentFalloff = .35f;
    [SerializeField] float attackSpeed = 60f;
    Character character;
    readonly HashSet<Transform> bones = new();
    readonly List<Reaction> reactions = new();
    CombatManager Tuning => CombatManager.HasInstance ? CombatManager.Instance : null;

    class Reaction
    {
        public Transform bone;
        public Quaternion current = Quaternion.identity, target = Quaternion.identity;
        public Quaternion baseline, applied;
        public bool ownsPose;
    }

    void Awake()
    {
        character = GetComponentInParent<Character>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        // Exclude hips/root, fingers and helper transforms so recoil cannot propel or twist the actor.
        foreach (var id in new[]{HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot})
        {
            var bone = animator.GetBoneTransform(id);
            if (bone != null) bones.Add(bone);
        }
    }
    void OnEnable()
    {
        if (character != null) { character.Damaged += OnDamaged; character.Died += Clear; }
    }
    void OnDisable()
    {
        if (character != null) { character.Damaged -= OnDamaged; character.Died -= Clear; }
        Clear();
    }
    void OnDamaged(DamageInfo hit)
    {
        if (character.IsAlive && !hit.Blocked && hit.Amount > 0) ReactToHit(hit.HitPoint, hit.Direction);
    }
    public void ReactToHit(Vector3 point, Vector3 direction)
    {
        if (!isActiveAndEnabled || (Tuning != null && !Tuning.hitReactionEnabled) || direction.sqrMagnitude < .0001f) return;
        Transform closest = null;float distance = float.MaxValue;
        foreach (var bone in bones)
        {
            if (bone == null) continue;
            float candidate = (point-bone.position).sqrMagnitude;
            if (candidate < distance) { distance=candidate;closest=bone; }
        }
        float strength=1;
        int depth=Tuning != null ? Tuning.hitReactionInfluenceDepth : influenceDepth;
        for (int i=0;i<Mathf.Clamp(depth,1,4) && closest!=null && bones.Contains(closest);i++)
        {
            var pivot=closest.parent!=null ? closest.parent.position : closest.position;
            var axis=Vector3.Cross(point-pivot,direction.normalized);
            if (axis.sqrMagnitude < .0001f) axis=Vector3.Cross(Vector3.up,direction);
            if (axis.sqrMagnitude < .0001f) axis=closest.right;
            float angle=Mathf.Clamp(Tuning != null ? Tuning.hitReactionAngle : reactionAngle,0,30)*strength;
            var reaction=reactions.Find(r=>r.bone==closest);
            if (reaction==null) { reaction=new Reaction{bone=closest};reactions.Add(reaction); }
            reaction.target=Quaternion.RotateTowards(Quaternion.identity,Quaternion.AngleAxis(angle,axis.normalized)*reaction.target,angle);
            strength*=Mathf.Clamp01(Tuning != null ? Tuning.hitReactionParentFalloff : parentFalloff);
            closest=closest.parent;
        }
    }
    static void Restore(Reaction r)
    {
        // Restore only our own last pose; never overwrite a newly evaluated Animator pose.
        if (r.bone!=null && r.ownsPose && Quaternion.Angle(r.bone.localRotation,r.applied)<.01f)
            r.bone.localRotation=r.baseline;
        r.ownsPose=false;
    }
    void Update() { foreach (var reaction in reactions) Restore(reaction); }
    void Clear() { foreach (var reaction in reactions) Restore(reaction);reactions.Clear(); }
    void LateUpdate()
    {
        if (character!=null && !character.IsAlive) { Clear();return; }
        float dt=Time.deltaTime;
        float attack=1-Mathf.Exp(-Mathf.Max(0,Tuning!=null ? Tuning.hitReactionAttackSpeed : attackSpeed)*dt);
        float decay=1-Mathf.Exp(-Mathf.Max(0,Tuning!=null ? Tuning.hitReactionDamping : damping)*dt);
        for (int i=reactions.Count-1;i>=0;i--)
        {
            var r=reactions[i];Restore(r);
            if (r.bone==null) { reactions.RemoveAt(i);continue; }
            r.current=Quaternion.Slerp(r.current,r.target,attack);
            r.target=Quaternion.Slerp(r.target,Quaternion.identity,decay);
            if (Quaternion.Angle(r.current,Quaternion.identity)<.05f && Quaternion.Angle(r.target,Quaternion.identity)<.05f)
            { reactions.RemoveAt(i);continue; }
            r.baseline=r.bone.localRotation;
            var parent=r.bone.parent!=null ? r.bone.parent.rotation : Quaternion.identity;
            r.applied=Quaternion.Inverse(parent)*r.current*parent*r.baseline;
            r.bone.localRotation=r.applied;r.ownsPose=true;
        }
    }
}

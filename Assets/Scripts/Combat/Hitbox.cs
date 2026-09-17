using System;
using System.Collections.Generic;
using UnityEngine;

// What a swing does when it connects. Items author one of these for weapons;
// enemy attacks build one from their EnemyAttack entry.
[Serializable]
public class HitProfile
{
    [Tooltip("Use CombatManager's default hit/swing audio and particle pools instead of the ones below.")]
    public bool       useDefaultEffects = true;
    public AudioData  hitAudio;
    public AudioData  swingAudio;
    public GameObject hitParticle;
    [Tooltip("Multiplier on the attacker's AttackDamage stat.")]
    public float      damageMultiplier = 1f;
    [Tooltip("Knockback force. Negative = use the attacker's CharacterFX default.")]
    public float      knockbackForce = -1f;
    [Tooltip("Hit stop length multiplier. 0 = no hit stop for this attack.")]
    public float      hitStopScale = 1f;

    public static HitProfile Default => new HitProfile();
}

// A damage volume driven by animation events. Works for the player's weapon and for
// enemy limbs alike: the owner is whichever Character is above it in the hierarchy.
public class Hitbox : MonoBehaviour
{
    [Tooltip("The collider that defines the hit shape. Capsule, sphere or box. Can be on any child object.")]
    [SerializeField] Collider weaponCollider;
    [SerializeField] LayerMask hitMask = ~0;
    [Tooltip("Optional id so an animation event can enable a specific hitbox (EnableHitboxAt).")]
    [SerializeField] int hitboxIndex = 0;

    public int  HitboxIndex => hitboxIndex;
    public bool Active      { get; private set; }

    HitProfile profile = HitProfile.Default;
    Character  owner;
    readonly HashSet<IDamageable> hitThisSwing = new();
    readonly Collider[] overlapBuffer = new Collider[64];

    Vector3 previousPosition;
    Quaternion previousRotation;
    Vector3 queryCenter;
    bool heavy;

    public void SetProfile(HitProfile p) => profile = p ?? HitProfile.Default;
    public void ClearProfile()           => profile = HitProfile.Default;

    void Awake()
    {
        if (weaponCollider == null) weaponCollider = GetComponent<Collider>();
        if (weaponCollider != null) weaponCollider.enabled = false; // overlap queries only
    }

    public void PlaySwingAudio()
    {
        if (!AudioManager.HasInstance) return;
        AudioData audio = profile.useDefaultEffects
            ? (CombatManager.HasInstance ? CombatManager.Instance.defaultSwingAudio : null)
            : profile.swingAudio;
        if (audio != null) AudioManager.Instance.PlaySFXData(audio, transform.position);
    }

    public void EnableHitbox()
    {
        if (Active) return;
        hitThisSwing.Clear();
        owner  = GetComponentInParent<Character>();
        Active = true;
        heavy=owner is Player p && p.Combat != null && p.Combat.HeavySwing;
        if(weaponCollider != null) { previousPosition=weaponCollider.transform.position; previousRotation=weaponCollider.transform.rotation; }
    }

    public void DisableHitbox()
    {
        Active = false;
        hitThisSwing.Clear();
    }

    void OnDisable() => DisableHitbox();
    void LateUpdate()
    {
        if (!Active || weaponCollider == null || owner == null || !owner.IsAlive) return;
        if(GameManager.HasInstance && !GameManager.Instance.GameplayActive) { DisableHitbox(); return; }
        var t=weaponCollider.transform;
        int steps=CombatManager.HasInstance ? Mathf.Clamp(CombatManager.Instance.weaponSweepSteps,1,16) : 6;
        for(int step=1;step<=steps;step++)
        {
            float f=(float)step/steps;
            var rotation=Quaternion.Slerp(previousRotation,t.rotation,f);
            var matrix=Matrix4x4.TRS(Vector3.Lerp(previousPosition,t.position,f),rotation,t.lossyScale);
            int count=Overlap(matrix,rotation);
            for(int i=0;i<count;i++) ProcessHit(overlapBuffer[i]);
        }
        previousPosition=t.position; previousRotation=t.rotation;
    }

    int Overlap(Matrix4x4 matrix,Quaternion rotation)
    {
        var scale=weaponCollider.transform.lossyScale;
        scale=new Vector3(Mathf.Abs(scale.x),Mathf.Abs(scale.y),Mathf.Abs(scale.z));
        float largest=Mathf.Max(scale.x,scale.y,scale.z);
        switch(weaponCollider)
        {
            case CapsuleCollider cap:
                queryCenter=matrix.MultiplyPoint3x4(cap.center);
                Vector3 localAxis=cap.direction==0 ? Vector3.right : cap.direction==1 ? Vector3.up : Vector3.forward;
                float radius=cap.radius*largest;
                float extent=Mathf.Max(0,cap.height*scale[cap.direction]*.5f-radius);
                Vector3 axis=rotation*localAxis*extent;
                return Physics.OverlapCapsuleNonAlloc(queryCenter+axis,queryCenter-axis,radius,overlapBuffer,hitMask,QueryTriggerInteraction.Ignore);
            case SphereCollider sphere:
                queryCenter=matrix.MultiplyPoint3x4(sphere.center);
                return Physics.OverlapSphereNonAlloc(queryCenter,sphere.radius*largest,overlapBuffer,hitMask,QueryTriggerInteraction.Ignore);
            case BoxCollider box:
                queryCenter=matrix.MultiplyPoint3x4(box.center);
                return Physics.OverlapBoxNonAlloc(queryCenter,Vector3.Scale(box.size,scale)*.5f,overlapBuffer,rotation,hitMask,QueryTriggerInteraction.Ignore);
            default: return 0;
        }
    }

    void ProcessHit(Collider other)
    {
        if (!Active || owner == null || !owner.IsAlive) return;
        if (owner != null && other.transform.IsChildOf(owner.transform)) return;
        var damageable = other.GetComponentInParent<IDamageable>();
        if (damageable == null || hitThisSwing.Contains(damageable)) return;

        var target = other.GetComponentInParent<Character>();
        if (target != null)
        {
            if (!target.IsAlive) return;
            if (owner != null && !FactionRules.IsHostile(owner.Faction, target.Faction)) return;
        }

        Vector3 contact = other.ClosestPoint(queryCenter);

        Vector3 attackerPos = owner != null ? owner.transform.position : transform.position;
        Vector3 targetPos   = target != null ? target.transform.position : other.transform.position;
        Vector3 dir = targetPos - attackerPos;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = transform.root.forward;
        dir.Normalize();

        float baseDamage = owner != null && owner.Stats != null ? owner.Stats.GetFinal(StatType.AttackDamage) : 0f;
        float force = profile.knockbackForce >= 0f
            ? profile.knockbackForce
            : (owner != null && owner.FX != null ? owner.FX.KnockbackForce : 0f);
        if (CombatManager.HasInstance) force = CombatManager.Instance.ScaleKnockback(force);

        if(heavy && CombatManager.HasInstance)
        { baseDamage*=CombatManager.Instance.heavyDamageMultiplier; force*=CombatManager.Instance.heavyKnockbackMultiplier; }
        var info = new DamageInfo
        {
            Profile        = profile,
            Amount         = baseDamage * profile.damageMultiplier,
            Source         = owner,
            HitPoint       = contact,
            Direction      = dir,
            KnockbackForce = force,
        };

        if(CombatManager.HasInstance && !CombatManager.Instance.HasMeleeLineOfSight(owner,target,contact))return;
        hitThisSwing.Add(damageable);
        damageable.TakeDamage(info);
        // A heavy hit stops the world longer. The request only extends the stop the impact already started.
        if (heavy && CombatManager.HasInstance) CombatManager.Instance.RequestHitStop(CombatManager.Instance.heavyHitStopScale);
    }
}

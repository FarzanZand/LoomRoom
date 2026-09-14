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
    readonly HashSet<Collider> hitThisSwing = new();
    readonly Collider[] overlapBuffer = new Collider[16];

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
        hitThisSwing.Clear();
        owner  = GetComponentInParent<Character>();
        Active = true;
    }

    public void DisableHitbox()
    {
        Active = false;
        hitThisSwing.Clear();
    }

    void Update()
    {
        if (!Active || weaponCollider == null) return;

        int count = Overlap();
        for (int i = 0; i < count; i++)
            ProcessHit(overlapBuffer[i]);
    }

    int Overlap()
    {
        switch (weaponCollider)
        {
            case CapsuleCollider cap:
                GetCapsulePoints(cap, out var p1, out var p2, out var r);
                return Physics.OverlapCapsuleNonAlloc(p1, p2, r, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            case SphereCollider sph:
            {
                var t = sph.transform;
                float scale = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
                return Physics.OverlapSphereNonAlloc(t.TransformPoint(sph.center), sph.radius * scale, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);
            }
            case BoxCollider box:
            {
                var t = box.transform;
                Vector3 half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
                return Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center), half, overlapBuffer, t.rotation, hitMask, QueryTriggerInteraction.Ignore);
            }
            default:
                return Physics.OverlapBoxNonAlloc(weaponCollider.bounds.center, weaponCollider.bounds.extents, overlapBuffer, Quaternion.identity, hitMask, QueryTriggerInteraction.Ignore);
        }
    }

    static void GetCapsulePoints(CapsuleCollider cap, out Vector3 p1, out Vector3 p2, out float radius)
    {
        Transform t = cap.transform;
        Vector3 center = t.TransformPoint(cap.center);
        Vector3 axis; float axisScale;
        switch (cap.direction)
        {
            case 0:  axis = t.right;   axisScale = t.lossyScale.x; break;
            case 1:  axis = t.up;      axisScale = t.lossyScale.y; break;
            default: axis = t.forward; axisScale = t.lossyScale.z; break;
        }
        float uniformScale = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
        radius = cap.radius * uniformScale;
        float halfHeight = Mathf.Max(0f, cap.height * axisScale * 0.5f - radius);
        p1 = center + axis * halfHeight;
        p2 = center - axis * halfHeight;
    }

    void ProcessHit(Collider other)
    {
        if (other.transform.IsChildOf(transform.root)) return;
        if (owner != null && other.transform.IsChildOf(owner.transform)) return;
        if (!hitThisSwing.Add(other)) return;

        var target = other.GetComponentInParent<Character>();
        if (target != null)
        {
            if (!target.IsAlive) return;
            if (owner != null && !FactionRules.IsHostile(owner.Faction, target.Faction)) return;
        }

        Vector3 contact = other.ClosestPoint(weaponCollider.bounds.center);

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

        var info = new DamageInfo
        {
            Amount         = baseDamage * profile.damageMultiplier,
            Source         = owner,
            HitPoint       = contact,
            Direction      = dir,
            KnockbackForce = force,
        };

        var damageable = other.GetComponentInParent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(info);
            other.GetComponentInParent<HitReactionController>()?.ReactToHit(contact, dir);

            bool victimIsPlayer = target is Player;
            if (CombatManager.HasInstance &&
                (!victimIsPlayer || CombatManager.Instance.hitStopOnPlayerHurt))
                CombatManager.Instance.RequestHitStop(profile.hitStopScale);
        }

        PlayHitEffects(contact, dir);
        owner?.NotifyHitLanded(info);
    }

    void PlayHitEffects(Vector3 contact, Vector3 dir)
    {
        AudioData audio = profile.useDefaultEffects
            ? (CombatManager.HasInstance ? CombatManager.Instance.defaultHitAudio : null)
            : profile.hitAudio;
        if (audio != null && AudioManager.HasInstance)
            AudioManager.Instance.PlaySFXData(audio, contact);

        GameObject particle = profile.useDefaultEffects
            ? (CombatManager.HasInstance ? CombatManager.Instance.GetRandomHitParticle() : null)
            : profile.hitParticle;
        if (particle != null)
            Instantiate(particle, contact, Quaternion.LookRotation(-dir));
    }
}

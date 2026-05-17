using System.Collections.Generic;
using UnityEngine;

public class WeaponHitbox : MonoBehaviour
{
    [Tooltip("The capsule that defines the weapon's hit shape. Can be on any child object.")]
    [SerializeField] CapsuleCollider weaponCollider;
    [SerializeField] LayerMask hitMask;

    [Header("Hit FX")]
    [SerializeField] AudioClip hitSound;
    [SerializeField] GameObject hitParticlePrefab;

    [Header("Hit Stop")]
    [SerializeField] bool enableHitStop = true;
    [SerializeField] float hitStopDuration = 0.07f;

    [Header("Knockback")]
    [SerializeField] bool enableKnockback = true;

    bool active;
    StatsComponent cachedAttackerStats;
    readonly HashSet<Collider> hitThisSwing = new HashSet<Collider>();
    readonly Collider[] overlapBuffer = new Collider[16];

    // Called by ItemHolder when a weapon is equipped/unequipped
    public void SetWeapon(AudioClip sound = null, GameObject particlePrefab = null)
    {
        hitSound          = sound;
        hitParticlePrefab = particlePrefab;
    }

    public void ClearWeapon()
    {
        hitSound          = null;
        hitParticlePrefab = null;
    }

    public void EnableHitbox()
    {
        hitThisSwing.Clear();
        cachedAttackerStats = GetComponentInParent<StatsComponent>();
        active = true;
    }

    public void DisableHitbox()
    {
        active = false;
        hitThisSwing.Clear();
    }

    void Update()
    {
        if (!active || weaponCollider == null) return;

        GetCapsulePoints(out Vector3 p1, out Vector3 p2, out float radius);
        int count = Physics.OverlapCapsuleNonAlloc(p1, p2, radius, overlapBuffer, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
            ProcessHit(overlapBuffer[i]);
    }

    void GetCapsulePoints(out Vector3 p1, out Vector3 p2, out float radius)
    {
        Transform t = weaponCollider.transform;
        Vector3 center = t.TransformPoint(weaponCollider.center);

        Vector3 axis;
        float axisScale;
        switch (weaponCollider.direction)
        {
            case 0:  axis = t.right;   axisScale = t.lossyScale.x; break;
            case 1:  axis = t.up;      axisScale = t.lossyScale.y; break;
            default: axis = t.forward; axisScale = t.lossyScale.z; break;
        }

        float uniformScale = Mathf.Max(t.lossyScale.x, t.lossyScale.y, t.lossyScale.z);
        radius = weaponCollider.radius * uniformScale;
        float halfHeight = Mathf.Max(0f, weaponCollider.height * axisScale * 0.5f - radius);

        p1 = center + axis * halfHeight;
        p2 = center - axis * halfHeight;
    }

    void ProcessHit(Collider other)
    {
        if (other.transform.IsChildOf(transform.root)) return;
        if (!hitThisSwing.Add(other)) return;

        float damage = cachedAttackerStats != null ? cachedAttackerStats.GetFinal(StatType.AttackDamage) : 0f;

        // Skip friendly fire
        var targetStats = other.GetComponentInParent<StatsComponent>();
        if (cachedAttackerStats != null && targetStats != null && cachedAttackerStats.Faction == targetStats.Faction)
            return;

        Vector3 direction = (other.transform.position - transform.root.position).normalized;

        var target = other.GetComponentInParent<IDamageable>();
        if (target != null)
            target.TakeDamage(damage, enableKnockback ? direction : Vector3.zero);

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, other.transform.position);

        if (hitParticlePrefab != null)
        {
            var contact = other.ClosestPoint(transform.position);
            Instantiate(hitParticlePrefab, contact, Quaternion.LookRotation(-direction));
        }

        if (enableHitStop)
            HitStopManager.Instance?.Trigger(hitStopDuration);
    }
}

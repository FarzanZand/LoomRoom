using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IDamageable
{
    void TakeDamage(float amount, Vector3 knockbackDirection);
}

[RequireComponent(typeof(CapsuleCollider))]
public class WeaponHitbox : MonoBehaviour
{
    [Header("Hit Stop")]
    [SerializeField] bool enableHitStop = true;
    [SerializeField] float hitStopDuration = 0.07f;

    [Header("Knockback")]
    [SerializeField] bool enableKnockback = true;
    [SerializeField] float knockbackForce = 5f;

    private CapsuleCollider col;
    private float damage;
    private AudioClip hitSound;
    private GameObject hitParticlePrefab;

    private readonly HashSet<Collider> hitThisSwing = new HashSet<Collider>();
    private Coroutine hitStopRoutine;

    private void Awake()
    {
        col = GetComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.enabled = false;
    }

    public void SetWeapon(float attackRange, float attackDamage, AudioClip sound = null, GameObject particlePrefab = null)
    {
        damage            = attackDamage;
        hitSound          = sound;
        hitParticlePrefab = particlePrefab;
        col.direction     = 2;
        col.radius        = 0.12f;
        col.height        = attackRange;
        col.center        = new Vector3(0f, 0f, attackRange * 0.5f);
    }

    public void ClearWeapon()
    {
        damage            = 0f;
        hitSound          = null;
        hitParticlePrefab = null;
        col.height        = 0f;
        col.enabled       = false;
    }

    public void EnableHitbox()
    {
        hitThisSwing.Clear();
        col.enabled = true;
    }

    public void DisableHitbox()
    {
        col.enabled = false;
        hitThisSwing.Clear();
    }

    // OnTriggerEnter misses colliders already overlapping when the hitbox is enabled.
    // OnTriggerStay catches those. The HashSet prevents double-hitting the same collider.
    private void OnTriggerEnter(Collider other) => ProcessHit(other);
    private void OnTriggerStay(Collider other)  => ProcessHit(other);

    private void ProcessHit(Collider other)
    {
        if (other.transform.IsChildOf(transform.root)) return;
        if (!hitThisSwing.Add(other)) return;

        Vector3 direction = (other.transform.position - transform.root.position).normalized;

        if (other.TryGetComponent<IDamageable>(out var target))
        {
            Debug.Log($"[WeaponHitbox] hit {other.name} for {damage} damage");
            target.TakeDamage(damage, direction);
        }

        if (enableKnockback && other.attachedRigidbody != null)
            other.attachedRigidbody.AddForce(direction * knockbackForce, ForceMode.Impulse);

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, other.transform.position);

        if (hitParticlePrefab != null)
        {
            var contact = other.ClosestPoint(transform.position);
            Instantiate(hitParticlePrefab, contact, Quaternion.LookRotation(-direction));
        }

        if (enableHitStop)
        {
            if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
            hitStopRoutine = StartCoroutine(HitStopRoutine());
        }
    }

    private IEnumerator HitStopRoutine()
    {
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(hitStopDuration);
        Time.timeScale = 1f;
        hitStopRoutine = null;
    }
}

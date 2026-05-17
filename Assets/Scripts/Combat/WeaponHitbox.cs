using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IDamageable
{
    void TakeDamage(float amount, Vector3 knockbackDirection);
}

public class WeaponHitbox : MonoBehaviour
{
    [SerializeField] Collider hitCollider;

    [Header("Damage")]
    [SerializeField] float damage = 10f;
    [SerializeField] AudioClip hitSound;
    [SerializeField] GameObject hitParticlePrefab;

    [Header("Hit Stop")]
    [SerializeField] bool enableHitStop = true;
    [SerializeField] float hitStopDuration = 0.07f;

    [Header("Knockback")]
    [SerializeField] bool enableKnockback = true;
    [SerializeField] float knockbackForce = 5f;

    private readonly HashSet<Collider> hitThisSwing = new HashSet<Collider>();
    private Coroutine hitStopRoutine;

    private void Awake()
    {
        if (hitCollider == null)
            hitCollider = GetComponent<Collider>();

        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
            hitCollider.enabled = false;
        }
    }

    public void SetWeapon(float attackDamage, AudioClip sound = null, GameObject particlePrefab = null)
    {
        damage            = attackDamage;
        hitSound          = sound;
        hitParticlePrefab = particlePrefab;
    }

    public void ClearWeapon()
    {
        damage            = 0f;
        hitSound          = null;
        hitParticlePrefab = null;
    }

    public void EnableHitbox()
    {
        hitThisSwing.Clear();
        if (hitCollider != null) hitCollider.enabled = true;
    }

    public void DisableHitbox()
    {
        if (hitCollider != null) hitCollider.enabled = false;
        hitThisSwing.Clear();
    }

    private void OnTriggerEnter(Collider other) => ProcessHit(other);
    private void OnTriggerStay(Collider other)  => ProcessHit(other);

    public void ProcessHit(Collider other)
    {
        if (other.transform.IsChildOf(transform.root)) return;
        if (!hitThisSwing.Add(other)) return;

        Vector3 direction = (other.transform.position - transform.root.position).normalized;

        if (other.TryGetComponent<IDamageable>(out var target))
        {
            Debug.Log($"[WeaponHitbox] hit {other.name} for {damage} damage");
            target.TakeDamage(damage, enableKnockback ? direction : Vector3.zero);
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

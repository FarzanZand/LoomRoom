using UnityEngine;

public interface IDamageable
{
    void TakeDamage(float amount);
}

[RequireComponent(typeof(CapsuleCollider))]
public class WeaponHitbox : MonoBehaviour
{
    private CapsuleCollider col;
    private float damage;

    private void Awake()
    {
        col = GetComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.enabled = false;
    }

    public void SetWeapon(float attackRange, float attackDamage)
    {
        damage = attackDamage;
        col.direction = 2; // Z = forward
        col.radius = 0.12f;
        col.height = attackRange;
        col.center = new Vector3(0f, 0f, attackRange * 0.5f);
    }

    public void ClearWeapon()
    {
        damage = 0f;
        col.height = 0f;
        col.enabled = false;
    }

    // Called by animation events
    public void EnableHitbox()  => col.enabled = true;
    public void DisableHitbox() => col.enabled = false;

    private void OnTriggerEnter(Collider other)
    {
        if (other.transform.IsChildOf(transform.root)) return;

        if (other.TryGetComponent<IDamageable>(out var target))
            target.TakeDamage(damage);
    }
}

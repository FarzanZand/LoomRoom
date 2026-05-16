using UnityEngine;

public class PlayerHealth : MonoBehaviour, IDamageable
{
    [SerializeField] float maxHealth = 100f;

    public float Health { get; private set; }
    public bool IsAlive => Health > 0f;

    void Awake() => Health = maxHealth;

    public void TakeDamage(float amount, Vector3 knockbackDirection)
    {
        if (!IsAlive) return;
        Health = Mathf.Max(0f, Health - amount);
        Debug.Log($"[Player] took {amount} damage — health: {Health}/{maxHealth}");

        if (!IsAlive)
            Debug.Log("[Player] died");
    }

    public void Heal(float amount)
    {
        if (!IsAlive) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }
}

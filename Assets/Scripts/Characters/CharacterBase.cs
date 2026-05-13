using UnityEngine;

public abstract class CharacterBase : MonoBehaviour
{
    [Header("Stats")]
    public float maxHealth = 100f;

    public float Health { get; private set; }
    public bool IsAlive => Health > 0f;

    protected virtual void Awake()
    {
        Health = maxHealth;
    }

    public virtual void TakeDamage(float damage)
    {
        if (!IsAlive) return;
        Health = Mathf.Max(0f, Health - damage);
        if (!IsAlive) Die();
    }

    public virtual void Heal(float amount)
    {
        if (!IsAlive) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }

    protected virtual void Die() { }
}

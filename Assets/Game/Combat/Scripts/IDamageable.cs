using UnityEngine;

// Everything a hit carries. Built by the attacker (Hitbox, EnemyBrain fallback,
// effects), consumed by CharacterStats.TakeDamage and then broadcast through
// Character.Damaged so FX, hit reactions and item triggers all see the same facts.
public struct DamageInfo
{
    public HitProfile Profile;       // impact presentation, resolved by CombatManager
    public float     Amount;          // raw on the way in, actual after TakeDamage
    public Character Source;          // who did it (may be null for environment)
    public Character Target;          // the victim, set by CharacterStats.TakeDamage (null for props)
    public Vector3   HitPoint;
    public Vector3   Direction;       // attacker -> victim, horizontal, normalised
    public float     KnockbackForce;  // already scaled by CombatManager
    public bool      Parried;
    public bool      Heavy;           // captured by the hitbox for this swing
    public bool      Blocked;         // set by the victim's IBlocker
    public bool      FromEffect;      // created by an item effect; never counts as a landed hit

    public static DamageInfo Simple(float amount, Character source = null)
        => new DamageInfo { Amount = amount, Source = source, Direction = Vector3.zero };
}

public interface IDamageable
{
    void TakeDamage(DamageInfo info);
}

// Read by health bars without knowing which component owns the number.
public interface IHealth
{
    float Current { get; }
    float Max     { get; }
    event System.Action HealthChanged;
}

// Implemented by whatever moves the character (PlayerMotor, EnemyMotor).
public interface IKnockbackReceiver
{
    void ApplyKnockback(Vector3 direction, float force);
}

// Implemented by something that can turn an incoming hit into a block (PlayerCombat).
// Returns true if the hit was blocked; may adjust the info (e.g. mark Blocked).
public interface IBlocker
{
    // Shield raised right now: the shield's armor counts toward the Armor stat.
    bool IsGuarding { get; }
    bool TryBlock(ref DamageInfo info);
}

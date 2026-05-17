using System;
using UnityEngine;

public enum StatType
{
    MaxHealth,
    Defense,
    AttackDamage,
    CritChance,           // 0–1
    CritMultiplier,       // e.g. 1.5 = 150% damage on crit
    DodgeChance,          // 0–1
    MoveSpeed,
    AttackSpeed,          // multiplier on attack cooldown (1 = normal)
    Experience,
    ExperienceToNextLevel
}

public enum ModifierType
{
    Flat,             // added directly to base value
    PercentAdd,       // additive percent — multiple sources stack before applying
    PercentMultiply   // multiplicative — each source multiplies independently
}

[Serializable]
public struct StatEntry
{
    public StatType stat;
    public float    baseValue;
    [Tooltip("Minimum final value after all modifiers.")]
    public float    min;
    [Tooltip("Maximum final value after all modifiers. 0 = no cap.")]
    public float    max;
}

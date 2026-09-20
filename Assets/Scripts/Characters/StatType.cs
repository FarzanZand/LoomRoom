using System;
using UnityEngine;

// Values are explicit and must never be reused or renumbered — Unity serializes
// enums as integers, so changing a value re-points every authored asset.
// Retired: 3 (CritChance), 4 (CritMultiplier), 5 (DodgeChance), 8 (Experience),
// 9 (ExperienceToNextLevel). Leave those numbers unused forever.
public enum StatType
{
    MaxHealth    = 0,
    Defense      = 1,   // flat damage reduction per hit
    AttackDamage = 2,
    MoveSpeed    = 6,   // multiplier on CharacterData movement speeds (1 = normal)
    AttackSpeed  = 7,   // multiplier on attack animation speed (1 = normal)
    MaxStamina   = 10,
    StaminaRegen = 11,  // stamina per second
}

public enum ModifierType
{
    Flat            = 0,   // added directly to base value
    PercentAdd      = 1,   // additive percent — multiple sources stack before applying
    PercentMultiply = 2,   // multiplicative — each source multiplies independently
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

    public StatEntry(StatType stat, float baseValue, float min = 0f, float max = 0f)
    {
        this.stat = stat; this.baseValue = baseValue; this.min = min; this.max = max;
    }
}

// Authored on items and other sources: "+10 AttackDamage flat".
[Serializable]
public class StatModifierEntry
{
    public StatType     stat;
    public float        value;
    public ModifierType type = ModifierType.Flat;

    public string Describe()
    {
        string sign = value >= 0 ? "+" : "";
        return type switch
        {
            ModifierType.Flat => $"{sign}{value:0.##} {stat}",
            _                 => $"{sign}{value * 100f:0.#}% {stat}",
        };
    }
}

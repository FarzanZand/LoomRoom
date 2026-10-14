using System;
using UnityEngine;

// Values are explicit and must never be reused or renumbered — Unity serializes
// enums as integers, so changing a value re-points every authored asset.
// Retired: 3 (CritChance), 4 (CritMultiplier), 5 (DodgeChance), 8 (Experience),
// 9 (ExperienceToNextLevel). Leave those numbers unused forever.
public enum StatType
{
    MaxHealth    = 0,
    Armor        = 1,   // flat damage reduction per hit
    AttackDamage = 2,
    MoveSpeed    = 6,   // multiplier on CharacterData movement speeds (1 = normal)
    AttackSpeed  = 7,   // multiplier on attack animation speed (1 = normal)
    MaxMana   = 10,
    ManaRegen = 11,  // mana per second
    MaxStamina = 12,
    StaminaRegen = 13,
    Strength = 14,
    Dexterity = 15,
    Constitution = 16,
    Intelligence = 17,
    Perception = 18,
    Charisma = 19,
    // Gear stats (the loot rework). Fractions where the line shows a percent (0.06 = +6%).
    CriticalChance = 20,     // added to CombatManager.critChance
    BackstabDamage = 21,     // backstabs deal (1 + this) times more
    LightRadius = 22,        // metres added to the light radius on dark floors
    QuietSteps = 23,         // footsteps carry this much less far (1 = silent)
    SpellDamage = 24,        // spells hit and heal (1 + this) times harder
    KillHeal = 25,           // health back on every kill
    GoldFind = 26,           // gold picked up is (1 + this) times more
    ShieldBashDamage = 27,   // added to the shield bash's damage
    DamageToSkeletons = 28,  // melee hits on skeletons deal (1 + this) times more
    DamageToBeasts = 29,
    DamageToSlimes = 30,
    SwordsSkill = 31,        // ranks added to the skill while worn
    MacesSkill = 32,
    BlockingSkill = 33,
    FireCircleOnHit = 34,    // chance a melee hit casts Fire Circle under the target (CombatManager.fireCircleOnHit)
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

    public static string Label(StatType stat)=>stat switch {StatType.Armor=>"Armor",StatType.AttackDamage=>"Damage",StatType.MaxHealth=>"Health",StatType.MaxMana=>"Mana",StatType.ManaRegen=>"Mana/sec",StatType.MaxStamina=>"Stamina",StatType.StaminaRegen=>"Stamina/sec",_=>stat.ToString()};
    public string Describe() => Describe(stat, type, value);

    // One stat in plain words: "+6% critical chance", "Kills heal 2", "+1 m light radius".
    public static string Describe(StatType stat, ModifierType type, float value)
    {
        string sign = value >= 0 ? "+" : "";
        if (type == ModifierType.Flat)
            switch (stat)
            {
                case StatType.CriticalChance: return $"{sign}{value * 100f:0.#}% critical chance";
                case StatType.BackstabDamage: return $"{sign}{value * 100f:0.#}% backstab damage";
                case StatType.LightRadius: return $"{sign}{value:0.#} m light radius";
                case StatType.QuietSteps: return value >= 1f ? "Your steps make no noise" : $"Your steps are heard {value * 100f:0}% less far";
                case StatType.SpellDamage: return $"{sign}{value * 100f:0.#}% spell damage";
                case StatType.KillHeal: return $"Kills heal {value:0.#}";
                case StatType.GoldFind: return $"{sign}{value * 100f:0.#}% gold found";
                case StatType.ShieldBashDamage: return $"{sign}{value:0.#} shield bash damage";
                case StatType.DamageToSkeletons: return $"{sign}{value * 100f:0.#}% damage to skeletons";
                case StatType.DamageToBeasts: return $"{sign}{value * 100f:0.#}% damage to beasts";
                case StatType.DamageToSlimes: return $"{sign}{value * 100f:0.#}% damage to slimes";
                case StatType.SwordsSkill: return $"{sign}{value:0} Swords";
                case StatType.MacesSkill: return $"{sign}{value:0} Maces";
                case StatType.BlockingSkill: return $"{sign}{value:0} Blocking";
                case StatType.FireCircleOnHit: return $"{value * 100f:0.#}% chance on hit to cast Fire Circle";
                case StatType.MoveSpeed: return $"{sign}{value * 100f:0.#}% move speed";
                case StatType.AttackSpeed: return $"Swings {Mathf.Abs(value) * 100f:0}% {(value < 0 ? "slower" : "faster")}";
                case StatType.ManaRegen: return $"{sign}{value:0.#} mana a second";
                case StatType.MaxHealth: return $"{sign}{value:0.#} health";
                case StatType.MaxMana: return $"{sign}{value:0.#} mana";
                case StatType.AttackDamage: return $"{sign}{value:0.#} damage";
                case StatType.Armor: return $"{sign}{value:0.#} armor";
                case StatType.MaxStamina: return $"{sign}{value:0.#} stamina";
            }
        return type switch
        {
            ModifierType.Flat => $"{sign}{value:0.##} {Label(stat)}",
            _                 => $"{sign}{value * 100f:0.#}% {Label(stat)}",
        };
    }
}

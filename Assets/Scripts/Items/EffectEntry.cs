using System;
using Sirenix.OdinInspector;
using UnityEngine;

// One inline effect on an item. The common cases (heal, stamina, a timed buff, a
// sound, a flag) are configured directly with no extra asset; Custom routes to an
// ItemEffect ScriptableObject for anything richer. Only the fields the chosen type
// uses are shown.
[Serializable]
public class EffectEntry
{
    [ValueDropdown("TypeOptions")]
    public EffectType type = EffectType.Heal;

    // Grouped picker. Presentational only — the enum ints are untouched.
    static readonly ValueDropdownList<EffectType> TypeOptions = new()
    {
        { "Vitals/Heal",             EffectType.Heal },
        { "Vitals/Restore Stamina",  EffectType.RestoreStamina },
        { "Vitals/Damage",           EffectType.Damage },
        { "Stats/Timed Stat Buff",   EffectType.TimedStatBuff },
        { "Feedback/Play Audio",     EffectType.PlayAudio },
        { "Feedback/Spawn Prefab",   EffectType.SpawnPrefab },
        { "Progression/Set Flag",    EffectType.SetFlag },
        { "Custom (script asset)",   EffectType.Custom },
    };

    [Tooltip("When this effect runs. OnUse for consumables, OnEquip/OnUnequip for gear, OnHitLanded/OnHurt for reactive gear.")]
    public EffectTrigger trigger = EffectTrigger.OnUse;

    [Range(0f, 100f)]
    [Tooltip("Percent chance the effect fires when triggered.")]
    public float chance = 100f;

    [ShowIf("@type == EffectType.Heal || type == EffectType.RestoreStamina || type == EffectType.Damage || type == EffectType.TimedStatBuff")]
    public float value = 10f;

    [ShowIf("@type == EffectType.TimedStatBuff")]
    public StatType stat = StatType.AttackDamage;

    [ShowIf("@type == EffectType.TimedStatBuff")]
    public ModifierType modifierType = ModifierType.Flat;

    [ShowIf("@type == EffectType.TimedStatBuff")]
    [Tooltip("Seconds the buff lasts. 0 or less = permanent (removed on unequip when the trigger is OnEquip).")]
    public float duration = 10f;

    [ShowIf("@type == EffectType.PlayAudio")]
    public AudioData audio;

    [ShowIf("@type == EffectType.PlayAudio")]
    [Tooltip("Play as 2D (no spatialisation).")]
    public bool audio2D = true;

    [ShowIf("@type == EffectType.SpawnPrefab")]
    public GameObject prefab;

    [ShowIf("@type == EffectType.SpawnPrefab")]
    [Tooltip("Parent the spawned object to the user so it follows them.")]
    public bool attachToUser;

    [ShowIf("@type == EffectType.SetFlag")]
    public string flag;

    [ShowIf("@type == EffectType.SetFlag")]
    public int flagValue = 1;

    [ShowIf("@type == EffectType.Custom")]
    public ItemEffect customEffect;

    public string Describe()
    {
        switch (type)
        {
            case EffectType.Heal:           return $"Heals {value:0.#}";
            case EffectType.RestoreStamina: return $"Restores {value:0.#} stamina";
            case EffectType.Damage:         return $"Deals {value:0.#} damage";
            case EffectType.TimedStatBuff:
            {
                string v = modifierType == ModifierType.Flat ? $"{value:+0.#;-0.#}" : $"{value * 100f:+0.#;-0.#}%";
                return duration > 0f ? $"{v} {stat} for {duration:0.#}s" : $"{v} {stat}";
            }
            case EffectType.SetFlag:        return $"Sets {flag}";
            case EffectType.Custom:         return customEffect != null ? customEffect.Describe(this) : "";
            default:                        return "";
        }
    }
}

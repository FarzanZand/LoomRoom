using System;
using System.Collections.Generic;
using UnityEngine;

// Universal stat container. Any GameObject — enemy, player, NPC, destructible prop —
// can carry one of these to participate in the damage and buff systems.
public class StatsComponent : MonoBehaviour, IDamageable
{
    [Tooltip("Shared stat defaults. Per-instance overrides below take priority.")]
    [SerializeField] StatsDataSO statsData;

    [Tooltip("Override or add stats on top of the SO values.")]
    [SerializeField] List<StatEntry> overrides;

    // ── Events ────────────────────────────────────────────────────────
    public event Action<float, Vector3> OnDamageTaken;  // (actualDamage, knockbackDir)
    public event Action<float>          OnHealed;       // (amount)
    public event Action                 OnDied;
    public event Action<StatType, float> OnStatChanged; // (stat, newFinalValue)

    // ── State ─────────────────────────────────────────────────────────
    public float CurrentHealth { get; private set; }
    public bool  IsAlive       => CurrentHealth > 0f;

    readonly Dictionary<StatType, float> baseStats = new();
    readonly List<StatModifier>          modifiers = new();

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake()
    {
        // SO values first
        if (statsData != null)
            foreach (var e in statsData.stats)
                baseStats[e.stat] = e.baseValue;

        // Per-instance overrides on top
        if (overrides != null)
            foreach (var e in overrides)
                baseStats[e.stat] = e.baseValue;

        CurrentHealth = GetFinal(StatType.MaxHealth);
    }

    // ── Queries ───────────────────────────────────────────────────────

    public bool HasStat(StatType stat) => baseStats.ContainsKey(stat);

    public float GetBase(StatType stat) =>
        baseStats.TryGetValue(stat, out var v) ? v : 0f;

    public float GetFinal(StatType stat)
    {
        float flat       = 0f;
        float percentAdd = 0f;
        float percentMul = 1f;

        foreach (var mod in modifiers)
        {
            if (mod.Stat != stat) continue;
            switch (mod.Type)
            {
                case ModifierType.Flat:            flat       += mod.Value;           break;
                case ModifierType.PercentAdd:      percentAdd += mod.Value;           break;
                case ModifierType.PercentMultiply: percentMul *= (1f + mod.Value);    break;
            }
        }

        return (GetBase(stat) + flat) * (1f + percentAdd) * percentMul;
    }

    // ── Modifiers ─────────────────────────────────────────────────────

    public void AddModifier(StatModifier mod)
    {
        modifiers.Add(mod);
        OnStatChanged?.Invoke(mod.Stat, GetFinal(mod.Stat));
    }

    public void RemoveModifier(StatModifier mod)
    {
        if (modifiers.Remove(mod))
            OnStatChanged?.Invoke(mod.Stat, GetFinal(mod.Stat));
    }

    public void RemoveAllFromSource(object source)
    {
        var affected = new HashSet<StatType>();
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (modifiers[i].Source == source)
            {
                affected.Add(modifiers[i].Stat);
                modifiers.RemoveAt(i);
            }
        }
        foreach (var stat in affected)
            OnStatChanged?.Invoke(stat, GetFinal(stat));
    }

    // ── IDamageable ───────────────────────────────────────────────────

    public void TakeDamage(float rawAmount, Vector3 knockbackDirection)
    {
        if (!IsAlive) return;

        float defense = GetFinal(StatType.Defense);
        float actual  = Mathf.Max(0f, rawAmount - defense);

        CurrentHealth = Mathf.Max(0f, CurrentHealth - actual);
        OnDamageTaken?.Invoke(actual, knockbackDirection);

        if (!IsAlive) OnDied?.Invoke();
    }

    // ── Healing ───────────────────────────────────────────────────────

    public void Heal(float amount)
    {
        if (!IsAlive) return;
        float maxHP  = GetFinal(StatType.MaxHealth);
        float healed = Mathf.Min(amount, maxHP - CurrentHealth);
        if (healed <= 0f) return;
        CurrentHealth += healed;
        OnHealed?.Invoke(healed);
    }
}

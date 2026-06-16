using System;
using System.Collections.Generic;
using UnityEngine;

// Universal stat container — attach to any GameObject that participates
// in the damage, buff, or progression systems.
public class StatsComponent : MonoBehaviour, IDamageable
{
    [Tooltip("Assign a StatProfile directly, or leave empty and let the character " +
             "data asset (EnemyData / PlayerData) push the profile at runtime.")]
    [SerializeField] StatProfile statsData;

    [Tooltip("Per-instance overrides on top of the profile values.")]
    [SerializeField] List<StatEntry> overrides;

    [SerializeField] Faction faction = Faction.Neutral;

    // ── Events ────────────────────────────────────────────────────────
    public event Action<float, Vector3>   OnDamageTaken;   // (actualDamage, knockbackDir)
    public event Action<float>            OnHealed;        // (healAmount)
    public event Action                   OnDied;
    public event Action<StatType, float, float> OnStatChanged; // (stat, oldValue, newValue)

    // ── State ─────────────────────────────────────────────────────────
    public float   CurrentHealth { get; private set; }
    public float   IncomingDamageScale { get; set; } = 1f;
    public bool    IsAlive       => CurrentHealth > 0f;
    public Faction Faction       => faction;

    readonly Dictionary<StatType, float>          baseStats  = new();
    readonly Dictionary<StatType, (float min, float max)> statRanges = new();
    readonly List<StatModifier>                   modifiers  = new();

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake()
    {
        LoadProfile(statsData);

        if (overrides != null)
            foreach (var e in overrides)
            {
                baseStats[e.stat]  = e.baseValue;
                statRanges[e.stat] = (e.min, e.max);
            }
    }

    void Start()
    {
        // Initialised here so any ApplyProfile call from a character controller's
        // Awake has already run and MaxHealth reflects the correct value.
        CurrentHealth = GetFinal(StatType.MaxHealth);
    }

    void Update()
    {
        // Tick timed modifiers, remove expired ones, fire change events
        var oldValues = new Dictionary<StatType, float>();

        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (modifiers[i].IsPermanent) continue;

            modifiers[i].Tick(Time.deltaTime);

            if (modifiers[i].IsExpired)
            {
                var stat = modifiers[i].Stat;
                if (!oldValues.ContainsKey(stat))
                    oldValues[stat] = GetFinal(stat);
                modifiers.RemoveAt(i);
            }
        }

        foreach (var kvp in oldValues)
            OnStatChanged?.Invoke(kvp.Key, kvp.Value, GetFinal(kvp.Key));
    }

    // ── Profile loading ───────────────────────────────────────────────

    void LoadProfile(StatProfile profile)
    {
        if (profile?.stats == null) return;
        foreach (var e in profile.stats)
        {
            baseStats[e.stat]  = e.baseValue;
            statRanges[e.stat] = (e.min, e.max);
        }
    }

    // Called by EnemyController in Awake to push the data asset's profile.
    public void ApplyProfile(StatProfile profile)
    {
        LoadProfile(profile);
    }

    // Called by PlayerController — stats embedded directly in PlayerData.
    public void ApplyProfile(List<StatEntry> entries)
    {
        if (entries == null) return;
        foreach (var e in entries)
        {
            baseStats[e.stat]  = e.baseValue;
            statRanges[e.stat] = (e.min, e.max);
        }
    }

    // ── Queries ───────────────────────────────────────────────────────

    public bool  HasStat(StatType stat) => baseStats.ContainsKey(stat);
    public float GetBase(StatType stat) => baseStats.TryGetValue(stat, out var v) ? v : 0f;

    public float GetFinal(StatType stat)
    {
        float flat = 0f, percentAdd = 0f, percentMul = 1f;

        foreach (var mod in modifiers)
        {
            if (mod.Stat != stat) continue;
            switch (mod.Type)
            {
                case ModifierType.Flat:            flat       += mod.Value;         break;
                case ModifierType.PercentAdd:      percentAdd += mod.Value;         break;
                case ModifierType.PercentMultiply: percentMul *= (1f + mod.Value);  break;
            }
        }

        float result = (GetBase(stat) + flat) * (1f + percentAdd) * percentMul;

        if (statRanges.TryGetValue(stat, out var range))
        {
            result = Mathf.Max(result, range.min);
            if (range.max > 0f)
                result = Mathf.Min(result, range.max);
        }

        return result;
    }

    // ── Modifiers ─────────────────────────────────────────────────────

    public void AddModifier(StatModifier mod)
    {
        float old = GetFinal(mod.Stat);
        modifiers.Add(mod);
        OnStatChanged?.Invoke(mod.Stat, old, GetFinal(mod.Stat));
    }

    public void RemoveModifier(StatModifier mod)
    {
        float old = GetFinal(mod.Stat);
        if (modifiers.Remove(mod))
            OnStatChanged?.Invoke(mod.Stat, old, GetFinal(mod.Stat));
    }

    public void RemoveAllFromSource(object source)
    {
        var oldValues = new Dictionary<StatType, float>();

        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(modifiers[i].Source, source)) continue;
            var stat = modifiers[i].Stat;
            if (!oldValues.ContainsKey(stat))
                oldValues[stat] = GetFinal(stat);
            modifiers.RemoveAt(i);
        }

        foreach (var kvp in oldValues)
            OnStatChanged?.Invoke(kvp.Key, kvp.Value, GetFinal(kvp.Key));
    }

    // ── Faction ───────────────────────────────────────────────────────

    public void SetFaction(Faction f) => faction = f;

    // ── IDamageable ───────────────────────────────────────────────────

    public void TakeFlatDamage(float amount)
    {
        if (!IsAlive) return;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
        OnDamageTaken?.Invoke(amount, Vector3.zero);
        if (!IsAlive) OnDied?.Invoke();
    }

    public void TakeDamage(float rawAmount, Vector3 knockbackDirection)
    {
        if (!IsAlive) return;

        float defense = GetFinal(StatType.Defense);
        float actual  = Mathf.Max(0f, rawAmount - defense) * IncomingDamageScale;

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

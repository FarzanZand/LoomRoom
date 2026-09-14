using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Universal stat container — health and stamina pools plus a modifier stack over the
// base values authored on CharacterData. Resolves fully in Awake from the sibling
// Character so nothing has to push a profile in "before Start".
[DefaultExecutionOrder(-50)]
public class CharacterStats : MonoBehaviour, IDamageable, IHealth
{
    [Tooltip("Per-instance overrides on top of the CharacterData values.")]
    [SerializeField] List<StatEntry> overrides = new();

    // ── Events ────────────────────────────────────────────────────────
    public event Action<DamageInfo>             Damaged;
    public event Action<float>                  Healed;
    public event Action                         Died;
    public event Action<StatType, float, float> StatChanged;   // (stat, old, new)
    public event Action                         HealthChanged;
    public event Action                         StaminaChanged;

    // ── State ─────────────────────────────────────────────────────────
    [ShowInInspector, ReadOnly] public float CurrentHealth  { get; private set; }
    [ShowInInspector, ReadOnly] public float CurrentStamina { get; private set; }
    public float MaxHealth  => GetFinal(StatType.MaxHealth);
    public float MaxStamina => HasStat(StatType.MaxStamina) ? GetFinal(StatType.MaxStamina) : 0f;
    public bool  IsAlive    => CurrentHealth > 0f;
    public bool  IsExhausted { get; private set; }

    // IHealth
    public float Current => CurrentHealth;
    public float Max     => MaxHealth;

    public Character Character { get; private set; }
    public Faction   Faction   => Character != null ? Character.Faction : Faction.Neutral;

    readonly Dictionary<StatType, float>               baseStats  = new();
    readonly Dictionary<StatType, (float min, float max)> statRanges = new();
    readonly List<StatModifier>                        modifiers  = new();
    readonly List<StatType>                            changedScratch = new();

    IBlocker blocker;
    float staminaRegenDelayTimer;
    bool  initialised;

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake()
    {
        Character = GetComponent<Character>();
        blocker   = GetComponent<IBlocker>();
        LoadBase();
    }

    void Start()
    {
        // Pools initialise here so every modifier added during Awake (starting gear) counts.
        CurrentHealth  = MaxHealth;
        CurrentStamina = MaxStamina;
        initialised = true;
        HealthChanged?.Invoke();
        StaminaChanged?.Invoke();
    }

    void LoadBase()
    {
        baseStats.Clear();
        statRanges.Clear();
        if (Character != null && Character.data != null && Character.data.stats != null)
            foreach (var e in Character.data.stats) SetBase(e);
        foreach (var e in overrides) SetBase(e);
    }

    void SetBase(StatEntry e)
    {
        baseStats[e.stat]  = e.baseValue;
        statRanges[e.stat] = (e.min, e.max);
    }

    void Update()
    {
        TickModifiers();
        TickStamina();
    }

    void TickModifiers()
    {
        changedScratch.Clear();
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            var m = modifiers[i];
            if (m.IsPermanent) continue;
            m.Tick(Time.deltaTime);
            if (!m.IsExpired) continue;
            modifiers.RemoveAt(i);
            if (!changedScratch.Contains(m.Stat)) changedScratch.Add(m.Stat);
        }
        // Old value is approximated by the current one plus nothing — callers that care
        // about deltas subscribe to the pool events; StatChanged is a "refresh" signal here.
        foreach (var s in changedScratch)
            StatChanged?.Invoke(s, GetFinal(s), GetFinal(s));
    }

    void TickStamina()
    {
        if (!initialised || !HasStat(StatType.MaxStamina)) return;

        if (staminaRegenDelayTimer > 0f)
        {
            staminaRegenDelayTimer -= Time.deltaTime;
            return;
        }

        float max = MaxStamina;
        if (CurrentStamina >= max) return;

        float regen = HasStat(StatType.StaminaRegen) ? GetFinal(StatType.StaminaRegen) : 0f;
        if (regen <= 0f) return;

        CurrentStamina = Mathf.Min(max, CurrentStamina + regen * Time.deltaTime);
        if (IsExhausted && CurrentStamina >= max * 0.25f) IsExhausted = false;
        StaminaChanged?.Invoke();
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
                case ModifierType.Flat:            flat       += mod.Value;        break;
                case ModifierType.PercentAdd:      percentAdd += mod.Value;        break;
                case ModifierType.PercentMultiply: percentMul *= (1f + mod.Value); break;
            }
        }

        float result = (GetBase(stat) + flat) * (1f + percentAdd) * percentMul;

        if (statRanges.TryGetValue(stat, out var range))
        {
            result = Mathf.Max(result, range.min);
            if (range.max > 0f) result = Mathf.Min(result, range.max);
        }
        return result;
    }

    // Multiplier-style stats default to 1 when not authored, so callers can just multiply.
    public float GetMultiplier(StatType stat) => HasStat(stat) ? GetFinal(stat) : 1f;

    // ── Modifiers ─────────────────────────────────────────────────────

    public void AddModifier(StatModifier mod)
    {
        float old = GetFinal(mod.Stat);
        modifiers.Add(mod);
        OnStatChanged(mod.Stat, old);
    }

    public void RemoveModifier(StatModifier mod)
    {
        float old = GetFinal(mod.Stat);
        if (modifiers.Remove(mod)) OnStatChanged(mod.Stat, old);
    }

    public void RemoveAllFromSource(object source)
    {
        changedScratch.Clear();
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(modifiers[i].Source, source)) continue;
            var stat = modifiers[i].Stat;
            modifiers.RemoveAt(i);
            if (!changedScratch.Contains(stat)) changedScratch.Add(stat);
        }
        foreach (var s in changedScratch) OnStatChanged(s, GetFinal(s));
    }

    void OnStatChanged(StatType stat, float old)
    {
        float now = GetFinal(stat);
        StatChanged?.Invoke(stat, old, now);
        if (stat == StatType.MaxHealth)
        {
            CurrentHealth = Mathf.Min(CurrentHealth, now);
            HealthChanged?.Invoke();
        }
        else if (stat == StatType.MaxStamina)
        {
            CurrentStamina = Mathf.Min(CurrentStamina, now);
            StaminaChanged?.Invoke();
        }
    }

    // ── Damage ────────────────────────────────────────────────────────

    public void TakeDamage(DamageInfo info)
    {
        if (!IsAlive) return;

        if (blocker != null && blocker.TryBlock(ref info))
        {
            info.Blocked = true;
            float reduction = CombatManager.HasInstance ? CombatManager.Instance.blockDamageReduction : 1f;
            info.Amount *= 1f - reduction;
        }

        float defense = GetFinal(StatType.Defense);
        float actual  = Mathf.Max(0f, info.Amount - defense);
        info.Amount   = actual;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - actual);
        HealthChanged?.Invoke();
        Damaged?.Invoke(info);

        if (!IsAlive) Died?.Invoke();
    }

    public void TakeFlatDamage(float amount) => TakeDamage(DamageInfo.Simple(amount));

    // ── Healing ───────────────────────────────────────────────────────

    public void Heal(float amount)
    {
        if (!IsAlive) return;
        float healed = Mathf.Min(amount, MaxHealth - CurrentHealth);
        if (healed <= 0f) return;
        CurrentHealth += healed;
        HealthChanged?.Invoke();
        Healed?.Invoke(healed);
    }

    // ── Stamina ───────────────────────────────────────────────────────

    public bool HasStamina(float amount) => !HasStat(StatType.MaxStamina) || CurrentStamina >= amount;

    // Spend stamina. Returns false (and spends nothing) if there isn't enough.
    public bool TryUseStamina(float amount, float regenDelay = 0.6f)
    {
        if (!HasStat(StatType.MaxStamina)) return true;
        if (amount <= 0f) return true;
        if (CurrentStamina < amount) return false;
        CurrentStamina -= amount;
        staminaRegenDelayTimer = Mathf.Max(staminaRegenDelayTimer, regenDelay);
        if (CurrentStamina <= 0.001f) { CurrentStamina = 0f; IsExhausted = true; }
        StaminaChanged?.Invoke();
        return true;
    }

    // Continuous drain (sprinting): spends what it can and reports whether any was left.
    public bool DrainStamina(float perSecond, float regenDelay = 0.6f)
    {
        if (!HasStat(StatType.MaxStamina)) return true;
        float cost = perSecond * Time.deltaTime;
        if (CurrentStamina <= 0f) { IsExhausted = true; return false; }
        CurrentStamina = Mathf.Max(0f, CurrentStamina - cost);
        staminaRegenDelayTimer = Mathf.Max(staminaRegenDelayTimer, regenDelay);
        if (CurrentStamina <= 0f) IsExhausted = true;
        StaminaChanged?.Invoke();
        return true;
    }

    public void RestoreStamina(float amount)
    {
        if (!HasStat(StatType.MaxStamina)) return;
        CurrentStamina = Mathf.Min(MaxStamina, CurrentStamina + amount);
        if (CurrentStamina > 0f) IsExhausted = false;
        StaminaChanged?.Invoke();
    }

    // Allow sprinting again once stamina climbed back above this fraction after exhaustion.
    public bool CanSprint(float recoveryFraction = 0.25f)
    {
        if (!HasStat(StatType.MaxStamina)) return true;
        if (IsExhausted) return CurrentStamina >= MaxStamina * recoveryFraction;
        return CurrentStamina > 0f;
    }

    // Bring a dead character back at full health (respawn, debug).
    public void Revive()
    {
        CurrentHealth  = MaxHealth;
        CurrentStamina = MaxStamina;
        IsExhausted = false;
        HealthChanged?.Invoke();
        StaminaChanged?.Invoke();
    }

    // Reload base values (e.g. after swapping CharacterData at runtime).
    public void ReloadFromData()
    {
        LoadBase();
        CurrentHealth  = Mathf.Min(CurrentHealth, MaxHealth);
        CurrentStamina = Mathf.Min(CurrentStamina, MaxStamina);
        HealthChanged?.Invoke();
        StaminaChanged?.Invoke();
    }
}

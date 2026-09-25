using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Universal stat container — health, stamina and mana pools plus a modifier stack over the
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
    public event Action                         ManaChanged;

    // ── State ─────────────────────────────────────────────────────────
    [ShowInInspector, ReadOnly] public float CurrentHealth  { get; private set; }
    [ShowInInspector, ReadOnly] public float CurrentMana { get; private set; }
    public float MaxHealth  => GetFinal(StatType.MaxHealth);
    public float MaxMana => HasStat(StatType.MaxMana) ? GetFinal(StatType.MaxMana) : 0f;
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
    Equipment equipment;
    float manaRegenDelayTimer;
    float staminaRegenDelayTimer;
    public event Action StaminaChanged;
    public event Action StaminaUseDenied;
    public void ReportInsufficientStamina() => StaminaUseDenied?.Invoke();
    public float CurrentStamina { get; private set; }
    public float MaxStamina => GetFinal(StatType.MaxStamina);
    bool  initialised;

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake()
    {
        Character = GetComponent<Character>();
        blocker   = GetComponent<IBlocker>();
        equipment = GetComponent<Equipment>();
        LoadBase();
    }

    void Start()
    {
        // Pools initialise here so every modifier added during Awake (starting gear) counts.
        CurrentHealth  = MaxHealth;
        CurrentMana = MaxMana;
        CurrentStamina = MaxStamina;
        staminaRegenDelayTimer = 0;
        StaminaChanged?.Invoke();
        initialised = true;
        HealthChanged?.Invoke();
        ManaChanged?.Invoke();
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

    public float FoodRemaining { get; private set; }
    public float FoodHealingPerSecond { get; private set; }
    public void EatFood(float rate,float duration) {
        if(!IsAlive || rate<=0 || duration<=0)return;
        FoodHealingPerSecond=rate;FoodRemaining=duration;
    }
    public void ClearFood(){FoodRemaining=0;FoodHealingPerSecond=0;}
    void TickFood() {
        if(!IsAlive){ClearFood();return;}
        float dt=Mathf.Min(FoodRemaining,Time.deltaTime);
        if(dt<=0)return;
        FoodRemaining-=dt;Heal(FoodHealingPerSecond*dt);
    }
    void Update()
    {
        TickModifiers();
        TickMana();
        TickStamina();
        TickFood();
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

    void TickMana()
    {
        if (!initialised || !HasStat(StatType.MaxMana)) return;

        if (manaRegenDelayTimer > 0f)
        {
            manaRegenDelayTimer -= Time.deltaTime;
            return;
        }

        float max = MaxMana;
        if (CurrentMana >= max) return;

        float regen = HasStat(StatType.ManaRegen) ? GetFinal(StatType.ManaRegen) : 0f;
        if (regen <= 0f) return;

        CurrentMana = Mathf.Min(max, CurrentMana + regen * Time.deltaTime);

        ManaChanged?.Invoke();
    }

    // ── Queries ───────────────────────────────────────────────────────

    public bool  HasStat(StatType stat) => baseStats.ContainsKey(stat);
    public float GetBase(StatType stat) => baseStats.TryGetValue(stat, out var v) ? v : 0f;

    public float GetFinal(StatType stat) =>
        ResolveStat(stat, stat == StatType.Armor && blocker != null && blocker.IsGuarding);

    float ResolveStat(StatType stat, bool shieldArmor)
    {
        float flat = 0f, percentAdd = 0f, percentMul = 1f;

        foreach (var mod in modifiers)
        {
            if (mod.Stat != stat) continue;
            if (stat == StatType.Armor && !shieldArmor && equipment != null && equipment.IsShieldSource(mod.Source)) continue;
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
        else if (stat == StatType.MaxMana)
        {
            CurrentMana = Mathf.Min(CurrentMana, now);
            ManaChanged?.Invoke();
        }
    }

    // ── Damage ────────────────────────────────────────────────────────

    public void TakeDamage(DamageInfo info)
    {
        if (!IsAlive) return;
        info.Target = Character;

        if (blocker != null && blocker.TryBlock(ref info))
        {
            info.Blocked = true;

        }

        float armor = ResolveStat(StatType.Armor, info.Blocked);
        float actual  = Mathf.Max(0f, info.Amount - armor);
        if(info.Blocked)actual*=1f-Mathf.Clamp01(CombatManager.HasInstance?CombatManager.Instance.blockDamageReduction:.5f);
        info.Amount   = actual;

        CurrentHealth = Mathf.Max(0f, CurrentHealth - actual);
        HealthChanged?.Invoke();
        Damaged?.Invoke(info);

        if (!IsAlive) Died?.Invoke();
    }

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

    // ── Mana ───────────────────────────────────────────────────────

    public bool HasMana(float amount) => !HasStat(StatType.MaxMana) || CurrentMana >= amount;

    // Spend mana. Returns false (and spends nothing) if there isn't enough.
    public bool TryUseMana(float amount, float regenDelay = 0.6f)
    {
        if (!HasStat(StatType.MaxMana)) return true;
        if (amount <= 0f) return true;
        if (CurrentMana < amount) return false;
        CurrentMana -= amount;
        manaRegenDelayTimer = Mathf.Max(manaRegenDelayTimer, regenDelay);
        if (CurrentMana <= 0.001f) CurrentMana = 0f;
        ManaChanged?.Invoke();
        return true;
    }

    // Continuous drain (channelled abilities): spends what it can and reports whether any was left.
    public bool DrainMana(float perSecond, float regenDelay = 0.6f)
    {
        if (!HasStat(StatType.MaxMana)) return true;
        float cost = perSecond * Time.deltaTime;
        if (CurrentMana <= 0f) return false;
        CurrentMana = Mathf.Max(0f, CurrentMana - cost);
        manaRegenDelayTimer = Mathf.Max(manaRegenDelayTimer, regenDelay);

        ManaChanged?.Invoke();
        return true;
    }

    public void RestoreMana(float amount)
    {
        if (!HasStat(StatType.MaxMana)) return;
        CurrentMana = Mathf.Min(MaxMana, CurrentMana + amount);

        ManaChanged?.Invoke();
    }

    void TickStamina()
    {
        if (!initialised || !IsAlive || !HasStat(StatType.MaxStamina)) return;
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        if (staminaRegenDelayTimer > 0) { staminaRegenDelayTimer -= Time.deltaTime; return; }
        RestoreStamina(GetFinal(StatType.StaminaRegen) * Time.deltaTime);
    }

    public bool TryUseStamina(float amount, float regenDelay = .6f)
    {
        if (!HasStat(StatType.MaxStamina) || amount <= 0) return true;
        if (IsExhausted || CurrentStamina < amount) { ReportInsufficientStamina(); return false; }
        SpendStamina(amount, regenDelay);
        return true;
    }

    public bool DrainStamina(float perSecond, float regenDelay = .6f)
    {
        if (!HasStat(StatType.MaxStamina) || perSecond <= 0) return true;
        if (IsExhausted || CurrentStamina <= 0) { ReportInsufficientStamina(); return false; }
        SpendStamina(perSecond * Time.deltaTime, regenDelay);
        if (IsExhausted) ReportInsufficientStamina();
        return !IsExhausted;
    }

    void SpendStamina(float amount, float delay)
    {
        CurrentStamina = Mathf.Max(0, CurrentStamina - amount);
        staminaRegenDelayTimer = Mathf.Max(staminaRegenDelayTimer, delay);
        if (CurrentStamina <= .001f) { CurrentStamina = 0; IsExhausted = true; }
        StaminaChanged?.Invoke();
    }

    public void ExhaustStamina(float regenDelay = .6f) => SpendStamina(CurrentStamina, regenDelay);

    public void RestoreStamina(float amount)
    {
        if (amount <= 0 || !HasStat(StatType.MaxStamina)) return;
        float max = MaxStamina;
        if (CurrentStamina >= max && !IsExhausted) return;
        CurrentStamina = Mathf.Min(max, CurrentStamina + amount);
        float fraction = Character != null && Character.data != null ? Character.data.sprintRecoveryFraction : .25f;
        if (CurrentStamina >= max * Mathf.Clamp(fraction, .01f, 1f)) IsExhausted = false;
        StaminaChanged?.Invoke();
    }

    // Bring a dead character back at full health (respawn, debug).
    public void Revive()
    {
        ClearFood();
        CurrentHealth  = MaxHealth;
        CurrentMana = MaxMana;
        CurrentStamina = MaxStamina;
        staminaRegenDelayTimer = 0;
        StaminaChanged?.Invoke();
        IsExhausted = false;
        HealthChanged?.Invoke();
        ManaChanged?.Invoke();
    }

    // Reload base values (e.g. after swapping CharacterData at runtime).
    public void ReloadFromData()
    {
        LoadBase();
        CurrentHealth  = Mathf.Min(CurrentHealth, MaxHealth);
        CurrentMana = Mathf.Min(CurrentMana, MaxMana);
        CurrentStamina = Mathf.Min(CurrentStamina, MaxStamina);
        StaminaChanged?.Invoke();
        HealthChanged?.Invoke();
        ManaChanged?.Invoke();
    }
}

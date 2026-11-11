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
    public System.Collections.Generic.IReadOnlyList<StatModifier> Modifiers => modifiers;
    readonly List<StatType>                            changedScratch = new();
    readonly List<float>                               oldScratch     = new();   // value of changedScratch[i] before the change

    IBlocker blocker;
    AdventurerProgress adventurer;   // players only: attribute-derived stats and skill rules
    Equipment equipment;
    float manaRegenDelayTimer;
    float staminaRegenDelayTimer;
    public event Action StaminaChanged;
    public event Action StaminaUseDenied;
    public void ReportInsufficientStamina() => StaminaUseDenied?.Invoke();
    public float CurrentStamina { get; private set; }
    public float MaxStamina => GetFinal(StatType.MaxStamina);
    bool  initialised;
    // Pools are filled in Start: before that (a body switched off since it was put down) health reads 0 though it never died.
    public bool Initialised => initialised;

    // ── Lifecycle ─────────────────────────────────────────────────────

    void Awake()
    {
        Character = GetComponent<Character>();
        blocker   = GetComponent<IBlocker>();
        adventurer = GetComponent<AdventurerProgress>();
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
    public ItemData FoodSource { get; private set; }   // what was eaten, for the buff icon; null after a load
    public void EatFood(float rate, float duration, ItemData source = null)
    {
        if (!IsAlive || rate <= 0 || duration <= 0) return;
        FoodHealingPerSecond = rate;
        FoodRemaining = duration;
        FoodSource = source;
    }

    public void ClearFood()
    {
        FoodRemaining = 0;
        FoodHealingPerSecond = 0;
        FoodSource = null;
    }

    void TickFood()
    {
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        if (!IsAlive) { ClearFood(); return; }
        float dt = Mathf.Min(FoodRemaining, Time.deltaTime);
        if (dt <= 0) return;
        FoodRemaining -= dt;
        Heal(FoodHealingPerSecond * dt);
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
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
        changedScratch.Clear();
        oldScratch.Clear();
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            var m = modifiers[i];
            if (m.IsPermanent) continue;
            m.Tick(Time.deltaTime);
            if (!m.IsExpired) continue;
            RememberOld(m.Stat);
            modifiers.RemoveAt(i);
        }
        // Same path as any other modifier change: pools clamp to the new max and their events fire.
        for (int i = 0; i < changedScratch.Count; i++) OnStatChanged(changedScratch[i], oldScratch[i]);
    }

    // Records a stat's value before the first of its modifiers is removed (for StatChanged's old value).
    void RememberOld(StatType stat)
    {
        if (changedScratch.Contains(stat)) return;
        changedScratch.Add(stat);
        oldScratch.Add(GetFinal(stat));
    }

    void TickMana()
    {
        if (GameManager.HasInstance && !GameManager.Instance.SimulationActive) return;
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

    // A shield adds no armour (since 2026-11-11 it blocks a flat amount, Block): armour on a shield, rolled before then,
    // is left out everywhere.
    public float GetFinal(StatType stat) => ResolveStat(stat, false);

    // What a raised shield takes off a blocked physical hit (the shield's Block; CharacterStats.AfterBlock).
    public float Block => Mathf.Max(0f, ResolveStat(StatType.Block, false));
    public float ArmorWithoutShield => ResolveStat(StatType.Armor, false);

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

        float derived = 0;
        var rules = adventurer != null ? adventurer.rules : null;
        if (rules != null)
        {
            if (stat == StatType.AttackDamage) derived = ResolveStat(StatType.Strength, false) * rules.strengthAttack;
            else if (stat == StatType.Armor) derived = ResolveStat(StatType.Constitution, false) * rules.constitutionArmor;
            else if (stat == StatType.MaxHealth) derived = ResolveStat(StatType.Constitution, false) * rules.constitutionHealth;
            else if (stat == StatType.MaxMana) derived = ResolveStat(StatType.Intelligence, false) * rules.intelligenceMana;
            else if (stat == StatType.MoveSpeed || stat == StatType.AttackSpeed) derived = Mathf.Clamp(ResolveStat(StatType.Dexterity, false) * rules.dexteritySpeed, -.2f, .3f);
        }
        float result = (GetBase(stat) + flat + derived) * (1f + percentAdd) * percentMul;

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
        oldScratch.Clear();
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(modifiers[i].Source, source)) continue;
            RememberOld(modifiers[i].Stat);
            modifiers.RemoveAt(i);
        }
        for (int i = 0; i < changedScratch.Count; i++) OnStatChanged(changedScratch[i], oldScratch[i]);
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

    // Who this returns true for still takes every hit (blocks, hurt screen, knockback, hit reactions, sounds) but loses no
    // health, so can't die (god mode, DevGodMode). Null in normal play.
    public static System.Func<CharacterStats, bool> Invulnerable;
    // Until this time (Time.time) hits take no health either, as with Invulnerable: the grace after a revive
    // (RunManager.reviveGrace).
    public float SparedUntil { get; set; }

    // Any hit that takes health kills outright (a boss's backup dancers, BossFight.backupOneHit).
    public bool DiesInOneHit { get; set; }

    // The boss arena's blessing (BossBlessing): a successful block stops all of a hit's damage, spells included.
    public bool BlockBlessing { get; set; }

    // What a hit leaves once the target's armour has had its share (armour: the target's Armor stat). The player's armour
    // against an unblocked physical hit takes a share off it that depends on the floor (CombatManager.Armor.cs); otherwise
    // armour comes off the effective share of the hit and the rest always lands, and magic ignores it. A blocked physical
    // hit does not come here: the shield takes its Block off it as a flat amount (AfterBlock). TakeDamage takes every hit
    // through these; Asset Studio's Balance page reckons fights with them.
    public static float AfterArmor(float amount, float armour, bool player, bool blocked, bool magic, int floor)
    {
        if (!magic && !blocked && player && CombatManager.ScaledPlayerArmor) return amount * (1f - CombatManager.ArmorCut(armour, floor));
        float stop = magic ? 0 : armour * CombatManager.ArmorPerPoint;
        float effective = blocked ? 1f : CombatManager.HasInstance ? CombatManager.Instance.armorEffectiveness : .65f;
        return Mathf.Max(0f, effective * amount - stop) + (1f - effective) * amount;
    }

    // A blocked physical hit (the user, 2026-11-11: a shield lowers a hit by a flat amount instead of adding armour): the
    // shield's Block comes off it, armour plays no part, and the Blocking skill takes its share off what is left
    // (blockedMultiplier, AdventurerProgress.BlockedDamageMultiplier). Shields are set so a shield of a floor's usual tier
    // stops 85% of that floor's average enemy hit (AssetStudioWindow.SetShieldBlocks).
    public static float AfterBlock(float amount, float block, float blockedMultiplier = 1f) => Mathf.Max(0f, amount - Mathf.Max(0f, block)) * Mathf.Clamp01(blockedMultiplier);

    public void TakeDamage(DamageInfo info)
    {
        if (!IsAlive) return;
        bool spared = Invulnerable != null && Invulnerable(this) || Time.time < SparedUntil;
        info.Target = Character;
        info.Spared = spared;

        bool blessed = BlockBlessing;
        if ((!info.Magic || blessed) && blocker != null && blocker.TryBlock(ref info))
        {
            info.Blocked = true;

        }

        // Blocked: the shield's Block off the hit, then Blocking's share off what is left. Otherwise armour has its share.
        float actual = info.Blocked && !info.Magic
            ? AfterBlock(info.Amount, Block, adventurer != null ? adventurer.BlockedDamageMultiplier : 1f)
            : AfterArmor(info.Amount, ResolveStat(StatType.Armor, false), Character is Player, info.Blocked, info.Magic, CombatManager.CurrentFloor);
        // Legendary Blocking: nothing gets through a successful block.
        if (info.Blocked && adventurer != null && adventurer.BlockAbsorbsAll) actual = 0;
        // The boss arena's blessing: the same, for every blocked hit, spells included.
        if (info.Blocked && blessed) actual = 0;
        // Every hit is a whole number (the user, 2026-11-11), to the nearest, halves up; a tick of an effect (a burn) that
        // does anything does at least 1, so a small one is not rounded away.
        float exact = actual;
        actual = Mathf.Floor(actual + .5f);
        if (info.FromEffect && exact > 0f && actual < 1f) actual = 1f;
        if (DiesInOneHit && actual > 0f) actual = Mathf.Max(actual, CurrentHealth);
        // The cheats' 999 damage attacks: every hit the table player lands on someone else does 999.
        if (DevCheats.MightyBlows && info.Source is Player && Character is not Player) actual = Mathf.Max(actual, DevCheats.MightyDamage);
        info.Amount   = actual;

        if (!spared) CurrentHealth = Mathf.Max(0f, CurrentHealth - actual);
        HealthChanged?.Invoke();
        Damaged?.Invoke(info);

        if (!IsAlive)
        {
            BlockBlessing = false;
            Died?.Invoke();
            // Gear that heals on a kill (KillHeal).
            var killer = info.Source != null && info.Source != Character ? info.Source.Stats : null;
            if (killer != null && killer.IsAlive) { float heal = killer.GetFinal(StatType.KillHeal); if (heal > 0f) killer.Heal(heal); }
        }
    }

    // ── Healing ───────────────────────────────────────────────────────

    // Health set to a share of the most (a boss's backup dancers come out weaker than their kind).
    public void SetHealthShare(float share)
    {
        if (!IsAlive) return;
        CurrentHealth = Mathf.Clamp(MaxHealth * share, 1f, MaxHealth);
        HealthChanged?.Invoke();
    }

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
        float fraction = Character != null && Character.data is PlayerData player ? player.sprintRecoveryFraction : .25f;
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
        // The death pose flag would otherwise keep the animator in its death state.
        if (Character != null) Character.ClearDeadFlag();
        HealthChanged?.Invoke();
        ManaChanged?.Invoke();
    }

    public void RestorePools(float health, float mana, float stamina)
    {
        CurrentHealth = Mathf.Clamp(health, 0, MaxHealth);
        CurrentMana = Mathf.Clamp(mana, 0, MaxMana);
        CurrentStamina = Mathf.Clamp(stamina, 0, MaxStamina);
        IsExhausted = CurrentStamina <= 0;
        HealthChanged?.Invoke();
        ManaChanged?.Invoke();
        StaminaChanged?.Invoke();
    }
}

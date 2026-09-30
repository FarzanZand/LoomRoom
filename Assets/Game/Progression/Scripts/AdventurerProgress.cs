using System;
using System.Collections.Generic;
using UnityEngine;

// Character and skill levels advance through experience earned during the run.
// Everything resets when a new run begins. Other systems read the bonuses through the helpers below.
[DisallowMultipleComponent]
public class AdventurerProgress : MonoBehaviour
{
    public AdventureRules rules;
    public AdventurerClass selectedClass;
    [Header("Visibility")]
    public Light perceptionLight;
    public float baseVisibilityRange = 6;

    public int Level { get; private set; } = 1;
    public float Experience { get; private set; }
    public int[] Ranks { get; private set; } = new int[AdventureSkills.Count];
    public float[] SkillExperience { get; private set; } = new float[AdventureSkills.Count];
    // Skill, awarded XP, previous fraction, resulting fraction, ranks gained.
    public event Action<AdventureSkill, float, float, float, int> SkillExperienceGained;
    public float SkillProgress(AdventureSkill skill) => Rank(skill) >= 100 ? 1 : SkillExperience[(int)skill] / (Definition(skill)?.ExperienceRequired(Rank(skill)) ?? 100);
    public int[] Growth { get; private set; } = new int[6];
    public float NextLevelXp => rules != null ? rules.levelXp + (Level - 1) * rules.levelXpGrowth : 100;
    public bool InRun => RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended;

    public event Action Changed;
    // Raised whenever a skill gains a point: (skill, new rank).
    public event Action<AdventureSkill, int> SkillRaised;
    public event Action<float> ExperienceGained;

    Player player;
    readonly object source = new object();
    readonly Dictionary<(Character, AdventureSkill), int> taught = new();
    readonly HashSet<Vector2Int> visited = new();
    Vector3 lastPosition;
    float sprintDistance, healCredit;

    void Awake() { player = GetComponent<Player>(); lastPosition = transform.position; }
    void OnEnable() { player.HitLanded += OnHit; player.Damaged += OnHurt; }
    void OnDisable() { player.HitLanded -= OnHit; player.Damaged -= OnHurt; }

    // ── Reading skills ─────────────────────────────────────────────────────────
    public AdventureSkillData Definition(AdventureSkill skill) => rules != null && rules.skills != null ? Array.Find(rules.skills, x => x != null && x.skill == skill) : null;
    public int Rank(AdventureSkill skill) => Ranks[(int)skill];
    public bool Legendary(AdventureSkill skill) => Ranks[(int)skill] >= 100;
    // The continuous bonus at the current rank (0.25 = +25%).
    public float Bonus(AdventureSkill skill) => Ranks[(int)skill] * .01f * (Definition(skill)?.bonusAt100 ?? 0);

    float Attribute(StatType stat) => player != null && player.Stats != null ? player.Stats.GetFinal(stat) : 0;

    // Melee: the weapon's skill adds damage; Legendary adds flat Attack, and Swords hit harder when fully charged.
    public float MeleeDamage(float damage, ItemData weapon, bool heavy)
    {
        var skill = AdventureSkills.ForWeapon(weapon);
        damage *= 1 + Bonus(skill);
        if (Legendary(skill)) damage += rules != null ? rules.legendaryAttack : 5;
        if (heavy && skill == AdventureSkill.Swords && Legendary(skill)) damage *= 1.25f;
        return damage;
    }
    // Legendary Maces: fully charged strikes stagger for twice as long.
    public float StaggerMultiplier(ItemData weapon) => AdventureSkills.ForWeapon(weapon) == AdventureSkill.Maces && Legendary(AdventureSkill.Maces) ? 2 : 1;

    public float BlockCost => Mathf.Max(.2f, 1 - Bonus(AdventureSkill.Blocking));
    // Legendary Blocking: a successful block takes no damage.
    public bool BlockAbsorbsAll => Legendary(AdventureSkill.Blocking);
    public float SprintCost => Mathf.Max(.2f, 1 - Bonus(AdventureSkill.Athletics));

    public float SpellPower(AdventureSkill school)
    {
        float intelligence = Mathf.Max(0, Attribute(StatType.Intelligence)) * (rules != null ? rules.intelligenceSpell : .035f);
        // Thaumaturgy draws on Constitution as well, as in Barony.
        if (school == AdventureSkill.Thaumaturgy) intelligence += Mathf.Max(0, Attribute(StatType.Constitution)) * (rules != null ? rules.intelligenceSpell * .5f : .0175f);
        return (1 + Bonus(school)) * (1 + intelligence * (Legendary(school) ? 1.4f : 1));
    }
    public float SpellCastTime(AdventureSkill school) => Legendary(school) ? .5f : 1 - .3f * Ranks[(int)school] * .01f;

    public float BuyPriceMultiplier => Mathf.Max(.5f, 1 - Bonus(AdventureSkill.Trading) - Charisma - (Legendary(AdventureSkill.Trading) ? .1f : 0));
    public float SellPriceMultiplier => 1 + Bonus(AdventureSkill.Trading) * 1.5f + Charisma;
    float Charisma => rules != null ? Mathf.Clamp(Attribute(StatType.Charisma) * rules.charismaDiscountPerPoint, 0, rules.charismaDiscountCap) : 0;

    // ── Practising skills ──────────────────────────────────────────────────────
    // Every eligible action earns XP. Returns true if at least one rank was gained.
    public bool Practise(AdventureSkill skill, SkillAction action, Character teacher = null)
    {
        if (!InRun) return false;
        int i = (int)skill;
        if (Ranks[i] >= 100) return false;
        var def = Definition(skill); if (def == null) return false;
        float award = def.ExperienceFor(action);
        if (award <= 0 || float.IsNaN(award) || float.IsInfinity(award)) return false;
        taught.TryGetValue((teacher, skill), out int given);
        if (teacher != null && def.perEnemyLimit > 0)
        {
            if (given >= def.perEnemyLimit) return false;
            float capacity = -SkillExperience[i];
            for (int r = Ranks[i]; r < Mathf.Min(100, Ranks[i] + def.perEnemyLimit - given); r++) capacity += def.ExperienceRequired(r);
            award = Mathf.Min(award, capacity);
        }
        float before = SkillProgress(skill);
        int oldRank = Ranks[i];
        SkillExperience[i] += award;
        while (Ranks[i] < 100 && SkillExperience[i] >= def.ExperienceRequired(Ranks[i]))
        {
            SkillExperience[i] -= def.ExperienceRequired(Ranks[i]);
            Ranks[i]++;
            if (teacher != null) taught[(teacher, skill)] = ++given;
            string tierBefore = AdventureSkills.Tier(Ranks[i] - 1), tier = AdventureSkills.Tier(Ranks[i]);
            if (tier != tierBefore) AnnouncementUI.Show($"{def.displayName}: {tier}", Ranks[i] >= 100 ? def.legendaryText : null);
            SkillRaised?.Invoke(skill, Ranks[i]);
        }
        if (Ranks[i] >= 100) SkillExperience[i] = 0;
        bool raised = Ranks[i] > oldRank;
        if (raised)
        {
            if (AudioManager.HasInstance) AudioManager.Instance.PlaySkillUp();
            ApplyStats();
        }
        SkillExperienceGained?.Invoke(skill, award, before, SkillProgress(skill), Ranks[i] - oldRank);
        Changed?.Invoke();
        return raised;
    }

    void OnHit(DamageInfo hit)
    {
        if (hit.FromEffect || hit.Magic || hit.Target == null || !FactionRules.IsHostile(player.Faction, hit.Target.Faction)) return;
        if (hit.Amount <= 0 && !hit.Blocked) return;
        Practise(AdventureSkills.ForWeapon(hit.Weapon), hit.Target.IsAlive ? SkillAction.Hit : SkillAction.Kill, hit.Target);
        if (hit.Backstab) Practise(AdventureSkill.Stealth, SkillAction.Backstab, hit.Target);
    }

    void OnHurt(DamageInfo hit)
    {
        if (hit.Source == null || hit.Source == player || !FactionRules.IsHostile(player.Faction, hit.Source.Faction)) return;
        // Only health lost to enemies can train healing, so hurting yourself to practise doesn't work.
        healCredit = Mathf.Min(player.Stats.MaxHealth, healCredit + Mathf.Max(0, hit.Amount));
        if (hit.Blocked) Practise(AdventureSkill.Blocking, SkillAction.Block, hit.Source);
    }

    // A heal practises Thaumaturgy only if it restored health an enemy took.
    public void PractiseHeal(float restored)
    {
        float eligible = Mathf.Min(healCredit, Mathf.Max(0, restored));
        healCredit -= eligible;
        if (eligible >= 1) Practise(AdventureSkill.Thaumaturgy, SkillAction.Heal);
    }

    // ── Character level ────────────────────────────────────────────────────────
    public bool SelectClass(AdventurerClass value)
    {
        if (InRun || value == null || !value.Unlocked) return false;
        selectedClass = value; Changed?.Invoke(); return true;
    }

    public void Begin()
    {
        if (rules == null || rules.classes == null || rules.classes.Length == 0) return;
        if (selectedClass == null || !selectedClass.Unlocked) selectedClass = rules.classes[0];
        Level = 1; Experience = 0; Growth = new int[6];
        SkillExperience = new float[AdventureSkills.Count];
        Ranks = new int[AdventureSkills.Count];
        foreach (var skill in AdventureSkills.All) Ranks[(int)skill] = selectedClass.StartingRank(skill);
        taught.Clear(); visited.Clear(); sprintDistance = healCredit = 0; lastPosition = transform.position;
        player.Equipment.UnequipAll(); player.Bag.Clear(); player.Hotbar.Clear();
        ApplyStats();
        foreach (var item in selectedClass.equipment)
        {
            if (item == null) continue;
            bool hand = item.equipSlot == EquipmentSlot.RightHand || item.equipSlot == EquipmentSlot.LeftHand;
            if ((hand ? player.Hotbar : player.Bag).TryAdd(item)) player.Equipment.Equip(item, false);
        }
        if (selectedClass.spells != null) foreach (var spell in selectedClass.spells) if (spell != null) player.Hotbar.TryAdd(spell);
        foreach (var stack in selectedClass.supplies) if (stack != null && stack.item != null) player.Bag.TryAdd(stack.item, stack.count);
        player.Stats.Revive();
        GetComponent<PlayerSpellcasting>()?.ResetHand(selectedClass.leftHand);
        Changed?.Invoke();
    }

    public void ApplyStats()
    {
        if (player == null) player = GetComponent<Player>();
        if (selectedClass == null || rules == null) return;
        var stats = player.Stats;
        float health = stats.CurrentHealth, mana = stats.CurrentMana, stamina = stats.CurrentStamina;
        stats.RemoveAllFromSource(source);
        void Add(StatType stat, float value, ModifierType kind = ModifierType.Flat) => stats.AddModifier(new StatModifier(stat, value, kind, source));
        for (int i = 0; i < 6; i++) Add(StatType.Strength + i, (i < selectedClass.attributes.Length ? selectedClass.attributes[i] : 0) + Growth[i]);
        Add(StatType.MaxHealth, selectedClass.health - stats.GetBase(StatType.MaxHealth) + (Level - 1) * rules.healthPerLevel);
        Add(StatType.MaxMana, selectedClass.mana - stats.GetBase(StatType.MaxMana) + (Level - 1) * rules.manaPerLevel);
        // Legendary Athletics: a deeper well of stamina.
        Add(StatType.MaxStamina, selectedClass.stamina - stats.GetBase(StatType.MaxStamina) + (Legendary(AdventureSkill.Athletics) ? rules.legendaryStamina : 0));
        stats.RestorePools(health, mana, stamina);
        Changed?.Invoke();
    }

    public void GainExperience(float amount)
    {
        if (!InRun || amount <= 0 || rules == null) return;
        Experience += amount;
        ExperienceGained?.Invoke(amount);
        while (Experience >= NextLevelXp && Level < 100)
        {
            Experience -= NextLevelXp; Level++;
            var growth = selectedClass.growth;
            int raised = growth != null && growth.Length > 0 ? Mathf.Clamp(growth[(Level - 2) % growth.Length], 0, 5) : -1;
            if (raised >= 0) Growth[raised]++;
            ApplyStats();
            AnnouncementUI.Show($"Level {Level}", raised >= 0 ? $"+1 {StatNames[raised]}" : null);
            if (AudioManager.HasInstance) AudioManager.Instance.PlayLevelUp();
            MessageLog.Post($"You are now level {Level}.", MessageKind.Good);
        }
        Changed?.Invoke();
    }

    public static readonly string[] StatNames = { "STR", "DEX", "CON", "INT", "PER", "CHR" };

    public void ReachedFloor()
    {
        visited.Clear(); sprintDistance = 0; lastPosition = transform.position;
        GainExperience(rules != null ? rules.xpPerFloor : 0);
    }

    void Update()
    {
        if (perceptionLight != null && rules != null)
        {
            perceptionLight.enabled = player.IsActive && InRun;
            perceptionLight.range = baseVisibilityRange * (1 + Mathf.Clamp(Attribute(StatType.Perception) * rules.perceptionRangePerPoint, 0, rules.perceptionRangeCap));
        }
        var position = transform.position; float moved = Vector3.Distance(position, lastPosition); lastPosition = position;
        if (!InRun || !player.IsActive || rules == null || !GameManager.HasInstance || !GameManager.Instance.GameplayActive) return;
        PractiseSneaking();
        if (moved > 3 || moved < .001f || player.Motor == null || !player.Motor.IsSprinting) return;
        // Athletics: sprinting across ground you haven't covered yet this floor.
        var cell = new Vector2Int(Mathf.FloorToInt(position.x / 2), Mathf.FloorToInt(position.z / 2));
        if (!visited.Add(cell)) return;
        sprintDistance += 2;
        if (sprintDistance < rules.athleticsMetres) return;
        sprintDistance -= rules.athleticsMetres;
        Practise(AdventureSkill.Athletics, SkillAction.Sprint);
    }

    // Stealth: crouching close to an enemy that hasn't noticed you. Each enemy teaches a few points at most.
    float nextSneakCheck;
    void PractiseSneaking()
    {
        if (Time.time < nextSneakCheck || player.Motor == null || !player.Motor.IsCrouching) return;
        nextSneakCheck = Time.time + 1f;
        foreach (var brain in EnemyBrain.Active)
        {
            if (brain == null || brain.Character == null || !brain.Character.IsAlive || brain.IsAlerted) continue;
            if (brain.Perception != null && brain.Perception.TargetVisible) continue;
            if ((brain.transform.position - transform.position).sqrMagnitude > rules.sneakPractiseRange * rules.sneakPractiseRange) continue;
            var definition = Definition(AdventureSkill.Stealth);
            if (definition != null && definition.perEnemyLimit > 0 &&
                taught.TryGetValue((brain.Character, AdventureSkill.Stealth), out int given) && given >= definition.perEnemyLimit) continue;
            Practise(AdventureSkill.Stealth, SkillAction.Sneak, brain.Character);
            return;
        }
    }

    public void Restore(int level, float xp, int[] ranks, int[] growth, float[] skillExperience = null)
    {
        Level = Mathf.Clamp(level, 1, 100); Experience = Mathf.Max(0, xp);
        Ranks = ranks != null && ranks.Length == AdventureSkills.Count ? ranks : new int[AdventureSkills.Count];
        SkillExperience = new float[AdventureSkills.Count];
        for (int i = 0; i < Ranks.Length; i++)
        {
            Ranks[i] = Mathf.Clamp(Ranks[i], 0, 100);
            float value = skillExperience != null && i < skillExperience.Length ? skillExperience[i] : 0;
            SkillExperience[i] = Ranks[i] >= 100 || float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp(value, 0, (Definition((AdventureSkill)i)?.ExperienceRequired(Ranks[i]) ?? 100) - .001f);
        }
        taught.Clear();
        Growth = growth != null && growth.Length == 6 ? growth : new int[6];
        ApplyStats();
    }
}

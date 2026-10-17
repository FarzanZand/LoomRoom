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
    public float SkillProgress(AdventureSkill skill) => Ranks[(int)skill] >= 100 ? 1 : SkillExperience[(int)skill] / (Definition(skill)?.ExperienceRequired(Ranks[(int)skill]) ?? 100);
    public int[] Growth { get; private set; } = new int[StatNames.Length];
    // Attribute points put in this run, by MetaAttribute, on top of the character sheet's scores; and the points from
    // levels not put anywhere yet (one per level, spent on the character panel).
    public int[] AttributePoints { get; private set; } = new int[AttributeCount];
    public int UnspentPoints { get; private set; }
    public const int AttributeCount = 4;
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
    // The rank with the gear's skill bonus (+3 Swords on gloves); gear stops at 99, only training reaches Legendary (100).
    public int Rank(AdventureSkill skill) => Ranks[(int)skill] >= 100 ? 100 : Mathf.Min(99, Ranks[(int)skill] + GearRanks(skill));
    public bool Legendary(AdventureSkill skill) => Rank(skill) >= 100;
    // The continuous bonus at the current rank (0.25 = +25%).
    public float Bonus(AdventureSkill skill) => Rank(skill) * .01f * (Definition(skill)?.bonusAt100 ?? 0);
    // The skill's second bonus (cast time, blocked damage, backstabs, selling, max stamina).
    public float Extra(AdventureSkill skill) => Definition(skill)?.ExtraAt(Rank(skill)) ?? 0;
    // The skill's first milestone (Shield bash for Blocking, the 25 for the others): learned for this run.
    public bool FirstMilestone(AdventureSkill skill) => ProgressionManager.HasInstance && ProgressionManager.Instance.Unlocked(skill, 0, Rank(skill));
    int GearRanks(AdventureSkill skill)
    {
        StatType? stat = skill switch { AdventureSkill.Swords => StatType.SwordsSkill, AdventureSkill.Maces => StatType.MacesSkill, AdventureSkill.Blocking => StatType.BlockingSkill, _ => null };
        return stat.HasValue && player != null && player.Stats != null ? Mathf.Max(0, Mathf.RoundToInt(player.Stats.GetFinal(stat.Value))) : 0;
    }

    float Attribute(StatType stat) => player != null && player.Stats != null ? player.Stats.GetFinal(stat) : 0;

    // Melee: the weapon's skill adds damage; Legendary adds flat Attack, and Swords hit harder when fully charged.
    public float MeleeDamage(float damage, ItemData weapon, bool heavy)
    {
        var skill = AdventureSkills.ForWeapon(weapon);
        damage *= 1 + Bonus(skill);
        if (Legendary(skill)) damage += rules != null ? rules.legendaryAttack : 5;
        if (heavy && skill == AdventureSkill.Swords && Legendary(skill)) damage *= 1.25f;
        // Riposte (Swords 25): the first sword swing after a block hits half as hard again.
        if (skill == AdventureSkill.Swords && player != null && player.Combat != null && FirstMilestone(AdventureSkill.Swords))
        {
            float blocked = player.Combat.LastBlockTime;
            if (blocked > riposteUsed && Time.time - blocked <= RiposteWindow) { damage *= 1.5f; riposteUsed = blocked; }
        }
        return damage;
    }
    // Legendary Maces: fully charged strikes stagger for twice as long.
    public float StaggerMultiplier(ItemData weapon) => AdventureSkills.ForWeapon(weapon) == AdventureSkill.Maces && Legendary(AdventureSkill.Maces) ? 2 : 1;

    public float BlockCost => Mathf.Max(.2f, 1 - Bonus(AdventureSkill.Blocking));
    // Blocking's second bonus: less of a blocked hit gets through.
    public float BlockedDamageMultiplier => Mathf.Clamp01(1 - Extra(AdventureSkill.Blocking));
    // Sprint (Athletics 25): this run can sprint, though the table player can't otherwise.
    public bool SprintUnlocked => FirstMilestone(AdventureSkill.Athletics);
    const float RiposteWindow = 1.5f;
    float riposteUsed = -1;
    // Legendary Blocking: a successful block takes no damage.
    public bool BlockAbsorbsAll => Legendary(AdventureSkill.Blocking);
    public float SprintCost => 1f;

    public float SpellPower(AdventureSkill school)
    {
        // Intelligence points above the base (the sheet's and this run's) raise every spell; Thaumaturgy draws on
        // Constitution as well, at half.
        // The per-point share is the Intelligence track's third gain (shown on the sheet and panel), else the rules'.
        var intTrack = ProgressionManager.HasInstance ? ProgressionManager.Instance.Track(MetaAttribute.Intelligence) : null;
        float perPoint = intTrack != null && intTrack.thirdBonusPerPoint > 0 ? intTrack.thirdBonusPerPoint * .01f : rules != null ? rules.intelligenceSpell : .035f;
        float intelligence = Mathf.Max(0, PointsAbove(MetaAttribute.Intelligence)) * perPoint;
        if (school == AdventureSkill.Thaumaturgy) intelligence += Mathf.Max(0, PointsAbove(MetaAttribute.Constitution)) * perPoint * .5f;
        return (1 + Bonus(school)) * (1 + intelligence * (Legendary(school) ? 1.4f : 1));
    }
    public float SpellCastTime(AdventureSkill school) => Legendary(school) ? .5f : Mathf.Max(.5f, 1 - Extra(school));

    public float BuyPriceMultiplier => Mathf.Max(.5f, 1 - Bonus(AdventureSkill.Trading) - Charisma - (Legendary(AdventureSkill.Trading) ? .1f : 0));
    public float SellPriceMultiplier => 1 + Extra(AdventureSkill.Trading) + Charisma;
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
        if (hit.Heavy && hit.Target.IsAlive && AdventureSkills.ForWeapon(hit.Weapon) == AdventureSkill.Maces && FirstMilestone(AdventureSkill.Maces)) Crush(hit.Target);
        if (hit.Backstab) Practise(AdventureSkill.Stealth, SkillAction.Backstab, hit.Target);
    }

    // Crush (Maces 25): a heavy mace hit knocks up to CrushArmor off the target's armour for the rest of the fight, never
    // below none, up to CrushStacks times.
    const float CrushArmor = 10f;
    const int CrushStacks = 5;
    static readonly System.Collections.Generic.Dictionary<Character, int> crushed = new();
    // Emptied at every run start and floor change (the enemies it counted are gone) and between Play sessions.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetCrushed() => crushed.Clear();
    static readonly object crushSource = new();
    static void Crush(Character target)
    {
        if (target.Stats == null) return;
        crushed.TryGetValue(target, out int stacks);
        if (stacks >= CrushStacks) return;
        float off = Mathf.Min(CrushArmor, target.Stats.GetFinal(StatType.Armor));
        crushed[target] = stacks + 1;
        if (off > 0f) target.Stats.AddModifier(new StatModifier(StatType.Armor, -off, ModifierType.Flat, crushSource));
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
        Level = 1; Experience = 0; Growth = new int[StatNames.Length]; AttributePoints = new int[AttributeCount]; UnspentPoints = 0;
        SkillExperience = new float[AdventureSkills.Count];
        Ranks = new int[AdventureSkills.Count];
        // A run starts from the base bought on the character sheet (ProgressionManager) plus the class.
        bool meta = ProgressionManager.HasInstance && ProgressionManager.MetaActive;
        foreach (var skill in AdventureSkills.All) Ranks[(int)skill] = Mathf.Clamp((meta ? ProgressionManager.Instance.BaseRank(skill) : 0) + selectedClass.StartingRank(skill), 0, 100);
        taught.Clear(); crushed.Clear(); visited.Clear(); sprintDistance = healCredit = 0; lastPosition = transform.position;
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
        // The classes' old six attributes (AdventurerClass.attributes) are retired: nothing adds them, so the stats they
        // fed (CharacterStats' derived damage, armour, speed; Charisma prices, Perception light) stay at their own values.
        Add(StatType.MaxHealth, selectedClass.health - stats.GetBase(StatType.MaxHealth) + 0);
        Add(StatType.MaxMana, selectedClass.mana - stats.GetBase(StatType.MaxMana) + 0);
        // Legendary Athletics: a deeper well of stamina.
        Add(StatType.MaxStamina, selectedClass.stamina - stats.GetBase(StatType.MaxStamina) + (Legendary(AdventureSkill.Athletics) ? rules.legendaryStamina : 0));
        // Athletics: faster on your feet, and a little more stamina every rank.
        if (Bonus(AdventureSkill.Athletics) > 0) Add(StatType.MoveSpeed, Bonus(AdventureSkill.Athletics), ModifierType.PercentAdd);
        if (Extra(AdventureSkill.Athletics) > 0) Add(StatType.MaxStamina, Extra(AdventureSkill.Athletics));
        // Strength, Dexterity, Constitution and Intelligence: the sheet's score plus this run's points; each point above
        // the base gives what its track says (ProgressionManager.AttributeGains).
        if (ProgressionManager.HasInstance)
        {
            var meta = ProgressionManager.Instance;
            for (int a = 0; a < AttributeCount; a++)
            {
                var track = meta.Track((MetaAttribute)a);
                if (track == null) continue;
                foreach (var (stat, amount, kind) in ProgressionManager.AttributeGains(track, Score((MetaAttribute)a) - track.baseScore)) Add(stat, amount, kind);
            }
        }
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
            // Each level gives one attribute point, put where the player likes on the character panel.
            UnspentPoints++;
            if (AudioManager.HasInstance) AudioManager.Instance.PlayLevelUp();
            // The level-up panel (LeveledUp) says it; the feed doesn't say it again.
            var gains = new System.Collections.Generic.List<string> { "+1 attribute point" };
            LeveledUp?.Invoke(Level, gains);
        }
        Changed?.Invoke();
    }

    // An attribute's score now: the character sheet's plus the points put in this run.
    public int Score(MetaAttribute attribute)
    {
        int sheet = ProgressionManager.HasInstance ? ProgressionManager.Instance.AttributeScore(attribute) : 8;
        int i = (int)attribute;
        return sheet + (i >= 0 && i < AttributePoints.Length ? AttributePoints[i] : 0);
    }

    // An attribute's points above its track's base score (what its bonuses count).
    public int PointsAbove(MetaAttribute attribute)
    {
        var track = ProgressionManager.HasInstance ? ProgressionManager.Instance.Track(attribute) : null;
        return Score(attribute) - (track != null ? track.baseScore : 8);
    }

    // Puts one of the level's points into an attribute; false when there is none to put.
    public bool SpendPoint(MetaAttribute attribute)
    {
        int i = (int)attribute;
        if (!InRun || UnspentPoints <= 0 || i < 0 || i >= AttributePoints.Length) return false;
        UnspentPoints--; AttributePoints[i]++;
        ApplyStats();
        if (AudioManager.HasInstance) AudioManager.Instance.PlaySkillUp();
        return true;
    }

    public static readonly string[] StatNames = { "STR", "DEX", "CON", "INT", "PER", "CHR" };
    public static readonly string[] StatFullNames = { "Strength", "Dexterity", "Constitution", "Intelligence", "Perception", "Charisma" };
    // A level gained in a run: the new level and what it gave, one line each ("+3 Health"), for LevelUpUI.
    public event Action<int, System.Collections.Generic.List<string>> LeveledUp;

    public void ReachedFloor()
    {
        // The last floor's enemies are gone: their per-enemy training and crush counts with them.
        taught.Clear(); crushed.Clear();
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

    // attributePoints and unspent: the points from levels, as saved (null: none, a checkpoint from before them).
    public void Restore(int level, float xp, int[] ranks, int[] growth, float[] skillExperience = null, int[] attributePoints = null, int unspent = 0)
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
        Growth = growth != null && growth.Length == StatNames.Length ? growth : new int[StatNames.Length];
        AttributePoints = attributePoints != null && attributePoints.Length == AttributeCount ? attributePoints : new int[AttributeCount];
        UnspentPoints = Mathf.Max(0, unspent);
        ApplyStats();
    }
}

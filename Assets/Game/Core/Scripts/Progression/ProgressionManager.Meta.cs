using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// What a skill or attribute track is. Serialized by integer: append only.
public enum MetaTrackKind { Skill = 0, Attribute = 1 }
// What an attribute's points above its base give. Serialized by integer: append only.
public enum MetaAttribute { Constitution = 0, Intelligence = 1, Strength = 2, Dexterity = 3 }

// Meta progression: spoils carried out of runs, counted at the tally and spent on the character sheet at the hobby
// table for starting skill levels and attributes. Every milestone bought gives a trophy for the wall, and a mounted
// trophy unlocks its ability for every class. All of it is saved as flags (meta.*, story.*), so AdventureSave keeps it
// with no new format. The controls are the Meta progression section of this component.
public partial class ProgressionManager
{
    [Serializable]
    public class SpoilsKind
    {
        [HorizontalGroup("Row", 0.4f), HideLabel] public ItemData item;
        [HorizontalGroup("Row"), LabelWidth(42), Min(1)] public int worth = 1;
        [HorizontalGroup("Row"), LabelWidth(60), LabelText("Floor 1"), Min(0), Tooltip("Weight on floor 1 (75, 15, 10 for Lesser, Greater, Grand).")] public float floor1 = 75;
        [HorizontalGroup("Row"), LabelWidth(70), LabelText("Per floor"), Tooltip("Added to the weight each floor deeper (Lesser -5, Greater +3, Grand +2), never below 0.")] public float perFloor = -5;
        public float WeightAt(int floor) => Mathf.Max(0, floor1 + perFloor * (Mathf.Max(1, floor) - 1));
    }

    [Serializable]
    public class BossSpoils
    {
        [HorizontalGroup("Row", 0.4f), HideLabel, Tooltip("The boss (its character data). It always drops these spoils when it dies.")] public CharacterData boss;
        [HorizontalGroup("Row", 0.4f), HideLabel] public ItemData item;
        [HorizontalGroup("Row"), LabelWidth(42), Min(1)] public int worth = 25;
    }

    [Serializable]
    public class Milestone
    {
        [HorizontalGroup("Row", 60), HideLabel, Tooltip("The skill's value it is reached at (15 or 25, then 50).")] public int value = 25;
        [HorizontalGroup("Row"), LabelWidth(50), Tooltip("The ability it unlocks, an asset in its skill's folder (Characters/Skills/<Skill>/) that holds its name, icon and description. Empty: the fields below, \"?\" until designed.")] public SkillAbility ability;
        [HideIf(nameof(ability)), Tooltip("What it unlocks while it has no ability; \"?\" until designed.")] public string unlock = "?";
        [HideIf(nameof(ability)), Tooltip("Shown on the sheet's hover card.")] public string description;
        [HideIf(nameof(ability)), PreviewField(32), Tooltip("The unlock's icon on the sheet (empty: \"?\").")] public Sprite icon;
        [Tooltip("The trophy the DM hands over: its world model hangs on the wall, its icon shows in the key items.")] public ItemData trophy;

        // What the sheet and the character panel show: the ability's own words when there is one.
        public string Name => ability != null ? ability.displayName : unlock;
        public string Description => ability != null ? ability.description : description;
        public Sprite Icon => ability != null ? ability.icon : icon;
        public bool Known => !string.IsNullOrEmpty(Name) && Name != "?";
    }

    [Serializable]
    public class MetaTrack
    {
        [HorizontalGroup("Id", 0.5f), LabelWidth(40), LabelText("Id"), Tooltip("Stable save key; never change it once players have saves.")] public string id;
        [HorizontalGroup("Id"), LabelWidth(50), LabelText("Name")] public string displayName;
        [Tooltip("A skill (bought in steps of skill points, with milestones) or an attribute (bought a point at a time).")] public MetaTrackKind kind;
        [ShowIf("@kind == MetaTrackKind.Skill")] public AdventureSkill skill;
        [ShowIf("@kind == MetaTrackKind.Attribute")] public MetaAttribute attribute;
        [PreviewField(32)] public Sprite icon;
        [Min(0), LabelText("Free levels"), Tooltip("Levels it starts with for free (skills: 2, so every skill starts at 10).")] public int givenLevels = 2;
        [Min(1), LabelText("Levels"), Tooltip("All the levels it has, the free ones included.")] public int levels = 10;
        [Min(1), LabelText("Skill points a level"), ShowIf("@kind == MetaTrackKind.Skill"), Tooltip("Skill points each level adds.")] public int perLevel = 5;
        [LabelText("Starting score"), ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("The score with nothing bought.")] public int baseScore = 8;
        [LabelText("Gain 1, a point"), ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("What each point above the starting score gives to the attribute's first stat (AttributeStats): health, mana, damage or critical chance in percent.")] public float bonusPerPoint = 5;
        [LabelText("Gain 1, named"), ShowIf("@kind == MetaTrackKind.Attribute")] public string unit = "health";
        [LabelText("Gain 2, a point"), ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("What each point above the starting score gives to its second stat (AttributeStats): Strength's shield bash damage, Dexterity's backstab damage in percent, Constitution's stamina, Intelligence's mana per second.")] public float secondBonusPerPoint;
        [LabelText("Gain 2, named"), ShowIf("@kind == MetaTrackKind.Attribute")] public string secondUnit;
        [LabelText("Gain 3, a point (%)"), ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("What each point above the starting score gives to its third stat, in percent: Strength's heavy attack damage, Dexterity's attack speed, Constitution's healing from items; Intelligence's is its spell power, which is no stat (AdventurerProgress.SpellPower reads it).")] public float thirdBonusPerPoint;
        [LabelText("Gain 3, named"), ShowIf("@kind == MetaTrackKind.Attribute")] public string thirdUnit;
        [ShowIf("@kind == MetaTrackKind.Skill"), ListDrawerSettings(ShowFoldout = true), Tooltip("Values of the skill that unlock an ability and give a trophy for the wall (Athletics 15: the dash).")] public List<Milestone> milestones = new();
    }

    // ── Controls ──────────────────────────────────────────────────────

    [FoldoutGroup("Spoils"), PropertyOrder(30), LabelText("Sizes and drop weights"), ListDrawerSettings(ShowFoldout = true)]
    [InfoBox("Spoils are the red loot a run brings home (monster parts and baubles). When a run ends they are counted (the tally) and banked, to spend on the character sheet. The intro's practice run gives none.")]
    [Tooltip("The three sizes of spoils (an assortment of monster parts and baubles). Which one drops is picked by these weights for the floor.")]
    public List<SpoilsKind> spoils = new();
    [FoldoutGroup("Spoils"), PropertyOrder(30), LabelText("Chance a kill drops some"), Range(0, 1), Tooltip("Chance a kill drops spoils.")] public float enemySpoilsChance = .15f;
    [FoldoutGroup("Spoils"), PropertyOrder(30), LabelText("Chance a chest holds some"), Range(0, 1), Tooltip("Chance a chest holds spoils (barrels and crates never do).")] public float chestSpoilsChance = .30f;
    [FoldoutGroup("Spoils"), PropertyOrder(30), LabelText("Enemy health a bonus spoil"), Min(1), Tooltip("At the tally, 1 more spoil per this much health of the enemies killed in the run.")] public int healthPerSpoil = 50;
    [FoldoutGroup("Spoils"), PropertyOrder(30), LabelText("Bosses' own spoils"), ListDrawerSettings(ShowFoldout = true), Tooltip("Bosses always drop their own spoils.")]
    public List<BossSpoils> bossSpoils = new();

    [FoldoutGroup("Character sheet"), PropertyOrder(20), LabelText("Price of a first level"), Min(1), Tooltip("Spoils the first level bought costs, on any skill or attribute.")]
    [InfoBox("The character sheet on the hobby table in the bedroom. Between runs the player spends spoils there on skills and attributes, and every run starts from what was bought. A skill's milestones unlock abilities (Athletics 15: the dash) and give a trophy for the wall; a hung trophy unlocks it for every class.")] public int firstPrice = 10;
    [FoldoutGroup("Character sheet"), PropertyOrder(20), LabelText("Each next level costs x"), Min(1), Tooltip("Each level costs this many times the one before (1.5: 10, 15, 22...).")] public float priceMultiplier = 1.5f;
    [FoldoutGroup("Character sheet"), PropertyOrder(20), LabelText("Attributes cost x"), Min(0), Tooltip("Attributes (Strength, Dexterity, Constitution, Intelligence) cost this many times a skill's level at the same step (the user, 2026-11-11: 2, so 20, 30, 45...).")]
    public float attributePriceMultiplier = 2f;
    [FoldoutGroup("Character sheet"), PropertyOrder(20), LabelText("Skills and attributes for sale"), ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "displayName")]
    [Tooltip("Every row on the sheet: the eight skills and the four attributes, with their levels and milestones.")]
    public List<MetaTrack> tracks = new();

    [FoldoutGroup("First visit to the sheet"), PropertyOrder(25), LabelText("Says, pointing to the sheet"), Tooltip("Back in the room after the first run that ends in a real dungeon.")]
    [InfoBox("Teaching the sheet. After the first run ends, the Dungeon Master points the player to the sheet and tops up their spoils, and no new run starts until they buy the first skill below up to its first milestone (Athletics to 15: the dash). Here are that skill and his lines.")]
    public DungeonMaster.Line lessonIntro = new() { text = "There's a sheet on the hobby table by the trophy wall. Have a look." };
    [FoldoutGroup("First visit to the sheet"), PropertyOrder(25), LabelText("Skill to buy first"), Tooltip("The skill the lesson teaches first (Athletics since 2026-11-01, the dash at 15; Blocking before): until its first milestone is bought nothing else on the sheet can be, and no run starts.")]
    public AdventureSkill lessonSkill = AdventureSkill.Athletics;
    [FoldoutGroup("First visit to the sheet"), PropertyOrder(25), LabelText("Says, if something else is bought"), Tooltip("Trying to buy anything but the lesson's skill before its first milestone.")]
    [UnityEngine.Serialization.FormerlySerializedAs("lessonBlockingFirst")]
    public DungeonMaster.Line lessonSkillFirst = new() { text = "Athletics first. Trust me." };
    [FoldoutGroup("First visit to the sheet"), PropertyOrder(25), LabelText("Says, topping up the spoils"), Tooltip("When the first tally can't pay for the lesson skill's next level, the DM adds the rest.")]
    public DungeonMaster.Line lessonTopUp = new() { text = "Have a bit extra. Don't tell anyone." };
    [FoldoutGroup("Character sheet"), PropertyOrder(20), LabelText("Says, handing over a trophy"), Tooltip("The Dungeon Master, handing over one trophy as the sheet is put down.")]
    public DungeonMaster.Line handOverOne = new() { text = "This one goes on the wall." };
    [FoldoutGroup("Character sheet"), PropertyOrder(20), LabelText("Says, handing over trophies"), Tooltip("The Dungeon Master, handing over several trophies at once.")]
    public DungeonMaster.Line handOverMany = new() { text = "All of these go on the wall." };
    [FoldoutGroup("First visit to the sheet"), PropertyOrder(25), LabelText("Says, once it is bought"), Tooltip("Said once, as the sheet is put down after the lesson skill's first milestone (the dash) is bought. {sprint} and the other keys are the player's own.")]
    [UnityEngine.Serialization.FormerlySerializedAs("shieldBashMounted")]
    public DungeonMaster.Line lessonTrophyMounted = new() { text = "You can evade now. {sprint} and a direction, sideways or back." };
    [FoldoutGroup("First visit to the sheet"), PropertyOrder(25), LabelText("Wait before he speaks (s)"), Min(0), Tooltip("Seconds after the room player is free before his first line about the sheet.")] public float lessonDelay = 2.5f;
    [FoldoutGroup("Character sheet"), PropertyOrder(20), LabelText("Portraits in colour"), Tooltip("The character sheet's portraits in colour (each class's Sheet Portrait Colour) instead of drawn in pencil (Sheet Portrait).")]
    public bool colourSheetPortraits;

    // The portrait the sheet shows for a class, in the look picked above.
    public Sprite SheetPortrait(AdventurerClass c) => c == null ? null : colourSheetPortraits && c.sheetPortraitColour != null ? c.sheetPortraitColour : c.sheetPortrait;

    // Story flags of the lesson; meta.* flags hold the state.
    public const string LedgerIntroFlag = "story.ledgerIntro", LedgerSaidFlag = "story.ledgerSaid", LessonTaughtFlag = "story.blockingTaught", LessonTrophyFlag = "story.bashMounted";
    public const string SpoilsFlag = "meta.spoils", BoughtPrefix = "meta.bought.", TrophyPrefix = "meta.trophy.", PortraitFlag = "meta.portrait";
    // A trophy's state, kept in meta.trophy.<track>.<value>.
    public enum TrophyState { None = 0, Earned = 1, Carried = 2, Mounted = 3 }

    public event Action MetaChanged;

    // ── State ─────────────────────────────────────────────────────────

    public int Spoils => GetFlag(SpoilsFlag);
    public void AddSpoils(int amount) { if (amount == 0) return; SetFlag(SpoilsFlag, Mathf.Max(0, Spoils + amount)); TopUpForLesson(); MetaChanged?.Invoke(); }
    public int PortraitIndex { get => GetFlag(PortraitFlag); set { SetFlag(PortraitFlag, value); MetaChanged?.Invoke(); } }
    // The lesson is done once its skill's first milestone is bought (the flag's key is from when Blocking was taught).
    public bool LessonTaught => HasFlag(LessonTaughtFlag);
    public string LessonSkillName => Track(lessonSkill)?.displayName ?? lessonSkill.ToString();

    public MetaTrack Track(string id) => tracks.Find(t => t != null && t.id == id);
    public MetaTrack Track(AdventureSkill skill) => tracks.Find(t => t != null && t.kind == MetaTrackKind.Skill && t.skill == skill);
    public MetaTrack Track(MetaAttribute attribute) => tracks.Find(t => t != null && t.kind == MetaTrackKind.Attribute && t.attribute == attribute);

    public int Bought(MetaTrack t) => t == null ? 0 : Mathf.Clamp(GetFlag(BoughtPrefix + t.id), 0, Mathf.Max(0, t.levels - t.givenLevels));
    public int Levels(MetaTrack t) => t == null ? 0 : t.givenLevels + Bought(t);
    // A skill's starting value (levels x points), or an attribute's score (base + levels).
    public int Value(MetaTrack t) => t == null ? 0 : t.kind == MetaTrackKind.Skill ? Levels(t) * t.perLevel : t.baseScore + Levels(t);
    public float AttributeBonus(MetaTrack t) => t == null || t.kind != MetaTrackKind.Attribute ? 0 : Levels(t) * t.bonusPerPoint;
    public int ValueAtLevels(MetaTrack t, int levels) => t.kind == MetaTrackKind.Skill ? levels * t.perLevel : t.baseScore + levels;
    public bool Maxed(MetaTrack t) => t == null || Levels(t) >= t.levels;
    // The next level's price, or -1 at the top.
    public int NextPrice(MetaTrack t) => t == null || Maxed(t) ? -1 : PriceAt(t, Bought(t));
    public int Spent(MetaTrack t) { int sum = 0; for (int i = 0; i < Bought(t); i++) sum += PriceAt(t, i); return sum; }
    // A track's level at this step: the first price grown by the multiplier, an attribute's times Attributes Cost x.
    int PriceAt(MetaTrack t, int step) => Mathf.RoundToInt(firstPrice * Mathf.Pow(priceMultiplier, step) * (t.kind == MetaTrackKind.Attribute ? attributePriceMultiplier : 1f));

    // The skill a run starts from, before the class adds its own.
    public int BaseRank(AdventureSkill skill) { var t = Track(skill); return t != null ? Value(t) : 0; }
    // An attribute's score at the start of a run: the sheet's (its base outside the meta game, e.g. the intro's practice run).
    public int AttributeScore(MetaAttribute attribute) { var t = Track(attribute); return t == null ? 8 : MetaActive ? Value(t) : t.baseScore; }

    // The stats an attribute's points above its base raise: the first by bonusPerPoint, the second by secondBonusPerPoint,
    // the third by thirdBonusPerPoint (Intelligence has no third stat: its third gain is spell power, read by
    // AdventurerProgress.SpellPower). The sheet, the character panel, the level-up cards and the run's stats
    // (AdventurerProgress.ApplyStats) all read these, so they match.
    static readonly StatType[] ConstitutionStats = { StatType.MaxHealth, StatType.MaxStamina, StatType.HealingItems },
        IntelligenceStats = { StatType.MaxMana, StatType.ManaRegen },
        StrengthStats = { StatType.AttackDamage, StatType.ShieldBashDamage, StatType.HeavyDamage },
        DexterityStats = { StatType.CriticalChance, StatType.BackstabDamage, StatType.AttackSpeed };
    public static StatType[] AttributeStats(MetaAttribute a) => a switch
    {
        MetaAttribute.Constitution => ConstitutionStats, MetaAttribute.Intelligence => IntelligenceStats, MetaAttribute.Dexterity => DexterityStats, _ => StrengthStats,
    };
    // Stats written in percent on the track and kept as fractions in the stats (attack speed is a multiplier on 1, so
    // +3% is 0.03 added, as gear adds it).
    public static float StatScale(MetaAttribute a, StatType stat) => stat is StatType.CriticalChance or StatType.BackstabDamage or StatType.AttackSpeed
        or StatType.HeavyDamage or StatType.HealingItems ? .01f : 1f;
    public static ModifierType StatKind(MetaAttribute a, StatType stat) => ModifierType.Flat;
    // What this many points above the base give, as the stats take it: (stat, amount, kind) per stat the attribute raises.
    public static IEnumerable<(StatType stat, float amount, ModifierType kind)> AttributeGains(MetaTrack t, float points)
    {
        if (t == null || points == 0) yield break;
        var stats = AttributeStats(t.attribute);
        float[] perPoint = { t.bonusPerPoint, t.secondBonusPerPoint, t.thirdBonusPerPoint };
        for (int i = 0; i < stats.Length; i++)
            if (perPoint[i] != 0) yield return (stats[i], points * perPoint[i] * StatScale(t.attribute, stats[i]) * BelowBase(stats[i], points), StatKind(t.attribute, stats[i]));
    }

    // Points under the base score take mana regeneration away at a quarter of the rate they add it above (the user,
    // 2026-11-11: a Warrior's Intelligence 6 left 0 mana a second; it should still regenerate and scale with
    // Intelligence). With the table player's 0.3 a second: Intelligence 6 0.2, 7 0.25, 8 0.3, 9 0.5, 11 0.9. The stat
    // also never goes under its floor on TablePlayer's data (ManaRegen min 0.1).
    public const float ManaRegenBelowBase = .25f;
    static float BelowBase(StatType stat, float points) => stat == StatType.ManaRegen && points < 0f ? ManaRegenBelowBase : 1f;
    // "+5 health, +5 stamina": what this many points give, in the track's own words. lineBreak puts each on its own line.
    public static string AttributeEffects(MetaTrack t, float points, bool lineBreak = false)
    {
        if (t == null) return "";
        static string Number(float v) => Mathf.Approximately(v, Mathf.Round(v)) ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##");
        static string Unit(string unit) => unit != null && unit.StartsWith("%") ? unit : " " + unit;   // "+5% critical chance"
        string first = $"+{Number(points * t.bonusPerPoint)}{Unit(t.unit)}";
        string gap = lineBreak ? "\n" : ", ";
        if (t.secondBonusPerPoint != 0 && !string.IsNullOrEmpty(t.secondUnit)) first += gap + $"+{Number(points * t.secondBonusPerPoint)}{Unit(t.secondUnit)}";
        if (t.thirdBonusPerPoint != 0 && !string.IsNullOrEmpty(t.thirdUnit)) first += gap + $"+{Number(points * t.thirdBonusPerPoint)}{Unit(t.thirdUnit)}";
        return first;
    }

    // Each milestone's flag key, made once (the quest log and the key items count trophies several times a second).
    readonly Dictionary<Milestone, (string id, int value, string key)> trophyKeys = new();
    string TrophyKey(MetaTrack t, Milestone m)
    {
        if (trophyKeys.TryGetValue(m, out var k) && k.id == t.id && k.value == m.value) return k.key;
        string key = $"{TrophyPrefix}{t.id}.{m.value}";
        trophyKeys[m] = (t.id, m.value, key);
        return key;
    }
    public TrophyState Trophy(MetaTrack t, Milestone m) => t == null || m == null ? TrophyState.None : (TrophyState)Mathf.Clamp(GetFlag(TrophyKey(t, m)), 0, 3);
    void SetTrophy(MetaTrack t, Milestone m, TrophyState state) => SetFlag(TrophyKey(t, m), (int)state);
    // The dev console's addroomitem: this trophy handed over, whatever its state, ready to hang on the wall.
    public void CarryTrophy(MetaTrack t, Milestone m) { if (t == null || m == null) return; SetTrophy(t, m, TrophyState.Carried); MetaChanged?.Invoke(); }
    public bool Reached(MetaTrack t, Milestone m) => t != null && m != null && Value(t) >= m.value;

    public IEnumerable<(MetaTrack track, Milestone milestone)> AllMilestones()
    {
        foreach (var t in tracks)
            if (t != null && t.kind == MetaTrackKind.Skill && t.milestones != null)
                foreach (var m in t.milestones) if (m != null) yield return (t, m);
    }
    public List<(MetaTrack track, Milestone milestone)> TrophiesIn(TrophyState state)
    {
        var list = new List<(MetaTrack, Milestone)>();
        foreach (var t in tracks)
            if (t != null && t.kind == MetaTrackKind.Skill && t.milestones != null)
                foreach (var m in t.milestones) if (m != null && Trophy(t, m) == state) list.Add((t, m));
        return list;
    }
    // How many trophies are in this state, with no list made.
    public int TrophyCount(TrophyState state)
    {
        int count = 0;
        foreach (var t in tracks)
            if (t != null && t.kind == MetaTrackKind.Skill && t.milestones != null)
                foreach (var m in t.milestones) if (m != null && Trophy(t, m) == state) count++;
        return count;
    }

    // An unlock is yours in this run when its trophy is mounted, or (after the lesson) when this run's rank reaches it
    // (bought base + class + training).
    public bool Unlocked(MetaTrack t, Milestone m, int runRank)
    {
        if (t == null || m == null) return true;
        if (Trophy(t, m) == TrophyState.Mounted) return true;
        // The editor's Debug, Dungeon and Boss sessions skip the lesson, so the run's own ranks count there.
        return (LessonTaught || DebugDungeonSession.Active) && runRank >= m.value;
    }
    public bool Unlocked(AdventureSkill skill, int milestoneIndex, int runRank)
    {
        var t = Track(skill);
        if (t == null || t.milestones == null || milestoneIndex >= t.milestones.Count) return true;
        return Unlocked(t, t.milestones[milestoneIndex], runRank);
    }
    // An ability's unlock: the milestone that points at it, at the run's rank in its skill (an ability no milestone
    // points at is there from the start, as the shield bash is).
    public bool Unlocked(SkillAbility ability, AdventurerProgress progress)
    {
        if (ability == null) return true;
        foreach (var (t, m) in AllMilestones())
            if (m.ability == ability) return Unlocked(t, m, progress != null ? progress.Rank(t.skill) : 0);
        return true;
    }

    // ── Buying ────────────────────────────────────────────────────────

    public enum BuyResult { Bought, Locked, Poor, Max }
    // Once the lesson has begun (the first run ended), only its skill can be bought until its first milestone is; before
    // that nothing is locked, so the sheet doesn't say "Athletics first" on the first morning with no spoils at all.
    public bool LockedByLesson(MetaTrack t) => !LessonTaught && HasFlag(LedgerIntroFlag) && (t == null || t.kind != MetaTrackKind.Skill || t.skill != lessonSkill);

    public BuyResult Buy(MetaTrack t)
    {
        if (t == null || Maxed(t)) return BuyResult.Max;
        if (LockedByLesson(t)) return BuyResult.Locked;
        int price = NextPrice(t);
        if (Spoils < price) return BuyResult.Poor;
        SetFlag(SpoilsFlag, Spoils - price);
        SetFlag(BoughtPrefix + t.id, Bought(t) + 1);
        if (t.milestones != null)
            foreach (var m in t.milestones)
                if (m != null && Reached(t, m) && Trophy(t, m) == TrophyState.None) SetTrophy(t, m, TrophyState.Earned);
        if (t.kind == MetaTrackKind.Skill && t.skill == lessonSkill && !LessonTaught && t.milestones != null && t.milestones.Count > 0 && Reached(t, t.milestones[0]))
            SetFlag(LessonTaughtFlag);
        // A level that doesn't reach the first milestone yet leaves the lesson on: the next one stays affordable.
        TopUpForLesson();
        MetaChanged?.Invoke();
        return BuyResult.Bought;
    }

    // Everything bought comes back, free; the trophies of milestones no longer reached come off the wall.
    public int RefundAll()
    {
        int back = 0;
        foreach (var t in tracks) { if (t == null) continue; back += Spent(t); SetFlag(BoughtPrefix + t.id, 0); }
        if (back > 0) SetFlag(SpoilsFlag, Spoils + back);
        foreach (var (t, m) in AllMilestones()) if (!Reached(t, m) && Trophy(t, m) != TrophyState.None) SetTrophy(t, m, TrophyState.None);
        TopUpForLesson();
        MetaChanged?.Invoke();
        return back;
    }

    // The sheet is put down: the DM hands over every trophy earned since, in one line.
    public int HandOverTrophies()
    {
        var earned = TrophiesIn(TrophyState.Earned);
        foreach (var (t, m) in earned) SetTrophy(t, m, TrophyState.Carried);
        if (earned.Count > 0)
        {
            DungeonMaster.Say(earned.Count == 1 ? handOverOne : handOverMany);
            MetaChanged?.Invoke();
        }
        // The lesson's ability works from the moment it is bought: he says so as the sheet goes down, not once its
        // trophy is on the wall (that could be long after, or never).
        SayLessonLearned();
        return earned.Count;
    }

    // At the wall: everything carried goes up at once (TrophyWall.hangAtPlaques off).
    public int MountCarried()
    {
        var carried = TrophiesIn(TrophyState.Carried);
        bool lesson = false;
        foreach (var (t, m) in carried)
        {
            SetTrophy(t, m, TrophyState.Mounted);
            if (t.skill == lessonSkill && t.milestones.IndexOf(m) == 0) lesson = true;
        }
        if (carried.Count > 0) MetaChanged?.Invoke();
        if (lesson) SayLessonLearned();
        return carried.Count;
    }

    // At its own plaque: one carried trophy goes up. False when it isn't carried.
    public bool MountOne(MetaTrack t, Milestone m)
    {
        if (t == null || m == null || Trophy(t, m) != TrophyState.Carried) return false;
        SetTrophy(t, m, TrophyState.Mounted);
        MetaChanged?.Invoke();
        if (t.skill == lessonSkill && t.milestones.IndexOf(m) == 0) SayLessonLearned();
        return true;
    }

    // "You can dash now", once (the flag's key is from when the lesson ended with the shield bash's trophy).
    void SayLessonLearned()
    {
        if (!LessonTaught || HasFlag(LessonTrophyFlag)) return;
        SetFlag(LessonTrophyFlag);
        DungeonMaster.Say(lessonTrophyMounted.With(KeysIn(lessonTrophyMounted.text)));
    }

    // ── Spoils drops and the tally ────────────────────────────────────

    public int WorthOf(ItemData item)
    {
        if (item == null) return 0;
        foreach (var k in spoils) if (k != null && k.item == item) return k.worth;
        foreach (var b in bossSpoils) if (b != null && b.item == item) return b.worth;
        return 0;
    }

    // Spoils for a kill or a chest, or null. Its own random stream, so the loot's seeds stay as they were.
    public ItemData RollSpoils(int seed, int floor, bool chest)
    {
        if (!MetaActive) return null;
        var rng = new System.Random(seed ^ 0x5B0115);
        if (rng.NextDouble() >= (chest ? chestSpoilsChance : enemySpoilsChance)) return null;
        float total = 0;
        foreach (var k in spoils) if (k != null && k.item != null) total += k.WeightAt(floor);
        if (total <= 0) return null;
        double pick = rng.NextDouble() * total;
        foreach (var k in spoils)
        {
            if (k == null || k.item == null) continue;
            pick -= k.WeightAt(floor);
            if (pick < 0) return k.item;
        }
        return null;
    }
    public ItemData BossSpoilsFor(CharacterData data)
    {
        if (data == null || !MetaActive) return null;
        foreach (var b in bossSpoils) if (b != null && b.boss == data && b.item != null) return b.item;
        return null;
    }

    // The intro's practice board neither drops nor counts spoils.
    public static bool MetaActive => !IntroController.PracticeRunning;

    public struct Tally
    {
        public List<(ItemData item, int count)> carried;
        public int carriedWorth, kills, healthSlain, fromHealth, total, before, after, toppedUp;
        public bool counted;
    }

    // The run's end: the spoils carried out plus 1 per Health Per Spoil of the kills, into the bank. On the first death
    // in a real dungeon the lesson starts, and the DM tops the bank up to the lesson skill's next price.
    public Tally Settle(Player player, int kills, int healthSlain, bool victory)
    {
        var tally = new Tally { carried = new(), kills = kills, healthSlain = healthSlain, before = Spoils };
        if (!MetaActive) { tally.after = Spoils; return tally; }
        void Count(Inventory inventory)
        {
            if (inventory == null) return;
            for (int i = 0; i < inventory.SlotCount; i++)
            {
                var item = inventory.ItemAt(i);
                if (item == null || item.itemType != ItemType.Spoils) continue;
                int n = inventory.CountAt(i);
                tally.carriedWorth += WorthOf(item) * n;
                int at = tally.carried.FindIndex(x => x.item == item);
                if (at >= 0) tally.carried[at] = (item, tally.carried[at].count + n); else tally.carried.Add((item, n));
            }
        }
        if (player != null) { Count(player.Bag); Count(player.Hotbar); }
        tally.carried.Sort((a, b) => WorthOf(a.item).CompareTo(WorthOf(b.item)));
        tally.fromHealth = healthSlain / Mathf.Max(1, healthPerSpoil);
        tally.total = tally.carriedWorth + tally.fromHealth;
        SetFlag(SpoilsFlag, Spoils + tally.total);
        // The carried spoils are spent into the bank: they leave the bag.
        if (player != null) foreach (var inv in new[] { player.Bag, player.Hotbar }) if (inv != null) for (int i = 0; i < inv.SlotCount; i++) if (inv.ItemAt(i) != null && inv.ItemAt(i).itemType == ItemType.Spoils) inv.RemoveAt(i);
        // The key to the locked box, if found, comes home too.
        CarryKeyHome(player);
        // The first run that ends (normally a death, sometimes a win) starts the lesson: the DM points to the sheet back
        // in the room and tops the spoils up to the lesson skill's price, since no run starts until it is bought.
        if (!HasFlag(LedgerIntroFlag))
        {
            SetFlag(LedgerIntroFlag);
            tally.toppedUp = TopUpForLesson();
            if (tally.toppedUp > 0) DungeonMaster.Say(lessonTopUp);
        }
        tally.after = Spoils;
        tally.counted = true;
        MetaChanged?.Invoke();
        return tally;
    }

    // While the lesson holds runs (LessonHoldsRuns: its skill first), the bank never stays below that skill's next price,
    // or no run could ever start again. Returns the spoils the DM added.
    int TopUpForLesson()
    {
        if (!LessonHoldsRuns) return 0;
        int price = NextPrice(Track(lessonSkill));
        if (price <= 0 || Spoils >= price) return 0;
        int added = price - Spoils;
        SetFlag(SpoilsFlag, price);
        return added;
    }

    // ── The lesson's first line, back in the room ────────────────────

    Coroutine lessonRoutine;
    bool lessonBankChecked;
    void Update()
    {
        if (!Application.isPlaying) return;
        // Once, after the save is read: a save left in the lesson with too few spoils to buy its skill is put right.
        if (!lessonBankChecked) { lessonBankChecked = true; if (TopUpForLesson() + HandOverMovedTrophies() > 0) MetaChanged?.Invoke(); }
        UpdateSheetQuest();
        UpdatePosterQuest();
        UpdateDiceQuest();
        UpdateClassQuest();
        if (lessonRoutine != null) return;
        if (HasFlag(LedgerIntroFlag) && !HasFlag(LedgerSaidFlag) && RoomPlayerFree) lessonRoutine = StartCoroutine(SayLesson());
    }
    // Once, after the save is read: a milestone moved onto a rank already bought (the dash at Athletics 15, Deflect at
    // Blocking 25, 2026-11-01) never earned its trophy, since trophies are earned when buying. It is handed over quietly,
    // ready to hang. Returns how many.
    int HandOverMovedTrophies()
    {
        int count = 0;
        foreach (var (t, m) in AllMilestones())
            if (Reached(t, m) && Trophy(t, m) == TrophyState.None) { SetTrophy(t, m, TrophyState.Carried); count++; }
        return count;
    }
    IEnumerator SayLesson()
    {
        float until = Time.time + lessonDelay;
        while (Time.time < until) { if (!RoomPlayerFree) { lessonRoutine = null; yield break; } yield return null; }
        SetFlag(LedgerSaidFlag);
        // The player went to the sheet before he got to it: nothing to point at.
        if (!LessonTaught && !HasFlag(CharacterSheetTable.OpenedFlag)) DungeonMaster.Say(lessonIntro);
        lessonRoutine = null;
    }
    // Not during the win ending either: the DM points at the door then, not at the sheet.
    static bool RoomPlayerFree => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
        && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore && !TableManager.EndingRunning;

    // ── Debug ─────────────────────────────────────────────────────────

    [FoldoutGroup("Debug (Play mode)"), PropertyOrder(70), ShowInInspector, ReadOnly, LabelText("Spoils to spend")] int DebugSpoils => Application.isPlaying ? Spoils : 0;
    [FoldoutGroup("Debug (Play mode)"), PropertyOrder(70), Button("Add 100 spoils"), EnableIf("@UnityEngine.Application.isPlaying")] void DebugAddSpoils() => AddSpoils(100);
    [FoldoutGroup("Debug (Play mode)"), PropertyOrder(70), Button("Teach the lesson skill (skip the lesson)"), EnableIf("@UnityEngine.Application.isPlaying")]
    void DebugTeach() { SetFlag(LedgerIntroFlag); SetFlag(LedgerSaidFlag); SetFlag(LessonTaughtFlag); MetaChanged?.Invoke(); }
    [FoldoutGroup("Debug (Play mode)"), PropertyOrder(70), Button("Reset meta progression"), EnableIf("@UnityEngine.Application.isPlaying")]
    void DebugReset()
    {
        foreach (var key in new List<string>(flags.Keys))
            if (key.StartsWith("meta.") || key == LedgerIntroFlag || key == LedgerSaidFlag || key == LessonTaughtFlag || key == LessonTrophyFlag) SetFlag(key, 0);
        // The lesson's quests and the sheet's first look too, or the lesson can't be played again.
        foreach (var quest in new[] { SheetQuest, SpendQuest, TableQuest, TrophyQuest }) SetFlag(QuestPrefix + quest, 0);
        SetFlag(CharacterSheetTable.OpenedFlag, 0);
        MetaChanged?.Invoke();
    }
    [FoldoutGroup("Debug (Play mode)"), PropertyOrder(70), Button("Open the character sheet"), EnableIf("@UnityEngine.Application.isPlaying")]
    void DebugOpenSheet() { if (CharacterSheetUI.HasInstance) CharacterSheetUI.Instance.Open(); }
}

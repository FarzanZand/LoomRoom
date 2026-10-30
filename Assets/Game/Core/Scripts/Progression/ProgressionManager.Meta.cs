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
        [HorizontalGroup("Row"), LabelWidth(60), Min(0), Tooltip("Weight on floor 1 (75, 15, 10 for Lesser, Greater, Grand).")] public float floor1 = 75;
        [HorizontalGroup("Row"), LabelWidth(70), Tooltip("Added to the weight each floor deeper (Lesser -5, Greater +3, Grand +2), never below 0.")] public float perFloor = -5;
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
        [HorizontalGroup("Row", 60), HideLabel, Tooltip("The value the track reaches (15 or 25, then 50).")] public int value = 25;
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
        [HorizontalGroup("Id", 0.5f), LabelWidth(40), Tooltip("Stable save key; never change it once players have saves.")] public string id;
        [HorizontalGroup("Id"), LabelWidth(50)] public string displayName;
        public MetaTrackKind kind;
        [ShowIf("@kind == MetaTrackKind.Skill")] public AdventureSkill skill;
        [ShowIf("@kind == MetaTrackKind.Attribute")] public MetaAttribute attribute;
        [PreviewField(32)] public Sprite icon;
        [Min(0), Tooltip("Levels the track starts with for free (skills: 2, so every skill starts at 10).")] public int givenLevels = 2;
        [Min(1), Tooltip("All levels, given ones included.")] public int levels = 10;
        [Min(1), ShowIf("@kind == MetaTrackKind.Skill"), Tooltip("Skill points per level.")] public int perLevel = 5;
        [ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("The score with nothing bought.")] public int baseScore = 8;
        [ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("What each point above the base gives to the attribute's first stat (AttributeStats): health, mana, damage or critical chance in percent.")] public float bonusPerPoint = 5;
        [ShowIf("@kind == MetaTrackKind.Attribute")] public string unit = "health";
        [ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("What each point above the base gives to its second stat, if it has one: Constitution's stamina, Intelligence's mana per second.")] public float secondBonusPerPoint;
        [ShowIf("@kind == MetaTrackKind.Attribute")] public string secondUnit;
        [ShowIf("@kind == MetaTrackKind.Attribute"), Tooltip("A third gain that isn't a stat: Intelligence's spell power in percent (AdventurerProgress.SpellPower reads it).")] public float thirdBonusPerPoint;
        [ShowIf("@kind == MetaTrackKind.Attribute")] public string thirdUnit;
        [ShowIf("@kind == MetaTrackKind.Skill"), ListDrawerSettings(ShowFoldout = true)] public List<Milestone> milestones = new();
    }

    // ── Controls ──────────────────────────────────────────────────────

    [FoldoutGroup("Meta progression"), TabGroup("Meta progression/Tabs", "Spoils"), ListDrawerSettings(ShowFoldout = true)]
    [Tooltip("The three sizes of spoils (an assortment of monster parts and baubles). Which one drops is picked by these weights for the floor.")]
    public List<SpoilsKind> spoils = new();
    [TabGroup("Meta progression/Tabs", "Spoils"), Range(0, 1), Tooltip("Chance a kill drops spoils.")] public float enemySpoilsChance = .15f;
    [TabGroup("Meta progression/Tabs", "Spoils"), Range(0, 1), Tooltip("Chance a chest holds spoils (barrels and crates never do).")] public float chestSpoilsChance = .30f;
    [TabGroup("Meta progression/Tabs", "Spoils"), Min(1), Tooltip("At the tally, 1 spoil per this much health of the enemies killed.")] public int healthPerSpoil = 50;
    [TabGroup("Meta progression/Tabs", "Spoils"), ListDrawerSettings(ShowFoldout = true), Tooltip("Bosses always drop their own spoils.")]
    public List<BossSpoils> bossSpoils = new();

    [TabGroup("Meta progression/Tabs", "Tracks"), Min(1), Tooltip("The spoils the first level costs on any track.")] public int firstPrice = 10;
    [TabGroup("Meta progression/Tabs", "Tracks"), Min(1), Tooltip("Each level costs this much more than the one before.")] public float priceMultiplier = 1.5f;
    [TabGroup("Meta progression/Tabs", "Tracks"), ListDrawerSettings(ShowFoldout = true, ListElementLabelName = "displayName")]
    public List<MetaTrack> tracks = new();

    [TabGroup("Meta progression/Tabs", "Lesson"), Tooltip("Back in the room after the first run that ends in a real dungeon.")]
    public DungeonMaster.Line lessonIntro = new() { text = "There's a sheet on the little table by the wall. Have a look." };
    [TabGroup("Meta progression/Tabs", "Lesson"), Tooltip("Trying to buy anything but Blocking before the first Blocking level.")]
    public DungeonMaster.Line lessonBlockingFirst = new() { text = "Blocking first. Trust me." };
    [TabGroup("Meta progression/Tabs", "Lesson"), Tooltip("When the first tally can't pay for Blocking's next level, the DM adds the rest.")]
    public DungeonMaster.Line lessonTopUp = new() { text = "Have a bit extra. Don't tell anyone." };
    [TabGroup("Meta progression/Tabs", "Lesson"), Tooltip("Handing over one trophy.")]
    public DungeonMaster.Line handOverOne = new() { text = "This one goes on the wall." };
    [TabGroup("Meta progression/Tabs", "Lesson"), Tooltip("Handing over several trophies at once.")]
    public DungeonMaster.Line handOverMany = new() { text = "All of these go on the wall." };
    [TabGroup("Meta progression/Tabs", "Lesson"), Tooltip("After Blocking's first trophy (Deflect) is mounted for the first time.")]
    public DungeonMaster.Line shieldBashMounted = new() { text = "Hold your shield up against arrows and fire now. They go back where they came from." };
    [TabGroup("Meta progression/Tabs", "Lesson"), Min(0), Tooltip("Seconds after the room player is free before the lesson's first line.")] public float lessonDelay = 2.5f;
    [TabGroup("Meta progression/Tabs", "Sheet"), Tooltip("The character sheet's portraits in colour (each class's Sheet Portrait Colour) instead of drawn in pencil (Sheet Portrait).")]
    public bool colourSheetPortraits;

    // The portrait the sheet shows for a class, in the look picked above.
    public Sprite SheetPortrait(AdventurerClass c) => c == null ? null : colourSheetPortraits && c.sheetPortraitColour != null ? c.sheetPortraitColour : c.sheetPortrait;

    // Story flags of the lesson; meta.* flags hold the state.
    public const string LedgerIntroFlag = "story.ledgerIntro", LedgerSaidFlag = "story.ledgerSaid", BlockingTaughtFlag = "story.blockingTaught", BashMountedFlag = "story.bashMounted";
    public const string SpoilsFlag = "meta.spoils", BoughtPrefix = "meta.bought.", TrophyPrefix = "meta.trophy.", PortraitFlag = "meta.portrait";
    // A trophy's state, kept in meta.trophy.<track>.<value>.
    public enum TrophyState { None = 0, Earned = 1, Carried = 2, Mounted = 3 }

    public event Action MetaChanged;

    // ── State ─────────────────────────────────────────────────────────

    public int Spoils => GetFlag(SpoilsFlag);
    public void AddSpoils(int amount) { if (amount == 0) return; SetFlag(SpoilsFlag, Mathf.Max(0, Spoils + amount)); TopUpForLesson(); MetaChanged?.Invoke(); }
    public int PortraitIndex { get => GetFlag(PortraitFlag); set { SetFlag(PortraitFlag, value); MetaChanged?.Invoke(); } }
    public bool BlockingTaught => HasFlag(BlockingTaughtFlag);

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
    public int NextPrice(MetaTrack t) => t == null || Maxed(t) ? -1 : Mathf.RoundToInt(firstPrice * Mathf.Pow(priceMultiplier, Bought(t)));
    public int Spent(MetaTrack t) { int sum = 0; for (int i = 0; i < Bought(t); i++) sum += Mathf.RoundToInt(firstPrice * Mathf.Pow(priceMultiplier, i)); return sum; }

    // The skill a run starts from, before the class adds its own.
    public int BaseRank(AdventureSkill skill) { var t = Track(skill); return t != null ? Value(t) : 0; }
    // An attribute's score at the start of a run: the sheet's (its base outside the meta game, e.g. the intro's practice run).
    public int AttributeScore(MetaAttribute attribute) { var t = Track(attribute); return t == null ? 8 : MetaActive ? Value(t) : t.baseScore; }

    // The stats an attribute's points above its base raise: the first by bonusPerPoint, the second by secondBonusPerPoint.
    // The sheet, the character panel and the run's stats (AdventurerProgress.ApplyStats) all read these, so they match.
    static readonly StatType[] ConstitutionStats = { StatType.MaxHealth, StatType.MaxStamina }, IntelligenceStats = { StatType.MaxMana, StatType.ManaRegen },
        StrengthStats = { StatType.AttackDamage }, DexterityStats = { StatType.CriticalChance };
    public static StatType[] AttributeStats(MetaAttribute a) => a switch
    {
        MetaAttribute.Constitution => ConstitutionStats, MetaAttribute.Intelligence => IntelligenceStats, MetaAttribute.Dexterity => DexterityStats, _ => StrengthStats,
    };
    // Critical chance and Strength's damage are written in percent on the track and kept as fractions in the stats;
    // Strength's damage is a share of the player's damage (weapon included), not a flat amount.
    public static float StatScale(MetaAttribute a, StatType stat) => stat == StatType.CriticalChance || a == MetaAttribute.Strength && stat == StatType.AttackDamage ? .01f : 1f;
    public static ModifierType StatKind(MetaAttribute a, StatType stat) => a == MetaAttribute.Strength && stat == StatType.AttackDamage ? ModifierType.PercentAdd : ModifierType.Flat;
    // What this many points above the base give, as the stats take it: (stat, amount, kind) per stat the attribute raises.
    public static IEnumerable<(StatType stat, float amount, ModifierType kind)> AttributeGains(MetaTrack t, float points)
    {
        if (t == null || points == 0) yield break;
        var stats = AttributeStats(t.attribute);
        if (stats.Length > 0 && t.bonusPerPoint != 0) yield return (stats[0], points * t.bonusPerPoint * StatScale(t.attribute, stats[0]), StatKind(t.attribute, stats[0]));
        if (stats.Length > 1 && t.secondBonusPerPoint != 0) yield return (stats[1], points * t.secondBonusPerPoint * StatScale(t.attribute, stats[1]), StatKind(t.attribute, stats[1]));
    }
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

    // An unlock is yours in this run when its trophy is mounted, or (after the first Blocking lesson) when this run's
    // rank reaches it (bought base + class + training).
    public bool Unlocked(MetaTrack t, Milestone m, int runRank)
    {
        if (t == null || m == null) return true;
        if (Trophy(t, m) == TrophyState.Mounted) return true;
        // The editor's Debug, Dungeon and Boss sessions skip the lesson, so the run's own ranks count there.
        return (BlockingTaught || DebugDungeonSession.Active) && runRank >= m.value;
    }
    public bool Unlocked(AdventureSkill skill, int milestoneIndex, int runRank)
    {
        var t = Track(skill);
        if (t == null || t.milestones == null || milestoneIndex >= t.milestones.Count) return true;
        return Unlocked(t, t.milestones[milestoneIndex], runRank);
    }

    // ── Buying ────────────────────────────────────────────────────────

    public enum BuyResult { Bought, Locked, Poor, Max }
    // Before the first Blocking level only Blocking can be bought (the lesson).
    public bool LockedByLesson(MetaTrack t) => !BlockingTaught && (t == null || t.kind != MetaTrackKind.Skill || t.skill != AdventureSkill.Blocking);

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
        if (t.kind == MetaTrackKind.Skill && t.skill == AdventureSkill.Blocking && !BlockingTaught && t.milestones != null && t.milestones.Count > 0 && Reached(t, t.milestones[0]))
            SetFlag(BlockingTaughtFlag);
        // A Blocking level that doesn't reach the first milestone yet leaves the lesson on: the next one stays affordable.
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
        return earned.Count;
    }

    // At the wall: everything carried goes up at once.
    public int MountCarried()
    {
        var carried = TrophiesIn(TrophyState.Carried);
        bool bash = false;
        foreach (var (t, m) in carried)
        {
            SetTrophy(t, m, TrophyState.Mounted);
            if (t.skill == AdventureSkill.Blocking && t.milestones.IndexOf(m) == 0) bash = true;
        }
        if (carried.Count > 0) MetaChanged?.Invoke();
        if (bash && !HasFlag(BashMountedFlag)) { SetFlag(BashMountedFlag); DungeonMaster.Say(shieldBashMounted); }
        return carried.Count;
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
    // in a real dungeon the lesson starts, and the DM tops the bank up to Blocking's next price.
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
        // The puzzle's missing tile, if found, comes home too.
        CarryTileHome(player);
        // The first run that ends (normally a death, sometimes a win) starts the lesson: the DM points to the sheet back
        // in the room and tops the spoils up to Blocking's price, since no run starts until Blocking is bought.
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

    // While the lesson holds runs (LessonHoldsRuns: Blocking first), the bank never stays below Blocking's next price,
    // or no run could ever start again. Returns the spoils the DM added.
    int TopUpForLesson()
    {
        if (!LessonHoldsRuns) return 0;
        int price = NextPrice(Track(AdventureSkill.Blocking));
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
        // Once, after the save is read: a save left in the lesson with too few spoils to buy Blocking is put right.
        if (!lessonBankChecked) { lessonBankChecked = true; if (TopUpForLesson() > 0) MetaChanged?.Invoke(); }
        UpdateSheetQuest();
        UpdatePosterQuest();
        UpdateDiceQuest();
        if (lessonRoutine != null) return;
        if (HasFlag(LedgerIntroFlag) && !HasFlag(LedgerSaidFlag) && RoomPlayerFree) lessonRoutine = StartCoroutine(SayLesson());
    }
    IEnumerator SayLesson()
    {
        float until = Time.time + lessonDelay;
        while (Time.time < until) { if (!RoomPlayerFree) { lessonRoutine = null; yield break; } yield return null; }
        SetFlag(LedgerSaidFlag);
        DungeonMaster.Say(lessonIntro);
        lessonRoutine = null;
    }
    // Not during the win ending either: the DM points at the door then, not at the sheet.
    static bool RoomPlayerFree => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
        && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore && !TableManager.EndingRunning;

    // ── Debug ─────────────────────────────────────────────────────────

    [TabGroup("Meta progression/Tabs", "Debug"), ShowInInspector, ReadOnly, LabelText("Spoils to spend")] int DebugSpoils => Application.isPlaying ? Spoils : 0;
    [TabGroup("Meta progression/Tabs", "Debug"), Button("Add 100 spoils"), EnableIf("@UnityEngine.Application.isPlaying")] void DebugAddSpoils() => AddSpoils(100);
    [TabGroup("Meta progression/Tabs", "Debug"), Button("Teach Blocking (skip the lesson)"), EnableIf("@UnityEngine.Application.isPlaying")]
    void DebugTeach() { SetFlag(LedgerIntroFlag); SetFlag(LedgerSaidFlag); SetFlag(BlockingTaughtFlag); MetaChanged?.Invoke(); }
    [TabGroup("Meta progression/Tabs", "Debug"), Button("Reset meta progression"), EnableIf("@UnityEngine.Application.isPlaying")]
    void DebugReset()
    {
        foreach (var key in new List<string>(flags.Keys))
            if (key.StartsWith("meta.") || key == LedgerIntroFlag || key == LedgerSaidFlag || key == BlockingTaughtFlag || key == BashMountedFlag) SetFlag(key, 0);
        // The lesson's quests and the sheet's first look too, or the lesson can't be played again.
        foreach (var quest in new[] { SheetQuest, SpendQuest, TableQuest, TrophyQuest }) SetFlag(QuestPrefix + quest, 0);
        SetFlag(CharacterSheetTable.OpenedFlag, 0);
        MetaChanged?.Invoke();
    }
    [TabGroup("Meta progression/Tabs", "Debug"), Button("Open the character sheet"), EnableIf("@UnityEngine.Application.isPlaying")]
    void DebugOpenSheet() { if (CharacterSheetUI.HasInstance) CharacterSheetUI.Instance.Open(); }
}

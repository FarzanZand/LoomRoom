using System;
using System.Collections;
using UnityEngine;

// One dungeon run from entry to death or victory: its statistics, the death moment
// (slow motion, draining colour) and the recap screen. TableLevelLoader starts runs and
// reports floors; everything else here listens for itself.
public class RunManager : Singleton<RunManager>
{
    [Header("Death moment")]
    [Range(.01f, 1f), Tooltip("Time scale while the player falls.")]
    public float slowMotionScale = .2f;
    [Min(0), Tooltip("Real seconds of slow motion before the recap appears.")]
    public float slowMotionSeconds = 1.8f;
    [Min(.01f), Tooltip("Real seconds for the screen to drain to grey.")]
    public float desaturateSeconds = 1.1f;
    public AudioClip deathSting;
    [Range(0, 1)] public float deathStingVolume = .9f;
    [Header("Victory")]
    public AudioClip victorySting;
    [Range(0, 1)] public float victoryStingVolume = .9f;

    [Tooltip("Scene-authored recap screen.")]
    public RunRecapUI recap;

    // Run-long blessings and curses (fountains, altars) are stat modifiers tagged with this;
    // a new run removes them.
    public static readonly object BlessingSource = new BlessingTag();
    sealed class BlessingTag { public override string ToString() => "Run blessing"; }

    public bool Running { get; private set; }
    public TableLevelData Level { get; private set; }
    public int Seed { get; private set; }
    public int Floor { get; private set; }
    public int Kills { get; private set; }
    public int GoldFound { get; private set; }
    public int BossesSlain { get; private set; }
    // The summed maximum health of the player's kills: the tally turns it into spoils (ProgressionManager.healthPerSpoil).
    public int HealthSlain { get; private set; }
    // The run's tally, settled as it ends (spoils into the bank).
    public ProgressionManager.Tally LastTally { get; private set; }
    public float Seconds { get; private set; }
    public string Killer { get; private set; }
    public bool Ended { get; private set; }

    // A killer as a phrase: "a Crypt Soldier", "an Ogre"; phrases that already read as a noun ("the dungeon",
    // "a curse") pass through. lowerName lower-cases a bare name, for the middle of a spoken line.
    public static string KillerWithArticle(string killer, bool lowerName = false)
    {
        if (string.IsNullOrEmpty(killer)) return "the dungeon";
        if (killer.StartsWith("the ") || killer.StartsWith("a ") || killer.StartsWith("an ") || Character.ProperNames.Contains(killer)) return killer;
        return ("aeiouAEIOU".IndexOf(killer[0]) >= 0 ? "an " : "a ") + (lowerName ? killer.ToLower() : killer);
    }

    public event Action RunStarted;
    public event Action<bool> RunEnded; // victory
    // The run goes on after a death (RevivePlayer), raised once the player stands again. The argument is whether the
    // death had already been banked (RunEnded(false) had been raised): its listeners then take back what they did for it.
    public event Action<bool> Revived; // counted

    Player player;
    Coroutine deathRoutine;

    void Start()
    {
        player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        player?.GetComponent<AdventureSave>()?.Initialize();
        if (player != null) player.Damaged += OnPlayerDamaged;
        Character.AnyDied += OnAnyDied;
    }

    // The table player wakes (and gets its Wallet) only when first used, after Start: listen per run.
    Wallet wallet;
    void ListenToWallet()
    {
        var w = player != null ? player.GetComponent<Wallet>() : null;
        if (w == wallet) return;
        if (wallet != null) wallet.Changed -= OnGold;
        wallet = w;
        if (wallet != null) wallet.Changed += OnGold;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (player != null)
        {
            player.Damaged -= OnPlayerDamaged;
            if (wallet != null) wallet.Changed -= OnGold;
        }
        Character.AnyDied -= OnAnyDied;
        if (deathRoutine != null) Time.timeScale = 1f;
    }

    void Update()
    {
        if (Running && !Ended && GameManager.HasInstance && GameManager.Instance.SimulationActive
            && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table)
            Seconds += Time.deltaTime;
    }

    // ── Called by TableLevelLoader ────────────────────────────────────

    public void BeginRun(TableLevelData level, int seed)
    {
        StopDeathMoment();
        deathPending = false;
        Running = true; Ended = false; won = false;
        Level = level; Seed = seed; Floor = 1;
        Kills = 0; GoldFound = 0; BossesSlain = 0; HealthSlain = 0; Seconds = 0f; Killer = null; LastTally = default;
        ListenToWallet();
        if (player != null)
        {
            player.Stats?.RemoveAllFromSource(BlessingSource);
            if (player.Stats != null) player.Stats.BlockBlessing = false;
            player.Wallet?.Clear();
        }
        recap?.Hide();
        player?.GetComponent<AdventurerProgress>()?.Begin();
        ItemData.PruneVariants();
        RunStarted?.Invoke();
    }

    public void ReachFloor(int floor)
    {
        bool advanced = floor > Floor;
        Floor = Mathf.Max(Floor, floor);
        if (ProgressionManager.HasInstance && Floor > ProgressionManager.Instance.GetFlag("adventure.deepestFloor"))
        {
            ProgressionManager.Instance.SetFlag("adventure.deepestFloor", Floor);
        }
        if (advanced) player?.GetComponent<AdventurerProgress>()?.ReachedFloor();
    }
    public void NoteBossSlain() => BossesSlain++;
    public void RestoreTotals(int kills, int bosses, int gold, float seconds, int healthSlain = 0) { Kills=Mathf.Max(0,kills); BossesSlain=Mathf.Max(0,bosses); GoldFound=Mathf.Max(0,gold); Seconds=Mathf.Max(0,seconds); HealthSlain=Mathf.Max(0,healthSlain); }

    // Spoils carried and the kills' health go into the bank before anything else hears the run end.
    void Settle(bool victory)
    {
        LastTally = ProgressionManager.HasInstance ? ProgressionManager.Instance.Settle(player, Kills, HealthSlain, victory) : default;
    }

    public void EndRunInVictory()
    {
        if (!Running || Ended) return;
        Ended = true; won = true;
        Settle(true);
        RunEnded?.Invoke(true);
        if (AudioManager.HasInstance && victorySting != null) AudioManager.Instance.PlaySFX2D(victorySting, victoryStingVolume);
        MessageLog.Post($"You beat {PlaceName(true)}.", MessageKind.Good);
        ShowRecap(true);
    }

    // Returns false when there is no recap screen, so the caller falls back to its old menu.
    // The death is banked and saved (Settle, RunEnded) on the frame of death. Only once the dev console has been opened
    // (Editor and development builds) does it wait for the recap, so the console's revive costs nothing until then.
    public bool PlayDeath()
    {
        if (!Running || Ended || recap == null) return false;
        Ended = true;
        deathPending = true;
        CountDeath();
        if (!DeferDeath) CommitDeath();
        StopDeathMoment();
        deathRoutine = StartCoroutine(DeathMoment());
        return true;
    }

    // Set by the dev console: DeferDeath once it has been opened (a death waits for the recap before it is banked),
    // HoldDeath while it is open (the death moment waits too). Never set in a release build.
    public static bool DeferDeath, HoldDeath;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetDeathHolds() => DeferDeath = HoldDeath = false;
    bool deathPending;
    // The run ended in victory (EndRunInVictory), not a death.
    bool won;

    // Deaths over the whole save, in its flags (not the intro's practice run): the death screen's Revive button unlocks
    // after RunRecapUI.reviveAfterDeaths of them.
    public const string DeathsFlag = "story.deaths";
    static void CountDeath()
    {
        if (!ProgressionManager.HasInstance || !ProgressionManager.MetaActive) return;
        var progression = ProgressionManager.Instance;
        progression.SetFlag(DeathsFlag, progression.GetFlag(DeathsFlag) + 1);
    }

    void CommitDeath()
    {
        if (!deathPending) return;
        deathPending = false;
        Settle(false);
        RunEnded?.Invoke(false);
    }

    // The dev console's revive: the run goes on after a death. Before the recap came up the death was never banked, so
    // nothing changes; after it, the death had been banked and saved, so the tallies restart from here and nothing is
    // banked twice. Returns whether the death had already been counted. RevivePlayer raises Revived after it.
    public bool ReviveRun(out bool counted)
    {
        counted = false;
        // A won run stays won: a player who falls after it (nothing on the floor stops) gets up without reopening it.
        if (!Running || !Ended || won) return false;
        counted = !deathPending;
        deathPending = false;
        StopDeathMoment();
        recap?.Hide();
        Ended = false;
        if (counted) { Kills = 0; HealthSlain = 0; }
        Killer = null;
        return true;
    }

    // Back on your feet after dying, the run going on: the dev console's revive and the recap's Revive (debug) button.
    // Run: a run was going on; counted: its death had already been banked (see ReviveRun).
    public static bool RevivePlayer(Player player, out bool run, out bool counted)
    {
        run = counted = false;
        if (player == null || player.IsAlive) return false;
        run = HasInstance && Instance.ReviveRun(out counted);
        player.Stats.Revive();
        var animator = player.Animator;
        if (animator != null && Character.HasParameter(animator, "Dead", AnimatorControllerParameterType.Bool)) animator.SetBool("Dead", false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
        // Raised with the player up again, so listeners see them alive (the save's checkpoint, the fights on the floor).
        if (run) Instance.Revived?.Invoke(counted);
        return true;
    }

    public void Abandon()
    {
        CommitDeath();
        StopDeathMoment();
        Running = false;
        recap?.Hide();
    }

    // ── Death ─────────────────────────────────────────────────────────

    IEnumerator DeathMoment()
    {
        if (AudioManager.HasInstance && deathSting != null) AudioManager.Instance.PlaySFX2D(deathSting, deathStingVolume);
        float elapsed = 0f;
        while (elapsed < slowMotionSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            // Re-assert every frame: a hit stop ending mid-fall restores time to 1.
            Time.timeScale = Mathf.Lerp(slowMotionScale, 1f, Mathf.Clamp01(elapsed / slowMotionSeconds) * .25f);
            if (ScreenManager.HasInstance) ScreenManager.Instance.SetDeathEffect(Mathf.Clamp01(elapsed / desaturateSeconds));
            yield return null;
        }
        Time.timeScale = 1f;
        while (HoldDeath) yield return null;
        deathRoutine = null;
        CommitDeath();
        ShowRecap(false);
    }

    void StopDeathMoment()
    {
        if (deathRoutine != null) { StopCoroutine(deathRoutine); deathRoutine = null; }
        Time.timeScale = 1f;
        if (ScreenManager.HasInstance) ScreenManager.Instance.SetDeathEffect(0f);
    }

    void ShowRecap(bool victory)
    {
        if (recap == null) return;
        // The intro's practice board ends back at the table, not on the recap.
        if (!victory && IntroController.HasInstance && IntroController.Instance.TakeOverDeath()) return;
        recap.Show(Summarize(victory));
    }

    // The recap hides; the colour comes back with whatever the player picks next.
    public void RecapClosed()
    {
        if (ScreenManager.HasInstance) ScreenManager.Instance.SetDeathEffect(0f);
    }

    public RunSummary Summarize(bool victory) => new RunSummary
    {
        victory = victory,
        levelName = PlaceName(victory),
        killer = Killer,
        floor = Floor,
        floors = Level != null ? Level.FloorCount : 1,
        kills = Kills,
        gold = GoldFound,
        bosses = BossesSlain,
        seconds = Seconds,
        seed = Seed,
        tally = LastTally,
        adventurer = Adventurer(out var skills),
        skills = skills,
    };

    // Where the run ended, for the recap's sentence ("You conquered the Crypt.", "on floor 2 of the Cellars"): the
    // floor's biome, else the level's name; a win in the campaign names the place on the map ("You conquered Hill
    // Tower."). A leading "The" is lowered so it reads mid-sentence.
    string PlaceName(bool victory)
    {
        // A level with no stages runs on its own base biome (often a copy named after the biome it came from, "Cellars"):
        // its place on the map, or its own name, says where it was, whatever the outcome.
        bool ownBiome = Level != null && (Level.stages == null || Level.stages.Length == 0);
        if ((victory || ownBiome) && CampaignManager.Active && CampaignManager.Instance.Current != null && CampaignManager.Instance.Current.level == Level)
        {
            string place = CampaignManager.Instance.Current.displayName.Trim();
            return place.StartsWith("The ") ? "the " + place.Substring(4) : place;
        }
        var biome = Level != null && !ownBiome ? Level.Biome(Mathf.Max(1, Floor)) : null;
        string name = biome != null && !string.IsNullOrWhiteSpace(biome.displayName) ? biome.displayName.Trim()
            : Level != null && !string.IsNullOrWhiteSpace(Level.displayName) ? Level.displayName.Trim() : "the dungeon";
        return name.StartsWith("The ") ? "the " + name.Substring(4) : name;
    }

    // "Level 4 Wizard" and the three best skills, for the recap.
    string Adventurer(out string skills)
    {
        skills = "";
        var progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        if (progress == null || progress.selectedClass == null) return "";
        var best = new System.Collections.Generic.List<AdventureSkill>(AdventureSkills.All);
        best.RemoveAll(x => progress.Rank(x) <= 0);
        best.Sort((a, b) => progress.Rank(b).CompareTo(progress.Rank(a)));
        var parts = new System.Collections.Generic.List<string>();
        for (int i = 0; i < best.Count && i < 3; i++) parts.Add($"{progress.Definition(best[i])?.displayName ?? best[i].ToString()} {progress.Rank(best[i])}");
        skills = string.Join("   ·   ", parts);
        return $"Level {progress.Level} {progress.selectedClass.displayName}";
    }

    // ── Listeners ─────────────────────────────────────────────────────

    void OnPlayerDamaged(DamageInfo info)
    {
        if (!Running || info.Amount <= 0f || info.Blocked) return;
        Killer = info.Source != null && info.Source != player ? info.Source.DisplayName
            : !string.IsNullOrWhiteSpace(info.SourceName) ? info.SourceName
            : info.Source == player ? "a curse" : "the dungeon";
    }

    void OnGold(int total, int delta)
    {
        if (Running && !Ended && delta > 0) GoldFound += delta;
    }

    void OnAnyDied(Character c)
    {
        if (!Running || Ended || c == null || c is Player || c.GetComponent<EnemyBrain>() == null) return;
        // Only the player's kills count and give XP; their summons' kills are theirs (Summon.Commander).
        if (player == null || Summon.Commander(c.LastDamage.Source) != player) return;
        // A summoned body is no kill: a necromancer would raise it again and again.
        if (c.GetComponent<Summon>() != null) return;
        Kills++;
        if (c.Stats != null) HealthSlain += Mathf.RoundToInt(c.Stats.MaxHealth);
        var progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        if (progress == null || progress.rules == null) return;
        // Tougher enemies are worth more: an authored value, or one scaled from maximum health.
        var data = c.GetComponent<EnemyBrain>().Data;
        float xp = data != null && data.experience > 0 ? data.experience
                 : progress.rules.xpPerEnemy * Mathf.Clamp(c.Stats != null ? c.Stats.MaxHealth / 20f : 1f, .5f, 6f);
        progress.GainExperience(xp * (1 + (Floor - 1) * .12f));
    }
}

public struct RunSummary
{
    public bool victory;
    public string adventurer, skills;
    public string levelName, killer;
    public int floor, floors, kills, gold, bosses, seed;
    public ProgressionManager.Tally tally;
    public float seconds;

    public string TimeText
    {
        get
        {
            var t = TimeSpan.FromSeconds(seconds);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
        }
    }
}

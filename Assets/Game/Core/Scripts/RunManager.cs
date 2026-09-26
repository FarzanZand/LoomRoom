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
    public float Seconds { get; private set; }
    public string Killer { get; private set; }
    public bool Ended { get; private set; }

    public event Action RunStarted;
    public event Action<bool> RunEnded; // victory

    Player player;
    Coroutine deathRoutine;

    void Start()
    {
        player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        if (player != null)
        {
            player.Damaged += OnPlayerDamaged;
            if (player.Wallet != null) player.Wallet.Changed += OnGold;
        }
        Character.AnyDied += OnAnyDied;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (player != null)
        {
            player.Damaged -= OnPlayerDamaged;
            if (player.Wallet != null) player.Wallet.Changed -= OnGold;
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
        Running = true; Ended = false;
        Level = level; Seed = seed; Floor = 1;
        Kills = 0; GoldFound = 0; BossesSlain = 0; Seconds = 0f; Killer = null;
        if (player != null)
        {
            player.Stats?.RemoveAllFromSource(BlessingSource);
            player.Wallet?.Clear();
        }
        recap?.Hide();
        RunStarted?.Invoke();
    }

    public void ReachFloor(int floor) => Floor = Mathf.Max(Floor, floor);
    public void NoteBossSlain() => BossesSlain++;

    public void EndRunInVictory()
    {
        if (!Running || Ended) return;
        Ended = true;
        RunEnded?.Invoke(true);
        if (AudioManager.HasInstance && victorySting != null) AudioManager.Instance.PlaySFX2D(victorySting, victoryStingVolume);
        MessageLog.Post($"You conquer {Level?.displayName}!", MessageKind.Good);
        ShowRecap(true);
    }

    // Returns false when there is no recap screen, so the caller falls back to its old menu.
    public bool PlayDeath()
    {
        if (!Running || Ended || recap == null) return false;
        Ended = true;
        RunEnded?.Invoke(false);
        StopDeathMoment();
        deathRoutine = StartCoroutine(DeathMoment());
        return true;
    }

    public void Abandon()
    {
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
        deathRoutine = null;
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
        levelName = Level != null ? Level.displayName : "the dungeon",
        killer = Killer,
        floor = Floor,
        floors = Level != null && Level.multipleLevels ? Mathf.Max(1, Level.levelCount) : 1,
        kills = Kills,
        gold = GoldFound,
        bosses = BossesSlain,
        seconds = Seconds,
        seed = Seed,
    };

    // ── Listeners ─────────────────────────────────────────────────────

    void OnPlayerDamaged(DamageInfo info)
    {
        if (!Running || info.Amount <= 0f || info.Blocked) return;
        Killer = info.Source == null ? "the dungeon" : info.Source == player ? "a curse" : info.Source.DisplayName;
    }

    void OnGold(int total, int delta)
    {
        if (Running && !Ended && delta > 0) GoldFound += delta;
    }

    void OnAnyDied(Character c)
    {
        if (!Running || Ended || c == null || c is Player || c.GetComponent<EnemyBrain>() == null) return;
        Kills++;
    }
}

public struct RunSummary
{
    public bool victory;
    public string levelName, killer;
    public int floor, floors, kills, gold, bosses, seed;
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

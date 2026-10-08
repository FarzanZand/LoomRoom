using System.Collections;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

// When the Dungeon Master speaks. Listens to the game (waking, sitting down, runs starting and ending,
// the looping door, a forgotten figure, idling, the ending) and says the lines typed here. During a run
// it says the level's own Dialogue entries (TableLevelData: trigger, chance, lines). The introduction is a
// Dialogue Database conversation started by talking to the DM (DungeonMasterSeat). Sits on the Dialogue Manager.
public class DungeonMasterRemarks : MonoBehaviour
{
    [Header("Room")]
    [Tooltip("After the opening wake-up.")] public DungeonMaster.Line morning;
    [Tooltip("After waking, until the player has talked to the DM once.")] public DungeonMaster.Line callOver;
    [Tooltip("After walking about for a while without sitting down.")] public DungeonMaster.Line idle;
    [Tooltip("The first time the door loops back into the room.")] public DungeonMaster.Line door;
    [Tooltip("When the door has looped a few times in one session.")] public DungeonMaster.Line doorAgain;
    [Tooltip("The first time the player looks at a blank figure on the memorial table.")] public DungeonMaster.Line forgottenFigure;
    [Min(0), Tooltip("Seconds after waking before the greeting.")] public float greetingDelay = 1.5f;
    [Min(5), Tooltip("Seconds of walking about after waking, without sitting down, before a nudge.")] public float idleSeconds = 75f;
    [Min(2), Tooltip("The door's second remark comes on this many loops in one session.")] public int doorLoopAgainAt = 4;
    [Header("Table")]
    [Tooltip("Sitting down after a death.")] public DungeonMaster.Line again;
    [Tooltip("Sitting down after a death deeper than any before.")] public DungeonMaster.Line further;
    [Tooltip("Sitting down after two deaths to the same enemy. {killer} is its name.")] public DungeonMaster.Line sameKiller;
    [Tooltip("Sitting down the first time after the ending.")] public DungeonMaster.Line afterTheEnd;
    [Tooltip("Sitting down after a class was unlocked. Its figure is picked first.")] public DungeonMaster.Line newFigure;

    [Header("Runs")]
    [Tooltip("Run start, with the class the player has lost most often.")] public DungeonMaster.Line usualPick;
    [Tooltip("Run start, with a class the player has lost before.")] public DungeonMaster.Line samePick;
    [Tooltip("Run start, with a class none of the figures has.")] public DungeonMaster.Line newPick;
    [InfoBox("What he says during a run (kills, close calls, new floors, rooms) is set on each level's Dialogue tab.")]
    [Range(.05f, .5f), Tooltip("Health left, as a fraction, that counts as a close call.")] public float closeCallFraction = .25f;
    [Min(0), Tooltip("Seconds between two run remarks.")] public float remarkGap = 25f;
    [Min(0), Tooltip("Seconds after arriving in the dungeon before a remark.")] public float runRemarkDelay = 1.5f;

    public const string SatFlag = "story.sat", OfferedPrefix = "story.offered.", LoopedFlag = "story.loopedDoor", ForgottenSeenFlag = "story.forgottenSeen";
    const string DeepestFlag = "adventure.deepestFloor";

    AdventureSave save;
    AdventurerProgress progress;
    bool died, won, beatRecord, saidDeeper, satThisMorning, nudged;
    int recordAtRunStart, loops;
    readonly System.Collections.Generic.HashSet<DungeonDialogue> saidThisRun = new();
    int lastRoom = -1;
    float lastRemark = -999f;
    Player tablePlayer;
    float idleTime;
    Coroutine greeting;

    void Start()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        save = player != null ? player.GetComponent<AdventureSave>() : null;
        progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        var world = WorldManager.HasInstance ? WorldManager.Instance : null;
        if (world != null && world.wakeUpCutscene != null) world.wakeUpCutscene.Woke += OnWoke;
        if (world != null && world.ending != null) world.ending.Finished += OnEndingFinished;
        if (TableManager.HasInstance) TableManager.Instance.Seated += OnSeated;
        if (RunManager.HasInstance) { RunManager.Instance.RunStarted += OnRunStarted; RunManager.Instance.RunEnded += OnRunEnded; }
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged += OnFlag;
        RoomLoopPortal.Crossed += OnLooped;
        AdventureMemorial.Inspected += OnInspected;
        tablePlayer = player;
        Character.AnyDied += OnAnyDied;
        if (tablePlayer != null) { tablePlayer.HitLanded += OnHitLanded; tablePlayer.Damaged += OnPlayerDamaged; }
        if (progress != null) progress.Changed += OnProgress;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp += OnPickedUp;
    }

    void OnDestroy()
    {
        var world = WorldManager.HasInstance ? WorldManager.Instance : null;
        if (world != null && world.wakeUpCutscene != null) world.wakeUpCutscene.Woke -= OnWoke;
        if (world != null && world.ending != null) world.ending.Finished -= OnEndingFinished;
        if (TableManager.HasInstance) TableManager.Instance.Seated -= OnSeated;
        if (RunManager.HasInstance) { RunManager.Instance.RunStarted -= OnRunStarted; RunManager.Instance.RunEnded -= OnRunEnded; }
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlag;
        RoomLoopPortal.Crossed -= OnLooped;
        AdventureMemorial.Inspected -= OnInspected;
        Character.AnyDied -= OnAnyDied;
        if (tablePlayer != null) { tablePlayer.HitLanded -= OnHitLanded; tablePlayer.Damaged -= OnPlayerDamaged; }
        if (progress != null) progress.Changed -= OnProgress;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp -= OnPickedUp;
    }

    static bool Flag(string key) => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(key);
    static void SetFlag(string key) { if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(key); }
    static void Say(DungeonMaster.Line line) { if (line != null) DungeonMaster.Say(line); }
    static bool EndingRunning => WorldManager.HasInstance && WorldManager.Instance.ending != null && WorldManager.Instance.ending.Running;
    static bool RoomPlayerFree => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
        && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;

    // ── The room ──────────────────────────────────────────────────────

    void OnWoke(bool brief)
    {
        idleTime = 0f; nudged = false; satThisMorning = false;
        if (greeting != null) StopCoroutine(greeting);
        // The intro's own night wake-up has its own words.
        greeting = !brief && !EndingRunning && !IntroController.Seated ? StartCoroutine(Greet()) : null;
    }

    // "Morning", and until the player has sat down once, a call to the table.
    IEnumerator Greet()
    {
        yield return new WaitForSeconds(greetingDelay);
        greeting = null;
        if (!RoomPlayerFree || satThisMorning) yield break;
        // The morning after the intro has its own words.
        var first = IntroController.HasInstance ? IntroController.Instance.TakeFirstMorning() : null;
        if (first != null && first.Length > 0) { foreach (var line in first) Say(line); yield break; }
        Say(morning);
        if (!Flag(SatFlag)) Say(callOver);
    }

    void Update()
    {
        WatchRooms();
        if (nudged || satThisMorning || EndingRunning || !RoomPlayerFree) return;
        idleTime += Time.deltaTime;
        if (idleTime > idleSeconds) { nudged = true; Say(idle); }
    }

    // Walking into a room of the floor plan (EnterRoom entries).
    void WatchRooms()
    {
        if (!RunOn || tablePlayer == null || !TableManager.HasInstance) return;
        var dungeon = TableManager.Instance.GetComponent<TableLevelLoader>()?.Dungeon;
        if (dungeon == null || dungeon.Layout == null) return;
        int room = dungeon.Layout.RoomAt(dungeon.CellOf(tablePlayer.transform.position));
        if (room == lastRoom || room < 0) return;
        lastRoom = room;
        Trigger(DungeonDialogueTrigger.EnterRoom, room);
    }

    void OnLooped(Player player)
    {
        if (player == null || player.kind != PlayerKind.Room || EndingRunning) return;
        loops++;
        if (!Flag(LoopedFlag)) { SetFlag(LoopedFlag); Say(door); }
        else if (loops == doorLoopAgainAt) Say(doorAgain);
    }

    void OnInspected(AdventureSave.Memorial record)
    {
        if (record == null || !record.forgotten || Flag(ForgottenSeenFlag)) return;
        SetFlag(ForgottenSeenFlag);
        Say(forgottenFigure);
    }

    void OnEndingFinished() { won = true; died = false; }

    // ── The table ─────────────────────────────────────────────────────

    void OnSeated(Player player)
    {
        if (greeting != null) { StopCoroutine(greeting); greeting = null; }
        satThisMorning = true;
        // The intro does the talking until the room opens.
        if (IntroController.Seated || IntroController.SheetPending) { died = won = beatRecord = false; return; }
        if (won) Say(afterTheEnd);
        else if (died)
        {
            string killer = LastTwoKiller();
            if (beatRecord) Say(further);
            else if (killer != null && sameKiller != null) DungeonMaster.Say(sameKiller.With(sameKiller.text.Replace("{killer}", killer)));
            else Say(again);
        }
        died = won = beatRecord = false;

        // A class unlocked since the last time: said, and its figure picked first.
        var classes = progress != null && progress.rules != null ? progress.rules.classes : System.Array.Empty<AdventurerClass>();
        var unlocked = classes.Where(c => c != null && !string.IsNullOrEmpty(c.unlockFlag) && c.Unlocked && !Flag(OfferedPrefix + c.id)).ToList();
        if (unlocked.Count == 0) return;
        Say(newFigure);
        if (!progress.InRun) progress.SelectClass(unlocked[0]);
        foreach (var c in unlocked) SetFlag(OfferedPrefix + c.id);
    }

    // The killer, when the last two deaths came from the same enemy.
    string LastTwoKiller()
    {
        var m = save != null ? save.Saved.memorials : null;
        if (m == null || m.Count < 2) return null;
        string killer = m[m.Count - 1].killer;
        return !string.IsNullOrWhiteSpace(killer) && killer == m[m.Count - 2].killer ? killer : null;
    }

    // ── Runs ──────────────────────────────────────────────────────────

    // The deepest anyone has been: this save's record or any remembered miniature.
    int Record()
    {
        int floor = ProgressionManager.HasInstance ? ProgressionManager.Instance.GetFlag(DeepestFlag) : 0;
        if (save != null) foreach (var m in save.Saved.memorials) if (!m.forgotten) floor = Mathf.Max(floor, m.floor);
        return floor;
    }

    // ── During a run ──────────────────────────────────────────────────

    static bool RunOn => RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended;

    // The level's Dialogue entries for this trigger: the first that passes its chance (and has not had
    // its one go this run) is said. Spaced out so the DM never chatters.
    void Trigger(DungeonDialogueTrigger trigger, int room = -1, float delay = .6f)
    {
        if (!RunOn || IntroController.PracticeRunning || Time.time - lastRemark < remarkGap) return;
        var entries = RunManager.Instance.Level != null ? RunManager.Instance.Level.dialogue : null;
        if (entries == null) return;
        foreach (var entry in entries)
        {
            if (entry == null || entry.trigger != trigger || (trigger == DungeonDialogueTrigger.EnterRoom && entry.room != room)) continue;
            if (entry.oncePerRun && saidThisRun.Contains(entry)) continue;
            if (Random.value > entry.chance) continue;
            var line = entry.Pick();
            if (line == null) continue;
            saidThisRun.Add(entry); lastRemark = Time.time;
            DungeonMaster.Say(line, null, delay);
            return;
        }
    }

    void OnAnyDied(Character c)
    {
        if (c != null && !(c is Player) && c.GetComponent<EnemyBrain>() != null) Trigger(DungeonDialogueTrigger.Kill);
    }

    void OnHitLanded(DamageInfo info) { if (info.Backstab && !info.Blocked) Trigger(DungeonDialogueTrigger.Backstab); }

    void OnPlayerDamaged(DamageInfo info)
    {
        var stats = tablePlayer != null ? tablePlayer.Stats : null;
        if (stats == null || info.Blocked || !tablePlayer.IsAlive || stats.MaxHealth <= 0) return;
        if (stats.CurrentHealth / stats.MaxHealth < closeCallFraction) Trigger(DungeonDialogueTrigger.CloseCall);
        else Trigger(DungeonDialogueTrigger.Hurt);
    }

    int levelSeen = 1;
    void OnProgress()
    {
        if (progress == null) return;
        if (progress.Level > levelSeen && progress.Level >= 2) Trigger(DungeonDialogueTrigger.LevelUp);
        levelSeen = progress.Level;
    }

    void OnPickedUp(ItemData item, Player who, int count)
    {
        if (item == null || who != tablePlayer || !RunOn) return;
        int best = 0;
        foreach (var e in who.Equipment.EquippedItems) if (e != null && e != item) best = Mathf.Max(best, e.tier);
        if (item.tier > 0 && item.tier > best) Trigger(DungeonDialogueTrigger.BetterGear);
        else Trigger(DungeonDialogueTrigger.PickUp);
    }

    void OnRunStarted()
    {
        saidThisRun.Clear(); levelSeen = 1; lastRoom = -1;
        lastRemark = Time.time - remarkGap + 8f; // not right on top of the run-start remark
        recordAtRunStart = Record();
        saidDeeper = false;
        StartCoroutine(CommentOnClass());
    }

    void OnRunEnded(bool victory)
    {
        if (DungeonMaster.HasInstance) DungeonMaster.Instance.Silence();
        if (!victory) died = true;
        beatRecord = RunManager.HasInstance && recordAtRunStart >= 1 && RunManager.Instance.Floor > recordAtRunStart;
    }

    IEnumerator CommentOnClass()
    {
        // A resumed run restores its class and totals right after it starts.
        yield return null;
        if (progress == null || progress.selectedClass == null || save == null || !RunManager.HasInstance || IntroController.PracticeRunning) yield break;
        if (RunManager.Instance.Floor > 1 || RunManager.Instance.Seconds > 0) yield break;
        var loader = TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
        if (loader != null && loader.Resumed) yield break; // continuing, not picking
        var memorials = save.Saved.memorials;
        int count = memorials.Count(m => m.classId == progress.selectedClass.id);
        int most = memorials.Count == 0 ? 0 : memorials.GroupBy(m => m.classId).Max(g => g.Count());
        SayInDungeon(count == 0 ? newPick : count >= 2 && count == most ? usualPick : samePick);
        // The level's own opening remark, after the class comment.
        yield return new WaitForSeconds(runRemarkDelay + .5f);
        lastRemark = -999f;
        Trigger(DungeonDialogueTrigger.RunStart, -1, runRemarkDelay);
    }

    void OnFlag(string key, int value)
    {
        if (key != DeepestFlag || saidDeeper || recordAtRunStart < 1 || value <= recordAtRunStart) return;
        if (!RunManager.HasInstance || !RunManager.Instance.Running || RunManager.Instance.Ended) return;
        saidDeeper = true;
        Trigger(DungeonDialogueTrigger.NewDeepestFloor, -1, runRemarkDelay);
    }

    // Queued now, while the floor is still being set up: the DM feed holds it through the descent and
    // says it a moment after arrival, ahead of the biome's own entry line.
    void SayInDungeon(DungeonMaster.Line line)
    {
        if (line != null && !line.IsEmpty) DungeonMaster.Say(line.With(line.text), null, runRemarkDelay);
    }
}

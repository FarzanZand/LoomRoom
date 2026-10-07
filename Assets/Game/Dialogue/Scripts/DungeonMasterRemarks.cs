using System.Collections;
using System.Linq;
using UnityEngine;

// When the Dungeon Master speaks. Listens to the game (waking, sitting down, runs starting and ending,
// a new deepest floor, the looping door, a forgotten figure, idling, the ending) and picks what is
// said: a DMLine asset for the simple lines (Dialogue/Lines/{Room,Table,Dungeon}), or a Dialogue
// Database conversation for the first introduction. Sits on the Dialogue Manager.
public class DungeonMasterRemarks : MonoBehaviour
{
    [Header("Room")]
    [Tooltip("After the opening wake-up.")] public DMLine morning;
    [Tooltip("After waking, until the player has sat down once.")] public DMLine callOver;
    [Tooltip("After walking about for a while without sitting down.")] public DMLine idle;
    [Tooltip("The first time the door loops back into the room.")] public DMLine door;
    [Tooltip("When the door has looped a few times in one session.")] public DMLine doorAgain;
    [Tooltip("The first time the player looks at a blank figure on the memorial table.")] public DMLine forgottenFigure;
    [Min(0), Tooltip("Seconds after waking before the greeting.")] public float greetingDelay = 1.5f;
    [Min(5), Tooltip("Seconds of walking about after waking, without sitting down, before a nudge.")] public float idleSeconds = 75f;
    [Min(2), Tooltip("The door's second remark comes on this many loops in one session.")] public int doorLoopAgainAt = 4;
    [Header("Table")]
    [Tooltip("Dialogue Database conversation, the first time the player sits down.")] public string introduction = "Table/Introduction";
    [Tooltip("Sitting down after a death.")] public DMLine again;
    [Tooltip("Sitting down after a death deeper than any before.")] public DMLine further;
    [Tooltip("Sitting down after two deaths to the same enemy. {killer} is its name.")] public DMLine sameKiller;
    [Tooltip("Sitting down the first time after the ending.")] public DMLine afterTheEnd;
    [Tooltip("Sitting down after a class was unlocked. Its figure is picked first.")] public DMLine newFigure;

    [Header("Runs")]
    [Tooltip("Run start, with the class the player has lost most often.")] public DMLine usualPick;
    [Tooltip("Run start, with a class the player has lost before.")] public DMLine samePick;
    [Tooltip("Run start, with a class none of the figures has.")] public DMLine newPick;
    [Tooltip("Once a run, on the first floor deeper than any before.")] public DMLine deeper;
    [Min(0), Tooltip("Seconds after arriving in the dungeon before a remark.")] public float runRemarkDelay = 1.5f;

    public const string SatFlag = "story.sat", OfferedPrefix = "story.offered.", LoopedFlag = "story.loopedDoor", ForgottenSeenFlag = "story.forgottenSeen";
    const string DeepestFlag = "adventure.deepestFloor";

    AdventureSave save;
    AdventurerProgress progress;
    bool died, won, beatRecord, saidDeeper, satThisMorning, nudged;
    int recordAtRunStart, loops;
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
    }

    static bool Flag(string key) => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(key);
    static void SetFlag(string key) { if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(key); }
    static void Say(DMLine line) => DungeonMaster.Say(line);
    static bool EndingRunning => WorldManager.HasInstance && WorldManager.Instance.ending != null && WorldManager.Instance.ending.Running;
    static bool RoomPlayerFree => PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
        && GameManager.HasInstance && GameManager.Instance.State == GameState.Explore;

    // ── The room ──────────────────────────────────────────────────────

    void OnWoke(bool brief)
    {
        idleTime = 0f; nudged = false; satThisMorning = false;
        if (greeting != null) StopCoroutine(greeting);
        greeting = !brief && !EndingRunning ? StartCoroutine(Greet()) : null;
    }

    // "Morning", and until the player has sat down once, a call to the table.
    IEnumerator Greet()
    {
        yield return new WaitForSeconds(greetingDelay);
        greeting = null;
        if (!RoomPlayerFree || satThisMorning) yield break;
        Say(morning);
        if (!Flag(SatFlag)) Say(callOver);
    }

    void Update()
    {
        if (nudged || satThisMorning || EndingRunning || !RoomPlayerFree) return;
        idleTime += Time.deltaTime;
        if (idleTime > idleSeconds) { nudged = true; Say(idle); }
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
        if (!Flag(SatFlag))
        {
            // The first time: who they are and what the game is. Replaces anything still waiting to be said.
            if (DungeonMaster.HasInstance) DungeonMaster.Instance.Silence();
            var dm = TableManager.Instance.DM;
            DialogueBridge.StartConversation(introduction, player.transform, dm != null ? dm.transform : TableManager.Instance.transform);
        }
        else if (won) Say(afterTheEnd);
        else if (died)
        {
            string killer = LastTwoKiller();
            if (beatRecord) Say(further);
            else if (killer != null && sameKiller != null) DungeonMaster.Say(sameKiller.With(sameKiller.text.Replace("{killer}", killer)));
            else Say(again);
        }
        SetFlag(SatFlag);
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

    void OnRunStarted()
    {
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
        if (progress == null || progress.selectedClass == null || save == null || !RunManager.HasInstance) yield break;
        if (RunManager.Instance.Floor > 1 || RunManager.Instance.Seconds > 0) yield break;
        var memorials = save.Saved.memorials;
        int count = memorials.Count(m => m.classId == progress.selectedClass.id);
        int most = memorials.Count == 0 ? 0 : memorials.GroupBy(m => m.classId).Max(g => g.Count());
        SayInDungeon(count == 0 ? newPick : count >= 2 && count == most ? usualPick : samePick);
    }

    void OnFlag(string key, int value)
    {
        if (key != DeepestFlag || saidDeeper || recordAtRunStart < 1 || value <= recordAtRunStart) return;
        if (!RunManager.HasInstance || !RunManager.Instance.Running || RunManager.Instance.Ended) return;
        saidDeeper = true;
        SayInDungeon(deeper);
    }

    // Queued now, while the floor is still being set up: the DM feed holds it through the descent and
    // says it a moment after arrival, ahead of the biome's own entry line.
    void SayInDungeon(DMLine line)
    {
        if (line != null) DungeonMaster.Say(line.With(line.text), null, runRemarkDelay);
    }
}

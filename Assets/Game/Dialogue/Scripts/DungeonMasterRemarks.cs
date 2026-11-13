using System.Collections;
using System.Linq;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// When the Dungeon Master speaks. Listens to the game (waking, sitting down, runs starting and ending,
// the looping door, idling, the ending) and says the lines typed here. During a run
// it says the level's own Dialogue entries (TableLevelData: trigger, chance, lines). The introduction is a
// Dialogue Database conversation started by talking to the DM (DungeonMasterSeat). Sits on the Dialogue Manager.
public class DungeonMasterRemarks : MonoBehaviour
{
    // Each remark is a list of lines: one is picked at random each time (one line is fine for things said once).
    const string Tabs = "Tabs";
    [TabGroup(Tabs, "Room"), Tooltip("Waking up in the morning.")] public List<DungeonMaster.Line> morning = new();
    [TabGroup(Tabs, "Room"), Tooltip("After waking, until the player has talked to him once.")] public List<DungeonMaster.Line> callOver = new();
    [TabGroup(Tabs, "Room"), Tooltip("Walking about for a while without sitting down.")] public List<DungeonMaster.Line> idle = new();
    [TabGroup(Tabs, "Room"), Tooltip("The first time the door loops back into the room.")] public List<DungeonMaster.Line> door = new();
    [TabGroup(Tabs, "Room"), Tooltip("When the door has looped a few times in one session.")] public List<DungeonMaster.Line> doorAgain = new();
    [TabGroup(Tabs, "Room"), Min(0), Tooltip("Seconds after waking before the greeting.")] public float greetingDelay = 1.5f;
    [TabGroup(Tabs, "Room"), Min(5), Tooltip("Seconds of walking about without sitting down before the nudge.")] public float idleSeconds = 75f;
    [TabGroup(Tabs, "Room"), Min(2), Tooltip("Loops of the door in one session before its second remark.")] public int doorLoopAgainAt = 4;

    [TabGroup(Tabs, "Table"), Tooltip("Sitting down after a death.")] public List<DungeonMaster.Line> again = new();
    [TabGroup(Tabs, "Table"), Tooltip("Sitting down after a death deeper than any before.")] public List<DungeonMaster.Line> further = new();
    [TabGroup(Tabs, "Table"), Tooltip("Sitting down after two deaths to the same enemy. {killer} is its name; \"the {killer}\" drops its \"the\" for a proper name (a boss's stage name).")] public List<DungeonMaster.Line> sameKiller = new();
    [TabGroup(Tabs, "Table"), Tooltip("Sitting down the first time after the ending.")] public List<DungeonMaster.Line> afterTheEnd = new();
    [TabGroup(Tabs, "Table"), Tooltip("Sitting down after a class was unlocked (its figure is picked first).")] public List<DungeonMaster.Line> newFigure = new();

    [TabGroup(Tabs, "Runs"), Tooltip("Run start with the class the player has lost most often.")] public List<DungeonMaster.Line> usualPick = new();
    [TabGroup(Tabs, "Runs"), Tooltip("Run start with a class the player has lost before.")] public List<DungeonMaster.Line> samePick = new();
    [TabGroup(Tabs, "Runs"), Tooltip("Run start with a class none of the figures has.")] public List<DungeonMaster.Line> newPick = new();
    // The lines name the spell that hit him (the user, 2026-11-11: the old ones never said what happened, so they didn't
    // read as his answer to the fireball). The name is filled in between asterisks, so it shows in the highlight colour.
    [TabGroup(Tabs, "Runs"), Tooltip("The player's spell hits him from the dungeon (up through a skylight). Not during the boss fight, which has its own lines. {spell} is the spell's name in small letters, in the highlight colour (fireball); {a spell} has a or an in front, {A spell} starts a sentence.")]
    public List<DungeonMaster.Line> spellHitMe = new()
    {
        new() { text = "Ow! Did you just throw {a spell} at me?" },
        new() { text = "Hey! The {spell} is for the monsters. Not for me." },
        new() { text = "{A spell}. At me. Really?" },
        new() { text = "Ow! That {spell} hit me right in the face." },
        new() { text = "Ow! Aim the {spell} down there, not up here." },
    };
    [TabGroup(Tabs, "Runs"), Min(0), Tooltip("Seconds before he answers another spell at him.")] public float spellHitGap = 8f;
    // The user, 2026-11-11: comments when you cast a spell on yourself, meaning the room player: you standing at the
    // table while your figure plays. A spell flown up out of the dungeon into your own body there.
    [TabGroup(Tabs, "Runs"), Tooltip("The player's spell flies up out of the dungeon and hits the room player standing at the table (you). Not during a boss fight or the intro's practice. {spell}, {a spell} and {A spell} as above.")]
    public List<DungeonMaster.Line> ownSpellHit = new()
    {
        new() { text = "Did you just {spell} yourself?" },
        new() { text = "That's you standing there. You just hit yourself with {a spell}." },
        new() { text = "{A spell}. At yourself. Bold." },
        new() { text = "Aim the {spell} down there. You're up here." },
        new() { text = "The {spell} is for the monsters. You are not a monster." },
        new() { text = "I'm writing that down. {A spell}, cast on yourself." },
    };
    [TabGroup(Tabs, "Runs"), Min(0), Tooltip("Seconds before he remarks on another spell cast on yourself.")] public float ownSpellGap = 12f;
    // The user, 2026-11-13: an event's fighter that waits for the player to walk up and talk (Little Undead John, the
    // Bridge Troll, the Black Knight) is kept out of reach by him until the talk starts (EventProtection).
    [TabGroup(Tabs, "Runs"), Tooltip("A blow or spell on an event's fighter before the player has walked up and heard him out (the duel, the toll bridge, the Arena): he stops it. One at random, never the one said last.")]
    public List<DungeonMaster.Line> protectedHit = new()
    {
        new() { text = "Not yet. Let him have his say first." },
        new() { text = "Hold it. He hasn't said a word yet." },
        new() { text = "No. Walk up and hear him out." },
        new() { text = "He's waiting to talk to you. Go and talk." },
        new() { text = "Not until he's had his say." },
    };
    [TabGroup(Tabs, "Runs"), Min(0), Tooltip("Seconds before he stops another blow on a fighter who hasn't talked yet with a line (the blow is stopped either way).")] public float protectedHitGap = 4f;
    [TabGroup(Tabs, "Runs"), InfoBox("What he says during a run (kills, close calls, new floors, rooms) is set on each level's Dialogue tab.")]
    [Range(.05f, .5f), Tooltip("Health left, as a share, that counts as a close call.")] public float closeCallFraction = .25f;
    [TabGroup(Tabs, "Runs"), Min(0), Tooltip("Seconds between two run remarks.")] public float remarkGap = 25f;
    [TabGroup(Tabs, "Runs"), Min(0), Tooltip("Seconds after arriving in the dungeon before a remark.")] public float runRemarkDelay = 1.5f;

    public const string SatFlag = "story.sat", OfferedPrefix = "story.offered.", LoopedFlag = "story.loopedDoor";
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
        Character.AnyDied -= OnAnyDied;
        if (tablePlayer != null) { tablePlayer.HitLanded -= OnHitLanded; tablePlayer.Damaged -= OnPlayerDamaged; }
        if (progress != null) progress.Changed -= OnProgress;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp -= OnPickedUp;
    }

    static bool Flag(string key) => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(key);
    static void SetFlag(string key) { if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(key); }
    static void Say(DungeonMaster.Line line) { if (line != null) DungeonMaster.Say(line); }
    static void Say(List<DungeonMaster.Line> lines) => Say(Pick(lines));
    // One of the lines at random, skipping empty ones; null when there are none.
    static DungeonMaster.Line Pick(List<DungeonMaster.Line> lines)
    {
        if (lines == null || lines.Count == 0) return null;
        var usable = lines.FindAll(l => l != null && !l.IsEmpty);
        return usable.Count > 0 ? usable[Random.Range(0, usable.Count)] : null;
    }
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
        var dungeon = TableManager.Loader?.Dungeon;
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
            else if (killer != null && Pick(sameKiller) is DungeonMaster.Line kill) DungeonMaster.Say(kill.With(RunManager.FillKiller(kill.text, killer)));
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

    // The player took a class themselves (the hero select, from the map's figure or the poster wall): every class open
    // now counts as offered, so sitting down never swaps their pick for a newly unlocked one (the user, 2026-11-12: picked
    // the Wizard at the desk, then E at the table started a run as the Rogue the Arena had just unlocked).
    public static void PlayerPickedClass(AdventurerProgress progress)
    {
        var classes = progress != null && progress.rules != null ? progress.rules.classes : null;
        if (classes == null) return;
        foreach (var c in classes) if (c != null && c.Unlocked) SetFlag(OfferedPrefix + c.id);
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

    float lastSpellHit = -999f;
    // A player's projectile spell hit the Dungeon Master (SpellProjectile). Not during a boss fight on the stage.
    public static void SpellHit(Character caster, SpellDefinition spell)
    {
        var remarks = FindAnyObjectByType<DungeonMasterRemarks>();
        if (remarks == null || caster is not Player || Hold || BossStageEncounter.Current != null) return;
        if (Time.time - remarks.lastSpellHit < remarks.spellHitGap) return;
        var line = Pick(remarks.spellHitMe);
        if (line == null) return;
        remarks.lastSpellHit = Time.time;
        // Said at once, cutting off whatever he was saying or had queued: it answers the hit.
        DungeonMaster.CutIn(line.With(FillSpell(line.text, spell)), null, .3f);
        // His house rule about it (the user, 2026-11-13: "The Dungeon Master is not a target. However big he is."): given
        // after his answer the first time, on the sheet from then on, and quoted after his answer on later hits.
        if (!RuleManager.HasInstance) return;
        var rule = RuleManager.Instance.Rule(TargetRule);
        if (rule == null) return;
        if (RuleManager.Instance.Given(rule)) RuleManager.Instance.Quote(rule);
        else if (!remarks.givingRule) remarks.StartCoroutine(remarks.GiveRuleAfterLine(rule));
    }
    const int TargetRule = 4;   // the fourth rule on RuleManager (its number on the sheet is the order given)
    bool givingRule;
    // The rule is given as he starts to say it, once his answer has been read: the sheet and whatever else shows a new
    // rule (RuleManager.Give) then land with his words, not the six seconds of his answer ahead of them.
    IEnumerator GiveRuleAfterLine(HouseRule rule)
    {
        givingRule = true;
        yield return null;
        while (DungeonMaster.HasInstance && DungeonMaster.Instance.Speaking) yield return null;
        givingRule = false;
        if (RuleManager.HasInstance && !RuleManager.Instance.Given(rule)) DungeonMaster.Say(RuleManager.Instance.Give(rule));
    }

    float lastOwnSpell = -999f;
    static Player roomPlayer;
    static readonly List<Renderer> roomBody = new();
    static float roomBodyRead = -999f;

    // How far along this step of a player's spell its path meets the room player's body, standing at the table while
    // the table player plays (float.MaxValue: it doesn't). The body has no colliders then (its controller is switched
    // off with the room player), so its drawn bounds stand in, widened by the spell's radius.
    public static float RoomSelfAlong(Character caster, Vector3 from, Vector3 direction, float distance, float radius)
    {
        if (caster is not Player { kind: PlayerKind.Table } || !PlayerManager.HasInstance) return float.MaxValue;
        if (roomPlayer == null) roomPlayer = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
        if (roomPlayer == null || roomPlayer.gameObject.activeInHierarchy || !roomPlayer.ActivationRoot.activeInHierarchy) return float.MaxValue;
        // The body's renderers, read again now and then (a class or body change swaps them).
        if (Time.time - roomBodyRead > 1f || roomBody.Count == 0)
        {
            roomBodyRead = Time.time;
            roomBody.Clear();
            foreach (var r in roomPlayer.ActivationRoot.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && (r is SkinnedMeshRenderer || r is MeshRenderer)) roomBody.Add(r);
        }
        bool any = false; var bounds = new Bounds();
        foreach (var r in roomBody)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
        }
        if (!any) return float.MaxValue;
        bounds.Expand(radius * 2f);
        if (bounds.Contains(from)) return 0f;
        return bounds.IntersectRay(new Ray(from, direction), out float at) && at <= distance ? at : float.MaxValue;
    }

    // A player's spell hit the room player at the table (SpellProjectile). Not during a boss fight or the intro's
    // practice, where he says other things.
    // A blow or spell on a fighter he keeps out of reach until it has talked (EventProtection.Stops): one of his lines,
    // said at once (it answers the blow), not again within the gap.
    public static void ProtectedHit()
    {
        var remarks = FindAnyObjectByType<DungeonMasterRemarks>();
        if (remarks == null || Time.time - remarks.lastProtectedHit < remarks.protectedHitGap) return;
        var usable = remarks.protectedHit != null ? remarks.protectedHit.FindAll(l => l != null && !l.IsEmpty && l != remarks.lastProtectedLine) : null;
        var line = usable != null && usable.Count > 0 ? usable[Random.Range(0, usable.Count)] : Pick(remarks.protectedHit);
        if (line == null) return;
        remarks.lastProtectedHit = Time.time;
        remarks.lastProtectedLine = line;
        DungeonMaster.CutIn(line, null, .15f);
        // His house rule about it (the user, 2026-11-13): given after this line the first time ("*New rule:* Let them
        // have their say before you hit them."), and on later hits quoted after it, as the rule itself with no number.
        if (!RuleManager.HasInstance) return;
        var rule = RuleManager.Instance.Rule(SayRule);
        if (rule == null) return;
        if (RuleManager.Instance.Given(rule)) { if (rule.quote != null && !rule.quote.IsEmpty) DungeonMaster.Say(rule.quote); }
        else if (!remarks.givingRule) remarks.StartCoroutine(remarks.GiveRuleAfterLine(rule));
    }
    const int SayRule = 5;   // the fifth rule on RuleManager: let them have their say
    float lastProtectedHit = -999f;
    DungeonMaster.Line lastProtectedLine;

    public static void SpellHitRoomSelf(Character caster, SpellDefinition spell)
    {
        if (caster is not Player { kind: PlayerKind.Table } || Hold || BossStageEncounter.Current != null || IntroController.PracticeRunning) return;
        var remarks = FindAnyObjectByType<DungeonMasterRemarks>();
        if (remarks == null || Time.time - remarks.lastOwnSpell < remarks.ownSpellGap) return;
        var line = Pick(remarks.ownSpellHit);
        if (line == null) return;
        remarks.lastOwnSpell = Time.time;
        DungeonMaster.Say(line.With(FillSpell(line.text, spell)), null, .5f);
    }

    // {spell}, {a spell} and {A spell} filled with the spell's name, highlighted ("a *fireball*").
    public static string FillSpell(string text, SpellDefinition spell)
    {
        string name = spell != null && !string.IsNullOrWhiteSpace(spell.displayName) ? spell.displayName.Trim().ToLowerInvariant() : "spell";
        string article = "aeiou".IndexOf(name[0]) >= 0 ? "an" : "a";
        return text.Replace("{a spell}", $"{article} *{name}*")
                   .Replace("{A spell}", $"{char.ToUpperInvariant(article[0])}{article.Substring(1)} *{name}*")
                   .Replace("{spell}", $"*{name}*");
    }

    static bool RunOn => RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended;

    // While a boss fight is on the stage the DM only comments on the fight (BossStageEncounter); his other remarks wait.
    public static bool Hold { get; set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetHold() { Hold = false; roomPlayer = null; roomBody.Clear(); roomBodyRead = -999f; }

    // The level's Dialogue entries for this trigger: the first that passes its chance (and has not had
    // its one go this run) is said. Spaced out so the DM never chatters.
    void Trigger(DungeonDialogueTrigger trigger, int room = -1, float delay = .6f)
    {
        if (!RunOn || Hold || IntroController.PracticeRunning || Time.time - lastRemark < remarkGap) return;
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
        if (!victory) { died = true; satThisMorning = false; idleTime = 0f; nudged = false; }
        beatRecord = RunManager.HasInstance && recordAtRunStart >= 1 && RunManager.Instance.Floor > recordAtRunStart;
    }

    string lastRunClass;   // the class of the last run started this session (the campaign's class stays put)

    IEnumerator CommentOnClass()
    {
        // A resumed run restores its class and totals right after it starts.
        yield return null;
        if (progress == null || progress.selectedClass == null || save == null || !RunManager.HasInstance || IntroController.PracticeRunning) yield break;
        if (RunManager.Instance.Floor > 1 || RunManager.Instance.Seconds > 0) yield break;
        var loader = TableManager.Loader;
        if (loader != null && loader.Resumed) yield break; // continuing, not picking
        var memorials = save.Saved.memorials;
        // On the campaign the class is only picked when it changes (the hero select): the same class as last run was
        // not picked again, so there is nothing to remark on.
        string classId = progress.selectedClass.id;
        bool picked = !CampaignManager.Active || lastRunClass == null ? !(CampaignManager.Active && memorials.Count > 0 && memorials[memorials.Count - 1].classId == classId) : lastRunClass != classId;
        lastRunClass = classId;
        if (picked)
        {
            int count = memorials.Count(m => m.classId == classId);
            int most = memorials.Count == 0 ? 0 : memorials.GroupBy(m => m.classId).Max(g => g.Count());
            SayInDungeon(count == 0 ? newPick : count >= 2 && count == most ? usualPick : samePick);
        }
        // The level's own opening remark, after the class comment.
        yield return new WaitForSeconds(picked ? runRemarkDelay + .5f : .5f);
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
    void SayInDungeon(List<DungeonMaster.Line> lines)
    {
        var line = Pick(lines);
        if (line != null && !line.IsEmpty) DungeonMaster.Say(line.With(line.text), null, runRemarkDelay);
    }
}

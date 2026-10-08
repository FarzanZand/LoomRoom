using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

// The first launch, Inscryption style. The player opens already seated at the table in the dark, across
// from the Dungeon Master (only his eyes show). He lights the lamp, says what the game is and sets out
// one figure for the IntroDungeon: a small table level where he teaches as you go (TutorialStep) and a
// death you cannot avoid. Back at the table he says a few words and puts the lamp out; the player wakes
// in bed for the first morning and the room is theirs. WorldManager asks TryPlay before the wake-up.
public class IntroController : Singleton<IntroController>
{
    public const string IntroFlag = "story.intro", RoomOpenFlag = "story.roomOpen";

    [Header("Seat")]
    [Tooltip("Where the room player sits through the intro, facing the Dungeon Master.")]
    public Transform seat;
    [Tooltip("Seated eye height, in the player's own units (standing is 1).")]
    public float eyeHeight = .55f;
    [Tooltip("Field of view change while seated (negative zooms in).")]
    public float zoom = -24f;
    [Tooltip("How far below the Dungeon Master's head the view rests (world units): his chest and the near table.")]
    public float aimBelowHead = 7f;

    [Header("Darkness")]
    [Tooltip("The room while the intro runs, the same setting a level has: Dark is black but for the lamp.")]
    public RoomLighting roomLighting = RoomLighting.Dark;
    [Tooltip("The colour the room's light takes during the intro. White leaves it as it is. The lamp's colour is on the lamp.")]
    [ColorUsage(false)] public Color roomTint = Color.white;
    [Tooltip("The one lamp over the corner of the table. Off until the Dungeon Master lights it.")]
    public Light lamp;
    [Tooltip("The lamp's colour (warm yellow, white, red ...). Changes live, also in Play mode. The practice board's own light is on the IntroDungeon level (Look tab).")]
    [ColorUsage(false)] public Color lampColor = new(1f, .76f, .48f);
    [Tooltip("The Dungeon Master's eyes (GlowInDark: they show only while he sits in the dark). Closed for the night at the end of the intro.")]
    public GameObject dmEyes;
    public LightingManager lighting;
    [Min(0)] public float openingBlack = 2f;
    [Min(0)] public float openingFade = 3f;

    [Header("Opening")]
    [Tooltip("Said in the dark.")] public DungeonMaster.Line[] beforeLamp = new DungeonMaster.Line[0];
    [Tooltip("Said once the lamp is on, before the practice figure comes out.")] public DungeonMaster.Line[] afterLamp = new DungeonMaster.Line[0];
    [Tooltip("Played when the lamp comes on.")] public AudioClip lampSound;

    [Header("Practice board")]
    [Tooltip("The intro's own small dungeon (Levels/IntroDungeon): a normal table level with a tutorial-sized layout and its own biome.")]
    [FormerlySerializedAs("practiceLevel")] public TableLevelData introDungeon;
    [Tooltip("The one figure set out for the practice board.")]
    public AdventurerClass practiceClass;
    [Tooltip("Said once the practice figure is down.")] public DungeonMaster.Line takeIt;
    [Min(0), Tooltip("Seconds after arriving before the first step.")] public float firstStepDelay = 2f;
    public TutorialStep[] steps = new TutorialStep[0];
    [Tooltip("Said once if the player stands still for Idle Seconds during the practice.")] public DungeonMaster.Line idleLine;
    [Min(5)] public float idleSeconds = 22f;
    [Tooltip("Said the first time the practice figure gets hurt.")] public DungeonMaster.Line hurtLine;

    [Header("The end of the practice")]
    [Tooltip("Walking into this room (its number on the floor plan) ends the practice: the killer comes up the stairs. -1: after the last step.")]
    public int finaleRoom = -1;
    [Tooltip("Said when the steps are done, before the killer arrives.")] public DungeonMaster.Line enough;
    [Tooltip("Said before the killer appears.")] public DungeonMaster.Line killerComing;
    [Tooltip("Enemy that ends the practice. It comes up behind the player.")] public GameObject killer;
    [Min(1)] public float killerHealth = 10f;
    [Min(1)] public float killerDamage = 3f;
    [Tooltip("Said as the killer arrives.")] public DungeonMaster.Line killerLine;
    [Min(1), Tooltip("If the player is still alive after this many seconds, the Dungeon Master ends it himself.")]
    public float killerSeconds = 20f;
    [Tooltip("Said when he ends it himself.")] public DungeonMaster.Line timeUp;

    [Header("Back at the table")]
    [Tooltip("After the practice death, with the lamp still on.")] public DungeonMaster.Line[] afterPractice = new DungeonMaster.Line[0];
    [Tooltip("Said first when the killer did it.")] public DungeonMaster.Line diedAtEnd;
    [Tooltip("Said first instead when something else killed the figure before the end. {killer} is its name.")] public DungeonMaster.Line diedEarly;
    [Tooltip("The last words before he puts the lamp out.")] public DungeonMaster.Line[] goodnight = new DungeonMaster.Line[0];
    [Tooltip("Played when the lamp goes out.")] public AudioClip lampOutSound;
    [Min(0), Tooltip("Seconds of darkness with only his eyes before they close.")] public float eyesLinger = 2.5f;
    [Min(0), Tooltip("Seconds of black before the first morning.")] public float nightSeconds = 2f;
    [Tooltip("Said once the player is up on the first morning, instead of the usual greeting.")] public DungeonMaster.Line[] firstMorning = new DungeonMaster.Line[0];

    [Header("Testing")]
    [Tooltip("Editor only: start with the wake-up as before.")]
    public bool skipInEditor;

    // The intro owns the start: the player stays seated in the dark until the first real run ends.
    public static bool Seated => HasInstance && Instance.running;
    // The practice board is being played (no saved checkpoint, no run remarks).
    public static bool PracticeRunning => HasInstance && Instance.introDungeon != null && RunManager.HasInstance
        && RunManager.Instance.Running && RunManager.Instance.Level == Instance.introDungeon;

    bool running;
    float lampIntensity;
    LightingManager.Snapshot? roomLight;
    Player room, table;
    TableLevelLoader loader;

    // Running totals for the steps.
    float walked, guarded;
    int hits, kills, pickups;

    protected override void Awake()
    {
        base.Awake();
        if (lamp != null) { lampIntensity = lamp.intensity; lamp.enabled = false; lamp.color = lampColor; }
        if (lighting == null) lighting = FindAnyObjectByType<LightingManager>();
    }

    void OnDestroy()
    {
        if (RunManager.HasInstance) RunManager.Instance.RunStarted -= OnRunStarted;
        Unlisten();
    }

    static bool Flag(string key) => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(key);

    // Called by WorldManager on the first frame. False: the normal wake-up plays.
    public bool TryPlay()
    {
        if (Application.isEditor && skipInEditor) return false;
        if (!PlayerManager.HasInstance || seat == null) return false;
        room = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
        table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        // The flags come from the save; it may not have been read yet.
        var save = table != null ? table.GetComponent<AdventureSave>() : null;
        if (save != null) save.Initialize();
        // Played once: a game that got as far as the practice death starts with the mornings.
        if (room == null || Flag(RoomOpenFlag) || Flag(IntroFlag)) return false;
        loader = TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
        if (loader == null || introDungeon == null) return false;

        running = true;
        if (RunManager.HasInstance) { RunManager.Instance.RunStarted -= OnRunStarted; RunManager.Instance.RunStarted += OnRunStarted; }
        StartCoroutine(Opening());
        return true;
    }

    // ── At the table ──────────────────────────────────────────────────

    IEnumerator Opening()
    {
        GameManager.Instance.Push(GameState.Dialogue);
        Darken();
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeIn(0f);
        yield return new WaitForSeconds(openingBlack);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeOut(openingFade);
        yield return new WaitForSeconds(openingFade);
        yield return SayAll(beforeLamp);
        yield return LampOn();
        yield return SayAll(afterLamp);
        GameManager.Instance.Pop(GameState.Dialogue);
        yield return WaitForTable();
        TableManager.Instance.Play(introDungeon, practiceClass, takeIt);
    }

    // After the practice death: the board is gone, the lamp is still on.
    IEnumerator BackAtTable()
    {
        GameManager.Instance.Push(GameState.Dialogue);
        Darken();
        LightLamp(1f);
        if (RunManager.HasInstance) RunManager.Instance.RecapClosed();
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeOut(1.5f, .8f);
        yield return new WaitForSeconds(2.3f);
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(IntroFlag);
        // Killed by the rat or the slime before the end: that is remarked on first.
        if (endedByFinale) yield return SayAll(new[] { diedAtEnd });
        else if (diedEarly != null)
        {
            string killer = RunManager.HasInstance && !string.IsNullOrEmpty(RunManager.Instance.Killer) ? RunManager.Instance.Killer : "the dungeon";
            if (!killer.StartsWith("the ") && !killer.StartsWith("a ")) killer = ("aeiouAEIOU".IndexOf(killer[0]) >= 0 ? "an " : "a ") + killer.ToLower();
            DungeonMaster.Say(diedEarly.With(diedEarly.text.Replace("{killer}", killer)));
            yield return null;
            while (DungeonMaster.HasInstance && DungeonMaster.Instance.Speaking) yield return null;
        }
        yield return SayAll(afterPractice);
        yield return new WaitForSeconds(.6f);
        yield return SayAll(goodnight);
        yield return new WaitForSeconds(.8f);
        yield return LampOut();
        yield return new WaitForSeconds(eyesLinger);
        if (dmEyes != null) dmEyes.SetActive(false);
        yield return new WaitForSeconds(nightSeconds);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeIn(.01f);
        yield return null;
        GameManager.Instance.Pop(GameState.Dialogue);
        OpenRoom();
        firstMorningPending = true;
        if (WorldManager.HasInstance && WorldManager.Instance.wakeUpCutscene != null) WorldManager.Instance.wakeUpCutscene.Play(false);
        else if (ScreenManager.HasInstance) ScreenManager.Instance.FadeOut(1.5f);
    }

    bool firstMorningPending;

    // The first morning's greeting, once (DungeonMasterRemarks says it instead of the usual one).
    public DungeonMaster.Line[] TakeFirstMorning()
    {
        if (!firstMorningPending) return null;
        firstMorningPending = false;
        return firstMorning;
    }

    // The flame dies down: a few weak flickers and out.
    IEnumerator LampOut()
    {
        if (lamp == null) yield break;
        if (lampOutSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(lampOutSound, 1f);
        float[] flicker = { .7f, .85f, .45f, .6f, .25f, .35f, .1f };
        foreach (var level in flicker)
        {
            LightLamp(level);
            yield return new WaitForSeconds(Random.Range(.07f, .16f));
        }
        LightLamp(0f);
    }

    // The intro is over: the lights come back and the room is the player's. The caller wakes them.
    public void OpenRoom()
    {
        if (!running) return;
        running = false;
        Unlisten();
        if (ProgressionManager.HasInstance)
        {
            ProgressionManager.Instance.SetFlag(IntroFlag);
            ProgressionManager.Instance.SetFlag(DungeonMasterRemarks.SatFlag);
            ProgressionManager.Instance.SetFlag(RoomOpenFlag);
        }
        if (lamp != null) lamp.enabled = false;
        // Open again for good: they only glow when he is in the dark.
        if (dmEyes != null) dmEyes.SetActive(true);
        if (lighting != null) lighting.lampHeld = false;
        if (lighting != null && roomLight.HasValue) lighting.Restore(roomLight.Value, 0f);
        roomLight = null;
        if (room != null && room.Look != null) room.Look.HeightOverride = null;
        if (room != null && room.CameraRig != null) room.CameraRig.FovOffset = 0f;
    }

    void Darken()
    {
        if (lighting != null)
        {
            roomLight ??= lighting.Capture();
            lighting.lampHeld = true;
            lighting.BlendToMood(lighting.RoomLook(roomLighting, roomTint), 0f);
        }
        if (dmEyes != null) dmEyes.SetActive(true);
        Sit();
    }

    // In the chair at the corner of the table, looking at the Dungeon Master.
    void Sit()
    {
        if (room == null) return;
        if (room.CameraRig != null) room.CameraRig.FovOffset = zoom;
        room.Warp(seat.position);
        room.transform.rotation = Quaternion.Euler(0f, seat.eulerAngles.y, 0f);
        if (room.Look == null) return;
        room.Look.HeightOverride = eyeHeight;
        room.Look.SetYaw(seat.eulerAngles.y);
        // Level with the Dungeon Master: the seat's own pitch if he cannot be found.
        float pitch = seat.eulerAngles.x;
        pitch = pitch > 180f ? 360f - pitch : -pitch;
        var dm = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.GetComponent<Animator>() : null;
        var head = dm != null && dm.isHuman ? dm.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null)
        {
            var eye = seat.position + Vector3.up * (room.Look.PitchTransform.position.y - room.transform.position.y);
            var d = head.position + Vector3.down * aimBelowHead - eye;
            room.Look.SetYaw(Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)).eulerAngles.y);
            pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        }
        room.Look.SetPitch(pitch);
    }

    IEnumerator LampOn()
    {
        if (lamp == null) yield break;
        if (lampSound != null && AudioManager.HasInstance) AudioManager.Instance.PlaySFX2D(lampSound, 1f);
        // A wick catching: a few flickers, then steady.
        float[] flicker = { .35f, 0f, .6f, .15f, .8f, .5f, 1f };
        foreach (var level in flicker)
        {
            LightLamp(level);
            yield return new WaitForSeconds(Random.Range(.05f, .14f));
        }
        LightLamp(1f);
        yield return new WaitForSeconds(.8f);
    }

    // At the table the lamp lights everything; the Lighting Manager limits it to the room during a dungeon.
    void OnValidate() { if (lamp != null) lamp.color = lampColor; }

    void LightLamp(float level)
    {
        if (lamp == null) return;
        lamp.color = lampColor;
        lamp.enabled = level > 0f;
        lamp.intensity = lampIntensity * level;
        FirstPersonLighting.SetLayers(lamp, uint.MaxValue);
    }

    static IEnumerator SayAll(DungeonMaster.Line[] lines)
    {
        foreach (var line in lines)
        {
            if (line == null) continue;
            DungeonMaster.Say(line);
            yield return null;
            while (DungeonMaster.HasInstance && DungeonMaster.Instance.Speaking) yield return null;
        }
    }

    IEnumerator WaitForTable()
    {
        while (loader != null && loader.Busy) yield return null;
        yield return null;
    }

    // ── The practice board ────────────────────────────────────────────

    void OnRunStarted()
    {
        if (!running || !PracticeRunning) return;
        StopAllCoroutines();
        teaching = StartCoroutine(Teach());
    }

    IEnumerator Teach()
    {
        // On the board the lamp lights only the room around it; the board has its own light (its colour is the level's).
        if (lamp != null) FirstPersonLighting.SetLayers(lamp, LightingManager.RoomLayer);
        walked = guarded = 0f; hits = kills = pickups = 0; finale = false; fromStairs = false;
        stillFor = 0f; saidIdle = saidHurt = endedByFinale = false;
        Listen();
        while (loader.Busy) yield return null;
        yield return new WaitForSeconds(firstStepDelay);
        Vector3 last = table.transform.position;
        if (finaleRoom >= 0) StartCoroutine(WatchFinaleRoom());
        foreach (var step in steps)
        {
            if (step == null || Met(step)) continue;
            while (step.room >= 0 && PlayerRoom() != step.room)
            {
                if (!PracticeRunning || !table.IsAlive) yield break;
                Track(ref last);
                yield return null;
            }
            if (Met(step)) continue;
            yield return SayAll(new[] { step.line });
            var hint = Hint(step.hint);
            bool hinted = string.IsNullOrEmpty(hint);
            float started = Time.time;
            while (!Met(step) && (step.giveUpAfter <= 0f || Time.time - started < step.giveUpAfter))
            {
                if (!PracticeRunning || !table.IsAlive) yield break;
                Track(ref last);
                // The controls only if the player has not worked it out.
                if (!hinted && Time.time - started >= step.hintDelay) { hinted = true; MessageLog.Post(hint, MessageKind.Info); }
                if (step.goal == TutorialGoal.Wait && Time.time - started >= step.amount) break;
                yield return null;
            }
            if (Met(step) && step.doneLine != null) yield return SayAll(new[] { step.doneLine });
        }
        yield return Finale();
    }

    bool finale, fromStairs;
    Coroutine teaching;

    // The player's room on the floor plan, -1 in a passage.
    int PlayerRoom()
    {
        var dungeon = loader != null ? loader.Dungeon : null;
        return dungeon != null && dungeon.Layout != null ? dungeon.Layout.RoomAt(dungeon.CellOf(table.transform.position)) : -1;
    }

    // Reaching the finale room ends the lessons wherever they are.
    IEnumerator WatchFinaleRoom()
    {
        while (PracticeRunning && !finale)
        {
            if (PlayerRoom() == finaleRoom) { fromStairs = true; if (teaching != null) StopCoroutine(teaching); StartCoroutine(Finale()); yield break; }
            yield return null;
        }
    }

    IEnumerator Finale()
    {
        finale = true; endedByFinale = true;
        yield return SayAll(new[] { enough });
        yield return new WaitForSeconds(.6f);
        yield return SayAll(new[] { killerComing });
        yield return EndPractice();
    }

    // Called by TableLevelLoader at the last stairs. The practice board cannot be won: reaching the
    // stairs early only brings the end sooner.
    public bool StairsReached()
    {
        if (!running || !PracticeRunning) return false;
        if (!finale) { fromStairs = true; StopAllCoroutines(); StartCoroutine(Finale()); }
        return true;
    }

    float stillFor;
    bool saidIdle, saidHurt, endedByFinale;

    void Track(ref Vector3 last)
    {
        var p = table.transform.position;
        float moved = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(last.x, 0f, last.z));
        walked += moved;
        // Standing about: one remark, once.
        stillFor = moved > .001f ? 0f : stillFor + Time.deltaTime;
        if (!saidIdle && idleLine != null && stillFor > idleSeconds && !DungeonMaster.Instance.Speaking) { saidIdle = true; DungeonMaster.Say(idleLine); }
        last = p;
        if (table.Combat != null && table.Combat.IsGuarding) guarded += Time.deltaTime;
    }

    bool Met(TutorialStep step) => step.goal switch
    {
        TutorialGoal.Walk => walked >= step.amount,
        TutorialGoal.Hit => hits >= step.amount,
        TutorialGoal.Kill => kills >= step.amount,
        TutorialGoal.PickUp => pickups >= step.amount,
        TutorialGoal.Guard => guarded >= step.amount,
        _ => false,
    };

    // Something the player cannot beat comes up behind them. If they keep away from it for long
    // enough, the Dungeon Master ends it anyway.
    IEnumerator EndPractice()
    {
        Character brute = SpawnKiller();
        yield return SayAll(new[] { killerLine });
        float until = Time.time + killerSeconds;
        while (PracticeRunning && table.IsAlive && Time.time < until) yield return null;
        if (!PracticeRunning || !table.IsAlive) yield break;
        yield return SayAll(new[] { timeUp });
        if (!table.IsAlive || table.Stats == null) yield break;
        // He ends it himself: the figure's memorial says so.
        var info = DamageInfo.Simple(table.Stats.CurrentHealth + 1f);
        info.Magic = true; info.FromEffect = true;
        info.SourceName = "the Dungeon Master";
        table.Stats.TakeDamage(info);
    }

    Character SpawnKiller()
    {
        if (killer == null || loader.Dungeon == null) return null;
        // Up the stairs, when the player has come to them.
        // Up the stairs, when the player has come to them: out of the stairwell's mouth, not inside its stone
        // housing (a board's exit is a solid stairwell, and the exit point is its middle).
        if (fromStairs && StairsMouth(out var stairs))
            return Configure(Instantiate(killer, stairs, Quaternion.LookRotation(Vector3.ProjectOnPlane(table.transform.position - stairs, Vector3.up).sqrMagnitude > .001f ? Vector3.ProjectOnPlane(table.transform.position - stairs, Vector3.up) : Vector3.forward), loader.Dungeon.transform));
        var view = table.Look != null ? table.Look.YawTransform.forward : table.transform.forward;
        view.y = 0f; view.Normalize();
        // Behind the player if there is floor there in plain sight, otherwise to a side, otherwise ahead.
        var from = table.transform.position + Vector3.up * .8f;
        Vector3 at = table.transform.position;
        bool found = false;
        foreach (var dir in new[] { -view, Vector3.Cross(Vector3.up, view), -Vector3.Cross(Vector3.up, view), view })
            foreach (var distance in new[] { 5f, 4f, 3f, 2f })
            {
                var probe = table.transform.position + dir * distance;
                if (!NavMesh.SamplePosition(probe, out var hit, .8f, NavMesh.AllAreas)) continue;
                if (Physics.Linecast(from, hit.position + Vector3.up * .8f, out var wall, ~0, QueryTriggerInteraction.Ignore)
                    && wall.collider.GetComponentInParent<Character>() == null) continue;
                at = hit.position; found = true; break;
            }
        if (!found && NavMesh.SamplePosition(table.transform.position, out var near, 2f, NavMesh.AllAreas)) at = near.position;
        return Configure(Instantiate(killer, at, Quaternion.LookRotation(Vector3.ProjectOnPlane(table.transform.position - at, Vector3.up).sqrMagnitude > .001f ? Vector3.ProjectOnPlane(table.transform.position - at, Vector3.up) : Vector3.forward), loader.Dungeon.transform));
    }

    // Floor just outside the exit stairwell's opening (its local -z), clear of the stonework.
    bool StairsMouth(out Vector3 at)
    {
        at = loader.Dungeon.ExitPoint;
        DungeonExit exit = null;
        foreach (var e in loader.Dungeon.GetComponentsInChildren<DungeonExit>(true)) if (!e.entrance) { exit = e; break; }
        var mouth = exit != null ? exit.transform.position - exit.transform.forward * 1.6f : at;
        foreach (var distance in new[] { 0f, .6f, 1.2f })
        {
            var probe = mouth - (exit != null ? exit.transform.forward : Vector3.zero) * distance;
            if (!NavMesh.SamplePosition(probe, out var hit, 1f, NavMesh.AllAreas)) continue;
            // Not inside anything solid.
            if (Physics.CheckCapsule(hit.position + Vector3.up * .5f, hit.position + Vector3.up * 1.4f, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            at = hit.position;
            return true;
        }
        return false;
    }

    Character Configure(GameObject go)
    {
        var character = go.GetComponent<Character>();
        if (character != null && character.Stats != null)
        {
            character.Stats.AddModifier(new StatModifier(StatType.MaxHealth, killerHealth - 1f, ModifierType.PercentMultiply, this));
            character.Stats.AddModifier(new StatModifier(StatType.AttackDamage, killerDamage - 1f, ModifierType.PercentMultiply, this));
            character.Stats.Heal(character.Stats.MaxHealth);
        }
        var drop = go.GetComponent<DungeonLootDrop>();
        if (drop != null) drop.enabled = false;
        go.GetComponent<EnemyBrain>()?.Alert();
        return character;
    }

    // Called by RunManager instead of the run recap. True: the practice death is handled here.
    public bool TakeOverDeath()
    {
        if (!running || !PracticeRunning) return false;
        StopAllCoroutines();
        Unlisten();
        StartCoroutine(AfterPracticeDeath());
        return true;
    }

    IEnumerator AfterPracticeDeath()
    {
        yield return new WaitForSecondsRealtime(.6f);
        bool dark = false;
        loader.ReturnToRoom(() => dark = true);
        while (!dark) yield return null;
        // The board is gone by the time the lamp shows the table again.
        while (loader.Busy) yield return null;
        loader.ClearTable();
        StartCoroutine(BackAtTable());
    }

    // ── Listening ─────────────────────────────────────────────────────

    bool listening;

    void Listen()
    {
        if (listening || table == null) return;
        listening = true;
        table.HitLanded += OnHit;
        table.Damaged += OnHurt;
        Character.AnyDied += OnAnyDied;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp += OnPickedUp;
    }

    void Unlisten()
    {
        if (!listening) return;
        listening = false;
        if (table != null) { table.HitLanded -= OnHit; table.Damaged -= OnHurt; }
        Character.AnyDied -= OnAnyDied;
        if (InventoryManager.HasInstance) InventoryManager.Instance.ItemPickedUp -= OnPickedUp;
    }

    void OnHit(DamageInfo info) { if (!info.FromEffect) hits++; }
    void OnHurt(DamageInfo info)
    {
        if (saidHurt || finale || hurtLine == null || info.Amount <= 0f || info.Blocked || !table.IsAlive) return;
        saidHurt = true; DungeonMaster.Say(hurtLine);
    }
    void OnAnyDied(Character c) { if (c != null && !(c is Player) && c.GetComponent<EnemyBrain>() != null) kills++; }
    void OnPickedUp(ItemData item, Player who, int count) { if (who == table) pickups++; }

    // "{PrimaryAction}" becomes the Table map's binding, "Left Button" and the like.
    static string Hint(string text)
    {
        if (string.IsNullOrEmpty(text) || !InputManager.HasInstance || InputManager.Instance.Actions == null) return text;
        var asset = InputManager.Instance.Actions.asset;
        var result = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('{', i), close = open < 0 ? -1 : text.IndexOf('}', open);
            if (open < 0 || close < 0) { result.Append(text, i, text.Length - i); break; }
            result.Append(text, i, open - i);
            string name = text.Substring(open + 1, close - open - 1);
            var action = asset.FindAction("Table/" + name);
            result.Append(action != null ? KeyboardBinding(action) : name);
            i = close + 1;
        }
        return result.ToString();
    }

    // The keyboard and mouse binding only: "W/A/S/D", not every device's.
    static string KeyboardBinding(InputAction action)
    {
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (b.isPartOfComposite) continue;
            bool keyboard = b.isComposite
                ? i + 1 < bindings.Count && bindings[i + 1].effectivePath.StartsWith("<Keyboard>")
                : b.effectivePath.StartsWith("<Keyboard>") || b.effectivePath.StartsWith("<Mouse>");
            if (!keyboard) continue;
            string text = action.GetBindingDisplayString(i);
            return text == "LMB" ? "left mouse" : text == "RMB" ? "right mouse" : text;
        }
        return action.GetBindingDisplayString();
    }
}

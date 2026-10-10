using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

// The first launch. Two openings (IntroOpening): Lamp, the player seated at the table in the dark across
// from the Dungeon Master (only his eyes show) until he lights the lamp; or Tabletop (IntroController.Tabletop.cs),
// waking in bed at night and walking to the lamp-lit table for a character sheet and dice. Either way he sets out
// one figure for the IntroDungeon: a small table level where he teaches as you go (TutorialStep) and a
// death you cannot avoid. Back at the table he says a few words and puts the lamp out; the player wakes
// in bed for the first morning and the room is theirs. WorldManager asks TryPlay before the wake-up.
public partial class IntroController : Singleton<IntroController>
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
    [Tooltip("The room during the Lamp opening, the same setting a level has: Dark is black but for the lamp.")]
    [ShowIf(nameof(IsLampOpening))] public RoomLighting roomLighting = RoomLighting.Dark;
    [Tooltip("The colour the room's light takes during the Lamp opening. White leaves it as it is.")]
    [ShowIf(nameof(IsLampOpening)), ColorUsage(false)] public Color roomTint = Color.white;
    [Tooltip("The room during the Tabletop opening (night): Night leaves the room and the Dungeon Master visible before the lamp.")]
    [ShowIf(nameof(IsTabletopOpening)), LabelText("Room Lighting")] public RoomLighting tabletopRoomLighting = RoomLighting.Night;
    [Tooltip("The colour the room's light takes during the Tabletop opening. White leaves it as it is; a blue reads as city light at night.")]
    [ShowIf(nameof(IsTabletopOpening)), LabelText("Room Tint"), ColorUsage(false)] public Color tabletopRoomTint = Color.white;
    bool IsLampOpening => opening == IntroOpening.Lamp;
    bool IsTabletopOpening => opening == IntroOpening.Tabletop;
    [Tooltip("The one lamp over the corner of the table. Off until the Dungeon Master lights it.")]
    public Light lamp;
    [Tooltip("The intro's colour scheme: the lamp, the Dungeon Master's eyes, the practice board's light and the class card. Changes live, also in Play mode.")]
    [OnValueChanged(nameof(ApplyStyle))] public IntroStyle style = IntroStyle.Cold;
    [Tooltip("What each style looks like. Edit a style's colours or card art here.")]
    [ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = false), OnValueChanged(nameof(ApplyStyle), true)]
    public IntroLook[] looks = new IntroLook[0];
    // The lamp's colour comes from the style.
    Color lampColor = new(1f, .76f, .48f);
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

    // The intro is running (from TryPlay until OpenRoom, after the practice death): it owns the room
    // player's seat at the table, so the table does not let them stand up.
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
        ApplyStyle();
        if (lamp != null) { lampIntensity = lamp.intensity; lamp.enabled = false; lamp.color = lampColor; }
        SetupOpening();
    }

    void OnDisable() => PopDialogue();

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (RunManager.HasInstance) RunManager.Instance.RunStarted -= OnRunStarted;
        if (InputManager.HasInstance) InputManager.Instance.SubmitPressed -= OnSubmit;
        Unlisten();
        PopDialogue();
    }

    // The intro's own Dialogue state: pushed once, popped once, also when it is cut short.
    bool ownsDialogue;

    void PushDialogue()
    {
        if (ownsDialogue || !GameManager.HasInstance) return;
        GameManager.Instance.Push(GameState.Dialogue);
        ownsDialogue = true;
    }

    void PopDialogue()
    {
        if (!ownsDialogue) return;
        ownsDialogue = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
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
        // Called from WorldManager.Start, after every OnEnable: the Lighting Manager has registered.
        if (lighting == null) lighting = LightingManager.Instance;
        if (RunManager.HasInstance) { RunManager.Instance.RunStarted -= OnRunStarted; RunManager.Instance.RunStarted += OnRunStarted; }
        StartCoroutine(Tabletop ? TabletopNight() : Opening());
        return true;
    }

    // ── At the table ──────────────────────────────────────────────────

    IEnumerator Opening()
    {
        PushDialogue();
        Darken();
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(0f);
        yield return new WaitForSeconds(openingBlack);
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(openingFade);
        yield return new WaitForSeconds(openingFade);
        yield return SayAll(beforeLamp);
        yield return LampOn();
        yield return SayAll(afterLamp);
        PopDialogue();
        yield return WaitForTable();
        PlayPractice(true);
    }

    // The practice figure goes on the table. The table refuses while it is busy or the player cannot use it.
    void PlayPractice(bool showCard)
    {
        if (!TableManager.HasInstance || !TableManager.Instance.Play(introDungeon, practiceClass, takeIt, showCard))
            Debug.LogWarning("[IntroController] The table refused the practice board (TableManager.Play): the intro cannot go on.", this);
    }

    // After the practice death: the board is gone, the lamp is still on.
    IEnumerator BackAtTable()
    {
        PushDialogue();
        Darken();
        if (Tabletop && dmEyes != null) dmEyes.SetActive(false);
        LightLamp(1f);
        if (RunManager.HasInstance) RunManager.Instance.RecapClosed();
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(1.5f, .8f);
        yield return new WaitForSeconds(2.3f);
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(IntroFlag);
        // Killed by the rat or the slime before the end: that is remarked on first.
        if (endedByFinale) yield return SayAll(new[] { diedAtEnd });
        else if (diedEarly != null)
        {
            string killer = RunManager.KillerWithArticle(RunManager.HasInstance ? RunManager.Instance.Killer : null, lowerName: true);
            DungeonMaster.Say(diedEarly.With(diedEarly.text.Replace("{killer}", killer)));
            yield return null;
            while (DungeonMaster.HasInstance && DungeonMaster.Instance.Speaking) yield return null;
        }
        if (Tabletop) yield return TabletopGoodnight();
        else
        {
            yield return SayAll(afterPractice);
            yield return new WaitForSeconds(.6f);
            yield return SayAll(goodnight);
            yield return new WaitForSeconds(.8f);
            yield return LampOut();
            yield return new WaitForSeconds(eyesLinger);
            if (dmEyes != null) dmEyes.SetActive(false);
            yield return new WaitForSeconds(nightSeconds);
        }
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(.01f);
        yield return null;
        PopDialogue();
        OpenRoom();
        firstMorningPending = true;
        if (WorldManager.HasInstance && WorldManager.Instance.wakeUpCutscene != null) WorldManager.Instance.wakeUpCutscene.Play(false);
        else if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(1.5f);
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
            lighting.BlendToMood(Tabletop ? lighting.RoomLook(tabletopRoomLighting, tabletopRoomTint) : lighting.RoomLook(roomLighting, roomTint), 0f);
        }
        if (dmEyes != null) dmEyes.SetActive(true);
        Sit();
    }

    // In the chair at the corner of the table, looking at the Dungeon Master.
    void Sit()
    {
        if (room == null) return;
        if (room.CameraRig != null) room.CameraRig.FovOffset = zoom;
        room.Warp(seat.position, Quaternion.Euler(0f, seat.eulerAngles.y, 0f));
        if (room.Look == null) return;
        room.Look.HeightOverride = eyeHeight;
        // Level with the Dungeon Master: the seat's own pitch if he cannot be found.
        float pitch = seat.eulerAngles.x;
        pitch = pitch > 180f ? 360f - pitch : -pitch;
        var dm = TableManager.HasInstance && TableManager.Instance.DM != null ? TableManager.Instance.DM.GetComponent<Animator>() : null;
        var head = dm != null && dm.isHuman ? dm.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null)
        {
            var eye = seat.position + Vector3.up * (room.Look.PitchTransform.position.y - room.transform.position.y);
            CameraEase.YawPitchTo(eye, head.position + Vector3.down * aimBelowHead, out float yaw, out pitch, seat.eulerAngles.y);
            room.Look.SetYaw(yaw);
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

    void OnValidate() => ApplyStyle();

    IntroLook Look => System.Array.Find(looks ?? new IntroLook[0], l => l != null && l.style == style);

    // The chosen style onto the lamp, the eyes, the practice board's light and the class card.
    public void ApplyStyle()
    {
        var look = Look;
        if (look == null) return;
        lampColor = look.lamp;
        if (lamp != null) lamp.color = lampColor;
        var glow = dmEyes != null ? dmEyes.GetComponent<GlowInDark>() : null;
        if (glow != null) glow.glow = look.eyes;
        if (introDungeon != null) introDungeon.boardLightColor = look.boardLight;
        var figures = TableManager.HasInstance ? TableManager.Instance.GetComponentInChildren<ClassFigures>(true) : FindAnyObjectByType<ClassFigures>(FindObjectsInactive.Include);
        look.ApplyToCard(figures);
        if (sheet != null) look.ApplyTo(sheet.transform);
    }

    // At the table the lamp lights everything; on the practice board (Teach) it is limited to the room.
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
}

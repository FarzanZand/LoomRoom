using System.Collections;
using UnityEngine;

// The game table. Using it is its own moment: the room player, standing where they are, turns to the
// Dungeon Master, who may speak (DungeonMasterRemarks listens to Seated); then the view goes to the
// class figures on the table (ClassFigures) and taking one starts the dungeon through TableLevelLoader
// (on this object). A run saved on quit is its class's figure, picked first: taking it continues the
// run. Cancelling hands the view back. Without figures the adventure menu opens instead.
// tableIntroController only lists which objects belong to the town (read by TableLevelLoader).
public class TableManager : Singleton<TableManager>, IInteractable
{
    public TableIntroController tableIntroController;
    public GameObject DM;
    public Transform dmPlacement;
    [SerializeField] string prompt = "Play";
    [SerializeField, Tooltip("Said when the table is used before the player has talked to the Dungeon Master.")]
    DungeonMaster.Line talkFirst;
    [SerializeField, Tooltip("Said when the table is used during the win ending, while the door stands open.")]
    DungeonMaster.Line endingLine = new() { text = "Not now. The door's open. Go and get some air." };
    [SerializeField, Tooltip("Said when the table is used while the spend-spoils quest is open (Blocking first).")]
    DungeonMaster.Line learnFirstLine = new() { text = "Not yet. Train Blocking at the sheet first." };
    [SerializeField, Tooltip("Said instead while the lesson holds runs and the sheet has not been opened yet.")]
    DungeonMaster.Line lookFirstLine = new() { text = "Not yet. Have a look at the sheet first." };

    [Header("At the table")]
    [SerializeField, Min(.1f), Tooltip("Seconds for the view to turn to the Dungeon Master.")] float sitSeconds = 1.1f;
    [SerializeField, Min(0), Tooltip("When the figures come out the player steps up to them: this far from the figures, on the side they face (world units).")]
    float figureViewDistance = 20f;
    [SerializeField, Tooltip("Field of view change while picking a figure (negative zooms in).")]
    float figureZoom = -14f;
    [SerializeField, Min(.1f)] float stepSeconds = .9f;
    [SerializeField, Min(0), Tooltip("How far above the table the view aims while picking (world units).")] float figureAimHeight = 3f;
    [SerializeField, Min(0), Tooltip("Extra seconds the Dungeon Master's last line stays readable before the menu covers it.")]
    float afterSpeech = 1.5f;
    [SerializeField, Min(1), Tooltip("Longest wait for the Dungeon Master before the menu opens anyway.")]
    float maxSpeechWait = 60f;

    [Header("Picking a figure")]
    [SerializeField, Tooltip("Figures set on the table to pick a class. Without them (or with a saved run to resume) the adventure menu opens instead.")]
    ClassFigures figures;
    [SerializeField, Tooltip("Where a picked figure goes. Empty: the first dungeon in the catalog.")]
    TableLevelData figureLevel;
    [SerializeField, Min(.1f)] float lookSeconds = .7f;

    // The player has just sat down; whatever is said now is waited for before the figures.
    public event System.Action<Player> Seated;
    static bool DMTalking => PixelCrushers.DialogueSystem.DialogueManager.isConversationActive
        || (DungeonMaster.HasInstance && DungeonMaster.Instance.Speaking);

    TableLevelLoader loader;
    /// <summary>The table's level loader, or null when there is no table.</summary>
    public static TableLevelLoader Loader
    {
        get
        {
            if (!HasInstance) return null;
            var found = Instance.loader != null ? Instance.loader : Instance.GetComponent<TableLevelLoader>();
            return found != null ? found : null;   // a real null, so ?. is safe
        }
    }
    TableLevelMenu menu;
    Coroutine sitting;
    Player sittingPlayer;
    bool ownsDialogue; // this pushed GameState.Dialogue and has not popped it yet
    public string Prompt => IntroController.WaitingForSeat ? "Sit down"
        : ProgressionManager.HasInstance && ProgressionManager.Instance.LessonHoldsRuns
            ? (ProgressionManager.Instance.HasFlag(CharacterSheetTable.OpenedFlag) ? "Train Blocking at the sheet first" : "Look at the character sheet first") : prompt;
    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room
        && player.IsAlive && PlayerManager.HasInstance && PlayerManager.Instance.Active == player
        && (loader == null || !loader.Busy) && sitting == null
        && !(Coop.Running && MultiplayerManager.Instance.LocalInRun)
        && !PixelCrushers.DialogueSystem.DialogueManager.isConversationActive;
    // The win ending is playing (the morning walk to the open door): the table only points the player out.
    public static bool EndingRunning => WorldManager.HasInstance && WorldManager.Instance.ending != null && WorldManager.Instance.ending.Running;
    protected override void Awake()
    {
        base.Awake();
        // There is no authored loader: it is added here.
        loader = GetComponent<TableLevelLoader>();
        if (loader == null) loader = gameObject.AddComponent<TableLevelLoader>();
    }
    // Until the Dungeon Master has introduced the game, the table sends the player to him.
    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (EndingRunning)
        {
            if (endingLine != null && !endingLine.IsEmpty && DungeonMaster.HasInstance && !DungeonMaster.Instance.Speaking) DungeonMaster.Say(endingLine);
            return;
        }
        // The tabletop intro: the Dungeon Master is waiting for the player to sit down.
        if (IntroController.HasInstance && IntroController.Instance.TakeSeat()) return;
        // The lesson: no run until Blocking is trained at the sheet (the spend-spoils quest).
        if (ProgressionManager.HasInstance && ProgressionManager.Instance.LessonHoldsRuns)
        {
            SayLessonLine();
            return;
        }
        // The whole dungeon won: the prototype's thank-you instead of a new run.
        // In co-op each player gets their own (once per save); a teammate already seated waits the card out, then this
        // player uses the table again to sit.
        if ((!CampaignManager.Active || CampaignManager.Instance.playEndingOnWin) && TryGetComponent<PrototypeEnd>(out var end) && end.TryPlay()) return;
        if (DungeonMasterSeat.Introduced) EnterTable();
        else if (talkFirst != null && !talkFirst.IsEmpty && DungeonMaster.HasInstance && !DungeonMaster.Instance.Speaking) DungeonMaster.Say(talkFirst);
    }
    public void EnterTable() => Play();

    // The lesson's refusal: look at the sheet until it has been opened, then train Blocking there.
    void SayLessonLine()
    {
        bool opened = ProgressionManager.Instance.HasFlag(CharacterSheetTable.OpenedFlag);
        var line = !opened && lookFirstLine != null && !lookFirstLine.IsEmpty ? lookFirstLine : learnFirstLine;
        if (line != null && !line.IsEmpty && DungeonMaster.HasInstance && !DungeonMaster.Instance.Speaking) DungeonMaster.Say(line);
    }

    // level: where the figure goes (empty: Figure Level). only: set out just this figure (the intro's
    // practice board), with prompt said instead of the figures' own line.
    // False when the table cannot be used now (busy loader, no active room player, a conversation...).
    public bool Play(TableLevelData level = null, AdventurerClass only = null, DungeonMaster.Line prompt = null, bool showCard = true)
    {
        if (loader == null || !PlayerManager.HasInstance || EndingRunning || !CanInteract(PlayerManager.Instance.Active)) return false;
        // "Let's play." in the DM's conversation comes here too: Blocking first.
        if (ProgressionManager.HasInstance && ProgressionManager.Instance.LessonHoldsRuns)
        {
            SayLessonLine();
            return false;
        }
        if (GameManager.HasInstance)
        {
            sittingPlayer = PlayerManager.Instance.Active;
            sitting = StartCoroutine(SitThenChoose(sittingPlayer, level, only, prompt, showCard));
        }
        else loader.ShowSelection();
        return true;
    }

    // Dialogue state while sitting and listening: no movement or look, HUD (and the DM's lines) visible.
    IEnumerator SitThenChoose(Player player, TableLevelData chosenLevel = null, AdventurerClass only = null, DungeonMaster.Line prompt = null, bool showCard = true)
    {
        // The last game's floor sinks into the table at once while the player sits down.
        if (figures != null && figures.HasFigures) loader.SweepTable(player.transform.position);
        PushDialogue();
        if (Coop.Running) MultiplayerManager.Instance.SatDown();
        yield return Sit(player);
        // Co-op: the Dungeon Master and the figures wait until everyone in the room has sat down.
        if (Coop.Running && only == null)
        {
            bool all = false;
            yield return MultiplayerManager.Instance.WaitForEveryoneSeated(ok => all = ok);
            if (!all)
            {
                PopDialogue();
                StandUp(player);
                MultiplayerManager.Instance.StoodUp();
                sitting = null; sittingPlayer = null;
                yield break;
            }
        }

        Seated?.Invoke(player);
        yield return null;
        if (DMTalking)
        {
            float waited = 0f;
            while (DMTalking && waited < maxSpeechWait)
            {
                waited += GameManager.CutsceneDelta;
                yield return null;
            }
            yield return GameManager.WaitCutscene(afterSpeech);
        }
        // The campaign: the world map on the table (CampaignManager). The first morning's sheet and dice still come
        // first, then its figures, and the class picked stands on the map.
        // Co-op: the host plays the map; a guest picks their figure as before and the host's place is the run.
        if (only == null && CampaignManager.Active && Coop.IsGuest) CampaignManager.Instance.HideNow();
        if (only == null && CampaignManager.Active && !Coop.IsGuest)
        {
            if (IntroController.SheetPending)
            {
                CampaignManager.Instance.HideNow();
                yield return IntroController.Instance.FirstMorningSheet(player);
                if (figures != null && figures.HasFigures)
                {
                    yield return CameraEase.LookAt(player.Look, figures.FocusPoint(false) + Vector3.up * figureAimHeight, lookSeconds);
                    AdventurerClass first = null;
                    // No run starts from this pick (the map comes next), so the figure is taken without the taken line.
                    figures.SilentTake = true;
                    try { yield return figures.Choose(c => first = c, null, null, null, null, true, true, IntroController.OfferedClasses); }
                    finally { figures.SilentTake = false; }
                    if (IntroController.HasInstance) IntroController.Instance.WriteClass(first);
                    if (first == null)
                    {
                        if (IntroController.HasInstance) IntroController.Instance.EndSheetAtTable();
                        PopDialogue();
                        yield return CameraEase.Zoom(player.CameraRig, 0f, .35f, smooth: false);
                        StandUp(player);
                        sitting = null; sittingPlayer = null;
                        yield break;
                    }
                    yield return GameManager.WaitCutscene(.6f);
                }
                if (IntroController.HasInstance) IntroController.Instance.EndSheetAtTable();
            }
            bool started = false, failed = false;
            // Guarded: an error in the map view lets go of everything, so the table can be used again.
            yield return GuardedRoutine.Run(CampaignManager.Instance.Choose(player, r => started = r), _ => failed = true);
            if (failed)
            {
                PopDialogue();
                CampaignManager.Instance.AbortMapView();
                StandUp(player);
                sitting = null; sittingPlayer = null;
                yield break;
            }
            PopDialogue();
            if (!started) { yield return CameraEase.Zoom(player.CameraRig, 0f, .35f, smooth: false); StandUp(player); if (Coop.Running) MultiplayerManager.Instance.StoodUp(); }
            sitting = null; sittingPlayer = null;
            yield break;
        }
        // The first morning after the intro: the character sheet and dice come first.
        if (IntroController.SheetPending && only == null) yield return IntroController.Instance.FirstMorningSheet(player);
        if (figures != null && figures.HasFigures)
        {
            // The board is swept and the figures go down where the last floor stood.
            loader.ClearTable();
            // Seated (the intro, or the chair the sheet put them in): the view only tips down to the figures.
            if (IntroController.Seated || IntroController.SheetAtTable) yield return CameraEase.LookAt(player.Look, figures.FocusPoint(only != null) + Vector3.up * figureAimHeight, lookSeconds);
            else yield return StepToFigures(player);
            AdventurerClass picked = null;
            AdventurerClass savedClass = null; string savedDetail = null;
            if (only == null && !Coop.Running) SavedRun(out savedClass, out savedDetail);
            // Through the intro there is no standing up: Esc does nothing.
            bool canStand = !IntroController.Seated;
            var offered = only == null ? IntroController.OfferedClasses : null;
            yield return figures.Choose(c => picked = c, savedClass, savedDetail, only, prompt, canStand, showCard, offered);
            if (IntroController.HasInstance) IntroController.Instance.WriteClass(picked);
            if (picked == null && IntroController.HasInstance) IntroController.Instance.EndSheetAtTable();
            var level = chosenLevel != null ? chosenLevel : figureLevel != null ? figureLevel : loader.Catalog != null ? System.Array.Find(loader.Catalog.levels, l => l != null && l.IsDungeon) : null;
            if (picked != null && level != null)
            {
                // Still seated while the view eases back: the look is held until the load's cutscene takes
                // over, so the table being built is never missed by glancing away.
                yield return CameraEase.Zoom(player.CameraRig, 0f, .35f, smooth: false);
                player.Look.HeightOverride = null;
                if (Coop.Running)
                {
                    // Co-op: wait seated for the others; the host starts the run for everyone.
                    bool started = false;
                    yield return MultiplayerManager.Instance.WaitAtTable(picked, level, player, s => started = s);
                    if (!started)
                    {
                        PopDialogue();
                        StandUp(player);
                        MultiplayerManager.Instance.StoodUp();
                        sitting = null; sittingPlayer = null;
                        yield break;
                    }
                }
                else if (figures.ResumeChosen) loader.ResumeAdventure();
                else loader.Load(level);
                PopDialogue();
            }
            else
            {
                PopDialogue();
                yield return CameraEase.Zoom(player.CameraRig, 0f, .35f, smooth: false);
                StandUp(player);
                if (Coop.Running) MultiplayerManager.Instance.StoodUp();
            }
            sitting = null; sittingPlayer = null;
            yield break;
        }

        PopDialogue();
        loader.ShowSelection();

        // Stand up when the menu closes without a run starting (Return to room, Esc).
        if (menu == null) menu = GetComponent<TableLevelMenu>();
        while (menu != null && menu.IsOpen) yield return null;
        yield return null;
        if (PlayerManager.Instance.Active == player && !loader.Busy) StandUp(player);
        else if (player.Look != null) player.Look.HeightOverride = null;
        sitting = null; sittingPlayer = null;
    }

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

    // Asks the loader to bring the room back (whileDark runs under the black), retrying while a load is in
    // progress or the run has not ended yet. Logs an error if it still refuses after maxWait seconds.
    // started reports whether the return began.
    public static IEnumerator ReturnToRoom(TableLevelLoader loader, System.Action whileDark, System.Action<bool> started = null, Object context = null, float maxWait = 10f)
    {
        float until = Time.unscaledTime + maxWait;
        while (loader != null)
        {
            if (!loader.Busy)
            {
                loader.ReturnToRoom(whileDark);
                // ReturnToRoom marks the loader busy as it starts; still idle means it refused.
                if (loader.Busy) { started?.Invoke(true); yield break; }
            }
            if (Time.unscaledTime >= until) break;
            yield return null;
        }
        Debug.LogError("[TableManager] TableLevelLoader.ReturnToRoom refused (busy, or the run has not ended); the room was not brought back.", context);
        started?.Invoke(false);
    }

    // A run saved on quit: its class and where it stopped, for the figures.
    void SavedRun(out AdventurerClass cls, out string detail)
    {
        cls = null; detail = null;
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        var save = table != null ? table.GetComponent<AdventureSave>() : null;
        if (save == null || !save.HasCheckpoint || !save.CanRestore(save.Saved.checkpoint)) return;
        var c = save.Saved.checkpoint;
        var rules = table.GetComponent<AdventurerProgress>()?.rules;
        cls = rules != null ? System.Array.Find(rules.classes, x => x != null && x.id == c.classId) : null;
        detail = $"level {c.characterLevel}, floor {c.floor}";
    }

    // Up to the table edge in front of the figures, looking down at them, zooming in on the way.
    IEnumerator StepToFigures(Player player)
    {
        if (player.Look == null || figures.spots.Length == 0 || figures.spots[0] == null) { yield return CameraEase.LookAt(player.Look, figures.Centre, lookSeconds); yield break; }
        var facing = figures.spots[0].forward; facing.y = 0; facing.Normalize();
        // Aim at the figures' middle, not their feet.
        var centre = figures.Centre + Vector3.up * figureAimHeight;
        var to = new Vector3(centre.x, player.transform.position.y, centre.z) + facing * figureViewDistance;
        // Co-op: side by side with the others.
        if (Coop.Running) to += MultiplayerManager.Instance.FigureOffset(facing);
        var from = player.transform.position;
        float fromYaw = player.Look.YawTransform.eulerAngles.y, fromPitch = player.Look.Pitch;
        float fromZoom = player.CameraRig != null ? player.CameraRig.FovOffset : 0f;
        for (float t = 0; t < stepSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / stepSeconds);
            player.Warp(Vector3.Lerp(from, to, k));
            CameraEase.YawPitchTo(player.Look, centre, out float yaw, out float pitch);
            player.Look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
            player.Look.SetPitch(Mathf.Lerp(fromPitch, pitch, k));
            if (player.CameraRig != null) player.CameraRig.FovOffset = Mathf.Lerp(fromZoom, figureZoom, k);
            yield return null;
        }
        player.Warp(to);
        yield return CameraEase.LookAt(player.Look, centre, .15f);
    }

    // The player stays standing where they are; only the view turns to the Dungeon Master.
    IEnumerator Sit(Player player)
    {
        if (player.Look == null) yield break;
        float fromYaw = player.Look.YawTransform.eulerAngles.y, fromPitch = player.Look.Pitch;
        for (float t = 0; t < sitSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / sitSeconds);
            Aim(player, out float yaw, out float pitch);
            player.Look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
            player.Look.SetPitch(Mathf.Lerp(fromPitch, pitch, k));
            yield return null;
        }
        Aim(player, out float endYaw, out float endPitch);
        player.Look.SetYaw(endYaw);
        player.Look.SetPitch(endPitch);
    }

    // Looking at the Dungeon Master's face from the seated eye.
    void Aim(Player player, out float yaw, out float pitch)
    {
        Vector3 target = dmPlacement != null ? dmPlacement.position : transform.position;
        var animator = DM != null && DM.activeInHierarchy ? DM.GetComponent<Animator>() : null;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null) target = head.position;
        CameraEase.YawPitchTo(player.Look, target, out yaw, out pitch);
    }

    // Back from a run: the room player stands at the table with the normal view (the way back flies into their eyes).
    public void StandRoomPlayer()
    {
        var room = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Room) : null;
        if (room != null) StandUp(room);
        // Co-op: not inside a teammate who stands where this player comes back (the map view walks along the table).
        if (room != null && Coop.Running) MultiplayerManager.Instance.KeepClearOfTeammates(room);
    }

    // Nothing to stand up from any more: just hand the view back.
    void StandUp(Player player)
    {
        if (player.Look != null) player.Look.HeightOverride = null;
        if (player.CameraRig != null) player.CameraRig.FovOffset = 0f;
    }

    // Interrupted mid-sit: pop exactly what was pushed and hand the view back.
    void OnDisable()
    {
        if (sitting != null) StopCoroutine(sitting);
        sitting = null;
        PopDialogue();
        if (CampaignManager.HasInstance) CampaignManager.Instance.AbortMapView();
        if (sittingPlayer != null) StandUp(sittingPlayer);
        sittingPlayer = null;
    }
}

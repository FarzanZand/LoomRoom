using System.Collections;
using UnityEngine;

// Table entry selects a level. The intro steps only tell TableLevelLoader what belongs to the town.
// Using the table is its own moment: the room player, standing where they are, turns to the Dungeon
// Master, who may speak (DungeonMasterRemarks listens to Seated); then the
// view drops to the class figures on the table (ClassFigures) and taking one starts the dungeon. A run
// saved on quit is its class's figure, picked first: taking it continues the run. Cancelling stands
// the player go. Without figures the adventure menu opens instead.
public class TableManager : Singleton<TableManager>, IInteractable
{
    public TableIntroController tableIntroController;
    public GameObject DM;
    public Transform dmPlacement;
    [SerializeField] string prompt = "Play";
    [SerializeField, Tooltip("Said when the table is used before the player has talked to the Dungeon Master.")]
    DungeonMaster.Line talkFirst;

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
    TableLevelMenu menu;
    Coroutine sitting;
    public string Prompt => prompt;
    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room
        && player.IsAlive && PlayerManager.HasInstance && PlayerManager.Instance.Active == player
        && (loader == null || !loader.Busy) && sitting == null
        && !PixelCrushers.DialogueSystem.DialogueManager.isConversationActive
        && !(WorldManager.HasInstance && WorldManager.Instance.ending != null && WorldManager.Instance.ending.Running);
    protected override void Awake()
    {
        base.Awake();
        loader = GetComponent<TableLevelLoader>() ?? gameObject.AddComponent<TableLevelLoader>();
    }
    // Until the Dungeon Master has introduced the game, the table sends the player to him.
    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (DungeonMasterSeat.Introduced) EnterTable();
        else if (talkFirst != null && !talkFirst.IsEmpty && DungeonMaster.HasInstance && !DungeonMaster.Instance.Speaking) DungeonMaster.Say(talkFirst);
    }
    public void EnterTable() => Play();

    // level: where the figure goes (empty: Figure Level). only: set out just this figure (the intro's
    // practice board), with prompt said instead of the figures' own line.
    public void Play(TableLevelData level = null, AdventurerClass only = null, DungeonMaster.Line prompt = null)
    {
        if (loader == null || !PlayerManager.HasInstance || !CanInteract(PlayerManager.Instance.Active)) return;
        if (GameManager.HasInstance) sitting = StartCoroutine(SitThenChoose(PlayerManager.Instance.Active, level, only, prompt));
        else loader.ShowSelection();
    }

    // Dialogue state while sitting and listening: no movement or look, HUD (and the DM's lines) visible.
    IEnumerator SitThenChoose(Player player, TableLevelData chosenLevel = null, AdventurerClass only = null, DungeonMaster.Line prompt = null)
    {
        GameManager.Instance.Push(GameState.Dialogue);
        yield return Sit(player);

        Seated?.Invoke(player);
        yield return null;
        if (DMTalking)
        {
            float waited = 0f;
            while (DMTalking && waited < maxSpeechWait)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(afterSpeech);
        }
        if (figures != null && figures.HasFigures)
        {
            // The board is swept and the figures go down where the last floor stood.
            loader.ClearTable();
            // Seated through the intro: the view only tips down to the figures.
            if (IntroController.Seated) yield return Turn(player, figures.FocusPoint(only != null) + Vector3.up * figureAimHeight, lookSeconds);
            else yield return StepToFigures(player);
            AdventurerClass picked = null;
            AdventurerClass savedClass = null; string savedDetail = null;
            if (only == null) SavedRun(out savedClass, out savedDetail);
            // Through the intro there is no standing up: Esc does nothing.
            bool canStand = !IntroController.Seated;
            yield return figures.Choose(c => picked = c, savedClass, savedDetail, only, prompt, canStand);
            GameManager.Instance.Pop(GameState.Dialogue);
            yield return Zoom(player, 0f, .35f);
            var level = chosenLevel != null ? chosenLevel : figureLevel != null ? figureLevel : loader.catalog != null ? System.Array.Find(loader.catalog.levels, l => l != null && l.IsDungeon) : null;
            if (picked != null && level != null)
            {
                player.Look.HeightOverride = null;
                if (figures.ResumeChosen) loader.ResumeAdventure();
                else loader.Load(level);
            }
            else StandUp(player);
            sitting = null;
            yield break;
        }

        GameManager.Instance.Pop(GameState.Dialogue);
        loader.ShowSelection();

        // Stand up when the menu closes without a run starting (Return to room, Esc).
        if (menu == null) menu = GetComponent<TableLevelMenu>();
        while (menu != null && menu.IsOpen) yield return null;
        yield return null;
        if (PlayerManager.Instance.Active == player && !loader.Busy) StandUp(player);
        else if (player.Look != null) player.Look.HeightOverride = null;
        sitting = null;
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
        if (player.Look == null || figures.spots.Length == 0 || figures.spots[0] == null) { yield return Turn(player, figures.Centre, lookSeconds); yield break; }
        var facing = figures.spots[0].forward; facing.y = 0; facing.Normalize();
        // Aim at the figures' middle, not their feet.
        var centre = figures.Centre + Vector3.up * figureAimHeight;
        var to = new Vector3(centre.x, player.transform.position.y, centre.z) + facing * figureViewDistance;
        var from = player.transform.position;
        float fromYaw = player.Look.YawTransform.eulerAngles.y, fromPitch = player.Look.Pitch;
        float fromZoom = player.CameraRig != null ? player.CameraRig.FovOffset : 0f;
        for (float t = 0; t < stepSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / stepSeconds);
            player.Warp(Vector3.Lerp(from, to, k));
            var d = centre - player.Look.PitchTransform.position;
            float yaw = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)).eulerAngles.y;
            float pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            player.Look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
            player.Look.SetPitch(Mathf.Lerp(fromPitch, pitch, k));
            if (player.CameraRig != null) player.CameraRig.FovOffset = Mathf.Lerp(fromZoom, figureZoom, k);
            yield return null;
        }
        player.Warp(to);
        yield return Turn(player, centre, .15f);
    }

    IEnumerator Zoom(Player player, float target, float seconds)
    {
        var rig = player.CameraRig; if (rig == null) yield break;
        float from = rig.FovOffset;
        for (float t = 0; t < seconds; t += Time.deltaTime) { rig.FovOffset = Mathf.Lerp(from, target, t / seconds); yield return null; }
        rig.FovOffset = target;
    }

    // Turns the view to a point on the table.
    IEnumerator Turn(Player player, Vector3 target, float seconds)
    {
        if (player.Look == null) yield break;
        float fromYaw = player.Look.YawTransform.eulerAngles.y, fromPitch = player.Look.Pitch;
        Vector3 d = target - player.Look.PitchTransform.position;
        float yaw = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)).eulerAngles.y;
        float pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
        for (float t = 0; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            player.Look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
            player.Look.SetPitch(Mathf.Lerp(fromPitch, pitch, k));
            yield return null;
        }
        player.Look.SetYaw(yaw);
        player.Look.SetPitch(pitch);
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
        Vector3 eye = player.Look.PitchTransform.position;
        Vector3 target = dmPlacement != null ? dmPlacement.position : transform.position;
        var animator = DM != null && DM.activeInHierarchy ? DM.GetComponent<Animator>() : null;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null) target = head.position;
        Vector3 d = target - eye;
        yaw = d.sqrMagnitude < .01f ? player.Look.YawTransform.eulerAngles.y : Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)).eulerAngles.y;
        pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
    }

    // Nothing to stand up from any more: just hand the view back.
    void StandUp(Player player)
    {
        if (player.Look != null) player.Look.HeightOverride = null;
        if (player.CameraRig != null) player.CameraRig.FovOffset = 0f;
    }

    void OnDisable()
    {
        if (sitting == null) return;
        StopCoroutine(sitting); sitting = null;
        if (GameManager.HasInstance && GameManager.Instance.State == GameState.Dialogue) GameManager.Instance.Pop(GameState.Dialogue);
    }
}

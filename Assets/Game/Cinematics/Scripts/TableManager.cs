using System.Collections;
using UnityEngine;

// Table entry selects a level. The intro steps only tell TableLevelLoader what belongs to the town.
// Sitting down is its own moment: the room player moves into the seat at the near end of the table
// and turns to the Dungeon Master, who may speak (DungeonMasterRemarks listens to Seated); then the view drops to the class figures
// on the table (ClassFigures) and taking one starts the dungeon. With a saved run to resume the
// adventure menu opens instead. Cancelling, or closing the menu, stands the player back up.
public class TableManager : Singleton<TableManager>, IInteractable
{
    public TableIntroController tableIntroController;
    public GameObject DM;
    public Transform dmPlacement;
    [SerializeField] string prompt = "Sit down";

    [Header("Sitting")]
    [Tooltip("Where the room player sits: feet position, facing the Dungeon Master.")]
    [SerializeField] Transform playerSeat;
    [SerializeField, Min(.1f)] float sitSeconds = 1.1f;
    [SerializeField, Tooltip("Camera height while seated, in the same units as PlayerLook's camera heights.")]
    float seatedCameraHeight = .55f;
    [SerializeField, Tooltip("Where standing up puts the player, back from the seat.")]
    float standUpDistance = 16f;
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
    public void Interact(Character who) { if (CanInteract(who)) EnterTable(); }
    public void EnterTable()
    {
        if (loader == null || !PlayerManager.HasInstance || !CanInteract(PlayerManager.Instance.Active)) return;
        if (GameManager.HasInstance) sitting = StartCoroutine(SitThenChoose(PlayerManager.Instance.Active));
        else loader.ShowSelection();
    }

    // Dialogue state while sitting and listening: no movement or look, HUD (and the DM's lines) visible.
    IEnumerator SitThenChoose(Player player)
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
        if (figures != null && figures.HasFigures && !HasSavedRun())
        {
            // The board is swept and the figures go down where the last floor stood.
            loader.ClearTable();
            yield return Turn(player, figures.Centre, lookSeconds);
            AdventurerClass picked = null;
            yield return figures.Choose(c => picked = c);
            GameManager.Instance.Pop(GameState.Dialogue);
            var level = figureLevel != null ? figureLevel : loader.catalog != null ? System.Array.Find(loader.catalog.levels, l => l != null && l.kind == TableLevelKind.Dungeon) : null;
            if (picked != null && level != null)
            {
                player.Look.HeightOverride = null;
                loader.Load(level);
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

    // A run saved on quit: the adventure menu offers to resume it.
    bool HasSavedRun()
    {
        var save = PlayerManager.Instance.GetPlayer(PlayerKind.Table)?.GetComponent<AdventureSave>();
        return save != null && save.HasCheckpoint && save.CanRestore(save.Saved.checkpoint);
    }

    // Turns the seated view to a point on the table.
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

    IEnumerator Sit(Player player)
    {
        if (playerSeat == null || player.Look == null) yield break;
        Vector3 from = player.transform.position;
        float fromYaw = player.Look.YawTransform.eulerAngles.y, fromPitch = player.Look.Pitch;
        player.Look.HeightOverride = seatedCameraHeight;
        for (float t = 0; t < sitSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / sitSeconds);
            player.Warp(Vector3.Lerp(from, playerSeat.position, k));
            Aim(player, out float yaw, out float pitch);
            player.Look.SetYaw(Mathf.LerpAngle(fromYaw, yaw, k));
            player.Look.SetPitch(Mathf.Lerp(fromPitch, pitch, k));
            yield return null;
        }
        player.Warp(playerSeat.position);
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
        yaw = playerSeat != null && d.sqrMagnitude < .01f ? playerSeat.eulerAngles.y : Quaternion.LookRotation(new Vector3(d.x, 0f, d.z)).eulerAngles.y;
        pitch = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
    }

    void StandUp(Player player)
    {
        if (player.Look != null) player.Look.HeightOverride = null;
        if (playerSeat != null) player.Warp(playerSeat.position - playerSeat.forward * standUpDistance);
    }

    void OnDisable()
    {
        if (sitting == null) return;
        StopCoroutine(sitting); sitting = null;
        if (GameManager.HasInstance && GameManager.Instance.State == GameState.Dialogue) GameManager.Instance.Pop(GameState.Dialogue);
    }
}

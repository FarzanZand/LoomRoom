using UnityEngine;

// The Dungeon Master in the room, seated across the table. Plays the talking gesture whenever a line
// is said in person, and talking to them starts their conversation in the Dialogue Database: the
// introduction the first time, and afterwards the everyday talk (the table never waits for either).
// When the introduction ends, or the talk ends on a choice that sets the Lua variable PlayNow, the
// player goes straight to the table. Sits on
// the DM's interaction trigger, so it is used instead of the NpcBrain conversation. While the player is
// in a table level the DM stands beside the chair, and sits back down when the player returns.
public class DungeonMasterSeat : MonoBehaviour, IInteractable
{
    [Tooltip("The DM's animator.")]
    public Animator animator;
    [Tooltip("State played on enable: the DM is already seated when the player looks.")]
    public string seatedState = "Sitting Idle";
    [Tooltip("Seated talking gesture, blended in as soon as a line is said. It returns to the seated idle by itself.")]
    public string talkingState = "Sitting Talking";
    [Min(0)] public float talkBlend = .25f;
    [Tooltip("Played when the player goes into a table level; it ends in the standing idle.")]
    public string standState = "Sit To Stand";
    [Tooltip("Played when the player comes back to the room; it ends in the seated idle.")]
    public string sitState = "Stand To Sit";
    [Min(0)] public float postureBlend = .3f;
    [Tooltip("Standing, he turns to face the middle of the table at this many degrees a second.")]
    [Min(0)] public float turnSpeed = 90f;
    [Tooltip("Where he stands, relative to where he sits (world units): beside the chair, clear of the table.")]
    public Vector3 standOffset = new(-9f, 0f, 1f);
    public string prompt = "Talk";
    [Tooltip("Dialogue Database conversation the first time the player talks to the DM. When it ends the player goes to the table.")]
    public string introduction = "Table/Introduction";
    [Tooltip("Dialogue Database conversation started when the player talks to the DM after the introduction.")]
    public string conversation = "Room/Talk";

    public const string PlayNowVariable = "PlayNow";
    public static bool Introduced => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(DungeonMasterRemarks.SatFlag);

    public string Prompt => IntroController.WaitingForSeat ? "Sit down" : prompt;

    Transform body;
    Vector3 seatPosition;
    // Where he sits, as the scene was loaded: the same on every machine whatever he is doing now (the floor's layout is
    // placed from it, DungeonGenerator.DungeonMasterPosition).
    public Vector3 SeatPosition => body != null ? seatPosition : transform.position;
    Quaternion seatRotation;
    bool standing;

    void Awake()
    {
        body = animator != null ? animator.transform : transform;
        seatPosition = body.position; seatRotation = body.rotation;
    }

    void OnEnable()
    {
        DungeonMaster.Spoke += OnSpoke;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
        standing = false;
        Sit();
    }

    // PlayerManager may wake after this object: make sure the swap is heard.
    void Start()
    {
        if (!PlayerManager.HasInstance) return;
        PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
        PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
    }

    // A boss fight brings him up to the table in front of the stage, facing it (BossStageEncounter).
    Vector3? focusAt, faceAt;
    [Tooltip("World units a second he moves when a boss fight brings him up to the table.")]
    [Min(.1f)] public float focusSpeed = 8f;
    [Min(0), Tooltip("How far his body sinks while he leans over the table (his lean's depth; the lean clip's own root motion used to carry him this far down, a frame at a time, and made him shake).")]
    public float leanDrop = 1.43f;
    [Tooltip("Held while a boss fight brings him to the table: he leans over it on his forearms, face low over the stage.")]
    public string leanState = "Lean On Table";
    [Tooltip("His standing idle, back from the table.")]
    public string standingState = "IdleWalkRunBT";
    [Min(0)] public float leanBlend = .8f;
    [Tooltip("World units he stands back from the table's edge while he leans on it (his forearms reach the top).")]
    [Min(0)] public float leanBack = 6f;

    // He has reached the spot a boss fight brought him to (or there is none).
    public bool Arrived => !focusAt.HasValue || (new Vector2(body.position.x - focusAt.Value.x, body.position.z - focusAt.Value.z)).sqrMagnitude < .05f;

    public void Focus(Vector3 standAt, Vector3 face)
    {
        focusAt = standAt; faceAt = face;
        if (animator != null && animator.isActiveAndEnabled && animator.HasState(0, Animator.StringToHash(leanState)))
            animator.CrossFadeInFixedTime(leanState, leanBlend, 0);
    }

    // While he pours his beam he straightens up from the lean (his standing idle, still at the table), so his raised palm
    // is high above the stage and the beam pours down onto the player; then he leans in again. Only while focused.
    public void Straighten(bool up)
    {
        if (!focusAt.HasValue || animator == null || !animator.isActiveAndEnabled) return;
        var state = up ? standingState : leanState;
        if (animator.HasState(0, Animator.StringToHash(state))) animator.CrossFadeInFixedTime(state, leanBlend, 0);
    }

    // He is there at once, leaning in already (the Flick spell's Appear): no walk, wherever it is, the table or not.
    // False while he sits.
    public bool Teleport(Vector3 standAt, Vector3 face)
    {
        if (!standing || body == null) return false;
        var at = new Vector3(standAt.x, seatPosition.y, standAt.z);
        var look = face - at; look.y = 0;
        body.SetPositionAndRotation(at, look.sqrMagnitude > .01f ? Quaternion.LookRotation(look) : body.rotation);
        focusAt = at; faceAt = face; returning = false;
        if (animator != null && animator.isActiveAndEnabled && animator.HasState(0, Animator.StringToHash(leanState)))
            animator.CrossFadeInFixedTime(leanState, .1f, 0);
        return true;
    }

    // Back at his place at once (after Teleport).
    public void TeleportHome()
    {
        focusAt = null; faceAt = null; returning = false;
        if (!standing || body == null) return;
        var standAt = seatPosition + standOffset;
        var home = new Vector3(standAt.x, seatPosition.y, standAt.z);
        var to = TableManager.HasInstance ? TableManager.Instance.transform.position - home : body.forward; to.y = 0;
        body.SetPositionAndRotation(home, to.sqrMagnitude > .01f ? Quaternion.LookRotation(to) : body.rotation);
        if (animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(standingState, .1f, 0);
    }

    public void Unfocus()
    {
        if (focusAt.HasValue)
        {
            returning = true;
            if (standing && animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(standingState, leanBlend, 0);
        }
        focusAt = null; faceAt = null;
    }
    bool returning;

    // After the animation step (LateUpdate, not Update): the Animator applies its clips' root motion to his body each
    // frame after Update, so a place set in Update was drawn with that frame's root motion on top, and the leaning clip's
    // made him pop up 6-8 cm for a frame several times a second through every fight. Set here, the drawn place is this one.
    void LateUpdate()
    {
        // Seated (and sitting down), he stays on the chair: the clips' root motion would walk him off it.
        if (!standing) { body.SetPositionAndRotation(seatPosition, seatRotation); return; }
        if (focusAt.HasValue)
        {
            var target = new Vector3(focusAt.Value.x, seatPosition.y - leanDrop, focusAt.Value.z);
            body.position = Vector3.MoveTowards(body.position, target, focusSpeed * Time.deltaTime);
            var look = faceAt.Value - body.position; look.y = 0;
            if (look.sqrMagnitude > .01f) body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(look), turnSpeed * Time.deltaTime);
            return;
        }
        var standAt = seatPosition + standOffset;
        var standHere = new Vector3(standAt.x, seatPosition.y, standAt.z);
        // Back from the stage he walks; otherwise he simply stands there.
        if (returning) { body.position = Vector3.MoveTowards(body.position, standHere, focusSpeed * Time.deltaTime); returning = (body.position - standHere).sqrMagnitude > .01f; }
        else body.position = standHere;
        if (!TableManager.HasInstance) return;
        var to = TableManager.Instance.transform.position - body.position; to.y = 0;
        if (to.sqrMagnitude < .01f) return;
        body.rotation = Quaternion.RotateTowards(body.rotation, Quaternion.LookRotation(to), turnSpeed * Time.deltaTime);
    }

    void OnDisable()
    {
        DungeonMaster.Spoke -= OnSpoke;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
    }

    public void Sit()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        body.SetPositionAndRotation(seatPosition, seatRotation);
        animator.Play(seatedState, 0, Random.value);
    }

    void OnPlayerSwapped(Player active)
    {
        if (animator == null || !animator.isActiveAndEnabled) return;
        bool stand = active != null && active.kind == PlayerKind.Table;
        if (stand == standing) return;
        standing = stand;
        // Under a black screen nobody sees him move: he is simply back in his chair when it clears,
        // instead of still sitting down as the picture comes back. The way back's flight (TableLevelExit) starts with
        // the view down in the dungeon, so he takes his chair then too, before the room comes into view.
        if (!stand && (TableLevelExit.Flying || ScreenManager.HasInstance && ScreenManager.Instance.FadeAlpha > .99f)) { Sit(); return; }
        if (stand) animator.CrossFadeInFixedTime(standState, postureBlend, 0);
        else
        {
            // Back on the chair before sitting down, wherever standing left him.
            body.SetPositionAndRotation(seatPosition, seatRotation);
            animator.CrossFadeInFixedTime(sitState, postureBlend, 0);
        }
    }

    float gestureUntil;

    // A seated gesture (an animator state, e.g. pushing the character sheet across); talking waits for it.
    public void Gesture(string state, float seconds)
    {
        if (standing || animator == null || !animator.isActiveAndEnabled || string.IsNullOrEmpty(state)) return;
        animator.CrossFadeInFixedTime(state, .2f, 0);
        gestureUntil = Time.time + seconds;
    }

    void OnSpoke(DungeonMaster.Line line)
    {
        if (Time.time < gestureUntil) return;
        if (!standing && line.inPerson && animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(talkingState, talkBlend, 0);
    }

    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room
        && PlayerManager.HasInstance && PlayerManager.Instance.Active == player
        && PixelCrushers.DialogueSystem.DialogueManager.hasInstance && !PixelCrushers.DialogueSystem.DialogueManager.isConversationActive;

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (IntroController.HasInstance && IntroController.Instance.TakeSeat()) return;
        bool first = !Introduced;
        if (first && DungeonMaster.HasInstance) DungeonMaster.Instance.Silence();
        PixelCrushers.DialogueSystem.DialogueLua.SetVariable(PlayNowVariable, false);
        if (DialogueBridge.StartConversation(first ? introduction : conversation, who.transform, animator != null ? animator.transform : transform))
            StartCoroutine(AfterTalk(first));
    }

    System.Collections.IEnumerator AfterTalk(bool first)
    {
        yield return null;
        while (PixelCrushers.DialogueSystem.DialogueManager.isConversationActive) yield return null;
        bool play = first || PixelCrushers.DialogueSystem.DialogueLua.GetVariable(PlayNowVariable).asBool;
        if (first && ProgressionManager.HasInstance) ProgressionManager.Instance.SetFlag(DungeonMasterRemarks.SatFlag);
        yield return null; // the Dialogue state is popped as the conversation ends
        if (play && TableManager.HasInstance) TableManager.Instance.EnterTable();
    }
}

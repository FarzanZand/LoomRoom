using UnityEngine;

// The Dungeon Master in the room, seated across the table. Plays the talking gesture whenever a line
// is said in person, and talking to them starts their conversation in the Dialogue Database: the
// introduction the first time (the table stays shut until then), and afterwards the everyday talk.
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

    void Update()
    {
        // Seated (and sitting down), he stays on the chair: the clips' root motion would walk him off it.
        if (!standing) { body.SetPositionAndRotation(seatPosition, seatRotation); return; }
        var standAt = seatPosition + standOffset;
        body.position = new Vector3(standAt.x, seatPosition.y, standAt.z);
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

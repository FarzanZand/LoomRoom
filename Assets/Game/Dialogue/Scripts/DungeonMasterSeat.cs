using UnityEngine;

// The Dungeon Master in the room, seated across the table. Plays the talking gesture whenever a line
// is said in person, and talking to them starts their conversation in the Dialogue Database. Sits on
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
    [Tooltip("Dialogue Database conversation started when the player talks to the DM.")]
    public string conversation = "Room/Talk";

    public string Prompt => prompt;

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

    void OnSpoke(DungeonMaster.Line line)
    {
        if (!standing && line.inPerson && animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(talkingState, talkBlend, 0);
    }

    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room
        && PlayerManager.HasInstance && PlayerManager.Instance.Active == player
        && PixelCrushers.DialogueSystem.DialogueManager.hasInstance && !PixelCrushers.DialogueSystem.DialogueManager.isConversationActive;

    public void Interact(Character who)
    {
        if (CanInteract(who)) DialogueBridge.StartConversation(conversation, who.transform, animator != null ? animator.transform : transform);
    }
}

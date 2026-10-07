using UnityEngine;

// The Dungeon Master in the room, seated across the table. Plays the talking gesture whenever a line
// is said in person, and talking to them starts their conversation in the Dialogue Database. Sits on
// the DM's interaction trigger, so it is used instead of the NpcBrain conversation.
public class DungeonMasterSeat : MonoBehaviour, IInteractable
{
    [Tooltip("The DM's animator.")]
    public Animator animator;
    [Tooltip("State played on enable: the DM is already seated when the player looks.")]
    public string seatedState = "Sitting Idle";
    [Tooltip("Seated talking gesture, blended in as soon as a line is said. It returns to the seated idle by itself.")]
    public string talkingState = "Sitting Talking";
    [Min(0)] public float talkBlend = .25f;
    public string prompt = "Talk";
    [Tooltip("Dialogue Database conversation started when the player talks to the DM.")]
    public string conversation = "Room/Talk";

    public string Prompt => prompt;

    void OnEnable()
    {
        DungeonMaster.Spoke += OnSpoke;
        Sit();
    }

    void OnDisable() => DungeonMaster.Spoke -= OnSpoke;

    public void Sit()
    {
        if (animator != null && animator.isActiveAndEnabled) animator.Play(seatedState, 0, Random.value);
    }

    void OnSpoke(DungeonMaster.Line line)
    {
        if (line.inPerson && animator != null && animator.isActiveAndEnabled) animator.CrossFadeInFixedTime(talkingState, talkBlend, 0);
    }

    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room
        && PlayerManager.HasInstance && PlayerManager.Instance.Active == player
        && PixelCrushers.DialogueSystem.DialogueManager.hasInstance && !PixelCrushers.DialogueSystem.DialogueManager.isConversationActive;

    public void Interact(Character who)
    {
        if (CanInteract(who)) DialogueBridge.StartConversation(conversation, who.transform, animator != null ? animator.transform : transform);
    }
}

using System.Collections;
using UnityEngine;

// The prototype's last word: once the whole dungeon has been won (EndingController.WinsFlag), using the table again
// has the Dungeon Master thank the player for playing, then fades to a thank-you card. On the TableManager, which asks
// it first (TableManager.Interact). It plays once per save (ThankedFlag); afterwards the table plays runs as before.
public class PrototypeEnd : MonoBehaviour
{
    public DungeonMaster.Line thanks = new() { text = "That's all I have for now. Thank you for playing the prototype." };
    [Tooltip("The thank-you card, on the screen fade's canvas so it shows over the black (authored in the scene).")]
    [SerializeField] CanvasGroup card;
    [SerializeField, Min(0)] float fadeSeconds = 1.5f, cardSeconds = 6f;

    bool ownsDialogue;

    public bool Running { get; private set; }
    public const string ThankedFlag = "story.prototypeThanked";
    public static bool Won => ProgressionManager.HasInstance && ProgressionManager.Instance.GetFlag(EndingController.WinsFlag) > 0;
    // Once per save: after the thanks the table plays runs as before.
    static bool Thanked => ProgressionManager.HasInstance && ProgressionManager.Instance.HasFlag(ThankedFlag);

    void Awake()
    {
        if (card != null) { card.alpha = 0f; card.gameObject.SetActive(false); }
    }

    void OnDisable()
    {
        if (ownsDialogue && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
        ownsDialogue = false;
        Running = false;
    }

    // True when it took the table's use (also while it plays, so the table doesn't seat the player mid-thanks).
    public bool TryPlay()
    {
        if (Running) return true;
        if (!Won || Thanked) return false;
        ProgressionManager.Instance.SetFlag(ThankedFlag);
        StartCoroutine(Play());
        return true;
    }

    IEnumerator Play()
    {
        Running = true;
        // Dialogue, not Cutscene: the player holds still and the HUD (with the DM's line) stays up.
        if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Dialogue); ownsDialogue = true; }
        if (DungeonMaster.HasInstance && thanks != null && !string.IsNullOrEmpty(thanks.text))
        {
            DungeonMaster.Say(thanks);
            // The line types out, then stays a moment to be read before the fade.
            yield return GameManager.WaitCutscene(.5f);
            float until = Time.unscaledTime + 12f;
            while (DungeonMaster.Instance.Speaking && Time.unscaledTime < until) yield return null;
            yield return GameManager.WaitCutscene(2.5f);
        }
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(fadeSeconds);
        yield return GameManager.WaitCutscene(fadeSeconds + .3f);
        if (card != null)
        {
            card.gameObject.SetActive(true);
            card.alpha = 0f;
            yield return ScreenManager.FadeGroup(card, 1f, 1f);
            yield return GameManager.WaitCutscene(cardSeconds);
            yield return ScreenManager.FadeGroup(card, 0f, 1f);
            card.gameObject.SetActive(false);
        }
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(fadeSeconds);
        if (ownsDialogue && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dialogue);
        ownsDialogue = false;
        Running = false;
    }
}

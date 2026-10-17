using System.Collections;
using UnityEngine;

// The prototype's last word: once the whole dungeon has been won (EndingController.WinsFlag), using the table again
// has the Dungeon Master thank the player for playing, then fades to a thank-you card. On the TableManager, which asks
// it first (TableManager.Interact). Afterwards the room is as it was and the table works as usual until the next use.
public class PrototypeEnd : MonoBehaviour
{
    public DungeonMaster.Line thanks = new() { text = "That's all I have for now. Thank you for playing the prototype." };
    [Tooltip("The thank-you card, on the screen fade's canvas so it shows over the black (authored in the scene).")]
    [SerializeField] CanvasGroup card;
    [SerializeField, Min(0)] float fadeSeconds = 1.5f, cardSeconds = 6f;

    bool ownsCutscene;

    public bool Running { get; private set; }
    public static bool Won => ProgressionManager.HasInstance && ProgressionManager.Instance.GetFlag(EndingController.WinsFlag) > 0;

    void Awake()
    {
        if (card != null) { card.alpha = 0f; card.gameObject.SetActive(false); }
    }

    void OnDisable()
    {
        if (ownsCutscene && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene);
        ownsCutscene = false;
        Running = false;
    }

    // True when it took the table's use.
    public bool TryPlay()
    {
        if (Running || !Won) return false;
        StartCoroutine(Play());
        return true;
    }

    IEnumerator Play()
    {
        Running = true;
        if (GameManager.HasInstance) { GameManager.Instance.Push(GameState.Cutscene); ownsCutscene = true; }
        if (DungeonMaster.HasInstance && thanks != null && !string.IsNullOrEmpty(thanks.text))
        {
            DungeonMaster.Say(thanks);
            yield return new WaitForSecondsRealtime(.5f);
            float until = Time.unscaledTime + 12f;
            while (DungeonMaster.Instance.Speaking && Time.unscaledTime < until) yield return null;
            yield return new WaitForSecondsRealtime(1f);
        }
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeToBlack(fadeSeconds);
        yield return new WaitForSecondsRealtime(fadeSeconds + .3f);
        if (card != null)
        {
            card.gameObject.SetActive(true);
            card.alpha = 0f;
            yield return ScreenManager.FadeGroup(card, 1f, 1f);
            yield return new WaitForSecondsRealtime(cardSeconds);
            yield return ScreenManager.FadeGroup(card, 0f, 1f);
            card.gameObject.SetActive(false);
        }
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(fadeSeconds);
        if (ownsCutscene && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Cutscene);
        ownsCutscene = false;
        Running = false;
    }
}

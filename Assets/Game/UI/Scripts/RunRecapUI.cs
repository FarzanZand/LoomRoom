using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The run summary after death or victory: what killed you, how deep, kills, gold, time,
// seed, then a new run, the adventure menu or back to the room.
public class RunRecapUI : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text title;
    [SerializeField] TMP_Text cause;
    [SerializeField] TMP_Text stats;
    [SerializeField] TMP_Text seed;
    [SerializeField] Button newRunButton;
    [SerializeField] Button adventuresButton;
    [SerializeField] Button returnButton;
    [SerializeField, Min(.05f)] float fadeSeconds = .8f;
    [SerializeField] Color deathColor = new Color(.85f, .22f, .18f);
    [SerializeField] Color victoryColor = new Color(1f, .8f, .35f);

    bool open;
    Tween fade;

    static TableLevelLoader Loader => TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        newRunButton?.onClick.AddListener(NewRun);
        adventuresButton?.onClick.AddListener(Adventures);
        returnButton?.onClick.AddListener(ReturnToRoom);
    }

    public void Show(RunSummary s)
    {
        if (root == null) return;
        if (!open && GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        open = true;
        root.SetActive(true);
        if (title != null) { title.text = s.victory ? "VICTORY" : "YOU DIED"; title.color = s.victory ? victoryColor : deathColor; }
        if (cause != null)
            cause.text = s.victory
                ? $"You conquered {s.levelName}."
                : $"Killed by {Article(s.killer)} on floor {s.floor} of {s.levelName}.";
        if (stats != null)
            stats.text = $"Floor reached  <b>{s.floor}</b> / {s.floors}\n" +
                         $"Enemies slain  <b>{s.kills}</b>\n" +
                         (s.bosses > 0 ? $"Bosses slain  <b>{s.bosses}</b>\n" : "") +
                         $"Gold found  <b>{s.gold}</b>\n" +
                         $"Time  <b>{s.TimeText}</b>";
        if (seed != null) seed.text = $"Seed {s.seed}";
        fade?.Kill();
        if (group != null)
        {
            group.alpha = 0f;
            fade = group.DOFade(1f, fadeSeconds).SetUpdate(true);
        }
        if (newRunButton != null) newRunButton.Select();
    }

    // "a Crypt Soldier", "an Ogre"; phrases that already read as a noun ("the dungeon", "a curse") pass through.
    static string Article(string killer)
    {
        if (string.IsNullOrEmpty(killer)) return "the dungeon";
        if (killer.StartsWith("the ") || killer.StartsWith("a ") || killer.StartsWith("an ")) return killer;
        return ("aeiouAEIOU".IndexOf(killer[0]) >= 0 ? "an " : "a ") + killer;
    }

    public void Hide()
    {
        if (!open) return;
        open = false;
        fade?.Kill();
        if (root != null) root.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        if (RunManager.HasInstance) RunManager.Instance.RecapClosed();
    }

    void NewRun()
    {
        var level = RunManager.HasInstance ? RunManager.Instance.Level : null;
        Hide();
        if (Loader != null && level != null) Loader.Load(level);
        else Loader?.ShowSelection();
    }

    void Adventures()
    {
        Hide();
        if (RunManager.HasInstance) RunManager.Instance.Abandon();
        Loader?.ShowSelection("Choose your adventure");
    }

    void ReturnToRoom()
    {
        Hide();
        if (RunManager.HasInstance) RunManager.Instance.Abandon();
        Loader?.ReturnToRoom();
    }
}

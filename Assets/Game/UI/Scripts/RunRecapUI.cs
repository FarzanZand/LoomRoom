using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The run summary after death or victory: what killed you, how deep, kills, gold, time,
// seed, then a new run, the adventure menu or back to the room. With Wake In Bed it offers only
// "Wake up": back to bed for the next morning (after a win, WorldManager's ending).
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
    [SerializeField, Tooltip("Every run ends by waking in bed: the only button is Wake up.")] bool wakeInBed = true;
    [SerializeField, Tooltip("The return button's label after a win (the ending, waking in bed).")] string wakeLabel = "Wake up";
    [SerializeField, Tooltip("The return button's label after a death (back in the room, standing by the table).")] string standLabel = "Stand up";

    [Header("Tally (spoils, ProgressionManager)")]
    [SerializeField, Tooltip("The tally rows; hidden when a run counts no spoils (the intro's practice board).")] GameObject tallyRoot;
    [SerializeField] RectTransform spoilsStrip;
    [SerializeField, Tooltip("One spoils icon, cloned per spoil carried (it stays hidden).")] Image spoilsIconTemplate;
    [SerializeField] TMP_Text spoilsValue;
    [SerializeField] RectTransform killsStrip;
    [SerializeField, Tooltip("One skull, cloned per kill (it stays hidden).")] Image skullTemplate;
    [SerializeField] TMP_Text killsValue, healthLine, healthValue, totalValue, bankValue, helpLine;
    [SerializeField, Tooltip("The short line of floor, gold and time under the tally.")] TMP_Text summaryLine;
    [SerializeField, Min(0)] int maxSkulls = 64, maxSpoilIcons = 24;
    [SerializeField, Min(.01f)] float spoilPopSeconds = .09f, skullPopSeconds = .025f, countSeconds = .7f;

    bool open, wakes, won;
    Coroutine counting;
    string returnLabel;
    Vector2? returnPosition;
    Tween fade;

    static TableLevelLoader Loader => TableManager.Loader;

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
            cause.text = (string.IsNullOrEmpty(s.adventurer) ? "" : $"<color=#A3A3A3>{s.adventurer}</color>\n") + (s.victory
                ? $"You conquered {s.levelName}."
                : $"Killed by {RunManager.KillerWithArticle(s.killer)} on floor {s.floor} of {s.levelName}.");
        bool tallied = s.tally.counted && tallyRoot != null;
        if (tallyRoot != null) tallyRoot.SetActive(tallied);
        if (summaryLine != null)
        {
            summaryLine.gameObject.SetActive(tallied);
            summaryLine.text = $"Floor {s.floor} / {s.floors}   ·   Gold {s.gold}   ·   {s.TimeText}" + (s.bosses > 0 ? $"   ·   Bosses {s.bosses}" : "");
        }
        if (stats != null) stats.gameObject.SetActive(!tallied);
        if (counting != null) StopCoroutine(counting);
        counting = tallied ? StartCoroutine(Count(s)) : null;
        if (stats != null)
            stats.text = $"Floor reached  <b>{s.floor}</b> / {s.floors}\n" +
                         $"Enemies slain  <b>{s.kills}</b>\n" +
                         (s.bosses > 0 ? $"Bosses slain  <b>{s.bosses}</b>\n" : "") +
                         $"Gold found  <b>{s.gold}</b>\n" +
                         $"Time  <b>{s.TimeText}</b>" +
                         (string.IsNullOrEmpty(s.skills) ? "" : $"\n\n<color=#8C8173>{s.skills}</color>");
        if (seed != null) seed.text = $"Seed {s.seed}";
        fade?.Kill();
        if (group != null)
        {
            group.alpha = 0f;
            fade = group.DOFade(1f, fadeSeconds).SetUpdate(true);
        }
        // With the morning loop, a run has one way on: waking up in bed.
        wakes = wakeInBed && WorldManager.HasInstance && WorldManager.Instance.wakeUpCutscene != null;
        won = s.victory;
        if (newRunButton != null) newRunButton.gameObject.SetActive(!wakes);
        if (adventuresButton != null) adventuresButton.gameObject.SetActive(!wakes);
        var label = returnButton != null ? returnButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null)
        {
            returnLabel ??= label.text;
            label.text = wakes ? (won ? wakeLabel : standLabel) : returnLabel;
        }
        // Alone, it takes the middle slot.
        if (returnButton != null && returnButton.transform is RectTransform rect)
        {
            returnPosition ??= rect.anchoredPosition;
            rect.anchoredPosition = wakes ? new Vector2(0f, returnPosition.Value.y) : returnPosition.Value;
        }
        (wakes ? returnButton : newRunButton)?.Select();
    }

    // The tally counts up row by row: the spoils pop into their strip, the kills fill theirs, their health turns into
    // spoils, then the total goes into what there is to spend.
    IEnumerator Count(RunSummary s)
    {
        var t = s.tally;
        Clear(spoilsStrip, spoilsIconTemplate); Clear(killsStrip, skullTemplate);
        SetText(spoilsValue, "0"); SetText(killsValue, "0"); SetText(healthValue, "0"); SetText(totalValue, "0");
        // The health line is gone from the recap (the user's call); the spoils it gives still count in the total.
        if (healthLine != null) healthLine.gameObject.SetActive(false);
        if (healthValue != null) healthValue.gameObject.SetActive(false);
        SetText(bankValue, t.before.ToString());
        SetText(helpLine, "");
        yield return Wait(.45f);
        int worth = 0, icons = 0;
        if (t.carried != null)
            foreach (var (item, count) in t.carried)
                for (int i = 0; i < count; i++)
                {
                    worth += Meta != null ? Meta.WorthOf(item) : 0;
                    SetText(spoilsValue, worth.ToString());
                    if (icons++ < maxSpoilIcons) Pop(spoilsStrip, spoilsIconTemplate, item.icon);
                    Tick();
                    yield return Wait(spoilPopSeconds);
                }
        SetText(spoilsValue, t.carriedWorth.ToString());
        yield return Wait(.3f);
        for (int i = 0; i < t.kills; i++)
        {
            if (i < maxSkulls) Pop(killsStrip, skullTemplate, null);
            SetText(killsValue, (i + 1).ToString());
            if (i < maxSkulls) yield return Wait(skullPopSeconds);
        }
        yield return Wait(.25f);
        yield return Wait(.1f);
        yield return CountUp(totalValue, 0, t.total, v => v.ToString());
        if (t.sharedWith > 1) SetText(helpLine, $"Shared between {t.sharedWith}");
        // Co-op: the party's kills, everyone's (the spoils are each player's own unless shared).
        if (Coop.Running && MultiplayerManager.Instance.Players.Count > 1)
        {
            var party = new System.Collections.Generic.List<string>();
            foreach (var p in MultiplayerManager.Instance.Players)
                if (p != null) party.Add($"{p.DisplayName} {(p.IsOwner ? (RunManager.HasInstance ? RunManager.Instance.Kills : 0) : p.Current.kills)}");
            if (party.Count > 1) SetText(helpLine, (t.sharedWith > 1 ? $"Shared between {t.sharedWith}.   " : "") + "Kills: " + string.Join(",  ", party));
        }
        if (t.toppedUp > 0) SetText(helpLine, $"+{t.toppedUp} from the Dungeon Master");
        yield return Wait(.25f);
        yield return CountUp(bankValue, t.before, t.after, v => v.ToString());
        counting = null;
    }
    static ProgressionManager Meta => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
    static void SetText(TMP_Text text, string value) { if (text != null) text.text = value; }
    static object Wait(float seconds) => new WaitForSecondsRealtime(seconds);
    IEnumerator CountUp(TMP_Text text, int from, int to, System.Func<int, string> format)
    {
        if (text == null) yield break;
        for (float e = 0; e < countSeconds; e += Time.unscaledDeltaTime) { text.text = format(Mathf.RoundToInt(Mathf.Lerp(from, to, e / countSeconds))); yield return null; }
        text.text = format(to);
    }
    static void Clear(RectTransform strip, Image template)
    {
        if (strip == null) return;
        for (int i = strip.childCount - 1; i >= 0; i--) { var c = strip.GetChild(i); if (template == null || c != template.transform) Destroy(c.gameObject); }
        if (template != null) template.gameObject.SetActive(false);
    }
    static void Pop(RectTransform strip, Image template, Sprite sprite)
    {
        if (strip == null || template == null) return;
        var img = Instantiate(template, strip);
        img.gameObject.SetActive(true);
        if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
        img.transform.localScale = Vector3.zero;
        img.transform.DOScale(1f, .18f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(img.gameObject);
    }
    static void Tick() { if (AudioManager.HasInstance && AudioManager.Instance.HasUI("tallyTick")) AudioManager.Instance.PlayUI("tallyTick"); }

    public void Hide()
    {
        if (!open) return;
        open = false;
        if (counting != null) { StopCoroutine(counting); counting = null; }
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

    // A win plays the ending (a new morning); a death puts the player back in the room, standing by the table,
    // with no wake-up.
    static void WakeUp(bool victory)
    {
        var world = WorldManager.Instance;
        if (victory && world.ending != null) { world.ending.Play(); return; }
        Loader?.ReturnToRoom();
    }

    void ReturnToRoom()
    {
        bool wake = wakes, victory = won;
        Hide();
        if (RunManager.HasInstance) RunManager.Instance.Abandon();
        if (wake) WakeUp(victory);
        else Loader?.ReturnToRoom();
    }
}

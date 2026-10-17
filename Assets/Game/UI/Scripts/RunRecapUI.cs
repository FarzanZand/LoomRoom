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
    [SerializeField, Min(1), Tooltip("Rows of icons each strip holds (each icon its template's size); past that the icons of a row overlap so they all fit.")]
    int stripRows = 3;
    [SerializeField, Tooltip("Inside each strip: space from its left and right edges (x) and its top and bottom (y).")] Vector2 stripPadding = new(14, 10);
    [SerializeField, Min(0)] float stripGap = 4;

    [Header("The ledger (built by RunRecapShowcase.Build, run from an eval)")]
    [SerializeField, Tooltip("The big picture of the most valuable spoil brought back.")] Image showcaseIcon;
    [SerializeField, Tooltip("The rows, written in one after another: the spoils (label, value, showcase, strip), the kills, the total (with its rule), what there is to spend.")]
    GameObject[] spoilsRow, killsRow, totalRow, bankRow;
    [SerializeField, Min(0), Tooltip("Seconds before the first row and between rows.")] float rowGap = .4f;
    [SerializeField, Min(.01f), Tooltip("Seconds each row takes to fade in.")] float rowFade = .3f;

    [Header("The Dungeon Master's closing line (said in his feed, over the recap)")]
    [SerializeField, Tooltip("A total of at least this is a good haul.")] int goodHaul = 25;
    [SerializeField, Tooltip("After a win.")] DungeonMaster.Line[] victoryLines =
    {
        new() { text = "You beat the Crypt Lord. I didn't think you would." },
        new() { text = "A win. Spend it well." },
    };
    [SerializeField, Tooltip("A death with a good haul.")] DungeonMaster.Line[] goodHaulLines =
    {
        new() { text = "Not a bad haul, for someone who died." },
        new() { text = "You got a lot out. Not yourself, though." },
    };
    [SerializeField, Tooltip("A death with little to show.")] DungeonMaster.Line[] poorHaulLines =
    {
        new() { text = "That's all? Go deeper next time." },
        new() { text = "Slim pickings." },
    };

    // UI Library keys (RunRecapShowcase.AddSounds): one plain recorded sound a row.
    const string SpoilsKey = "tallySpoils", KillsKey = "tallyKills", TotalKey = "tallyTotal", BankKey = "tallyBank";

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

    // The ledger: the rows are written in one after another, each whole and with one quiet sound (what was carried out,
    // what was killed, the total, what there is to spend), then the Dungeon Master has the last word.
    IEnumerator Count(RunSummary s)
    {
        var t = s.tally;
        Clear(spoilsStrip, spoilsIconTemplate); Clear(killsStrip, skullTemplate);
        // The health line is gone from the recap (the user's call); the spoils it gives still count in the total.
        if (healthLine != null) healthLine.gameObject.SetActive(false);
        if (healthValue != null) healthValue.gameObject.SetActive(false);
        SetText(spoilsValue, t.carriedWorth.ToString());
        SetText(killsValue, t.kills.ToString());
        SetText(totalValue, t.total.ToString());
        SetText(bankValue, t.after.ToString());
        // The spoils, cheapest first; the showcase shows the most valuable one.
        Sprite best = null;
        int icons = 0;
        if (t.carried != null)
            foreach (var (item, count) in t.carried)
            {
                if (item.icon != null) best = item.icon;
                for (int i = 0; i < count && icons < maxSpoilIcons; i++, icons++) Add(spoilsStrip, spoilsIconTemplate, item.icon);
            }
        if (showcaseIcon != null) { showcaseIcon.sprite = best; showcaseIcon.preserveAspect = true; showcaseIcon.enabled = best != null; }
        for (int i = 0; i < Mathf.Min(t.kills, maxSkulls); i++) Add(killsStrip, skullTemplate, null);
        Arrange(spoilsStrip, spoilsIconTemplate);
        Arrange(killsStrip, skullTemplate);

        var rows = new[] { (spoilsRow, SpoilsKey), (killsRow, KillsKey), (totalRow, TotalKey), (bankRow, BankKey) };
        foreach (var (row, _) in rows) SetAlpha(row, 0f);
        SetText(helpLine, "");
        foreach (var (row, key) in rows)
        {
            yield return Wait(rowGap);
            FadeIn(row);
            Sound(key);
        }
        yield return Wait(rowGap * 1.5f);
        ClosingLine(s);
        PartyLine(s);
        counting = null;
    }
    static ProgressionManager Meta => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
    static void SetText(TMP_Text text, string value) { if (text != null) text.text = value; }
    static object Wait(float seconds) => new WaitForSecondsRealtime(seconds);

    // The Dungeon Master's word on the run, said in his own feed over the recap: a win, a good haul or a poor one. After
    // the lesson's top-up the help line says what he added instead (he explains it himself once back in the room).
    // Co-op: under the tally, how the spoils were shared and everyone's kills (unless the lesson's top-up has the line).
    void PartyLine(RunSummary s)
    {
        if (helpLine == null || s.tally.toppedUp > 0 || !Coop.Running || MultiplayerManager.Instance.Players.Count < 2) return;
        var party = new System.Collections.Generic.List<string>();
        foreach (var p in MultiplayerManager.Instance.Players)
            if (p != null) party.Add($"{p.DisplayName} {(p.IsOwner ? (RunManager.HasInstance ? RunManager.Instance.Kills : 0) : p.Current.kills)}");
        helpLine.text = (s.tally.sharedWith > 1 ? $"Shared between {s.tally.sharedWith}.   " : "") + "Kills: " + string.Join(",  ", party);
        var group = Group(helpLine.gameObject);
        group.alpha = 0f;
        group.DOFade(1f, rowFade * 2f).SetUpdate(true).SetLink(helpLine.gameObject);
    }

    void ClosingLine(RunSummary s)
    {
        if (s.tally.toppedUp > 0)
        {
            if (helpLine == null) return;
            helpLine.text = $"+{s.tally.toppedUp} from the Dungeon Master";
            var group = Group(helpLine.gameObject);
            group.alpha = 0f;
            group.DOFade(1f, rowFade * 2f).SetUpdate(true).SetLink(helpLine.gameObject);
            return;
        }
        var lines = s.victory ? victoryLines : s.tally.total >= goodHaul ? goodHaulLines : poorHaulLines;
        var line = lines != null && lines.Length > 0 ? lines[Random.Range(0, lines.Length)] : null;
        if (line == null || string.IsNullOrWhiteSpace(line.text)) return;
        DungeonMaster.OverMenus = true;
        DungeonMaster.Say(line);
    }

    static CanvasGroup Group(GameObject go) => go.TryGetComponent<CanvasGroup>(out var g) ? g : go.AddComponent<CanvasGroup>();

    static void SetAlpha(GameObject[] row, float alpha)
    {
        if (row == null) return;
        foreach (var go in row) if (go != null) { var g = Group(go); g.DOKill(); g.alpha = alpha; }
    }

    void FadeIn(GameObject[] row)
    {
        if (row == null) return;
        foreach (var go in row) if (go != null) Group(go).DOFade(1f, rowFade).SetUpdate(true).SetLink(go);
    }

    static void Sound(string key)
    {
        if (AudioManager.HasInstance && AudioManager.Instance.HasUI(key)) AudioManager.Instance.PlayUI(key);
    }
    static void Clear(RectTransform strip, Image template)
    {
        if (strip == null) return;
        for (int i = strip.childCount - 1; i >= 0; i--) { var c = strip.GetChild(i); if (template == null || c != template.transform) { c.gameObject.SetActive(false); Destroy(c.gameObject); } }
        if (template != null) template.gameObject.SetActive(false);
    }
    // One more icon in its strip (laid out by Arrange).
    static void Add(RectTransform strip, Image template, Sprite sprite)
    {
        if (strip == null || template == null) return;
        var img = Instantiate(template, strip);
        img.gameObject.SetActive(true);
        if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
    }

    // Lays a strip's icons out left to right in up to stripRows rows (the rows in use centred in the strip); once all
    // the rows are full, each row takes an equal share and its icons close up and overlap until they fit.
    void Arrange(RectTransform strip, Image template)
    {
        if (strip.TryGetComponent<LayoutGroup>(out var group)) group.enabled = false;   // placed here instead
        var icons = new System.Collections.Generic.List<RectTransform>();
        foreach (RectTransform child in strip) if (child != template.transform && child.gameObject.activeSelf) icons.Add(child);
        if (icons.Count == 0) return;
        var size = template.rectTransform.rect.size;
        var area = strip.rect.size - stripPadding * 2f;
        int fits = Mathf.Max(1, Mathf.FloorToInt((area.x + stripGap) / (size.x + stripGap)));
        int rows = Mathf.Clamp(Mathf.CeilToInt(icons.Count / (float)fits), 1, Mathf.Max(1, stripRows));
        int perRow = Mathf.Max(fits, Mathf.CeilToInt(icons.Count / (float)rows));   // rows fill one by one until all are full
        float step = perRow <= fits ? size.x + stripGap : (area.x - size.x) / Mathf.Max(1, perRow - 1);
        float rowStep = size.y + stripGap;
        float top = (rows * size.y + (rows - 1) * stripGap) * .5f;   // the rows in use, centred
        for (int i = 0; i < icons.Count; i++)
        {
            int row = i / perRow, column = i % perRow;
            var icon = icons[i];
            icon.anchorMin = icon.anchorMax = new Vector2(0f, .5f);
            icon.pivot = new Vector2(.5f, .5f);
            icon.sizeDelta = size;
            icon.anchoredPosition = new Vector2(stripPadding.x + size.x * .5f + column * step, top - size.y * .5f - row * rowStep);
        }
    }

    // Switched off while open (the scene going): give the Menu state back, nothing else.
    void OnDisable()
    {
        if (!open) return;
        open = false;
        DungeonMaster.OverMenus = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    public void Hide()
    {
        if (!open) return;
        open = false;
        if (counting != null) { StopCoroutine(counting); counting = null; }
        fade?.Kill();
        DungeonMaster.OverMenus = false;
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

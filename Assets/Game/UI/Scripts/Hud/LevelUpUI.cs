using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

// The level up at the top middle of the screen: "LEVEL UP" lands big with the level under it, then what the level
// gave comes in one line at a time ("+3 Health"), each line fading away a while after it came, the
// title last. A second level while it shows starts it again with the new level and both levels' lines. Authored under
// the HUD canvas: a group, the title, the level line and one stat line template (switched off, cloned per line).
// The level up sting itself is AudioManager's levelUp key (AdventurerProgress plays it).
public class LevelUpUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text title;
    [SerializeField] TMP_Text levelLine;
    [SerializeField, Tooltip("One stat line, switched off; cloned per line.")] TMP_Text statTemplate;
    [SerializeField, Min(0f), Tooltip("From the title landing to the first stat line, then between lines.")] float firstStatDelay = .7f, statGap = .45f;
    [SerializeField, Min(0f), Tooltip("How long each stat line stays before it fades.")] float statHold = 2.2f;
    [SerializeField, Min(.05f)] float statFade = .8f;
    [SerializeField, Min(0f), Tooltip("The title stays this long after the last line has gone.")] float titleHold = .3f;
    [SerializeField, Min(.05f)] float titleFade = .7f;

    readonly List<TMP_Text> lines = new();
    readonly HashSet<TMP_Text> gone = new();   // lines whose fade-out has finished
    readonly List<string> pending = new();
    AdventurerProgress progress;
    Sequence show;

    // Taking turns with the skill rank popup (ExperienceBarUI): a level-up waits for a popup that is already showing,
    // then goes ahead of any popups still queued; those wait until it is done.
    static LevelUpUI current;
    int waitingLevel;   // a level-up waiting for the skill popup on screen to finish (0: none)
    public static bool Showing => current != null && current.show != null && current.show.IsActive();
    // A level-up waits until this time (Time.time): a boss's last pose is not covered by it (BossStageEncounter).
    public static float HoldUntil;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ClearHold() => HoldUntil = 0f;
    // Called by the skill popup when it is about to show its next one: a waiting level-up goes first.
    public static bool PlayWaiting()
    {
        if (current == null || current.waitingLevel == 0 || Time.time < HoldUntil) return false;
        int level = current.waitingLevel; current.waitingLevel = 0;
        current.Clear();
        current.Play(level, Merge(current.pending));
        current.pending.Clear();
        return true;
    }

    void Awake()
    {
        current = this;
        if (statTemplate != null) statTemplate.gameObject.SetActive(false);
        if (group != null) group.alpha = 0f;
    }

    void OnEnable() => Bind();

    void OnDisable()
    {
        if (progress != null) progress.LeveledUp -= LeveledUp;
        progress = null;
        show?.Kill(); show = null;
        Clear();
        if (group != null) group.alpha = 0f;
    }

    void Update()
    {
        if (progress == null) Bind();
        // Held back (a boss's last pose): it plays once the hold is over, after any skill popup on screen.
        if (waitingLevel > 0 && HoldUntil > 0f && Time.time >= HoldUntil && !ExperienceBarUI.Playing) { HoldUntil = 0f; PlayWaiting(); }
    }

    void Bind()
    {
        if (progress != null || !PlayerManager.HasInstance) return;
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
        if (progress != null) progress.LeveledUp += LeveledUp;
    }

    void LeveledUp(int level, List<string> gains)
    {
        // A skill popup on screen finishes first; it hands over when it is done (PlayWaiting).
        if (!Showing && (waitingLevel > 0 || ExperienceBarUI.Playing || Time.time < HoldUntil)) { waitingLevel = level; pending.AddRange(gains); return; }
        // Still showing the last level (or two in one go): every line not yet faded out stays in the list, those still
        // to come in included, and points from several levels are summed ("+2 attribute points").
        if (show != null && show.IsActive()) { show.Kill(); foreach (var l in lines) if (l != null && !gone.Contains(l)) pending.Add(l.text); }
        Clear();
        pending.AddRange(gains);
        Play(level, Merge(pending));
        pending.Clear();
    }

    void Play(int level, List<string> gains)
    {
        if (group == null || title == null) return;
        if (levelLine != null) levelLine.text = "Level " + level;
        group.alpha = 1f;
        var titleRect = title.rectTransform;
        title.alpha = 0f; titleRect.localScale = Vector3.one * 1.6f;
        if (levelLine != null) levelLine.alpha = 0f;

        show = DOTween.Sequence().SetUpdate(true);
        show.Append(title.DOFade(1f, .18f)).Join(titleRect.DOScale(1f, .35f).SetEase(Ease.OutBack, 2.2f));
        if (levelLine != null) show.Insert(.25f, levelLine.DOFade(1f, .3f));
        float at = firstStatDelay, lastGone = at;
        foreach (var text in gains)
        {
            var line = Instantiate(statTemplate, statTemplate.transform.parent);
            line.gameObject.SetActive(true);
            line.text = text;
            line.alpha = 0f;
            lines.Add(line);
            var rect = line.rectTransform;
            show.Insert(at, line.DOFade(1f, .2f));
            show.Insert(at, DOTween.To(() => 0f, x => rect.localScale = Vector3.one * (1.25f - .25f * x), 1f, .25f).SetEase(Ease.OutQuad));
            show.Insert(at + statHold, line.DOFade(0f, statFade).OnComplete(() => gone.Add(line)));
            lastGone = at + statHold + statFade;
            at += statGap;
        }
        float titleGoes = Mathf.Max(lastGone, firstStatDelay + statHold) + titleHold;
        show.Insert(titleGoes, title.DOFade(0f, titleFade));
        if (levelLine != null) show.Insert(titleGoes, levelLine.DOFade(0f, titleFade));
        show.OnComplete(() => { Clear(); group.alpha = 0f; show = null; ExperienceBarUI.Resume(); });
    }

    // "+1 attribute point" twice becomes "+2 attribute points"; other lines stay as they are, in order.
    static List<string> Merge(List<string> gains)
    {
        const string point = "+1 attribute point";
        int points = 0;
        var merged = new List<string>();
        foreach (var g in gains) { if (g == point) points++; else merged.Add(g); }
        if (points > 0) merged.Insert(0, points == 1 ? point : $"+{points} attribute points");
        return merged;
    }

    void Clear()
    {
        gone.Clear();
        foreach (var l in lines) if (l != null) Destroy(l.gameObject);
        lines.Clear();
    }
}

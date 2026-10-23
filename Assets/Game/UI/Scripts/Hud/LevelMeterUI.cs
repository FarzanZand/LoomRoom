using DG.Tweening;
using TMPro;
using UnityEngine;

// The character level meter at the top of the screen: every time the table player gains XP (a kill, a new floor) it
// comes in showing "LEVEL n" and the bar where it stood, the gain shows as a pale stretch ahead of the fill, the fill
// runs up into it with a bright leading edge, and "+24 XP" rises beside it; a level reached fills the bar, flashes it,
// and it starts again from empty at the new level (the level-up panel says the rest). It stays a moment, then fades.
// More XP while it shows carries on from where the fill is. Authored under the HUD canvas: the group, the level text,
// the gain text, and inside the bar's frame two stretched images (gain and fill, filled by their anchors' right edge)
// plus the leading edge.
public class LevelMeterUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text levelText, gainText;
    [SerializeField, Tooltip("The pale stretch the gain covers, ahead of the fill.")] RectTransform gain;
    [SerializeField] RectTransform fill;
    [SerializeField, Tooltip("A short bright piece riding the fill's right edge while it runs.")] RectTransform edge;
    [SerializeField, Tooltip("Flashes the bar when a level is reached.")] CanvasGroup flash;
    [SerializeField, Min(.05f)] float fadeIn = .15f, runSeconds = .55f, hold = 1.8f, fadeOut = .5f;
    [SerializeField, Min(0f)] float gainRise = 14f;

    AdventurerProgress progress;
    int shownLevel = -1;
    float shownFraction;
    bool pending;
    float pendingAmount;
    Sequence run;
    Vector2 gainTextAt;

    void Awake()
    {
        if (group != null) group.alpha = 0f;
        if (flash != null) flash.alpha = 0f;
        if (gainText != null) gainTextAt = gainText.rectTransform.anchoredPosition;
    }

    void OnDisable()
    {
        if (progress != null) progress.ExperienceGained -= Gained;
        progress = null;
        run?.Kill(); run = null;
        if (group != null) group.alpha = 0f;
    }

    void Bind()
    {
        if (progress != null || !PlayerManager.HasInstance) return;
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
        if (progress == null) return;
        progress.ExperienceGained += Gained;
        Remember();
    }

    float Fraction => progress == null || progress.NextLevelXp <= 0f ? 0f : Mathf.Clamp01(progress.Experience / progress.NextLevelXp);
    void Remember() { shownLevel = progress.Level; shownFraction = Fraction; }

    // Fired before the level-ups are applied: the meter reads the result at the end of the frame.
    void Gained(float amount) { pending = true; pendingAmount += amount; }

    void LateUpdate()
    {
        if (progress == null) { Bind(); return; }
        // A new run starts at level 1: nothing to show, just the new starting point.
        if (!progress.InRun || progress.Level < shownLevel) { if (run == null || !run.IsActive()) Remember(); pending = false; pendingAmount = 0f; return; }
        if (!pending) return;
        pending = false;
        Show(pendingAmount);
        pendingAmount = 0f;
    }

    void Show(float amount)
    {
        if (group == null || fill == null) return;
        // Carrying on from wherever a running fill had got to.
        if (run != null && run.IsActive()) { shownFraction = fill.anchorMax.x; run.Kill(); }
        int fromLevel = shownLevel, toLevel = progress.Level;
        float from = shownFraction, to = Fraction;
        Remember();

        if (levelText != null) levelText.text = "LEVEL " + fromLevel;
        if (gainText != null)
        {
            gainText.text = $"+{Mathf.RoundToInt(amount)} XP";
            gainText.rectTransform.anchoredPosition = gainTextAt;
            gainText.alpha = 0f;
        }
        SetFill(from); SetGain(toLevel > fromLevel ? 1f : to);
        if (edge != null) edge.gameObject.SetActive(false);

        run = DOTween.Sequence().SetUpdate(true);
        run.Append(group.DOFade(1f, fadeIn));
        run.AppendCallback(() => { if (edge != null) edge.gameObject.SetActive(true); });
        // Up to each level reached: fill to the end, flash, start again empty at the next level.
        float start = from;
        for (int level = fromLevel; level < toLevel; level++)
        {
            int reached = level + 1;
            run.Append(Run(start, 1f, runSeconds * (1f - start)));
            run.AppendCallback(() => Reached(reached, reached < toLevel ? 1f : to));
            run.AppendInterval(.25f);
            start = 0f;
        }
        run.Append(Run(start, to, runSeconds * Mathf.Max(.25f, to - start)));
        run.AppendCallback(() => { if (edge != null) edge.gameObject.SetActive(false); });
        run.AppendInterval(hold);
        run.Append(group.DOFade(0f, fadeOut));
        // The "+XP" rises alongside it all: inserted after the rest is built (an Append goes after everything already
        // in the sequence, so inserting it first would hold the fill back).
        if (gainText != null)
        {
            run.Insert(0f, gainText.DOFade(1f, fadeIn));
            run.Insert(0f, gainText.rectTransform.DOAnchorPosY(gainTextAt.y + gainRise, runSeconds + hold).SetEase(Ease.OutQuad));
        }
    }

    Tween Run(float from, float to, float seconds) =>
        DOTween.To(() => from, SetFill, to, Mathf.Max(.08f, seconds)).SetEase(Ease.OutCubic).SetUpdate(true);

    // A level reached: the full bar flashes and empties for the new level.
    void Reached(int level, float nextGain)
    {
        if (levelText != null)
        {
            levelText.text = "LEVEL " + level;
            levelText.rectTransform.DOKill(true);
            levelText.rectTransform.DOPunchScale(Vector3.one * .25f, .35f, 6, .5f).SetUpdate(true);
        }
        if (flash != null) { flash.DOKill(); flash.alpha = 1f; flash.DOFade(0f, .45f).SetUpdate(true); }
        SetFill(0f); SetGain(nextGain);
    }

    void SetFill(float amount)
    {
        amount = Mathf.Clamp01(amount);
        fill.anchorMax = new Vector2(amount, fill.anchorMax.y);
        if (edge != null) { edge.anchorMin = new Vector2(amount, edge.anchorMin.y); edge.anchorMax = new Vector2(amount, edge.anchorMax.y); }
    }

    void SetGain(float amount)
    {
        if (gain != null) gain.anchorMax = new Vector2(Mathf.Clamp01(amount), gain.anchorMax.y);
    }
}

using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Character XP bar and queued, animated skill XP notifications.
public class ExperienceBarUI : MonoBehaviour
{
    [SerializeField] CanvasGroup bar;
    [SerializeField] TMP_Text title, amount;
    [SerializeField] Image fill;
    [SerializeField, Min(.1f)] float fillSpeed = 2f;

    [Header("Skill XP notification")]
    [SerializeField] CanvasGroup popup;
    [SerializeField] TMP_Text popupTitle, popupRank;
    [SerializeField] Image popupIcon, popupRing;
    [SerializeField, Min(.05f)] float progressDuration = .45f;
    [SerializeField, Min(0)] float popupHold = 2.5f, popupFade = .4f;

    [Header("Skill rank celebration")]
    [SerializeField] CanvasGroup xpVisuals, rankVisuals, rankFlash;
    [SerializeField] TMP_Text rankTitle, rankNumber;
    [SerializeField] Image rankIcon;
    [SerializeField, Min(0)] float rankHold = 3f, rankRevealDuration = .25f;
    [SerializeField, Range(0, .5f)] float rankPunch = .15f;
    [SerializeField] RectTransform rankBadge;
    [SerializeField] CanvasGroup rankLabelGroup, rankValueGroup, rankGainGroup;
    [SerializeField] TMP_Text rankGainText;
    [SerializeField, Min(.05f)] float iconSettleDuration = .3f, rankCountDuration = .4f;
    [SerializeField, Min(1)] float iconRevealScale = 1.65f;
    [SerializeField] float gainRise = 10f;
    Vector2 badgePosition, gainPosition;
    Vector3 badgeScale, valueScale;
    Vector3 rankBaseScale;
    void Awake()
    {
        if (rankVisuals != null) rankBaseScale = rankVisuals.transform.localScale;
        if (rankBadge != null) { badgePosition = rankBadge.anchoredPosition; badgeScale = rankBadge.localScale; }
        if (rankGainGroup != null) gainPosition = ((RectTransform)rankGainGroup.transform).anchoredPosition;
        if (rankValueGroup != null) valueScale = rankValueGroup.transform.localScale;
    }

    AdventurerProgress progress;
    float shown;
    class Gain
    {
        public AdventureSkill skill;
        public float xp, before, after;
        public int ranks, resultingRank;
    }
    readonly List<Gain> raises = new();
    Sequence popupRoutine;
    Gain activeGain;

    void OnEnable() { if (popup != null) popup.alpha = 0; if (bar != null) bar.alpha = 0; }
    void OnDisable() { Bind(null); popupRoutine?.Kill(); raises.Clear(); }

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var current = player != null ? player.GetComponent<AdventurerProgress>() : null;
        if (current != progress) Bind(current);
        bool visible = progress != null && progress.InRun && player.IsActive && progress.selectedClass != null;
        if (bar != null) bar.alpha = visible ? 1 : 0;
        if (!visible) { popupRoutine?.Kill(); popupRoutine = null; activeGain = null; raises.Clear(); if (popup != null) popup.alpha = 0; return; }
        float target = progress.NextLevelXp > 0 ? Mathf.Clamp01(progress.Experience / progress.NextLevelXp) : 0;
        shown = target < shown ? target : Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * fillSpeed);
        UIBar.Set(fill, shown);
        if (title != null) title.text = $"LVL {progress.Level}  {progress.selectedClass.displayName}";
        if (amount != null) amount.text = $"{progress.Experience:0} / {progress.NextLevelXp:0} XP";
    }

    void Bind(AdventurerProgress next)
    {
        if (progress != null) progress.SkillExperienceGained -= OnSkillExperienceGained;
        popupRoutine?.Kill(); popupRoutine = null; activeGain = null; raises.Clear();
        if (popup != null) popup.alpha = 0;
        progress = next;
        if (progress != null) progress.SkillExperienceGained += OnSkillExperienceGained;
        shown = progress != null && progress.NextLevelXp > 0 ? progress.Experience / progress.NextLevelXp : 0;
    }

    void OnSkillExperienceGained(AdventureSkill skill, float xp, float before, float after, int ranks)
    {
        // XP may coalesce, but rank events retain their own rank and animation.
        var pending = ranks == 0 ? raises.Find(x => x.skill == skill && x.ranks == 0) : null;
        if (pending != null) { pending.xp += xp; pending.after = after; pending.resultingRank = progress.Rank(skill); }
        else raises.Add(new Gain { skill = skill, xp = xp, before = before, after = after, ranks = ranks, resultingRank = progress.Rank(skill) });
        if (ranks > 0)
        {
            // The rank reveal supersedes stale progress for this skill.
            raises.RemoveAll(x => x.skill == skill && x.ranks == 0);
            if (activeGain != null && activeGain.ranks == 0)
            {
                popupRoutine?.Kill(); popupRoutine = null; activeGain = null;
            }
        }
        if (popupRoutine == null || !popupRoutine.IsActive()) NextPopup();
    }

    void NextPopup()
    {
        popupRoutine = null; activeGain = null;
        if (popup == null || raises.Count == 0 || progress == null) return;
        int index = raises.FindIndex(x => x.ranks > 0);
        if (index < 0) index = 0;
        var gain = raises[index]; raises.RemoveAt(index);
        activeGain = gain;
        var def = progress.Definition(gain.skill);
        if (popupTitle != null) popupTitle.text = def?.displayName ?? gain.skill.ToString();
        if (popupRank != null) popupRank.text = $"+{gain.xp:0.#} XP";
        if (popupIcon != null) { popupIcon.sprite = def != null ? def.icon : null; popupIcon.enabled = popupIcon.sprite != null; }
        if (xpVisuals != null) xpVisuals.alpha = 1;
        if (rankVisuals != null) { rankVisuals.alpha = 0; rankVisuals.transform.localScale = rankBaseScale; }
        if (rankFlash != null) rankFlash.alpha = 0;
        popup.alpha = 0;
        popupRoutine = DOTween.Sequence().SetUpdate(true).Append(popup.DOFade(1, .12f));
        if (popupRing != null)
        {
            popupRing.fillAmount = gain.before;
            if (gain.ranks > 0)
            {
                popupRoutine.Append(popupRing.DOFillAmount(1, progressDuration).SetEase(Ease.OutCubic));
                if (rankVisuals == null && gain.after < 1) popupRoutine.AppendCallback(() => popupRing.fillAmount = 0)
                    .Append(popupRing.DOFillAmount(gain.after, progressDuration).SetEase(Ease.OutCubic));
            }
            else popupRoutine.Append(popupRing.DOFillAmount(gain.after, progressDuration).SetEase(Ease.OutCubic));
        }
        if (gain.ranks > 0 && rankVisuals != null)
        {
            popupRoutine.AppendCallback(() =>
            {
                if (rankTitle != null) rankTitle.text = $"{def?.displayName ?? gain.skill.ToString()} increased!";
                if (rankNumber != null) rankNumber.text = (gain.resultingRank - gain.ranks).ToString();
                if (rankLabelGroup != null) rankLabelGroup.alpha = 0;
                if (rankValueGroup != null) { rankValueGroup.alpha = 0; rankValueGroup.transform.localScale = valueScale; }
                if (rankBadge != null) { rankBadge.anchoredPosition = new Vector2(0, badgePosition.y); rankBadge.localScale = badgeScale * iconRevealScale; }
                if (rankGainGroup != null) { rankGainGroup.alpha = 0; ((RectTransform)rankGainGroup.transform).anchoredPosition = gainPosition; }
                if (rankGainText != null) rankGainText.text = $"+{gain.ranks}";
                if (rankIcon != null) { rankIcon.sprite = def != null ? def.icon : null; rankIcon.enabled = rankIcon.sprite != null; }
                if (rankFlash != null) rankFlash.alpha = 1;
            });
            if (xpVisuals != null) popupRoutine.Append(xpVisuals.DOFade(0, .1f));
            popupRoutine.Append(rankVisuals.DOFade(1, .1f));
            if (rankBadge != null)
                popupRoutine.Append(rankBadge.DOScale(badgeScale, iconSettleDuration).SetEase(Ease.OutCubic))
                    .Join(rankBadge.DOAnchorPos(badgePosition, iconSettleDuration).SetEase(Ease.InOutSine));
            if (rankLabelGroup != null) popupRoutine.Append(rankLabelGroup.DOFade(1, rankRevealDuration));
            if (rankValueGroup != null) popupRoutine.Join(rankValueGroup.DOFade(1, rankRevealDuration));
            popupRoutine.AppendInterval(.15f);
            int displayedRank = gain.resultingRank - gain.ranks;
            popupRoutine.Append(DOTween.To(() => displayedRank, value =>
            {
                displayedRank = value;
                if (rankNumber != null) rankNumber.text = value.ToString();
            }, gain.resultingRank, rankCountDuration).SetEase(Ease.Linear));
            if (rankValueGroup != null) popupRoutine.Join(rankValueGroup.transform.DOPunchScale(Vector3.one * rankPunch, rankCountDuration, 1, .4f));
            if (rankGainGroup != null)
            {
                popupRoutine.Join(rankGainGroup.DOFade(1, .12f));
                popupRoutine.Join(((RectTransform)rankGainGroup.transform).DOAnchorPosY(gainPosition.y + gainRise, rankCountDuration + .35f).SetEase(Ease.OutSine));
                popupRoutine.Append(rankGainGroup.DOFade(0, .3f));
            }
            if (rankFlash != null) popupRoutine.Join(rankFlash.DOFade(0, rankRevealDuration));
        }
        popupRoutine.AppendInterval(gain.ranks > 0 ? rankHold : (raises.Count > 0 ? popupHold * .5f : popupHold))
            .Append(popup.DOFade(0, popupFade)).OnComplete(NextPopup);
    }
}

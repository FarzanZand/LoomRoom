using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Barony's top bar: level, class and experience, and under it "Swords increased!" with the
// skill's icon and new rank whenever a skill goes up. Only shown during a dungeon run.
public class ExperienceBarUI : MonoBehaviour
{
    [SerializeField] CanvasGroup bar;
    [SerializeField] TMP_Text title, amount;
    [SerializeField] Image fill;
    [SerializeField, Min(.1f)] float fillSpeed = 2f;

    [Header("Skill increase")]
    [SerializeField] CanvasGroup popup;
    [SerializeField] TMP_Text popupTitle, popupRank;
    [SerializeField] Image popupIcon;
    [SerializeField, Min(0)] float popupHold = 2.5f, popupFade = .4f;

    AdventurerProgress progress;
    float shown;
    readonly Queue<(AdventureSkill skill, int rank)> raises = new();
    Sequence popupRoutine;

    void OnEnable() { if (popup != null) popup.alpha = 0; if (bar != null) bar.alpha = 0; }
    void OnDisable() { Bind(null); popupRoutine?.Kill(); raises.Clear(); }

    void Update()
    {
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var current = player != null ? player.GetComponent<AdventurerProgress>() : null;
        if (current != progress) Bind(current);
        bool visible = progress != null && progress.InRun && player.IsActive && progress.selectedClass != null;
        if (bar != null) bar.alpha = visible ? 1 : 0;
        if (!visible) return;
        float target = progress.NextLevelXp > 0 ? Mathf.Clamp01(progress.Experience / progress.NextLevelXp) : 0;
        shown = target < shown ? target : Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * fillSpeed);
        if (fill != null) fill.fillAmount = shown;
        if (title != null) title.text = $"LVL {progress.Level}  {progress.selectedClass.displayName}";
        if (amount != null) amount.text = $"{progress.Experience:0} / {progress.NextLevelXp:0} XP";
    }

    void Bind(AdventurerProgress next)
    {
        if (progress != null) progress.SkillRaised -= OnSkillRaised;
        progress = next;
        if (progress != null) progress.SkillRaised += OnSkillRaised;
        shown = progress != null && progress.NextLevelXp > 0 ? progress.Experience / progress.NextLevelXp : 0;
    }

    void OnSkillRaised(AdventureSkill skill, int rank)
    {
        raises.Enqueue((skill, rank));
        if (popupRoutine == null || !popupRoutine.IsActive()) NextPopup();
    }

    void NextPopup()
    {
        popupRoutine = null;
        if (popup == null || raises.Count == 0 || progress == null) return;
        var (skill, rank) = raises.Dequeue();
        var def = progress.Definition(skill);
        if (popupTitle != null) popupTitle.text = $"{(def != null ? def.displayName : skill.ToString())} increased!";
        if (popupRank != null) popupRank.text = rank.ToString();
        if (popupIcon != null) { popupIcon.sprite = def != null ? def.icon : null; popupIcon.enabled = popupIcon.sprite != null; }
        popup.alpha = 0;
        popupRoutine = DOTween.Sequence().SetUpdate(true)
            .Append(popup.DOFade(1, .12f))
            .AppendInterval(raises.Count > 0 ? popupHold * .5f : popupHold)
            .Append(popup.DOFade(0, popupFade))
            .OnComplete(NextPopup);
    }
}

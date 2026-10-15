using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

// Short line in the middle of the screen: a skill or level going up. Queues when several arrive together.
public class AnnouncementUI : Singleton<AnnouncementUI>
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TextMeshProUGUI title;
    [SerializeField] TextMeshProUGUI subtitle;
    [SerializeField, Min(0)] float fadeIn = .15f, hold = 1.4f, fadeOut = .4f;

    readonly Queue<(string, string, float)> queue = new();
    Sequence routine;

    protected override void Awake()
    {
        base.Awake();
        if (group != null) group.alpha = 0;
    }

    // holdFor: seconds it stays up (0 or less = the usual hold).
    public static void Show(string text, string sub = null, float holdFor = 0f)
    {
        if (HasInstance) Instance.Enqueue(text, sub, holdFor);
        else NotificationUI.Show(text);
    }

    void Enqueue(string text, string sub, float holdFor)
    {
        queue.Enqueue((text, sub, holdFor));
        if (routine == null || !routine.IsActive()) Next();
    }

    void Next()
    {
        routine = null;
        if (group == null || queue.Count == 0) return;
        var (text, sub, holdFor) = queue.Dequeue();
        float stay = holdFor > 0f ? holdFor : hold;
        if (title != null) title.text = text;
        if (subtitle != null) { subtitle.text = sub ?? ""; subtitle.gameObject.SetActive(!string.IsNullOrEmpty(sub)); }
        group.alpha = 0;
        var rect = (RectTransform)group.transform;
        rect.localScale = Vector3.one * .92f;
        routine = DOTween.Sequence().SetUpdate(true)
            .Append(group.DOFade(1, fadeIn)).Join(rect.DOScale(1, fadeIn).SetEase(Ease.OutBack))
            .AppendInterval(queue.Count > 0 ? stay * .6f : stay)
            .Append(group.DOFade(0, fadeOut))
            .OnComplete(Next);
    }

    void OnDisable() { routine?.Kill(); routine = null; queue.Clear(); if (group != null) group.alpha = 0; }
}

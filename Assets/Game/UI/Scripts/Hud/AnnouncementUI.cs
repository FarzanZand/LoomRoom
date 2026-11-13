using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

// Short line in the middle of the screen (the boss blessing, the dev toggles). Queues when several arrive together, and
// waits while the level-up panel is up, which uses the same spot.
public class AnnouncementUI : Singleton<AnnouncementUI>
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TextMeshProUGUI title;
    [SerializeField] TextMeshProUGUI subtitle;
    [SerializeField, Min(0)] float fadeIn = .15f, hold = 1.4f, fadeOut = .4f;
    [Header("Big (Bag full)")]
    [SerializeField, Tooltip("Where a big line stands (from the screen's middle; just above the crosshair so it doesn't cover it). The user, 2026-11-13: \"Bag full\" bigger and centred.")]
    Vector2 bigAt = new(0f, 40f);
    [SerializeField, Min(1f), Tooltip("How much bigger a big line is drawn.")] float bigScale = 1.6f;

    readonly Queue<(string, string, float, bool)> queue = new();
    Vector2 home; bool homeKnown;
    Sequence routine;

    protected override void Awake()
    {
        base.Awake();
        if (group != null) group.alpha = 0;
    }

    // holdFor: seconds it stays up (0 or less = the usual hold). once: not queued again while the same line is up or
    // waiting (a "Bag full" for every try at a pickup would stack up behind itself); the one up stays a full hold more.
    // big: in the middle of the screen, larger and outlined (Bag full).
    public static void Show(string text, string sub = null, float holdFor = 0f, bool once = false, bool big = false)
    {
        if (!HasInstance) { NotificationUI.Show(text); return; }
        if (once && Instance.Showing(text)) { Instance.Restart(); return; }
        Instance.Enqueue(text, sub, holdFor, big);
    }

    string shown;
    bool Showing(string text)
    {
        foreach (var (t, _, _, _) in queue) if (t == text) return true;
        return shown == text && routine != null && routine.IsActive();
    }
    // The line already up stays a full hold more.
    void Restart()
    {
        if (routine == null || !routine.IsActive() || queue.Count > 0 || group == null) return;
        routine.Kill();
        group.alpha = 1f; ((RectTransform)group.transform).localScale = Vector3.one * (shownBig ? bigScale : 1f);
        routine = DOTween.Sequence().SetUpdate(true).AppendInterval(hold).Append(group.DOFade(0, fadeOut)).OnComplete(Next);
    }

    void Enqueue(string text, string sub, float holdFor, bool big)
    {
        queue.Enqueue((text, sub, holdFor, big));
        if (routine == null || !routine.IsActive()) Next();
    }

    void Next()
    {
        routine = null;
        if (group == null || queue.Count == 0) return;
        // The level-up panel takes the same middle of the screen: wait until it has gone.
        if (LevelUpShowing) { routine = DOTween.Sequence().SetUpdate(true).AppendInterval(.25f).OnComplete(Next); return; }
        var (text, sub, holdFor, big) = queue.Dequeue();
        float stay = holdFor > 0f ? holdFor : hold;
        shown = text;
        Place(big);
        if (title != null) title.text = text;
        if (subtitle != null) { subtitle.text = sub ?? ""; subtitle.gameObject.SetActive(!string.IsNullOrEmpty(sub)); }
        group.alpha = 0;
        var rect = (RectTransform)group.transform;
        float scale = big ? bigScale : 1f;
        rect.localScale = Vector3.one * .92f * scale;
        routine = DOTween.Sequence().SetUpdate(true)
            .Append(group.DOFade(1, fadeIn)).Join(rect.DOScale(scale, fadeIn).SetEase(Ease.OutBack))
            .AppendInterval(queue.Count > 0 ? stay * .6f : stay)
            .Append(group.DOFade(0, fadeOut))
            .OnComplete(Next);
    }

    // A big line in the middle of the screen; any other back at its own place. (An outline round the letters ate the
    // pixel font's thin strokes.)
    bool shownBig;
    void Place(bool big)
    {
        var rect = (RectTransform)group.transform;
        if (!homeKnown) { home = rect.anchoredPosition; homeKnown = true; }
        rect.anchoredPosition = big ? bigAt : home;
        shownBig = big;
    }

    CanvasGroup levelUp;
    bool levelUpLooked;
    bool LevelUpShowing
    {
        get
        {
            if (!levelUpLooked) { levelUpLooked = true; var ui = FindAnyObjectByType<LevelUpUI>(FindObjectsInactive.Include); levelUp = ui != null ? ui.GetComponent<CanvasGroup>() : null; }
            return levelUp != null && levelUp.isActiveAndEnabled && levelUp.alpha > .05f;
        }
    }

    void OnDisable() { routine?.Kill(); routine = null; queue.Clear(); if (group != null) group.alpha = 0; }
}

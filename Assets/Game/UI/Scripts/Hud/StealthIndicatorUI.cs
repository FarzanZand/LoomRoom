using DG.Tweening;
using UnityEngine;

public class StealthIndicatorUI : MonoBehaviour
{
    [SerializeField] CanvasGroup visibility;
    [SerializeField] StealthEyeGraphic eye;
    [SerializeField] Color hiddenColor = new(.85f, .82f, .72f), seenColor = new(.95f, .58f, .35f);
    [SerializeField, Min(0)] float transitionDuration = .18f;
    Tween transition;
    bool? lastSeen;

    void OnEnable() { if (visibility != null) visibility.alpha = 0; lastSeen = null; }
    void OnDisable() { transition?.Kill(); if (visibility != null) visibility.alpha = 0; lastSeen = null; }

    void Update()
    {
        if (eye == null || visibility == null) return;
        var player = PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;
        bool sneaking = player != null && player.kind == PlayerKind.Table && player.IsActive && player.IsAlive && player.Motor != null && player.Motor.IsCrouching;
        visibility.alpha = sneaking ? 1 : 0;
        if (!sneaking) { transition?.Kill(); lastSeen = null; return; }
        bool seen = false;
        foreach (var brain in EnemyBrain.Active)
            if (brain != null && brain.Character != null && brain.Character.IsAlive && brain.Perception != null && brain.Perception.Target == player && brain.Perception.TargetVisible) { seen = true; break; }
        if (lastSeen == seen) return;
        transition?.Kill();
        eye.color = seen ? seenColor : hiddenColor;
        float target = seen ? 1 : 0;
        if (!lastSeen.HasValue) eye.Openness = target;
        else transition = DOTween.To(() => eye.Openness, value => eye.Openness = value, target, transitionDuration).SetEase(Ease.OutCubic).SetUpdate(true);
        lastSeen = seen;
    }
}

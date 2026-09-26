using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Big health bar at the top of the screen during a boss encounter.
public class BossBarUI : Singleton<BossBarUI>
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text bossName;
    [SerializeField] TMP_Text subtitle;
    [Tooltip("Filled by anchorMax.x, like the enemy bars.")]
    [SerializeField] RectTransform fill;
    [SerializeField] RectTransform recentDamage;
    [SerializeField, Min(.05f)] float fadeSeconds = .6f;

    Character boss;
    public Character Boss => boss;
    float shown = 1f, lossHoldUntil;
    Tween fade;

    protected override void Awake()
    {
        base.Awake();
        if (group != null) group.alpha = 0f;
    }

    public void Show(Character character, string title)
    {
        Unbind();
        boss = character;
        if (boss != null) boss.Damaged += OnDamaged;
        if (bossName != null) bossName.text = boss != null ? boss.DisplayName.ToUpperInvariant() : "";
        if (subtitle != null) subtitle.text = title ?? "";
        shown = 1f;
        Apply(1f);
        fade?.Kill();
        if (group != null) fade = group.DOFade(1f, fadeSeconds).SetUpdate(true);
    }

    public void Hide()
    {
        Unbind();
        fade?.Kill();
        if (group != null) fade = group.DOFade(0f, fadeSeconds).SetUpdate(true);
    }

    void Unbind()
    {
        if (boss != null) boss.Damaged -= OnDamaged;
        boss = null;
    }

    void OnDamaged(DamageInfo info) => lossHoldUntil = Time.time + .4f;

    void Update()
    {
        if (boss == null || boss.Stats == null) return;
        float health = boss.Stats.MaxHealth > 0 ? Mathf.Clamp01(boss.Stats.CurrentHealth / boss.Stats.MaxHealth) : 0f;
        if (Time.time > lossHoldUntil) shown = Mathf.MoveTowards(shown, health, Time.deltaTime * .8f);
        shown = Mathf.Max(shown, health);
        Apply(health);
    }

    void Apply(float health)
    {
        if (fill != null) fill.anchorMax = new Vector2(health, fill.anchorMax.y);
        if (recentDamage != null) recentDamage.anchorMax = new Vector2(shown, recentDamage.anchorMax.y);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Unbind();
    }
}

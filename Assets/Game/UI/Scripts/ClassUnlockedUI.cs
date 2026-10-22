using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A class unlocked (the room puzzle): a card pops up in the middle of the screen with the class's portrait, "NEW CLASS",
// its name and where to pick it, over a gold glow and slowly turning rays, with AudioManager's classUnlocked sting. It
// stays a moment and fades. Authored under the HUD canvas (built by ClassUnlockedSetup.Build()); code only fills and
// plays it.
public class ClassUnlockedUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] RectTransform card;
    [SerializeField] Image glow, rays, portrait;
    [SerializeField] TMP_Text heading, className, line;
    [SerializeField, Min(0f), Tooltip("Seconds the card stays before it fades.")] float hold = 3.5f;
    [SerializeField, Tooltip("AudioManager UI Library key played as it lands.")] string soundKey = "classUnlocked";
    [SerializeField, Tooltip("Degrees a second the rays turn.")] float raySpeed = 14f;

    static ClassUnlockedUI instance;
    Sequence show;

    void Awake()
    {
        instance = this;
        if (group != null) group.alpha = 0f;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        show?.Kill();
    }

    // Shows the card for a class (its sheet portrait and name; the name alone when there is no class asset).
    public static void Show(AdventurerClass unlocked, string fallbackName)
    {
        if (instance == null) return;
        instance.Play(unlocked != null && !string.IsNullOrEmpty(unlocked.displayName) ? unlocked.displayName : fallbackName,
            unlocked != null ? (unlocked.sheetPortrait != null ? unlocked.sheetPortrait : unlocked.portrait) : null);
    }

    void Play(string name, Sprite picture)
    {
        if (group == null || card == null) return;
        if (className != null) className.text = name.ToUpperInvariant();
        if (portrait != null) { portrait.sprite = picture; portrait.enabled = picture != null; }
        show?.Kill();
        group.alpha = 0f;
        card.localScale = Vector3.one * .4f;
        if (glow != null) { glow.color = new Color(glow.color.r, glow.color.g, glow.color.b, 0f); glow.rectTransform.localScale = Vector3.one * .5f; }
        if (rays != null) rays.rectTransform.localScale = Vector3.one * .6f;
        if (AudioManager.HasInstance && AudioManager.Instance.HasUI(soundKey)) AudioManager.Instance.PlayUI(soundKey);
        else if (AudioManager.HasInstance) AudioManager.Instance.PlayLevelUp();
        show = DOTween.Sequence().SetUpdate(true);
        show.Append(group.DOFade(1f, .12f));
        show.Join(card.DOScale(1.1f, .32f).SetEase(Ease.OutBack));
        if (glow != null) show.Join(glow.DOFade(1f, .18f)).Join(glow.rectTransform.DOScale(1.25f, .5f).SetEase(Ease.OutQuad));
        if (rays != null) show.Join(rays.rectTransform.DOScale(1f, .6f).SetEase(Ease.OutQuad));
        show.Append(card.DOScale(1f, .18f).SetEase(Ease.InOutQuad));
        if (glow != null) show.Join(glow.DOFade(.55f, .7f));
        show.AppendInterval(hold);
        show.Append(group.DOFade(0f, .7f));
    }

    void Update()
    {
        if (rays != null && group != null && group.alpha > 0f) rays.rectTransform.Rotate(0f, 0f, -raySpeed * Time.unscaledDeltaTime);
    }
}

using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One adventure card at the table: the level's picture, name, description and a short tag line.
// Hover or keyboard focus lifts the card and lights its frame.
public class AdventureOptionUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public Button button;
    public TMP_Text title;
    public TMP_Text description;
    public TMP_Text tagLine;
    public Image art;
    [Tooltip("Frame shown while the card is hovered or selected.")]
    public GameObject highlight;
    [Tooltip("Moved up a little on hover; usually the card's visual root.")]
    public RectTransform lift;

    Tween tween;

    public void Bind(TableLevelData level)
    {
        title.text = level.displayName;
        description.text = level.description;
        if (tagLine != null)
            tagLine.text = level.kind == TableLevelKind.Dungeon
                ? (level.multipleLevels ? $"DUNGEON  ·  {Mathf.Max(1, level.levelCount)} FLOORS" : "DUNGEON")
                : "TOWN";
        if (art != null)
        {
            art.sprite = level.cardArt;
            art.enabled = level.cardArt != null;
        }
        Show(false, true);
    }

    public void OnPointerEnter(PointerEventData data)
    {
        if (!button.interactable) return;
        EventSystem.current?.SetSelectedGameObject(gameObject);
        UIFeedbackSettings.Shared?.Hover();
    }

    public void OnPointerExit(PointerEventData data)
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void OnSelect(BaseEventData data) => Show(true);
    public void OnDeselect(BaseEventData data) => Show(false);

    void Show(bool on, bool instant = false)
    {
        if (highlight != null) highlight.SetActive(on);
        if (lift == null) return;
        tween?.Kill();
        var target = new Vector2(lift.anchoredPosition.x, on ? 8 : 0);
        if (instant) lift.anchoredPosition = target;
        else tween = lift.DOAnchorPos(target, .12f).SetEase(Ease.OutCubic).SetUpdate(true);
    }

    void OnDisable() { tween?.Kill(); }
}

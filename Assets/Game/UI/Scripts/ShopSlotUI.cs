using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One cell of the player's pack in the shop, laid out like the inventory grid. Hover shows the
// item and its sell price; click sells one. Empty cells stay visible so the grid keeps its shape.
public class ShopSlotUI : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    [SerializeField] Image background;
    [SerializeField] Image icon;
    [SerializeField] Image honedFill;
    [SerializeField] TMP_Text count;
    [SerializeField] Button button;
    [SerializeField] Color emptyColor = new(.035f, .045f, .045f, .96f);
    [SerializeField] Color filledColor = new(.09f, .11f, .105f, .98f);

    ItemData item;
    Action click;
    Action<ItemData> hover;

    void Awake() => button?.onClick.AddListener(() => click?.Invoke());

    public void Bind(ItemData data, int amount, bool available, Action onClick, Action<ItemData> onHover)
    {
        item = data; click = onClick; hover = onHover;
        bool filled = data != null;
        if (icon != null) { icon.sprite = filled ? data.icon : null; icon.enabled = icon.sprite != null; icon.color = available ? Color.white : new Color(1, 1, 1, .4f); }
        if (honedFill != null) honedFill.enabled = filled && data.honed > 0;
        if (count != null) count.text = filled && amount > 1 ? $"x{amount}" : "";
        if (background != null) background.color = filled ? filledColor : emptyColor;
        if (button != null) button.interactable = filled && available;
    }

    public void Clear() => Bind(null, 0, false, null, null);

    public void OnPointerEnter(PointerEventData eventData) => hover?.Invoke(item);
    public void OnSelect(BaseEventData eventData) => hover?.Invoke(item);
}

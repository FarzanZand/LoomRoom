using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One cell of the player's pack in the shop, laid out like the inventory grid. Hover (or a left click) shows the
// item and its sell price; a right click (or Submit on a pad or keyboard) sells one. Empty cells stay visible so the
// grid keeps its shape.
public class ShopSlotUI : MonoBehaviour, IPointerEnterHandler, ISelectHandler, IPointerClickHandler, ISubmitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] Image background;
    [SerializeField] Image icon;
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("honedFill")] Image rarityFill;
    [SerializeField] TMP_Text count;
    [SerializeField] Button button;

    ItemData item;
    Action click;
    Action<ItemData> hover;


    // drawItem false: a bag cell under an item drawn across its footprint (ShopUI.DrawBag); the cell only answers for it.
    public void Bind(ItemData data, int amount, bool available, Action onClick, Action<ItemData> onHover, bool drawItem = true)
    {
        item = data; click = onClick; hover = onHover;
        bool filled = data != null;
        bool drawn = filled && drawItem;
        if (icon != null) { icon.sprite = drawn ? data.icon : null; icon.enabled = icon.sprite != null; icon.color = available ? Color.white : new Color(1, 1, 1, .4f); }
        ItemData.ShowSlotFill(rarityFill, data, drawn);
        if (count != null) count.text = drawn && amount > 1 ? $"x{amount}" : "";
        // The inventory's grey cell, the same filled or empty (the item's rarity floor marks it).
        if (background != null) background.color = Color.white;
        if (button != null) button.interactable = filled && available;
    }

    public void Clear() => Bind(null, 0, false, null, null);

    public void OnPointerEnter(PointerEventData eventData) => hover?.Invoke(item);
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) Sell();
        else hover?.Invoke(item);
    }
    public void OnSubmit(BaseEventData eventData) => Sell();
    void Sell() { if (button == null || button.interactable) click?.Invoke(); }

    // Dragged onto the wares, it sells (ShopUI).
    bool dragging;
    public void OnBeginDrag(PointerEventData eventData)
    {
        dragging = item != null && eventData.button == PointerEventData.InputButton.Left && ShopUI.HasInstance && ShopUI.Instance.BeginDrag(item, icon);
    }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;
        if (ShopUI.Instance.EndDrag(eventData.position) == ShopUI.DropZone.Wares) Sell();
    }
    public void OnSelect(BaseEventData eventData) => hover?.Invoke(item);
}

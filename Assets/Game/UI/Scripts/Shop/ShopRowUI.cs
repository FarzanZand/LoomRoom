using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One ware in the shop: icon, name, a short info line, count and price. Hover shows details; a left or right click
// buys, and so does dragging it onto the pack (ShopUI).
public class ShopRowUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IDeselectHandler, ISelectHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] Image icon;
    [Tooltip("The ware's rarity floor behind its icon (the inventory's), shown for Fine, Masterwork and other coloured items.")]
    [SerializeField] Image rarityFill;
    [SerializeField] TMP_Text itemName;
    [SerializeField] TMP_Text info;
    [SerializeField] TMP_Text count;
    [SerializeField] TMP_Text price;
    [SerializeField] Button button;
    [SerializeField] CanvasGroup group;

    ItemData item;
    Action click;
    Action<ItemData> hover;

    void Awake() => button?.onClick.AddListener(() => click?.Invoke());

    public void Bind(ItemData data, int amount, string cost, bool available, Action onClick, Action<ItemData> onHover)
    {
        gameObject.SetActive(true);
        item = data; click = onClick; hover = onHover;
        if (icon != null) { icon.sprite = data != null ? data.icon : null; icon.enabled = icon.sprite != null; }
        ItemData.ShowSlotFill(rarityFill, data, data != null);
        if (itemName != null) itemName.text = data == null ? "" : data.HasRarityColour ? $"<color={data.RarityTextColour}>{data.DisplayName}</color>" : data.itemName;
        if (info != null) info.text = data != null ? data.ShortInfo() : "";
        if (count != null) count.text = amount > 1 ? $"x{amount}" : "";
        if (price != null) price.text = cost;
        if (button != null) button.interactable = available;
        if (group != null) group.alpha = available ? 1f : .5f;
    }

    public void Clear()
    {
        item = null; click = null;
        gameObject.SetActive(false);
    }

    // The item's tooltip (the inventory's) beside the mouse, besides the details panel.
    public void OnPointerEnter(PointerEventData eventData) { hover?.Invoke(item); ShowTooltip(null); }
    public void OnPointerExit(PointerEventData eventData) => HideTooltip();
    public void OnDeselect(BaseEventData eventData) => HideTooltip();
    void ShowTooltip(RectTransform at) { if (item != null && TooltipUI.HasInstance && !dragging) TooltipUI.Instance.Show(item, at); }
    static void HideTooltip() { if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }
    public void OnSelect(BaseEventData eventData) { hover?.Invoke(item); if (eventData is not PointerEventData) ShowTooltip((RectTransform)transform); }

    // The left button is the Button's own click; the right one buys too.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right) Buy();
    }

    void Buy() { if (button == null || button.interactable) click?.Invoke(); }

    bool dragging;
    public void OnBeginDrag(PointerEventData eventData)
    {
        HideTooltip();
        dragging = item != null && eventData.button == PointerEventData.InputButton.Left && ShopUI.HasInstance && ShopUI.Instance.BeginDrag(item, icon);
    }
    public void OnDrag(PointerEventData eventData) { }
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!dragging) return;
        dragging = false;
        if (ShopUI.Instance.EndDrag(eventData.position) == ShopUI.DropZone.Pack) Buy();
    }
}

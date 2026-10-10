using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One ware in the shop: icon, name, a short info line, count and price. Hover shows details; click buys.
public class ShopRowUI : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    [SerializeField] Image icon;
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
        if (itemName != null) itemName.text = data != null ? data.itemName : "";
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

    public void OnPointerEnter(PointerEventData eventData) => hover?.Invoke(item);
    public void OnSelect(BaseEventData eventData) => hover?.Invoke(item);
}

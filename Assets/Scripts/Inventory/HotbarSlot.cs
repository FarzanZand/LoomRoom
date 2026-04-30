using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class HotbarSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [SerializeField] public Image iconImage;
    [SerializeField] public Image background;
    [SerializeField] public Image selectionBorder;
    [SerializeField] public TextMeshProUGUI keyLabel;

    private static readonly Color EmptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    private static readonly Color FilledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);

    public int SlotIndex { get; private set; }
    private ItemData item;

    public void Init(int index)
    {
        SlotIndex = index;
        if (keyLabel != null) keyLabel.text = (index + 1).ToString();
        if (selectionBorder != null) selectionBorder.gameObject.SetActive(false);
        Clear();
    }

    public void SetItem(ItemData data)
    {
        item = data;
        iconImage.sprite  = data.icon;
        iconImage.enabled = data.icon != null;
        background.color  = FilledColor;
    }

    public void Clear()
    {
        item = null;
        iconImage.enabled = false;
        background.color  = EmptyColor;
    }

    public void SetSelected(bool selected)
    {
        if (selectionBorder != null) selectionBorder.gameObject.SetActive(selected);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (item != null) TooltipUI.Instance.Show(item);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Instance.Hide();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (item == null || eventData.button != PointerEventData.InputButton.Right) return;
        if (item.itemType != ItemType.Consumable) return;
        item.UseEffect();
        HotbarSystem.Instance.Remove(SlotIndex);
    }
}

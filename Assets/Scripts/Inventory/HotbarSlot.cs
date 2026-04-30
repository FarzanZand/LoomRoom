using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class HotbarSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] public Image iconImage;
    [SerializeField] public Image background;
    [SerializeField] public Image equippedHighlight;
    [SerializeField] public TextMeshProUGUI keyLabel;

    private static readonly Color EmptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    private static readonly Color FilledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);

    public int SlotIndex { get; private set; }
    private ItemData item;

    public void Init(int index)
    {
        SlotIndex = index;
        if (keyLabel != null) keyLabel.text = (index + 1).ToString();
        if (equippedHighlight != null) equippedHighlight.gameObject.SetActive(false);
        Clear();
    }

    public void SetEquipped(bool equipped)
    {
        if (equippedHighlight != null) equippedHighlight.gameObject.SetActive(equipped);
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

    public void Pulse()
    {
        StopAllCoroutines();
        StartCoroutine(PulseRoutine());
    }

    private System.Collections.IEnumerator PulseRoutine()
    {
        float duration = 0.07f;
        Vector3 big    = Vector3.one * 1.18f;

        float t = 0f;
        while (t < duration)
        {
            transform.localScale = Vector3.Lerp(Vector3.one, big, t / duration);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        t = 0f;
        while (t < duration)
        {
            transform.localScale = Vector3.Lerp(big, Vector3.one, t / duration);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        transform.localScale = Vector3.one;
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

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (item == null) return;
        var size = GetComponent<RectTransform>().rect.size;
        ItemDragHandler.Instance.BeginDrag(item, true, SlotIndex, item.icon, size);
    }

    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!ItemDragHandler.Instance.WasDropped)
            ItemDragHandler.Instance.EndDrag();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (!ItemDragHandler.Instance.IsDragging) return;

        var handler = ItemDragHandler.Instance;
        var dragged = handler.DraggedItem;
        int srcIdx = handler.SourceIndex;
        bool fromHotbar = handler.SourceIsHotbar;

        if (!IsHotbarCompatible(dragged)) return;

        if (fromHotbar)
        {
            HotbarSystem.Instance.Swap(srcIdx, SlotIndex);
        }
        else
        {
            var existingInHotbar = HotbarSystem.Instance.Get(SlotIndex);
            InventorySystem.Instance.Remove(srcIdx);
            if (existingInHotbar != null)
                InventorySystem.Instance.Insert(srcIdx, existingInHotbar);
            HotbarSystem.Instance.Set(SlotIndex, dragged);
        }

        handler.NotifyDropped();
        handler.EndDrag();
    }

    private static bool IsHotbarCompatible(ItemData data) => data.canBeEquipped;
}

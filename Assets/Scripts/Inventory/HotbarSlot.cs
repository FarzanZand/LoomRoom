using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class HotbarSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] public Image          iconImage;
    [SerializeField] public Image          background;
    [SerializeField] public Image          equippedHighlight;
    [SerializeField] public TextMeshProUGUI keyLabel;

    private static readonly Color EmptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    private static readonly Color FilledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);

    public int SlotIndex { get; private set; }
    private ItemData item;

    private TextMeshProUGUI stackCountLabel;

    public void Init(int index)
    {
        SlotIndex = index;
        if (keyLabel != null) keyLabel.text = (index + 1).ToString();
        if (equippedHighlight != null) equippedHighlight.gameObject.SetActive(false);
        CreateStackLabel();
        Clear();
    }

    private void CreateStackLabel()
    {
        var go = new GameObject("StackCount");
        go.transform.SetParent(transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin       = Vector2.zero;
        rt.anchorMax       = Vector2.one;
        rt.offsetMin       = Vector2.zero;
        rt.offsetMax       = new Vector2(-2f, -2f);
        go.AddComponent<CanvasRenderer>();
        stackCountLabel                = go.AddComponent<TextMeshProUGUI>();
        stackCountLabel.raycastTarget  = false;
        stackCountLabel.fontSize       = 11f;
        stackCountLabel.alignment = TextAlignmentOptions.BottomRight;
        stackCountLabel.color     = Color.white;
        stackCountLabel.fontStyle = FontStyles.Bold;
        go.SetActive(false);
    }

    public void SetEquipped(bool equipped)
    {
        if (equippedHighlight != null) equippedHighlight.gameObject.SetActive(equipped);
    }

    public void SetItem(ItemData data, int count = 1)
    {
        item = data;
        iconImage.sprite  = data.icon;
        iconImage.enabled = data.icon != null;
        background.color  = FilledColor;
        RefreshStackLabel(count);
    }

    public void Clear()
    {
        item = null;
        iconImage.enabled = false;
        background.color  = EmptyColor;
        if (stackCountLabel != null) stackCountLabel.gameObject.SetActive(false);
    }

    private void RefreshStackLabel(int count)
    {
        if (stackCountLabel == null) return;
        if (count > 1) { stackCountLabel.text = $"x{count}"; stackCountLabel.gameObject.SetActive(true); }
        else           stackCountLabel.gameObject.SetActive(false);
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
        while (t < duration) { transform.localScale = Vector3.Lerp(Vector3.one, big, t / duration); t += Time.unscaledDeltaTime; yield return null; }
        t = 0f;
        while (t < duration) { transform.localScale = Vector3.Lerp(big, Vector3.one, t / duration); t += Time.unscaledDeltaTime; yield return null; }
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
        ShowContextMenu();
    }

    private void ShowContextMenu()
    {
        var options = new List<(string, System.Action)>();

        if (item.canBeEquipped)
        {
            bool equipped = ItemHolder.Instance.GetHeldItem(item.equipSlot) == item;
            if (equipped)
                options.Add(("Unequip", () => ItemHolder.Instance.ClearSlot(item.equipSlot)));
            else
                options.Add(("Equip",   () => ItemHolder.Instance.HoldItem(item)));
        }

        if (item.itemType == ItemType.Consumable)
        {
            int idx = SlotIndex;
            options.Add(("Use", () =>
            {
                item.UseEffect();
                HotbarSystem.Instance.Consume(idx);
            }));
        }

        if (item.itemType != ItemType.Key)
        {
            int idx        = SlotIndex;
            var droppedItem = item;
            options.Add(("Drop", () =>
            {
                ContextMenuUI.DropItemToWorld(droppedItem);
                HotbarSystem.Instance.Remove(idx);
            }));
        }

        ContextMenuUI.Instance.Show(options, Input.mousePosition);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (item == null) return;
        if (item.canBeEquipped && ItemHolder.Instance.GetHeldItem(item.equipSlot) == item)
            ItemHolder.Instance.ClearSlot(item.equipSlot);
        int count = HotbarSystem.Instance.GetCount(SlotIndex);
        var size  = GetComponent<RectTransform>().rect.size;
        ItemDragHandler.Instance.BeginDrag(item, true, SlotIndex, count, item.icon, size);
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

        var  handler   = ItemDragHandler.Instance;
        var  dragged   = handler.DraggedItem;
        int  srcIdx    = handler.SourceIndex;
        int  srcCount  = handler.SourceCount;
        bool fromHotbar = handler.SourceIsHotbar;

        if (!IsHotbarCompatible(dragged)) return;

        if (fromHotbar)
        {
            HotbarSystem.Instance.Shift(srcIdx, SlotIndex);
        }
        else
        {
            // Inventory → Hotbar
            var existingInHotbar  = HotbarSystem.Instance.Get(SlotIndex);
            int existingHotbarCount = HotbarSystem.Instance.GetCount(SlotIndex);
            InventorySystem.Instance.Remove(srcIdx);
            if (existingInHotbar != null)
                InventorySystem.Instance.Insert(srcIdx, existingInHotbar, existingHotbarCount);
            HotbarSystem.Instance.Set(SlotIndex, dragged, srcCount);
        }

        handler.NotifyDropped();
        handler.EndDrag();
    }

    private static bool IsHotbarCompatible(ItemData data) => data.canBeEquipped;
}

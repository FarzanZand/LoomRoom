using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InventorySlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] public Image iconImage;
    [SerializeField] public Image background;

    private static readonly Color EmptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    private static readonly Color FilledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);

    public int SlotIndex { get; private set; }
    private ItemData item;

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
    }

    public void Init(int index)
    {
        SlotIndex = index;
        Clear();
    }

    public void SetItem(ItemData data)
    {
        item = data;
        iconImage.sprite = data.icon;
        iconImage.enabled = data.icon != null;
        background.color = FilledColor;
    }

    public void Clear()
    {
        item = null;
        iconImage.enabled = false;
        background.color = EmptyColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (item == null) return;
        if (eventData.button == PointerEventData.InputButton.Right)
            OnRightClick();
    }

    public void OnClick()
    {
        if (item == null) return;
        Debug.Log($"[Inventory] Clicked: {item.itemName} | canBeEquipped: {item.canBeEquipped}");
        if (item.canBeEquipped)
            ItemHolder.Instance.HoldItem(item);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (item != null) TooltipUI.Instance.Show(item);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Instance.Hide();
    }

    void OnRightClick()
    {
        Debug.Log($"[Inventory] Right-clicked: {item.itemName}");
        if (item.itemType != ItemType.Consumable) return;
        item.UseEffect();
        InventorySystem.Instance.Remove(SlotIndex);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (item == null) return;
        var size = GetComponent<RectTransform>().rect.size;
        ItemDragHandler.Instance.BeginDrag(item, false, SlotIndex, item.icon, size);
    }

    public void OnDrag(PointerEventData eventData) { }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!ItemDragHandler.Instance.WasDropped)
            ItemDragHandler.Instance.EndDrag();
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (!ItemDragHandler.Instance.IsDragging || !ItemDragHandler.Instance.SourceIsHotbar) return;

        var handler = ItemDragHandler.Instance;
        var hotbarItem = handler.DraggedItem;
        int srcHotbarIdx = handler.SourceIndex;

        var items = InventorySystem.Instance.Items;
        var existingItem = SlotIndex < items.Count ? items[SlotIndex] : null;

        if (existingItem != null && IsHotbarCompatible(existingItem))
        {
            // Swap: equippable inventory item goes to hotbar, hotbar item takes its slot
            InventorySystem.Instance.Insert(SlotIndex, hotbarItem);
            HotbarSystem.Instance.Set(srcHotbarIdx, existingItem);
        }
        else if (existingItem == null)
        {
            // Empty slot: place hotbar item directly here
            InventorySystem.Instance.Insert(SlotIndex, hotbarItem);
            HotbarSystem.Instance.Remove(srcHotbarIdx);
        }
        else
        {
            // Slot occupied by non-equippable item — refuse the drop
            return;
        }

        handler.NotifyDropped();
        handler.EndDrag();
    }

    private static bool IsHotbarCompatible(ItemData data) => data.canBeEquipped;
}

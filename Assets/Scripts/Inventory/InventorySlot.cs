using System.Collections.Generic;
using TMPro;
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

    private TextMeshProUGUI stackCountLabel;

    void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClick);
        CreateOverlayElements();
    }

    private void CreateOverlayElements()
    {
        // Stack count label (bottom-right corner).
        var countGo = new GameObject("StackCount");
        countGo.transform.SetParent(transform, false);
        var crt = countGo.AddComponent<RectTransform>();
        crt.anchorMin       = Vector2.zero;
        crt.anchorMax       = Vector2.one;
        crt.offsetMin       = Vector2.zero;
        crt.offsetMax       = new Vector2(-2f, -2f);
        countGo.AddComponent<CanvasRenderer>();
        stackCountLabel               = countGo.AddComponent<TextMeshProUGUI>();
        stackCountLabel.raycastTarget = false;
        stackCountLabel.fontSize      = 11f;
        stackCountLabel.alignment   = TextAlignmentOptions.BottomRight;
        stackCountLabel.color       = Color.white;
        stackCountLabel.fontStyle   = FontStyles.Bold;
        countGo.SetActive(false);

    }

    public void Init(int index)
    {
        SlotIndex = index;
        Clear();
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
        if (count > 1)
        {
            stackCountLabel.text = $"x{count}";
            stackCountLabel.gameObject.SetActive(true);
        }
        else
        {
            stackCountLabel.gameObject.SetActive(false);
        }
    }

public void OnPointerClick(PointerEventData eventData)
    {
        if (item == null) return;
        if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging)
            OnLeftClick();
        else if (eventData.button == PointerEventData.InputButton.Right)
            ShowContextMenu();
    }

    private void OnLeftClick()
    {
        if (item.canBeEquipped)
            ItemHolder.Instance.HoldItem(item);
    }

    public void OnClick() { } // kept for Button component; logic is in OnPointerClick

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (item != null) TooltipUI.Instance.Show(item);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipUI.Instance.Hide();
    }

    private void ShowContextMenu()
    {
        var options = new List<(string, System.Action)>();

        if (item.canBeEquipped)
        {
            bool heldInHand = ItemHolder.Instance.GetHeldItem(item.equipSlot) == item;
            if (heldInHand)
            {
                options.Add(("Unequip", () => ItemHolder.Instance.ClearSlot(item.equipSlot)));
            }
            else
            {
                int  idx       = SlotIndex;
                var  equipItem = item;
                options.Add(("Add to Hotbar", () =>
                {
                    if (!HotbarSystem.Instance.TryAdd(equipItem))
                    {
                        ContextMenuUI.Instance.ShowNotification("Hotbar Full!");
                        return;
                    }
                    InventorySystem.Instance.Remove(idx);
                }));
            }
        }

        if (item.itemType == ItemType.Consumable)
        {
            int idx = SlotIndex;
            options.Add(("Use", () =>
            {
                item.UseEffect();
                InventorySystem.Instance.Consume(idx);
            }));
        }

        if (item.itemType != ItemType.Key)
        {
            int idx       = SlotIndex;
            var droppedItem = item;
            options.Add(("Drop", () =>
            {
                ContextMenuUI.DropItemToWorld(droppedItem);
                InventorySystem.Instance.Remove(idx);
            }));
        }

        ContextMenuUI.Instance.Show(options, Input.mousePosition);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (item == null) return;
        int count = InventorySystem.Instance.GetCount(SlotIndex);
        var size  = GetComponent<RectTransform>().rect.size;
        ItemDragHandler.Instance.BeginDrag(item, false, SlotIndex, count, item.icon, size);
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

        var  handler      = ItemDragHandler.Instance;
        var  draggedItem  = handler.DraggedItem;
        int  draggedCount = handler.SourceCount;
        bool fromHotbar   = handler.SourceIsHotbar;
        int  srcIdx       = handler.SourceIndex;

        var existingItem  = InventorySystem.Instance.Items[SlotIndex];
        int existingCount = InventorySystem.Instance.GetCount(SlotIndex);

        if (fromHotbar)
        {
            // ── Hotbar → Inventory ──────────────────────────────────────────
            if (existingItem != null && existingItem.canBeEquipped)
            {
                // Swap: equippable inv item goes back to hotbar slot.
                InventorySystem.Instance.Insert(SlotIndex, draggedItem, draggedCount);
                HotbarSystem.Instance.Set(srcIdx, existingItem, existingCount);
            }
            else if (existingItem == null)
            {
                InventorySystem.Instance.Insert(SlotIndex, draggedItem, draggedCount);
                HotbarSystem.Instance.Remove(srcIdx);
            }
            else
            {
                return; // non-equippable item in slot — refuse
            }
        }
        else
        {
            // ── Inventory → Inventory ────────────────────────────────────────
            if (srcIdx == SlotIndex) { handler.NotifyDropped(); handler.EndDrag(); return; }

            if (existingItem == null)
            {
                InventorySystem.Instance.Insert(SlotIndex, draggedItem, draggedCount);
                InventorySystem.Instance.Remove(srcIdx);
            }
            else
            {
                // Swap the two slots.
                InventorySystem.Instance.Insert(SlotIndex, draggedItem, draggedCount);
                InventorySystem.Instance.Insert(srcIdx,    existingItem, existingCount);
            }
        }

        handler.NotifyDropped();
        handler.EndDrag();
    }
}

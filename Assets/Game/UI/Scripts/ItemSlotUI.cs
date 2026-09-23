using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// One cell of an inventory or hotbar grid. Bound to (container, index) by its owner
// UI; all visuals are authored in the prefab/scene.
public class ItemSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] Image           iconImage;
    [SerializeField] Image           background;
    [SerializeField] Image           equippedHighlight;
    [SerializeField] TextMeshProUGUI keyLabel;
    [SerializeField] TextMeshProUGUI stackLabel;

    [SerializeField] Color emptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    [SerializeField] Color filledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);

    public Inventory Container { get; private set; }
    public int       Index     { get; private set; }
    public ItemData  Item      => Container != null ? Container.ItemAt(Index) : null;

    Player Owner => Container != null ? Container.GetComponentInParent<Player>() : null;

    bool dungeonStyled, savedStyle;
    Color originalEmpty, originalFilled, originalHighlight;
    TMP_FontAsset originalKeyFont, originalStackFont;
    float originalKeySize, originalStackSize;
    Color originalKeyColor, originalStackColor;
    [Header("Dungeon appearance")]
    [SerializeField] Image dungeonFrame;
    [SerializeField] Color dungeonEmptyColor=new Color(.035f,.045f,.045f,.96f);
    [SerializeField] Color dungeonFilledColor=new Color(.09f,.11f,.105f,.98f);
    [SerializeField] Color dungeonEquippedColor=new Color(1,.72f,.22f,.32f);
    [SerializeField] Color dungeonKeyColor=new Color(.9f,.84f,.66f);
    [SerializeField] TMP_FontAsset dungeonFont;
    [SerializeField] float dungeonKeyFontSize=22,dungeonStackFontSize=22;

    public void SetDungeonStyle(bool enabled, TMP_FontAsset font, Sprite frame)
    {
        if (dungeonStyled == enabled) return;
        if (!savedStyle)
        {
            savedStyle=true; originalEmpty=emptyColor;originalFilled=filledColor;
            if(equippedHighlight!=null)originalHighlight=equippedHighlight.color;
            if(keyLabel!=null){originalKeyFont=keyLabel.font;originalKeySize=keyLabel.fontSize;originalKeyColor=keyLabel.color;}
            if(stackLabel!=null){originalStackFont=stackLabel.font;originalStackSize=stackLabel.fontSize;originalStackColor=stackLabel.color;}
        }
        dungeonStyled=enabled;
        emptyColor=enabled ? dungeonEmptyColor:originalEmpty;
        filledColor=enabled ? dungeonFilledColor:originalFilled;
        if(equippedHighlight!=null)equippedHighlight.color=enabled ? dungeonEquippedColor:originalHighlight;
        if(keyLabel!=null){keyLabel.font=enabled && (dungeonFont!=null || font!=null) ? (dungeonFont!=null?dungeonFont:font):originalKeyFont;keyLabel.fontSize=enabled ? dungeonKeyFontSize:originalKeySize;keyLabel.color=enabled ? dungeonKeyColor:originalKeyColor;}
        if(stackLabel!=null){stackLabel.font=enabled && (dungeonFont!=null || font!=null) ? (dungeonFont!=null?dungeonFont:font):originalStackFont;stackLabel.fontSize=enabled ? dungeonStackFontSize:originalStackSize;stackLabel.color=enabled ? Color.white:originalStackColor;}
        if(dungeonFrame!=null)dungeonFrame.enabled=enabled;
        Refresh();
    }

    public void Bind(Inventory container, int index)
    {
        Container = container;
        Index     = index;
        if (keyLabel != null && container != null && container.role == InventoryRole.Hotbar)
            keyLabel.text = (index + 1).ToString();
        Refresh();
    }

    public void Refresh()
    {
        var item  = Item;
        int count = Container != null ? Container.CountAt(Index) : 0;

        if (iconImage != null)
        {
            iconImage.sprite  = item != null ? item.icon : null;
            iconImage.enabled = item != null && item.icon != null;
        }
        if (background != null) background.color = item != null ? filledColor : emptyColor;
        if (stackLabel != null)
        {
            stackLabel.text = count > 1 ? $"x{count}" : "";
            stackLabel.gameObject.SetActive(count > 1);
        }
        if (equippedHighlight != null)
        {
            var eq = Owner != null ? Owner.Equipment : null;
            equippedHighlight.gameObject.SetActive(item != null && eq != null && eq.IsEquipped(item));
        }
        if(dungeonStyled && dungeonFrame!=null)
        {
            bool equipped=item!=null && Owner?.Equipment!=null && Owner.Equipment.IsEquipped(item);
            dungeonFrame.color=equipped ? new Color(1,.83f,.35f):new Color(.5f,.57f,.55f);
        }
    }

    public void Pulse()
    {
        if(TryGetComponent<UIInteractionFeedback>(out var feedback)){feedback.Pulse();return;}
        StopAllCoroutines();
        StartCoroutine(PulseRoutine());
    }

    IEnumerator PulseRoutine()
    {
        float duration = 0.07f;
        Vector3 big = Vector3.one * 1.06f;
        float t = 0f;
        while (t < duration) { transform.localScale = Vector3.Lerp(Vector3.one, big, t / duration); t += Time.unscaledDeltaTime; yield return null; }
        t = 0f;
        while (t < duration) { transform.localScale = Vector3.Lerp(big, Vector3.one, t / duration); t += Time.unscaledDeltaTime; yield return null; }
        transform.localScale = Vector3.one;
    }

    // ── Pointer ───────────────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData e) { if (Item != null && TooltipUI.HasInstance) TooltipUI.Instance.Show(Item); }
    public void OnPointerExit(PointerEventData e)  { if (TooltipUI.HasInstance) TooltipUI.Instance.Hide(); }

    public void OnPointerClick(PointerEventData e)
    {
        if (Item == null) return;
        if (e.button == PointerEventData.InputButton.Right) ShowContextMenu();
        else if (e.button == PointerEventData.InputButton.Left && !e.dragging && e.clickCount == 2)
            ActivateItem();
    }

    void ActivateItem()
    {
        var item = Item;
        var owner = Owner;
        if (item == null || owner == null || !owner.IsAlive) return;
        if (ContextMenuUI.HasInstance) ContextMenuUI.Instance.Hide();
        var eq = owner.Equipment;
        if (item.IsConsumable)
        {
            if (!InventoryManager.HasInstance) return;
            if (item.canBeEquipped && eq != null && eq.Get(item.equipSlot) == item)
                InventoryManager.Instance.UseHeld(owner, item.equipSlot);
            else InventoryManager.Instance.Use(Container, Index);
        }
        else if (eq != null && eq.CanEquip(item) && !eq.IsEquipped(item)) eq.Equip(item);
    }

    void ShowContextMenu()
    {
        var item  = Item;
        var owner = Owner;
        if (item == null || owner == null || !ContextMenuUI.HasInstance) return;

        var options = new List<(string, System.Action)>();
        var eq = owner.Equipment;
        var container = Container;
        int index = Index;

        if (eq != null && eq.CanEquip(item))
        {
            if (eq.IsEquipped(item)) options.Add(("Unequip", () => eq.Unequip(item.equipSlot)));
            else                     options.Add(("Equip",   () => eq.Equip(item)));
        }

        if (item.IsConsumable && !item.canBeEquipped)
            options.Add(("Use", () => InventoryManager.Instance.Use(container, index)));

        if (container.role == InventoryRole.Bag && owner.Hotbar != null && owner.Hotbar.Accepts(item))
        {
            options.Add(("Add to Hotbar", () =>
            {
                int empty = owner.Hotbar.FirstEmpty();
                if (empty < 0) { NotificationUI.Show("Hotbar full"); return; }
                Inventory.Move(container, index, owner.Hotbar, empty);
            }));
        }
        else if (container.role == InventoryRole.Hotbar && owner.Bag != null)
        {
            options.Add(("Move to Bag", () =>
            {
                int empty = owner.Bag.FirstEmpty();
                if (empty < 0) { NotificationUI.Show("Inventory full"); return; }
                Inventory.Move(container, index, owner.Bag, empty);
            }));
        }

        if (item.itemType != ItemType.Key)
        {
            options.Add(("Drop", () =>
            {
                var stack = container[index];
                if (stack == null) return;
                if (eq != null && eq.IsEquipped(item) && container.TotalCount(item) <= 1) eq.Unequip(item.equipSlot);
                InventoryManager.Instance.DropFromPlayer(item, owner);
                container.Consume(index);
            }));
        }

        ContextMenuUI.Instance.Show(options, Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
    }

    // ── Drag & drop ───────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e)
    {
        if (Item == null || !ItemDragHandler.HasInstance) return;
        ItemDragHandler.Instance.Begin(this, GetComponent<RectTransform>().rect.size);
    }

    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e)
    {
        if (ItemDragHandler.HasInstance && !ItemDragHandler.Instance.WasDropped) ItemDragHandler.Instance.End();
    }

    public void OnDrop(PointerEventData e)
    {
        if (!ItemDragHandler.HasInstance || !ItemDragHandler.Instance.IsDragging) return;
        var src = ItemDragHandler.Instance.Source;
        if (src != null && src != this && Container != null)
        {
            var eq = Owner?.Equipment;
            var moving = src.Item;
            // Equipment reconciles ownership after the transfer; hands require a hotbar slot.
            Inventory.Move(src.Container, src.Index, Container, Index);
            if (eq != null && moving != null) src.Refresh();
        }
        ItemDragHandler.Instance.NotifyDropped();
        ItemDragHandler.Instance.End();
    }
}

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
    Image dungeonFrame;

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
        emptyColor=enabled ? new Color(.035f,.045f,.045f,.96f):originalEmpty;
        filledColor=enabled ? new Color(.09f,.11f,.105f,.98f):originalFilled;
        if(equippedHighlight!=null)equippedHighlight.color=enabled ? new Color(1,.72f,.22f,.32f):originalHighlight;
        if(keyLabel!=null){keyLabel.font=enabled && font!=null ? font:originalKeyFont;keyLabel.fontSize=enabled ? 22:originalKeySize;keyLabel.color=enabled ? new Color(.9f,.84f,.66f):originalKeyColor;}
        if(stackLabel!=null){stackLabel.font=enabled && font!=null ? font:originalStackFont;stackLabel.fontSize=enabled ? 22:originalStackSize;stackLabel.color=enabled ? Color.white:originalStackColor;}
        if(enabled && frame!=null && dungeonFrame==null)
        {
            var go=new GameObject("Dungeon pixel frame",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);
            dungeonFrame=go.GetComponent<Image>();dungeonFrame.sprite=frame;dungeonFrame.type=Image.Type.Sliced;dungeonFrame.raycastTarget=false;
            var r=dungeonFrame.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        }
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
        else if (e.button == PointerEventData.InputButton.Left && !e.dragging && Container.role == InventoryRole.Bag)
            ToggleEquip();
    }

    void ToggleEquip()
    {
        var item = Item;
        var eq = Owner?.Equipment;
        if (item == null || eq == null || !eq.CanEquip(item)) return;
        if (eq.IsEquipped(item)) eq.Unequip(item.equipSlot);
        else eq.Equip(item);
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

        if (item.IsConsumable)
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
            // Equipped items stay equipped when moved between containers.
            Inventory.Move(src.Container, src.Index, Container, Index);
            if (eq != null && moving != null) src.Refresh();
        }
        ItemDragHandler.Instance.NotifyDropped();
        ItemDragHandler.Instance.End();
    }
}

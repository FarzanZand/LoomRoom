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
    [Tooltip("Shown behind the icon in the item's rarity colour.")]
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("honedFill")] Image rarityFill;
    [SerializeField] TextMeshProUGUI keyLabel;
    [SerializeField] TextMeshProUGUI stackLabel;

    [SerializeField] Color emptyColor  = new Color(0.12f, 0.12f, 0.12f, 0.85f);
    [SerializeField] Color filledColor = new Color(0.22f, 0.22f, 0.22f, 0.95f);
    [Tooltip("Keep the prefab's appearance; only item contents and equipped state change at runtime.")]
    [SerializeField] bool useAuthoredAppearance;
    [SerializeField] Color frameColor = new Color(.5f,.57f,.55f);
    [SerializeField] Color equippedFrameColor = new Color(1,.83f,.35f);
    [Tooltip("Authored appearance: tint of a cell an item covers (blue-grey).")]

    public Inventory Container { get; private set; }
    public int       Index     { get; private set; }
    // In a bag grid every cell an item covers answers for it: its anchor (top-left) holds the stack.
    public int       Anchor    => Container == null ? -1 : Container.IsGrid ? Container.OwnerAt(Index) : Index;
    public ItemData  Item      => Container != null && Anchor >= 0 ? Container.ItemAt(Anchor) : null;
    // A grid draws its items across their cells (ItemGridUI); the cell itself only shows that it is taken.
    bool GridCell => Container != null && Container.IsGrid;
    // Set by ItemGridUI while an item is dragged over the grid: where it would go (green) or not (red).
    public Color? PreviewTint { get; set; }

    Player Owner => Container != null ? Container.GetComponentInParent<Player>() : null;

    bool dungeonStyled, savedStyle;
    Color originalEmpty, originalFilled, originalHighlight;
    TMP_FontAsset originalKeyFont, originalStackFont;
    float originalKeySize, originalStackSize;
    Color originalKeyColor, originalStackColor;
    [Header("Dungeon appearance")]
    [SerializeField] Image dungeonFrame;
    [SerializeField] Color dungeonEmptyColor=DungeonEmpty;
    [SerializeField] Color dungeonFilledColor=DungeonFilled;
    // Default dungeon cell colours, shared with the shop's pack grid (ShopSlotUI).
    public static readonly Color DungeonEmpty=new Color(.035f,.045f,.045f,.96f);
    public static readonly Color DungeonFilled=new Color(.09f,.11f,.105f,.98f);
    [SerializeField] Color dungeonEquippedColor=new Color(1,.72f,.22f,.32f);
    [SerializeField] Color dungeonKeyColor=new Color(.9f,.84f,.66f);
    [SerializeField] TMP_FontAsset dungeonFont;
    [SerializeField] float dungeonKeyFontSize=22,dungeonStackFontSize=22;

    public void SetDungeonStyle(bool enabled, TMP_FontAsset font, Sprite frame)
    {
        if (useAuthoredAppearance) return;
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

    // A HUD slot (its background switches with the HUD style) while the HUD is Rugged.
    bool Rugged => background != null && background.TryGetComponent<HudStyleImage>(out _) && UIManager.CurrentHudStyle == HudStyle.Rugged;
    UIManager styleSource;
    void OnEnable() { if (UIManager.HasInstance) { styleSource = UIManager.Instance; styleSource.Changed += Refresh; } }
    void OnDisable() { if (styleSource != null) styleSource.Changed -= Refresh; styleSource = null; }

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
        int count = Container != null && Anchor >= 0 ? Container.CountAt(Anchor) : 0;
        if (GridCell) count = 0;

        if (iconImage != null)
        {
            iconImage.sprite  = item != null ? item.icon : null;
            iconImage.enabled = item != null && item.icon != null && !GridCell;
        }
        // The Rugged HUD (UIManager.hudStyle) draws the slot as the inventory's grey cell: its own colours, not the tint.
        bool rugged = Rugged;
        if (background != null && !useAuthoredAppearance) background.color = PreviewTint ?? (rugged ? Color.white : item != null ? filledColor : emptyColor);
        // Authored cells (the bag grid) keep their sprite as it is, taken or not (the item's rarity floor marks it);
        // only the drag footprint tints it.
        else if (background != null && GridCell) background.color = PreviewTint ?? Color.white;
        // The hotbar shows no rarity floor (the bag grid and the equipment slots do): its slots stay as they are.
        if (rarityFill != null) ItemData.ShowSlotFill(rarityFill, item, false);
        if (stackLabel != null)
        {
            stackLabel.text = count > 1 ? $"x{count}" : "";
            stackLabel.gameObject.SetActive(count > 1);
        }
        if (equippedHighlight != null)
        {
            var eq = Owner != null ? Owner.Equipment : null;
            equippedHighlight.gameObject.SetActive(!rugged && item != null && !GridCell && HeldSlot(eq, Container, Index).HasValue);
        }
        if(!useAuthoredAppearance && dungeonStyled && dungeonFrame!=null)
        {
            bool equipped=item!=null && HeldSlot(Owner?.Equipment, Container, Index).HasValue;
            dungeonFrame.color=rugged ? Color.clear : equipped ? equippedFrameColor:frameColor;
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
        if (e.button == PointerEventData.InputButton.Left && !e.dragging && Keyboard.current != null && Keyboard.current.shiftKey.isPressed) { QuickMove(); return; }
        // Right-click: a consumable is used at once, gear goes on or off, anything else gets the actions menu.
        if (e.button == PointerEventData.InputButton.Right) { if (Item.IsConsumable) ActivateItem(); else if (!ToggleEquip()) ShowContextMenu(); }
        else if (e.button == PointerEventData.InputButton.Left && !e.dragging && e.clickCount == 2)
            ActivateItem();
    }

    // Shift-click: from the bag to the first free hotbar slot that takes it, or from the hotbar back into the bag.
    void QuickMove()
    {
        var owner = Owner;
        var item = Item;
        if (owner == null || item == null || Container == null) return;
        if (ContextMenuUI.HasInstance) ContextMenuUI.Instance.Hide();
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        bool fromHotbar = Container == owner.Hotbar;
        var to = fromHotbar ? owner.Bag : owner.Hotbar;
        if (to == null) return;
        if (fromHotbar && HeldSlot(owner.Equipment, Container, Anchor).HasValue) { Refuse("Put it away first"); return; }
        if (!to.Accepts(item)) { Refuse(fromHotbar ? null : "That doesn't go on the hotbar"); return; }
        int spot = to.IsGrid ? to.FindSpot(item) : to.FirstEmpty();
        if (spot < 0 || !Inventory.Move(Container, Anchor, to, spot)) { Refuse(fromHotbar ? "Inventory full" : "The hotbar is full"); return; }
        var s = UIFeedbackSettings.Shared; s?.Play(s.equipKey);
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
            else InventoryManager.Instance.Use(Container, Anchor);
        }
        else if (eq != null && eq.CanEquip(item) && !HeldSlot(eq, Container, Anchor).HasValue) eq.Equip(item);
    }

    // Right-click on gear: puts it on (a worn item from the bag replaces what is worn), or takes it off when this copy
    // is the one in hand. False for anything that can't be equipped, which gets the actions menu instead.
    bool ToggleEquip()
    {
        var item = Item;
        var owner = Owner;
        var eq = owner != null ? owner.Equipment : null;
        if (item == null || eq == null || !owner.IsAlive || !item.canBeEquipped || item.IsConsumable) return false;
        if (ContextMenuUI.HasInstance) ContextMenuUI.Instance.Hide();
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        var held = HeldSlot(eq, Container, Anchor);
        if (held.HasValue) eq.TryUnequip(held.Value);
        else if (!eq.CanEquip(item) || !eq.Equip(item)) Refuse(null);
        return true;
    }

    void ShowContextMenu()
    {
        var item  = Item;
        var owner = Owner;
        if (item == null || owner == null || !ContextMenuUI.HasInstance) return;

        var options = new List<(string, System.Action)>();
        var eq = owner.Equipment;
        var container = Container;
        int index = Anchor;
        // The slot may change while the menu is open; act only if it still holds this item.
        bool Unchanged() => container[index]?.item == item;

        if (eq != null && eq.CanEquip(item))
        {
            // Only the copy in hand is equipped; a worn item has left the bag, so any copy here is a spare.
            var held = HeldSlot(eq, container, index);
            if (held.HasValue) options.Add(("Unequip", () => { if (Unchanged()) eq.TryUnequip(held.Value); }));
            else               options.Add(("Equip",   () => { if (Unchanged()) eq.Equip(item); }));
        }

        if (item.IsConsumable && !item.canBeEquipped)
            options.Add(("Use", () => { if (Unchanged() && InventoryManager.HasInstance) InventoryManager.Instance.Use(container, index); }));

        if (container.role == InventoryRole.Bag && owner.Hotbar != null && owner.Hotbar.Accepts(item))
        {
            options.Add(("Add to Hotbar", () =>
            {
                if (!Unchanged()) return;
                int empty = owner.Hotbar.FirstEmpty();
                if (empty < 0) { NotificationUI.Show("Hotbar full"); return; }
                Inventory.Move(container, index, owner.Hotbar, empty);
            }));
        }
        else if (container.role == InventoryRole.Hotbar && owner.Bag != null && !HeldSlot(eq, container, index).HasValue)
        {
            options.Add(("Move to Bag", () =>
            {
                if (!Unchanged()) return;
                int empty = owner.Bag.FindSpot(item);
                if (empty < 0) { NotificationUI.Show("Inventory full"); return; }
                Inventory.Move(container, index, owner.Bag, empty);
            }));
        }

        if (item.itemType != ItemType.Key)
        {
            options.Add(("Drop", () =>
            {
                if (!Unchanged() || !InventoryManager.HasInstance) return;
                // The copy in hand stays in hand (put it away first); spare copies drop freely.
                if (HeldSlot(eq, container, index).HasValue && container.CountAt(index) <= 1) { Refuse(PutAwayFirst); return; }
                if (InventoryManager.Instance.DropFromPlayer(item, owner) == null) return;
                container.Consume(index);
            }));
        }

        ContextMenuUI.Instance.Show(options, Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
    }

    // ── Drag & drop ───────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e)
    {
        if (Item == null || !ItemDragHandler.HasInstance) return;
        // A grid item is held where it was grabbed: the cell under the pointer keeps the same place within it.
        var cell = GetComponent<RectTransform>().rect.size;
        var size = Container.SizeOf(Item);
        var grab = Container.IsGrid ? Container.CellOf(Index) - Container.CellOf(Anchor) : Vector2Int.zero;
        ItemDragHandler.Instance.Begin(this, new Vector2(cell.x * size.x, cell.y * size.y), Anchor, grab);
    }

    public void OnDrag(PointerEventData e) { }

    public void OnEndDrag(PointerEventData e) => EndDrag(e);

    // Released with nothing caught it: over the world (no UI under the pointer) the item is dropped on the floor.
    public static void EndDrag(PointerEventData e)
    {
        if (!ItemDragHandler.HasInstance) return;
        var drag = ItemDragHandler.Instance;
        if (drag.IsDragging && e.pointerCurrentRaycast.gameObject == null) DropToFloor(drag);
        drag.End();
    }

    static void DropToFloor(ItemDragHandler drag)
    {
        var item = drag.Item;
        var owner = drag.SourceContainer != null ? drag.SourceContainer.GetComponentInParent<Player>() : InventoryManager.HasInstance ? InventoryManager.Instance.ActivePlayer : null;
        if (item == null || owner == null || !InventoryManager.HasInstance || item.itemType == ItemType.Key) return;
        var eq = owner.Equipment;
        if (drag.WornSlot.HasValue)
        {
            if (eq == null || eq.Get(drag.WornSlot.Value) != item) return;
            eq.UnequipToFloor(drag.WornSlot.Value);
            return;
        }
        var container = drag.SourceContainer;
        if (container == null || container.ItemAt(drag.SourceIndex) != item) return;
        if (HeldSlot(eq, container, drag.SourceIndex).HasValue) { Refuse(PutAwayFirst); return; }
        int count = container.CountAt(drag.SourceIndex);
        for (int i = 0; i < count; i++)
            if (InventoryManager.Instance.DropFromPlayer(item, owner) != null) container.Consume(drag.SourceIndex);
    }

    // Number keys don't work while the inventory is open, so the doll is the way.
    const string PutAwayFirst = "Put it away first: click it under Equipment";

    // The hand holding the item in this slot: only the hotbar slot the held item sits in counts, never a spare copy
    // of the same item elsewhere in the hotbar or bag.
    public static EquipmentSlot? HeldSlot(Equipment eq, Inventory container, int index)
    {
        if (eq == null || container == null || container.role != InventoryRole.Hotbar || index < 0) return null;
        var item = container.ItemAt(index);
        if (item == null) return null;
        if (eq.Get(EquipmentSlot.RightHand) == item && eq.HeldIndex(EquipmentSlot.RightHand) == index) return EquipmentSlot.RightHand;
        if (eq.Get(EquipmentSlot.LeftHand) == item && eq.HeldIndex(EquipmentSlot.LeftHand) == index) return EquipmentSlot.LeftHand;
        return null;
    }

    static void Refuse(string reason)
    {
        var s = UIFeedbackSettings.Shared; s?.Play(s.invalidKey);
        if (!string.IsNullOrEmpty(reason)) NotificationUI.Show(reason);
    }

    // The slot the dragged item's top-left would take when dropped on cell `index` of `dst`.
    static int TargetFor(ItemDragHandler drag, Inventory dst, int index)
    {
        if (!dst.IsGrid) return index;
        var grab = drag.SourceContainer == dst ? drag.GrabOffset : Vector2Int.zero;
        var size = dst.SizeOf(drag.Item);
        var cell = dst.CellOf(index) - grab;
        cell.x = Mathf.Clamp(cell.x, 0, dst.GridWidth - size.x);
        cell.y = Mathf.Clamp(cell.y, 0, dst.GridHeight - size.y);
        return cell.y * dst.GridWidth + cell.x;
    }

    // Held items stay in the hotbar: one leaves it only by swapping places with another item for the same
    // hand, which is then taken in hand instead (Equipment). Worn items come off the doll onto a free spot
    // or swap with an item for the same slot.
    public static bool Allowed(ItemDragHandler drag, Inventory dst, int to, out string reason)
    {
        reason = null;
        var item = drag.Item;
        var owner = dst != null ? dst.GetComponentInParent<Player>() : null;
        var eq = owner != null ? owner.Equipment : null;
        if (item == null || dst == null || eq == null) return true;
        var src = drag.SourceContainer;
        int ignore = src == dst ? drag.SourceIndex : -1;
        if (drag.WornSlot.HasValue)
        {
            if (!dst.Accepts(item)) return false;
            var hit = dst.Overlaps(item, to);
            return hit.Count == 0 ? dst.Fits(item, to) : hit.Count == 1 && eq.Fits(dst.ItemAt(hit[0]), drag.WornSlot.Value);
        }
        if (src == null || src == dst) return true;
        if (HeldSlot(eq, src, drag.SourceIndex) is EquipmentSlot hand)
        {
            // Only a swap for another item of the same hand; a stack of the same thing would merge it out of the hotbar.
            var hit = dst.Overlaps(item, to, ignore);
            if (hit.Count == 1 && dst.ItemAt(hit[0]) != item && eq.Fits(dst.ItemAt(hit[0]), hand)) return true;
            reason = PutAwayFirst;
            return false;
        }
        if (HeldSlot(eq, dst, to) is EquipmentSlot held && (!eq.Fits(item, held) || item == dst.ItemAt(to)))
        {
            reason = "That one is in your hand";
            return false;
        }
        return true;
    }

    public void OnDrop(PointerEventData e)
    {
        if (!ItemDragHandler.HasInstance || !ItemDragHandler.Instance.IsDragging) return;
        var drag = ItemDragHandler.Instance;
        // Equipment reconciles ownership after the transfer; hands require a hotbar slot.
        if (drag.Item != null && Container != null)
        {
            int to = TargetFor(drag, Container, Index);
            if (!Allowed(drag, Container, to, out string reason)) Refuse(reason);
            else if (drag.WornSlot.HasValue)
            {
                var eq = Owner != null ? Owner.Equipment : null;
                if (eq == null || !eq.UnequipInto(drag.WornSlot.Value, Container, to)) Refuse(null);
            }
            else if (drag.SourceContainer != null && !(drag.SourceContainer == Container && drag.SourceIndex == to) &&
                     !Inventory.Move(drag.SourceContainer, drag.SourceIndex, Container, to))
                Refuse(null);
        }
        drag.End();
    }
}

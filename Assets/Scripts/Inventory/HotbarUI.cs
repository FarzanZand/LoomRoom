using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using MFPC;

public class HotbarUI : MonoBehaviour
{
    [SerializeField] private List<HotbarSlot> slots;

    private PlayerInputActions inputActions;

    void Awake()
    {
        inputActions = new PlayerInputActions();
        inputActions.Enable();
        inputActions.Player.Hotbar1.performed += _ => SelectSlot(0);
        inputActions.Player.Hotbar2.performed += _ => SelectSlot(1);
        inputActions.Player.Hotbar3.performed += _ => SelectSlot(2);
        inputActions.Player.Hotbar4.performed += _ => SelectSlot(3);
        inputActions.Player.Hotbar5.performed += _ => SelectSlot(4);
        inputActions.Player.Hotbar6.performed += _ => SelectSlot(5);
        inputActions.Player.SecondaryAction.performed += _ => TryEatHeldConsumable();
        HotbarSystem.Instance.OnHotbarChanged += Refresh;
        ItemHolder.Instance.OnHeldItemChanged  += RefreshHighlights;
    }

    void OnDestroy()
    {
        inputActions.Disable();
        if (HotbarSystem.Instance != null)
            HotbarSystem.Instance.OnHotbarChanged -= Refresh;
        if (ItemHolder.Instance != null)
            ItemHolder.Instance.OnHeldItemChanged  -= RefreshHighlights;
    }

    void Start()
    {
        for (int i = 0; i < slots.Count; i++)
            slots[i].Init(i);
        Refresh();
    }

    void SelectSlot(int index)
    {
        var item = HotbarSystem.Instance.Get(index);
        if (item == null) return;

        bool alreadyInHand  = item.canBeEquipped
                           && ItemHolder.Instance.GetHeldItem(item.equipSlot) == item;

        slots[index].Pulse();

        if (alreadyInHand)
        {
            ItemHolder.Instance.ClearSlot(item.equipSlot);
            return;
        }

        if (item.canBeEquipped)
            ItemHolder.Instance.HoldItem(item);
    }

    void TryEatHeldConsumable()
    {
        var item = ItemHolder.Instance.GetHeldItem(EquipmentSlot.RightHand);
        if (item == null || item.itemType != ItemType.Consumable) return;

        item.UseEffect();
        ItemHolder.Instance.ClearSlot(EquipmentSlot.RightHand);

        // Remove from hotbar (or inventory as fallback)
        for (int i = 0; i < HotbarSystem.SlotCount; i++)
        {
            if (HotbarSystem.Instance.Get(i) != item) continue;
            HotbarSystem.Instance.Remove(i);
            return;
        }
        // Fallback: was equipped from inventory
        var invItems = InventorySystem.Instance.Items;
        for (int i = 0; i < invItems.Count; i++)
        {
            if (invItems[i] == item) { InventorySystem.Instance.Remove(i); return; }
        }
    }

    void Refresh()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var item = HotbarSystem.Instance.Get(i);
            if (item != null) slots[i].SetItem(item);
            else              slots[i].Clear();
        }
        RefreshHighlights();
    }

    void RefreshHighlights()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var item      = HotbarSystem.Instance.Get(i);
            bool equipped = item != null && item.canBeEquipped
                         && ItemHolder.Instance.GetHeldItem(item.equipSlot) == item;
            slots[i].SetEquipped(equipped);
        }
    }
}

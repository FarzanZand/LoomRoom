using System.Collections.Generic;
using UnityEngine;

// The hotbar strip. Binds to the active player's Hotbar; number keys toggle equip,
// right mouse uses a held consumable.
public class HotbarUI : MonoBehaviour
{
    [SerializeField] List<ItemSlotUI> slots = new();

    Inventory bound;
    Equipment boundEquipment;
    Player    player;

    void OnEnable()
    {
        if (InputManager.HasInstance)
        {
            InputManager.Instance.HotbarSelected   += SelectSlot;
            InputManager.Instance.SecondaryPressed += TryUseHeldConsumable;
        }
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
            if (PlayerManager.Instance.Active != null) OnPlayerSwapped(PlayerManager.Instance.Active);
        }
    }

    void OnDisable()
    {
        if (InputManager.HasInstance)
        {
            InputManager.Instance.HotbarSelected   -= SelectSlot;
            InputManager.Instance.SecondaryPressed -= TryUseHeldConsumable;
        }
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
        Bind(null);
    }

    void Start()
    {
        // Managers may awaken after this UI's OnEnable. Subscribe once they all exist.
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
        }
        if (InputManager.HasInstance)
        {
            InputManager.Instance.HotbarSelected -= SelectSlot;
            InputManager.Instance.HotbarSelected += SelectSlot;
            InputManager.Instance.SecondaryPressed -= TryUseHeldConsumable;
            InputManager.Instance.SecondaryPressed += TryUseHeldConsumable;
        }
        if (PlayerManager.HasInstance && PlayerManager.Instance.Active != null) OnPlayerSwapped(PlayerManager.Instance.Active);
    }

    void OnPlayerSwapped(Player p)
    {
        // A swap event already in flight can still reach us after the HUD disables us.
        player = isActiveAndEnabled && p != null && p.kind == PlayerKind.Table ? p : null;
        Bind(player);
    }

    void Bind(Player p)
    {
        if (bound != null)          bound.Changed          -= Refresh;
        if (boundEquipment != null) boundEquipment.Changed -= Refresh;
        bound          = p != null ? p.Hotbar    : null;
        boundEquipment = p != null ? p.Equipment : null;
        if (bound != null)          bound.Changed          += Refresh;
        if (boundEquipment != null) boundEquipment.Changed += Refresh;

        for (int i = 0; i < slots.Count; i++)
            if (slots[i] != null) slots[i].Bind(bound, i);
        Refresh();
    }

    void SelectSlot(int index)
    {
        if (bound == null || player == null || index < 0 || index >= slots.Count) return;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive) return;

        var item = bound.ItemAt(index);
        if (item == null) return;

        slots[index]?.Pulse();

        var eq = player.Equipment;
        if (eq == null || !eq.CanEquip(item))
        {
            if (item.IsConsumable) InventoryManager.Instance.Use(bound, index);
            return;
        }

        if (eq.IsEquipped(item)) eq.Unequip(item.equipSlot);
        else eq.Equip(item);
    }

    void TryUseHeldConsumable()
    {
        if (player == null || !InventoryManager.HasInstance) return;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive) return;
        InventoryManager.Instance.UseHeld(player);
    }

    void Refresh()
    {
        foreach (var s in slots) if (s != null) s.Refresh();
    }
}

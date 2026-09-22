using System.Collections.Generic;
using UnityEngine;

// The bag panel. Binds its authored slot objects to the active player's Bag and
// toggles through the Menu game state.
public class InventoryUI : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] List<ItemSlotUI> slots = new();

    Inventory bound;
    Equipment boundEquipment;
    bool isOpen;

    void OnEnable()
    {
        if (InputManager.HasInstance) InputManager.Instance.InventoryToggled += Toggle;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed    += Close;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped  += OnPlayerSwapped;
        if (PlayerManager.HasInstance && PlayerManager.Instance.Active != null) OnPlayerSwapped(PlayerManager.Instance.Active);
    }

    void OnDisable()
    {
        if (InputManager.HasInstance) InputManager.Instance.InventoryToggled -= Toggle;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed    -= Close;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped  -= OnPlayerSwapped;
        Bind(null, null);
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
            InputManager.Instance.InventoryToggled -= Toggle;
            InputManager.Instance.InventoryToggled += Toggle;
            InputManager.Instance.CancelPressed -= Close;
            InputManager.Instance.CancelPressed += Close;
        }
        if (panel != null) panel.SetActive(false);
        if (PlayerManager.HasInstance && PlayerManager.Instance.Active != null) OnPlayerSwapped(PlayerManager.Instance.Active);
    }

    void OnPlayerSwapped(Player player) => Bind(player != null ? player.Bag : null, player != null ? player.Equipment : null);

    void Bind(Inventory inv, Equipment eq)
    {
        if (bound != null)          bound.Changed          -= Refresh;
        if (boundEquipment != null) boundEquipment.Changed -= Refresh;
        bound = inv;
        boundEquipment = eq;
        if (bound != null)          bound.Changed          += Refresh;
        if (boundEquipment != null) boundEquipment.Changed += Refresh;

        for (int i = 0; i < slots.Count; i++)
            if (slots[i] != null) slots[i].Bind(bound, i);
        Refresh();
    }

    void Toggle()
    {
        if (isOpen) Close(); else Open();
    }

    void Open()
    {
        if (isOpen) return;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive) return;
        isOpen = true;
        if (panel != null) panel.SetActive(true);
        GameManager.Instance?.Push(GameState.Menu);
        Refresh();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        if (panel != null) panel.SetActive(false);
        if (TooltipUI.HasInstance) TooltipUI.Instance.Hide();
        if (ContextMenuUI.HasInstance) ContextMenuUI.Instance.Hide();
        GameManager.Instance?.Pop(GameState.Menu);
    }

    void Refresh()
    {
        foreach (var s in slots) if (s != null) s.Refresh();
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using MFPC;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private List<InventorySlot> slots;

    private bool isOpen;
    private PlayerInputActions inputActions;
    private System.Action<InputAction.CallbackContext> onInventory;

    void Awake()
    {
        inputActions = new PlayerInputActions();
        onInventory  = _ => Toggle();
        inputActions.Enable();
        inputActions.Player.Inventory.performed += onInventory;
    }

    void OnEnable()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged += Refresh;
    }

    void OnDisable()
    {
        if (InventorySystem.Instance != null)
            InventorySystem.Instance.OnInventoryChanged -= Refresh;
    }

    void OnDestroy()
    {
        inputActions.Player.Inventory.performed -= onInventory;
        inputActions.Disable();
    }

    void Start()
    {
        for (int i = 0; i < slots.Count; i++)
            slots[i].Init(i);
        panel.SetActive(false);
    }

    void Toggle()
    {
        isOpen = !isOpen;
        panel.SetActive(isOpen);
        Cursor.lockState = isOpen ? CursorLockMode.None  : CursorLockMode.Locked;
        Cursor.visible   = isOpen;
        if (isOpen) Refresh();
    }

    void Refresh()
    {
        var items = InventorySystem.Instance.Items;
        for (int i = 0; i < slots.Count; i++)
        {
            if (i < items.Count) slots[i].SetItem(items[i]);
            else                 slots[i].Clear();
        }
    }
}

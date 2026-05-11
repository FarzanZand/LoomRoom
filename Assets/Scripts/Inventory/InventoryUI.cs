using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using MFPC;

public class InventoryUI : MonoBehaviour
{
    [SerializeField] private GameObject      panel;
    [SerializeField] private List<InventorySlot> slots;

    private bool isOpen;
    private PlayerInputActions inputActions;
    private System.Action<InputAction.CallbackContext> onInventory;

    private TextMeshProUGUI fullNotice;
    private Coroutine       fullNoticeRoutine;

    void Awake()
    {
        inputActions = new PlayerInputActions();
        onInventory  = _ => Toggle();
        inputActions.Enable();
        inputActions.Player.Inventory.performed += onInventory;
    }

    void OnDestroy()
    {
        inputActions.Player.Inventory.performed -= onInventory;
        inputActions.Disable();
        if (InventorySystem.Instance != null)
        {
            InventorySystem.Instance.OnInventoryChanged -= Refresh;
            InventorySystem.Instance.OnInventoryFull    -= FlashFull;
        }
    }

    void Start()
    {
        for (int i = 0; i < slots.Count; i++)
            slots[i].Init(i);
        panel.SetActive(false);
        CreateFullNotice();
        InventorySystem.Instance.OnInventoryChanged += Refresh;
        InventorySystem.Instance.OnInventoryFull    += FlashFull;
    }

    private void CreateFullNotice()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) return;

        var go = new GameObject("InvFullNotice");
        go.transform.SetParent(canvas.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.08f);
        rt.anchorMax        = new Vector2(0.5f, 0.08f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = new Vector2(280f, 44f);
        rt.anchoredPosition = Vector2.zero;
        go.AddComponent<CanvasRenderer>();
        fullNotice                = go.AddComponent<TextMeshProUGUI>();
        fullNotice.raycastTarget  = false;
        fullNotice.text           = "Inventory Full!";
        fullNotice.fontSize   = 20f;
        fullNotice.fontStyle  = FontStyles.Bold;
        fullNotice.alignment  = TextAlignmentOptions.Center;
        fullNotice.color      = new Color(1f, 0.3f, 0.2f, 0f);
        go.SetActive(true);
    }

    void Toggle()
    {
        isOpen = !isOpen;
        panel.SetActive(isOpen);
        Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = isOpen;
        if (isOpen) { MenuManager.Instance.OpenMenu("inventory"); Refresh(); }
        else          MenuManager.Instance.CloseMenu("inventory");
    }

    void Refresh()
    {
        var items = InventorySystem.Instance.Items;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null) continue;
            if (i < items.Count && items[i] != null)
                slots[i].SetItem(items[i], InventorySystem.Instance.GetCount(i));
            else
                slots[i].Clear();
        }
    }

    private void FlashFull()
    {
        if (fullNotice == null) return;
        if (fullNoticeRoutine != null) StopCoroutine(fullNoticeRoutine);
        fullNoticeRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        float fadeIn  = 0.15f;
        float hold    = 0.8f;
        float fadeOut = 0.4f;

        float t = 0f;
        while (t < fadeIn)
        {
            SetNoticeAlpha(t / fadeIn);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        SetNoticeAlpha(1f);
        yield return new WaitForSecondsRealtime(hold);
        t = 0f;
        while (t < fadeOut)
        {
            SetNoticeAlpha(1f - t / fadeOut);
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        SetNoticeAlpha(0f);
        fullNoticeRoutine = null;
    }

    private void SetNoticeAlpha(float a)
    {
        if (fullNotice == null) return;
        var c = fullNotice.color;
        c.a = a;
        fullNotice.color = c;
    }
}

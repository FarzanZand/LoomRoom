using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Shows the active player's interaction prompt.
[DefaultExecutionOrder(11000)]
public class InteractUI : MonoBehaviour
{
    [SerializeField] GameObject promptRoot;
    [SerializeField] TMP_Text   promptText;

    [SerializeField] RectTransform targetMarker;
    InteractController bound;
    Canvas canvas;
    InputManager input;
    // What the prompt says now: rebuilt only when the key or the target's message changes.
    string shownKey, shownMessage;
    float nextPromptCheck;
    const float PromptCheckSeconds = .2f;

    void LateUpdate()
    {
        var target = bound != null ? bound.Active : null;
        // A prompt can change while it is shown (a door opened, a chest emptied): look again a few times a second.
        if (target != null && Time.unscaledTime >= nextPromptCheck)
        {
            nextPromptCheck = Time.unscaledTime + PromptCheckSeconds;
            ShowPrompt(target);
        }
        bool show = target != null && target.WorldItem != null && bound.ViewCamera != null;
        if (targetMarker == null) return;
        if (targetMarker.gameObject.activeSelf != show) targetMarker.gameObject.SetActive(show);
        if (!show) return;
        var bounds = target.TargetBounds;
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        if (canvas == null) canvas = targetMarker.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        var uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var parent = (RectTransform)targetMarker.parent;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 screen = bound.ViewCamera.WorldToScreenPoint(corner);
            if (screen.z <= 0) { targetMarker.gameObject.SetActive(false); return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out var local);
            min = Vector2.Min(min, local); max = Vector2.Max(max, local);
        }
        targetMarker.localPosition = (min + max) * .5f;
        targetMarker.sizeDelta = Vector2.Max(max - min + Vector2.one * 16, Vector2.one * 32);
    }

    void OnEnable()
    {
        ListenForKeys();
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
            OnPlayerSwapped(PlayerManager.Instance.Active);
        }
    }

    void OnDisable()
    {
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
        if (input != null) input.BindingsChanged -= OnBindingsChanged;
        input = null;
        Bind(null);
    }

    // The prompt names the Interact key: written again after a rebind.
    void ListenForKeys()
    {
        if (input != null || !InputManager.HasInstance) return;
        input = InputManager.Instance;
        input.BindingsChanged += OnBindingsChanged;
    }

    void OnBindingsChanged() { shownKey = null; if (bound != null && bound.Active != null) ShowPrompt(bound.Active); }

    void Start()
    {
        // Managers may awaken after this UI's OnEnable. Subscribe once they all exist.
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
        }
        if (PlayerManager.HasInstance) OnPlayerSwapped(PlayerManager.Instance.Active);
        ListenForKeys();
    }

    void OnPlayerSwapped(Player player) => Bind(player != null ? player.Interact : null);

    void Bind(InteractController controller)
    {
        if (bound != null) bound.OnActiveChanged -= OnActiveChanged;
        bound = controller;
        if (bound != null) bound.OnActiveChanged += OnActiveChanged;
        OnActiveChanged(bound != null ? bound.Active : null);
    }

    void OnActiveChanged(InteractableTrigger trigger)
    {
        if (promptRoot != null) promptRoot.SetActive(trigger != null);
        if (targetMarker != null && trigger == null) targetMarker.gameObject.SetActive(false);
        shownKey = shownMessage = null;
        if (trigger != null) ShowPrompt(trigger);
    }

    void ShowPrompt(InteractableTrigger trigger)
    {
        if (promptText == null) return;
        // The key is read once per rebind (shownKey is cleared then), the message each check.
        string key = shownKey ?? (InputManager.HasInstance ? InputManager.Instance.BindingDisplay("Interact") : "E");
        string message = trigger.WorldItem != null && trigger.WorldItem.Item != null
            ? trigger.WorldItem.Item.itemName : trigger.PromptMessage;
        if (key == shownKey && message == shownMessage) return;
        shownKey = key; shownMessage = message;
        promptText.text = "<b>[" + key + "]</b>  " + (trigger.WorldItem != null && trigger.WorldItem.Item != null ? "Pick up " + message : message);
        if (promptRoot != null)
        {
            var size = promptText.GetPreferredValues(promptText.text, 560, 0);
            ((RectTransform)promptRoot.transform).sizeDelta = new Vector2(size.x + 32, size.y + 16);
        }
    }
}

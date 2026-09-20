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

    void LateUpdate()
    {
        var target = bound != null ? bound.Active : null;
        bool show = target != null && target.WorldItem != null && bound.ViewCamera != null;
        if (targetMarker == null) return;
        targetMarker.gameObject.SetActive(show);
        if (!show) return;
        var bounds = target.TargetBounds;
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        var canvas = targetMarker.GetComponentInParent<Canvas>();
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
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
            OnPlayerSwapped(PlayerManager.Instance.Active);
        }
    }

    void OnDisable()
    {
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
        if (PlayerManager.HasInstance) OnPlayerSwapped(PlayerManager.Instance.Active);
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
        if (trigger != null && promptText != null)
        {
            string key = "E";
            if (InputManager.HasInstance && PlayerManager.HasInstance && PlayerManager.Instance.Active != null)
            {
                var actions = InputManager.Instance.Actions;
                var action = PlayerManager.Instance.Active.kind == PlayerKind.Table ? actions.Table.Interact : actions.Room.Interact;
                key = action.GetBindingDisplayString();
            }
            string message = trigger.WorldItem != null && trigger.WorldItem.Item != null
                ? "Pick up " + trigger.WorldItem.Item.itemName : trigger.PromptMessage;
            promptText.text = "<b>[" + key + "]</b>  " + message;
            if (promptRoot != null)
            {
                var size = promptText.GetPreferredValues(promptText.text, 560, 0);
                ((RectTransform)promptRoot.transform).sizeDelta = new Vector2(size.x + 32, size.y + 16);
            }
        }
    }
}

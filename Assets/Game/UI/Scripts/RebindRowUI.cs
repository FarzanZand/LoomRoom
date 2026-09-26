using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One rebindable control in the settings menu. Click, then press the new key.
public class RebindRowUI : MonoBehaviour
{
    [Tooltip("Action name in the Table/Room maps, e.g. Jump, Interact, PrimaryAction.")]
    [SerializeField] string actionName;
    [Tooltip("Composite part for Move: up, down, left or right. Empty for plain actions.")]
    [SerializeField] string part;
    [SerializeField] TMP_Text label;
    [SerializeField] TMP_Text binding;
    [SerializeField] Button button;

    void Awake() => button?.onClick.AddListener(Rebind);

    public void Refresh()
    {
        if (binding != null && InputManager.HasInstance) binding.text = InputManager.Instance.BindingDisplay(actionName, string.IsNullOrEmpty(part) ? null : part);
    }

    void Rebind()
    {
        if (!InputManager.HasInstance) return;
        if (binding != null) binding.text = "Press a key...";
        InputManager.Instance.StartRebind(actionName, string.IsNullOrEmpty(part) ? null : part, Refresh);
    }
}

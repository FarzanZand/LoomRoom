using TMPro;
using UnityEngine;

public class InteractUI : MonoBehaviour
{
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private InteractController[] controllers;

    private void OnEnable()
    {
        foreach (var c in controllers)
            if (c != null) c.OnActiveChanged += OnActiveChanged;
    }

    private void OnDisable()
    {
        foreach (var c in controllers)
            if (c != null) c.OnActiveChanged -= OnActiveChanged;
    }

    private void OnActiveChanged(IInteractable interactable)
    {
        promptRoot.SetActive(interactable != null);
        if (interactable != null)
            promptText.text = interactable.PromptMessage;
    }
}

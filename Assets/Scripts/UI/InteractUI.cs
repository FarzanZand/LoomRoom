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

    private void OnActiveChanged(InteractableTrigger trigger)
    {
        promptRoot.SetActive(trigger != null);
        if (trigger != null)
            promptText.text = trigger.PromptMessage;
    }
}

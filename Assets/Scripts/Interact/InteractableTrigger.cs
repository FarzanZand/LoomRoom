using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class InteractableTrigger : MonoBehaviour
{
    [SerializeField] private MonoBehaviour interactableTarget;

    public IInteractable Interactable { get; private set; }

    private readonly List<InteractController> registeredControllers = new();

    private void Awake()
    {
        if (interactableTarget != null)
            Interactable = interactableTarget as IInteractable;
        else
            Interactable = GetComponentInParent<IInteractable>();

        GetComponent<Collider>().isTrigger = true;
    }

    private void OnDisable()
    {
        foreach (var controller in registeredControllers)
            controller?.Unregister(this);
        registeredControllers.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        var controller = other.GetComponent<InteractController>();
        if (controller == null) return;
        registeredControllers.Add(controller);
        controller.Register(this);
    }

    private void OnTriggerExit(Collider other)
    {
        var controller = other.GetComponent<InteractController>();
        if (controller == null) return;
        registeredControllers.Remove(controller);
        controller.Unregister(this);
    }
}

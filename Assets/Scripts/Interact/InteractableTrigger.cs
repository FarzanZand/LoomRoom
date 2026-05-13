using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class InteractableTrigger : MonoBehaviour
{
    [Delayed]
    [SerializeField] string promptMessage = "Interact";
    [SerializeField] bool consumable = false;
    [ShowIf("consumable")]
    [SerializeField] bool destroyOnConsume = false;
    public UnityEvent<GameObject> onInteract;

    public string PromptMessage => worldItem != null ? worldItem.PromptMessage : promptMessage;
    public IInteractable Interactable { get; private set; }

    WorldItem worldItem;

    private readonly List<InteractController> registeredControllers = new();

    private void Awake()
    {
        Interactable = GetComponentInParent<IInteractable>();
        worldItem = GetComponentInParent<WorldItem>();
        GetComponent<Collider>().isTrigger = true;
    }

    public void Interact(GameObject interactor)
    {
        onInteract.Invoke(interactor);
        Interactable?.Interact(interactor);
        if (consumable)
        {
            if (destroyOnConsume) Destroy(gameObject);
            else enabled = false;
        }
    }

    private void OnDisable()
    {
        foreach (var controller in registeredControllers)
            controller?.Unregister(this);
        registeredControllers.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        var controller = other.GetComponentInParent<InteractController>();
        if (controller == null) return;
        registeredControllers.Add(controller);
        controller.Register(this);
    }

    private void OnTriggerExit(Collider other)
    {
        var controller = other.GetComponentInParent<InteractController>();
        if (controller == null) return;
        registeredControllers.Remove(controller);
        controller.Unregister(this);
    }
}

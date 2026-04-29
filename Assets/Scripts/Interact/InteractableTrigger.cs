using UnityEngine;

[RequireComponent(typeof(Collider))]
public class InteractableTrigger : MonoBehaviour
{
    [SerializeField] private MonoBehaviour interactableTarget;

    public IInteractable Interactable { get; private set; }

    private void Awake()
    {
        if (interactableTarget != null)
            Interactable = interactableTarget as IInteractable;
        else
            Interactable = GetComponentInParent<IInteractable>();

        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        other.GetComponent<InteractController>()?.Register(this);
    }

    private void OnTriggerExit(Collider other)
    {
        other.GetComponent<InteractController>()?.Unregister(this);
    }
}

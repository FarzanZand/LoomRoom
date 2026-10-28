using UnityEngine;

// One of the wager's three bowls (with an InteractableTrigger on its collider): picked while the dealer waits for a pick.
// Keeps the place it rests at on the table (local), which changes as the bowls are shuffled.
public class WagerCup : MonoBehaviour, IInteractable
{
    WagerEvent wager;
    Vector3 rest;
    bool hasRest;

    void Awake() { wager = GetComponentInParent<WagerEvent>(); }

    public Vector3 RestLocal(Transform self) { if (!hasRest) { rest = self.localPosition; hasRest = true; } return rest; }
    public void SetRest(Vector3 local) { rest = local; hasRest = true; }

    public string Prompt => wager != null && wager.Picking ? "Pick this bowl" : "";
    public bool CanInteract(Character who) => who is Player && wager != null && wager.Picking;
    public void Interact(Character who) { if (CanInteract(who)) wager.Pick(transform); }
}

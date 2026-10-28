using UnityEngine;

// One of the wager's three bowls (with an InteractableTrigger on its collider): picked while the dealer waits for a pick,
// and, once he has cheated, used to call him out.
// Keeps the place it rests at on the table (local), which changes as the bowls are shuffled.
public class WagerCup : MonoBehaviour, IInteractable
{
    WagerEvent wager;
    Vector3 rest;
    bool hasRest;

    void Awake() { wager = GetComponentInParent<WagerEvent>(); }

    public Vector3 RestLocal(Transform self) { if (!hasRest) { rest = self.localPosition; hasRest = true; } return rest; }
    public void SetRest(Vector3 local) { rest = local; hasRest = true; }

    public string Prompt => wager == null ? "" : wager.Picking ? "Pick this bowl" : wager.CanCall ? "Call him a cheat" : "";
    public bool CanInteract(Character who) => who is Player && wager != null && (wager.Picking || wager.CanCall);
    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        if (wager.Picking) wager.Pick(transform); else wager.CallOut(who as Player);
    }
}

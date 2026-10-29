using UnityEngine;

// The wager's table (with an InteractableTrigger on its collider): putting gold down starts a game (WagerEvent).
public class WagerTable : MonoBehaviour, IInteractable
{
    WagerEvent wager;
    void Awake() => wager = GetComponentInParent<WagerEvent>();

    public string Prompt => wager != null ? wager.TablePrompt : "";
    public bool CanInteract(Character who) => who is Player && wager != null && wager.CanBet;
    public void Interact(Character who) { if (CanInteract(who)) wager.PlaceBet(who as Player); }
}

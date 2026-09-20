// Anything the player can use. Prompt feeds the on-screen hint; CanInteract lets a
// locked door or a busy NPC refuse without a custom trigger.
public interface IInteractable
{
    string Prompt { get; }
    bool CanInteract(Character who);
    void Interact(Character who);
}

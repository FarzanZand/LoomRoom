// A second thing the player can do with an interactable, on its own key (the room's Map action, R): the table's
// R opens the map while E plays straight away. The prompt shows it beside the Interact key's line.
public interface ISecondaryInteractable
{
    // What the second key does now, or empty when it does nothing here.
    string SecondaryPrompt { get; }
    // The input action whose key the prompt names.
    string SecondaryAction { get; }
    void SecondaryInteract(Character who);
}

using UnityEngine;

public class AdventurePuzzleButton : MonoBehaviour, IInteractable
{
    public AdventureRoomPuzzle puzzle;
    public int symbol;
    public string Prompt => puzzle.Solved ? $"{puzzle.unlockedClassName} unlocked" : "Press the " + puzzle.symbols[symbol];
    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room && puzzle.Revealed && !puzzle.Solved;
    public void Interact(Character who) { if (CanInteract(who)) puzzle.Press(symbol); }
}


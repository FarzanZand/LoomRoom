using UnityEngine;

public class DungeonExit : MonoBehaviour, IInteractable
{
    public bool entrance;
    public string Prompt => entrance ? "Leave dungeon" : "Complete floor and return";
    public bool CanInteract(Character who) => who is Player;
    public void Interact(Character who)
    {
        if (CanInteract(who)) FindAnyObjectByType<TableLevelLoader>()?.ShowSelection(entrance ? "Leave the crypt" : "Floor complete");
    }
}

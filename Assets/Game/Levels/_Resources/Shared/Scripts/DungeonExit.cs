using UnityEngine;

public class DungeonExit : MonoBehaviour, IInteractable
{
    public bool entrance;
    TableLevelLoader Loader => TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
    public string Prompt => entrance ? "Leave dungeon" : Loader!=null && Loader.HasNextFloor ? "Descend to floor "+(Loader.FloorNumber+1) : "Complete dungeon and return";
    public bool CanInteract(Character who) => who is Player p && p.kind==PlayerKind.Table && p.IsAlive && Loader!=null && !Loader.Busy;
    public void Interact(Character who)
    {
        if (!CanInteract(who))return;
        if(!entrance && Loader.HasNextFloor)Loader.Descend();
        else Loader.ShowSelection(entrance ? "Leave the crypt" : "Dungeon complete");
    }
    void OnTriggerEnter(Collider other)
    {
        if(entrance || Loader==null || !Loader.HasNextFloor)return;
        var player=other.GetComponentInParent<Player>();
        if(player!=null && player.IsActive)Interact(player);
    }
}

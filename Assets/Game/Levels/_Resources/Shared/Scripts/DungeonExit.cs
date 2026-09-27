using UnityEngine;

public class DungeonExit : MonoBehaviour, IInteractable
{
    public bool entrance;
    TableLevelLoader Loader => TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
    public string Prompt => !entrance && Sealed ? "Blocked" : entrance ? "No way back" : Loader!=null && Loader.HasNextFloor ? "Descend to floor "+(Loader.FloorNumber+1) : "Complete dungeon and return";
    // A living boss seals the way down (see DungeonBossEncounter).
    public bool Sealed { get; set; }
    public bool CanInteract(Character who) => who is Player p && p.kind==PlayerKind.Table && p.IsAlive && Loader!=null && !Loader.Busy;
    public void Interact(Character who)
    {
        if (!CanInteract(who))return;
        if(!entrance && Sealed){MessageLog.Post("The stairs are blocked until the boss is dead.",MessageKind.Warning);NotificationUI.Show("Blocked");return;}
        if(!entrance && Loader.HasNextFloor)Loader.Descend();
        else if(!entrance)Loader.CompleteRun();
        else NotificationUI.Show("No way back");
    }
    void OnTriggerEnter(Collider other)
    {
        if(entrance || Sealed || Loader==null || !Loader.HasNextFloor)return;
        var player=other.GetComponentInParent<Player>();
        if(player!=null && player.IsActive)Interact(player);
    }
}

using UnityEngine;

public class DungeonExit : MonoBehaviour, IInteractable
{
    public bool entrance;
    TableLevelLoader Loader => TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;
    public string Prompt => !entrance && Sealed ? "The way is sealed" : entrance ? "Leave dungeon" : Loader!=null && Loader.HasNextFloor ? "Descend to floor "+(Loader.FloorNumber+1) : "Complete dungeon and return";
    // A living boss seals the way down (see DungeonBossEncounter).
    public bool Sealed { get; set; }
    public bool CanInteract(Character who) => who is Player p && p.kind==PlayerKind.Table && p.IsAlive && Loader!=null && !Loader.Busy;
    public void Interact(Character who)
    {
        if (!CanInteract(who))return;
        if(!entrance && Sealed){MessageLog.Post("A dark power holds the way shut. Slay its guardian.",MessageKind.Warning);NotificationUI.Show("The way is sealed");return;}
        if(!entrance && Loader.HasNextFloor)Loader.Descend();
        else if(!entrance)Loader.CompleteRun();
        else Loader.ShowSelection("Leave the crypt");
    }
    void OnTriggerEnter(Collider other)
    {
        if(entrance || Sealed || Loader==null || !Loader.HasNextFloor)return;
        var player=other.GetComponentInParent<Player>();
        if(player!=null && player.IsActive)Interact(player);
    }
}

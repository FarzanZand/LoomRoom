using UnityEngine;

// Table entry selects a level. The intro steps only tell TableLevelLoader what belongs to the town.
public class TableManager : Singleton<TableManager>, IInteractable
{
    public TableIntroController tableIntroController;
    public GameObject DM;
    public Transform dmPlacement;
    [SerializeField] string prompt = "Choose table adventure";
    TableLevelLoader loader;
    public string Prompt => prompt;
    public bool CanInteract(Character who) => who is Player player && player.kind == PlayerKind.Room
        && player.IsAlive && PlayerManager.HasInstance && PlayerManager.Instance.Active == player
        && (loader == null || !loader.Busy);
    protected override void Awake()
    {
        base.Awake();
        loader = GetComponent<TableLevelLoader>() ?? gameObject.AddComponent<TableLevelLoader>();
    }
    public void Interact(Character who) { if (CanInteract(who)) EnterTable(); }
    public void EnterTable()
    {
        if (loader == null || !PlayerManager.HasInstance || !CanInteract(PlayerManager.Instance.Active)) return;
        loader.ShowSelection();
    }
}

using System.Collections;
using UnityEngine;

// The game table: first use plays the intro, later uses just swap to the table player.
public class TableManager : Singleton<TableManager>, IInteractable
{
    public TableIntroController tableIntroController;
    public GameObject DM;
    public Transform dmPlacement;
    [SerializeField] string prompt = "Sit at the table";

    public string Prompt => prompt;
    public bool CanInteract(Character who) => who is Player;

    void Start()
    {
        if (ProgressionManager.HasInstance && ProgressionManager.Instance.tableEntered)
            EnterTable();
    }

    public void Interact(Character who)
    {
        bool entered = ProgressionManager.HasInstance && ProgressionManager.Instance.tableEntered;
        if (!entered && tableIntroController != null) tableIntroController.PlayTableIntro();
        else EnterTable();
    }

    public void EnterTable() => StartCoroutine(EnterTableRoutine());

    IEnumerator EnterTableRoutine()
    {
        if (ScreenManager.HasInstance) ScreenManager.Instance.FadeInOut(1f, 1f, 1f);
        yield return new WaitForSeconds(1.5f);
        PlayerManager.Instance?.SwapToPlayer(PlayerKind.Table);
        if (DM != null && dmPlacement != null) DM.transform.rotation = dmPlacement.rotation;
    }
}

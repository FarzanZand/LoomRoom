using System.Collections;
using UnityEngine;

public class TableManager : Singleton<TableManager>, IInteractable
{
    public TableIntroController tableIntroController;
    public GameObject DM;
    public Transform dmPlacement;
    NPCController dmNPC;
    
    public void Start()
    {
        dmNPC = DM.GetComponent<NPCController>();
        if (ProgressionManager.Instance.tableEntered)
            EnterTable();
    }
    
    
    public void Interact(GameObject interactor)
    {
        if (ProgressionManager.Instance.tableEntered == false)
            tableIntroController.PlayTableIntro();
        else
            EnterTable();
    }

    public void EnterTable()
    {
        StartCoroutine(EnterTableRoutine());
    }

    private IEnumerator EnterTableRoutine()
    {
        ScreenManager.Instance.FadeInOut(1f, 1f, 1f);
        yield return new WaitForSeconds(1.5f);
        PlayerManager.Instance.SwapToPlayer(PlayerManager.ActivePlayer.TablePlayer);
        DM.transform.rotation = dmPlacement.rotation;
        Debug.Log($"[TableManager] DM rotation set to {dmPlacement.rotation.eulerAngles}");
    }
}

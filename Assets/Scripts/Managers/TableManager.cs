using System.Collections;
using UnityEngine;

public class TableManager : MonoBehaviour, IInteractable
{
    public TableIntroController tableIntroController;

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
    }
}

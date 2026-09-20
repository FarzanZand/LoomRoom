using System.Collections;
using UnityEngine;

// The game table: first use plays the intro, later uses just swap to the table player.
public class TableManager : Singleton<TableManager>, IInteractable
{
    public TableIntroController tableIntroController;
    public GameObject DM;
    public Transform dmPlacement;
    [SerializeField] string prompt = "Sit at the table";
    bool entering;

    public string Prompt => prompt;
    public bool CanInteract(Character who) => who is Player && !entering &&
        (tableIntroController == null || !tableIntroController.IsPlaying);

    void Start()
    {
        if (ProgressionManager.HasInstance && ProgressionManager.Instance.tableEntered)
            EnterTable();
    }

    public void Interact(Character who)
    {
        if (!CanInteract(who)) return;
        bool entered = ProgressionManager.HasInstance && ProgressionManager.Instance.tableEntered;
        if (!entered && tableIntroController != null) tableIntroController.PlayTableIntro();
        else EnterTable();
    }

    public void EnterTable()
    {
        if (!entering) StartCoroutine(EnterTableRoutine());
    }

    IEnumerator EnterTableRoutine()
    {
        entering = true;
        var pm = PlayerManager.Instance;
        bool freeze = GameManager.HasInstance && !GameManager.Instance.IsOpen(GameState.Cutscene);
        if (freeze) pm?.SetControlsFrozen(true);
        try
        {
            if (ScreenManager.HasInstance)
                yield return ScreenManager.Instance.TransitionThroughBlack(1f, 0.5f, 0.5f, 1f, Swap);
            else Swap();
        }
        finally
        {
            if (freeze) pm?.SetControlsFrozen(false);
            entering = false;
        }

        void Swap()
        {
            pm?.SwapToPlayerImmediately(PlayerKind.Table);
            if (DM != null && dmPlacement != null) DM.transform.rotation = dmPlacement.rotation;
        }
    }
}

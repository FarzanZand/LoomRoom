using UnityEngine;

public class TableManager : MonoBehaviour, IInteractable
{
    [SerializeField] private string promptMessage = "Press E to enter table";

    public string PromptMessage => promptMessage;

    public void Interact(GameObject interactor)
    {
        PlayerManager.Instance.SwapToPlayer(PlayerManager.ActivePlayer.TablePlayer);
    }
}

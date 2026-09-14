using UnityEngine;

public class DialogueInputController : MonoBehaviour
{
    void OnConversationStart(Transform actor)
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        PlayerManager.Instance?.SetControlsFrozen(true);
    }

    void OnConversationEnd(Transform actor)
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        PlayerManager.Instance?.SetControlsFrozen(false);
    }
}

using UnityEngine;

public class DinnerCutsceneController : CutsceneController
{
    [Header("Dinner")]
    [Tooltip("Where the room player stands when dinner ends.")]
    [SerializeField] Transform roomPlayerDinner;

    protected override void OnFinished()
    {
        if (!PlayerManager.HasInstance) return;
        var pm = PlayerManager.Instance;

        var room = pm.GetPlayer(PlayerKind.Room);
        if (room != null && roomPlayerDinner != null)
        {
            room.Warp(roomPlayerDinner.position);
            room.transform.rotation = roomPlayerDinner.rotation;
            room.Look?.SetYaw(roomPlayerDinner.eulerAngles.y);
        }

        pm.ForceSwapToPlayer(PlayerKind.Room);
    }
}

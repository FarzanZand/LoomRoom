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

        var room = pm.roomPlayer;
        if (room != null && roomPlayerDinner != null)
        {
            var ctx = pm.Get(PlayerKind.Room);
            var target = ctx != null && ctx.player != null ? ctx.player.transform : room.transform;
            target.SetPositionAndRotation(roomPlayerDinner.position, roomPlayerDinner.rotation);
            ctx?.player?.Look?.SetYaw(roomPlayerDinner.eulerAngles.y);
        }

        pm.ForceSwapToPlayer(PlayerKind.Room);
    }
}

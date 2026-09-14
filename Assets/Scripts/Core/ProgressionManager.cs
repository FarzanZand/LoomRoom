using UnityEngine;

public class ProgressionManager : Singleton<ProgressionManager>
{
    public bool tableEntered = false;
    public bool skipWakeUp = false;

    [Header("Player")]
    public PlayerManager.ActivePlayer startingPlayer = PlayerManager.ActivePlayer.RoomPlayer;
}

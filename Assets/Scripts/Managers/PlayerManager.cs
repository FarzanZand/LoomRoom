using MFPC;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerManager : Singleton<PlayerManager>
{
    public enum ActivePlayer { RoomPlayer, TablePlayer }

    [Header("Players")]
    public GameObject roomPlayer;
    public GameObject roomPlayerBody;
    public GameObject roomPlayerTorso;
    public GameObject tablePlayer;
    public GameObject tablePlayerBody;
    public GameObject tablePlayerTorso;

    [Header("Settings")]
    [SerializeField] public ActivePlayer startingPlayer = ActivePlayer.RoomPlayer;

    public ActivePlayer? CurrentPlayer { get; private set; }

    public GameObject ActivePlayerObject =>
        (CurrentPlayer ?? startingPlayer) == ActivePlayer.RoomPlayer ? roomPlayer : tablePlayer;

    public event System.Action<ActivePlayer> OnPlayerSwapped;

    private PlayerInputActions inputActions;

    protected override void Awake()
    {
        base.Awake();
        inputActions = new PlayerInputActions();
        SetPlayerControlled(roomPlayer, false);
        SetPlayerControlled(tablePlayer, false);
    }

    private void OnEnable()
    {
        inputActions.Enable();
        inputActions.Player.Debug1.performed += OnDebug1;
        inputActions.Player.Debug2.performed += OnDebug2;
    }

    private void OnDisable()
    {
        inputActions.Player.Debug1.performed -= OnDebug1;
        inputActions.Player.Debug2.performed -= OnDebug2;
        inputActions.Disable();
    }

    private void OnDebug1(InputAction.CallbackContext _) => SwapToPlayer(ActivePlayer.RoomPlayer);
    private void OnDebug2(InputAction.CallbackContext _) => SwapToPlayer(ActivePlayer.TablePlayer);

    private void Start()
    {
        SwapToPlayer(startingPlayer);
    }

    public void SwapToPlayer(ActivePlayer player)
    {
        if (CurrentPlayer == player) return;
        CurrentPlayer = player;

        if (roomPlayerBody != null)
            roomPlayerBody.transform.SetPositionAndRotation(roomPlayer.transform.position, roomPlayerTorso.transform.rotation);
        if (tablePlayerBody != null)
            tablePlayerBody.transform.SetPositionAndRotation(tablePlayer.transform.position, tablePlayerTorso.transform.rotation);

        SetPlayerControlled(roomPlayer, player == ActivePlayer.RoomPlayer);
        SetPlayerControlled(tablePlayer, player == ActivePlayer.TablePlayer);

        OnPlayerSwapped?.Invoke(player);
        InventorySystem.Instance.NotifyChanged();
        HotbarSystem.Instance.NotifyChanged();
    }

    public void SetControlsFrozen(bool frozen)
    {
        var activePlayer = CurrentPlayer == ActivePlayer.RoomPlayer ? roomPlayer : tablePlayer;
        if (activePlayer == null) return;

        var pc = activePlayer.GetComponentInChildren<MFPC.PlayerController>();
        if (pc != null) pc.SetInputEnabled(!frozen);

        var interact = activePlayer.GetComponentInChildren<InteractController>();
        if (interact != null) interact.SetBlocked(frozen);
    }

    public void SetRoomPlayerControllerEnabled(bool enabled)
    {
        if (roomPlayer == null) return;
        var pc = roomPlayer.GetComponentInChildren<MFPC.PlayerController>(true);
        if (pc != null) pc.enabled = enabled;
    }

    private void SetPlayerControlled(GameObject playerObject, bool controlled)
    {
        if (playerObject == null) return;
        playerObject.SetActive(controlled);
    }
}

using MFPC;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerManager : Singleton<PlayerManager>
{
    public enum ActivePlayer { RoomPlayer, TablePlayer }

    [Header("Players")]
    public GameObject roomPlayer;
    public GameObject tablePlayer;

    [Header("Settings")]
    [SerializeField] private ActivePlayer startingPlayer = ActivePlayer.RoomPlayer;

    public ActivePlayer CurrentPlayer { get; private set; }

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
        CurrentPlayer = player;

        SetPlayerControlled(roomPlayer, player == ActivePlayer.RoomPlayer);
        SetPlayerControlled(tablePlayer, player == ActivePlayer.TablePlayer);
    }

    private void SetPlayerControlled(GameObject playerObject, bool controlled)
    {
        if (playerObject == null) return;
        playerObject.SetActive(controlled);
    }
}

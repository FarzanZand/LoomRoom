using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum PlayerKind { Room = 0, Table = 1 }

// Everything a swap needs to know about one player, wired once in the Inspector.
[Serializable]
public class PlayerContext
{
    public PlayerKind kind;
    [Tooltip("The Player component (on the object that carries the CharacterController).")]
    public Player player;
}

// Owns which of the two players is live. Swapping activates the right root, switches
// the input map, and tells everyone through PlayerSwapped. Nothing else should poke
// SetActive on a player.
public class PlayerManager : Singleton<PlayerManager>
{
    [ListDrawerSettings(ShowFoldout = true)]
    public List<PlayerContext> players = new();
    [SerializeField] Camera outputCamera;
    public Camera OutputCamera => outputCamera;

    public Player Active { get; private set; }
    public PlayerKind ActiveKind { get; private set; }
    public bool HasActive => Active != null;

    public event Action<Player> PlayerSwapped;

    // Convenience accessors kept for cutscene scripts.
    public GameObject roomPlayer  => Get(PlayerKind.Room)?.player?.ActivationRoot;
    public GameObject tablePlayer => Get(PlayerKind.Table)?.player?.ActivationRoot;

    PlayerKind StartingPlayer =>
        ProgressionManager.HasInstance ? ProgressionManager.Instance.startingPlayer : PlayerKind.Room;

    public PlayerContext Get(PlayerKind kind)
    {
        foreach (var p in players)
            if (p.kind == kind) return p;
        return null;
    }

    public Player GetPlayer(PlayerKind kind) => Get(kind)?.player;

    protected override void Awake()
    {
        base.Awake();
        foreach (var p in players)
            if (p.player != null) p.player.ActivationRoot.SetActive(false);
    }

    void OnEnable()
    {
        if (InputManager.HasInstance)
            InputManager.Instance.DebugSwapRequested += SwapToPlayer;
    }

    void OnDisable()
    {
        if (InputManager.HasInstance)
            InputManager.Instance.DebugSwapRequested -= SwapToPlayer;
    }

    void Start()
    {
        // InputManager awakes before us (execution order) but OnEnable order isn't
        // guaranteed, so make sure the debug hook is attached.
        if (InputManager.HasInstance)
        {
            InputManager.Instance.DebugSwapRequested -= SwapToPlayer;
            InputManager.Instance.DebugSwapRequested += SwapToPlayer;
        }
        SwapToPlayer(StartingPlayer, force: true);
    }

    public void ForceSwapToPlayer(PlayerKind kind) => SwapToPlayer(kind, force: true);

    public void SwapToPlayer(PlayerKind kind) => SwapToPlayer(kind, force: false);

    public void SwapToPlayer(PlayerKind kind, bool force)
    {
        if (!force && Active != null && ActiveKind == kind) return;

        var ctx = Get(kind);
        if (ctx == null || ctx.player == null)
        {
            Debug.LogError($"[PlayerManager] No PlayerContext wired for {kind}.", this);
            return;
        }

        Active     = ctx.player;
        ActiveKind = kind;
        Active.ViewPresentation?.Configure(outputCamera);

        foreach (var p in players)
        {
            bool on = p.kind == kind;
            if (p.player == null) continue;
            p.player.SynchronizePresentation();
            p.player.ActivationRoot.SetActive(on);
        }

        if (InputManager.HasInstance)
            InputManager.Instance.SetGameplayMap(kind);

        PlayerSwapped?.Invoke(Active);
    }

    // Kept for cutscene scripts: freezing is just the Cutscene game state.
    public void SetControlsFrozen(bool frozen)
    {
        if (!GameManager.HasInstance) return;
        if (frozen) GameManager.Instance.Push(GameState.Cutscene);
        else        GameManager.Instance.Pop(GameState.Cutscene);
    }
}

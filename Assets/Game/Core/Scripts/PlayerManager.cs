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

// Owns which player is controlled. The inactive room body remains in the world,
// while its Controller subtree (input, movement and camera) is disabled.
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

#if UNITY_EDITOR
    // Outside play mode the Game view renders through the output camera as saved in the scene.
    // Give it the starting player's view (culling mask, clip planes) so it never shows that
    // player's own head from the inside.
    void OnValidate()
    {
        if (Application.isPlaying || outputCamera == null) return;
        var progression = FindAnyObjectByType<ProgressionManager>();
        var view = Get(progression != null ? progression.startingPlayer : PlayerKind.Room)?.player?.ViewPresentation;
        if (view == null || outputCamera.cullingMask == view.cullingMask) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || outputCamera == null || view == null || Application.isPlaying) return;
            UnityEditor.Undo.RecordObject(outputCamera, "Match starting player view");
            view.Configure(outputCamera);
        };
    }
#endif

    public void ForceSwapToPlayer(PlayerKind kind) => SwapToPlayer(kind, force: true);

    // Players start switched off (see Awake), so a player that has never been active hasn't run its
    // own Awake yet: no Stats, Bag or Equipment. Wake it without making it the active player.
    public void EnsureInitialized(PlayerKind kind)
    {
        var player = GetPlayer(kind);
        if (player == null || player.Equipment != null || player == Active) return;
        var root = player.ActivationRoot;
        bool rootActive = root.activeSelf, selfActive = player.gameObject.activeSelf;
        player.gameObject.SetActive(true); root.SetActive(true);
        root.SetActive(rootActive); player.gameObject.SetActive(selfActive);
    }

    // For covered transitions: discard the room-to-table camera travel.
    public void SwapToPlayerImmediately(PlayerKind kind)
    {
        SwapToPlayer(kind);
        var cam = Active != null ? Active.CameraRig?.Camera : null;
        if (cam != null) cam.PreviousStateIsValid = false;
        var brain = outputCamera != null ? outputCamera.GetComponent<Unity.Cinemachine.CinemachineBrain>() : null;
        if (brain != null) brain.ResetState();
    }

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
            // BodyVisuals is a sibling of Controller, so it can stay visible without
            // leaving any of the room player's gameplay or camera scripts running.
            bool keepBody = !on && p.kind == PlayerKind.Room &&
                p.player.ActivationRoot != p.player.gameObject;
            if (!on) p.player.gameObject.SetActive(false);
            p.player.ActivationRoot.SetActive(on || keepBody);
            if (on) p.player.gameObject.SetActive(true);
        }

        if (InputManager.HasInstance)
            InputManager.Instance.SetGameplayMap(kind);

        PlayerSwapped?.Invoke(Active);
    }
}

using System;
using UnityEngine;
using UnityEngine.InputSystem;

// The single owner of the input asset. Everything else reads these properties and
// events; nothing else instantiates PlayerInputActions. Which gameplay map is live
// follows the active player (Room vs Table have different controls) and GameManager
// turns gameplay off entirely while a menu, dialogue or cutscene runs.
//
// Room and Table share action names, so this subscribes to both maps and only the
// enabled one ever fires.
[DefaultExecutionOrder(-500)]
public class InputManager : Singleton<InputManager>
{
    PlayerInputActions actions;
    PlayerKind gameplayKind = PlayerKind.Room;
    bool gameplayEnabled = true;
    int discardLookThroughFrame = -1;

    // ── Continuous state ──────────────────────────────────────────────
    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }
    public bool SprintHeld    { get; private set; }
    public bool CrouchHeld    { get; private set; }
    public bool LeanLeftHeld  { get; private set; }
    public bool LeanRightHeld { get; private set; }
    public bool PrimaryHeld   { get; private set; }
    public bool SecondaryHeld { get; private set; }

    // ── Edge events ───────────────────────────────────────────────────
    public event Action JumpPressed;
    public event Action SprintPressed;
    public event Action CrouchPressed;
    public event Action InteractPressed;
    public event Action InventoryToggled;
    public event Action CancelPressed;
    public event Action PausePressed;    // Room or Table gameplay: open the pause menu
    public event Action NavigatePressed; // UI: move between menu options
    public event Action SubmitPressed;   // UI: confirm or skip
    public bool CancelHandledThisFrame => cancelFrame == Time.frameCount;
    int cancelFrame = -1;
    public event Action PrimaryPressed;
    public event Action PrimaryReleased;
    public event Action SecondaryPressed;
    public event Action SecondaryReleased;
    public event Action<int> HotbarSelected;
    public event Action<PlayerKind> DebugSwapRequested;
    public event Action DebugMoodPreviewRequested;

    public PlayerInputActions Actions => actions;

    protected override void Awake()
    {
        base.Awake();
        actions = new PlayerInputActions();
        Bind(actions.Room.Move, actions.Room.Look, actions.Room.Jump, actions.Room.Sprint, actions.Room.Crouch,
             actions.Room.Interact, actions.Room.Inventory,
             new[] { actions.Room.Hotbar1, actions.Room.Hotbar2, actions.Room.Hotbar3,
                     actions.Room.Hotbar4, actions.Room.Hotbar5, actions.Room.Hotbar6 });
        Bind(actions.Table.Move, actions.Table.Look, actions.Table.Jump, actions.Table.Sprint, actions.Table.Crouch,
             actions.Table.Interact, actions.Table.Inventory,
             new[] { actions.Table.Hotbar1, actions.Table.Hotbar2, actions.Table.Hotbar3,
                     actions.Table.Hotbar4, actions.Table.Hotbar5, actions.Table.Hotbar6 });

        actions.Table.LeanLeft.performed  += _ => LeanLeftHeld  = true;
        actions.Table.LeanLeft.canceled   += _ => LeanLeftHeld  = false;
        actions.Table.LeanRight.performed += _ => LeanRightHeld = true;
        actions.Table.LeanRight.canceled  += _ => LeanRightHeld = false;
        actions.Table.PrimaryAction.performed   += _ => { PrimaryHeld = true;    PrimaryPressed?.Invoke(); };
        actions.Table.PrimaryAction.canceled    += _ => { PrimaryHeld = false;   PrimaryReleased?.Invoke(); };
        actions.Table.SecondaryAction.performed += _ => { SecondaryHeld = true;  SecondaryPressed?.Invoke(); };
        actions.Table.SecondaryAction.canceled  += _ => { SecondaryHeld = false; SecondaryReleased?.Invoke(); };

        actions.Table.Pause.performed += _ => PausePressed?.Invoke();
        actions.Room.Pause.performed  += _ => PausePressed?.Invoke();
        LoadBindingOverrides();
        actions.UI.Navigate.performed += _ => NavigatePressed?.Invoke();
        actions.UI.Submit.performed   += _ => SubmitPressed?.Invoke();
        actions.UI.Inventory.performed += _ => InventoryToggled?.Invoke();
        actions.UI.Cancel.performed    += _ =>
        {
            if (CancelHandledThisFrame) return;
            cancelFrame = Time.frameCount;
            // Dismiss a nested item action menu before its underlying inventory.
            if (ContextMenuUI.HasInstance && ContextMenuUI.Instance.IsOpen) ContextMenuUI.Instance.Hide();
            else CancelPressed?.Invoke();
        };

        actions.Dev.Debug1.performed += _ => DebugSwapRequested?.Invoke(PlayerKind.Room);
        actions.Dev.Debug2.performed += _ => DebugSwapRequested?.Invoke(PlayerKind.Table);
        actions.Dev.PreviewSceneMood.performed += _ => DebugMoodPreviewRequested?.Invoke();
    }

    void Bind(InputAction move, InputAction look, InputAction jump, InputAction sprint, InputAction crouch,
              InputAction interact, InputAction inventory, InputAction[] hotbar)
    {
        move.performed += ctx => Move = ctx.ReadValue<Vector2>();
        move.canceled  += _   => Move = Vector2.zero;
        look.performed += ctx => Look = gameplayEnabled && Time.frameCount > discardLookThroughFrame ? ctx.ReadValue<Vector2>() : Vector2.zero;
        look.canceled  += _   => Look = Vector2.zero;
        jump.performed += _ => JumpPressed?.Invoke();
        sprint.performed += _ => { SprintHeld = true;  SprintPressed?.Invoke(); };
        sprint.canceled  += _ =>   SprintHeld = false;
        crouch.performed += _ => { CrouchHeld = true;  CrouchPressed?.Invoke(); };
        crouch.canceled  += _ =>   CrouchHeld = false;
        interact.performed  += _ => InteractPressed?.Invoke();
        inventory.performed += _ => InventoryToggled?.Invoke();
        for (int i = 0; i < hotbar.Length; i++)
        {
            int index = i;
            hotbar[i].performed += _ => HotbarSelected?.Invoke(index);
        }
    }

    void OnEnable()
    {
        actions.Dev.Enable();
        ApplyMaps();
    }

    void OnDisable()
    {
        actions.Disable();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        actions?.Dispose();
        actions = null;
    }

    // ── Rebinding ─────────────────────────────────────────────────────
    // Room and Table share action names; a rebind is made on the Table action and mirrored
    // onto the Room action with the same name, so both players keep the same keys.

    const string BindingsPref = "input_bindings";
    UnityEngine.InputSystem.InputActionRebindingExtensions.RebindingOperation rebind;
    public bool IsRebinding => rebind != null;

    void LoadBindingOverrides()
    {
        string json = PlayerPrefs.GetString(BindingsPref, "");
        if (string.IsNullOrEmpty(json)) return;
        try { actions.asset.LoadBindingOverridesFromJson(json); }
        catch (Exception e) { Debug.LogWarning($"[InputManager] Ignoring saved bindings: {e.Message}"); }
    }

    void SaveBindingOverrides() => PlayerPrefs.SetString(BindingsPref, actions.asset.SaveBindingOverridesAsJson());

    public void ResetBindings()
    {
        actions.asset.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(BindingsPref);
    }

    // The keyboard/mouse binding for an action, or one part of a composite ("up", "left"...).
    static int FindBinding(InputAction action, string part)
    {
        if (action == null) return -1;
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var b = action.bindings[i];
            if (b.isComposite) continue;
            if (!string.IsNullOrEmpty(part) != b.isPartOfComposite) continue;
            if (!string.IsNullOrEmpty(part) && !string.Equals(b.name, part, StringComparison.OrdinalIgnoreCase)) continue;
            string path = b.effectivePath ?? "";
            if (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>")) return i;
        }
        return -1;
    }

    InputAction TableAction(string name) => actions.Table.Get().FindAction(name);
    InputAction RoomAction(string name) => actions.Room.Get().FindAction(name);

    public string BindingDisplay(string actionName, string part = null)
    {
        var action = TableAction(actionName) ?? RoomAction(actionName);
        int index = FindBinding(action, part);
        return index < 0 ? "-" : action.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions);
    }

    // Listens for the next key or mouse button. Escape cancels. onDone runs either way.
    public void StartRebind(string actionName, string part, Action onDone)
    {
        CancelRebind();
        var action = TableAction(actionName) ?? RoomAction(actionName);
        int index = FindBinding(action, part);
        if (index < 0) { onDone?.Invoke(); return; }
        bool wasEnabled = action.enabled;
        action.Disable();
        rebind = action.PerformInteractiveRebinding(index)
            .WithControlsHavingToMatchPath("<Keyboard>")
            .WithControlsHavingToMatchPath("<Mouse>")
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            .WithControlsExcluding("<Mouse>/scroll")
            .WithCancelingThrough("<Keyboard>/escape")
            .OnMatchWaitForAnother(.1f)
            .OnComplete(op =>
            {
                Mirror(action, index);
                SaveBindingOverrides();
                Finish();
            })
            .OnCancel(_ => Finish());
        rebind.Start();

        void Finish()
        {
            rebind?.Dispose();
            rebind = null;
            if (wasEnabled) action.Enable();
            // The UI map sees the pressed key too; do not let it close the menu.
            cancelFrame = Time.frameCount;
            onDone?.Invoke();
        }
    }

    public void CancelRebind() => rebind?.Cancel();

    void Mirror(InputAction source, int index)
    {
        var binding = source.bindings[index];
        var map = source.actionMap == actions.Table.Get() ? actions.Room.Get() : actions.Table.Get();
        var other = map.FindAction(source.name);
        if (other == null) return;
        for (int i = 0; i < other.bindings.Count; i++)
        {
            var b = other.bindings[i];
            if (b.isComposite || b.path != binding.path || b.name != binding.name) continue;
            other.ApplyBindingOverride(i, binding.overridePath);
        }
    }

    // Called by PlayerManager on swap: Room and Table players have different controls.
    public void SetGameplayMap(PlayerKind kind)
    {
        gameplayKind = kind;
        ApplyMaps();
    }

    // Called by GameManager: gameplay off means the UI map is live instead.
    public void SetGameplayEnabled(bool enabled)
    {
        if (enabled && !gameplayEnabled)
        {
            Look = Vector2.zero;
            // Discard the activation frame and the first input update after relocking.
            discardLookThroughFrame = Time.frameCount + 1;
        }
        gameplayEnabled = enabled;
        ApplyMaps();
    }

    void ApplyMaps()
    {
        if (actions == null) return;

        bool room  = gameplayEnabled && gameplayKind == PlayerKind.Room;
        bool table = gameplayEnabled && gameplayKind == PlayerKind.Table;

        Toggle(actions.Room.Get(),  room);
        Toggle(actions.Table.Get(), table);
        Toggle(actions.UI.Get(),    !gameplayEnabled);

        if (!gameplayEnabled) ClearHeldState();
    }

    static void Toggle(InputActionMap map, bool on)
    {
        if (on && !map.enabled) map.Enable();
        else if (!on && map.enabled) map.Disable();
    }

    // Disabling a map fires canceled on active actions, but clear explicitly too so a
    // held key never leaks a stale "true" into the next Explore frame.
    void ClearHeldState()
    {
        Move = Vector2.zero;
        Look = Vector2.zero;
        SprintHeld = CrouchHeld = LeanLeftHeld = LeanRightHeld = PrimaryHeld = SecondaryHeld = false;
    }
}

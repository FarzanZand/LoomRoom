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
    bool movementOnly;
    int discardLookThroughFrame = -1;

    // ── Continuous state ──────────────────────────────────────────────
    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }
    // Look comes from a stick (a held direction, turned per second) rather than the mouse (counts moved this frame).
    public bool LookIsStick { get; private set; }
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
    // While paused, Cancel goes only here (the pause menu): screens under it (figures, the map, a reveal) don't see it.
    public event Action PausedCancelPressed;
    public event Action PausePressed;    // Room or Table gameplay, or the UI map's Pause (a pad's Start): open the pause menu
    public event Action NavigatePressed; // UI: move between menu options
    public event Action SubmitPressed;   // UI: confirm or skip
    // A rebind or a reset to defaults changed the keys: anything showing key names writes them again.
    public event Action BindingsChanged;
    public bool CancelHandledThisFrame => cancelFrame == Time.frameCount;
    int cancelFrame = -1;
    // A rebind ended this frame (Escape that cancelled it must not also close the settings screen).
    public bool RebindEndedThisFrame => rebindEndFrame == Time.frameCount;
    int rebindEndFrame = -1;
    // Presses of this frame are spent (the pause menu just closed on it): pointer clicks and line skips read false.
    int heldPressFrame = -1;
    public void HoldPresses() => heldPressFrame = Time.frameCount;
    static bool Paused => GameManager.HasInstance && GameManager.Instance.State == GameState.Paused;
    bool PressesHeld => heldPressFrame == Time.frameCount || Paused;
    public event Action PrimaryPressed;
    public event Action PrimaryReleased;
    public event Action SecondaryPressed;
    public event Action SecondaryReleased;
    public event Action<int> HotbarSelected;
    public event Action<PlayerKind> DebugSwapRequested;
    public event Action DebugMoodPreviewRequested;
    public event Action DebugPixelLightingToggled;
    public event Action DebugGodModeToggled;
    public event Action DebugScreenFilterCycled;

    public PlayerInputActions Actions => actions;

    // The step the last UI Navigate press asks for (read in a NavigatePressed handler): -1 for up or left, +1 for down,
    // right or Tab (Navigate is a button bound to several keys, so its value carries no direction).
    public int NavigateStep
    {
        get
        {
            var path = actions.UI.Navigate.activeControl?.path ?? "";
            return path.EndsWith("/up") || path.EndsWith("/upArrow") || path.EndsWith("/left") || path.EndsWith("/leftArrow") ? -1 : 1;
        }
    }

    // A pad is plugged in: menus select a control as they open so its Submit works.
    public static bool PadConnected => Gamepad.current != null;
    // A pad's d-pad or an arrow key pressed: a menu with nothing selected selects something (not South, which would
    // press what was just selected).
    public static bool MenuInputStarted =>
        Gamepad.current != null && (Gamepad.current.dpad.ReadValue() != Vector2.zero || Gamepad.current.leftStick.ReadValue().sqrMagnitude > .25f)
        || Keyboard.current != null && (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.downArrowKey.wasPressedThisFrame
            || Keyboard.current.leftArrowKey.wasPressedThisFrame || Keyboard.current.rightArrowKey.wasPressedThisFrame);
    // The UI selection is empty or sits on something hidden (a menu closed with it selected): a menu selects again.
    public static bool NothingSelected
    {
        get
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events == null) return false;
            var selected = events.currentSelectedGameObject;
            return selected == null || !selected.activeInHierarchy;
        }
    }
    // Nothing of this menu is selected: the selection is empty, hidden, or on something outside it (a recap's button
    // left selected behind it), so the menu takes it.
    public static bool NothingSelectedIn(Transform menu)
    {
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events == null) return false;
        var selected = events.currentSelectedGameObject;
        return selected == null || !selected.activeInHierarchy || menu != null && !selected.transform.IsChildOf(menu);
    }
    // A text field has the keyboard (the letters typed are text, not keys for the game).
    static bool Typing
    {
        get
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            var selected = events != null ? events.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent<TMPro.TMP_InputField>(out var field) && field.isFocused;
        }
    }

    // The mouse for picking things in the world while the cursor is free (ClassFigures). Never while paused: the click
    // is for the pause menu, not what lies under it.
    public Vector2 PointerPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    public bool PointerClicked => !PressesHeld && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    public bool PointerRightClicked => !PressesHeld && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
    // Moves a Dungeon Master line on (DungeonMaster, in the Dialogue state): click, Space, Enter or gamepad South.
    public bool SkipLinePressed => !PressesHeld && (PointerClicked
        || (Keyboard.current != null && (Keyboard.current.spaceKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame))
        || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame));

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
        // The UI map is live in cutscenes, conversations and menus. A Pause action there (a pad's Start; not Escape,
        // which is Cancel) lets the pause menu open over a cutscene or a conversation. Found by name, so the map works without it.
        var uiPause = actions.UI.Get().FindAction("Pause");
        // P is a letter: typed into a text field (a name, the dev prompt over the pause menu) it is not a pause.
        if (uiPause != null) uiPause.performed += ctx => { if (!(ctx.control?.device is Keyboard && Typing)) PausePressed?.Invoke(); };
        LoadBindingOverrides();
        // Paused: the screens under the pause menu don't move or confirm.
        actions.UI.Navigate.performed += _ => { if (!Paused) NavigatePressed?.Invoke(); };
        actions.UI.Submit.performed   += _ => { if (!Paused) SubmitPressed?.Invoke(); };
        actions.UI.Inventory.performed += _ => InventoryToggled?.Invoke();
        actions.UI.Cancel.performed    += _ =>
        {
            if (CancelHandledThisFrame) return;
            cancelFrame = Time.frameCount;
            // Dismiss a nested item action menu before its underlying inventory.
            if (ContextMenuUI.HasInstance && ContextMenuUI.Instance.IsOpen) ContextMenuUI.Instance.Hide();
            else if (Paused) PausedCancelPressed?.Invoke();
            else CancelPressed?.Invoke();
        };

        // Dev keys only act in gameplay (Explore): never behind a cutscene, dialogue or menu.
        actions.Dev.Debug1.performed += _ => { if (gameplayEnabled && (!RunManager.HasInstance || !RunManager.Instance.Running || RunManager.Instance.Ended)) DebugSwapRequested?.Invoke(PlayerKind.Room); };
        actions.Dev.Debug2.performed += _ => { if (gameplayEnabled) DebugSwapRequested?.Invoke(PlayerKind.Table); };
        actions.Dev.PreviewSceneMood.performed += _ => { if (gameplayEnabled) DebugMoodPreviewRequested?.Invoke(); };
        // Look comparison, so it works anywhere, cutscenes included.
        actions.Dev.TogglePixelLighting.performed += _ => DebugPixelLightingToggled?.Invoke();
        actions.Dev.ToggleGodMode.performed += _ => DebugGodModeToggled?.Invoke();
        actions.Dev.CycleScreenFilter.performed += _ => DebugScreenFilterCycled?.Invoke();
    }

    void Bind(InputAction move, InputAction look, InputAction jump, InputAction sprint, InputAction crouch,
              InputAction interact, InputAction inventory, InputAction[] hotbar)
    {
        move.performed += ctx => Move = ctx.ReadValue<Vector2>();
        move.canceled  += _   => Move = Vector2.zero;
        look.performed += ctx =>
        {
            LookIsStick = ctx.control != null && ctx.control.device is UnityEngine.InputSystem.Gamepad;
            Look = gameplayEnabled && Time.frameCount > discardLookThroughFrame ? ctx.ReadValue<Vector2>() : Vector2.zero;
        };
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
        // The Dev map (player swap, mood preview) exists only in the Editor and development builds.
        if (Debug.isDebugBuild) actions.Dev.Enable();
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

    // Written to disk at once, so a crash doesn't lose a rebind.
    void SaveBindingOverrides()
    {
        PlayerPrefs.SetString(BindingsPref, actions.asset.SaveBindingOverridesAsJson());
        PlayerPrefs.Save();
    }

    public void ResetBindings()
    {
        actions.asset.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(BindingsPref);
        PlayerPrefs.Save();
        BindingsChanged?.Invoke();
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

    // Keys are named by the key itself ("Space", "Left Shift"), not by the Windows keyboard language (which would say
    // "Blanksteg" on a Swedish layout); mouse buttons keep their short names (LMB, RMB).
    public string BindingDisplay(string actionName, string part = null)
    {
        var action = TableAction(actionName) ?? RoomAction(actionName);
        int index = FindBinding(action, part);
        if (index < 0) return "-";
        string path = action.bindings[index].effectivePath ?? "";
        if (path.StartsWith("<Keyboard>"))
            return InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        return action.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions);
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
                BindingsChanged?.Invoke();
            })
            .OnCancel(_ => Finish());
        rebind.Start();

        void Finish()
        {
            rebind?.Dispose();
            rebind = null;
            if (wasEnabled) action.Enable();
            // The UI map sees the pressed key too; do not let it close the menu.
            cancelFrame = rebindEndFrame = Time.frameCount;
            onDone?.Invoke();
        }
    }

    public void CancelRebind() => rebind?.Cancel();

    // Also onto the UI map's action of that name (Inventory), which closes the menu while it is open.
    void Mirror(InputAction source, int index)
    {
        var binding = source.bindings[index];
        var map = source.actionMap == actions.Table.Get() ? actions.Room.Get() : actions.Table.Get();
        MirrorOnto(map.FindAction(source.name), binding);
        MirrorOnto(actions.UI.Get().FindAction(source.name), binding);
    }

    static void MirrorOnto(InputAction other, InputBinding binding)
    {
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
    // allowMovement keeps walking (Move, Sprint) live while gameplay is off, e.g. with the inventory open.
    public void SetGameplayEnabled(bool enabled, bool allowMovement = false)
    {
        movementOnly = !enabled && allowMovement;
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

        if (movementOnly)
        {
            var map = gameplayKind == PlayerKind.Table ? actions.Table.Get() : actions.Room.Get();
            map.FindAction("Move").Enable();
            map.FindAction("Sprint").Enable();
        }

        if (!gameplayEnabled) ClearHeldState(movementOnly);
    }

    // Unconditional: map.enabled is already true when only Move is live (movementOnly).
    static void Toggle(InputActionMap map, bool on)
    {
        if (on) map.Enable();
        else map.Disable();
    }

    // Disabling a map fires canceled on active actions, but clear explicitly too so a
    // held key never leaks a stale "true" into the next Explore frame.
    void ClearHeldState(bool keepMovement = false)
    {
        if (!keepMovement) { Move = Vector2.zero; SprintHeld = false; }
        Look = Vector2.zero;
        CrouchHeld = LeanLeftHeld = LeanRightHeld = PrimaryHeld = SecondaryHeld = false;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// The one owner of "what mode is the game in". Cursor lock, which input map is live,
// and whether the HUD shows are all decided here and nowhere else — menus, dialogue
// and cutscenes push a state and pop it when done instead of each toggling the cursor.
//
// States stack: opening the inventory during Explore pushes Inventory; closing pops back.
// A cutscene that starts a conversation pushes Dialogue on top of Cutscene and the
// cutscene state is restored when the conversation ends.
public enum GameState
{
    Explore   = 0,   // player in control
    Menu      = 1,   // level selection or another full-screen menu, cursor free
    Dialogue  = 2,   // Pixel Crushers conversation running, cursor free
    Cutscene  = 3,   // timeline or scripted sequence, no input, HUD hidden
    Dead      = 4,   // player died, no input
    Inventory = 5,   // character menu: the world keeps running; the player can walk but not look or fight
    Paused    = 6,   // pause menu: time stopped, cursor free
}

public class GameManager : Singleton<GameManager>
{
    [Tooltip("Hide the HUD canvas while a cutscene plays.")]
    [SerializeField] bool hideHudInCutscenes = true;

    readonly List<GameState> stack = new();
    readonly Dictionary<GameState, int> owners = new();

    // Real time that stands still while the pause menu is open: cutscenes run on real time (they go on under slow
    // motion), and the pause menu can open over them.
    // Co-op: the game goes on for the others under the pause menu, so a cutscene (a table reveal, the shared intro) does too.
    public static float CutsceneDelta => HasInstance && Instance.State == GameState.Paused && !Coop.Running ? 0f : Time.unscaledDeltaTime;
    public static IEnumerator WaitCutscene(float seconds) { for (float t = 0f; t < seconds; t += CutsceneDelta) yield return null; }

    public GameState State => stack.Count > 0 ? stack[stack.Count - 1] : GameState.Explore;
    public bool GameplayActive => State == GameState.Explore;
    // Enemies, regen and projectiles keep running while the inventory is open.
    public bool SimulationActive => State == GameState.Explore || State == GameState.Inventory;

    public event Action<bool>      HudVisibilityChanged;

    // A scripted moment that wants the HUD in view (resting by the tavern fire shows the health bar fill) keeps it shown
    // through a Cutscene: counted like the states, every true needs its false from the same owner.
    int hudKept;
    public void KeepHud(bool keep) { hudKept = Mathf.Max(0, hudKept + (keep ? 1 : -1)); Apply(); }

    void Start() => Apply();

    // The cursor is held to the state every frame, not only when the state changes: losing focus, Escape in the
    // editor's Game view or a plugin can free it behind our back, and it then showed while walking until a click.
    // Written only when it differs, so it never fights itself (which made it flicker).
    bool cursorLocked = true;
    void Update() => HoldCursor();
    void OnApplicationFocus(bool focused) { if (focused) HoldCursor(); }

    void HoldCursor()
    {
        if (!Application.isFocused) return;
        var mode = cursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        if (Cursor.visible == cursorLocked) Cursor.visible = !cursorLocked;
    }

    // Dead is a flag, not a count: level loads clear it without knowing whether anyone died.
    static bool Counted(GameState state) => state != GameState.Dead;

    // Enter a state on top of whatever is running. States are reference-counted: every
    // Push needs its own Pop, and the state stays active until the last owner pops it.
    public void Push(GameState state)
    {
        if (state == GameState.Explore) return;
        owners.TryGetValue(state, out int count);
        if (count > 0)
        {
            if (Counted(state)) owners[state] = count + 1;
            return;
        }
        owners[state] = 1;
        stack.Add(state);
        Apply();
    }

    // Leave a state. Removes it wherever it sits in the stack once no owner is left, so
    // callers never have to worry about ordering.
    public void Pop(GameState state)
    {
        owners.TryGetValue(state, out int count);
        if (count <= 0)
        {
            if (Counted(state)) Debug.LogWarning($"[GameManager] Pop({state}) without a matching Push.", this);
            return;
        }
        if (Counted(state) && count > 1)
        {
            owners[state] = count - 1;
            return;
        }
        owners[state] = 0;
        stack.Remove(state);
        Apply();
    }

    void Apply()
    {
        GameState s = State;
        cursorLocked = s == GameState.Explore || s == GameState.Cutscene || s == GameState.Dead;
        Cursor.lockState = cursorLocked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !cursorLocked;

        if (InputManager.HasInstance)
            InputManager.Instance.SetGameplayEnabled(s == GameState.Explore, allowMovement: s == GameState.Inventory);

        bool hudVisible = !(hideHudInCutscenes && s == GameState.Cutscene && hudKept == 0);
        HudVisibilityChanged?.Invoke(hudVisible);
    }
}

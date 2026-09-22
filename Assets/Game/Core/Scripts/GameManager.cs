using System;
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
    Explore  = 0,   // player in control
    Menu     = 1,   // level selection or another full-screen menu, cursor free
    Dialogue = 2,   // Pixel Crushers conversation running, cursor free
    Cutscene = 3,   // timeline or scripted sequence, no input, HUD hidden
    Inventory = 5, // live inventory: AI and simulation continue; player input is disabled
    Dead     = 4,   // player died, no input
}

public class GameManager : Singleton<GameManager>
{
    [Tooltip("Hide the HUD canvas while a cutscene plays.")]
    [SerializeField] bool hideHudInCutscenes = true;

    readonly List<GameState> stack = new();

    public GameState State => stack.Count > 0 ? stack[stack.Count - 1] : GameState.Explore;
    public bool GameplayActive => State == GameState.Explore;
    public bool SimulationActive => State == GameState.Explore || State == GameState.Inventory;
    public bool IsOpen(GameState state) => stack.Contains(state);

    public event Action<GameState> StateChanged;
    public event Action<bool>      HudVisibilityChanged;

    void Start() => Apply();

    // Enter a state on top of whatever is running. Pushing the same state twice is a no-op
    // so two systems that both open "Menu" don't need to coordinate.
    public void Push(GameState state)
    {
        if (state == GameState.Explore) return;
        if (stack.Contains(state)) return;
        stack.Add(state);
        Apply();
    }

    // Leave a state. Removes it wherever it sits in the stack so callers never have to
    // worry about ordering (a dialogue closing under an open menu just disappears).
    public void Pop(GameState state)
    {
        if (!stack.Remove(state)) return;
        Apply();
    }

    public void PopAll()
    {
        if (stack.Count == 0) return;
        stack.Clear();
        Apply();
    }

    void Apply()
    {
        GameState s = State;

        bool locked = s == GameState.Explore || s == GameState.Cutscene || s == GameState.Dead;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible   = !locked;

        if (InputManager.HasInstance)
            InputManager.Instance.SetGameplayEnabled(s == GameState.Explore);

        bool hudVisible = !(hideHudInCutscenes && s == GameState.Cutscene);
        HudVisibilityChanged?.Invoke(hudVisible);
        StateChanged?.Invoke(s);
    }
}

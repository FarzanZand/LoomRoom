#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

// § (the key left of 1; ` on a US keyboard), Editor and development builds only: a console for testing.
//   god [on|off]    no damage, health, mana and stamina kept full (same as F8)
//   ghost [on|off]  fly through walls along the view (sprint is faster), enemies don't see you; off puts you back on
//                   the nearest floor
//   revive          back on your feet after dying; in a run the run goes on. Once the console has been opened, a death
//                   waits for the recap before it is banked (and while the console is open it waits on), so a revive
//                   before then never counts; normal play banks it on the frame of death as always
//   help, clear
// While it is open the game is in the Menu state, so typing never moves the player or fires hotbar keys.
// The window is Development/Resources/Dev Console.prefab (Tools > LoomRoom > Dev > Build Dev Console; edit it freely).
public class DevConsole : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] TMP_InputField input;
    [SerializeField] TMP_Text log;
    [SerializeField, Min(4)] int lines = 14;

    readonly List<string> history = new();
    bool open;
    int openedFrame = -10;
    Vector3 ghostFrom;   // where the player stood when ghost went on: the way back if nothing below is reachable

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        RunManager.DeferDeath = RunManager.HoldDeath = false;
        if (!Debug.isDebugBuild || FindAnyObjectByType<DevConsole>() != null) return;
        var prefab = Resources.Load<GameObject>("Dev Console");
        if (prefab == null) { Debug.LogWarning("[DevConsole] Development/Resources/Dev Console.prefab is missing."); return; }
        var go = Instantiate(prefab);
        go.name = "Dev Console";
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        panel.SetActive(false);
        // The key that opens it must not end up in the line, whatever it types on this keyboard layout: nothing typed on
        // the frame it opens counts, nor the characters that key gives on common layouts.
        input.onValidateInput = (text, index, c) =>
            Time.frameCount <= openedFrame + 1 || c == '§' || c == '½' || c == '`' || c == '~' || c == '|' ? '\0' : c;
        input.onSubmit.AddListener(Submit);
        Print("Type help for the commands.");
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.backquoteKey.wasPressedThisFrame) { if (open) Close(); else Open(); }
        else if (open && keyboard.escapeKey.wasPressedThisFrame) Close();
    }

    void Open()
    {
        open = true;
        openedFrame = Time.frameCount;
        RunManager.DeferDeath = RunManager.HoldDeath = true;
        panel.SetActive(true);
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        input.text = "";
        input.ActivateInputField();
    }

    void Close()
    {
        open = false;
        RunManager.HoldDeath = false;
        input.DeactivateInputField();
        panel.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void OnDestroy()
    {
        if (open && GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        if (open) RunManager.HoldDeath = false;
    }

    void Submit(string text)
    {
        if (!open) return;
        text = text.Trim();
        input.text = "";
        input.ActivateInputField();
        if (text.Length == 0) return;
        Print("> " + text);
        Run(text);
    }

    void Run(string line)
    {
        var parts = line.ToLowerInvariant().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        string command = parts[0], arg = parts.Length > 1 ? parts[1] : null;
        switch (command)
        {
            case "help":
                Print("god [on|off]: no damage, health, mana and stamina kept full");
                Print("ghost [on|off]: fly through walls, enemies don't see you");
                Print("revive: back on your feet after dying");
                Print("clear: empty this window");
                break;
            case "god":
                DevGodMode.Set(Switch(arg, DevGodMode.On));
                Print(DevGodMode.On ? "God mode on." : "God mode off.");
                break;
            case "ghost":
                Ghost(arg);
                break;
            case "revive":
                Revive();
                break;
            case "clear":
                history.Clear();
                Refresh();
                break;
            default:
                Print($"Unknown command: {command}. Type help.");
                break;
        }
    }

    static bool Switch(string arg, bool current) => arg == "on" ? true : arg == "off" ? false : !current;

    static Player Active => PlayerManager.HasInstance ? PlayerManager.Instance.Active : null;

    void Ghost(string arg)
    {
        var player = Active;
        if (player == null) { Print("No player."); return; }
        bool on = Switch(arg, player.Ghost);
        if (on == player.Ghost) { Print(on ? "Already a ghost." : "Not a ghost."); return; }
        if (on)
        {
            ghostFrom = player.transform.position;
            player.Ghost = true;
            Print("Ghost on: fly where you look, through walls. Enemies don't see you.");
            return;
        }
        player.Ghost = false;
        player.Warp(LandingFor(player.transform.position));
        Print("Ghost off.");
    }

    // Where a ghost lands: the first floor straight below that can be walked to from where ghost went on (so never the
    // top of a ceiling or a closed-off pocket), else where ghost went on.
    Vector3 LandingFor(Vector3 at)
    {
        bool haveStart = NavMesh.SamplePosition(ghostFrom, out var start, 3f, NavMesh.AllAreas);
        var hits = Physics.RaycastAll(at + Vector3.up * .5f, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
        var path = new NavMeshPath();
        foreach (var h in hits)
        {
            if (!NavMesh.SamplePosition(h.point, out var spot, 1.5f, NavMesh.AllAreas)) continue;
            if (!haveStart) return spot.position;
            if (NavMesh.CalculatePath(start.position, spot.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                return spot.position;
        }
        return ghostFrom;
    }

    void Revive()
    {
        var player = Active;
        if (player == null) { Print("No player."); return; }
        if (player.IsAlive) { Print("You're not dead."); return; }
        bool counted = false;
        bool run = RunManager.HasInstance && RunManager.Instance.ReviveRun(out counted);
        player.Stats.Revive();
        var animator = player.Animator;
        if (animator != null && Character.HasParameter(animator, "Dead", AnimatorControllerParameterType.Bool)) animator.SetBool("Dead", false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
        Print(!run ? "Revived." : counted
            ? "Revived. The run goes on, but that death was already counted: it is in the save and the run's spoils were banked."
            : "Revived. The run goes on; the death was not counted.");
    }

    void Print(string line)
    {
        history.Add(line);
        if (history.Count > 200) history.RemoveRange(0, history.Count - 200);
        Refresh();
    }

    void Refresh()
    {
        if (log == null) return;
        int from = Mathf.Max(0, history.Count - lines);
        log.text = string.Join("\n", history.GetRange(from, history.Count - from));
    }
}
#endif

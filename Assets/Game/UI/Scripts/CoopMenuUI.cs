using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The co-op screen (authored in the scene under the HUD canvas; Tools > LoomRoom > Multiplayer > Set Up Co-op builds it).
// From the pause menu's Co-op button: host a game, join one by address, leave, and the host's rules. At the start of
// a new game (IntroController asks MultiplayerManager.NewGameLobby): play alone, host (Begin starts the intro for
// everyone who has joined) or join someone's game.
public class CoopMenuUI : MonoBehaviour
{
    public enum Mode { Pause, NewGame }

    [SerializeField] GameObject root;
    [SerializeField] TMP_Text title, status, playerList;
    [SerializeField] TMP_InputField address;
    [SerializeField] Button hostButton, joinButton, leaveButton, beginButton, aloneButton, closeButton;
    [SerializeField] Button sharedLootButton, hostAdventurerButton, partyProgressionButton;

    public static CoopMenuUI Instance { get; private set; }
    public bool IsOpen => root != null && root.activeSelf;
    public Mode Current { get; private set; }
    // A new game's choice: true once the host pressed Begin (or the player chose to play alone).
    public bool Began { get; private set; }

    bool pushed;

    void Awake()
    {
        Instance = this;
        if (root != null) root.SetActive(false);
        Wire(hostButton, () => { if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.Host(); });
        Wire(joinButton, () => { if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.Join(address != null ? address.text : ""); });
        Wire(leaveButton, () => { if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.Leave(); });
        Wire(beginButton, () => { Began = true; Close(); });
        Wire(aloneButton, () => { if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.Leave(); Began = true; Close(); });
        Wire(closeButton, Close);
        Wire(sharedLootButton, () => Toggle(ref MultiplayerManager.Instance.sharedLoot));
        Wire(hostAdventurerButton, () => Toggle(ref MultiplayerManager.Instance.hostAdventurer));
        Wire(partyProgressionButton, () => Toggle(ref MultiplayerManager.Instance.partyProgression));
    }

    void Start() { if (InputManager.HasInstance) InputManager.Instance.CancelPressed += OnCancel; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= OnCancel;
        Unpush();
    }

    void OnCancel() { if (IsOpen && Current == Mode.Pause && (address == null || !address.isFocused)) Close(); }

    static void Wire(Button button, UnityEngine.Events.UnityAction action) { if (button != null) button.onClick.AddListener(action); }

    void Toggle(ref bool rule)
    {
        if (!MultiplayerManager.HasInstance || (Coop.Running && !Coop.IsHost)) return;
        rule = !rule;
        MultiplayerManager.Instance.RulesChanged();
    }

    public void Open(Mode mode)
    {
        if (root == null) return;
        Current = mode; Began = false;
        root.SetActive(true);
        if (!pushed && GameManager.HasInstance) { GameManager.Instance.Push(GameState.Menu); pushed = true; }
        Refresh();
    }

    public void Close()
    {
        if (root != null) root.SetActive(false);
        Unpush();
    }

    void Unpush()
    {
        if (!pushed) return;
        pushed = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
    }

    void Update()
    {
        if (!IsOpen) return;
        Refresh();
        // A new game joined to someone else's: their game decides what happens next.
        if (Current == Mode.NewGame && Coop.IsGuest) { Began = false; Close(); }
    }

    void Refresh()
    {
        bool running = Coop.Running, host = Coop.IsHost;
        var manager = MultiplayerManager.HasInstance ? MultiplayerManager.Instance : null;
        if (title != null) title.text = Current == Mode.NewGame ? "NEW GAME" : "CO-OP";
        if (status != null)
            status.text = manager != null && manager.Starting ? "Connecting..."
                : !running ? (Current == Mode.NewGame ? "Play alone, host a game for friends, or join one with its code." : "Not in a game. Join with a code or an address.")
                : host ? (!string.IsNullOrEmpty(manager.JoinCode) ? $"Join code: {manager.JoinCode}  (copied)" : $"Others join with {MultiplayerManager.LocalAddress()}, port {manager.port}.")
                : "In someone else's game.";
        if (playerList != null)
        {
            var text = new System.Text.StringBuilder();
            if (manager != null && running)
                foreach (var p in manager.Players) if (p != null) text.AppendLine(p.DisplayName + (p.IsOwner ? " (you)" : ""));
            playerList.text = text.ToString();
        }
        bool starting = manager != null && manager.Starting;
        Show(hostButton, !running && !starting);
        Show(joinButton, !running && !starting);
        if (address != null) address.gameObject.SetActive(!running && !starting);
        Show(leaveButton, running && Current == Mode.Pause);
        Show(beginButton, Current == Mode.NewGame && host);
        Show(aloneButton, Current == Mode.NewGame);
        Show(closeButton, Current == Mode.Pause);
        bool rules = manager != null && (!running || host);
        Rule(sharedLootButton, rules, "Loot", manager != null && manager.sharedLoot ? "shared" : "each their own");
        Rule(hostAdventurerButton, rules, "Adventurer", manager != null && manager.hostAdventurer ? "the host's" : "each their own");
        Rule(partyProgressionButton, rules, "Progress saved", manager != null && manager.partyProgression ? "for everyone" : "for the host");
    }

    static void Show(Button button, bool on) { if (button != null) button.gameObject.SetActive(on); }

    static void Rule(Button button, bool on, string name, string value)
    {
        if (button == null) return;
        button.gameObject.SetActive(on);
        var label = button.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = $"{name}: {value}";
    }
}

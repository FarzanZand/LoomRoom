using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The co-op screen (authored in the scene under the HUD canvas; Tools > LoomRoom > Multiplayer > Rebuild Co-op Screen
// builds it). Three pages: Start (your name, host a game, or join one with its code), Your game (the host: the join
// code, who is in, the rules) and someone else's game (a guest: who is in, the host's rules). Opened from the pause
// menu's Co-op button, or at the start of a new game (IntroController asks MultiplayerManager.NewGameLobby): there the
// host's Begin starts the intro for everyone who has joined, and Play alone starts it alone.
public class CoopMenuUI : MonoBehaviour
{
    public enum Mode { Pause, NewGame }

    [SerializeField] GameObject root;
    [SerializeField] TMP_Text title, status;
    [Tooltip("The status line's colour for something that went wrong (a bad code, a refusal).")]
    [SerializeField] Color errorColor = new Color(.92f, .45f, .4f);

    [Header("Start page")]
    [SerializeField] GameObject startPage;
    [Tooltip("This player's name over their head (kept).")]
    [SerializeField] TMP_InputField playerName;
    [SerializeField] TMP_InputField address;
    [SerializeField] Button hostButton, joinButton;

    [Header("Game page (host and guests)")]
    [SerializeField] GameObject gamePage;
    [Tooltip("The join code with its caption and Copy button: the host's only.")]
    [SerializeField] GameObject codeGroup;
    [SerializeField] TMP_Text codeCaption, codeText, playersHeader, playerList;
    [SerializeField] Button copyButton;
    [SerializeField] GameObject hostRules;
    [SerializeField] Button sharedLootButton, hostAdventurerButton, partyProgressionButton, sharedSpoilsButton;
    [Tooltip("A guest's view of the host's rules.")]
    [SerializeField] TMP_Text guestRules;

    [Header("Bottom row")]
    [SerializeField] Button beginButton;
    [SerializeField] Button aloneButton, leaveButton, closeButton;
    [SerializeField] float bottomY = -300f, bottomSpread = 145f;

    public static CoopMenuUI Instance { get; private set; }
    public bool IsOpen => root != null && root.activeSelf;
    public Mode Current { get; private set; }
    // A new game's choice: true once the host pressed Begin (or the player chose to play alone).
    public bool Began { get; private set; }

    enum Page { None, Start, Host, Guest }
    Page shown;
    bool pushed;
    Color statusColor;
    float copiedUntil;

    void Awake()
    {
        Instance = this;
        if (root != null) root.SetActive(false);
        if (status != null) statusColor = status.color;
        // Hosting from here opens the lobby: friends who join wait until Begin.
        Wire(hostButton, () => { if (MultiplayerManager.HasInstance) { MultiplayerManager.Instance.Host(); StartCoroutine(LobbyWhenHosting()); } });
        Wire(joinButton, Join);
        // Enter in the code box joins.
        if (address != null) address.onSubmit.AddListener(_ => { if (!Coop.Running && joinButton != null && joinButton.interactable) Join(); });
        Wire(leaveButton, () => { if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.Leave(); });
        Wire(beginButton, Begin);
        Wire(aloneButton, () => { if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.Leave(); Began = true; Close(); });
        Wire(closeButton, Close);
        Wire(copyButton, Copy);
        if (playerName != null)
        {
            if (MultiplayerManager.HasInstance) playerName.text = MultiplayerManager.Instance.playerName;
            playerName.onEndEdit.AddListener(n => { if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.SetName(n); });
        }
        Wire(sharedLootButton, () => Toggle(ref MultiplayerManager.Instance.sharedLoot));
        Wire(hostAdventurerButton, () => Toggle(ref MultiplayerManager.Instance.hostAdventurer));
        Wire(partyProgressionButton, () => Toggle(ref MultiplayerManager.Instance.partyProgression));
        Wire(sharedSpoilsButton, () => Toggle(ref MultiplayerManager.Instance.sharedSpoils));
    }

    void Start() { if (InputManager.HasInstance) InputManager.Instance.CancelPressed += OnCancel; }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= OnCancel;
        Unpush();
    }

    System.Collections.IEnumerator LobbyWhenHosting()
    {
        var manager = MultiplayerManager.Instance;
        for (float t = 0; t < 20f && manager != null && !Coop.IsHost; t += Time.unscaledDeltaTime) yield return null;
        if (manager != null && Coop.IsHost) manager.OpenLobby();
    }

    // The lobby can't be left with Esc: the host begins or stops hosting, a waiting guest leaves.
    static bool InLobby => MultiplayerManager.HasInstance && (MultiplayerManager.Instance.HostInLobby || MultiplayerManager.Instance.LocalInLobby);

    void OnCancel() { if (IsOpen && Current == Mode.Pause && !Typing && !InLobby) Close(); }

    bool Typing => (address != null && address.isFocused) || (playerName != null && playerName.isFocused);

    static void Wire(Button button, UnityEngine.Events.UnityAction action) { if (button != null) button.onClick.AddListener(action); }

    void Join()
    {
        if (!MultiplayerManager.HasInstance) return;
        if (playerName != null) MultiplayerManager.Instance.SetName(playerName.text);
        // Nothing typed: a code copied from a chat will do.
        if (address != null && string.IsNullOrWhiteSpace(address.text) && ClipboardCode(out var copied)) address.text = copied;
        MultiplayerManager.Instance.Join(address != null ? address.text : "");
    }

    void Copy()
    {
        var manager = MultiplayerManager.HasInstance ? MultiplayerManager.Instance : null;
        if (manager == null) return;
        GUIUtility.systemCopyBuffer = !string.IsNullOrEmpty(manager.JoinCode) ? manager.JoinCode : MultiplayerManager.LocalAddress();
        copiedUntil = Time.unscaledTime + 2f;
    }

    void Toggle(ref bool rule)
    {
        if (!MultiplayerManager.HasInstance || (Coop.Running && !Coop.IsHost)) return;
        rule = !rule;
        MultiplayerManager.Instance.RulesChanged();
    }

    // The host's Begin: everyone who has joined plays the new game's intro.
    public void Begin() { Began = true; if (MultiplayerManager.HasInstance && Coop.IsHost) MultiplayerManager.Instance.BeginForEveryone(); Close(); }

    // A join code on the clipboard (six letters and digits, as the host's Copy gives): the code box fills itself in.
    static bool ClipboardCode(out string code)
    {
        code = (GUIUtility.systemCopyBuffer ?? "").Trim().ToUpperInvariant();
        if (code.Length != 6) return false;
        foreach (var c in code) if (!char.IsLetterOrDigit(c)) return false;
        return true;
    }

    void FillCodeFromClipboard()
    {
        if (address == null || Coop.Running || address.isFocused || !string.IsNullOrWhiteSpace(address.text)) return;
        // Not this game's own code (a host who left keeps it on their clipboard).
        if (ClipboardCode(out var code) && code != (MultiplayerManager.HasInstance ? MultiplayerManager.Instance.JoinCode : null)) address.text = code;
    }

    void OnApplicationFocus(bool focus) { if (focus && IsOpen) FillCodeFromClipboard(); }

    public void Open(Mode mode)
    {
        if (root == null) return;
        Current = mode; Began = false; shown = Page.None;
        root.SetActive(true);
        if (playerName != null && MultiplayerManager.HasInstance) playerName.text = MultiplayerManager.Instance.playerName;
        FillCodeFromClipboard();
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
        // (A guest who joined the host's lobby waits here for the host's Begin.)
        if (Current == Mode.NewGame && Coop.IsGuest && !MultiplayerManager.Instance.LocalInLobby) { Began = false; Close(); }
    }

    void Refresh()
    {
        var manager = MultiplayerManager.HasInstance ? MultiplayerManager.Instance : null;
        if (manager == null) return;
        bool running = Coop.Running, host = Coop.IsHost, starting = manager.Connecting;
        var page = !running ? Page.Start : host ? Page.Host : Page.Guest;
        bool newGame = Current == Mode.NewGame;
        var hostPlayer = manager.PlayerInSlot(0);
        string hostName = hostPlayer != null ? hostPlayer.DisplayName : "the host";

        if (startPage != null) startPage.SetActive(page == Page.Start);
        if (gamePage != null) gamePage.SetActive(page != Page.Start);
        if (title != null) title.text = page == Page.Host ? "YOUR GAME" : page == Page.Guest ? $"{hostName.ToUpperInvariant()}'S GAME" : newGame ? "NEW GAME" : "CO-OP";

        // The status line: what went wrong, what is happening, or what to do.
        bool error = page == Page.Start && !starting && !string.IsNullOrEmpty(manager.LastError);
        // A solo run in progress can't be brought into co-op, and joining would leave it running behind someone's game.
        bool soloRun = !running && RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended;
        if (status != null)
        {
            status.color = error ? errorColor : statusColor;
            status.text = error ? manager.LastError
                : starting ? "Connecting..."
                : page == Page.Start && soloRun ? "Finish or leave your run to play together."
                : page == Page.Start ? (newGame ? "Start the new game alone, or play it with friends." : "Play this game with friends, or join theirs.")
                : page == Page.Host ? (manager.Players.Count <= 1 ? "Send the code to your friends." : newGame || manager.HostInLobby ? "Press Begin when everyone is in." : "Sit down at the table together to start a run.")
                : manager.LocalInLobby ? $"Waiting for {hostName} to begin."
                : $"You are in {hostName}'s game. The host decides the rules.";
        }
        if (hostButton != null) hostButton.interactable = !starting && !soloRun;
        if (joinButton != null) joinButton.interactable = !starting && !soloRun;

        if (page != Page.Start)
        {
            if (codeGroup != null) codeGroup.SetActive(page == Page.Host);
            bool relay = !string.IsNullOrEmpty(manager.JoinCode);
            if (codeCaption != null) codeCaption.text = relay ? "JOIN CODE" : "YOUR ADDRESS (SAME NETWORK)";
            if (codeText != null) codeText.text = relay ? manager.JoinCode : MultiplayerManager.LocalAddress();
            if (copyButton != null) SetLabel(copyButton, Time.unscaledTime < copiedUntil ? "Copied" : "Copy");
            if (playersHeader != null) playersHeader.text = $"PLAYERS  {manager.Players.Count}/{manager.maxPlayers}";
            if (playerList != null)
            {
                var text = new System.Text.StringBuilder();
                foreach (var p in manager.Players)
                    if (p != null) text.AppendLine(p.DisplayName + (p.IsOwner ? " (you)" : p.SlotIndex == 0 ? " (host)" : ""));
                playerList.text = text.ToString();
            }
            if (hostRules != null) hostRules.SetActive(page == Page.Host);
            if (guestRules != null)
            {
                guestRules.gameObject.SetActive(page == Page.Guest);
                if (page == Page.Guest) guestRules.text = $"{LootRule(manager)}\n{AdventurerRule(manager)}\n{ProgressRule(manager)}\n{SpoilsRule(manager)}";
            }
            SetLabel(sharedLootButton, LootRule(manager));
            SetLabel(hostAdventurerButton, AdventurerRule(manager));
            SetLabel(partyProgressionButton, ProgressRule(manager));
            SetLabel(sharedSpoilsButton, SpoilsRule(manager));
        }

        // The bottom row: one centred button, or two side by side.
        Button left, right = null;
        if (page == Page.Start) left = newGame ? aloneButton : closeButton;
        else if (page == Page.Host) { bool lobby = newGame || manager.HostInLobby; left = lobby ? beginButton : closeButton; right = newGame ? aloneButton : leaveButton; }
        else if (manager.LocalInLobby) { left = leaveButton; }
        else { left = closeButton; right = leaveButton; }
        foreach (var b in new[] { beginButton, aloneButton, leaveButton, closeButton })
            if (b != null) b.gameObject.SetActive(b == left || b == right);
        Place(left, right != null ? -bottomSpread : 0f);
        Place(right, bottomSpread);
        SetLabel(leaveButton, page == Page.Host ? "Stop hosting" : "Leave the game");

        // A new page: the keyboard and gamepad start on its main button.
        if (page != shown)
        {
            shown = page;
            var first = page == Page.Start ? hostButton : page == Page.Host ? (newGame ? beginButton : copyButton) : manager.LocalInLobby ? leaveButton : closeButton;
            if (first != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first.gameObject);
        }
    }

    void Place(Button button, float x)
    {
        if (button == null) return;
        ((RectTransform)button.transform).anchoredPosition = new Vector2(x, bottomY);
    }

    static string LootRule(MultiplayerManager m) => m.Rules.sharedLoot ? "Loot: shared by everyone" : "Loot: everyone finds their own";
    static string AdventurerRule(MultiplayerManager m) => m.Rules.hostAdventurer ? "Classes: everyone uses the host's" : "Classes: everyone brings their own";
    static string SpoilsRule(MultiplayerManager m) => m.Rules.sharedSpoils ? "Spoils: split between everyone" : "Spoils: everyone keeps their own";
    static string ProgressRule(MultiplayerManager m) => m.Rules.partyProgression ? "Progress saved: for everyone" : "Progress saved: for the host only";

    static void SetLabel(Button button, string text)
    {
        if (button == null) return;
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null && label.text != text) label.text = text;
    }
}

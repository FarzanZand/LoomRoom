using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Co-op for up to four players. One player hosts (their room, their save); the others join and walk the same room,
// sit at the same table and go down into the same dungeon. Owns the session (host, join, leave), the host's rules
// for it, the lobby, and the run: who is at the table, when the run starts, the stairs, a dead player as a ghost,
// and the end. The floor itself is CoopWorld's; each player is a CoopPlayer.
[DefaultExecutionOrder(-800)]
public class MultiplayerManager : Singleton<MultiplayerManager>
{
    [Title("Session")]
    [Tooltip("The scene's NetworkManager (a root object of its own).")]
    public NetworkManager network;
    public string address = "127.0.0.1";
    public ushort port = 7777;
    [Range(2, 4)] public int maxPlayers = 4;
    [Tooltip("Shown over this player's head. Empty: \"Player 1\", \"Player 2\"...")]
    public string playerName = "";
    [Tooltip("Host online through Unity Relay: the others join with a short code, no ports to open. Off, or when Relay can't be reached: they join with this computer's address (port above).")]
    public bool online = true;

    [Title("Rules", "The host's apply to everyone")]
    [Tooltip("On: the party shares one lot of loot, taken once, and gold anyone picks up goes to everyone. Off: each player sees and rolls their own loot.")]
    public bool sharedLoot = false;
    [Tooltip("On: everyone plays the host's adventurer (the host's unlocked classes and story). Off: each player brings their own.")]
    public bool hostAdventurer = false;
    [Tooltip("Off: only the host's save gains progression (unlocks, deepest floor). On: every player's own save gains what they earned too.")]
    public bool partyProgression = false;
    [Tooltip("On: the spoils the party carries out and earns from the health slain are pooled and split evenly at the run's end. Off: everyone keeps their own. Either way each player banks into their own spoils, whatever the class rule says.")]
    public bool sharedSpoils = true;

    [Title("Dead players", "A ghost on the floor until the party reaches the next one")]
    [Tooltip("The pale light a ghost carries (it lights the way for everyone).")]
    public Color ghostLightColor = new Color(.6f, .8f, 1f);
    [Min(0)] public float ghostLightIntensity = 2.2f;
    [Min(.5f)] public float ghostLightRange = 6f;
    [Tooltip("How much faster a ghost walks and runs than the living (1.6 = 60% faster).")]
    [Min(1)] public float ghostSpeed = 1.6f;
    [Tooltip("The ghost's own view: a post-processing profile laid over everything while it is a ghost (Characters/Players/Settings/Ghost look: cold, drained, a dark blue vignette, grain and colour fringes).")]
    public UnityEngine.Rendering.VolumeProfile ghostLook;
    [Tooltip("Seconds the ghost look takes to come in.")]
    [Min(0)] public float ghostLookFade = .8f;
    [Tooltip("The see-through look of a ghost's body for the others (a transparent material).")]
    public Material ghostMaterial;

    [Title("Party")]
    [Tooltip("Enemy health added per player after the first (.5 = +50%).")]
    [Min(0)] public float enemyHealthPerPlayer = .5f;
    [Tooltip("Enemies added to a pack per player after the first (0: the same packs as alone).")]
    [Min(0)] public int extraEnemiesPerPlayer = 0;
    [Tooltip("XP a player gets for another player's kill, against the killer's.")]
    [Range(0, 1)] public float partyKillExperience = .5f;
    [Tooltip("Seconds the stairs wait for the others once someone reaches them.")]
    [Min(0)] public float descendCountdown = 10f;
    [Tooltip("Players this close to the stairs (metres) count as there.")]
    [Min(.5f)] public float stairsRadius = 5f;
    [Tooltip("Sideways step between players looking at the figures (room units).")]
    public float figureSpacing = 16f;
    [Tooltip("Metres between players where a floor starts.")]
    public float spawnSpacing = .9f;

    [Title("Bodies", "What the others look like. A class's own body is on its AdventurerClass (Co-op body)")]
    [Tooltip("Room bodies by player slot (repeats).")]
    public GameObject[] roomBodies = new GameObject[0];
    [Tooltip("The table body for a class without its own.")]
    public GameObject defaultTableBody;
    [Tooltip("Animator for every body (the Humanoid controller: locomotion, attack, cast, hurt, death).")]
    public RuntimeAnimatorController bodyController;
    public EnemyWeaponGrip weaponGrip, shieldGrip;
    [Tooltip("An extra turn of the shield while blocking, on top of Shield Grip (the block pose raises the arm; zero keeps the grip).")]
    public Vector3 shieldBlockTurn = Vector3.zero;
    [Tooltip("The room is built about this many units to the metre (the room player's scale).")]
    public float roomScale = 20f;
    [Tooltip("Enemies that can appear mid-floor (a crate's rat, ambush waves): every enemy prefab. Tools > LoomRoom > Multiplayer > Set Up fills it.")]
    public GameObject[] enemyPrefabs = new GameObject[0];

    [Tooltip("A small light the others carry on the table, so a teammate can be seen in the dark (intensity 0: none).")]
    public Color partnerLightColor = new Color(1f, .78f, .5f);
    [Min(0)] public float partnerLightIntensity = 1.6f;
    [Min(.5f)] public float partnerLightRange = 4f;

    [Title("Network")]
    [Min(1)] public float snapshotRate = 15f;

    [Title("Testing")]
    [Tooltip("Editor: with Multiplayer Play Mode players switched on (Window > Multiplayer > Multiplayer Play Mode), the main editor hosts and the others join, in the room with no intro.")]
    public bool autoStartInEditor = true;

    // ── State ─────────────────────────────────────────────────────────

    // The main menu's co-op menu: the game opens with the co-op screen, already hosting (StartFromMenu Host: the lobby
    // opens with the code to send) or joining MenuJoinCode.
    public enum MenuStart { None, Host, Join }
    public static bool CoopFromMenu;
    public static MenuStart StartFromMenu;
    public static string MenuJoinCode;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { CoopFromMenu = false; StartFromMenu = MenuStart.None; MenuJoinCode = null; }

    // The host's Relay join code (null when hosting by address, or not hosting).
    public string JoinCode { get; private set; }
    // Why the last try to host or join failed (the co-op screen shows it until the next try).
    public string LastError { get; private set; }

    void Fail(string message) { LastError = message; NotificationUI.Show(message); }
    // Hosting or joining through Relay is under way.
    public bool Starting { get; private set; }
    // Reaching the host: the code is being looked up, or the connection is being made.
    public bool Connecting => Starting || (network != null && network.IsListening && !network.IsServer && !network.IsConnectedClient);

    public bool Connected => network != null && network.IsListening && (network.IsServer || network.IsConnectedClient);
    public bool IsHost => network != null && network.IsServer;
    public int LocalSlot => LocalPlayer != null ? Mathf.Max(0, LocalPlayer.SlotIndex) : 0;
    public CoopPlayer LocalPlayer { get; private set; }
    public IReadOnlyList<CoopPlayer> Players => players;
    public int PartySize { get; private set; } = 1;
    public CoopRules Rules => rules;
    public bool SharedLoot => !Connected || rules.sharedLoot;
    // The party's spoils are pooled and split at a run's end (the host's rule).
    public bool SharedSpoils => Connected && rules.sharedSpoils;
    // Empty: everyone shows this player as "Player <slot>" (CoopPlayer.DisplayName).
    public string LocalName => !string.IsNullOrWhiteSpace(playerName) ? playerName.Trim() : "";
    // The host's own floor, and guests' reports that came before it was built (a floor is built over several frames, so a
    // guest can finish first): compared as soon as the host's is there.
    public (int key, int hash) HostFloorHash { get => hostFloorHash; set { hostFloorHash = value; CheckEarlyHashes(); } }
    (int key, int hash) hostFloorHash;
    readonly List<(ulong client, int key, int hash)> earlyHashes = new();

    // This player's part in the table: picking a figure, waiting for the run, in it.
    public bool LocalAtTable { get; private set; }
    public bool LocalReady { get; private set; }
    public bool LocalInRun { get; private set; }
    // The local table player died and walks the floor as a ghost (CoopGhost) until the next floor.
    public bool Ghost { get; private set; }
    // A guest that joined while the host was still on the co-op screen: it waits there until the host's Begin.
    public bool LocalInLobby { get; private set; }
    // Host: on the co-op screen with the game not begun yet (guests wait in the lobby).
    public bool HostInLobby { get; private set; }
    public event System.Action SessionChanged;

    readonly List<CoopPlayer> players = new();
    CoopRules rules;
    bool messagesRegistered;
    // The host's run.
    bool runActive;
    TableLevelData runLevel;
    int runSeed, runFloor = 1;
    float countdownEnds = -1f;
    readonly HashSet<int> atExit = new();
    bool runEndSent;
    // A guest's own flags while it plays someone else's adventurer, and what it earned.
    Dictionary<string, int> ownFlags;
    readonly Dictionary<string, int> earnedFlags = new();
    bool applyingHostFlags;
    Vector3 roomSpot;
    [Tooltip("Metres (at the room player's own scale) a room player keeps from a teammate when coming back from the table.")]
    [SerializeField] float roomClearance = 1.3f;
    float roomYaw;
    TableLevelData pendingLevel;

    // ── The host's trophy wall ────────────────────────────────────────
    // In the host's game a guest's trophy wall shows the host's trophies (TrophyWall reads HostTrophy), kept up to date
    // as the host hangs or takes them down. The guest's own trophies stay in the guest's own save.
    readonly Dictionary<string, int> hostTrophies = new();
    public event System.Action HostTrophiesChanged;
    public bool ShowsHostTrophies => Connected && !IsHost;
    public int HostTrophy(string key) => hostTrophies.TryGetValue(key, out var v) ? v : 0;

    void SendTrophies(ulong? to = null)
    {
        if (!Connected || !IsHost || !ProgressionManager.HasInstance) return;
        var sb = new System.Text.StringBuilder();
        foreach (var f in ProgressionManager.Instance.AllFlags)
            if (f.Key.StartsWith(ProgressionManager.TrophyPrefix)) sb.Append(f.Key).Append('=').Append(f.Value).Append(';');
        var msg = new CoopTrophies { text = new Unity.Collections.FixedString4096Bytes(sb.Length <= 4000 ? sb.ToString() : sb.ToString(0, 4000)) };
        if (to.HasValue) CoopMessages.ToClient(CoopMessages.Trophies, to.Value, msg);
        else CoopMessages.ToGuests(CoopMessages.Trophies, msg);
    }

    void ReceiveTrophies(CoopTrophies msg)
    {
        if (IsHost) return;
        hostTrophies.Clear();
        foreach (var pair in msg.text.ToString().Split(';'))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && int.TryParse(pair.Substring(eq + 1), out var v)) hostTrophies[pair.Substring(0, eq)] = v;
        }
        HostTrophiesChanged?.Invoke();
    }

    // PlayerPrefs key of this player's name (the main menu's co-op menu writes it too).
    public const string NameKey = "coop_name";

    // This player's name, kept between games (the co-op screen's name box).
    public void SetName(string name)
    {
        playerName = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
        PlayerPrefs.SetString(NameKey, playerName);
        if (LocalPlayer != null) LocalPlayer.Rename(LocalName);
    }

    protected override void Awake()
    {
        base.Awake();
        if (string.IsNullOrWhiteSpace(playerName)) playerName = PlayerPrefs.GetString(NameKey, "");
        // Every co-op window keeps playing while another one has the focus.
        Application.runInBackground = true;
        if (network == null) network = FindAnyObjectByType<NetworkManager>();
        // Room in the transport's queues for four players' snapshots, arms and events at once (128 filled up once,
        // on a guest while a floor was loading).
        var transport = network != null ? network.GetComponent<UnityTransport>() : null;
        if (transport != null && transport.MaxPacketQueueSize < 512) transport.MaxPacketQueueSize = 512;
        SyncRules();
        CommandLine();
        // Read here like the command line, so a new game's intro knows to wait for the connection.
        menuHost = StartFromMenu == MenuStart.Host;
        menuJoin = StartFromMenu == MenuStart.Join && !string.IsNullOrWhiteSpace(MenuJoinCode) ? MenuJoinCode.Trim() : null;
        StartFromMenu = MenuStart.None; MenuJoinCode = null;
        MenuLobby = fromMenuLobby = menuHost || menuJoin != null;
    }

    // This game was opened from the main menu's co-op menu: its lobby came first, so a new game's intro asks nothing more.
    bool fromMenuLobby;
    // Opened from the main menu to host, or to join a code: the co-op screen says so while the connection starts.
    public bool MenuHosting => menuHost;
    public bool MenuJoining => menuJoin != null;

    // A game opened from the main menu's co-op menu waits in the lobby before anything happens: the co-op screen over
    // a blank backdrop (CoopMenuUI), no wake-up and no one in the room (WorldManager waits), until the host's Begin
    // lets everyone in together. Also ends when joining fails or the player leaves.
    public bool MenuLobby { get; private set; }

    bool menuHost;
    string menuJoin;

    void Start()
    {
        if (network == null) { Debug.LogError("[Coop] MultiplayerManager needs the scene's NetworkManager."); return; }
        network.OnClientConnectedCallback += OnClientConnected;
        network.OnClientDisconnectCallback += OnClientDisconnected;
        network.OnServerStarted += OnStarted;
        network.ConnectionApprovalCallback = Approve;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged += OnFlagChanged;
        if (InputManager.HasInstance) InputManager.Instance.PingPressed += CoopPing.PingFromLocal;
        // The Dungeon Master talks to the party (lines on CoopRemarks; Set Up Co-op adds it to the prefab).
        if (GetComponent<CoopRemarks>() == null) gameObject.AddComponent<CoopRemarks>();
#if UNITY_EDITOR
        if (autoStartInEditor && CoopEditorTesting.Requested) StartCoroutine(AutoStart());
#endif
        if (commandHost) Host();
        else if (commandJoin != null) Join(commandJoin);
        else if (menuHost) { Host(); StartCoroutine(LobbyOnceHosting()); }
        else if (menuJoin != null) Join(menuJoin);
        if (CoopFromMenu) StartCoroutine(OpenFromMenu());
    }

    // An existing game from the menu's Co-op button: once the player is up in the room, the co-op screen.
    // (A new game asks in the dark before its intro instead, NewGameLobby.)
    IEnumerator OpenFromMenu()
    {
        yield return new WaitForSecondsRealtime(MenuLobby ? .2f : 1.5f);
        if (IntroController.Seated) yield break;
        CoopFromMenu = false;
        // In the lobby the screen opens at once (nothing has happened in the room yet); otherwise once the player is up.
        while (!MenuLobby && GameManager.HasInstance && GameManager.Instance.State != GameState.Explore) yield return null;
        if (CoopMenuUI.Instance != null && !CoopMenuUI.Instance.IsOpen) CoopMenuUI.Instance.Open(CoopMenuUI.Mode.Pause);
    }

    // A build started with -host (-lan: by address), or -join <code or address> (and -name <name>, -port <port>)
    // goes straight into co-op. Read in Awake, so a new game's intro knows to wait for the connection.
    bool commandHost;
    string commandJoin;
    int autoBegin;   // -autobegin <players>: the host begins a new game by itself once that many are in
    public bool CommandLineCoop => commandHost || commandJoin != null || menuHost || menuJoin != null;

    // Hosting from a menu opens the lobby once the game is up: friends who join wait until Begin.
    public IEnumerator LobbyOnceHosting()
    {
        for (float t = 0; t < 20f && !IsHost; t += Time.unscaledDeltaTime) yield return null;
        if (IsHost) OpenLobby();
    }

    void CommandLine()
    {
        var args = System.Environment.GetCommandLineArgs();
        string join = null; bool host = false;
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i].ToLowerInvariant())
            {
                case "-host": host = true; break;
                case "-lan": online = false; break;
                case "-autobegin": if (next != null && int.TryParse(next, out var n)) autoBegin = n; break;
                case "-join": join = next ?? address; break;
                case "-name": if (next != null) playerName = next; break;
                case "-port": if (next != null && ushort.TryParse(next, out var p)) port = p; break;
            }
        }
        commandHost = host;
        commandJoin = join;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (network != null)
        {
            network.OnClientConnectedCallback -= OnClientConnected;
            network.OnClientDisconnectCallback -= OnClientDisconnected;
            network.OnServerStarted -= OnStarted;
        }
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlagChanged;
        if (InputManager.HasInstance) InputManager.Instance.PingPressed -= CoopPing.PingFromLocal;
        CoopWorld.Unhook();
    }

#if UNITY_EDITOR
    IEnumerator AutoStart()
    {
        // Let the room settle (the player stands up by the bed) before the session starts.
        yield return new WaitForSecondsRealtime(CoopEditorTesting.IsMainEditor ? .5f : 2.5f);
        if (CoopEditorTesting.IsMainEditor) Host();
        else
        {
            for (int i = 0; i < 20 && !Connected; i++)
            {
                if (!network.IsListening) Join(address);
                yield return new WaitForSecondsRealtime(3f);
            }
        }
    }
#endif

    // ── Session ───────────────────────────────────────────────────────

    // ── The lobby ─────────────────────────────────────────────────────

    // Host: hosting from the co-op screen opens the lobby: whoever joins waits on their screen until Begin.
    public void OpenLobby()
    {
        if (!IsHost || LocalInRun) return;
        HostInLobby = true;
        RulesChanged();
    }

    // Host: Begin. Everyone waiting in the lobby comes into the game.
    public void BeginForEveryone()
    {
        MenuLobby = false;
        if (!HostInLobby) return;
        HostInLobby = false;
        RulesChanged();
    }

    // Guests: the host's rules came in. The first set after joining says whether to wait in the lobby; a later one
    // without it lets a waiting player in.
    bool rulesSeen;
    void GuestRules(CoopRules r)
    {
        if (network.IsServer) return;
        if (!rulesSeen)
        {
            rulesSeen = true; LocalInLobby = r.lobby && !LocalInRun;
            if (!LocalInLobby) MenuLobby = false;   // the game has begun: straight in
            // Joined a game already begun: into the room at once, at this player's own spot.
            bool intro = IntroController.Seated;
            if (!LocalInLobby && !LocalInRun && !intro && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room
                && WorldManager.HasInstance && WorldManager.Instance.wakeUpCutscene != null) WorldManager.Instance.wakeUpCutscene.PlaceInRoom();
            return;
        }
        if (LocalInLobby && !r.lobby) { LocalInLobby = false; LeftLobby(); }
    }

    // In from the lobby: the screen goes and the player stands at their own spot in the room (a new game's intro
    // seats everyone itself).
    void LeftLobby()
    {
        MenuLobby = false;
        var menu = CoopMenuUI.Instance;
        bool newGame = menu != null && menu.IsOpen && menu.Current == CoopMenuUI.Mode.NewGame;
        if (menu != null && menu.IsOpen && !newGame) menu.Close();
        if (!newGame && WorldManager.HasInstance && WorldManager.Instance.wakeUpCutscene != null) WorldManager.Instance.wakeUpCutscene.PlaceInRoom();
    }

    // Each player's spot beside the others in the room (room units: the room is built about roomScale to the metre).
    public Vector3 RoomSpawnOffset(Quaternion facing) => SpawnOffset(facing) * roomScale * roomSpawnSpread;

    [Tooltip("How much wider apart players stand in the room than on the table (both by spawnSpacing).")]
    [Min(1)] public float roomSpawnSpread = 1.7f;

    // The co-op screen changed a rule: the guests get the new set.
    public void RulesChanged()
    {
        SyncRules();
        if (Connected && IsHost) CoopMessages.ToGuests(CoopMessages.Rules, rules);
    }

    void SyncRules() => rules = new CoopRules { sharedLoot = sharedLoot, hostAdventurer = hostAdventurer, partyProgression = partyProgression, lobby = HostInLobby, sharedSpoils = sharedSpoils };

    void Transport(string host)
    {
        var transport = network.GetComponent<UnityTransport>();
        if (transport != null) transport.SetConnectionData(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim(), port, "0.0.0.0");
    }

    public bool Host()
    {
        if (network == null || network.IsListening || Starting) return false;
        LastError = null;
        SyncRules();
        bool relay = online;
#if UNITY_EDITOR
        if (CoopEditorTesting.Requested) relay = false;   // the one-computer test stays on this computer
#endif
        if (relay) { StartCoroutine(HostOnline()); return true; }
        return HostByAddress();
    }

    bool HostByAddress()
    {
        JoinCode = null;
        Transport(address);
        Configure();
        bool ok = network.StartHost();
        if (!ok) Fail("Could not start a game. Is another copy of the game hosting on this computer?");
        return ok;
    }

    IEnumerator HostOnline()
    {
        Starting = true;
        var task = CoopRelay.Host(network.GetComponent<UnityTransport>(), maxPlayers - 1);
        while (!task.IsCompleted) yield return null;
        Starting = false;
        if (task.IsFaulted || task.IsCanceled)
        {
            Debug.LogWarning($"[Coop] Relay hosting failed, hosting by address instead: {task.Exception?.GetBaseException().Message}");
            NotificationUI.Show("Online hosting is not available: others join with your address");
            HostByAddress();
            yield break;
        }
        JoinCode = task.Result;
        Configure();
        if (!network.StartHost()) { JoinCode = null; Fail("Could not start a game."); yield break; }
        GUIUtility.systemCopyBuffer = JoinCode;
        NotificationUI.Show($"Join code {JoinCode} (copied)");
        Debug.Log($"[Coop] Hosting online. Join code {JoinCode}");
    }

    // A join code (Relay) or an address.
    public bool Join(string target)
    {
        joinRetries = 0;
        return JoinTarget(target);
    }

    string lastTarget;
    int joinRetries;

    bool JoinTarget(string target)
    {
        if (network == null || network.IsListening || Starting) return false;
        LastError = null;
        lastTarget = target;
        if (string.IsNullOrWhiteSpace(target)) { Fail("Paste or type the host's join code first."); return false; }
        if (CoopRelay.LooksLikeCode(target)) { StartCoroutine(JoinOnline(target)); return true; }
        JoinCode = null;
        Transport(target);
        Configure();
        bool ok = network.StartClient();
        if (!ok) Fail("Could not join that address.");
        return ok;
    }

    IEnumerator JoinOnline(string code)
    {
        Starting = true;
        NotificationUI.Show("Joining...");
        var task = CoopRelay.Join(network.GetComponent<UnityTransport>(), code);
        while (!task.IsCompleted) yield return null;
        Starting = false;
        if (task.IsFaulted || task.IsCanceled)
        {
            Debug.LogWarning($"[Coop] Relay join failed: {task.Exception?.GetBaseException().Message}");
            var relay = task.Exception?.GetBaseException() as Unity.Services.Relay.RelayServiceException;
            bool badCode = relay != null && (relay.Reason == Unity.Services.Relay.RelayExceptionReason.JoinCodeNotFound
                || relay.Reason == Unity.Services.Relay.RelayExceptionReason.InvalidRequest || relay.Reason == Unity.Services.Relay.RelayExceptionReason.InvalidArgument
                || relay.Reason == Unity.Services.Relay.RelayExceptionReason.AllocationNotFound);
            Fail(badCode ? "No game with that code. Check it with the host." : "Could not reach the online service. Check your internet connection.");
            yield break;
        }
        Configure();
        if (!network.StartClient()) Fail("Could not join.");
    }

    const int JoinRetries = 4;
    IEnumerator RetryJoin()
    {
        Debug.Log($"[Coop] The connection failed; trying again ({joinRetries}/{JoinRetries}).");
        Starting = true;   // the screen keeps saying Connecting
        while (network.IsListening || network.ShutdownInProgress) yield return null;
        yield return new WaitForSecondsRealtime(joinRetries);
        Starting = false;
        JoinTarget(lastTarget);
    }

    void Configure()
    {
        network.NetworkConfig.ConnectionApproval = true;
        network.NetworkConfig.EnableSceneManagement = false;
        network.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(BuildId);
        joinedOnce = false;
        joinedAt = float.PositiveInfinity;   // nobody is announced until this player is in
    }

    // Which build this is: games from different builds can't play together (an editor plays with anything).
    static string BuildId => Application.isEditor ? "editor" : Application.buildGUID;
    bool joinedOnce;   // this connection got as far as joining (a refusal or a timeout never does)

    // This computer's address on the local network, for others to join by (hosting without Relay).
    public static string LocalAddress()
    {
        try
        {
            foreach (var ip in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip)) return ip.ToString();
        }
        catch { }
        return "127.0.0.1";
    }

    public void Leave()
    {
        if (network == null || !network.IsListening) return;
        bool wasGuest = !network.IsServer;
        network.Shutdown();
        Ended(wasGuest, "You left the game");
    }

    IEnumerator HostGone()
    {
        yield return new WaitForSecondsRealtime(2.5f);
        if (!Connected) ToMainMenu();
    }

    // Back to the main menu: out of any session, and the Network Manager (kept across scene loads) goes with this
    // scene, so the game's scene starts with its own again.
    public void ToMainMenu()
    {
        Leave();
        if (network != null) Destroy(network.gameObject);
        UnityEngine.SceneManagement.SceneManager.LoadScene("Main Menu");
    }

    void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        int count = network.ConnectedClientsIds.Count;
        bool host = request.ClientNetworkId == NetworkManager.ServerClientId;
        string theirs = request.Payload != null ? System.Text.Encoding.UTF8.GetString(request.Payload) : "";
        bool sameBuild = host || BuildId == "editor" || theirs == "editor" || theirs == BuildId;
        response.Approved = host || (count < maxPlayers && sameBuild);
        response.CreatePlayerObject = response.Approved;
        if (!response.Approved) response.Reason = !sameBuild ? "The host has a different build of the game. Play the same build." : "The game is full";
    }

    void OnStarted() { RegisterMessages(); CoopWorld.Hook(); SessionChanged?.Invoke(); }

    void OnClientConnected(ulong client)
    {
        if (client == network.LocalClientId)
        {
            RegisterMessages();
            CoopWorld.Hook();
            joinedOnce = true;
            LastError = null;
            rulesSeen = false;
            joinedAt = Time.unscaledTime;
            if (!network.IsServer) StartCoroutine(Joined());
        }
        if (network.IsServer && client != NetworkManager.ServerClientId)
        {
            SyncRules();
            CoopMessages.ToClient(CoopMessages.Rules, client, rules);
            CoopMessages.ToClient(CoopMessages.Flags, client, HostFlags());
            SendTrophies(client);
            CoopWorld.SendRoom(client);
        }
        SessionChanged?.Invoke();
    }

    void OnClientDisconnected(ulong client)
    {
        if (network == null) return;
        // The guest lost the host (or was refused).
        if (!network.IsServer && (client == network.LocalClientId || client == NetworkManager.ServerClientId))
        {
            // A refusal carries the host's own reason; the transport's own messages are not for players.
            string given = network.DisconnectReason;
            bool fromHost = !string.IsNullOrEmpty(given) && !given.StartsWith("[");
            // The first connection to a code sometimes fails (more so just after the game starts): a few more tries,
            // further apart, before giving up.
            if (!joinedOnce && !fromHost && joinRetries < JoinRetries && CoopRelay.LooksLikeCode(lastTarget))
            {
                joinRetries++;
                StartCoroutine(RetryJoin());
                return;
            }
            string reason = fromHost ? given
                : joinedOnce ? "The host left the game" : "Could not join. Check the code or address, and that the host is still in the game.";
            if (!joinedOnce) LastError = reason;
            Ended(true, reason);
            // The host left (or sent us away): the game was theirs, so everyone goes back to the main menu.
            if (joinedOnce) StartCoroutine(HostGone());
            return;
        }
        SessionChanged?.Invoke();
    }

    // The session is over here: back to playing alone, in the room if a run was on.
    void Ended(bool wasGuest, string message)
    {
        if (!string.IsNullOrEmpty(message)) NotificationUI.Show(message);
        CoopMessages.UnregisterAll(); messagesRegistered = false;
        CoopWorld.Unhook();
        bool inRun = LocalInRun;
        // Leaving a run part way (or the host leaving): what this player carries and their share of the health slain go
        // into their own spoils, as at a run's end, not lost with the run (a host who plays on keeps the run instead).
        bool goesOn = !wasGuest && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table
            && PlayerManager.Instance.Active != null && PlayerManager.Instance.Active.IsAlive;
        if (inRun && !goesOn && RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended
            && ProgressionManager.HasInstance && ProgressionManager.MetaActive)
        {
            int with = 1;
            foreach (var p in players) if (p != null && !p.IsOwner && p.Current.inRun) with++;
            int health = rules.sharedSpoils ? RunManager.Instance.HealthSlain / with : RunManager.Instance.HealthSlain;
            ProgressionManager.Instance.Settle(PlayerManager.Instance.GetPlayer(PlayerKind.Table), RunManager.Instance.Kills, health, false);
        }
        EndGhost();
        LocalAtTable = LocalReady = LocalInRun = LocalInLobby = HostInLobby = MenuLobby = false;
        runActive = false; countdownEnds = -1f; atExit.Clear(); runEndSent = false;
        players.Clear(); LocalPlayer = null; PartySize = 1; JoinCode = null;
        if (wasGuest) RestoreOwnFlags();
        if (hostTrophies.Count > 0) { hostTrophies.Clear(); HostTrophiesChanged?.Invoke(); }
        // The host keeps its own dungeon and plays on alone; a guest (or a host who was a ghost, dead) goes back to the room.
        bool playOn = !wasGuest && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table
            && PlayerManager.Instance.Active != null && PlayerManager.Instance.Active.IsAlive;
        if (inRun && !playOn && RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended)
        {
            RunManager.Instance.Abandon();
            var loader = TableManager.Loader;
            if (loader != null) loader.ReturnToRoom();
        }
        SessionChanged?.Invoke();
    }

    void RegisterMessages()
    {
        if (messagesRegistered) return;
        messagesRegistered = true;
        CoopMessages.Register<RunStart>(CoopMessages.RunStart, (s, m) => StartCoroutine(BeginRun(m)));
        CoopMessages.Register<CoopTrophies>(CoopMessages.Trophies, (s, m) => ReceiveTrophies(m));
        CoopMessages.Register<CoopInt>(CoopMessages.RunEnd, (s, m) => RunEnded((m.a & 1) != 0, m.b, m.a >> 1));
        CoopMessages.Register<SnapshotBatch>(CoopMessages.Snapshots, CoopWorld.ReceiveSnapshots);
        CoopMessages.Register<NetDamage>(CoopMessages.EnemyHit, CoopWorld.ReceiveEnemyDamaged);
        CoopMessages.Register<NetDamage>(CoopMessages.Strike, CoopWorld.ReceiveStrike);
        CoopMessages.Register<CoopEvent>(CoopMessages.Event, CoopWorld.ReceiveEvent);
        CoopMessages.Register<EnemySpawn>(CoopMessages.Spawn, CoopWorld.ReceiveSpawn);
        CoopMessages.Register<FlagSet>(CoopMessages.Flags, (s, m) => ApplyHostFlags(m));
        CoopMessages.Register<CoopRules>(CoopMessages.Rules, (s, m) => { rules = m; GuestRules(m); });
        CoopMessages.Register<CoopInt>(CoopMessages.Countdown, (s, m) => ShowCountdown(m.a, m.b));
        CoopMessages.Register<CoopInt>(CoopMessages.IntroBegin, (s, m) => { if (!network.IsServer && IntroController.HasInstance) IntroController.Instance.PlayShared(); });
        CoopMessages.Register<CoopInt>(CoopMessages.Finale, (s, m) => { if (!network.IsServer && IntroController.HasInstance) IntroController.Instance.RemoteFinale(m.a != 0); });
        CoopMessages.Register<CoopInt>(CoopMessages.FloorHash, (s, m) =>
        {
            // A guest: the host found this floor built differently here.
            if (!network.IsServer) { if (m.b == -1) NotificationUI.Show("Your floor is different from the host's: your game is out of date. Restart or update it, then join again."); return; }
            if (m.a == HostFloorHash.key) CheckFloorHash(s, m.a, m.b);
            else { earlyHashes.RemoveAll(e => e.client == s); earlyHashes.Add((s, m.a, m.b)); }
        });
        // A guest finished loading a floor (its reveal included): bring it up to date with what already happened there.
        CoopMessages.Register<CoopInt>(CoopMessages.Ready, (s, m) => { if (network.IsServer) CoopWorld.CatchUp(s, m.a); });
    }

    void CheckEarlyHashes()
    {
        for (int i = earlyHashes.Count - 1; i >= 0; i--)
        {
            var e = earlyHashes[i];
            if (e.key != hostFloorHash.key) continue;
            earlyHashes.RemoveAt(i);
            CheckFloorHash(e.client, e.key, e.hash);
        }
    }

    void CheckFloorHash(ulong client, int key, int hash)
    {
        if (hash == HostFloorHash.hash) { Debug.Log($"[Coop] Client {client} built the same floor ({hash:X8})."); return; }
        Debug.LogWarning($"[Coop] Client {client} built floor {key} differently (hash {hash:X8}, host {HostFloorHash.hash:X8}).");
        // Said on screen, to both: a different floor means enemies and loot that only one side can see. Its cause is
        // game files that differ (an older build, or an editor clone that missed changes).
        var who = players.Find(p => p != null && p.OwnerClientId == client);
        NotificationUI.Show($"{(who != null ? who.DisplayName : "A player")}'s floor is different from yours. Their game is out of date: they should restart or update it.");
        CoopMessages.ToClient(CoopMessages.FloorHash, client, new CoopInt { a = key, b = -1 });
    }

    // ── Players ───────────────────────────────────────────────────────

    public void Register(CoopPlayer player)
    {
        if (!players.Contains(player)) players.Add(player);
        if (player.IsOwner) LocalPlayer = player;
        // Someone new (not the players already there when this one joined): by name, once it has come through.
        if (!player.IsOwner && Time.unscaledTime - joinedAt > 2f) StartCoroutine(Announce(player));
        SessionChanged?.Invoke();
    }

    float joinedAt = -100f;

    // A guest is in: whose game, and what to do if a run is already on.
    IEnumerator Joined()
    {
        float until = Time.unscaledTime + 3f;
        CoopPlayer host = null;
        while (Time.unscaledTime < until && Connected && ((host = PlayerInSlot(0)) == null || host.PlayerName.Value.Length == 0)) yield return null;
        if (!Connected) yield break;
        string whose = host != null ? $"Joined {host.DisplayName}'s game" : "Joined the game";
        NotificationUI.Show(host != null && host.Current.inRun ? whose + ". Sit down at the table to join the run." : whose);
    }

    IEnumerator Announce(CoopPlayer player)
    {
        float until = Time.unscaledTime + 2f;
        while (player != null && Time.unscaledTime < until && player.PlayerName.Value.Length == 0) yield return null;
        if (player != null && Connected) NotificationUI.Show($"{player.DisplayName} joined");
    }

    public void Unregister(CoopPlayer player)
    {
        // Another player leaving a game that goes on (the host's leaving ends it here, with its own message).
        if (!player.IsOwner && Connected && !network.ShutdownInProgress && player.OwnerClientId != NetworkManager.ServerClientId && players.Contains(player))
            NotificationUI.Show($"{player.DisplayName} left");
        players.Remove(player);
        if (LocalPlayer == player) LocalPlayer = null;
        SessionChanged?.Invoke();
    }

    public byte FreeSlot(ulong client)
    {
        if (client == NetworkManager.ServerClientId) return 0;
        for (byte slot = 1; slot < 8; slot++)
        {
            bool taken = false;
            foreach (var p in players) if (p != null && p.Slot.Value == slot) taken = true;
            if (!taken) return slot;
        }
        return 7;
    }

    public CoopPlayer PlayerInSlot(int slot)
    {
        foreach (var p in players) if (p != null && p.SlotIndex == slot) return p;
        return null;
    }

    public GameObject RoomBody(int slot) => roomBodies != null && roomBodies.Length > 0 ? roomBodies[Mathf.Abs(slot) % roomBodies.Length] : null;

    public GameObject TableBody(string classId)
    {
        var rules = AdventureRulesAsset;
        if (rules != null && rules.classes != null)
            foreach (var c in rules.classes) if (c != null && c.id == classId && c.coopBody != null) return c.coopBody;
        return defaultTableBody;
    }

    AdventureRules AdventureRulesAsset
    {
        get
        {
            var table = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
            var progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
            return progress != null ? progress.rules : null;
        }
    }

    public int EnemyPrefabIndex(Character c)
    {
        if (c == null || enemyPrefabs == null) return -1;
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            var prefab = enemyPrefabs[i];
            var pc = prefab != null ? prefab.GetComponent<Character>() : null;
            if (pc != null && pc.data != null && pc.data == c.data) return i;
        }
        string name = c.name.Replace("(Clone)", "").Trim();
        for (int i = 0; i < enemyPrefabs.Length; i++) if (enemyPrefabs[i] != null && enemyPrefabs[i].name == name) return i;
        return -1;
    }

    public GameObject EnemyPrefab(int index) => enemyPrefabs != null && index >= 0 && index < enemyPrefabs.Length ? enemyPrefabs[index] : null;
    public GameObject EnemyPrefab(int index, string name)
    {
        var byIndex = EnemyPrefab(index);
        if (string.IsNullOrEmpty(name) || byIndex != null && byIndex.name == name || enemyPrefabs == null) return byIndex;
        foreach (var p in enemyPrefabs) if (p != null && p.name == name) return p;
        return byIndex;
    }

    // ── Flags (the host's story and unlocks) ──────────────────────────

    // A player's own meta progression (spoils, the character sheet, the trophies, the ledger lesson): never the host's,
    // never sent to anyone, kept as it is when leaving someone's game and written to the player's own save at once.
    // Each player sees their own sheet and their own trophies on the same wall.
    public static bool PersonalFlag(string key) => key.StartsWith("meta.") || key == ProgressionManager.LedgerIntroFlag || key == ProgressionManager.LedgerSaidFlag
        || key == ProgressionManager.BlockingTaughtFlag || key == ProgressionManager.BashMountedFlag;

    FlagSet HostFlags()
    {
        var keys = new List<string>(); var values = new List<int>();
        if (ProgressionManager.HasInstance) foreach (var pair in ProgressionManager.Instance.AllFlags) { if (PersonalFlag(pair.Key)) continue; keys.Add(pair.Key); values.Add(pair.Value); }
        return new FlagSet { keys = keys.ToArray(), values = values.ToArray() };
    }

    void ApplyHostFlags(FlagSet set)
    {
        if (network.IsServer || !ProgressionManager.HasInstance) return;
        var progression = ProgressionManager.Instance;
        if (ownFlags == null) ownFlags = new Dictionary<string, int>(progression.AllFlags);
        applyingHostFlags = true;
        try
        {
            // Playing someone else's game: their story, so the room and the table behave as they do for the host.
            // With their own adventurer a guest keeps its own unlocks and takes only the story flags it lacks.
            for (int i = 0; i < set.keys.Length; i++)
                if (PersonalFlag(set.keys[i])) continue;
                else if (rules.hostAdventurer || set.keys[i].StartsWith("story.") || set.keys[i] == ProgressionManager.TableEnteredFlag || set.keys[i] == DungeonMasterRemarks.SatFlag)
                    progression.SetFlag(set.keys[i], set.values[i]);
            if (rules.hostAdventurer && !set.changes) foreach (var pair in ownFlags) if (!PersonalFlag(pair.Key) && System.Array.IndexOf(set.keys, pair.Key) < 0) progression.SetFlag(pair.Key, 0);
        }
        finally { applyingHostFlags = false; }
    }

    void OnFlagChanged(string key, int value)
    {
        // The host's wall changed: the guests' walls follow.
        if (network != null && network.IsListening && network.IsServer && key.StartsWith(ProgressionManager.TrophyPrefix)) SendTrophies();
        if (applyingHostFlags || network == null || !network.IsListening || PersonalFlag(key)) return;
        // The host's unlocks and story reach the guests at once (playing the host's adventurer).
        if (network.IsServer) { if (rules.hostAdventurer) CoopMessages.ToGuests(CoopMessages.Flags, new FlagSet { keys = new[] { key }, values = new[] { value }, changes = true }); return; }
        earnedFlags[key] = value;
    }

    // Leaving someone else's game: the guest's own flags come back, plus what it earned there (Party Progression).
    void RestoreOwnFlags()
    {
        if (ownFlags == null || !ProgressionManager.HasInstance) { earnedFlags.Clear(); return; }
        var progression = ProgressionManager.Instance;
        bool keep = rules.partyProgression;
        var earned = new Dictionary<string, int>(earnedFlags);
        var now = new List<string>(progression.AllFlags.Keys);
        // These changes are the restore itself, not something earned (the connection may still be closing).
        applyingHostFlags = true;
        try
        {
            foreach (var key in now) if (!ownFlags.ContainsKey(key) && !PersonalFlag(key)) progression.SetFlag(key, 0);
            foreach (var pair in ownFlags) if (!PersonalFlag(pair.Key)) progression.SetFlag(pair.Key, pair.Value);
            if (keep) foreach (var pair in earned) if (pair.Value > progression.GetFlag(pair.Key)) progression.SetFlag(pair.Key, pair.Value);
        }
        finally { applyingHostFlags = false; }
        ownFlags = null; earnedFlags.Clear();
    }

    // A guest's save is left alone while it plays someone else's game (AdventureSave asks), all but the player's
    // own meta progression: SaveFlags is what it writes meanwhile.
    public bool HoldsSave => Connected && !IsHost;

    // A guest's flags as its own save should have them now: its own from before joining, with its meta progression as
    // it is (null: nothing held, save as usual).
    public List<ProgressionManager.FlagEntry> GuestSaveFlags()
    {
        if (!HoldsSave || ownFlags == null || !ProgressionManager.HasInstance) return null;
        var list = new List<ProgressionManager.FlagEntry>();
        foreach (var pair in ownFlags) if (!PersonalFlag(pair.Key)) list.Add(new ProgressionManager.FlagEntry { key = pair.Key, value = pair.Value });
        foreach (var pair in ProgressionManager.Instance.AllFlags) if (PersonalFlag(pair.Key)) list.Add(new ProgressionManager.FlagEntry { key = pair.Key, value = pair.Value });
        return list;
    }

    // ── The table ─────────────────────────────────────────────────────

    // TableManager, once a figure is picked: wait at the table for the others; the host starts the run when everyone
    // sitting down has picked. Esc gives the figure back and stands up. done(true): the run started.
    public IEnumerator WaitAtTable(AdventurerClass picked, TableLevelData level, Player roomPlayer, System.Action<bool> done)
    {
        roomSpot = roomPlayer.transform.position;
        roomYaw = roomPlayer.Look != null ? roomPlayer.Look.YawTransform.eulerAngles.y : 0f;
        pendingLevel = level;
        LocalReady = true;
        bool started = false, cancelled = false;
        void Cancel() => cancelled = true;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed += Cancel;
        float nextNote = 0f;
        try
        {
            while (Connected && !LocalInRun && !cancelled)
            {
                if (Time.unscaledTime >= nextNote)
                {
                    nextNote = Time.unscaledTime + 4f;
                    var waiting = WaitingFor();
                    NotificationUI.Show(waiting != null ? $"Waiting for {waiting}" : runActive || !IsHost ? "Waiting for the host" : "Starting");
                }
                if (TableManager.Loader != null && TableManager.Loader.Busy) { started = true; break; }
                yield return null;
            }
        }
        finally { if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= Cancel; }
        started |= LocalInRun;
        LocalReady = started;
        done?.Invoke(started);
    }

    // A room player coming back from the table steps aside from a teammate's room body standing on (or nearly on) the
    // same spot, along the floor, so neither view starts inside the other's head.
    public void KeepClearOfTeammates(Player room)
    {
        if (room == null) return;
        float scale = Mathf.Max(1f, room.transform.lossyScale.y), clear = roomClearance * scale;
        var at = room.transform.position;
        foreach (var p in players)
        {
            if (p == null || p.IsOwner || p.Current.place != CoopPlace.Room) continue;
            var away = at - p.transform.position; away.y = 0f;
            if (away.magnitude >= clear) continue;
            if (away.sqrMagnitude < .01f) away = room.transform.right;
            var to = p.transform.position + away.normalized * clear;
            if (UnityEngine.AI.NavMesh.SamplePosition(to, out var hit, clear, UnityEngine.AI.NavMesh.AllAreas)) to = hit.position;
            room.Warp(to, room.transform.rotation);
            at = to;
        }
    }

    // Sitting down: nobody hears the Dungeon Master or picks a figure alone. Seated, each waits until everyone in the
    // room has sat down too (players in a run or still in the lobby don't count). False: Esc, stand up again.
    public IEnumerator WaitForEveryoneSeated(System.Action<bool> done)
    {
        bool cancelled = false;
        void Cancel() => cancelled = true;
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed += Cancel;
        float nextNote = 0f;
        try
        {
            while (Connected && !cancelled)
            {
                string waiting = null; bool training = false; int seated = 1, total = 1;
                foreach (var p in players)
                {
                    if (p == null || p.IsOwner || p.Current.inRun || p.Current.inLobby) continue;
                    total++;
                    if (p.Current.atTable) seated++; else if (waiting == null) { waiting = p.DisplayName; training = p.Current.mustTrain; }
                }
                if (waiting == null) break;
                if (Time.unscaledTime >= nextNote)
                {
                    nextNote = Time.unscaledTime + 4f;
                    // The sheet lesson keeps them from the table until they train Blocking: say so, it isn't a wait for nothing.
                    NotificationUI.Show(training ? $"Waiting for {waiting} to train Blocking at the sheet ({seated}/{total}). Esc stands up."
                        : $"Waiting for {waiting} to sit down ({seated}/{total}). Esc stands up.");
                }
                yield return null;
            }
        }
        finally { if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= Cancel; }
        done?.Invoke(!cancelled);
    }

    public void SatDown() => LocalAtTable = true;
    public void StoodUp() { LocalAtTable = false; LocalReady = false; }

    // Another player sitting at the table who hasn't picked yet.
    string WaitingFor()
    {
        foreach (var p in players)
            if (p != null && !p.IsOwner && p.Current.atTable && !p.Current.ready && !p.Current.inRun) return p.DisplayName;
        return null;
    }

    // Where this player stands to look at the figures: beside the others.
    public Vector3 FigureOffset(Vector3 facing)
    {
        if (!Connected) return Vector3.zero;
        int slot = LocalSlot;
        float side = slot == 0 ? 0 : (slot % 2 == 1 ? 1 : -1) * ((slot + 1) / 2);
        return Vector3.Cross(Vector3.up, facing).normalized * side * figureSpacing;
    }

    // Where this player starts a floor: beside the others.
    public Vector3 SpawnOffset(Quaternion facing)
    {
        if (!Connected) return Vector3.zero;
        int slot = LocalSlot;
        float side = slot == 0 ? 0 : (slot % 2 == 1 ? 1 : -1) * ((slot + 1) / 2);
        return facing * Vector3.right * side * spawnSpacing;
    }

    void Update()
    {
        if (!Connected) return;
        CoopWorld.Tick();
        if (IsHost) HostRun();
        TeammateNews();
        TickCountdown();
        // A ghost on its feet again (the debug revive, or anything else that brings the table player back): no ghost now.
        if (Ghost && PlayerManager.HasInstance && PlayerManager.Instance.GetPlayer(PlayerKind.Table) is Player t && t != null && t.IsAlive) EndGhost();
    }

    // What happens to the others in a run goes in the feed: one of them dies, or is back on the next floor. A change
    // has to hold for a second before it is said (a state seen mid floor change never reaches the feed).
    readonly Dictionary<CoopPlayer, (bool said, bool seen, float since)> aliveNews = new();
    void TeammateNews()
    {
        foreach (var p in players)
        {
            if (p == null || p.IsOwner) continue;
            var s = p.Current;
            if (!s.inRun || TableManager.Loader == null || TableManager.Loader.Busy) { if (!s.inRun) aliveNews.Remove(p); continue; }
            if (!aliveNews.TryGetValue(p, out var n)) { aliveNews[p] = (s.alive, s.alive, Time.unscaledTime); continue; }
            if (s.alive != n.seen) n = (n.said, s.alive, Time.unscaledTime);
            if (n.seen != n.said && Time.unscaledTime - n.since >= 1f)
            {
                MessageLog.Post(n.seen ? $"{p.DisplayName} is back." : $"{p.DisplayName} died.", n.seen ? MessageKind.Good : MessageKind.Bad);
                Debug.Log($"[Coop] {p.DisplayName} {(n.seen ? "back" : "died")}: place {s.place} health {s.health} floor {TableManager.Loader.FloorNumber}");
                n = (n.seen, n.seen, n.since);
            }
            aliveNews[p] = n;
        }
    }

    // ── The host's run ────────────────────────────────────────────────

    void HostRun()
    {
        var loader = TableManager.Loader;
        if (!runActive)
        {
            // Everyone at the table has picked, the host too: down they go.
            if (!LocalReady || LocalInRun || loader == null || loader.Busy) return;
            foreach (var p in players) if (p != null && p.Current.atTable && !p.Current.ready && !p.IsOwner) return;
            StartHostRun();
            return;
        }
        // Someone picked a figure while the run is on: they join on the current floor. A guest who left the run (the pause
        // menu's Leave level) and isn't waiting at the table can be sent it again when they pick once more.
        foreach (var p in players)
            if (p != null && !p.IsOwner && !p.Current.ready && !p.Current.inRun) lateJoinSent.Remove(p.OwnerClientId);
        foreach (var p in players)
            if (p != null && !p.IsOwner && p.Current.ready && !p.Current.inRun && !lateJoinSent.Contains(p.OwnerClientId))
            {
                lateJoinSent.Add(p.OwnerClientId);
                CoopMessages.ToClient(CoopMessages.RunStart, p.OwnerClientId, RunStartFor(runFloor, false));
            }
        HostStairs();
        HostDeaths();
    }
    readonly HashSet<ulong> lateJoinSent = new();

    RunStart RunStartFor(int floor, bool descend)
    {
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        var progress = table != null ? table.GetComponent<AdventurerProgress>() : null;
        int generation = descend && progress != null ? progress.Level : TableManager.Loader != null ? TableManager.Loader.GenerationLevel : 1;
        return new RunStart
        {
            level = new Unity.Collections.FixedString128Bytes(runLevel != null ? runLevel.name : ""),
            seed = runSeed, floor = floor, generationLevel = Mathf.Max(1, generation), partySize = PartySize, descend = descend,
        };
    }

    void StartHostRun()
    {
        runLevel = pendingLevel;
        if (runLevel == null) return;
        runActive = true; runEndSent = false; lateJoinSent.Clear(); atExit.Clear(); countdownEnds = -1f;
        runSeed = runLevel.fixedSeed != 0 ? runLevel.fixedSeed : Random.Range(1, int.MaxValue);
        runFloor = 1;
        int party = 0;
        foreach (var p in players) if (p != null && (p.IsOwner || p.Current.ready)) party++;
        PartySize = Mathf.Max(1, party);
        SyncRules();
        CoopMessages.ToGuests(CoopMessages.Rules, rules);
        var start = RunStartFor(1, false);
        foreach (var p in players)
            if (p != null && !p.IsOwner && p.Current.ready)
            {
                lateJoinSent.Add(p.OwnerClientId);
                CoopMessages.ToClient(CoopMessages.RunStart, p.OwnerClientId, start);
            }
        StartCoroutine(BeginRun(start));
    }

    // Everyone: load the run's floor (a first floor, a deeper one, or the current one for a late joiner).
    IEnumerator BeginRun(RunStart start)
    {
        var loader = TableManager.Loader;
        if (loader == null) yield break;
        var level = ResolveLevel(start.level.ToString());
        if (level == null) { Debug.LogError($"[Coop] No level {start.level} in the catalog."); yield break; }
        while (loader.Busy) yield return null;
        PartySize = Mathf.Max(1, start.partySize);
        if (start.descend && !LocalInRun) yield break;
        LocalInRun = true; LocalReady = true;
        // A ghost comes back on the new floor (the loader revives a dead player who goes down).
        EndGhost();
        atExit.Clear(); countdownEnds = -1f;
        loader.CoopLoad(level, start);
        if (IsHost) runFloor = start.floor;
        else
        {
            yield return null;
            while (loader.Busy) yield return null;
            CoopMessages.ToHost(CoopMessages.Ready, new CoopInt { a = CoopWorld.FloorKey });
        }
    }

    // ── A new game ────────────────────────────────────────────────────

    // The intro is about to start on a new game (IntroController): play alone, host (the intro starts for everyone
    // once Begin is pressed) or join someone's. Returns when it is decided; the intro then plays here or not at all
    // (a guest plays the host's when it starts, PlayShared).
    public IEnumerator NewGameLobby()
    {
#if UNITY_EDITOR
        // The toolbar's Co-op New Game: this editor hosts and begins once the virtual players are in.
        if (autoStartInEditor && CoopEditorTesting.NewGame && CoopEditorTesting.IsMainEditor)
        {
            if (!Connected) Host();
            float until = Time.unscaledTime + 180f;
            while (Time.unscaledTime < until && (!Connected || network.ConnectedClientsIds.Count < 1 + CoopEditorTesting.ExpectedGuests)) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            BeginSharedIntro();
            yield break;
        }
        if (CoopEditorTesting.Requested) yield break;
#endif
        // Opened from the main menu's co-op menu: the lobby there is the choice. The host's Begin starts the intro for
        // everyone; a guest plays the host's when it starts.
        if (fromMenuLobby)
        {
            while (MenuLobby || LocalInLobby) yield return null;
            if (Connected && IsHost) BeginSharedIntro();
            yield break;
        }
        // A plain new game goes straight to its intro; the choice is only for one started from the menu's Co-op
        // button (or a build started with -host or -join).
        if (!CoopFromMenu && !CommandLineCoop && !Connected && !Starting && !(network != null && network.IsListening)) yield break;
        CoopFromMenu = false;
        // A connection on its way (Relay takes a moment): the screen waits for it, then shows who is in.
        yield return null;
        var menu = CoopMenuUI.Instance;
        if (menu == null) yield break;
        menu.Open(CoopMenuUI.Mode.NewGame);
        while (menu.IsOpen)
        {
            if (autoBegin > 0 && Connected && IsHost && players.Count >= autoBegin) { menu.Begin(); break; }
            yield return null;
        }
        if (menu.Began && Connected && IsHost) BeginSharedIntro();
    }

    // Host: everyone who has joined plays the intro now, from their own seat.
    void BeginSharedIntro()
    {
        if (!Connected || !IsHost) return;
        IntroShared = true;
        CoopMessages.ToGuests(CoopMessages.IntroBegin, new CoopInt());
    }

    // The intro is being played together (the host started it with guests in).
    public bool IntroShared { get; private set; }

    // Host: the practice board's end, for everyone (IntroController).
    public void SendFinale(bool fromStairs) { if (Connected && IsHost) CoopMessages.ToGuests(CoopMessages.Finale, new CoopInt { a = fromStairs ? 1 : 0 }); }

    // A level by its asset name: the catalog's, or the intro's practice board.
    public static TableLevelData ResolveLevel(string name)
    {
        var loader = TableManager.Loader;
        var level = loader != null && loader.Catalog != null ? System.Array.Find(loader.Catalog.levels, l => l != null && l.name == name) : null;
        if (level == null && IntroController.HasInstance && IntroController.Instance.introDungeon != null && IntroController.Instance.introDungeon.name == name) level = IntroController.Instance.introDungeon;
        if (level == null && WorldManager.HasInstance && WorldManager.Instance.debugDungeons != null) level = System.Array.Find(WorldManager.Instance.debugDungeons, l => l != null && l.name == name);
        return level;
    }

    public void ReachedStairs()
    {
        if (!Connected || !LocalInRun || Ghost) return;
        CoopWorld.Send(new CoopEvent { kind = CoopEventKind.AtExit });
        if (IsHost) PlayerAtExit(LocalSlot);
    }

    public void PlayerAtExit(int slot)
    {
        if (!IsHost || !runActive) return;
        // The practice board has no way down: the stairs bring its end.
        if (IntroController.PracticeRunning && IntroController.HasInstance) { IntroController.Instance.StairsReached(); return; }
        atExit.Add(slot);
        if (countdownEnds < 0f)
        {
            countdownEnds = Time.unscaledTime + descendCountdown;
            CoopMessages.ToGuests(CoopMessages.Countdown, new CoopInt { a = Mathf.CeilToInt(descendCountdown), b = slot });
            ShowCountdown(Mathf.CeilToInt(descendCountdown), slot);
        }
    }

    // Someone reached the stairs: who, and how long the rest have (counted down on screen from five).
    void ShowCountdown(int seconds, int slot)
    {
        var loader = TableManager.Loader;
        bool down = loader != null && loader.HasNextFloor;
        string when = down ? $"Going down in {seconds} seconds" : $"Leaving in {seconds} seconds";
        var who = PlayerInSlot(slot);
        string line = who == null || who.IsOwner ? $"{when}. Waiting for the others." : $"{who.DisplayName} is at the stairs. {when}.";
        NotificationUI.Show(line);
        MessageLog.Post(line, MessageKind.Info);
        countdownShownUntil = Time.unscaledTime + seconds;
        countdownShownLast = seconds;
        countdownDown = down;
    }

    float countdownShownUntil = -1f;
    int countdownShownLast;
    bool countdownDown;

    void TickCountdown()
    {
        if (countdownShownUntil < 0f) return;
        int left = Mathf.CeilToInt(countdownShownUntil - Time.unscaledTime);
        if (left <= 0 || (TableManager.Loader != null && TableManager.Loader.Busy)) { countdownShownUntil = -1f; return; }
        if (left < countdownShownLast && left <= 5) NotificationUI.Show(countdownDown ? $"Going down in {left}" : $"Leaving in {left}");
        countdownShownLast = left;
    }

    void HostStairs()
    {
        if (countdownEnds < 0f) return;
        var loader = TableManager.Loader;
        var exit = loader != null && loader.Dungeon != null ? loader.Dungeon.ExitPoint : Vector3.zero;
        bool everyone = true;
        foreach (var c in CoopWorld.Party())
            if ((c.transform.position - exit).sqrMagnitude > stairsRadius * stairsRadius) everyone = false;
        if (!everyone && Time.unscaledTime < countdownEnds) return;
        countdownEnds = -1f; atExit.Clear();
        if (loader != null && loader.HasNextFloor)
        {
            var start = RunStartFor(runFloor + 1, true);
            foreach (var p in players) if (p != null && !p.IsOwner && p.Current.inRun) CoopMessages.ToClient(CoopMessages.RunStart, p.OwnerClientId, start);
            StartCoroutine(BeginRun(start));
        }
        else EndRun(true);
    }

    void HostDeaths()
    {
        if (runEndSent) return;
        bool anyone = false, anyAlive = false;
        foreach (var p in players)
        {
            if (p == null) continue;
            bool inRun = p.IsOwner ? LocalInRun : p.Current.inRun;
            if (!inRun) continue;
            anyone = true;
            bool alive = p.IsOwner ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) is Player t && t.IsAlive : p.Current.alive;
            if (alive) anyAlive = true;
        }
        if (anyone && !anyAlive && !(TableManager.Loader != null && TableManager.Loader.Busy)) EndRun(false);
    }

    // A guest left the level from the pause menu: no longer in the party's run (their state says so to the host).
    public void LeftRun()
    {
        EndGhost();
        LocalInRun = false; LocalReady = false; LocalAtTable = false;
    }

    // Host: the run is won without the stairs (TableLevelLoader.CompleteRun, an arena's fight): it ends for everyone.
    public void WinRun() { if (IsHost && runActive) EndRun(true); }

    void EndRun(bool victory)
    {
        if (runEndSent) return;
        runEndSent = true;
        // Shared loot: the party's spoils, pooled and split (a = victory | how many shared << 1, b = each one's share).
        var (share, with) = SpoilsShares();
        CoopMessages.ToGuests(CoopMessages.RunEnd, new CoopInt { a = (victory ? 1 : 0) | (with << 1), b = share });
        RunEnded(victory, share, with);
    }

    // Everyone: the run is over (all dead, or the last stairs). The recap's button takes each player back to the room.
    // Host: with shared spoils, each one's share of everything the party carries out plus the spoils of the health slain
    // (every player's count already holds the whole party's kills): the total split evenly, rounded up so none is lost.
    // (-1, 1): not shared, each keeps their own.
    (int share, int with) SpoilsShares()
    {
        if (!SharedSpoils || !ProgressionManager.MetaActive || !ProgressionManager.HasInstance || !RunManager.HasInstance) return (-1, 1);
        int with = 0, carried = 0;
        foreach (var p in players)
        {
            if (p == null) continue;
            if (p.IsOwner) { if (!LocalInRun) continue; carried += CoopPlayer.CarriedSpoils(PlayerManager.Instance.GetPlayer(PlayerKind.Table)); }
            else { if (!p.Current.inRun) continue; carried += p.Current.spoils; }
            with++;
        }
        if (with <= 1) return (-1, 1);
        int total = carried + RunManager.Instance.HealthSlain / Mathf.Max(1, ProgressionManager.Instance.healthPerSpoil);
        return (Mathf.CeilToInt(total / (float)with), with);
    }

    void RunEnded(bool victory, int spoilsShare = -1, int sharedWith = 1)
    {
        if (!LocalInRun) return;
        if (spoilsShare >= 0 && sharedWith > 1 && RunManager.HasInstance) RunManager.Instance.SpoilsShare = (spoilsShare, sharedWith);
        EndGhost();
        LocalInRun = false; LocalReady = false; LocalAtTable = false;
        if (IsHost) { runActive = false; countdownEnds = -1f; }
        if (!RunManager.HasInstance) return;
        if (victory) RunManager.Instance.EndRunInVictory();
        else RunManager.Instance.PlayDeath();
    }

    // ── A dead player ─────────────────────────────────────────────────

    // TableLevelLoader: the local table player died. With someone still fighting, a ghost until the next floor.
    public void LocalDied()
    {
        if (!LocalInRun) return;
        bool othersAlive = false;
        foreach (var p in players) if (p != null && !p.IsOwner && p.Current.inRun && p.Current.alive) othersAlive = true;
        if (othersAlive && !IntroController.PracticeRunning) StartCoroutine(BecomeGhost());
        else NotificationUI.Show("Everyone has fallen");
    }

    // After the death moment the dead player gets up as a ghost where it fell (its body stays for the others).
    IEnumerator BecomeGhost()
    {
        yield return new WaitForSecondsRealtime(2.2f);
        if (!LocalInRun || !Connected || Ghost) yield break;
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (table == null || table.IsAlive) yield break;
        if (ScreenManager.HasInstance) { ScreenManager.Instance.FadeToBlack(.35f); yield return new WaitForSecondsRealtime(.4f); }
        if (!LocalInRun || table.IsAlive) { if (ScreenManager.HasInstance) ScreenManager.Instance.FadeFromBlack(.3f); yield break; }
        Ghost = true;
        // Player.OnDied pushed Dead; a ghost moves and looks about again.
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
        CoopGhost.Begin(table, ghostLightColor, ghostLightIntensity, ghostLightRange, ghostSpeed, ghostLook, ghostLookFade);
        if (ScreenManager.HasInstance) { ScreenManager.Instance.SetDeathEffect(0f); ScreenManager.Instance.FadeFromBlack(.5f); }
        NotificationUI.Show("You are a ghost. You come back when the others reach the next floor.");
    }

    void EndGhost()
    {
        if (!Ghost) return;
        Ghost = false;
        var table = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var ghost = table != null ? table.GetComponent<CoopGhost>() : null;
        if (ghost != null) ghost.End();
        if (ScreenManager.HasInstance) ScreenManager.Instance.SetDeathEffect(0f);
    }
}

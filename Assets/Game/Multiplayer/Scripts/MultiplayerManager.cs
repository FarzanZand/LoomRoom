using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// Co-op for up to four players. One player hosts (their room, their save); the others join and walk the same room,
// sit at the same table and go down into the same dungeon. Owns the session (host, join, leave), the host's rules
// for it, and the run: who is at the table, when the run starts, the stairs, a dead player watching from the room,
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
    public bool sharedLoot = true;
    [Tooltip("On: everyone plays the host's adventurer (the host's unlocked classes and story). Off: each player brings their own.")]
    public bool hostAdventurer = true;
    [Tooltip("Off: only the host's save gains progression (unlocks, deepest floor). On: every player's own save gains what they earned too.")]
    public bool partyProgression = false;

    [Title("Dead players", "Watch the others from the room until the next floor")]
    [Tooltip("The dungeon's roof and ceilings are not drawn for a dead player, so the others can be seen from the room.")]
    public bool hideCeilingsWhenDead = true;
    [Tooltip("Walls above this many tiles are not drawn for a dead player either (0: whole walls).")]
    [Min(0)] public float cutawayTiles = 1;
    [Tooltip("Field of view change while a dead player watches (negative zooms in)...")]
    public float spectatorWatchZoom = -40f;
    [Tooltip("...and while it holds the right mouse button.")]
    public float spectatorZoom = -62f;
    [Tooltip("A dead player's eye height over the room floor while watching (the room player's own units; standing is about 1.6): leaning over the table.")]
    public float spectatorEyeHeight = 2.05f;
    [Tooltip("How quickly a dead player's view turns to follow the teammate it watches (0: free look only). Left click watches the next one.")]
    [Min(0)] public float spectatorFollow = 2.5f;
    [Tooltip("The dungeon's light radius around the watched teammate, against a player's own.")]
    [Min(.1f)] public float spectatorLightRadius = 2.2f;
    [Tooltip("The watched teammate's carried light, this many times brighter and wider for a dead player watching.")]
    [Min(1)] public float spectatorLightBoost = 4f;
    [Tooltip("A dead player walks along the table's west side to stay level with the teammate it watches (room units a second; 0: stays put).")]
    [Min(0)] public float spectatorSlide = 60f;
    [Tooltip("Dead players come back on the next floor.")]
    public bool reviveOnNextFloor = true;

    [Title("Party")]
    [Tooltip("Enemy health added per player after the first (.5 = +50%).")]
    [Min(0)] public float enemyHealthPerPlayer = .5f;
    [Tooltip("Enemies added to a pack per player after the first.")]
    [Min(0)] public int extraEnemiesPerPlayer = 1;
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
    [Tooltip("The shield's turn on the left hand while blocking (the arm is raised, its face to the front); at rest it hangs as Shield Grip says.")]
    public Vector3 shieldBlockRotation = new Vector3(0, -90, 90);
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

    // The main menu's Co-op button: the game opens with the co-op screen (a new game asks before its intro).
    public static bool CoopFromMenu;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => CoopFromMenu = false;

    // The host's Relay join code (null when hosting by address, or not hosting).
    public string JoinCode { get; private set; }
    // Hosting or joining through Relay is under way.
    public bool Starting { get; private set; }

    public bool Connected => network != null && network.IsListening && (network.IsServer || network.IsConnectedClient);
    public bool IsHost => network != null && network.IsServer;
    public int LocalSlot => LocalPlayer != null ? Mathf.Max(0, LocalPlayer.SlotIndex) : 0;
    public CoopPlayer LocalPlayer { get; private set; }
    public IReadOnlyList<CoopPlayer> Players => players;
    public int PartySize { get; private set; } = 1;
    public CoopRules Rules => rules;
    public bool SharedLoot => !Connected || rules.sharedLoot;
    // Empty: everyone shows this player as "Player <slot>" (CoopPlayer.DisplayName).
    public string LocalName => !string.IsNullOrWhiteSpace(playerName) ? playerName.Trim() : "";
    public (int key, int hash) HostFloorHash { get; set; }

    // This player's part in the table: picking a figure, waiting for the run, in it.
    public bool LocalAtTable { get; private set; }
    public bool LocalReady { get; private set; }
    public bool LocalInRun { get; private set; }
    public bool Spectating { get; private set; }
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
    float roomYaw;
    TableLevelData pendingLevel;

    const string NameKey = "coop_name";

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
        SyncRules();
        CommandLine();
    }

    void Start()
    {
        if (network == null) { Debug.LogError("[Coop] MultiplayerManager needs the scene's NetworkManager."); return; }
        network.OnClientConnectedCallback += OnClientConnected;
        network.OnClientDisconnectCallback += OnClientDisconnected;
        network.OnServerStarted += OnStarted;
        network.ConnectionApprovalCallback = Approve;
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged += OnFlagChanged;
#if UNITY_EDITOR
        if (autoStartInEditor && CoopEditorTesting.Requested) StartCoroutine(AutoStart());
#endif
        if (commandHost) Host();
        else if (commandJoin != null) Join(commandJoin);
        if (CoopFromMenu) StartCoroutine(OpenFromMenu());
    }

    // An existing game from the menu's Co-op button: once the player is up in the room, the co-op screen.
    // (A new game asks in the dark before its intro instead, NewGameLobby.)
    IEnumerator OpenFromMenu()
    {
        yield return new WaitForSecondsRealtime(1.5f);
        if (IntroController.Seated) yield break;
        CoopFromMenu = false;
        while (GameManager.HasInstance && GameManager.Instance.State != GameState.Explore) yield return null;
        if (CoopMenuUI.Instance != null && !CoopMenuUI.Instance.IsOpen) CoopMenuUI.Instance.Open(CoopMenuUI.Mode.Pause);
    }

    // A build started with -host (-lan: by address), or -join <code or address> (and -name <name>, -port <port>)
    // goes straight into co-op. Read in Awake, so a new game's intro knows to wait for the connection.
    bool commandHost;
    string commandJoin;
    int autoBegin;   // -autobegin <players>: the host begins a new game by itself once that many are in
    public bool CommandLineCoop => commandHost || commandJoin != null;

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

    void OnDestroy()
    {
        if (network != null)
        {
            network.OnClientConnectedCallback -= OnClientConnected;
            network.OnClientDisconnectCallback -= OnClientDisconnected;
            network.OnServerStarted -= OnStarted;
        }
        if (ProgressionManager.HasInstance) ProgressionManager.Instance.FlagChanged -= OnFlagChanged;
        CoopWorld.Unhook();
        SetCutaway(false);
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

    // The co-op screen changed a rule: the guests get the new set.
    public void RulesChanged()
    {
        SyncRules();
        if (Connected && IsHost) CoopMessages.ToGuests(CoopMessages.Rules, rules);
    }

    void SyncRules() => rules = new CoopRules { sharedLoot = sharedLoot, hostAdventurer = hostAdventurer, partyProgression = partyProgression, cutaway = hideCeilingsWhenDead, cutawayTiles = cutawayTiles };

    void Transport(string host)
    {
        var transport = network.GetComponent<UnityTransport>();
        if (transport != null) transport.SetConnectionData(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim(), port, "0.0.0.0");
    }

    public bool Host()
    {
        if (network == null || network.IsListening || Starting) return false;
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
        if (!ok) NotificationUI.Show("Could not start a game");
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
        if (!network.StartHost()) { JoinCode = null; NotificationUI.Show("Could not start a game"); yield break; }
        GUIUtility.systemCopyBuffer = JoinCode;
        NotificationUI.Show($"Join code {JoinCode} (copied)");
        Debug.Log($"[Coop] Hosting online. Join code {JoinCode}");
    }

    // A join code (Relay) or an address.
    public bool Join(string target)
    {
        if (network == null || network.IsListening || Starting) return false;
        if (CoopRelay.LooksLikeCode(target)) { StartCoroutine(JoinOnline(target)); return true; }
        JoinCode = null;
        Transport(target);
        Configure();
        bool ok = network.StartClient();
        if (!ok) NotificationUI.Show("Could not join");
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
            NotificationUI.Show("No game with that code");
            yield break;
        }
        Configure();
        if (!network.StartClient()) NotificationUI.Show("Could not join");
    }

    void Configure()
    {
        network.NetworkConfig.ConnectionApproval = true;
        network.NetworkConfig.EnableSceneManagement = false;
    }

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

    void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        int count = network.ConnectedClientsIds.Count;
        bool host = request.ClientNetworkId == NetworkManager.ServerClientId;
        response.Approved = host || count < maxPlayers;
        response.CreatePlayerObject = response.Approved;
        if (!response.Approved) response.Reason = "The game is full";
    }

    void OnStarted() { RegisterMessages(); CoopWorld.Hook(); SessionChanged?.Invoke(); }

    void OnClientConnected(ulong client)
    {
        if (client == network.LocalClientId)
        {
            RegisterMessages();
            CoopWorld.Hook();
            if (!network.IsServer) NotificationUI.Show("Joined the game");
        }
        if (network.IsServer && client != NetworkManager.ServerClientId)
        {
            SyncRules();
            CoopMessages.ToClient(CoopMessages.Rules, client, rules);
            CoopMessages.ToClient(CoopMessages.Flags, client, HostFlags());
            CoopWorld.SendRoom(client);
            NotificationUI.Show("A player joined");
        }
        SessionChanged?.Invoke();
    }

    void OnClientDisconnected(ulong client)
    {
        if (network == null) return;
        // The guest lost the host (or was refused).
        if (!network.IsServer && (client == network.LocalClientId || client == NetworkManager.ServerClientId))
        {
            string reason = !string.IsNullOrEmpty(network.DisconnectReason) ? network.DisconnectReason : "The host left the game";
            Ended(true, reason);
            return;
        }
        if (network.IsServer) NotificationUI.Show("A player left");
        SessionChanged?.Invoke();
    }

    // The session is over here: back to playing alone, in the room if a run was on.
    void Ended(bool wasGuest, string message)
    {
        if (!string.IsNullOrEmpty(message)) NotificationUI.Show(message);
        CoopMessages.UnregisterAll(); messagesRegistered = false;
        CoopWorld.Unhook();
        SetCutaway(false);
        bool inRun = LocalInRun, spectating = Spectating;
        if (spectating) StopSpectating();
        LocalAtTable = LocalReady = LocalInRun = Spectating = false;
        runActive = false; countdownEnds = -1f; atExit.Clear(); runEndSent = false;
        players.Clear(); LocalPlayer = null; PartySize = 1; JoinCode = null;
        if (wasGuest) RestoreOwnFlags();
        // The host keeps its own dungeon and plays on alone; a guest (or a host who was watching, dead) goes back to the room.
        bool playOn = !wasGuest && !spectating && PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table
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
        CoopMessages.Register<CoopInt>(CoopMessages.RunEnd, (s, m) => RunEnded(m.a != 0));
        CoopMessages.Register<SnapshotBatch>(CoopMessages.Snapshots, CoopWorld.ReceiveSnapshots);
        CoopMessages.Register<NetDamage>(CoopMessages.EnemyHit, CoopWorld.ReceiveEnemyDamaged);
        CoopMessages.Register<NetDamage>(CoopMessages.Strike, CoopWorld.ReceiveStrike);
        CoopMessages.Register<CoopEvent>(CoopMessages.Event, CoopWorld.ReceiveEvent);
        CoopMessages.Register<EnemySpawn>(CoopMessages.Spawn, CoopWorld.ReceiveSpawn);
        CoopMessages.Register<FlagSet>(CoopMessages.Flags, (s, m) => ApplyHostFlags(m));
        CoopMessages.Register<CoopRules>(CoopMessages.Rules, (s, m) => rules = m);
        CoopMessages.Register<CoopInt>(CoopMessages.Countdown, (s, m) => ShowCountdown(m.a));
        CoopMessages.Register<CoopInt>(CoopMessages.IntroBegin, (s, m) => { if (!network.IsServer && IntroController.HasInstance) IntroController.Instance.PlayShared(); });
        CoopMessages.Register<CoopInt>(CoopMessages.Finale, (s, m) => { if (!network.IsServer && IntroController.HasInstance) IntroController.Instance.RemoteFinale(m.a != 0); });
        CoopMessages.Register<CoopInt>(CoopMessages.FloorHash, (s, m) =>
        {
            if (!network.IsServer) return;
            if (m.a == HostFloorHash.key && m.b != HostFloorHash.hash) Debug.LogWarning($"[Coop] Client {s} built floor {m.a} differently (hash {m.b:X8}, host {HostFloorHash.hash:X8}).");
            else if (m.a == HostFloorHash.key) Debug.Log($"[Coop] Client {s} built the same floor ({m.b:X8}).");
        });
        // A guest finished loading a floor (its reveal included): bring it up to date with what already happened there.
        CoopMessages.Register<CoopInt>(CoopMessages.Ready, (s, m) => { if (network.IsServer) CoopWorld.CatchUp(s, m.a); });
    }

    // ── Players ───────────────────────────────────────────────────────

    public void Register(CoopPlayer player)
    {
        if (!players.Contains(player)) players.Add(player);
        if (player.IsOwner) LocalPlayer = player;
        SessionChanged?.Invoke();
    }

    public void Unregister(CoopPlayer player)
    {
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

    // ── Flags (the host's story and unlocks) ──────────────────────────

    FlagSet HostFlags()
    {
        var keys = new List<string>(); var values = new List<int>();
        if (ProgressionManager.HasInstance) foreach (var pair in ProgressionManager.Instance.AllFlags) { keys.Add(pair.Key); values.Add(pair.Value); }
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
                if (rules.hostAdventurer || set.keys[i].StartsWith("story.") || set.keys[i] == ProgressionManager.TableEnteredFlag || set.keys[i] == DungeonMasterRemarks.SatFlag)
                    progression.SetFlag(set.keys[i], set.values[i]);
            if (rules.hostAdventurer && !set.changes) foreach (var pair in ownFlags) if (System.Array.IndexOf(set.keys, pair.Key) < 0) progression.SetFlag(pair.Key, 0);
        }
        finally { applyingHostFlags = false; }
    }

    void OnFlagChanged(string key, int value)
    {
        if (applyingHostFlags || network == null || !network.IsListening) return;
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
            foreach (var key in now) if (!ownFlags.ContainsKey(key)) progression.SetFlag(key, 0);
            foreach (var pair in ownFlags) progression.SetFlag(pair.Key, pair.Value);
            if (keep) foreach (var pair in earned) if (pair.Value > progression.GetFlag(pair.Key)) progression.SetFlag(pair.Key, pair.Value);
        }
        finally { applyingHostFlags = false; }
        ownFlags = null; earnedFlags.Clear();
    }

    // A guest's save is left alone while it plays someone else's game (AdventureSave asks).
    public bool HoldsSave => Connected && !IsHost;

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
        UpdateSpectating();
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
        // Someone picked a figure while the run is on: they join on the current floor.
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
        bool wasSpectating = Spectating;
        LocalInRun = true; LocalReady = true;
        if (wasSpectating) StopSpectating();
        if (start.descend && !reviveOnNextFloor && wasSpectating) yield break;
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
        if (!Connected || !LocalInRun) return;
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
            CoopMessages.ToGuests(CoopMessages.Countdown, new CoopInt { a = Mathf.CeilToInt(descendCountdown) });
            ShowCountdown(Mathf.CeilToInt(descendCountdown));
        }
    }

    void ShowCountdown(int seconds)
    {
        var loader = TableManager.Loader;
        string where = loader != null && loader.HasNextFloor ? $"Going down in {seconds} seconds" : $"Leaving in {seconds} seconds";
        NotificationUI.Show(where);
        MessageLog.Post(where + ". Get to the stairs.", MessageKind.Info);
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

    void EndRun(bool victory)
    {
        if (runEndSent) return;
        runEndSent = true;
        CoopMessages.ToGuests(CoopMessages.RunEnd, new CoopInt { a = victory ? 1 : 0 });
        RunEnded(victory);
    }

    // Everyone: the run is over (all dead, or the last stairs). The recap's button takes each player back to the room.
    void RunEnded(bool victory)
    {
        if (!LocalInRun) return;
        StopSpectating();
        LocalInRun = false; LocalReady = false; LocalAtTable = false;
        if (IsHost) { runActive = false; countdownEnds = -1f; }
        if (!RunManager.HasInstance) return;
        if (victory) RunManager.Instance.EndRunInVictory();
        else RunManager.Instance.PlayDeath();
    }

    // ── A dead player ─────────────────────────────────────────────────

    // TableLevelLoader: the local table player died. With someone still fighting, watch from the room.
    public void LocalDied()
    {
        if (!LocalInRun) return;
        bool othersAlive = false;
        foreach (var p in players) if (p != null && !p.IsOwner && p.Current.inRun && p.Current.alive) othersAlive = true;
        if (othersAlive) StartCoroutine(Spectate());
        else NotificationUI.Show("Everyone has fallen");
    }

    IEnumerator Spectate()
    {
        yield return new WaitForSecondsRealtime(2.2f);
        if (!LocalInRun || !Connected) yield break;
        var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
        if (table != null && table.IsAlive) yield break;
        Spectating = true;
        if (ScreenManager.HasInstance) { ScreenManager.Instance.FadeToBlack(.35f); yield return new WaitForSecondsRealtime(.4f); }
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Dead);
        PlayerManager.Instance.SwapToPlayerImmediately(PlayerKind.Room);
        var room = PlayerManager.Instance.GetPlayer(PlayerKind.Room);
        if (room != null && roomSpot != Vector3.zero) room.Warp(roomSpot, Quaternion.Euler(0, roomYaw, 0));
        if (room != null && room.Look != null)
        {
            var target = TableManager.Loader != null && TableManager.Loader.Dungeon != null ? TableManager.Loader.Dungeon.transform.position : room.transform.position + room.transform.forward;
            CameraEase.YawPitchTo(room.Look, target, out float yaw, out float pitch);
            room.Look.SetYaw(yaw); room.Look.SetPitch(pitch);
        }
        if (room != null && room.Look != null) room.Look.HeightOverride = spectatorEyeHeight;
        watched = -1;
        WatchNext();
        if (InputManager.HasInstance) { InputManager.Instance.PrimaryPressed -= WatchNext; InputManager.Instance.PrimaryPressed += WatchNext; }
        if (ScreenManager.HasInstance) { ScreenManager.Instance.SetDeathEffect(0f); ScreenManager.Instance.FadeFromBlack(.5f); }
        NotificationUI.Show("You watch from the room. Right mouse button looks closer, left click watches someone else.");
    }

    // The teammate a dead player's view follows (slot), and the next one along.
    int watched = -1;
    void WatchNext()
    {
        if (!Spectating) return;
        CoopPlayer next = null, first = null;
        foreach (var p in players)
        {
            if (p == null || p.IsOwner || !p.Current.inRun || !p.Current.alive) continue;
            if (first == null || p.SlotIndex < first.SlotIndex) first = p;
            if (p.SlotIndex > watched && (next == null || p.SlotIndex < next.SlotIndex)) next = p;
        }
        var pick = next ?? first;
        watched = pick != null ? pick.SlotIndex : -1;
        if (pick != null) NotificationUI.Show($"Watching {pick.DisplayName}");
    }

    [Tooltip("How far from the table's edge a watching player stands (room units).")]
    public float spectatorEdgeGap = 14f;

    void StopSpectating()
    {
        if (!Spectating) return;
        foreach (var p in players) if (p != null && !p.IsOwner) p.Avatar.LightBoost = 1f;
        Spectating = false;
        SetCutaway(false);
        if (InputManager.HasInstance) InputManager.Instance.PrimaryPressed -= WatchNext;
        LightingManager.RadiusCentre = null; LightingManager.RadiusScale = 1f;
        var room = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Room) : null;
        if (room != null && room.CameraRig != null) room.CameraRig.FovOffset = 0f;
        if (room != null && room.Look != null) room.Look.HeightOverride = null;
    }

    void UpdateSpectating()
    {
        if (!Spectating) return;
        bool inRoom = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Room;
        SetCutaway(inRoom && rules.cutaway);
        var room = inRoom ? PlayerManager.Instance.Active : null;
        if (room != null && room.CameraRig != null && InputManager.HasInstance)
        {
            float want = InputManager.Instance.SecondaryHeld && GameManager.HasInstance && GameManager.Instance.GameplayActive ? spectatorZoom : spectatorWatchZoom;
            room.CameraRig.FovOffset = Mathf.Lerp(room.CameraRig.FovOffset, want, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
        }
        // The view keeps turning to the teammate being watched (the one followed dies: the next).
        var target = watched >= 0 ? PlayerInSlot(watched) : null;
        if (target != null && !target.Current.alive) { WatchNext(); target = watched >= 0 ? PlayerInSlot(watched) : null; }
        LightingManager.RadiusCentre = target != null && inRoom ? target.transform.position + Vector3.up * 1.6f : (Vector3?)null;
        LightingManager.RadiusScale = spectatorLightRadius;
        foreach (var p in players) if (p != null && !p.IsOwner) p.Avatar.LightBoost = p == target && inRoom ? spectatorLightBoost : 1f;
        // Along the table's west side (the players' side), level with the one being watched.
        if (room != null && target != null && spectatorSlide > 0 && TableManager.HasInstance && GameManager.HasInstance && GameManager.Instance.GameplayActive)
        {
            var table = TableManager.Instance.GetComponent<BoxCollider>();
            if (table != null)
            {
                var b = table.bounds;
                var want = new Vector3(b.min.x - spectatorEdgeGap, room.transform.position.y, Mathf.Clamp(target.transform.position.z, b.min.z + 6f, b.max.z - 6f));
                var now = room.transform.position;
                if ((want - now).sqrMagnitude > 4f) room.Warp(Vector3.MoveTowards(now, want, spectatorSlide * Time.unscaledDeltaTime));
            }
        }
        if (room != null && room.Look != null && target != null && spectatorFollow > 0 && GameManager.HasInstance && GameManager.Instance.GameplayActive)
        {
            CameraEase.YawPitchTo(room.Look, target.transform.position + Vector3.up, out float yaw, out float pitch);
            float k = 1f - Mathf.Exp(-spectatorFollow * Time.unscaledDeltaTime);
            room.Look.SetYaw(Mathf.LerpAngle(room.Look.YawTransform.eulerAngles.y, yaw, k));
            room.Look.SetPitch(Mathf.Lerp(room.Look.Pitch, pitch, k));
        }
    }

    static readonly int CutawayId = Shader.PropertyToID("_LoomCutaway");
    bool cutawayOn;
    void SetCutaway(bool on)
    {
        var dungeon = TableManager.Loader != null ? TableManager.Loader.Dungeon : null;
        if (on && dungeon != null)
        {
            dungeon.ShowCeilings(false);
            float tile = dungeon.LevelData.architectureTileSize > .1f ? dungeon.LevelData.architectureTileSize : 3f;
            float top = rules.cutawayTiles > 0 ? dungeon.FloorHeight + rules.cutawayTiles * tile : 1e6f;
            Shader.SetGlobalVector(CutawayId, new Vector4(top, rules.cutawayTiles > 0 ? 1 : 0, 0, 0));
            cutawayOn = true;
            return;
        }
        if (!cutawayOn) return;
        cutawayOn = false;
        Shader.SetGlobalVector(CutawayId, Vector4.zero);
        if (dungeon != null) dungeon.ShowCeilings(true);
    }
}

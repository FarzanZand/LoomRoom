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
    [Tooltip("Field of view change while a dead player holds the right mouse button (negative zooms in).")]
    public float spectatorZoom = -38f;
    [Tooltip("A dead player's eye height over the room floor while watching (the room player's own units; standing is about 1.6): leaning over the table.")]
    public float spectatorEyeHeight = 2.05f;
    [Tooltip("How quickly a dead player's view turns to follow the teammate it watches (0: free look only). Left click watches the next one.")]
    [Min(0)] public float spectatorFollow = 2.5f;
    [Tooltip("The dungeon's light radius around the watched teammate, against a player's own.")]
    [Min(.1f)] public float spectatorLightRadius = 2.2f;
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

    protected override void Awake()
    {
        base.Awake();
        // Every co-op window keeps playing while another one has the focus.
        Application.runInBackground = true;
        if (network == null) network = FindAnyObjectByType<NetworkManager>();
        SyncRules();
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

    void SyncRules() => rules = new CoopRules { sharedLoot = sharedLoot, hostAdventurer = hostAdventurer, partyProgression = partyProgression, cutaway = hideCeilingsWhenDead, cutawayTiles = cutawayTiles };

    void Transport(string host)
    {
        var transport = network.GetComponent<UnityTransport>();
        if (transport != null) transport.SetConnectionData(string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim(), port, "0.0.0.0");
    }

    public bool Host()
    {
        if (network == null || network.IsListening) return false;
        if (ProgressionManager.HasInstance && !ProgressionManager.Instance.HasFlag(IntroController.IntroFlag) && !Application.isEditor)
        { NotificationUI.Show("Play the beginning first"); return false; }
        SyncRules();
        Transport(address);
        network.NetworkConfig.ConnectionApproval = true;
        network.NetworkConfig.EnableSceneManagement = false;
        bool ok = network.StartHost();
        if (!ok) NotificationUI.Show("Could not start a game");
        return ok;
    }

    public bool Join(string host)
    {
        if (network == null || network.IsListening) return false;
        Transport(host);
        network.NetworkConfig.ConnectionApproval = true;
        network.NetworkConfig.EnableSceneManagement = false;
        bool ok = network.StartClient();
        if (!ok) NotificationUI.Show("Could not join");
        return ok;
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
        bool inRun = LocalInRun;
        LocalAtTable = LocalReady = LocalInRun = Spectating = false;
        runActive = false; countdownEnds = -1f; atExit.Clear(); runEndSent = false;
        players.Clear(); LocalPlayer = null; PartySize = 1;
        if (wasGuest) RestoreOwnFlags();
        if (inRun && RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended)
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
        CoopMessages.Register<CoopInt>(CoopMessages.FloorHash, (s, m) =>
        {
            if (!network.IsServer) return;
            if (m.a == HostFloorHash.key && m.b != HostFloorHash.hash) Debug.LogWarning($"[Coop] Client {s} built floor {m.a} differently (hash {m.b:X8}, host {HostFloorHash.hash:X8}).");
            else if (m.a == HostFloorHash.key) Debug.Log($"[Coop] Client {s} built the same floor ({m.b:X8}).");
        });
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
            if (rules.hostAdventurer) foreach (var pair in ownFlags) if (System.Array.IndexOf(set.keys, pair.Key) < 0) progression.SetFlag(pair.Key, 0);
        }
        finally { applyingHostFlags = false; }
    }

    void OnFlagChanged(string key, int value)
    {
        if (applyingHostFlags || network == null || !network.IsListening || network.IsServer) return;
        earnedFlags[key] = value;
    }

    // Leaving someone else's game: the guest's own flags come back, plus what it earned there (Party Progression).
    void RestoreOwnFlags()
    {
        if (ownFlags == null || !ProgressionManager.HasInstance) { earnedFlags.Clear(); return; }
        var progression = ProgressionManager.Instance;
        bool keep = rules.partyProgression;
        var now = new List<string>(progression.AllFlags.Keys);
        foreach (var key in now) if (!ownFlags.ContainsKey(key)) progression.SetFlag(key, 0);
        foreach (var pair in ownFlags) progression.SetFlag(pair.Key, pair.Value);
        if (keep) foreach (var pair in earnedFlags) if (pair.Value > progression.GetFlag(pair.Key)) progression.SetFlag(pair.Key, pair.Value);
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
        if (loader == null || loader.Catalog == null) yield break;
        var level = System.Array.Find(loader.Catalog.levels, l => l != null && l.name == start.level.ToString());
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

    void StopSpectating()
    {
        if (!Spectating) return;
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
            float want = InputManager.Instance.SecondaryHeld && GameManager.HasInstance && GameManager.Instance.GameplayActive ? spectatorZoom : 0f;
            room.CameraRig.FovOffset = Mathf.Lerp(room.CameraRig.FovOffset, want, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
        }
        // The view keeps turning to the teammate being watched (the one followed dies: the next).
        var target = watched >= 0 ? PlayerInSlot(watched) : null;
        if (target != null && !target.Current.alive) { WatchNext(); target = watched >= 0 ? PlayerInSlot(watched) : null; }
        LightingManager.RadiusCentre = target != null && inRoom ? target.transform.position + Vector3.up * 1.6f : (Vector3?)null;
        LightingManager.RadiusScale = spectatorLightRadius;
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
            float top = rules.cutawayTiles > 0 ? dungeon.FloorHeight + rules.cutawayTiles * dungeon.LevelData.architectureTileSize : 1e6f;
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

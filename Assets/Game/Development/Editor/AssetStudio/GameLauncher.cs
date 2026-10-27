using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;

// Starting the game and Asset Studio from outside the Editor's menus: the "Asset Studio" toolbar button (left of
// Play), the studio's own Play game button, and the two desktop shortcuts (Tools/Desktop/*.ps1), which call
// GameLauncher.Play or GameLauncher.Studio in the running Editor, or start the Editor with -executeMethod on them.
// Play is the ordinary Play from Room.unity (the save in use, as the Play button does); nothing is built.
[InitializeOnLoad]
public static class GameLauncher
{
    const string RoomScene = "Assets/Game/Scenes/Room.unity";
    const string PlayWhenReadyKey = "LoomRoom.Launcher.PlayWhenReady";

    static GameLauncher()
    {
        // Started from a shortcut with the Editor closed: play once the Editor has finished loading and compiling.
        if (SessionState.GetBool(PlayWhenReadyKey, false)) EditorApplication.update += PlayWhenReady;
        // A toolbar layout saved before the button existed keeps it hidden: show it once per machine (hiding it by
        // hand afterwards sticks).
        EditorApplication.update += RunPending;
    }

    // ── Trying things in the game (Asset Studio's actions) ─────────────
    // An action waits in SessionState (it outlives the reload Play brings) until the game is up, then runs once.
    const string PendingKey = "LoomRoom.Launcher.Pending", PendingSinceKey = "LoomRoom.Launcher.PendingSince";

    static void Pend(string action)
    {
        SessionState.SetString(PendingKey, action);
        SessionState.SetFloat(PendingSinceKey, (float)EditorApplication.timeSinceStartup);
    }

    static Player TablePlayer => PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
    static bool GameReady => EditorApplication.isPlaying && GameManager.HasInstance && PlayerManager.HasInstance && PlayerManager.Instance.Active != null
        && GameManager.Instance.State == GameState.Explore;

    static void RunPending()
    {
        string action = SessionState.GetString(PendingKey, "");
        if (action.Length == 0) return;
        // Given up after two minutes without the game getting there, or when Play stopped for good (a failed start).
        // A toolbar restart stops Play and starts it again a moment later: the action waits through that gap.
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(PendingSinceKey, 0) > 120) { SessionState.EraseString(PendingKey); return; }
        // Never in the session a restart is stopping: the action is for the one that starts next.
        if (DebugDungeonToolbar.RestartPending) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (!EditorApplication.isCompiling && !DebugDungeonToolbar.RestartPending) SessionState.EraseString(PendingKey);
            return;
        }
        var parts = action.Split('|');
        // A place picked on the map: the map view is no Explore state, so this one doesn't wait for one.
        if (parts[0] == "click")
        {
            if (!EditorApplication.isPlaying || !CampaignManager.HasInstance || !CampaignManager.Instance.InMapView) return;
            SessionState.EraseString(PendingKey);
            CampaignManager.Instance.TestClick(parts[1]);
            return;
        }
        if (!GameReady) return;
        switch (parts[0])
        {
            case "give":
                var player = TablePlayer;
                if (player == null || !player.isActiveAndEnabled) return;   // the arena isn't up yet
                SessionState.EraseString(PendingKey);
                Give(AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(parts[1])), player);
                break;
            case "console":
                var loader = TableManager.HasInstance ? TableManager.Loader : null;
                if (loader == null || loader.Busy || TablePlayer == null || !TablePlayer.isActiveAndEnabled) return;
                SessionState.EraseString(PendingKey);
                if (!DevConsole.RunCommand(parts[1])) Debug.LogWarning("[Asset Studio] The dev console isn't there to run: " + parts[1]);
                break;
            case "room":
                var roomLoader = TableManager.HasInstance ? TableManager.Loader : null;
                if (roomLoader == null || roomLoader.Busy || TablePlayer == null || !TablePlayer.isActiveAndEnabled) return;
                SessionState.EraseString(PendingKey);
                Debug.Log("[Asset Studio] " + LoadRoom(roomLoader, AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(parts[1]))));
                break;
            case "run":
                var runLoader = TableManager.HasInstance ? TableManager.Loader : null;
                if (runLoader == null || runLoader.Busy || TablePlayer == null || !TablePlayer.isActiveAndEnabled) return;
                SessionState.EraseString(PendingKey);
                LoadRun(runLoader, parts);
                break;
            case "event":
                var screen = CampaignManager.HasInstance ? CampaignManager.Instance.eventScreen : null;
                if (screen == null || !screen.isActiveAndEnabled) return;
                SessionState.EraseString(PendingKey);
                ShowEvent(screen, AssetDatabase.LoadAssetAtPath<CampaignEvent>(AssetDatabase.GUIDToAssetPath(parts[1])));
                break;
            case "table":
                if (!TableManager.HasInstance) return;
                SessionState.EraseString(PendingKey);
                // table|<place id>|<deaths>|<shut place id>: that many deaths in the place first, and the shut place not
                // risen yet, so the map shows it come up (written into the save in use).
                if (parts.Length > 2 && ProgressionManager.HasInstance && int.TryParse(parts[2], out int deaths))
                {
                    ProgressionManager.Instance.SetFlag(CampaignManager.DeathsPrefix + parts[1], deaths);
                    if (parts.Length > 3 && parts[3].Length > 0) ProgressionManager.Instance.SetFlag(CampaignManager.OpenedPrefix + parts[3], 0);
                }
                TableManager.Instance.Play();
                break;
            case "place":
                // place|<id>: at the table, then (click|<id>) that place picked on the map once it is up.
                if (!TableManager.HasInstance) return;
                Pend("click|" + parts[1]);
                TableManager.Instance.Play();
                break;
            default:
                SessionState.EraseString(PendingKey);
                break;
        }
    }

    static string Give(ItemData item, Player player)
    {
        if (item == null || player == null) return "Nothing to give.";
        bool ok = item.IsConsumable || item.spell != null ? player.Hotbar.TryAdd(item) || player.Bag.TryAdd(item) : player.Bag.TryAdd(item) || player.Hotbar.TryAdd(item);
        string text = ok ? $"Gave {item.DisplayName} to the table player." : "No room in the bag or the hotbar.";
        if (ok) MessageLog.Post(text, MessageKind.Info);
        return text;
    }

    // In the bag now when a run is going; otherwise the Debug arena starts and it is put in the bag there.
    public static string GiveItem(ItemData item)
    {
        if (item == null) return "No item.";
        var player = TablePlayer;
        if (EditorApplication.isPlaying && player != null && player.isActiveAndEnabled) return Give(item, player);
        Pend("give|" + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(item)));
        if (!Launch(DebugDungeonToolbar.StartDebug)) return failure;
        return $"Starting the Debug arena with {item.DisplayName} in the bag.";
    }

    // The Debug arena with just this enemy in front of the player. The pick is only set when the arena really starts
    // (a pick left behind would make the next toolbar Debug spawn only that enemy).
    public static string FightInArena(GameObject enemy)
    {
        if (!RoomOpen()) return NoRoom;
        SessionState.SetString(DebugDungeonSession.StudioEnemyKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(enemy)));
        if (!DebugDungeonToolbar.StartDebug()) { SessionState.EraseString(DebugDungeonSession.StudioEnemyKey); return Busy; }
        return $"Starting the Debug arena with {enemy.name}.";
    }

    // A run opening in this room template, from the Debug arena unless the table player is already playing. Found by its
    // GUID (two folders can hold rooms of the same name), built in a level that uses it (the dev console's loadtemplate).
    public static string PlayRoom(GameObject template)
    {
        if (template == null) return "No room.";
        var loader = TableManager.HasInstance ? TableManager.Loader : null;
        if (GameReady && loader != null && !loader.Busy && TablePlayer != null && TablePlayer.isActiveAndEnabled) return LoadRoom(loader, template);
        Pend("room|" + Guid(template));
        if (!TableRunning && !Launch(DebugDungeonToolbar.StartDebug)) return failure;
        return $"Starting a run in {template.name}.";
    }

    static string LoadRoom(TableLevelLoader loader, GameObject template)
    {
        if (template == null) return "The room to test is gone.";
        var level = DevConsole.LevelFor(template, loader, out int stage);
        if (level == null) return "No generated dungeon to build " + template.name + " in.";
        // A copy that opens in this room, its run starting at the biome that uses it (same name: a checkpoint finds it).
        var copy = Object.Instantiate(level);
        copy.name = level.name;
        copy.startingRoom = template;
        copy.fixedSeed = 0;
        if (stage > 0 && copy.stages != null) copy.stages = copy.stages.Skip(stage).ToArray();
        loader.Load(copy, skipReveal: true);
        return $"Loading a run in {template.name} ({level.name}).";
    }

    // The toolbar's Dungeon button with this level picked.
    public static string PlayLevel(TableLevelData level)
    {
        if (level == null) return "No level.";
        if (!RoomOpen()) return NoRoom;
        EditorPrefs.SetString(DebugDungeonSession.SelectedDungeonKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level)));
        MainToolbar.Refresh("LoomRoom/Debug");
        if (!DebugDungeonToolbar.StartDungeon()) return Busy;
        return $"Generating {level.name} floor 1.";
    }

    // A run of this level opening at one of its stages (0 = the first), from the Debug arena; boss: that stage cut to
    // one floor, so its guardian waits on the first floor. A level without stages has one, its base biome (stage 0).
    // The level itself is not changed (the run plays a copy).
    // force: a room event every room that can have it gets on that run (ForceEvent: ambush, sleep, shrine, trap, dark,
    // eerie), to try it at once.
    public static string PlayStage(TableLevelData level, int stage, bool boss, string force = null)
    {
        if (level == null) return "No level.";
        var biome = !level.HasStages ? level.baseBiome : level.stages != null && stage >= 0 && stage < level.stages.Length ? level.stages[stage]?.biome : null;
        if (!level.HasStages) stage = 0;
        if (!StartRun($"run|{Guid(level)}|{stage}|{(boss ? 1 : 0)}|||{force}")) return failure;
        if (!string.IsNullOrEmpty(force)) return $"Starting {level.name} at {(biome != null ? biome.name : "stage " + (stage + 1))} with {ForcedName(force)}.";
        string where = biome != null ? biome.name : "stage " + (stage + 1);
        return boss ? $"Starting {level.name} at the boss of {where}." : $"Starting {level.name} at {where}.";
    }

    // A run of this biome alone: in a level that has it (from its stage; a base biome in its own level), else in the
    // Dungeon button's pick with just this biome. boss: straight to its guardian.
    public static string PlayBiome(DungeonBiome biome, bool boss)
    {
        if (biome == null) return "No biome.";
        if (AssetStudioWorld.OwnerOf(biome) is TableLevelData owner) return PlayStage(owner, 0, boss);
        foreach (var level in Levels())
            for (int i = 0; level.stages != null && i < level.stages.Length; i++)
                if (level.stages[i] != null && level.stages[i].biome == biome) return PlayStage(level, i, boss);
        var host = Host();
        if (host == null) return "No generated dungeon to build it in.";
        if (!StartRun($"run|{Guid(host)}|0|{(boss ? 1 : 0)}|{Guid(biome)}")) return failure;
        return boss ? $"Starting the boss of {biome.name} (in {host.name})." : $"Starting {biome.name} on its own (in {host.name}).";
    }

    // The event screen over the room, as the campaign map shows it. A preview: every choice can be picked, and none
    // costs or gives anything.
    public static string PreviewEvent(CampaignEvent e)
    {
        if (e == null) return "No event.";
        Pend("event|" + Guid(e));
        if (!EditorApplication.isPlaying && !Launch(DebugDungeonToolbar.StartRoom)) return failure;
        return $"Showing {e.title} (a preview: nothing is paid or given).";
    }

    // The room, then the table: the campaign map on it.
    public static string OpenTable() => OpenTable("table");

    // The table with a shut place's deaths in (CampaignEncounter.deathsToOpen in its Deaths In place) and the place not
    // risen yet: the map raises it with its line. Written into the save in use.
    public static string OpenTableWithDeaths(CampaignEncounter shut)
    {
        if (shut == null || shut.deathsIn == null || string.IsNullOrEmpty(shut.deathsIn.id)) return "That place doesn't wait on deaths anywhere.";
        int deaths = Mathf.Max(0, shut.deathsToOpen);
        string result = OpenTable($"table|{shut.deathsIn.id}|{deaths}|{shut.id}");
        return result.StartsWith("Nothing") ? result : result + $" {deaths} death{(deaths == 1 ? "" : "s")} in {shut.deathsIn.displayName} first.";
    }

    // A run started the way a player starts it: the room, the table, then this place clicked on the campaign map (the
    // map goes and the floor is built on the table). The save in use, as Room does.
    public static string PlayFromTable(CampaignEncounter place)
    {
        if (place == null || string.IsNullOrEmpty(place.id)) return "No place on the map plays it.";
        string result = OpenTable("place|" + place.id);
        return result.StartsWith("Nothing") ? result : $"Going to the table to start {place.displayName}.";
    }

    // The Boss toolbar button: the boss stage in front of the Dungeon Master, nothing else.
    public static string PlayBossStage() => Launch(DebugDungeonToolbar.StartBoss) ? "Starting on the boss stage: walk to its middle." : failure;

    static string OpenTable(string action)
    {
        Pend(action);
        // At the table already means the room player: a run going restarts in the room.
        if ((!EditorApplication.isPlaying || TableRunning) && !Launch(DebugDungeonToolbar.StartRoom)) return failure;
        return EditorApplication.isPlaying && !TableRunning ? "Going to the table." : "Starting the room, then sitting at the table.";
    }

    // A map template played on its own: in a level that plays it, else in a copy of its preview level (or the Dungeon
    // button's pick) laid out by this map.
    public static string PlayMap(DungeonMapTemplate map)
    {
        if (map == null) return "No map.";
        var host = map.previewLevel != null && map.previewLevel.IsDungeon ? map.previewLevel : Host();
        if (host == null) return "No dungeon level to play it in.";
        if (!StartRun($"run|{Guid(host)}|0|0||{Guid(map)}")) return failure;
        return $"Starting {map.name} (in a copy of {host.name}).";
    }

    // ── Starting Play for a test ──
    const string NoRoom = "Nothing started: the game runs in Room.unity, and it wasn't opened (other open scenes kept).";
    const string Busy = "Nothing started: Unity is compiling or already entering Play. Try again in a moment.";
    static string failure = NoRoom;   // why the last Launch started nothing

    static bool TableRunning => EditorApplication.isPlaying && TablePlayer != null && TablePlayer.isActiveAndEnabled;

    // Room.unity open (asking to save other open changes first), or already playing. False: nothing can start.
    static bool RoomOpen()
    {
        if (EditorApplication.isPlaying) return true;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path == RoomScene) return true;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        EditorSceneManager.OpenScene(RoomScene);
        return true;
    }

    // Starts Play in one of the toolbar's modes (restarting it when playing), or drops what was waiting for it.
    static bool Launch(System.Func<bool> start)
    {
        if (!RoomOpen()) { failure = NoRoom; SessionState.EraseString(PendingKey); return false; }
        if (!start()) { failure = Busy; SessionState.EraseString(PendingKey); return false; }
        return true;
    }

    static string Guid(Object asset) => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

    // A run built from the pending action once the table player is up: the Debug arena first unless a run is going.
    static bool StartRun(string action)
    {
        Pend(action);
        return TableRunning || Launch(DebugDungeonToolbar.StartDebug);
    }

    static IEnumerable<TableLevelData> Levels()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<TableLevelCatalog>("Assets/Game/Levels/Resources/TableLevels.asset");
        var listed = catalog != null && catalog.levels != null ? catalog.levels.Where(l => l != null) : Enumerable.Empty<TableLevelData>();
        var all = AssetDatabase.FindAssets("t:TableLevelData", new[] { "Assets/Game/Levels" }).Select(g => AssetDatabase.LoadAssetAtPath<TableLevelData>(AssetDatabase.GUIDToAssetPath(g)));
        return listed.Concat(all).Where(l => l != null && l.IsDungeon).Distinct();
    }

    // A generated dungeon to hold a biome no level has: the Dungeon button's pick, else the first in the catalog.
    static TableLevelData Host()
    {
        var picked = AssetDatabase.LoadAssetAtPath<TableLevelData>(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(DebugDungeonSession.SelectedDungeonKey, "")));
        if (picked != null && picked.IsDungeon && picked.layoutMode != DungeonLayoutMode.Authored) return picked;
        return Levels().FirstOrDefault(l => l.layoutMode != DungeonLayoutMode.Authored);
    }

    // run|level|stage|boss|biome: a copy of the level (same name, so nothing is saved under another), its run starting
    // at that stage, or made of the one biome.
    static void LoadRun(TableLevelLoader loader, string[] parts)
    {
        var level = AssetDatabase.LoadAssetAtPath<TableLevelData>(AssetDatabase.GUIDToAssetPath(parts[1]));
        if (level == null) { Debug.LogWarning("[Asset Studio] The level to test is gone."); return; }
        int.TryParse(parts[2], out int stage);
        bool boss = parts[3] == "1";
        var biome = parts.Length > 4 && parts[4].Length > 0 ? AssetDatabase.LoadAssetAtPath<DungeonBiome>(AssetDatabase.GUIDToAssetPath(parts[4])) : null;
        var map = parts.Length > 5 && parts[5].Length > 0 ? AssetDatabase.LoadAssetAtPath<DungeonMapTemplate>(AssetDatabase.GUIDToAssetPath(parts[5])) : null;
        var copy = Object.Instantiate(level);
        copy.name = level.name;
        copy.fixedSeed = 0;
        if (map != null) { copy.layoutMode = DungeonLayoutMode.Authored; copy.mapTemplate = map; }
        var stages = new List<TableLevelData.BiomeStage>();
        if (biome != null) stages.Add(new TableLevelData.BiomeStage { biome = biome, floors = 2, boss = biome.guardian != null && biome.guardian.enabled });
        else if (level.stages != null)
            foreach (var s in level.stages.Skip(Mathf.Max(0, stage)))
                if (s != null && s.biome != null) stages.Add(new TableLevelData.BiomeStage { biome = s.biome, floors = s.floors, boss = s.boss });
        if (boss && stages.Count > 0) stages = new List<TableLevelData.BiomeStage> { new() { biome = stages[0].biome, floors = 1, boss = true } };
        if (stages.Count > 0) copy.stages = stages.ToArray();
        // No stages: its floors run on its base biome, so the boss test is one floor with the base biome's guardian on it.
        else if (boss) { copy.floors = 1; copy.bossOnLastFloor = true; }
        // A room event forced on: copies of the base biome and of each stage biome with settings of its own, with it set to
        // come wherever it can (the assets are not changed).
        string force = parts.Length > 6 ? parts[6] : "";
        if (force.Length > 0)
        {
            if (copy.baseBiome != null) { var b = Object.Instantiate(copy.baseBiome); b.name = copy.baseBiome.name; ForceEvent(b.roomFeatures, force); copy.baseBiome = b; }
            foreach (var s in stages)
                if (s.biome != null && s.biome.overrideDungeonSettings) { var b = Object.Instantiate(s.biome); b.name = s.biome.name; ForceEvent(b.roomFeatures, force); s.biome = b; }
            if (stages.Count > 0) copy.stages = stages.ToArray();
        }
        var first = stages.Count > 0 ? stages[0].biome : copy.baseBiome;
        Debug.Log($"[Asset Studio] Testing {(map != null ? map.name + " in " : "")}{level.name}{(first != null ? " from " + first.name : "")}{(boss ? ", boss floor" : "")}{(force.Length > 0 ? ", with " + ForcedName(force) : "")}.");
        loader.Load(copy, skipReveal: true);
    }

    public static string ForcedName(string force) => force switch
    {
        "ambush" => "an ambush",
        "sleep" => "every pack asleep",
        "shrine" => "a shrine in every room that can have one",
        "trap" => "every chest trapped",
        "dark" => "every generated room dark",
        "eerie" => "every generated room green-lit",
        _ => force,
    };

    static void ForceEvent(DungeonRoomFeatures f, string force)
    {
        if (f == null) return;
        switch (force)
        {
            case "ambush": f.ambushes = true; f.ambushChance = 1; f.maxAmbushes = 1; break;
            case "sleep": f.sleepingPacks = true; f.sleepChance = 1; f.maxSleepingPacks = 0; f.ambushes = false; break;
            case "shrine": f.shrineBargains = true; f.shrineChance = 1; break;
            case "trap": f.trappedTreasure = true; f.trapChance = 1; break;
            case "dark": f.darkRooms = true; f.darkChance = 1; break;
            case "eerie": f.eerieRooms = true; f.eerieChance = 1; f.darkChance = 0; break;
        }
    }

    static void ShowEvent(CampaignEventUI screen, CampaignEvent e)
    {
        if (e == null) { Debug.LogWarning("[Asset Studio] The event to show is gone."); return; }
        screen.StartCoroutine(Preview());
        IEnumerator Preview()
        {
            var gm = GameManager.HasInstance ? GameManager.Instance : null;
            if (gm != null) gm.Push(GameState.Dialogue);
            try
            {
                // Every choice can be picked (enough spoils shown for the dearest), so each outcome can be read.
                int dearest = e.choices != null ? e.choices.Where(c => c != null).Select(c => c.spoilsCost).DefaultIfEmpty(0).Max() : 0;
                yield return screen.Play(e, () => Mathf.Max(dearest, ProgressionManager.HasInstance ? ProgressionManager.Instance.Spoils : 0), c =>
                {
                    if (c.line != null && !c.line.IsEmpty) DungeonMaster.Say(c.line);
                });
            }
            finally { if (gm != null) gm.Pop(GameState.Dialogue); }
        }
    }

    // Plays the game: Room.unity open (asking to save other open changes first), then Play.
    public static void Play()
    {
        if (EditorApplication.isPlaying) { FocusGame(); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            SessionState.SetBool(PlayWhenReadyKey, true);
            EditorApplication.update -= PlayWhenReady;
            EditorApplication.update += PlayWhenReady;
            return;
        }
        var active = EditorSceneManager.GetActiveScene();
        if (active.path != RoomScene)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(RoomScene);
        }
        EditorApplication.isPlaying = true;
        FocusGame();
    }

    static void PlayWhenReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= PlayWhenReady;
        SessionState.SetBool(PlayWhenReadyKey, false);
        Play();
    }

    // For -executeMethod when the Editor starts from a shortcut: wait until it is ready, then play.
    public static void PlayOnStart()
    {
        SessionState.SetBool(PlayWhenReadyKey, true);
        EditorApplication.update -= PlayWhenReady;
        EditorApplication.update += PlayWhenReady;
    }

    public static void Studio() => AssetStudioWindow.Open().Focus();

    static void FocusGame()
    {
        var gameView = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (gameView != null) EditorWindow.FocusWindowIfItsOpen(gameView);
    }
}

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
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (!EditorApplication.isCompiling && !DebugDungeonToolbar.RestartPending) SessionState.EraseString(PendingKey);
            return;
        }
        if (!GameReady) return;
        var parts = action.Split('|');
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
    // one floor, so its guardian waits on the first floor. The level itself is not changed (the run plays a copy).
    public static string PlayStage(TableLevelData level, int stage, bool boss)
    {
        if (level == null) return "No level.";
        var biome = level.stages != null && stage >= 0 && stage < level.stages.Length ? level.stages[stage]?.biome : null;
        if (!StartRun($"run|{Guid(level)}|{stage}|{(boss ? 1 : 0)}|")) return failure;
        string where = biome != null ? biome.name : "stage " + (stage + 1);
        return boss ? $"Starting {level.name} at the boss of {where}." : $"Starting {level.name} at {where}.";
    }

    // A run of this biome alone: in a level that has it (from its stage), else in the Dungeon button's pick with just
    // this biome. boss: straight to its guardian.
    public static string PlayBiome(DungeonBiome biome, bool boss)
    {
        if (biome == null) return "No biome.";
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
                if (s != null) stages.Add(new TableLevelData.BiomeStage { biome = s.biome, floors = s.floors, boss = s.boss });
        if (boss && stages.Count > 0) stages = new List<TableLevelData.BiomeStage> { new() { biome = stages[0].biome, floors = 1, boss = true } };
        if (stages.Count > 0) copy.stages = stages.ToArray();
        var first = stages.Count > 0 ? stages[0].biome : null;
        Debug.Log($"[Asset Studio] Testing {(map != null ? map.name + " in " : "")}{level.name}{(first != null ? " from " + first.name : "")}{(boss ? ", boss floor" : "")}.");
        loader.Load(copy, skipReveal: true);
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

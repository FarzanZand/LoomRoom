using System.Collections.Generic;
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
        if (!EditorPrefs.GetBool(ToolbarShownKey, false)) EditorApplication.delayCall += ShowToolbarButtonOnce;
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
        // Given up after two minutes without the game getting there (a failed start, Play stopped).
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(PendingSinceKey, 0) > 120 || !EditorApplication.isPlayingOrWillChangePlaymode)
        { if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling) SessionState.EraseString(PendingKey); return; }
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
        DebugDungeonToolbar.StartDebug();
        return $"Starting the Debug arena with {item.DisplayName} in the bag.";
    }

    // The Debug arena with just this enemy in front of the player.
    public static string FightInArena(GameObject enemy)
    {
        SessionState.SetString(DebugDungeonSession.StudioEnemyKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(enemy)));
        DebugDungeonToolbar.StartDebug();
        return $"Starting the Debug arena with {enemy.name}.";
    }

    // A run opening in this room template (the dev console's loadtemplate), from the Debug arena if nothing is running.
    public static string PlayRoom(GameObject template)
    {
        string command = "loadtemplate " + template.name;
        if (GameReady && TablePlayer != null && TablePlayer.isActiveAndEnabled && DevConsole.RunCommand(command)) return $"Loading a run in {template.name}.";
        Pend("console|" + command);
        if (!EditorApplication.isPlaying) DebugDungeonToolbar.StartDebug();
        return $"Starting a run in {template.name}.";
    }

    // The toolbar's Dungeon button with this level picked.
    public static string PlayLevel(TableLevelData level)
    {
        EditorPrefs.SetString(DebugDungeonSession.SelectedDungeonKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(level)));
        MainToolbar.Refresh("LoomRoom/Debug");
        DebugDungeonToolbar.StartDungeon();
        return $"Generating {level.name} floor 1.";
    }

    const string ToolbarShownKey = "LoomRoom.Launcher.ToolbarButtonShown";
    static void ShowToolbarButtonOnce()
    {
        var show = typeof(MainToolbar).GetMethod("SetDisplayedAll", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (show == null) return;
        show.Invoke(null, new object[] { "LoomRoom/Asset Studio", true });
        EditorPrefs.SetBool(ToolbarShownKey, true);
    }

    [MainToolbarElement("LoomRoom/Asset Studio", defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 0)]
    public static IEnumerable<MainToolbarElement> ToolbarButton() => new MainToolbarElement[]
    {
        new MainToolbarButton(new MainToolbarContent("Asset Studio", "Open Asset Studio: the game's whole content library, previews, inspectors and requests to Main."), () => AssetStudioWindow.Open()),
    };

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

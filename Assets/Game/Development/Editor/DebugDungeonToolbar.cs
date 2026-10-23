using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;

[InitializeOnLoad]
public static class DebugDungeonToolbar
{
    const string RestartKey = "LoomRoom.DebugDungeon.Restart";
    const string RestartModeKey = "LoomRoom.DebugDungeon.RestartMode";
    const string RoomScene = "Assets/Game/Scenes/Room.unity";

    // A restart is under way: Play stopped and starts again in another mode as soon as the Editor is ready. Kept set
    // until Play really starts again, so whatever waits for the game (GameLauncher's pending action) keeps waiting.
    public static bool RestartPending => SessionState.GetBool(RestartKey, false);

    static DebugDungeonToolbar()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !RestartPending) return;
            EditorApplication.update -= RestartWhenReady;
            EditorApplication.update += RestartWhenReady;
        };
        // A recompile between stopping and starting again drops the update hook: pick the restart up again.
        if (RestartPending && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.update -= RestartWhenReady;
            EditorApplication.update += RestartWhenReady;
        }
    }

    static void RestartWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        EditorApplication.update -= RestartWhenReady;
        SessionState.SetBool(RestartKey, false);
        Start((DebugDungeonSession.Mode)SessionState.GetInt(RestartModeKey, 0));
    }

    // All the buttons live in the one toolbar element, so the toolbar layout already showing Debug shows the others too.
    [MainToolbarElement("LoomRoom/Debug", defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
    public static IEnumerable<MainToolbarElement> Buttons() => new MainToolbarElement[]
    {
        new MainToolbarButton(new MainToolbarContent("Asset Studio", "Open Asset Studio: the game's whole content library, previews, painting, inspectors and requests to Main."), () => AssetStudioWindow.Open().Focus()),
        new MainToolbarButton(new MainToolbarContent("Debug", "Start the test arena: one open room, enemies from Debug Dungeon Settings standing just outside detection range."), () => StartDebug()),
        new MainToolbarButton(new MainToolbarContent("Dungeon", "Generate the level picked in the dropdown next to it and play it straight away, skipping the room."), () => StartDungeon()),
        new MainToolbarDropdown(new MainToolbarContent(DungeonLabel(), "The level the Dungeon button plays (WorldManager's Debug Dungeons)."), ShowDungeonMenu),
        new MainToolbarButton(new MainToolbarContent("Boss", "Start on the boss stage in front of the Dungeon Master (Boss Debug level). Walk to the middle of the stage and he sets the boss down."), () => StartBoss()),
        new MainToolbarButton(new MainToolbarContent("Room", "Start in the room as the room player on an ordinary morning: no intro, wake-up or character sheet. Uses the normal save."), () => StartRoom()),
        new MainToolbarButton(new MainToolbarContent("Co-op", "Test co-op on this computer: Multiplayer Play Mode's Player 2 is started, this editor hosts and Player 2 joins, both in the room (no intro). More players: Window > Multiplayer > Multiplayer Play Mode."), CoopTesting.Start),
        new MainToolbarButton(new MainToolbarContent("Co-op New", "Test a new co-op game: this editor starts a fresh game and hosts, Player 2 joins, and the intro plays for both."), CoopTesting.StartNewGame),
        new MainToolbarToggle(new MainToolbarContent("Fullscreen", "Play in a borderless Game view covering the whole monitor (F11 toggles it by hand)."), FullscreenGameView.OnPlay, FullscreenGameView.SetOnPlay),
    };

    static string DungeonLabel()
    {
        var level = DebugDungeonSession.SelectedDungeon;
        return level == null ? "(none)" : level.name;
    }

    static void ShowDungeonMenu(UnityEngine.Rect rect)
    {
        var menu = new UnityEditor.GenericMenu();
        var selected = DebugDungeonSession.SelectedDungeon;
        void Pick(TableLevelData picked)
        {
            EditorPrefs.SetString(DebugDungeonSession.SelectedDungeonKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked)));
            MainToolbar.Refresh("LoomRoom/Debug");
        }
        var listed = new List<TableLevelData>();
        foreach (var level in DebugDungeonSession.DebugDungeons)
        {
            if (level == null) continue;
            var picked = level;
            listed.Add(level);
            menu.AddItem(new UnityEngine.GUIContent(level.name), level == selected, () => Pick(picked));
        }
        // A level picked elsewhere (Asset Studio) that isn't on the list: shown too, so the button's pick is visible.
        if (selected != null && !listed.Contains(selected)) menu.AddItem(new UnityEngine.GUIContent(selected.name), true, () => Pick(selected));
        // Every other level, by the act of the campaign place that plays it.
        foreach (var (group, level) in AssetStudioWorld.DungeonsByGroup())
        {
            var picked = level;
            menu.AddItem(new UnityEngine.GUIContent("All levels/" + group + "/" + level.name), level == selected, () => Pick(picked));
        }
        if (menu.GetItemCount() > 0) menu.AddSeparator("");
        menu.AddItem(new UnityEngine.GUIContent("Edit list (WorldManager)"), false, () =>
        {
            if (WorldManager.HasInstance) { Selection.activeObject = WorldManager.Instance; EditorGUIUtility.PingObject(WorldManager.Instance); }
        });
        menu.DropDown(rect);
    }

    // Each returns false when nothing starts (compiling, already entering Play, or the Room scene not opened).
    public static bool StartDebug() => Start(DebugDungeonSession.Mode.Arena);
    public static bool StartDungeon() => Start(DebugDungeonSession.Mode.Dungeon);
    public static bool StartRoom() => Start(DebugDungeonSession.Mode.Room);
    public static bool StartBoss() => Start(DebugDungeonSession.Mode.Boss);

    static bool Start(DebugDungeonSession.Mode mode)
    {
        if (EditorApplication.isPlaying)
        {
            SessionState.SetBool(RestartKey, true);
            SessionState.SetInt(RestartModeKey, (int)mode);
            EditorApplication.isPlaying = false;
            return true;
        }
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return false;
        // The game runs in Room.unity: open it (asking to save other open changes first), as Asset Studio does.
        if (UnityEngine.Object.FindAnyObjectByType<TableManager>(UnityEngine.FindObjectsInactive.Include) == null)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.OpenScene(RoomScene);
            if (UnityEngine.Object.FindAnyObjectByType<TableManager>(UnityEngine.FindObjectsInactive.Include) == null)
            {
                UnityEngine.Debug.LogError("No TableManager in " + RoomScene + ": the Debug, Dungeon, Boss and Room buttons need it.");
                return false;
            }
        }
        SessionState.SetBool(mode switch
        {
            DebugDungeonSession.Mode.Dungeon => DebugDungeonSession.DungeonRequestKey,
            DebugDungeonSession.Mode.Room => DebugDungeonSession.RoomRequestKey,
            DebugDungeonSession.Mode.Boss => DebugDungeonSession.BossRequestKey,
            _ => DebugDungeonSession.RequestKey,
        }, true);
        EditorApplication.isPlaying = true;
        return true;
    }
}

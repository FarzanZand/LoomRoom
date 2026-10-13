using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Toolbars;

[InitializeOnLoad]
public static class DebugDungeonToolbar
{
    const string RestartKey = "LoomRoom.DebugDungeon.Restart";
    const string RestartModeKey = "LoomRoom.DebugDungeon.RestartMode";
    static DebugDungeonToolbar()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(RestartKey, false)) return;
            SessionState.SetBool(RestartKey, false);
            EditorApplication.update -= RestartWhenReady;
            EditorApplication.update += RestartWhenReady;
        };
    }

    static void RestartWhenReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        EditorApplication.update -= RestartWhenReady;
        Start((DebugDungeonSession.Mode)SessionState.GetInt(RestartModeKey, 0));
    }

    // All the buttons live in the one toolbar element, so the toolbar layout already showing Debug shows the others too.
    [MainToolbarElement("LoomRoom/Debug", defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
    public static IEnumerable<MainToolbarElement> Buttons() => new MainToolbarElement[]
    {
        new MainToolbarButton(new MainToolbarContent("Debug", "Start the test arena: one open room, enemies from Debug Dungeon Settings standing just outside detection range."), StartDebug),
        new MainToolbarButton(new MainToolbarContent("Dungeon", "Generate the level picked in the dropdown next to it and play it straight away, skipping the room."), StartDungeon),
        new MainToolbarDropdown(new MainToolbarContent(DungeonLabel(), "The level the Dungeon button plays (WorldManager's Debug Dungeons)."), ShowDungeonMenu),
        new MainToolbarButton(new MainToolbarContent("Room", "Start in the room as the room player on an ordinary morning: no intro, wake-up or character sheet. Uses the normal save."), StartRoom),
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
        foreach (var level in DebugDungeonSession.DebugDungeons)
        {
            if (level == null) continue;
            var picked = level;
            menu.AddItem(new UnityEngine.GUIContent(level.name), level == selected, () =>
            {
                EditorPrefs.SetString(DebugDungeonSession.SelectedDungeonKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked)));
                MainToolbar.Refresh("LoomRoom/Debug");
            });
        }
        if (menu.GetItemCount() > 0) menu.AddSeparator("");
        menu.AddItem(new UnityEngine.GUIContent("Edit list (WorldManager)"), false, () =>
        {
            if (WorldManager.HasInstance) { Selection.activeObject = WorldManager.Instance; EditorGUIUtility.PingObject(WorldManager.Instance); }
        });
        menu.DropDown(rect);
    }

    public static void StartDebug() => Start(DebugDungeonSession.Mode.Arena);
    public static void StartDungeon() => Start(DebugDungeonSession.Mode.Dungeon);
    public static void StartRoom() => Start(DebugDungeonSession.Mode.Room);

    static void Start(DebugDungeonSession.Mode mode)
    {
        if (EditorApplication.isPlaying)
        {
            SessionState.SetBool(RestartKey, true);
            SessionState.SetInt(RestartModeKey, (int)mode);
            EditorApplication.isPlaying = false;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (UnityEngine.Object.FindAnyObjectByType<TableManager>(UnityEngine.FindObjectsInactive.Include) == null)
        {
            UnityEngine.Debug.LogError("Open the Room scene before using the Debug, Dungeon or Room buttons.");
            return;
        }
        SessionState.SetBool(mode switch
        {
            DebugDungeonSession.Mode.Dungeon => DebugDungeonSession.DungeonRequestKey,
            DebugDungeonSession.Mode.Room => DebugDungeonSession.RoomRequestKey,
            _ => DebugDungeonSession.RequestKey,
        }, true);
        EditorApplication.isPlaying = true;
    }
}

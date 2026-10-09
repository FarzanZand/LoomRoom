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

    // Both buttons live in the one toolbar element, so the toolbar layout already showing Debug shows Dungeon too.
    [MainToolbarElement("LoomRoom/Debug", defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
    public static IEnumerable<MainToolbarElement> Buttons() => new MainToolbarElement[]
    {
        new MainToolbarButton(new MainToolbarContent("Debug", "Start the test arena: one open room, enemies from Debug Dungeon Settings standing just outside detection range."), StartDebug),
        new MainToolbarButton(new MainToolbarContent("Dungeon", "Generate WorldManager's Debug Dungeon and play it straight away, skipping the room."), StartDungeon),
        new MainToolbarToggle(new MainToolbarContent("Fullscreen", "Play in a borderless Game view covering the whole monitor (F11 toggles it by hand)."), FullscreenGameView.OnPlay, FullscreenGameView.SetOnPlay),
    };

    public static void StartDebug() => Start(DebugDungeonSession.Mode.Arena);
    public static void StartDungeon() => Start(DebugDungeonSession.Mode.Dungeon);

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
            UnityEngine.Debug.LogError("Open the Room scene before starting the debug dungeon.");
            return;
        }
        SessionState.SetBool(mode == DebugDungeonSession.Mode.Dungeon ? DebugDungeonSession.DungeonRequestKey : DebugDungeonSession.RequestKey, true);
        EditorApplication.isPlaying = true;
    }
}

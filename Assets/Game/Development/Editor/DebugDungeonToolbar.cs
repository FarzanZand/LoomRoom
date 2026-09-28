using UnityEditor;
using UnityEditor.Toolbars;

[InitializeOnLoad]
public static class DebugDungeonToolbar
{
    const string RestartKey = "LoomRoom.DebugDungeon.Restart";
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
        StartDebug();
    }

    [MainToolbarElement("LoomRoom/Debug", defaultDockPosition = MainToolbarDockPosition.Middle, defaultDockIndex = 1)]
    public static MainToolbarElement Button() => new MainToolbarButton(
        new MainToolbarContent("Debug", "Start the test dungeon. Enemies retaliate only when attacked."), StartDebug);

    public static void StartDebug()
    {
        if (EditorApplication.isPlaying)
        {
            SessionState.SetBool(RestartKey, true);
            EditorApplication.isPlaying = false;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (UnityEngine.Object.FindAnyObjectByType<TableManager>(UnityEngine.FindObjectsInactive.Include) == null)
        {
            UnityEngine.Debug.LogError("Open the Room scene before starting the debug dungeon.");
            return;
        }
        SessionState.SetBool(DebugDungeonSession.RequestKey, true);
        EditorApplication.isPlaying = true;
    }
}

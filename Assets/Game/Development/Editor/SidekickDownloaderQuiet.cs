using UnityEditor;
using UnityEngine;

// Synty's Sidekick Tool Downloader makes a window of itself at every script reload without opening it (it checks
// GitHub for a newer tool), and Unity logs "Invalid editor window of type ToolDownloader" for that unopened window each
// time Play starts. Before Play, an unopened one is closed; an open one (Synty > Sidekick Tool Downloader) is left be,
// and the menu makes a new one when asked.
[InitializeOnLoad]
static class SidekickDownloaderQuiet
{
    static SidekickDownloaderQuiet() => EditorApplication.playModeStateChanged += state =>
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            if (window != null && window.GetType().Name == "ToolDownloader" && !Open(window)) Object.DestroyImmediate(window);
    };

    // An opened window has a host view; one made with CreateInstance and never shown has none.
    static bool Open(EditorWindow window)
    {
        var parent = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return parent == null || parent.GetValue(window) != null;
    }
}

using System.Reflection;
using UnityEditor;
using UnityEngine;

// True fullscreen testing in the editor: a borderless Game view (no tab, no toolbar) covering the main
// monitor. The Fullscreen toggle on the main toolbar (next to Debug) opens it on every Play, like a fourth
// play mode; F11 (Tools > LoomRoom > Fullscreen Game View) opens and closes it by hand. It closes when
// Play mode ends.
[InitializeOnLoad]
public static class FullscreenGameView
{
    const string OnPlayKey = "LoomRoom.FullscreenGameView.OnPlay";
    const string MenuToggle = "Tools/LoomRoom/Fullscreen Game View _F11";
    const string MenuOnPlay = "Tools/LoomRoom/Fullscreen On Play";
    static EditorWindow window;

    static FullscreenGameView()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(OnPlayKey, false)) Open();
            else if (state == PlayModeStateChange.ExitingPlayMode) Close();
        };
    }

    [MenuItem(MenuToggle, priority = 200)]
    static void Toggle()
    {
        if (window != null) Close();
        else Open();
    }

    [MenuItem(MenuOnPlay, priority = 201)]
    static void ToggleOnPlay() => SetOnPlay(!OnPlay);

    public static bool OnPlay => EditorPrefs.GetBool(OnPlayKey, false);

    // From the toolbar toggle or the menu. Switched during Play, it takes effect at once.
    public static void SetOnPlay(bool on)
    {
        EditorPrefs.SetBool(OnPlayKey, on);
        if (EditorApplication.isPlaying) { if (on) Open(); else Close(); }
        UnityEditor.Toolbars.MainToolbar.Refresh("LoomRoom/Debug");
    }

    [MenuItem(MenuOnPlay, true)]
    static bool ToggleOnPlayCheck()
    {
        Menu.SetChecked(MenuOnPlay, EditorPrefs.GetBool(OnPlayKey, false));
        return true;
    }

    static void Open()
    {
        if (window != null) return;
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (type == null) { Debug.LogWarning("Fullscreen Game View: no GameView type in this Unity version."); return; }
        window = (EditorWindow)ScriptableObject.CreateInstance(type);
        // The Game view's own toolbar would take the top of the screen.
        type.GetProperty("showToolbar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(window, false);
        window.ShowPopup();
        // Editor windows are placed in points, the screen is measured in pixels.
        var screen = Screen.currentResolution;
        float scale = EditorGUIUtility.pixelsPerPoint;
        var size = new Vector2(screen.width / scale, screen.height / scale);
        // Pinned, or Windows keeps a borderless window above the taskbar.
        window.minSize = window.maxSize = size;
        window.position = new Rect(Vector2.zero, size);
        window.Focus();
    }

    static void Close()
    {
        if (window == null) return;
        window.Close();
        window = null;
    }
}

using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

// True fullscreen testing in the editor: a borderless Game view (no tab, no toolbar) covering the monitor
// the main editor window is on. Unity can't place a window exactly on a monitor whose scaling differs from
// the main one, and its popups sit under the taskbar, so once open the window is snapped to the monitor's
// pixels and kept topmost through the Windows API. The Fullscreen toggle on the main toolbar (next to Debug) opens it on every Play, like a fourth
// play mode; F11 (Tools > LoomRoom > Fullscreen Game View) opens and closes it by hand. It closes when
// Play mode ends.
[InitializeOnLoad]
public static class FullscreenGameView
{
    const string OnPlayKey = "LoomRoom.FullscreenGameView.OnPlay";
    const string MenuToggle = "Tools/LoomRoom/Fullscreen Game View _F11";
    const string MenuOnPlay = "Tools/LoomRoom/Fullscreen On Play";
    const string Title = "LoomRoom Fullscreen";
    static EditorWindow window;

    static FullscreenGameView()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(OnPlayKey, false)) Open();
            // Closed once this round of play mode callbacks is over: the window is a Game view too, and Unity's own
            // Game view callback for it still runs after this one (closing it here threw MissingReferenceException).
            else if (state == PlayModeStateChange.ExitingPlayMode) EditorApplication.delayCall += Close;
        };
    }

    [MenuItem(MenuToggle, priority = 200)]
    static void Toggle()
    {
        if (Find() != null) Close();
        else Open();
    }

    // A script reload forgets the window this class opened, so it is found again by its title.
    static EditorWindow Find()
    {
        if (window != null) return window;
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (type == null) return null;
        foreach (var o in Resources.FindObjectsOfTypeAll(type))
        {
            var w = (EditorWindow)o;
            if (w.titleContent.text == Title) return window = w;
        }
        // A script reload can also give the window back Unity's "Game" title (then it was never closed and stayed on top
        // of everything). It is the only Game view that covers the whole monitor from its corner.
        var monitor = TargetMonitor();
        foreach (var o in Resources.FindObjectsOfTypeAll(type))
        {
            var w = (EditorWindow)o;
            var r = w.position;
            if (!w.maximized && Mathf.Abs(r.x - monitor.x) < 4 && Mathf.Abs(r.y - monitor.y) < 4 && r.width >= monitor.width - 4 && r.height >= monitor.height - 4)
            {
                w.titleContent = new GUIContent(Title);
                return window = w;
            }
        }
        return null;
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
        if (Find() != null) return;
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (type == null) { Debug.LogWarning("Fullscreen Game View: no GameView type in this Unity version."); return; }
        window = (EditorWindow)ScriptableObject.CreateInstance(type);
        // The Game view's own toolbar would take the top of the screen.
        type.GetProperty("showToolbar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(window, false);
        window.titleContent = new GUIContent(Title);
        window.ShowPopup();
        window.position = TargetMonitor();
        window.Focus();
        // The popup's native window exists (and is the active one) once Unity has shown it.
        EditorApplication.delayCall += Snap;
    }

    static void Snap()
    {
        if (window == null || Application.platform != RuntimePlatform.WindowsEditor) return;
        var main = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        var popup = GetActiveWindow();
        if (popup == System.IntPtr.Zero || popup == main) { Debug.LogWarning("Fullscreen Game View: could not find its window to snap."); return; }
        var info = new MonitorInfo { size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(main, 2), ref info)) return;
        var r = info.monitor;
        // HWND_TOPMOST (-1) covers the taskbar; SWP_SHOWWINDOW 0x40, SWP_FRAMECHANGED 0x20.
        SetWindowPos(popup, new System.IntPtr(-1), r.left, r.top, r.right - r.left, r.bottom - r.top, 0x60);
    }

    // Roughly the monitor the main editor window is on, in editor points, as a first placement before Snap.
    static Rect TargetMonitor()
    {
        Rect main = EditorGUIUtility.GetMainWindowPosition();
        var hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
        var info = new MonitorInfo { size = Marshal.SizeOf<MonitorInfo>() };
        var client = new Point();
        if (Application.platform == RuntimePlatform.WindowsEditor && hwnd != System.IntPtr.Zero
            && GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) && ClientToScreen(hwnd, ref client))
        {
            float scale = GetDpiForWindow(hwnd) / 96f;
            var r = info.monitor;
            return new Rect(main.x - (client.x - r.left) / scale, main.y - (client.y - r.top) / scale,
                            (r.right - r.left) / scale, (r.bottom - r.top) / scale);
        }
        var screen = Screen.currentResolution;
        float ppp = EditorGUIUtility.pixelsPerPoint;
        return new Rect(0, 0, screen.width / ppp, screen.height / ppp);
    }

    [StructLayout(LayoutKind.Sequential)] struct Point { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct Box { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int size; public Box monitor, work; public uint flags; }
    [DllImport("user32.dll")] static extern System.IntPtr MonitorFromWindow(System.IntPtr window, uint flags);   // 2 = nearest
    [DllImport("user32.dll")] static extern bool ClientToScreen(System.IntPtr window, ref Point point);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(System.IntPtr window);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(System.IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern System.IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern bool SetWindowPos(System.IntPtr window, System.IntPtr after, int x, int y, int width, int height, uint flags);

    static void Close()
    {
        if (Find() == null) return;
        window.Close();
        window = null;
    }
}

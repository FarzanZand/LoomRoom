using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

// True fullscreen testing in the editor: a borderless Game view (no tab, no toolbar) covering the monitor
// the main editor window is on. Unity can't place a window exactly on a monitor whose scaling differs from
// the main one, and its popups sit under the taskbar, so once open the window is snapped to the monitor's
// pixels and kept topmost through the Windows API. The Fullscreen toggle on the main toolbar (next to Debug) opens it on every Play, like a fourth
// play mode; F11 (a shortcut, no menu entry) opens and closes it by hand. It closes when
// Play mode ends.
[InitializeOnLoad]
public static class FullscreenGameView
{
    const string OnPlayKey = "LoomRoom.FullscreenGameView.OnPlay";
    const string Title = "LoomRoom Fullscreen";
    static EditorWindow window;

    static FullscreenGameView()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(OnPlayKey, false)) Open();
            // Closed once this round of play mode callbacks is over: the window is a Game view too, and Unity's own
            // Game view callback for it still runs after this one (closing it here threw MissingReferenceException).
            // Unless Play has started again meanwhile: put off while the editor was unfocused, the close landed in the next
            // Play and shut its new window.
            else if (state == PlayModeStateChange.ExitingPlayMode) EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) Close(); };
        };
        // It never stays without Play (the user, 2026-11-11: "stuck in full screen mode again, happens often"): the close
        // above is a delayCall, which Unity skips while the editor is not focused and loses in a script reload, and a
        // reload also forgets the window. So once a second, while not playing, a fullscreen window still open is closed.
        EditorApplication.update += CloseWithoutPlay;
        DevPrompt.EditorCapture = Capture;
    }

    // F9's screenshot while the window is open (DevPrompt.EditorCapture): the frame it drew, read from its own render
    // texture (the whole picture, HUD included, at the window's size), turned the right way up. Null when it is closed.
    static Texture2D Capture()
    {
        var w = Find();
        if (w == null) return null;
        var playModeView = typeof(Editor).Assembly.GetType("UnityEditor.PlayModeView");
        var rt = playModeView?.GetField("m_TargetTexture", Any)?.GetValue(w) as RenderTexture;
        if (rt == null || !rt.IsCreated()) return null;
        var was = RenderTexture.active;
        RenderTexture.active = rt;
        var shot = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        shot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = was;
        // The view's texture is stored upside down where textures start at the top (Direct3D, the team's editors).
        if (!SystemInfo.graphicsUVStartsAtTop) { shot.Apply(); return shot; }
        var px = shot.GetPixels32();
        var flipped = new Color32[px.Length];
        for (int y = 0; y < rt.height; y++) System.Array.Copy(px, y * rt.width, flipped, (rt.height - 1 - y) * rt.width, rt.width);
        shot.SetPixels32(flipped);
        shot.Apply();
        return shot;
    }

    // ── VSync ──
    // The game turns VSync on (UserSettings), but the editor's Game view ignores that and has its own VSync switch
    // (its aspect menu), off by default, so panning fast tore the picture in the editor (the user, 2026-11-12). It is
    // switched on once per computer; switched off by hand after that (to measure fps past the screen's rate), it stays off.
    const string VSyncSetKey = "LoomRoom.GameViewVSyncSet";

    static PropertyInfo VSync(System.Type gameView) =>
        gameView.GetProperty("vSyncEnabled", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    static bool vsyncChecked;   // once a reload

    static void VSyncOnce()
    {
        vsyncChecked = true;
        if (EditorPrefs.GetBool(VSyncSetKey, false)) return;
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        var vsync = type == null ? null : VSync(type);
        if (vsync == null) return;
        var views = Resources.FindObjectsOfTypeAll(type);
        if (views.Length == 0) return;   // no Game view open yet: tried again after the next reload
        foreach (var o in views) if (Hosted((EditorWindow)o)) vsync.SetValue(o, true);
        EditorPrefs.SetBool(VSyncSetKey, true);
    }

    static bool DockedVSync(System.Type gameView)
    {
        var vsync = VSync(gameView);
        foreach (var o in Resources.FindObjectsOfTypeAll(gameView))
            if (((EditorWindow)o).titleContent.text != Title && Hosted((EditorWindow)o) && vsync?.GetValue(o) is bool on) return on;
        return true;
    }

    static double nextCheck;
    static bool strayChecked;   // once after each reload: a cover tab left in a saved layout
    static void CloseWithoutPlay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!vsyncChecked) VSyncOnce();   // here, not in a delayCall: those wait while the editor is unfocused
        if (Find() != null) Close();
        else if (SessionState.GetString(HiddenKey, "").Length > 0) ShowHiddenViews();   // the window went without its Close (a reload): the views it covered come back
        else if (!strayChecked) { strayChecked = true; foreach (var stray in Resources.FindObjectsOfTypeAll<FullscreenCover>()) RemoveCover(stray); }
    }

    [UnityEditor.ShortcutManagement.Shortcut("LoomRoom/Fullscreen Game View", KeyCode.F11)]
    static void Toggle()
    {
        if (Find() != null) Close();
        else Open();
    }

    // A script reload forgets the window this class opened, so it is found again by its title.
    static EditorWindow Find()
    {
        if (window != null && Hosted(window)) return window;
        var type = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (type == null) return null;
        foreach (var o in Resources.FindObjectsOfTypeAll(type))
        {
            var w = (EditorWindow)o;
            if (w.titleContent.text == Title && Hosted(w)) return window = w;
        }
        // A script reload can also give the window back Unity's "Game" title (then it was never closed and stayed on top
        // of everything). It is the only floating Game view with its toolbar hidden, or else the only one that covers the
        // monitor from its corner: at least nine tenths of its height, since Unity may report it fitted to the monitor
        // less the taskbar (1032 points of 1080; asked to cover all of it, the clean-up missed it, the user, 2026-11-12).
        var monitor = TargetMonitor();
        var toolbar = type.GetProperty("showToolbar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        foreach (var o in Resources.FindObjectsOfTypeAll(type))
        {
            var w = (EditorWindow)o;
            if (!Hosted(w) || w.docked || w.maximized) continue;
            var r = w.position;
            bool noToolbar = toolbar != null && toolbar.GetValue(w) is bool shown && !shown;
            bool covers = Mathf.Abs(r.x - monitor.x) < 4 && Mathf.Abs(r.y - monitor.y) < 4 && r.width >= monitor.width - 4 && r.height >= monitor.height * .9f;
            if (noToolbar || covers)
            {
                w.titleContent = new GUIContent(Title);
                return window = w;
            }
        }
        return null;
    }

    // A Game view with no window round it (made and never shown, or left from a torn-down one) is not ours: taken for the
    // fullscreen window, closing it threw every second (100 errors in the user's console, 2026-11-12).
    static bool Hosted(EditorWindow w) => w != null && typeof(EditorWindow).GetField("m_Parent", Any)?.GetValue(w) != null;

    public static bool OnPlay => EditorPrefs.GetBool(OnPlayKey, false);

    // From the toolbar toggle. Switched during Play, it takes effect at once.
    public static void SetOnPlay(bool on)
    {
        EditorPrefs.SetBool(OnPlayKey, on);
        if (EditorApplication.isPlaying) { if (on) Open(); else Close(); }
        UnityEditor.Toolbars.MainToolbar.Refresh("LoomRoom/Debug");
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
        // VSync as the docked Game view has it (on unless switched off there, say for measuring fps): a new Game view
        // starts with it off, and panning fast then tears the picture. Only once shown: set before, Unity threw (the view
        // has no host yet) and the window was left half made, with no fullscreen.
        try { VSync(type)?.SetValue(window, DockedVSync(type)); }
        catch (System.Exception e) { Debug.LogWarning("Fullscreen Game View: VSync not set: " + (e.InnerException ?? e).Message); }
        window.position = TargetMonitor();
        window.Focus();
        HideCoveredViews();
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
        ShowHiddenViews();
        if (Find() == null) return;
        window.Close();
        window = null;
    }

    // ── The views it covers ──
    // While the fullscreen window is open the main editor window lies behind it, yet Unity kept drawing its Game view
    // (the whole game a second time, about 2.7 ms a frame on the 4090) and its Scene view (the world again, about 2 ms):
    // measured 2026-11-12, the user's fps. So each docked Game view is pointed at a display no camera draws to, and
    // an empty tab (FullscreenCover) is put in front of each Scene view, and both are put back as the window closes. What
    // was changed is kept in SessionState, so a script reload during Play still puts it back.
    const string HiddenKey = "LoomRoom.FullscreenGameView.Hidden";
    const int EmptyDisplay = 7;   // "Display 8": no camera of the game draws there
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static void HideCoveredViews()
    {
        if (SessionState.GetString(HiddenKey, "").Length > 0) return;
        var hidden = new System.Text.StringBuilder();
        var gameView = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
        if (gameView != null)
            foreach (var o in Resources.FindObjectsOfTypeAll(gameView))
            {
                var w = (EditorWindow)o;
                if (w == window || w.titleContent.text == Title) continue;
                var display = TargetDisplay(w);
                if (display == null || (int)display.GetValue(w) == EmptyDisplay) continue;
                hidden.Append("g:").Append(EntityId.ToULong(w.GetEntityId())).Append(':').Append((int)display.GetValue(w)).Append(';');
                display.SetValue(w, EmptyDisplay);
                w.Repaint();
            }
        foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>())
        {
            var dock = Dock(view, out var panes, out var selected);
            if (dock == null || panes[(int)selected.GetValue(dock)] != (object)view) continue;
            var addTab = dock.GetType().GetMethod("AddTab", Any, null, new[] { typeof(EditorWindow), typeof(bool) }, null);
            if (addTab == null) continue;
            var cover = ScriptableObject.CreateInstance<FullscreenCover>();
            cover.titleContent = new GUIContent("Playing fullscreen");
            addTab.Invoke(dock, new object[] { cover, true });
            int at = panes.IndexOf(cover);
            if (at < 0) { Object.DestroyImmediate(cover); continue; }
            selected.SetValue(dock, at);
            hidden.Append("s:").Append(EntityId.ToULong(view.GetEntityId())).Append(':').Append(EntityId.ToULong(cover.GetEntityId())).Append(';');
        }
        SessionState.SetString(HiddenKey, hidden.ToString());
    }

    static void ShowHiddenViews()
    {
        string hidden = SessionState.GetString(HiddenKey, "");
        if (hidden.Length == 0) return;
        SessionState.EraseString(HiddenKey);
        foreach (var entry in hidden.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var part = entry.Split(':');
            if (part.Length < 2 || !ulong.TryParse(part[1], out var raw)) continue;
            var w = EditorUtility.EntityIdToObject(EntityId.FromULong(raw)) as EditorWindow;
            if (w == null) continue;
            if (part[0] == "g" && part.Length > 2 && int.TryParse(part[2], out var display))
            {
                TargetDisplay(w)?.SetValue(w, display);
                w.Repaint();
            }
            else if (part[0] == "s")
            {
                var cover = part.Length > 2 && ulong.TryParse(part[2], out var coverId) ? EditorUtility.EntityIdToObject(EntityId.FromULong(coverId)) as FullscreenCover : null;
                var dock = Dock(w, out var panes, out var selected);
                int at = dock != null ? panes.IndexOf(w) : -1;
                if (at >= 0) selected.SetValue(dock, at);
                RemoveCover(cover);
            }
        }
        // Any cover left from an earlier time (a crash or a quit during Play, with the layout saved).
        foreach (var stray in Resources.FindObjectsOfTypeAll<FullscreenCover>()) RemoveCover(stray);
    }

    static void RemoveCover(FullscreenCover cover)
    {
        if (cover == null) return;
        var dock = Dock(cover, out var panes, out _);
        var removeTab = dock?.GetType().GetMethod("RemoveTab", Any, null, new[] { typeof(EditorWindow) }, null);
        if (removeTab != null && panes.Contains(cover)) removeTab.Invoke(dock, new object[] { cover });
        if (cover != null) Object.DestroyImmediate(cover);
    }

    static PropertyInfo TargetDisplay(EditorWindow w)
    {
        for (var t = w.GetType(); t != null; t = t.BaseType)
        {
            var p = t.GetProperty("targetDisplay", Any | BindingFlags.DeclaredOnly);
            if (p != null && p.PropertyType == typeof(int) && p.CanWrite) return p;
        }
        return null;
    }

    // The dock area holding a window, its tabs and the property saying which tab shows; null when it isn't in one.
    static object Dock(EditorWindow w, out System.Collections.IList panes, out PropertyInfo selected)
    {
        panes = null; selected = null;
        var dock = typeof(EditorWindow).GetField("m_Parent", Any)?.GetValue(w);
        if (dock == null) return null;
        panes = dock.GetType().GetField("m_Panes", Any)?.GetValue(dock) as System.Collections.IList;
        selected = dock.GetType().GetProperty("selected", Any);
        return panes != null && selected != null && selected.PropertyType == typeof(int) ? dock : null;
    }
}

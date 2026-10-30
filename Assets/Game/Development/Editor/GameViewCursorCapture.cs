using UnityEditor;
using UnityEngine;

// In the editor a cursor lock only takes hold once the Game view has had a click: after pressing Play (that click is on
// the toolbar) or coming back to the editor without clicking into the game, GameManager's lock was set and read back as
// Locked, yet the cursor showed until the first click in the game, which was an attack (the user's F9 cd5c933a, 2026-10-30).
// A build has no such rule. So whenever the game wants the cursor locked and the Game view has just gained the focus
// (Play started, the editor came back to the front, or the focus moved to it from another panel), the Game view is sent
// an editor event click: the editor captures the cursor for the lock, and the game's input never sees it (no swing).
[InitializeOnLoad]
public static class GameViewCursorCapture
{
    static bool wasActive, wasOnGameView, pending;

    static GameViewCursorCapture() => EditorApplication.update += Update;

    static void Update()
    {
        if (!EditorApplication.isPlaying) { wasActive = wasOnGameView = pending = false; return; }
        bool active = UnityEditorInternal.InternalEditorUtility.isApplicationActive;
        var focused = EditorWindow.focusedWindow;
        bool onGameView = active && focused != null && focused.GetType().Name == "GameView";
        // Gained: the editor came back, or the Game view took the focus (pressing Play focuses it).
        if (onGameView && (!wasActive || !wasOnGameView)) pending = true;
        if (!onGameView) pending = false;
        wasActive = active; wasOnGameView = onGameView;
        // Waits until the game asks for the lock (a menu open as the focus came back keeps the cursor free).
        if (!pending || Cursor.lockState != CursorLockMode.Locked) return;
        pending = false;
        var centre = new Vector2(focused.position.width * .5f, focused.position.height * .5f);
        focused.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = centre, clickCount = 1 });
        focused.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = centre, clickCount = 1 });
    }
}

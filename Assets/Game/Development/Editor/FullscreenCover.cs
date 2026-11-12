using UnityEditor;

// An empty tab FullscreenGameView puts in front of each Scene view while the fullscreen Game view is open, so the
// editor stops drawing the world in it behind the fullscreen window (about 2 ms a frame on the 4090, 2026-11-12). Any
// tab of the user's own layout in its place could cost as much (the Animator window drew itself every frame: 2.8 ms).
// Removed again as the fullscreen window closes; one left over from a crash or a quit during Play goes once Play is over.
public class FullscreenCover : EditorWindow
{
    void OnGUI() { }
}

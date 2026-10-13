using UnityEngine;

// Testing co-op on one computer with Multiplayer Play Mode (Window > Multiplayer > Multiplayer Play Mode, or the
// toolbar's Co-op button): every virtual player starts in the room with no intro and joins the main editor, which hosts.
public static class CoopEditorTesting
{
    public const string RequestKey = "LoomRoom.Coop.Test";
    // Read once as Play mode starts (the toolbar's request is cleared then).
    public static bool Requested { get; private set; }

#if UNITY_EDITOR
    // A virtual player (an editor clone made by Multiplayer Play Mode).
    public static bool IsVirtualPlayer => !Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
    public static bool IsMainEditor => Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor;
#else
    public static bool IsVirtualPlayer => false;
    public static bool IsMainEditor => false;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Read()
    {
        Requested = false;
#if UNITY_EDITOR
        Requested = IsVirtualPlayer || UnityEditor.SessionState.GetBool(RequestKey, false);
        UnityEditor.SessionState.SetBool(RequestKey, false);
#endif
    }
}

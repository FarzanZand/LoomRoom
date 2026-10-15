using UnityEngine;

// Testing co-op on one computer with Multiplayer Play Mode (Window > Multiplayer > Multiplayer Play Mode, or the
// toolbar's Co-op button): every virtual player starts in the room with no intro and joins the main editor, which hosts.
public static class CoopEditorTesting
{
    public const string RequestKey = "LoomRoom.Coop.Test", NewGameKey = "LoomRoom.Coop.NewGame", GuestsKey = "LoomRoom.Coop.Guests";
    // Read once as Play mode starts (the toolbar's request is cleared then).
    public static bool Requested { get; private set; }
    // Co-op New Game: the main editor starts a new game (the intro) and hosts it; the virtual players join it.
    public static bool NewGame { get; private set; }
    // Virtual players the main editor waits for before the intro begins.
    public static int ExpectedGuests { get; private set; }

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
        Requested = false; NewGame = false; ExpectedGuests = 0;
#if UNITY_EDITOR
        // Play started on the main menu plays like a player: no auto-host or auto-join (menu tests).
        bool menu = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Main Menu";
        Requested = !menu && (IsVirtualPlayer || UnityEditor.SessionState.GetBool(RequestKey, false));
        NewGame = IsMainEditor && UnityEditor.SessionState.GetBool(NewGameKey, false);
        ExpectedGuests = UnityEditor.SessionState.GetInt(GuestsKey, 1);
        UnityEditor.SessionState.SetBool(RequestKey, false);
        UnityEditor.SessionState.SetBool(NewGameKey, false);
#endif
    }
}

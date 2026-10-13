using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The toolbar's Co-op button: Multiplayer Play Mode's Player 2 (a second editor on this computer) is switched on,
// this editor enters Play mode in the room and hosts, and Player 2 joins (CoopEditorTesting, MultiplayerManager).
// Multiplayer Play Mode's player API is internal, so it is reached by name.
public static class CoopTesting
{
    // Co-op New Game: a fresh save on this editor, which hosts; the intro starts once Player 2 is in.
    public static void StartNewGame() => Begin(true, 1);
    public static void Start() => Begin(false, 1);
    // More virtual players (up to 3: Player 2, 3 and 4).
    public static void StartWith(int guests, bool newGame) => Begin(newGame, Mathf.Clamp(guests, 1, 3));

    static void Begin(bool newGame, int guests)
    {
        if (EditorApplication.isPlaying) { EditorApplication.isPlaying = false; return; }
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (Object.FindAnyObjectByType<MultiplayerManager>(FindObjectsInactive.Include) == null)
        {
            Debug.LogError("No MultiplayerManager in the open scene: run Tools > LoomRoom > Multiplayer > Set Up Co-op first.");
            return;
        }
        if (!ActivatePlayers(guests)) return;
        SessionState.SetBool(CoopEditorTesting.RequestKey, true);
        SessionState.SetBool(CoopEditorTesting.NewGameKey, newGame);
        SessionState.SetInt(CoopEditorTesting.GuestsKey, guests);
        if (!newGame) SessionState.SetBool(DebugDungeonSession.RoomRequestKey, true);
        EditorApplication.isPlaying = true;
    }

    static Type PlaymodeType => Type.GetType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode, UnityEditor.MultiplayerModule");

    // The virtual players (Player 2, 3, 4) and whether each is running.
    public static List<(string name, string state)> Players()
    {
        var result = new List<(string, string)>();
        var all = PlaymodeType?.GetProperty("Players", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Array;
        if (all == null) return result;
        foreach (var p in all)
        {
            var t = p.GetType();
            if (t.GetProperty("Type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(p)?.ToString() != "Clone") continue;
            result.Add((t.GetProperty("Name")?.GetValue(p)?.ToString(), t.GetProperty("PlayerState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(p)?.ToString()));
        }
        return result;
    }

    // Switches on this many virtual players if they aren't already.
    public static bool ActivatePlayers(int count)
    {
        var all = PlaymodeType?.GetProperty("Players", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Array;
        if (all == null) { Debug.LogError("Multiplayer Play Mode is not available (Window > Multiplayer > Multiplayer Play Mode)."); return false; }
        int switched = 0;
        foreach (var p in all)
        {
            var t = p.GetType();
            const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            if (t.GetProperty("Type", any)?.GetValue(p)?.ToString() != "Clone" || switched >= count) continue;
            switched++;
            if (t.GetProperty("PlayerState", any)?.GetValue(p)?.ToString() != "NotLaunched") continue;
            var activate = t.GetMethod("Activate", any);
            if (activate == null) { Debug.LogError("Multiplayer Play Mode: no Activate on its player."); return false; }
            var args = new object[] { null, new List<string>() };
            bool ok = (bool)activate.Invoke(p, args);
            if (!ok) { Debug.LogError($"Multiplayer Play Mode could not start {t.GetProperty("Name")?.GetValue(p)}: {args[0]}"); return false; }
        }
        return true;
    }
}

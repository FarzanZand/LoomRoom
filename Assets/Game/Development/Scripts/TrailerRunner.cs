#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

// Runs editor-authored trailer shots (TrailerShots) as coroutines in Play mode. Created on demand, never saved.
// With Clean on it keeps the HUD, subtitles and prompts hidden every frame, so recordings have clean frames.
// It is an ordinary scene object, so it ends with Play mode: with domain reload off, a DontSave runner used to
// survive into later Play sessions and keep the HUD (and the Dungeon Master's lines) hidden.
public class TrailerRunner : MonoBehaviour
{
    static TrailerRunner instance;
    public static TrailerRunner Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject("Trailer runner");
            return instance = go.AddComponent<TrailerRunner>();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    void OnDestroy()
    {
        Restore();
        if (instance == this) instance = null;
    }

    public string Status { get; set; } = "idle";
    public bool Clean { get; set; }
    // UI/Canvas children that stay visible in clean frames.
    public readonly HashSet<string> keep = new() { "Run recap", "Figure card", "Announcement" };
    readonly List<CanvasGroup> hidden = new();
    readonly List<Canvas> hiddenCanvases = new();

    void LateUpdate()
    {
        if (!Clean) { Restore(); return; }
        var canvas = GameObject.Find("UI/Canvas");
        if (canvas != null)
            foreach (Transform child in canvas.transform)
            {
                if (keep.Contains(child.name)) continue;
                var g = child.GetComponent<CanvasGroup>();
                if (g == null) g = child.gameObject.AddComponent<CanvasGroup>();
                if (g.alpha != 0) { g.alpha = 0; if (!hidden.Contains(g)) hidden.Add(g); }
            }
        foreach (var hud in FindObjectsByType<DungeonHud>())
            foreach (var c in hud.GetComponentsInChildren<Canvas>(true))
                if (c.enabled) { c.enabled = false; hiddenCanvases.Add(c); }
    }

    void Restore()
    {
        foreach (var g in hidden) if (g != null) g.alpha = 1;
        hidden.Clear();
        foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
        hiddenCanvases.Clear();
    }
}
#endif

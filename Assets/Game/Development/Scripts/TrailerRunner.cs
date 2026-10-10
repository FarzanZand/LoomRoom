using System.Collections.Generic;
using UnityEngine;

// Runs editor-authored trailer shots (TrailerShots) as coroutines in Play mode. Created on demand, never saved.
// With Clean on it keeps the HUD, subtitles and prompts hidden every frame, so recordings have clean frames.
public class TrailerRunner : MonoBehaviour
{
    static TrailerRunner instance;
    public static TrailerRunner Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject("Trailer runner") { hideFlags = HideFlags.DontSave };
            return instance = go.AddComponent<TrailerRunner>();
        }
    }

    public string Status { get; set; } = "idle";
    public bool Clean { get; set; }
    // UI/Canvas children that stay visible in clean frames.
    public readonly HashSet<string> keep = new() { "Run recap", "Figure card", "Announcement" };
    readonly List<CanvasGroup> hidden = new();

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
        foreach (var hud in FindObjectsByType<DungeonHud>(FindObjectsSortMode.None))
            foreach (var c in hud.GetComponentsInChildren<Canvas>(true)) c.enabled = false;
    }

    void Restore()
    {
        foreach (var g in hidden) if (g != null) g.alpha = 1;
        hidden.Clear();
    }
}

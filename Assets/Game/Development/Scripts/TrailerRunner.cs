using UnityEngine;

// Runs editor-authored trailer shots (TrailerShots) as coroutines in Play mode. Created on demand, never saved.
public class TrailerRunner : MonoBehaviour
{
    public static TrailerRunner Instance
    {
        get
        {
            var existing = FindAnyObjectByType<TrailerRunner>();
            if (existing != null) return existing;
            var go = new GameObject("Trailer runner") { hideFlags = HideFlags.DontSave };
            return go.AddComponent<TrailerRunner>();
        }
    }

    public string Status { get; set; } = "idle";
}

using UnityEngine;

[CreateAssetMenu(fileName = "NewAudioData", menuName = "Audio/Audio Data")]
public class AudioData : ScriptableObject
{
    public AudioClip[] clips;
    [Range(0f, 1f)]  public float volume        = 1f;
    [Range(0f, 2f)]  public float pitch         = 1f;
    [Range(0f, 0.5f)] public float pitchVariance = 0f;

    [Header("3D attenuation (world units)")]
    [Min(.01f)] public float minDistance = 1f;
    [Min(.01f)] public float maxDistance = 500f;

    // A random pitch offset in [-variance, variance]; 0 without variance. Shared by every pitch-varied sound.
    public static float PitchOffset(float variance) => variance > 0f ? Random.Range(-variance, variance) : 0f;

    // This asset's pitch with its variance applied.
    public float RandomPitch() => pitch + PitchOffset(pitchVariance);

    public AudioClip GetClip()
    {
        if (clips == null || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

#if UNITY_EDITOR
    // ── Test buttons (inspector, in or out of Play mode) ──────────────
    // Each press plays it the way the game does: a random clip at the volume, with the pitch and its variance, in 2D.
    // A hidden temporary source per sound, destroyed when it ends (or on Stop).

    static readonly System.Collections.Generic.List<(AudioSource source, double end)> previews = new();
    static readonly System.Collections.Generic.List<(AudioData data, double at)> queued = new();

    [Sirenix.OdinInspector.ButtonGroup("Test"), Sirenix.OdinInspector.Button("Play")]
    void TestPlay() => Queue(0);

    [Sirenix.OdinInspector.ButtonGroup("Test"), Sirenix.OdinInspector.Button("Play 5 in a row")]
    void TestFive() { for (int i = 0; i < 5; i++) Queue(i * .45); }

    [Sirenix.OdinInspector.ButtonGroup("Test"), Sirenix.OdinInspector.Button("Stop")]
    void TestStop()
    {
        queued.Clear();
        foreach (var p in previews) if (p.source != null) DestroyImmediate(p.source.gameObject);
        previews.Clear();
    }

    void Queue(double delay)
    {
        if (clips == null || clips.Length == 0) { Debug.LogWarning($"{name} has no clips.", this); return; }
        queued.Add((this, UnityEditor.EditorApplication.timeSinceStartup + delay));
        UnityEditor.EditorApplication.update -= TestUpdate;
        UnityEditor.EditorApplication.update += TestUpdate;
    }

    static void TestUpdate()
    {
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        for (int i = queued.Count - 1; i >= 0; i--)
        {
            var (data, at) = queued[i];
            if (now < at) continue;
            queued.RemoveAt(i);
            if (data == null) continue;
            var clip = data.GetClip();
            if (clip == null) continue;
            float pitch = data.RandomPitch();
            var go = new GameObject("Audio Data preview") { hideFlags = HideFlags.HideAndDontSave };
            var source = go.AddComponent<AudioSource>();
            source.clip = clip; source.volume = data.volume; source.pitch = pitch; source.spatialBlend = 0f;
            source.Play();
            previews.Add((source, now + clip.length / Mathf.Max(.1f, Mathf.Abs(pitch)) + .1));
            Debug.Log($"[Audio Data] {data.name}: {clip.name}, pitch {pitch:0.00}, volume {data.volume:0.00}", data);
        }
        for (int i = previews.Count - 1; i >= 0; i--)
        {
            if (previews[i].source != null && now < previews[i].end) continue;
            if (previews[i].source != null) DestroyImmediate(previews[i].source.gameObject);
            previews.RemoveAt(i);
        }
        if (queued.Count == 0 && previews.Count == 0) UnityEditor.EditorApplication.update -= TestUpdate;
    }
#endif
}

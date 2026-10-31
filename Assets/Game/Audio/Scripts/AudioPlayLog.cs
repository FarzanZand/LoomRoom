using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

// When each sound was last heard, for the F10 window's Audio list (DevSpawner): a library key, an AudioData asset or a
// clip played straight from a component. AudioManager tells it of every one-shot it plays. The Heard calls are left out
// of release builds (Conditional), so the game pays nothing there.
public static class AudioPlayLog
{
    static readonly Dictionary<string, float> sfxKeys = new(), uiKeys = new();
    static readonly Dictionary<Object, float> assets = new();
    static readonly List<AudioData> datas = new();
    static readonly List<AudioClip> clips = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { sfxKeys.Clear(); uiKeys.Clear(); assets.Clear(); datas.Clear(); clips.Clear(); }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void HeardSfx(string key) { if (!string.IsNullOrEmpty(key)) sfxKeys[key] = Time.unscaledTime; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void HeardUI(string key) { if (!string.IsNullOrEmpty(key)) uiKeys[key] = Time.unscaledTime; }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Heard(AudioData data)
    {
        if (data == null) return;
        if (!assets.ContainsKey(data)) datas.Add(data);
        assets[data] = Time.unscaledTime;
    }

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Heard(AudioClip clip)
    {
        if (clip == null) return;
        if (!assets.ContainsKey(clip)) clips.Add(clip);
        assets[clip] = Time.unscaledTime;
    }

    // The unscaled time it was last heard, or -1 when it hasn't been since Play began.
    public static float LastSfx(string key) => key != null && sfxKeys.TryGetValue(key, out var t) ? t : -1f;
    public static float LastUI(string key) => key != null && uiKeys.TryGetValue(key, out var t) ? t : -1f;
    public static float Last(Object asset) => asset != null && assets.TryGetValue(asset, out var t) ? t : -1f;

    // Everything heard so far, in the order first heard.
    public static IReadOnlyList<AudioData> Datas => datas;
    public static IReadOnlyList<AudioClip> Clips => clips;
}

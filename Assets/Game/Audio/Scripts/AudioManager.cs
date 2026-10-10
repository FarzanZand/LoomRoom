using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// ── Audio library entry types ──────────────────────────────────────────────────
[System.Serializable]
public class SFXEntry
{
    public string          key;
    public AudioClip       clip;
    [Range(0f, 1f)]  public float volume        = 1f;
    [Range(0f, 0.5f)] public float pitchVariance = 0f;
}

[System.Serializable]
public class MusicEntry
{
    public string          key;
    public AudioClip       clip;
    [Range(0f, 1f)]  public float volume = 1f;
}

[System.Serializable]
public class UIEntry
{
    public enum Source { Clip = 0, Data = 1 }
    public string          key;
    [Tooltip("Clip: one sound. Data: an AudioData asset (several clips picked at random, its own pitch variance).")]
    public Source          source;
    [Sirenix.OdinInspector.ShowIf(nameof(UsesClip))] public AudioClip clip;
    [Sirenix.OdinInspector.ShowIf(nameof(UsesData))] public AudioData data;
    [Range(0f, 1f), Tooltip("With Data, multiplies the asset's own volume.")] public float volume = 1f;
    [Range(0.5f, 2f), Sirenix.OdinInspector.ShowIf(nameof(UsesClip))] public float pitch = 1f;
    bool UsesClip => source == Source.Clip;
    bool UsesData => source == Source.Data;
}

// ──────────────────────────────────────────────────────────────────────────────
// Split by concern: AudioManager.Music.cs (music, ambience) and AudioManager.Sfx.cs (one-shots).
public partial class AudioManager : Singleton<AudioManager>
{
    // ── Inspector ──────────────────────────────────────────────────────────────
    [Header("Mixer")]
    [SerializeField] private AudioMixer mixer;

    [Header("Pool")]
    [SerializeField, Min(4)]  private int sfxPoolSize = 20;
    [SerializeField, Min(2)]  private int uiPoolSize  = 8;
    [Header("Default spatial SFX range")]
    [SerializeField, Min(.01f), Tooltip("Full volume within this distance. Keeps nearby interactions audible.")]
    float sfxMinDistance = 3.5f;
    [SerializeField, Min(.01f)] float sfxMaxDistance = 40f;

    [Header("Default Volumes (0–1)")]
    [SerializeField, Range(0f, 1f)] private float defaultMasterVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume  = 0.8f;
    [SerializeField, Range(0f, 1f)] private float defaultSfxVolume    = 1f;
    [SerializeField, Range(0f, 1f)] private float defaultUiVolume     = 1f;

    [Header("Music")]
    [SerializeField] private float defaultFadeDuration = 1.5f;

    [Header("SFX Library")]
    [SerializeField] private SFXEntry[] sfxLibrary;

    [Header("Music Library")]
    [SerializeField] private MusicEntry[] musicLibrary;

    [Header("UI Library")]
    [SerializeField] private UIEntry[] uiLibrary;

    [Header("Ambience")]
    [SerializeField, Min(0f)] private float ambienceFadeDuration = 2f;

    // ── Mixer param names ──────────────────────────────────────────────────────
    public const string P_MASTER = "MasterVolume";
    public const string P_MUSIC  = "MusicVolume";
    public const string P_SFX    = "SFXVolume";
    public const string P_UI     = "UIVolume";

    // ── PlayerPrefs keys ───────────────────────────────────────────────────────
    const string K_MASTER = "vol_master";
    const string K_MUSIC  = "vol_music";
    const string K_SFX    = "vol_sfx";
    const string K_UI     = "vol_ui";

    // ── Volume state (linear 0–1) ──────────────────────────────────────────────
    float masterVolume, musicVolume, sfxVolume, uiVolume;

    public float MasterVolume => masterVolume;
    public float MusicVolume  => musicVolume;
    public float SfxVolume    => sfxVolume;
    public float UiVolume     => uiVolume;

    // ── Mixer groups ───────────────────────────────────────────────────────────
    AudioMixerGroup musicGroup, sfxGroup, uiGroup;
    // For looping sources owned by effects (a flame crackling in the hand).
    public AudioMixerGroup SfxGroup => sfxGroup;

    // ── Music ──────────────────────────────────────────────────────────────────
    AudioSource musicA, musicB;
    bool        usingMusicA;
    Coroutine   musicRoutine;

    AudioSource ActiveMusic => usingMusicA ? musicA : musicB;
    float musicTargetVolume = 1f;
    // What is playing now, so a caller can come back to it later (the room after a dungeon).
    public AudioClip CurrentMusic => ActiveMusic != null && ActiveMusic.isPlaying ? ActiveMusic.clip : null;
    public bool CurrentMusicLoops => ActiveMusic == null || ActiveMusic.loop;
    public float CurrentMusicVolume => musicTargetVolume;

    // ── Pools ──────────────────────────────────────────────────────────────────
    readonly List<AudioSource> sfxPool = new();
    readonly List<AudioSource> uiPool  = new();

    // ── Library lookups ────────────────────────────────────────────────────────
    readonly Dictionary<string, SFXEntry>   sfxDict   = new();
    readonly Dictionary<string, MusicEntry> musicDict = new();
    readonly Dictionary<string, UIEntry>    uiDict    = new();
    AudioSource ambience;
    Coroutine   ambienceRoutine;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    protected override void Awake()
    {
        base.Awake();
        BuildLibraries();
        ResolveGroups();
        LoadAndApplyVolumes();
        SetupMusicSources();
        BuildPool(sfxPool, sfxPoolSize, "SFX", sfxGroup, spatialBlend: 1f);
        BuildPool(uiPool,  uiPoolSize,  "UI",  uiGroup,  spatialBlend: 0f);
    }

    void BuildLibraries()
    {
        if (sfxLibrary != null)
            foreach (var e in sfxLibrary)
                if (!string.IsNullOrEmpty(e.key)) sfxDict[e.key] = e;

        if (musicLibrary != null)
            foreach (var e in musicLibrary)
                if (!string.IsNullOrEmpty(e.key)) musicDict[e.key] = e;

        if (uiLibrary != null)
            foreach (var e in uiLibrary)
                if (!string.IsNullOrEmpty(e.key)) uiDict[e.key] = e;
    }

    void ResolveGroups()
    {
        if (mixer == null) { Debug.LogError("[AudioManager] Mixer is not assigned."); return; }
        musicGroup = FindGroup("Music");
        sfxGroup   = FindGroup("SFX");
        uiGroup    = FindGroup("UI");
    }

    AudioMixerGroup FindGroup(string name)
    {
        var results = mixer.FindMatchingGroups(name);
        if (results == null || results.Length == 0)
            Debug.LogError($"[AudioManager] Mixer group '{name}' not found.");
        return results != null && results.Length > 0 ? results[0] : null;
    }

    // ── Volume ─────────────────────────────────────────────────────────────────
    void LoadAndApplyVolumes()
    {
        masterVolume = PlayerPrefs.GetFloat(K_MASTER, defaultMasterVolume);
        musicVolume  = PlayerPrefs.GetFloat(K_MUSIC,  defaultMusicVolume);
        sfxVolume    = PlayerPrefs.GetFloat(K_SFX,    defaultSfxVolume);
        uiVolume     = PlayerPrefs.GetFloat(K_UI,     defaultUiVolume);
        SetMixerDb(P_MASTER, masterVolume);
        SetMixerDb(P_MUSIC,  musicVolume);
        SetMixerDb(P_SFX,    sfxVolume);
        SetMixerDb(P_UI,     uiVolume);
    }

    void SetMixerDb(string param, float linear) =>
        mixer?.SetFloat(param, LinearToDb(linear));

    static float LinearToDb(float linear) =>
        linear <= 0f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(linear));

    public enum Channel { Master = 0, Music = 1, Sfx = 2, UI = 3 }

    public float GetVolume(Channel channel) => channel switch
    {
        Channel.Master => masterVolume,
        Channel.Music  => musicVolume,
        Channel.Sfx    => sfxVolume,
        _              => uiVolume,
    };

    // Settings menu entry point: applies to the mixer and remembers the value.
    public void SetVolume(Channel channel, float linear)
    {
        linear = Mathf.Clamp01(linear);
        switch (channel)
        {
            case Channel.Master: masterVolume = linear; SetMixerDb(P_MASTER, linear); PlayerPrefs.SetFloat(K_MASTER, linear); break;
            case Channel.Music:  musicVolume  = linear; SetMixerDb(P_MUSIC,  linear); PlayerPrefs.SetFloat(K_MUSIC,  linear); break;
            case Channel.Sfx:    sfxVolume    = linear; SetMixerDb(P_SFX,    linear); PlayerPrefs.SetFloat(K_SFX,    linear); break;
            case Channel.UI:     uiVolume     = linear; SetMixerDb(P_UI,     linear); PlayerPrefs.SetFloat(K_UI,     linear); break;
        }
    }

    // ── Internals ──────────────────────────────────────────────────────────────
    void SetupMusicSources()
    {
        var go = new GameObject("Music");
        go.transform.SetParent(transform);
        musicA = MakeSource(go, "MusicA", musicGroup, 0f);
        musicB = MakeSource(go, "MusicB", musicGroup, 0f);
        ambience = MakeSource(go, "Ambience", sfxGroup, 0f);
    }

    void BuildPool(List<AudioSource> pool, int count, string groupName, AudioMixerGroup group, float spatialBlend)
    {
        var go = new GameObject(groupName + "Pool");
        go.transform.SetParent(transform);
        for (int i = 0; i < count; i++)
            pool.Add(MakeSource(go, groupName + i, group, spatialBlend));
    }

    AudioSource MakeSource(GameObject parent, string label, AudioMixerGroup group, float spatialBlend)
    {
        var go = new GameObject(label);
        go.transform.SetParent(parent.transform);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake           = false;
        src.spatialBlend          = spatialBlend;
        src.outputAudioMixerGroup = group;
        return src;
    }

    AudioSource ClaimSource(List<AudioSource> pool)
    {
        foreach (var s in pool)
            if (!s.isPlaying) return ResetAttenuation(s);

        AudioSource steal = pool[0];
        float lowestRemaining = float.MaxValue;
        foreach (var s in pool)
        {
            if (s.clip == null) return ResetAttenuation(s);
            float remaining = s.clip.length - s.time;
            if (remaining < lowestRemaining) { lowestRemaining = remaining; steal = s; }
        }
        steal.Stop();
        return ResetAttenuation(steal);
    }

    // Pooled room-scale effects must not change the attenuation of the next miniature effect.
    AudioSource ResetAttenuation(AudioSource source)
    {
        source.minDistance = Mathf.Max(.01f,sfxMinDistance);
        source.maxDistance = Mathf.Max(source.minDistance,sfxMaxDistance);
        return source;
    }
}

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
    // The inspector: three tabs, the settings in foldouts.
    const string Tabs = "Tabs", LibrariesTab = "Libraries", SettingsTab = "Settings", VoiceTab = "Dungeon Master voice";

    [Sirenix.OdinInspector.TabGroup(Tabs, LibrariesTab), Sirenix.OdinInspector.LabelText("SFX Library"), Tooltip("Sound effects by key, heard where they happen (PlaySFX(key, position)).")]
    [SerializeField] private SFXEntry[] sfxLibrary;
    [Sirenix.OdinInspector.TabGroup(Tabs, LibrariesTab), Tooltip("Music by key (shop music, the wake-up tune...).")]
    [SerializeField] private MusicEntry[] musicLibrary;
    [Sirenix.OdinInspector.TabGroup(Tabs, LibrariesTab), Sirenix.OdinInspector.LabelText("UI Library"), Tooltip("Flat sounds heard the same anywhere: UI, game cues (level up, item drops by rarity), the DM's syllables.")]
    [SerializeField] private UIEntry[] uiLibrary;

    [Sirenix.OdinInspector.TabGroup(Tabs, SettingsTab), Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Mixer", Expanded = true)]
    [SerializeField] private AudioMixer mixer;

    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Pool")]
    [SerializeField, Min(4), Tooltip("Sound effects that can play at once. A fight easily has 20 going (footsteps, whooshes, hits, gore, voices); past this the quietest one is cut. Keep it and the UI pool under Project Settings > Audio > Real Voice Count with room for music, ambience and loops.")]
    private int sfxPoolSize = 48;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Pool"), Sirenix.OdinInspector.LabelText("UI Pool Size")]
    [SerializeField, Min(2)]  private int uiPoolSize  = 8;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Pool")]
    [SerializeField, Min(1), Tooltip("Copies of one clip that can play at once (five footsteps or whooshes in a frame would fill the pool).")]
    private int maxSameClip = 3;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Pool")]
    [SerializeField, Min(0f), Tooltip("A clip that started this recently (seconds) isn't started again: the copies would only stack into one louder sound.")]
    private float sameClipGap = .05f;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Pool")]
    [SerializeField, Tooltip("Editor and development builds: log a warning when a playing sound is cut to make room.")]
    private bool logCutSounds = true;

    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Spatial sound"), Sirenix.OdinInspector.LabelText("Full Volume Within")]
    [SerializeField, Min(.01f), Tooltip("Metres: sound effects are at full volume this close. Keeps nearby interactions audible.")]
    float sfxMinDistance = 3.5f;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Spatial sound"), Sirenix.OdinInspector.LabelText("Silent Beyond")]
    [SerializeField, Min(.01f), Tooltip("Metres: sound effects fade out to nothing by this distance.")] float sfxMaxDistance = 40f;

    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Default volumes"), Sirenix.OdinInspector.LabelText("Master")]
    [SerializeField, Range(0f, 1f), Tooltip("Until the player sets their own in Settings.")] private float defaultMasterVolume = 1f;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Default volumes"), Sirenix.OdinInspector.LabelText("Music")]
    [SerializeField, Range(0f, 1f)] private float defaultMusicVolume  = 0.8f;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Default volumes"), Sirenix.OdinInspector.LabelText("SFX")]
    [SerializeField, Range(0f, 1f)] private float defaultSfxVolume    = 1f;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Default volumes"), Sirenix.OdinInspector.LabelText("UI")]
    [SerializeField, Range(0f, 1f)] private float defaultUiVolume     = 1f;

    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Music and ambience"), Sirenix.OdinInspector.LabelText("Music Fade")]
    [SerializeField, Tooltip("Seconds a music change fades over when the caller doesn't say.")] private float defaultFadeDuration = 1.5f;
    [Sirenix.OdinInspector.FoldoutGroup(Tabs + "/" + SettingsTab + "/Music and ambience"), Sirenix.OdinInspector.LabelText("Ambience Fade")]
    [SerializeField, Min(0f)] private float ambienceFadeDuration = 2f;

    // ── Mixer param names ──────────────────────────────────────────────────────
    public const string P_MASTER = "MasterVolume";
    public const string P_MUSIC  = "MusicVolume";
    public const string P_SFX    = "SFXVolume";
    public const string P_UI     = "UIVolume";

    // ── PlayerPrefs keys ───────────────────────────────────────────────────────
    public const string K_MASTER = "vol_master";   // public: the main menu reads them (it has no AudioManager)
    public const string K_MUSIC  = "vol_music";
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

    // Unity ignores AudioMixer.SetFloat on exposed parameters during Awake (the mixer isn't ready yet): the volumes read
    // there are applied to the mixer again here, or every channel stays at 0 dB.
    void Start() => ApplyVolumesToMixer();

    void ApplyVolumesToMixer()
    {
        SetMixerDb(P_MASTER, masterVolume);
        SetMixerDb(P_MUSIC,  musicVolume);
        SetMixerDb(P_SFX,    sfxVolume);
        SetMixerDb(P_UI,     uiVolume);
    }

#if UNITY_EDITOR
    // The Default Volumes, changed in the inspector during Play, are heard at once (until the player saves their own
    // in Settings, which then win).
    void OnValidate()
    {
        if (!Application.isPlaying || mixer == null) return;
        if (!PlayerPrefs.HasKey(K_MASTER)) masterVolume = defaultMasterVolume;
        if (!PlayerPrefs.HasKey(K_MUSIC))  musicVolume  = defaultMusicVolume;
        if (!PlayerPrefs.HasKey(K_SFX))    sfxVolume    = defaultSfxVolume;
        if (!PlayerPrefs.HasKey(K_UI))     uiVolume     = defaultUiVolume;
        ApplyVolumesToMixer();
    }
#endif

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
        ApplyVolumesToMixer();
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

    // Where there is no AudioManager (the main menu): the remembered volumes, read and written straight, with an event
    // for whatever plays sound there (MainMenuUI's music).
    public static float StoredVolume(Channel channel) => PlayerPrefs.GetFloat(Key(channel), channel == Channel.Music ? .8f : 1f);
    public static void StoreVolume(Channel channel, float linear)
    {
        PlayerPrefs.SetFloat(Key(channel), Mathf.Clamp01(linear));
        VolumeStored?.Invoke();
    }
    public static event System.Action VolumeStored;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetVolumeStored() => VolumeStored = null;
    static string Key(Channel channel) => channel switch { Channel.Master => K_MASTER, Channel.Music => K_MUSIC, Channel.Sfx => K_SFX, _ => K_UI };

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

        // All busy: cut the one heard least (quiet or far away, distant footsteps first), so hits and kills play out.
        var listener = Listener();
        AudioSource steal = pool[0];
        float lowest = float.MaxValue;
        foreach (var s in pool)
        {
            if (s.clip == null) return ResetAttenuation(s);
            float heard = Audibility(s, listener);
            if (heard < lowest) { lowest = heard; steal = s; }
        }
        if (logCutSounds && Debug.isDebugBuild && Time.unscaledTime >= nextCutLog)
        {
            nextCutLog = Time.unscaledTime + 1f;
            Debug.LogWarning($"[AudioManager] All {pool.Count} sources busy: cut '{steal.clip.name}' ({steal.time:0.00} of {steal.clip.length:0.00} s) to make room.");
        }
        steal.Stop();
        return ResetAttenuation(steal);
    }

    float nextCutLog;
    AudioListener listenerCache;

    AudioListener Listener()
    {
        if (listenerCache == null || !listenerCache.isActiveAndEnabled) listenerCache = FindAnyObjectByType<AudioListener>();
        return listenerCache;
    }

    // Roughly how loud a playing source is at the listener (its volume through a logarithmic rolloff).
    static float Audibility(AudioSource s, AudioListener listener)
    {
        float volume = s.volume;
        if (s.spatialBlend < .5f || listener == null) return volume;
        float d = Vector3.Distance(s.transform.position, listener.transform.position);
        if (d >= s.maxDistance) return 0f;
        return d <= s.minDistance ? volume : volume * s.minDistance / d;
    }

    // Leaves out a clip that already plays maxSameClip times, or started within sameClipGap.
    readonly Dictionary<AudioClip, float> clipStarted = new();

    bool TooMany(List<AudioSource> pool, AudioClip clip)
    {
        if (clipStarted.TryGetValue(clip, out float at) && Time.unscaledTime - at < sameClipGap) return true;
        int playing = 0;
        foreach (var s in pool) if (s.isPlaying && s.clip == clip && ++playing >= maxSameClip) return true;
        clipStarted[clip] = Time.unscaledTime;
        if (clipStarted.Count > 256) clipStarted.Clear();
        return false;
    }

    // Pooled room-scale effects must not change the attenuation of the next miniature effect.
    AudioSource ResetAttenuation(AudioSource source)
    {
        source.minDistance = Mathf.Max(.01f,sfxMinDistance);
        source.maxDistance = Mathf.Max(source.minDistance,sfxMaxDistance);
        return source;
    }
}

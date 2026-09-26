using System.Collections;
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
    public string          key;
    public AudioClip       clip;
    [Range(0f, 1f)]  public float volume = 1f;
    [Range(0.5f, 2f)] public float pitch = 1f;
}

// One creature's voice and footsteps. Enemies look themselves up by CharacterData;
// an entry with no characters listed is the fallback for anything unlisted.
[System.Serializable]
public class CreatureAudioEntry
{
    public string label;
    [Tooltip("Characters using this entry. Leave empty to make this the fallback entry.")]
    public CharacterData[] characters = new CharacterData[0];
    [Tooltip("Muttering while idle, wandering or patrolling.")]
    public AudioClip[] idle = new AudioClip[0];
    [Tooltip("The bark when it spots the player. Plays before it comes around the corner.")]
    public AudioClip[] alert = new AudioClip[0];
    public AudioClip[] pain = new AudioClip[0];
    public AudioClip[] death = new AudioClip[0];
    public AudioClip[] footsteps = new AudioClip[0];
    [Range(0f, 1f)] public float voiceVolume = .9f;
    [Range(0f, 1f)] public float footstepVolume = .55f;
    [Range(0f, .5f)] public float pitchVariance = .08f;
    [Tooltip("Base pitch. Lower for big creatures, higher for small ones.")]
    [Range(.3f, 2f)] public float pitch = 1f;
    [Min(.1f), Tooltip("Full volume within this distance.")] public float minDistance = 2f;
    [Min(1f), Tooltip("Silent beyond this distance. Large values let the player hear things around corners.")] public float maxDistance = 26f;
    [Tooltip("Seconds between idle mutters (random in range).")] public Vector2 idleInterval = new Vector2(6f, 14f);
    [Min(.05f)] public float walkStepInterval = .55f;
    [Min(.05f)] public float runStepInterval = .34f;
    [Min(0f)] public float painCooldown = .6f;

    public static AudioClip Pick(AudioClip[] clips) =>
        clips == null || clips.Length == 0 ? null : clips[Random.Range(0, clips.Length)];
}

// ──────────────────────────────────────────────────────────────────────────────
public class AudioManager : Singleton<AudioManager>
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

    [Header("Creature Library (enemy voices and footsteps)")]
    [SerializeField] private CreatureAudioEntry[] creatureLibrary;

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

    // ── Music ──────────────────────────────────────────────────────────────────
    AudioSource musicA, musicB;
    bool        usingMusicA;
    Coroutine   musicRoutine;

    AudioSource ActiveMusic => usingMusicA ? musicA : musicB;

    // ── Pools ──────────────────────────────────────────────────────────────────
    readonly List<AudioSource> sfxPool = new();
    readonly List<AudioSource> uiPool  = new();

    // ── Library lookups ────────────────────────────────────────────────────────
    readonly Dictionary<string, SFXEntry>   sfxDict   = new();
    readonly Dictionary<string, MusicEntry> musicDict = new();
    readonly Dictionary<string, UIEntry>    uiDict    = new();
    readonly Dictionary<CharacterData, CreatureAudioEntry> creatureDict = new();
    CreatureAudioEntry creatureFallback;
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

        if (creatureLibrary != null)
            foreach (var e in creatureLibrary)
            {
                if (e == null) continue;
                if (e.characters == null || e.characters.Length == 0) { creatureFallback ??= e; continue; }
                foreach (var c in e.characters) if (c != null) creatureDict[c] = e;
            }
    }

    public CreatureAudioEntry GetCreatureAudio(CharacterData character) =>
        character != null && creatureDict.TryGetValue(character, out var e) ? e : creatureFallback;

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

    public enum Channel { Master, Music, Sfx, UI }

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

    // ── Music ──────────────────────────────────────────────────────────────────

    public void PlayMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f) =>
        StartMusic(clip, loop, fadeDuration, 1f, 1f, crossfade: false);

    public void CrossfadeMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f, float volume = 1f) =>
        StartMusic(clip, loop, fadeDuration, Mathf.Clamp01(volume), 1f, crossfade: true);

    public void PlayMusic(string key, bool loop = true, float fadeDuration = -1f)
    {
        if (!musicDict.TryGetValue(key, out var entry))
        { Debug.LogWarning($"[AudioManager] Music key '{key}' not found."); return; }
        StartMusic(entry.clip, loop, fadeDuration, entry.volume, 1f, crossfade: false);
    }

    public void PlayMusicData(AudioData data, bool loop = true, float fadeDuration = -1f)
    {
        if (data == null) return;
        StartMusic(data.GetClip(), loop, fadeDuration, data.volume, data.pitch, crossfade: false);
    }

    void StartMusic(AudioClip clip, bool loop, float fadeDuration, float volume, float pitch, bool crossfade)
    {
        if (clip == null) return;
        if (fadeDuration < 0f) fadeDuration = defaultFadeDuration;
        StopMusicRoutine();

        var outgoing = ActiveMusic;
        if (crossfade) usingMusicA = !usingMusicA;
        var incoming = ActiveMusic;

        incoming.clip   = clip;
        incoming.loop   = loop;
        incoming.volume = 0f;
        incoming.pitch  = pitch;
        incoming.Play();

        musicRoutine = crossfade
            ? StartCoroutine(CrossfadeRoutine(outgoing, incoming, fadeDuration, volume))
            : StartCoroutine(FadeSource(incoming, 0f, volume, fadeDuration, () => musicRoutine = null));
    }

    // ── Music — control ────────────────────────────────────────────────────────

    public void StopMusic(float fadeDuration = -1f)
    {
        if (fadeDuration < 0f) fadeDuration = defaultFadeDuration;
        StopMusicRoutine();
        var src = ActiveMusic;
        if (!src.isPlaying) return;
        musicRoutine = StartCoroutine(FadeSource(src, src.volume, 0f, fadeDuration, () =>
        {
            src.Stop();
            musicRoutine = null;
        }));
    }

    // Killing a crossfade mid-way would leave its outgoing source looping at partial volume.
    void StopMusicRoutine()
    {
        if (musicRoutine == null) return;
        StopCoroutine(musicRoutine);
        musicRoutine = null;
        var inactive = usingMusicA ? musicB : musicA;
        inactive.Stop();
        inactive.volume = 0f;
    }

    IEnumerator CrossfadeRoutine(AudioSource outgoing, AudioSource incoming, float duration, float targetVol = 1f)
    {
        float startVol = outgoing.volume;
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t  = Mathf.Clamp01(elapsed / duration);
            outgoing.volume = Mathf.Lerp(startVol,  0f,        t);
            incoming.volume = Mathf.Lerp(0f,         targetVol, t);
            yield return null;
        }

        outgoing.Stop();
        outgoing.volume = 0f;
        incoming.volume = targetVol;
        musicRoutine    = null;
    }

    IEnumerator FadeSource(AudioSource src, float from, float to, float duration, System.Action onDone = null)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed    += Time.unscaledDeltaTime;
            src.volume  = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        src.volume = to;
        onDone?.Invoke();
    }

    // ── SFX — clip overloads ───────────────────────────────────────────────────

    public AudioSource PlaySFX(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariance = 0f)
    {
        if (clip == null) return null;
        var src = ClaimSource(sfxPool);
        src.transform.position = position;
        src.clip         = clip;
        src.loop         = false;
        src.volume       = volume;
        src.pitch        = PitchedBy(pitchVariance);
        src.spatialBlend = 1f;
        src.Play();
        return src;
    }

    // Spatial SFX with its own audible range (creature voices carry further than a clink).
    public AudioSource PlaySFX(AudioClip clip, Vector3 position, float volume, float pitch, float pitchVariance, float minDistance, float maxDistance)
    {
        var src = PlaySFX(clip, position, volume, pitchVariance);
        if (src == null) return null;
        src.pitch *= pitch;
        src.minDistance = Mathf.Max(.01f, minDistance);
        src.maxDistance = Mathf.Max(src.minDistance, maxDistance);
        return src;
    }

    public AudioSource PlaySFX2D(AudioClip clip, float volume = 1f, float pitchVariance = 0f)
    {
        if (clip == null) return null;
        var src = ClaimSource(sfxPool);
        src.clip         = clip;
        src.loop         = false;
        src.volume       = volume;
        src.pitch        = PitchedBy(pitchVariance);
        src.spatialBlend = 0f;
        src.Play();
        return src;
    }

    // ── SFX — key overloads ────────────────────────────────────────────────────

    public AudioSource PlaySFX(string key, Vector3 position)
    {
        if (!sfxDict.TryGetValue(key, out var e))
        { Debug.LogWarning($"[AudioManager] SFX key '{key}' not found."); return null; }
        return PlaySFX(e.clip, position, e.volume, e.pitchVariance);
    }

    public AudioSource PlaySFX2D(string key)
    {
        if (!sfxDict.TryGetValue(key, out var e))
        { Debug.LogWarning($"[AudioManager] SFX key '{key}' not found."); return null; }
        return PlaySFX2D(e.clip, e.volume, e.pitchVariance);
    }

    // ── UI — clip overloads ────────────────────────────────────────────────────

    public AudioSource PlayUI(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return null;
        var src = ClaimSource(uiPool);
        src.clip         = clip;
        src.loop         = false;
        src.volume       = volume;
        src.pitch        = pitch;
        src.spatialBlend = 0f;
        src.Play();
        return src;
    }

    // ── AudioData overloads ────────────────────────────────────────────────────

    public AudioSource PlaySFXData(AudioData data, Vector3 position)
    {
        if (data == null) return null;
        var clip = data.GetClip();
        if (clip == null) return null;
        float pitch = data.pitch + (data.pitchVariance > 0f ? Random.Range(-data.pitchVariance, data.pitchVariance) : 0f);
        var src = ClaimSource(sfxPool);
        src.transform.position = position;
        src.clip         = clip;
        src.loop         = false;
        src.volume       = data.volume;
        src.pitch        = pitch;
        src.spatialBlend = 1f;
        src.minDistance = Mathf.Max(.01f, data.minDistance);
        src.maxDistance = Mathf.Max(src.minDistance, data.maxDistance);
        src.Play();
        return src;
    }

    public AudioSource PlaySFXData2D(AudioData data)
    {
        if (data == null) return null;
        var clip = data.GetClip();
        if (clip == null) return null;
        float pitch = data.pitch + (data.pitchVariance > 0f ? Random.Range(-data.pitchVariance, data.pitchVariance) : 0f);
        var src = ClaimSource(sfxPool);
        src.clip         = clip;
        src.loop         = false;
        src.volume       = data.volume;
        src.pitch        = pitch;
        src.spatialBlend = 0f;
        src.Play();
        return src;
    }

    public AudioSource PlayUIData(AudioData data)
    {
        if (data == null) return null;
        var clip = data.GetClip();
        if (clip == null) return null;
        float pitch = data.pitch + (data.pitchVariance > 0f ? Random.Range(-data.pitchVariance, data.pitchVariance) : 0f);
        return PlayUI(clip, data.volume, pitch);
    }

    // ── UI — key overloads ─────────────────────────────────────────────────────

    public AudioSource PlayUI(string key)
    {
        if (!uiDict.TryGetValue(key, out var e))
        { Debug.LogWarning($"[AudioManager] UI key '{key}' not found."); return null; }
        return PlayUI(e.clip, e.volume, e.pitch);
    }

    // ── Ambience ───────────────────────────────────────────────────────────────

    // One looping 2D bed on the SFX mixer (dripping crypt, sewer water). Separate from music.
    public void PlayAmbience(AudioClip clip, float volume = 1f, float fadeDuration = -1f)
    {
        if (fadeDuration < 0f) fadeDuration = ambienceFadeDuration;
        if (clip == null) { StopAmbience(fadeDuration); return; }
        if (ambienceRoutine != null) StopCoroutine(ambienceRoutine);
        if (ambience.clip == clip && ambience.isPlaying)
        {
            ambienceRoutine = StartCoroutine(FadeSource(ambience, ambience.volume, Mathf.Clamp01(volume), fadeDuration, () => ambienceRoutine = null));
            return;
        }
        ambienceRoutine = StartCoroutine(SwapAmbience(clip, Mathf.Clamp01(volume), fadeDuration));
    }

    IEnumerator SwapAmbience(AudioClip clip, float volume, float fadeDuration)
    {
        if (ambience.isPlaying) yield return FadeSource(ambience, ambience.volume, 0f, fadeDuration * .5f);
        ambience.clip = clip;
        ambience.loop = true;
        ambience.Play();
        yield return FadeSource(ambience, 0f, volume, fadeDuration * .5f);
        ambienceRoutine = null;
    }

    public void StopAmbience(float fadeDuration = -1f)
    {
        if (fadeDuration < 0f) fadeDuration = ambienceFadeDuration;
        if (ambienceRoutine != null) StopCoroutine(ambienceRoutine);
        if (!ambience.isPlaying) { ambienceRoutine = null; return; }
        ambienceRoutine = StartCoroutine(FadeSource(ambience, ambience.volume, 0f, fadeDuration, () => { ambience.Stop(); ambienceRoutine = null; }));
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

    static float PitchedBy(float variance) =>
        variance > 0f ? 1f + Random.Range(-variance, variance) : 1f;
}

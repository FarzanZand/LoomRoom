using System.Collections;
using UnityEngine;

// Music (two sources for crossfades) and the looping ambience bed.
public partial class AudioManager
{
    // ── Music ──────────────────────────────────────────────────────────────────

    public void PlayMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f) =>
        StartMusic(clip, loop, fadeDuration, 1f, 1f, crossfade: false);

    public void CrossfadeMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f, float volume = 1f) =>
        StartMusic(clip, loop, fadeDuration, Mathf.Clamp01(volume), 1f, crossfade: true);

    // A Music Library entry, crossfaded in at its own volume. False when the key isn't in the library.
    public bool CrossfadeMusic(string key, bool loop = true, float fadeDuration = -1f)
    {
        if (!musicDict.TryGetValue(key, out var entry) || entry.clip == null) { Debug.LogWarning($"[AudioManager] Music key '{key}' not found."); return false; }
        StartMusic(entry.clip, loop, fadeDuration, entry.volume, 1f, crossfade: true);
        return true;
    }

    // Seconds into the music now playing (to come back to it later with CrossfadeMusicAt).
    public float CurrentMusicTime => ActiveMusic != null && ActiveMusic.isPlaying ? ActiveMusic.time : 0f;

    // Crossfades back to a track from where it was left.
    public void CrossfadeMusicAt(AudioClip clip, float time, bool loop = true, float fadeDuration = -1f, float volume = 1f)
    {
        if (clip == null) return;
        StartMusic(clip, loop, fadeDuration, Mathf.Clamp01(volume), 1f, crossfade: true);
        if (time > 0f && time < clip.length) ActiveMusic.time = time;
    }

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
        musicTargetVolume = volume;

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
}

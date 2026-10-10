using System.Collections.Generic;
using UnityEngine;

// One-shot sounds: SFX from the SFX pool (spatial with a position, else 2D) and UI sounds from the UI pool.
public partial class AudioManager
{
    // ── SFX — clip overloads ───────────────────────────────────────────────────

    // Moving loops retain their prefab-owned source and stop with their visual.
    public void PlaySFXLoop(AudioSource source, AudioClip clip, float volume = 1f)
    {
        if (source == null || clip == null) return;
        source.outputAudioMixerGroup = SfxGroup;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = 1f;
        source.loop = true;
        source.Play();
    }

    public AudioSource PlaySFX(AudioClip clip, Vector3 position, float volume = 1f, float pitchVariance = 0f) =>
        Play(sfxPool, clip, volume, 1f + AudioData.PitchOffset(pitchVariance), position);

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

    public AudioSource PlaySFX2D(AudioClip clip, float volume = 1f, float pitchVariance = 0f) =>
        Play(sfxPool, clip, volume, 1f + AudioData.PitchOffset(pitchVariance), null);

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

    public AudioSource PlayUI(AudioClip clip, float volume = 1f, float pitch = 1f) =>
        Play(uiPool, clip, volume, pitch, null);

    // ── AudioData overloads ────────────────────────────────────────────────────

    public AudioSource PlaySFXData(AudioData data, Vector3 position)
    {
        var src = data != null ? Play(sfxPool, data.GetClip(), data.volume, data.RandomPitch(), position) : null;
        if (src == null) return null;
        src.minDistance = Mathf.Max(.01f, data.minDistance);
        src.maxDistance = Mathf.Max(src.minDistance, data.maxDistance);
        return src;
    }

    public AudioSource PlaySFXData2D(AudioData data) =>
        data != null ? Play(sfxPool, data.GetClip(), data.volume, data.RandomPitch(), null) : null;

    public AudioSource PlayUIData(AudioData data) =>
        data != null ? PlayUI(data.GetClip(), data.volume, data.RandomPitch()) : null;

    public bool HasUI(string key) => !string.IsNullOrEmpty(key) && uiDict.ContainsKey(key);

    // Game cues: entries in the UI Library, so they're swapped there with the other UI sounds.
    public const string SkillUpKey = "skillUp", LevelUpKey = "levelUp";
    public void PlaySkillUp() => PlayUI(SkillUpKey);
    public void PlayLevelUp() => PlayUI(uiDict.ContainsKey(LevelUpKey) ? LevelUpKey : SkillUpKey);

    // ── UI — key overloads ─────────────────────────────────────────────────────

    public AudioSource PlayUI(string key)
    {
        if (!uiDict.TryGetValue(key, out var e))
        { Debug.LogWarning($"[AudioManager] UI key '{key}' not found."); return null; }
        if (e.source == UIEntry.Source.Data)
        {
            var src = PlayUIData(e.data);
            if (src != null) src.volume *= e.volume;
            return src;
        }
        return PlayUI(e.clip, e.volume, e.pitch);
    }

    // Every one-shot goes through here: claim a pooled source, set it up and play it.
    // A position makes it spatial (attenuation reset to the defaults by ClaimSource); none plays it 2D.
    AudioSource Play(List<AudioSource> pool, AudioClip clip, float volume, float pitch, Vector3? position)
    {
        if (clip == null) return null;
        var src = ClaimSource(pool);
        if (position.HasValue) src.transform.position = position.Value;
        src.clip         = clip;
        src.loop         = false;
        src.volume       = volume;
        src.pitch        = pitch;
        src.spatialBlend = position.HasValue ? 1f : 0f;
        src.Play();
        return src;
    }
}

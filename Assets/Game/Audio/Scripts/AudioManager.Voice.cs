using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

// The Dungeon Master's mumble: Barony-style gibberish under lines that have no recorded voice.
// Syllables plays a UI Library sound once per syllable; every other voice is a set of phrases of rising
// length (Old from Tools/dm_babble.py, the rest from Tools/dm_voices.py <dir> <seed> <voice>), of which
// the one nearest the line's length plays.
// 2-6 (Soft, Whisper, Deep, Quick, Gravel: pitch variants of Old) are retired. 7 onwards are made by
// Tools/dm_voices.py, each a different way: beeps, additive chatter, kazoo, choir, growl, plucked strings, radio.
public enum MumbleVoice { Syllables = 0, Old = 1, Blips = 7, Animalese = 8, Kazoo = 9, Choir = 10, Beast = 11, Lute = 12, Radio = 13 }

[Serializable]
public class MumbleVoiceSet
{
    public MumbleVoice voice;
    [Tooltip("The babble phrases, several lengths. Volume and pitch variance come from the asset.")]
    public AudioData babble;
    [Min(.05f), Tooltip("Seconds of babble per word, used to pick the phrase nearest the line's length.")]
    public float perWord = .22f;
}

public partial class AudioManager
{
    [Header("Dungeon Master voice")]
    [SerializeField, Tooltip("The gibberish the Dungeon Master mumbles under lines without a recorded voice.")]
    MumbleVoice mumbleVoice = MumbleVoice.Old;
    [SerializeField, Tooltip("Babble phrases for each voice except Syllables.")]
    MumbleVoiceSet[] mumbleVoices;
    [SerializeField, Tooltip("Syllables: UI Library key played once per syllable. Make it a Data entry to pick from several syllables.")]
    string mumbleKey = "dmMumble";
    [SerializeField, Min(.03f), Tooltip("Syllables: seconds between syllables.")] float syllableGap = .09f;
    [SerializeField, Min(1), Tooltip("Syllables: per word, capped by Max Syllables.")] float syllablesPerWord = 1.5f;
    [SerializeField, Min(1)] int maxSyllables = 14;
    [SerializeField, TextArea(1, 2), Tooltip("Lines the Test buttons mumble, picked at random.")]
    string[] testSentences =
    {
        "Roll for initiative.",
        "You hear something moving in the dark.",
        "The door is locked. Perhaps there is a key somewhere.",
        "A rat. A big one.",
        "Well played. Shall we go deeper?",
        "Take the stairs down when you are ready.",
        "That one hit hard. You should drink something.",
    };

    public MumbleVoice MumbleVoice { get => mumbleVoice; set => mumbleVoice = value; }

    struct MumbleStep { public AudioClip clip; public float volume, pitch, at; }

    readonly List<MumbleStep> mumbleSteps = new();
    AudioClip lastBabble;
    int lastTestSentence = -1;

    // Mumbles under a line in the current voice. Returns the seconds of speech.
    public float Mumble(string text) => Mumble(text, mumbleVoice);

    public float Mumble(string text, MumbleVoice voice)
    {
        float seconds = PlanMumble(text, voice, mumbleSteps);
        List<MumbleStep> later = null;
        foreach (var step in mumbleSteps)
            if (step.at <= 0f) PlayUI(step.clip, step.volume, step.pitch);
            else (later ??= new List<MumbleStep>()).Add(step);
        if (later != null) StartCoroutine(PlayLater(later));
        return seconds;
    }

    IEnumerator PlayLater(List<MumbleStep> steps)
    {
        float start = Time.unscaledTime;
        foreach (var step in steps)
        {
            while (Time.unscaledTime - start < step.at) yield return null;
            PlayUI(step.clip, step.volume, step.pitch);
        }
    }

    // Fills steps with what to play and when; returns the seconds of speech (.5 when there is nothing to play).
    float PlanMumble(string text, MumbleVoice voice, List<MumbleStep> steps)
    {
        steps.Clear();
        int words = Mathf.Max(1, text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length);
        if (voice == MumbleVoice.Syllables) return PlanSyllables(words, steps);

        var set = VoiceSet(voice);
        if (set == null || set.babble == null || set.babble.clips == null)
        {
            Debug.LogWarning($"[AudioManager] No babble set for the {voice} voice; using Syllables.");
            return PlanSyllables(words, steps);
        }
        // One phrase for the whole line: of the two clips nearest its length, not the one heard last.
        float target = words * set.perWord;
        AudioClip best = null, second = null;
        foreach (var c in set.babble.clips)
        {
            if (c == null) continue;
            if (best == null || Mathf.Abs(c.length - target) < Mathf.Abs(best.length - target)) { second = best; best = c; }
            else if (second == null || Mathf.Abs(c.length - target) < Mathf.Abs(second.length - target)) second = c;
        }
        if (best == null) return .5f;
        var clip = second != null && (best == lastBabble || Random.value < .35f) ? second : best;
        lastBabble = clip;
        float pitch = set.babble.RandomPitch();
        steps.Add(new MumbleStep { clip = clip, volume = set.babble.volume, pitch = pitch });
        return clip.length / Mathf.Max(.1f, pitch);
    }

    float PlanSyllables(int words, List<MumbleStep> steps)
    {
        var entry = UIEntryFor(mumbleKey);
        if (entry == null) return .5f;
        int count = Mathf.Clamp(Mathf.RoundToInt(words * syllablesPerWord), 2, maxSyllables);
        float at = 0f;
        for (int i = 0; i < count; i++)
        {
            var step = new MumbleStep { at = at };
            if (entry.source == UIEntry.Source.Data && entry.data != null)
            {
                step.clip = entry.data.GetClip();
                step.volume = entry.data.volume * entry.volume;
                step.pitch = entry.data.RandomPitch();
            }
            else { step.clip = entry.clip; step.volume = entry.volume; step.pitch = entry.pitch; }
            if (step.clip != null) steps.Add(step);
            at += syllableGap * Random.Range(.8f, 1.25f);
        }
        return count * syllableGap;
    }

    MumbleVoiceSet VoiceSet(MumbleVoice voice)
    {
        if (mumbleVoices != null)
            foreach (var set in mumbleVoices)
                if (set != null && set.voice == voice) return set;
        return null;
    }

    // Read from the array, not the lookup built in Awake, so the inspector preview works outside Play mode.
    UIEntry UIEntryFor(string key)
    {
        if (string.IsNullOrEmpty(key) || uiLibrary == null) return null;
        foreach (var e in uiLibrary)
            if (e != null && e.key == key) return e;
        return null;
    }

    string TestSentence()
    {
        if (testSentences == null || testSentences.Length == 0) return "Roll for initiative.";
        int i = Random.Range(0, testSentences.Length);
        if (testSentences.Length > 1 && i == lastTestSentence) i = (i + 1) % testSentences.Length;
        lastTestSentence = i;
        return testSentences[i];
    }

    [Button("Test voice"), PropertyOrder(100)]
    void TestVoice() => Test(mumbleVoice, 0f);

    [Button("Test all voices"), PropertyOrder(101)]
    void TestAllVoices()
    {
        float at = 0f;
        foreach (MumbleVoice voice in Enum.GetValues(typeof(MumbleVoice)))
            at += Test(voice, at) + .6f;
    }

    // Mumbles a random test sentence in the given voice after a delay; returns its length in seconds.
    float Test(MumbleVoice voice, float delay)
    {
        string text = TestSentence();
        var steps = new List<MumbleStep>();
        float seconds = PlanMumble(text, voice, steps);
        Debug.Log($"[AudioManager] {voice}: \"{text}\"");
        if (Application.isPlaying)
        {
            for (int i = 0; i < steps.Count; i++) { var s = steps[i]; s.at += delay; steps[i] = s; }
            StartCoroutine(PlayLater(steps));
        }
#if UNITY_EDITOR
        else Preview(steps, delay);
#endif
        return seconds;
    }

#if UNITY_EDITOR
    // Outside Play mode there is no pool: each step gets a hidden temporary source, destroyed when it ends.
    static readonly List<(MumbleStep step, double at, UnityEngine.Audio.AudioMixerGroup group)> previewQueue = new();
    static readonly List<(AudioSource source, double end)> previewSources = new();

    void Preview(List<MumbleStep> steps, float delay)
    {
        var groups = mixer != null ? mixer.FindMatchingGroups("UI") : null;
        var group = groups != null && groups.Length > 0 ? groups[0] : null;
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        foreach (var step in steps) previewQueue.Add((step, now + delay + step.at, group));
        UnityEditor.EditorApplication.update -= PreviewUpdate;
        UnityEditor.EditorApplication.update += PreviewUpdate;
    }

    static void PreviewUpdate()
    {
        double now = UnityEditor.EditorApplication.timeSinceStartup;
        for (int i = previewQueue.Count - 1; i >= 0; i--)
        {
            var (step, at, group) = previewQueue[i];
            if (now < at) continue;
            previewQueue.RemoveAt(i);
            var go = new GameObject("Mumble preview") { hideFlags = HideFlags.HideAndDontSave };
            var source = go.AddComponent<AudioSource>();
            source.clip = step.clip;
            source.volume = step.volume;
            source.pitch = step.pitch;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = group;
            source.Play();
            previewSources.Add((source, now + step.clip.length / Mathf.Max(.1f, step.pitch) + .1));
        }
        for (int i = previewSources.Count - 1; i >= 0; i--)
        {
            if (previewSources[i].source != null && now < previewSources[i].end) continue;
            if (previewSources[i].source != null) DestroyImmediate(previewSources[i].source.gameObject);
            previewSources.RemoveAt(i);
        }
        if (previewQueue.Count == 0 && previewSources.Count == 0) UnityEditor.EditorApplication.update -= PreviewUpdate;
    }
#endif
}

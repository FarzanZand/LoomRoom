using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

// The voice at the table: a line typed out at the top of the screen, with a recorded clip or a
// generic mumble. Call DungeonMaster.Say(...) from code, pass a DMLine asset, or use Lua DMSay("...").
public class DungeonMaster : Singleton<DungeonMaster>
{
    [SerializeField] CanvasGroup group;
    [SerializeField] TextMeshProUGUI label;
    [SerializeField, Min(1)] float charactersPerSecond = 40;
    [SerializeField, Min(0), Tooltip("Seconds the line stays once fully shown, plus Hold Per Word.")] float hold = 1.5f, holdPerWord = .25f;
    [SerializeField, Min(0)] float fadeOut = .5f;

    [Header("Mumble")]
    [SerializeField, Tooltip("Short syllables picked at random while the text types out.")] AudioClip[] mumbleSyllables;
    [SerializeField, Range(0, 1)] float mumbleVolume = .6f;
    [SerializeField, Range(.5f, 2)] float mumblePitch = 1f;
    [SerializeField, Range(0, .5f)] float mumblePitchVariance = .12f;
    [SerializeField, Min(1), Tooltip("One syllable every this many letters.")] int lettersPerSyllable = 3;

    readonly Queue<(string text, AudioClip clip, AudioData data, bool mumble)> queue = new();
    Coroutine running;
    AudioSource voice;

    protected override void Awake()
    {
        base.Awake();
        if (group != null) group.alpha = 0;
    }

    public static void Say(string text, AudioClip clip = null, bool mumble = false) { if (HasInstance) Instance.Enqueue(text, clip, null, mumble); }
    public static void Say(string text, AudioData data, bool mumble = false) { if (HasInstance) Instance.Enqueue(text, null, data, mumble); }
    public static void Say(DMLine line) { if (line != null && HasInstance) Instance.Enqueue(line.text, null, line.voice, line.mumble); }

    void Enqueue(string text, AudioClip clip, AudioData data, bool mumble)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        MessageLog.Post(text.Trim(), MessageKind.Lore);
        queue.Enqueue((text.Trim(), clip, data, mumble));
        if (running == null && isActiveAndEnabled) running = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        while (queue.Count > 0)
        {
            var line = queue.Dequeue();
            if (label == null || group == null) continue;
            label.text = line.text; label.maxVisibleCharacters = 0;
            group.DOKill(); group.alpha = 1;
            voice = null;
            if (AudioManager.HasInstance)
            {
                if (line.data != null) voice = AudioManager.Instance.PlayUIData(line.data);
                else if (line.clip != null) voice = AudioManager.Instance.PlayUI(line.clip);
            }
            bool mumble = line.mumble && line.clip == null && line.data == null;
            int total = line.text.Length, sinceSyllable = lettersPerSyllable;
            float shown = 0;
            while (label.maxVisibleCharacters < total)
            {
                shown += Time.unscaledDeltaTime * charactersPerSecond;
                int next = Mathf.Min(total, Mathf.FloorToInt(shown));
                for (int c = label.maxVisibleCharacters; c < next; c++)
                    if (mumble && char.IsLetter(line.text[c]) && ++sinceSyllable >= lettersPerSyllable) { sinceSyllable = 0; Syllable(); }
                label.maxVisibleCharacters = next;
                yield return null;
            }
            float wait = hold + holdPerWord * line.text.Split(' ').Length;
            if (voice != null && voice.isPlaying && voice.clip != null) wait = Mathf.Max(wait, voice.clip.length - voice.time + .3f);
            yield return new WaitForSecondsRealtime(wait);
            if (queue.Count == 0) { group.DOFade(0, fadeOut).SetUpdate(true); yield return new WaitForSecondsRealtime(fadeOut); }
        }
        running = null;
    }

    void Syllable()
    {
        if (!AudioManager.HasInstance || mumbleSyllables == null || mumbleSyllables.Length == 0) return;
        var clip = mumbleSyllables[Random.Range(0, mumbleSyllables.Length)];
        AudioManager.Instance.PlayUI(clip, mumbleVolume, mumblePitch + Random.Range(-mumblePitchVariance, mumblePitchVariance));
    }

    void OnDisable() { if (running != null) StopCoroutine(running); running = null; queue.Clear(); if (group != null) group.alpha = 0; }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

// The on-screen message feed, the way Barony does it: what just happened in white (kills in orange),
// and the Dungeon Master's voice in its own colour ("You hear a voice inside your head..." and the
// quoted line, with a recorded clip or a short gibberish mumble). Lines appear whole in the lower
// middle (or bottom left) and fade. Damage lines stay out: floating numbers show those.
// The DM speaks through DungeonMaster.Say(...), a DMLine asset, a biome's entry lines, or Lua DMSay("...").
public class DungeonMaster : Singleton<DungeonMaster>
{
    public enum Placement { LowerMiddle = 0, BottomLeft = 1 }

    [Serializable]
    public class Line
    {
        [TextArea] public string text;
        [Tooltip("Recorded voice. Empty uses the mumble if Mumble is on.")] public AudioData voice;
        [Tooltip("Barony-style gibberish when the line appears.")] public bool mumble = true;
    }

    [SerializeField] TextMeshProUGUI label;
    [SerializeField, Tooltip("Where the lines sit on screen.")] Placement placement = Placement.LowerMiddle;
    [SerializeField, Tooltip("Shown before each new message. Empty for none.")] string prefix = "You hear a voice inside your head...";
    [SerializeField, Tooltip("The Dungeon Master's lines.")] Color color = new(.45f, .85f, .95f);
    [SerializeField, Tooltip("Everything else that happens.")] Color eventColor = Color.white;
    [SerializeField] Color killColor = new(1f, .6f, .22f);
    [SerializeField, Tooltip("Also show message log lines (pickups, doors, kills). Combat damage is always left out.")] bool showEvents = true;
    [SerializeField, Min(8)] float fontSize = 30;
    [SerializeField, Range(0, 1)] float outline = .12f;
    [SerializeField, Min(0), Tooltip("Seconds a line stays before fading.")] float hold = 6f;
    [SerializeField, Min(0)] float fadeOut = 1.5f;
    [SerializeField, Min(1)] int maxLines = 4;

    [Header("Mumble")]
    [SerializeField, Tooltip("Key prefix in AudioManager's UI Library: every entry starting with it is a syllable (dmMumble1, dmMumble2...).")] string mumbleKey = "dmMumble";
    [SerializeField, Range(0, .5f)] float mumblePitchVariance = .12f;
    [SerializeField, Min(.03f), Tooltip("Seconds between syllables.")] float syllableGap = .09f;
    [SerializeField, Min(1), Tooltip("Syllables per word, capped by Max Syllables.")] float syllablesPerWord = 1.5f;
    [SerializeField, Min(1)] int maxSyllables = 14;

    readonly Queue<(Line line, float delay)> queue = new();
    readonly List<(string text, float shown, Color color)> lines = new();
    readonly StringBuilder builder = new();
    Coroutine running;

    protected override void Awake()
    {
        base.Awake();
        ApplyStyle();
        if (label != null) label.text = "";
    }

    void OnValidate() { if (label != null) ApplyStyle(); }

    // Layout and look come from here so the placement switch is one field.
    void ApplyStyle()
    {
        var r = label.rectTransform;
        bool middle = placement == Placement.LowerMiddle;
        r.anchorMin = r.anchorMax = r.pivot = middle ? new Vector2(.5f, 0) : Vector2.zero;
        r.anchoredPosition = middle ? new Vector2(0, 250) : new Vector2(40, 300);
        r.sizeDelta = new Vector2(middle ? 1200 : 900, 200);
        label.alignment = middle ? TextAlignmentOptions.Bottom : TextAlignmentOptions.BottomLeft;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        if (Application.isPlaying) { label.outlineWidth = outline; label.outlineColor = Color.black; }
    }

    public static void Say(string text, AudioClip clip = null, bool mumble = false) => Say(new Line { text = text, mumble = mumble }, clip);
    public static void Say(string text, AudioData data, bool mumble = false) => Say(new Line { text = text, voice = data, mumble = mumble });
    public static void Say(DMLine asset) { if (asset != null) Say(new Line { text = asset.text, voice = asset.voice, mumble = asset.mumble }); }
    public static void Say(Line line, AudioClip clip = null, float delay = 0)
    {
        if (!HasInstance || line == null || string.IsNullOrWhiteSpace(line.text)) return;
        Instance.Enqueue(line, clip, delay);
    }

    AudioClip pendingClip;
    void Enqueue(Line line, AudioClip clip, float delay)
    {
        if (clip != null) { line = new Line { text = line.text, mumble = false }; pendingClip = clip; }
        queue.Enqueue((line, delay));
        if (running == null && isActiveAndEnabled) running = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        while (queue.Count > 0)
        {
            var (line, delay) = queue.Dequeue();
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            if (!string.IsNullOrWhiteSpace(prefix)) Show(prefix, color);
            Show($"\"{line.text.Trim()}\"", color);
            float speaking = .5f;
            if (AudioManager.HasInstance)
            {
                AudioSource voice = null;
                if (line.voice != null) voice = AudioManager.Instance.PlayUIData(line.voice);
                else if (pendingClip != null) { voice = AudioManager.Instance.PlayUI(pendingClip); pendingClip = null; }
                if (voice != null && voice.clip != null) speaking = voice.clip.length;
                else if (line.mumble) speaking = Mumble(line.text);
            }
            yield return new WaitForSecondsRealtime(speaking + .6f);
        }
        running = null;
    }

    void Show(string text, Color tint)
    {
        lines.Add((text, Time.unscaledTime, tint));
        while (lines.Count > maxLines) lines.RemoveAt(0);
    }

    float Mumble(string text)
    {
        if (!AudioManager.HasInstance || string.IsNullOrEmpty(mumbleKey)) return .5f;
        int words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        int count = Mathf.Clamp(Mathf.RoundToInt(words * syllablesPerWord), 2, maxSyllables);
        StartCoroutine(Syllables(count));
        return count * syllableGap;
    }

    IEnumerator Syllables(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (AudioManager.HasInstance) AudioManager.Instance.PlayUIVariant(mumbleKey, mumblePitchVariance);
            yield return new WaitForSecondsRealtime(syllableGap * Random.Range(.8f, 1.25f));
        }
    }

    void Update()
    {
        if (label == null) return;
        float now = Time.unscaledTime;
        lines.RemoveAll(l => now - l.shown > hold + fadeOut);
        builder.Clear();
        foreach (var (text, shown, tint) in lines)
        {
            float alpha = Mathf.Clamp01(1 - (now - shown - hold) / Mathf.Max(.01f, fadeOut));
            var c = tint; c.a *= alpha;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGBA(c)).Append('>').Append(text).Append("</color>");
        }
        label.text = builder.ToString();
    }

    void OnEnable() { if (MessageLog.HasInstance) MessageLog.Instance.Posted += OnLog; }
    // MessageLog may wake after this; subscribe once it exists.
    void Start() { if (MessageLog.HasInstance) { MessageLog.Instance.Posted -= OnLog; MessageLog.Instance.Posted += OnLog; } }

    void OnLog(MessageLog.Entry entry)
    {
        if (!showEvents || entry.kind == MessageKind.Combat) return;
        Show(MessageLog.Format(entry), entry.kind == MessageKind.Kill ? killColor : eventColor);
    }

    void OnDisable() { if (MessageLog.HasInstance) MessageLog.Instance.Posted -= OnLog; if (running != null) StopCoroutine(running); running = null; queue.Clear(); lines.Clear(); if (label != null) label.text = ""; }
}

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
// The DM speaks through DungeonMaster.Say(...), lines typed on the components and levels that use them, a biome's entry lines, or Lua DMSay("...").
public class DungeonMaster : Singleton<DungeonMaster>
{
    public enum Placement { LowerMiddle = 0, BottomLeft = 1 }

    [Serializable]
    public class Line
    {
        [TextArea] public string text;
        [Tooltip("Recorded voice. Empty uses the mumble if Mumble is on.")] public AudioData voice;
        [Tooltip("Barony-style gibberish when the line appears.")] public bool mumble = true;
        [Tooltip("Said across the table in the room, so no \"voice inside your head\" prefix.")] public bool inPerson;

        // The line with its text changed (a name filled in), keeping voice and delivery.
        public Line With(string newText) => new() { text = newText, voice = voice, mumble = mumble, inPerson = inPerson };
        public bool IsEmpty => string.IsNullOrWhiteSpace(text);
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
    [SerializeField, Min(0), Tooltip("Lines said in person (a conversation at the table) stay at least this long, plus the time per word, before the next one.")]
    float readBase = 1.2f;
    [SerializeField, Min(0)] float readPerWord = .3f;

    [Header("Mumble")]
    [SerializeField, Tooltip("Continuous babble phrases, one per line: the clip nearest the line's length plays (Tools/dm_babble.py makes them). Empty: the syllables below.")]
    AudioData babble;
    [SerializeField, Min(.05f), Tooltip("Seconds of babble per word.")] float babblePerWord = .22f;
    [SerializeField, Tooltip("UI Library key played once per syllable. Make it a Data entry to pick from several syllables.")] string mumbleKey = "dmMumble";
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
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false;
        if (Application.isPlaying) { label.outlineWidth = outline; label.outlineColor = Color.black; }
    }

    public static void Say(string text, AudioClip clip = null, bool mumble = false) => Say(new Line { text = text, mumble = mumble }, clip);
    public static void Say(string text, AudioData data, bool mumble = false) => Say(new Line { text = text, voice = data, mumble = mumble });
    public static void Say(Line line, AudioClip clip = null, float delay = 0)
    {
        if (!HasInstance || line == null || string.IsNullOrWhiteSpace(line.text)) return;
        Instance.Enqueue(line, clip, delay);
    }

    // Each line as it appears (DungeonMasterSeat gestures on the ones said in person).
    public static event Action<Line> Spoke;

    // True while lines are still queued or being spoken.
    public bool Speaking => running != null;
    bool wasCutscene;

    // Drops lines not yet said (a run ended: what was meant for the dungeon is not said in the room).
    public void Silence()
    {
        queue.Clear(); pendingClip = null;
        if (running != null) { StopCoroutine(running); running = null; }
    }

    AudioClip pendingClip;
    void Enqueue(Line line, AudioClip clip, float delay)
    {
        if (clip != null) { line = new Line { text = line.text, mumble = false, inPerson = line.inPerson }; pendingClip = clip; }
        queue.Enqueue((line, delay));
        if (running == null && isActiveAndEnabled) running = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        while (queue.Count > 0)
        {
            var (line, delay) = queue.Dequeue();
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            // The label sits on the HUD canvas, which cutscenes hide: hold the line until it can be read.
            while (GameManager.HasInstance && GameManager.Instance.State == GameState.Cutscene) yield return null;
            float speaking = Speak(line);
            float pause = speaking + .6f;
            if (line.inPerson) pause = Mathf.Max(pause, readBase + readPerWord * line.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length);
            yield return new WaitForSecondsRealtime(pause);
        }
        running = null;
    }

    // A line straight away, outside the queue: a conversation paces its own lines.
    public void SayNow(Line line)
    {
        if (line == null || string.IsNullOrWhiteSpace(line.text)) return;
        Speak(line);
    }

    // Shows the line and starts its voice or mumble. Returns the seconds of speech.
    float Speak(Line line)
    {
        // The "voice in your head" introduction once per stretch of lines, not before each one.
        bool stillOnScreen = lines.Exists(l => l.text == prefix);
        if (!line.inPerson && !string.IsNullOrWhiteSpace(prefix) && !stillOnScreen) Show(prefix, color);
        Show($"\"{line.text.Trim()}\"", color);
        Spoke?.Invoke(line);
        float speaking = .5f;
        if (AudioManager.HasInstance)
        {
            AudioSource voice = null;
            if (line.voice != null) voice = AudioManager.Instance.PlayUIData(line.voice);
            else if (pendingClip != null) { voice = AudioManager.Instance.PlayUI(pendingClip); pendingClip = null; }
            if (voice != null && voice.clip != null) speaking = voice.clip.length;
            else if (line.mumble) speaking = Mumble(line.text);
        }
        return speaking;
    }

    void Show(string text, Color tint)
    {
        lines.Add((text, Time.unscaledTime, tint));
        while (lines.Count > maxLines) lines.RemoveAt(0);
    }

    float Mumble(string text)
    {
        if (!AudioManager.HasInstance) return .5f;
        int words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
        if (babble != null && babble.clips != null && babble.clips.Length > 0) return Babble(words);
        if (string.IsNullOrEmpty(mumbleKey)) return .5f;
        int count = Mathf.Clamp(Mathf.RoundToInt(words * syllablesPerWord), 2, maxSyllables);
        StartCoroutine(Syllables(count));
        return count * syllableGap;
    }

    AudioClip lastBabble;

    // One phrase for the whole line: of the two clips nearest its length, not the one heard last.
    float Babble(int words)
    {
        float target = words * babblePerWord;
        AudioClip best = null, second = null;
        foreach (var c in babble.clips)
        {
            if (c == null) continue;
            if (best == null || Mathf.Abs(c.length - target) < Mathf.Abs(best.length - target)) { second = best; best = c; }
            else if (second == null || Mathf.Abs(c.length - target) < Mathf.Abs(second.length - target)) second = c;
        }
        if (best == null) return .5f;
        var clip = second != null && (best == lastBabble || Random.value < .35f) ? second : best;
        lastBabble = clip;
        float pitch = babble.pitch + (babble.pitchVariance > 0f ? Random.Range(-babble.pitchVariance, babble.pitchVariance) : 0f);
        AudioManager.Instance.PlayUI(clip, babble.volume, pitch);
        return clip.length / Mathf.Max(.1f, pitch);
    }

    IEnumerator Syllables(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (AudioManager.HasInstance && AudioManager.Instance.HasUI(mumbleKey)) AudioManager.Instance.PlayUI(mumbleKey);
            yield return new WaitForSecondsRealtime(syllableGap * Random.Range(.8f, 1.25f));
        }
    }

    void Update()
    {
        if (label == null) return;
        float now = Time.unscaledTime;
        lines.RemoveAll(l => now - l.shown > hold + fadeOut);
        builder.Clear();
        // Full-screen menus (the run recap shares this canvas) are not written over, and a conversation
        // has its own subtitles and choices in the same spot.
        if ((GameManager.HasInstance && (GameManager.Instance.State == GameState.Menu || GameManager.Instance.State == GameState.Paused))
            || PixelCrushers.DialogueSystem.DialogueManager.isConversationActive) { label.text = ""; return; }
        // A cutscene (the descent into a table level) leaves the lines from before it behind.
        bool cutscene = GameManager.HasInstance && GameManager.Instance.State == GameState.Cutscene;
        if (cutscene && !wasCutscene) lines.Clear();
        wasCutscene = cutscene;
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

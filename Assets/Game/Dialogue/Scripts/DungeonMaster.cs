using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using Random = UnityEngine.Random;

// The on-screen message feed: what just happened in white (kills in orange),
// and the Dungeon Master's voice in its own colour (the quoted line, with a recorded clip or a
// short gibberish mumble). Lines appear whole in the lower
// middle (or bottom left) and fade. Damage lines stay out: floating numbers show those.
// The DM speaks through DungeonMaster.Say(...), lines typed on the components and levels that use them, a biome's entry lines, or Lua DMSay("...").
// A part of a line written between asterisks is said in the highlight colour: "*The first rule.* Nobody leaves the
// table until I say so." shows "The first rule." in Highlight and the rest in Standard (the conversations too).
public class DungeonMaster : Singleton<DungeonMaster>
{
    public enum Placement { LowerMiddle = 0, BottomLeft = 1 }

    [Serializable]
    public class Line
    {
        [TextArea] public string text;
        [Tooltip("Recorded voice. Empty uses the mumble if Mumble is on.")] public AudioData voice;
        [Tooltip("Gibberish mumble when the line appears.")] public bool mumble = true;
        [Tooltip("Said across the table in the room: paced for reading, and the seated DM gestures.")] public bool inPerson;

        // The line with its text changed (a name filled in), keeping voice and delivery.
        public Line With(string newText) => new() { text = newText, voice = voice, mumble = mumble, inPerson = inPerson };
        public bool IsEmpty => string.IsNullOrWhiteSpace(text);
    }

    [SerializeField] TextMeshProUGUI label;
    [SerializeField, Tooltip("Where the lines sit on screen.")] Placement placement = Placement.LowerMiddle;
    [Header("Text colours")]
    [SerializeField, FormerlySerializedAs("color"), Tooltip("The Dungeon Master's lines, here and in conversations at the table.")] Color standardColor = new(.45f, .85f, .95f);
    [SerializeField, Tooltip("The part of a Dungeon Master line written between asterisks (*The first rule.*).")] Color highlightColor = Color.white;
    [SerializeField, Tooltip("Everything else that happens.")] Color eventColor = Color.white;
    [SerializeField] Color killColor = new(1f, .6f, .22f);
    [Header("Feed")]
    [SerializeField, Tooltip("Also show message log lines (pickups, doors, kills). Combat damage is always left out.")] bool showEvents = true;
    [SerializeField, Min(8)] float fontSize = 30;
    [SerializeField, Range(0, 1)] float outline = .12f;
    [SerializeField, Range(0, 1), Tooltip("A soft dark halo behind the letters, so the lines read on pale stone too (0: none).")] float backing = .7f;
    [SerializeField, Min(0), Tooltip("Screen pixels per em of the pixel font (PixelOperator: 16). The size snaps so each font pixel covers whole screen pixels, which keeps the letters crisp at any resolution. 0: no snapping.")]
    float pixelGrid = 16;
    [SerializeField, Min(0), Tooltip("Seconds a line stays before fading.")] float hold = 6f;
    [SerializeField, Min(0)] float fadeOut = 1.5f;
    [SerializeField, Min(1)] int maxLines = 4;
    [SerializeField, Min(0), Tooltip("Lines said in person (a conversation at the table) stay at least this long, plus the time per word, before the next one.")]
    float readBase = 1.2f;
    [SerializeField, Min(0)] float readPerWord = .3f;

    readonly Queue<(Line line, float delay)> queue = new();
    // Oldest first. hex is the colour's RGB, worked out once when the line is added.
    // marked: a Dungeon Master line, whose *asterisked* parts take the highlight colour.
    readonly List<(string text, float shown, Color color, string hex, bool marked)> lines = new();
    readonly StringBuilder builder = new();
    // The label is rebuilt only when lines were added or removed, or one is fading.
    bool dirty, blanked;
    static readonly string[] AlphaHex = BuildAlphaHex();
    static string[] BuildAlphaHex()
    {
        var hex = new string[256];
        for (int i = 0; i < hex.Length; i++) hex[i] = i.ToString("X2");
        return hex;
    }
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
        if (!Application.isPlaying) return;
        label.outlineWidth = outline; label.outlineColor = Color.black;
        var m = label.fontMaterial;   // the label's own copy
        if (backing > 0f)
        {
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, backing));
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, .7f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .8f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
        }
        else m.DisableKeyword(ShaderUtilities.Keyword_Underlay);
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
        StopVoice();
    }

    // The voice clip or mumble of the line being said, stopped when the line is skipped or dropped.
    AudioSource voiceSource;
    AudioClip voiceClip;
    public void StopVoice()
    {
        // A paused voice (the pause menu) reads as not playing, but is stopped all the same.
        if (voiceSource != null && (voiceSource.isPlaying || voicePaused) && voiceSource.clip == voiceClip) voiceSource.Stop();
        voiceSource = null; voiceClip = null;
        if (AudioManager.HasInstance) AudioManager.Instance.StopMumble();
    }

    // Lines show over full-screen menus while this is on (the run recap's closing line). Turning it on also drops what
    // was said or waiting from before, so only what is said from then on shows.
    public static bool OverMenus
    {
        get => overMenus;
        set
        {
            if (value && !overMenus && HasInstance) { Instance.Silence(); Instance.lines.Clear(); Instance.dirty = true; }
            overMenus = value;
        }
    }
    static bool overMenus;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetOverMenus() => overMenus = false;

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
            // The label sits on the HUD canvas, which cutscenes and the pause menu hide: hold the line until it can be read.
            while (GameManager.HasInstance && GameManager.Instance.State is GameState.Cutscene or GameState.Paused) yield return null;
            float speaking = Speak(line);
            float pause = speaking + .6f;
            if (line.inPerson) pause = Mathf.Max(pause, readBase + readPerWord * line.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length);
            // In a conversation scene (Dialogue state: the intro, the character sheet) a click, Space, Enter or
            // gamepad South moves on to the next line.
            // The pause menu holds the line (and its voice) where it is: whoever waits on it waits too.
            for (float t = 0f; t < pause; )
            {
                if (Paused) { yield return null; continue; }
                if (t > .2f && Skippable && InputManager.HasInstance && InputManager.Instance.SkipLinePressed) { StopVoice(); break; }
                yield return null;
                if (!Paused) t += Time.unscaledDeltaTime;
            }
        }
        running = null;
    }

    static bool Skippable => GameManager.HasInstance && GameManager.Instance.State == GameState.Dialogue;
    static bool Paused => GameManager.HasInstance && GameManager.Instance.State == GameState.Paused;
    // Held from Update, so a line said outside the queue (SayNow) is held too.
    bool voicePaused;
    void PauseVoice(bool pause)
    {
        if (pause == voicePaused) return;
        voicePaused = pause;
        // A mumble is only sound under the line: it stops rather than waits (its syllables run on real time).
        if (pause && AudioManager.HasInstance) AudioManager.Instance.StopMumble();
        if (voiceSource == null || voiceSource.clip != voiceClip) return;
        if (pause) voiceSource.Pause(); else voiceSource.UnPause();
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
        Show($"\"{line.text.Trim()}\"", standardColor, true);
        Spoke?.Invoke(line);
        float speaking = .5f;
        if (AudioManager.HasInstance)
        {
            StopVoice();   // a new line never talks over the last one's voice
            AudioSource voice = null;
            if (line.voice != null) voice = AudioManager.Instance.PlayUIData(line.voice);
            else if (pendingClip != null) { voice = AudioManager.Instance.PlayUI(pendingClip); pendingClip = null; }
            if (voice != null) { voiceSource = voice; voiceClip = voice.clip; }
            if (voice != null && voice.clip != null) speaking = voice.clip.length;
            else if (line.mumble) speaking = Mumble(Plain(line.text));
        }
        return speaking;
    }

    void Show(string text, Color tint, bool marked = false)
    {
        lines.Add((text, Time.unscaledTime, tint, ColorUtility.ToHtmlStringRGB(tint), marked));
        while (lines.Count > maxLines) lines.RemoveAt(0);
        dirty = true;
    }

    // The voice is chosen on AudioManager (Dungeon Master voice).
    static float Mumble(string text) => AudioManager.HasInstance ? AudioManager.Instance.Mumble(text) : .5f;

    float snappedFor;

    // Font Size is the size wanted at the reference resolution; the nearest whole multiple of the font's
    // pixel grid on the actual screen is used.
    void SnapSize()
    {
        float scale = label.canvas != null ? label.canvas.scaleFactor : 1f;
        if (pixelGrid <= 0 || scale <= 0 || Mathf.Approximately(scale, snappedFor)) return;
        snappedFor = scale;
        int multiple = Mathf.Max(1, Mathf.RoundToInt(fontSize * scale / pixelGrid));
        label.fontSize = pixelGrid * multiple / scale;
    }

    void Update()
    {
        PauseVoice(Paused);
        if (label == null) return;
        SnapSize();
        float now = Time.unscaledTime;
        // Paused: the lines on screen keep their age, so none fades behind the menu.
        if (Paused) for (int i = 0; i < lines.Count; i++) { var l = lines[i]; l.shown += Time.unscaledDeltaTime; lines[i] = l; }
        while (lines.Count > 0 && now - lines[0].shown > hold + fadeOut) { lines.RemoveAt(0); dirty = true; }
        // Full-screen menus (the run recap shares this canvas) are not written over unless one asks for it (OverMenus),
        // and a conversation has its own subtitles and choices in the same spot.
        if ((GameManager.HasInstance && !overMenus && (GameManager.Instance.State == GameState.Menu || GameManager.Instance.State == GameState.Paused))
            || PixelCrushers.DialogueSystem.DialogueManager.isConversationActive)
        {
            if (!blanked) { label.text = ""; blanked = true; }
            return;
        }
        // A cutscene (the descent into a table level) leaves the lines from before it behind.
        bool cutscene = GameManager.HasInstance && GameManager.Instance.State == GameState.Cutscene;
        if (cutscene && !wasCutscene && lines.Count > 0) { lines.Clear(); dirty = true; }
        wasCutscene = cutscene;
        bool fading = lines.Count > 0 && now - lines[0].shown > hold;   // the oldest line fades first
        if (!dirty && !blanked && !fading) return;
        dirty = blanked = false;
        builder.Clear();
        foreach (var (text, shown, tint, hex, marked) in lines)
        {
            float alpha = Mathf.Clamp01(1 - (now - shown - hold) / Mathf.Max(.01f, fadeOut));
            if (builder.Length > 0) builder.Append('\n');
            if (marked) AppendMarked(builder, text, alpha);
            else builder.Append("<color=#").Append(hex).Append(AlphaHex[Mathf.RoundToInt(Mathf.Clamp01(tint.a * alpha) * 255)])
                .Append('>').Append(text).Append("</color>");
        }
        label.text = builder.ToString();
    }

    // A Dungeon Master line in rich text: Standard, with the parts between asterisks in Highlight (alpha fades both).
    void AppendMarked(StringBuilder into, string text, float alpha = 1f)
    {
        bool high = false;
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '*') continue;
            if (i > start)
            {
                var tint = high ? highlightColor : standardColor;
                into.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(tint)).Append(AlphaHex[Mathf.RoundToInt(Mathf.Clamp01(tint.a * alpha) * 255)])
                    .Append('>').Append(text, start, i - start).Append("</color>");
            }
            high = !high;
            start = i + 1;
        }
    }

    // The line coloured for a conversation's subtitle (DialogueBridge), the same way as in the feed.
    public static string Colored(string text)
    {
        if (!HasInstance || string.IsNullOrEmpty(text)) return Plain(text);
        var into = new StringBuilder();
        Instance.AppendMarked(into, text);
        return into.ToString();
    }

    // The line without its highlight marks (for the mumble and anything that shows it plainly).
    public static string Plain(string text) => string.IsNullOrEmpty(text) ? text : text.Replace("*", "");

    void OnEnable() { if (MessageLog.HasInstance) MessageLog.Instance.Posted += OnLog; }
    // MessageLog may wake after this; subscribe once it exists.
    void Start() { if (MessageLog.HasInstance) { MessageLog.Instance.Posted -= OnLog; MessageLog.Instance.Posted += OnLog; } }

    void OnLog(MessageLog.Entry entry)
    {
        if (!showEvents || entry.kind == MessageKind.Combat || entry.kind == MessageKind.Pickup && PickupFeedUI.Active) return;   // pickups have their own list
        Show(MessageLog.Format(entry), entry.kind == MessageKind.Kill ? killColor : eventColor);
    }

    void OnDisable() { if (MessageLog.HasInstance) MessageLog.Instance.Posted -= OnLog; if (running != null) StopCoroutine(running); running = null; queue.Clear(); lines.Clear(); dirty = true; if (label != null) label.text = ""; StopVoice(); }
}

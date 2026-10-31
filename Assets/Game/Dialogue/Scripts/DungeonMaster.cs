using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Sirenix.OdinInspector;
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
    [Header("TikTok mode")]
    [SerializeField, Tooltip("For recording vertical (9:16) videos: the Dungeon Master's lines, his conversation lines and the choices sit in the middle of the screen, no wider than a 9:16 crop of it, so a whole line is always in the video. Switch it on before recording.")]
    bool tiktokMode;
    [SerializeField, Min(200), Tooltip("Width of the lines in TikTok mode, in the 1920 x 1080 canvas: a 9:16 crop of it is 607 wide.")]
    float tiktokWidth = 560f;
    public static bool TikTokMode => HasInstance && Instance.tiktokMode;

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
    [SerializeField, Tooltip("One line at a time: a new Dungeon Master line takes the place of every line before it, and a new message (a pickup, a kill) takes the place of earlier messages, never of his line.")]
    bool soloStrings;
    [SerializeField, Tooltip("The Dungeon Master's lines are typed out letter by letter. A line stays Hold seconds once it is all typed out.")]
    bool typewriter;
    [SerializeField, Min(1), ShowIf(nameof(typewriter)), Tooltip("Letters typed a second.")] float charactersPerSecond = 40f;
    [SerializeField, Min(0), ShowIf(nameof(typewriter)), Tooltip("Extra seconds after a full stop, question or exclamation mark (a comma, colon or semicolon waits half as long).")]
    float punctuationPause = .25f;
    [SerializeField, Min(0), Tooltip("Each line stays this long, plus the time per word, once it is typed out, before the next queued line takes its place (and at least as long as its voice).")]
    float readBase = 1.2f;
    [SerializeField, Min(0)] float readPerWord = .3f;
    // Times the reading time of every line said while it is set (the intro's opening sets it shorter, IntroController.openingReading); 1 = as above.
    public float ReadingScale { get; set; } = 1f;

    // Lines waiting their turn, oldest first: a new line never cuts off the one being said. clip: a recorded voice
    // given with the line (Say(text, clip)).
    readonly List<(Line line, AudioClip clip, float delay)> queue = new();
    // Oldest first. hex is the colour's RGB, worked out once when the line is added.
    // marked: a Dungeon Master line, whose *asterisked* parts take the highlight colour. typing: seconds it takes to type
    // out (0: shown whole); its hold and fade come after.
    readonly List<(string text, float shown, Color color, string hex, bool marked, float typing)> lines = new();
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

    // Applied a frame later: Unity doesn't allow changing a RectTransform inside OnValidate (it warned "SendMessage cannot
    // be called during Awake, CheckConsistency, or OnValidate" on every change).
    void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || label == null) return;
            ApplyStyle();
            // A change to TikTok mode in Play reaches an open conversation's panels too.
            if (Application.isPlaying && PixelCrushers.DialogueSystem.DialogueManager.isConversationActive) FitConversation();
        };
#endif
    }

    // Layout and look come from here so the placement switch is one field.
    void ApplyStyle()
    {
        var r = label.rectTransform;
        bool middle = placement == Placement.LowerMiddle || tiktokMode;
        r.anchorMin = r.anchorMax = r.pivot = middle ? new Vector2(.5f, 0) : Vector2.zero;
        r.anchoredPosition = middle ? new Vector2(0, 250) : new Vector2(40, 300);
        r.sizeDelta = new Vector2(tiktokMode ? tiktokWidth : middle ? 1200 : 900, tiktokMode ? 400 : 200);
        label.alignment = middle ? TextAlignmentOptions.Bottom : TextAlignmentOptions.BottomLeft;
        // (The conversation's panels are fitted as each conversation starts, DialogueBridge: asking for the dialogue UI
        // here, in Awake, made the Dialogue System build it then, and every SetParent in it warned.)
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
        queue.Clear();
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
        if (voiceSource != null && AudioManager.HasInstance) AudioManager.Instance.Hold(voiceSource, false);
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

    void Enqueue(Line line, AudioClip clip, float delay)
    {
        if (clip != null) line = new Line { text = line.text, mumble = false, inPerson = line.inPerson };
        queue.Add((line, clip, delay));
        if (running == null && isActiveAndEnabled) running = StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        while (queue.Count > 0)
        {
            var (line, clip, delay) = queue[0];
            queue.RemoveAt(0);
            if (delay > 0) yield return new WaitForSecondsRealtime(delay);
            // The label sits on the HUD canvas, which cutscenes and the pause menu hide: hold the line until it can be read.
            while (Cutscene || Paused) yield return null;
            float speaking = Speak(line, clip, out float typing);
            // The next line waits until this one has been heard, typed out and read.
            float pause = Mathf.Max(speaking + .6f, typing + Reading(line.text) * ReadingScale);
            // In a conversation scene (Dialogue state: the intro, the character sheet) a click, Space, Enter or
            // gamepad South moves on to the next line.
            // The pause menu holds the line (and its voice) where it is: whoever waits on it waits too.
            for (float t = 0f; t < pause; )
            {
                if (Paused) { yield return null; continue; }
                // A cutscene (a floor being built on the table) hides the feed: a line it cuts off mid-voice stops and
                // is said again, whole, once the cutscene is over, so the voice never plays without its words.
                if (Cutscene)
                {
                    if (t < speaking)
                    {
                        StopVoice();
                        string shown = line.text.Trim();
                        lines.RemoveAll(l => l.marked && l.text == shown); dirty = true;
                        queue.Insert(0, (line, clip, 0f));
                    }
                    break;
                }
                if (t > .2f && Skippable && InputManager.HasInstance && InputManager.Instance.SkipLinePressed) { StopVoice(); break; }
                yield return null;
                if (!Paused) t += Time.unscaledDeltaTime;
            }
        }
        running = null;
    }

    static bool Skippable => GameManager.HasInstance && GameManager.Instance.State == GameState.Dialogue;
    static bool Paused => GameManager.HasInstance && GameManager.Instance.State == GameState.Paused;
    static bool Cutscene => GameManager.HasInstance && GameManager.Instance.State == GameState.Cutscene;
    // Seconds to read a line once it is all on screen.
    float Reading(string text) => readBase + readPerWord * text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
    // Held from Update, so a line said outside the queue (SayNow) is held too.
    bool voicePaused;
    void PauseVoice(bool pause)
    {
        if (pause == voicePaused) return;
        voicePaused = pause;
        // A mumble is only sound under the line: it stops rather than waits (its syllables run on real time).
        if (pause && AudioManager.HasInstance) AudioManager.Instance.StopMumble();
        if (voiceSource == null || voiceSource.clip != voiceClip) return;
        // Held while paused, so the pool doesn't hand the paused voice's source to another sound.
        if (AudioManager.HasInstance) AudioManager.Instance.Hold(voiceSource, pause);
        if (pause) voiceSource.Pause(); else voiceSource.UnPause();
    }

    // A line straight away, outside the queue: a conversation paces its own lines.
    public void SayNow(Line line)
    {
        if (line == null || string.IsNullOrWhiteSpace(line.text)) return;
        Speak(line, null, out _);
    }

    // Shows the line and starts its voice (its own, the clip given with it, or the mumble). Returns the seconds of
    // speech; typing: the seconds it takes to type out.
    float Speak(Line line, AudioClip clip, out float typing)
    {
        typing = Show(line.text.Trim(), standardColor, true);
        Spoke?.Invoke(line);
        float speaking = .5f;
        if (AudioManager.HasInstance)
        {
            StopVoice();   // a new line never talks over the last one's voice
            AudioSource voice = null;
            if (line.voice != null) voice = AudioManager.Instance.PlayUIData(line.voice);
            else if (clip != null) voice = AudioManager.Instance.PlayUI(clip);
            if (voice != null) { voiceSource = voice; voiceClip = voice.clip; }
            if (voice != null && voice.clip != null) speaking = voice.clip.length;
            else if (line.mumble) speaking = Mumble(Plain(line.text));
        }
        return speaking;
    }

    // Returns the seconds the line takes to type out (0 when shown whole).
    float Show(string text, Color tint, bool marked = false)
    {
        // Solo: a line of his replaces everything; a message replaces only earlier messages.
        if (soloStrings) lines.RemoveAll(l => marked || !l.marked);
        float typing = marked && typewriter ? TypeOut(text, float.MaxValue, out _) : 0f;
        lines.Add((text, Time.unscaledTime, tint, ColorUtility.ToHtmlStringRGB(tint), marked, typing));
        while (lines.Count > maxLines) lines.RemoveAt(0);
        dirty = true;
        return typing;
    }

    // Typing a line out: how many of its letters (the highlight marks don't count) show after elapsed seconds, and how
    // long the whole line takes.
    float TypeOut(string text, float elapsed, out int shown)
    {
        float t = 0f, step = 1f / Mathf.Max(1f, charactersPerSecond);
        shown = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '*') continue;
            if (t > elapsed) return t;
            shown++;
            t += step;
            if (c == '.' || c == '!' || c == '?') t += punctuationPause;
            else if (c == ',' || c == ';' || c == ':') t += punctuationPause * .5f;
        }
        return t;
    }

    // The conversation screen's subtitle and choice panels: as wide as the feed's lines (narrow in TikTok mode).
    public void FitConversation()
    {
        var ui = PixelCrushers.DialogueSystem.DialogueManager.hasInstance ? PixelCrushers.DialogueSystem.DialogueManager.dialogueUI as Component : null;
        if (ui == null) return;
        float width = tiktokMode ? tiktokWidth : 1200f;
        foreach (var rect in ui.GetComponentsInChildren<RectTransform>(true))
            if (rect.name == "NPC Subtitle Panel" || rect.name == "PC Subtitle Panel" || rect.name == "Response Menu Panel")
                rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
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
        if (lines.RemoveAll(l => now - l.shown > l.typing + hold + fadeOut) > 0) dirty = true;
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
        bool fading = false, typing = false;
        foreach (var l in lines) { float age = now - l.shown; if (age > l.typing + hold) fading = true; else if (age < l.typing) typing = true; }
        if (!dirty && !blanked && !fading && !typing) return;
        dirty = blanked = false;
        builder.Clear();
        foreach (var (text, shown, tint, hex, marked, lineTyping) in lines)
        {
            float alpha = Mathf.Clamp01(1 - (now - shown - lineTyping - hold) / Mathf.Max(.01f, fadeOut));
            if (builder.Length > 0) builder.Append('\n');
            int typed = int.MaxValue;
            if (lineTyping > 0f && now - shown < lineTyping) TypeOut(text, now - shown, out typed);
            if (marked) AppendMarked(builder, text, alpha, typed);
            else builder.Append("<color=#").Append(hex).Append(AlphaHex[Mathf.RoundToInt(Mathf.Clamp01(tint.a * alpha) * 255)])
                .Append('>').Append(text).Append("</color>");
        }
        label.text = builder.ToString();
    }

    // A Dungeon Master line in rich text: Standard, with the parts between asterisks in Highlight (alpha fades both).
    // typed: how many of its letters show (the typewriter); the rest are written fully clear, so the line keeps its
    // shape and doesn't shift as it types.
    void AppendMarked(StringBuilder into, string text, float alpha = 1f, int typed = int.MaxValue)
    {
        bool high = false;
        int start = 0, letters = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '*') continue;
            if (i > start)
            {
                var tint = high ? highlightColor : standardColor;
                string rgb = ColorUtility.ToHtmlStringRGB(tint);
                int length = i - start, seen = Mathf.Clamp(typed - letters, 0, length);
                if (seen > 0) into.Append("<color=#").Append(rgb).Append(AlphaHex[Mathf.RoundToInt(Mathf.Clamp01(tint.a * alpha) * 255)]).Append('>').Append(text, start, seen).Append("</color>");
                if (seen < length) into.Append("<color=#").Append(rgb).Append("00>").Append(text, start + seen, length - seen).Append("</color>");
                letters += length;
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

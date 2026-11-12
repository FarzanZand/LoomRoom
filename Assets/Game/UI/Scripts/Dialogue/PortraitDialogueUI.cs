using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A dialogue box at the foot of the screen with the speaker's head and shoulders idling at its left, their name on a
// plate above it and the line typed out with a soft blip a letter (an alternative to the Dialogue System's
// conversations, for the boss and the dungeon's people: BossFight > Intro, the tavern keeper). The Dungeon Master never speaks in it:
// his lines among them go to his own feed as always. A click, Space, Enter or a pad's South
// finishes the typing, then moves on; a line with answers shows them as buttons (keys and pad move between them).
// Its look is the Boss dialogue prefab (Dialogue/UI, under the Dialogue Manager); code only fills it.
public class PortraitDialogueUI : MonoBehaviour
{
    public static PortraitDialogueUI Current { get; private set; }

    [SerializeField] CanvasGroup group;
    [SerializeField] RawImage portrait;
    [SerializeField] TMP_Text nameText, body;
    [SerializeField] Graphic arrow;
    [SerializeField] RectTransform choiceColumn;
    [SerializeField] Button choiceTemplate;

    [Header("Typing")]
    [Min(1), Tooltip("Letters a second.")] public float charactersPerSecond = 38f;
    [Min(0), Tooltip("Seconds held after . ! ? (half after , ; :).")] public float punctuationPause = .22f;
    [Tooltip("Played as letters appear (one picked each time).")] public AudioClip[] blips = new AudioClip[0];
    [Range(0, 1)] public float blipVolume = .35f;
    [Min(1), Tooltip("A blip every this many letters.")] public int blipEvery = 2;
    [Range(0, .3f)] public float blipPitchVariance = .06f;
    [Tooltip("The blips' pitch for the boss (a deep voice is lower) and anyone else.")]
    public float bossPitch = .8f, otherPitch = 1f;
    [Min(0)] public float fadeSeconds = .18f;
    [Min(0), Tooltip("Canvas units a line said mid-fight (Bark) sits higher than a conversation's, clear of the hotbar and vitals the HUD keeps showing.")] public float barkRaise = 130f;
    [Header("Portrait")]
    [Tooltip("Pixels across the speaker's portrait (drawn point-filtered, so fewer is chunkier).")] public int portraitResolution = 256;
    [Tooltip("Degrees the speaker is turned from facing the camera.")] public float portraitTurn = 22f;
    [Tooltip("How much body shows below the head, in head-to-chest lengths (more: a smaller figure).")] public float portraitBodyBelow = 1.9f;
    [Tooltip("Room above the head, in head-to-chest lengths (horns and helmets get their own room on top).")] public float portraitRoomAbove = .55f;

    PortraitStage stage;
    readonly List<Button> choiceButtons = new();
    int picked = -1, selected;

    public bool Open { get; private set; }
    // The answer taken in the last Play (its index among that line's choices), -1 for none.
    public int Picked => picked;
    // A line said in the middle of play (Bark): no state pushed, nothing to press, the HUD stays.
    public bool Barking { get; private set; }
    // While it is up the hotbar and vitals step aside (HudCanvas.BehindMenu); not for a bark.
    public static bool Covers => Current != null && Current.Open && !Current.Barking;
    // The box is on screen (a bark or a conversation; not while it has stepped aside for the Dungeon Master's own line).
    public bool Showing => Open && group != null && group.alpha > .01f;

    RectTransform box;
    Vector2 boxHome;

    void Awake()
    {
        Current = this;
        if (group != null) { box = (RectTransform)group.transform; boxHome = box.anchoredPosition; }
        RememberLayout();
        Hide(true);
    }

    void Raise(bool bark) { if (box != null) box.anchoredPosition = boxHome + (bark && placedAt == DialoguePlacement.Standard ? Vector2.up * barkRaise : Vector2.zero); }

    // ── Placement ──
    // The prefab's own layout is Standard. A corner keeps every font size and scales the rest around the text box: the
    // box Corner Scale as wide (its height kept, so the text has its lines), the portrait Corner Scale as big and in the
    // same place against the box, the text's left inset (the room beside the portrait) and the name plate scaled with it.
    [Header("Placement (corners)")]
    [Range(.3f, 1f), Tooltip("A corner box's width and portrait size against the Standard box's (the text keeps its size).")]
    public float cornerScale = .5f;
    [Min(0), Tooltip("Canvas units between a corner box and the screen's side.")] public float cornerMargin = 32f;
    [Min(0), Tooltip("Canvas units kept clear under the top of the screen for the top bars (experience, boss bar) by the upper corners.")]
    public float upperTop = 150f;
    [Min(0), Tooltip("Bottom Left: canvas units from the foot of the screen, clear of the health, mana and stamina bars.")]
    public float bottomLeftAbove = 180f;
    [Min(0), Tooltip("Bottom Right: canvas units from the foot of the screen, clear of the hotbar's right end.")] public float bottomRightAbove = 170f;
    [Min(0), Tooltip("Upper Right: canvas units between the box and the minimap at its right.")] public float mapGap = 24f;
    [Min(0), Tooltip("Upper Right: canvas units kept clear at the screen's right at least (the quest log under the minimap is wider than the map).")] public float upperRightClear = 610f;

    struct Home { public RectTransform r; public Vector2 aMin, aMax, pivot, pos, size; }
    readonly List<Home> homes = new();
    RectTransform frame, portraitRect, lineRect, nameRect, markerRect;
    DialoguePlacement placedAt = DialoguePlacement.Standard;
    public DialoguePlacement Placement => placedAt;

    void RememberLayout()
    {
        // The text box itself (the Box child); box above is the whole group, which a bark raises.
        frame = group != null ? group.transform.Find("Box") as RectTransform : null;
        if (frame == null) return;
        portraitRect = portrait != null ? portrait.rectTransform : null;
        lineRect = body != null ? body.rectTransform : null;
        nameRect = nameText != null ? nameText.rectTransform : null;
        markerRect = frame.Find("Marker") as RectTransform;
        foreach (var r in new[] { frame, portraitRect, choiceColumn, lineRect, nameRect, markerRect })
            if (r != null) homes.Add(new Home { r = r, aMin = r.anchorMin, aMax = r.anchorMax, pivot = r.pivot, pos = r.anchoredPosition, size = r.sizeDelta });
    }

    Home HomeOf(RectTransform r) { foreach (var h in homes) if (h.r == r) return h; return default; }

    void Restore() { foreach (var h in homes) { h.r.anchorMin = h.aMin; h.r.anchorMax = h.aMax; h.r.pivot = h.pivot; h.r.sizeDelta = h.size; h.r.anchoredPosition = h.pos; } }

    // Lays the box out for a placement (Standard puts the prefab's layout back; the bark's raise is applied after).
    void Place(DialoguePlacement where)
    {
        // Simple has no box of its own: a conversation said Simple (answers need the box) is the Standard box.
        if (where == DialoguePlacement.Simple) where = DialoguePlacement.Standard;
        placedAt = where;
        if (frame == null || homes.Count == 0) return;
        Restore();
        if (where == DialoguePlacement.Standard) return;
        var canvas = (RectTransform)frame.parent;
        float W = canvas.rect.width, H = canvas.rect.height, s = cornerScale;
        var hb = HomeOf(frame);
        float boxW = hb.size.x * s, boxH = hb.size.y;
        // The portrait against the box as the prefab has it (both anchored to the same point), scaled.
        float dx = 0f, dy = 0f; Vector2 portraitSize = Vector2.zero;
        if (portraitRect != null)
        {
            var hp = HomeOf(portraitRect);
            float boxLeft = hb.pos.x - hb.size.x * hb.pivot.x, boxBottom = hb.pos.y - hb.size.y * hb.pivot.y;
            dx = (hp.pos.x - hp.size.x * hp.pivot.x - boxLeft) * s;
            dy = (hp.pos.y - hp.size.y * hp.pivot.y - boxBottom) * s;
            portraitSize = hp.size * s;
        }
        float nameTop = nameRect != null ? HomeOf(nameRect).pos.y + HomeOf(nameRect).size.y : 0f;
        // The whole group against the box's bottom-left corner.
        float left = Mathf.Min(0f, dx), right = Mathf.Max(boxW, dx + portraitSize.x);
        float bottom = Mathf.Min(0f, dy), top = Mathf.Max(boxH + nameTop, dy + portraitSize.y);
        float mapRight = upperRightClear;
        if (DungeonHud.Map != null && DungeonHud.Map.isActiveAndEnabled)
        {
            var map = DungeonHud.Map.rectTransform;
            mapRight = Mathf.Max(mapRight, -map.anchoredPosition.x + map.rect.width + mapGap);
        }
        bool upper = where is DialoguePlacement.UpperLeft or DialoguePlacement.UpperRight;
        bool atLeft = where is DialoguePlacement.BottomLeft or DialoguePlacement.UpperLeft;
        float x = atLeft ? cornerMargin - left : W - (where == DialoguePlacement.UpperRight ? mapRight : cornerMargin) - right;
        float y = upper ? H - upperTop - top : (where == DialoguePlacement.BottomLeft ? bottomLeftAbove : bottomRightAbove) - bottom;

        void Put(RectTransform r, Vector2 pivot, Vector2 at, Vector2 size)
        {
            r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = pivot; r.sizeDelta = size; r.anchoredPosition = at;
        }
        Put(frame, Vector2.zero, new Vector2(x, y), new Vector2(boxW, boxH));
        if (portraitRect != null) Put(portraitRect, Vector2.zero, new Vector2(x + dx, y + dy), portraitSize);
        // The text's left inset is the room beside the portrait: scaled with it. The name plate and its marker too.
        if (lineRect != null) { var h = HomeOf(lineRect); lineRect.offsetMin = new Vector2((h.pos.x - h.size.x * h.pivot.x) * s, lineRect.offsetMin.y); }
        foreach (var r in new[] { nameRect, markerRect })
            if (r != null) { var h = HomeOf(r); r.anchoredPosition = new Vector2(h.pos.x * s, h.pos.y); }
        // Answers stand over the box's right end, or under it in an upper corner.
        if (choiceColumn != null)
        {
            var hc = HomeOf(choiceColumn);
            float inset = hb.pos.x + hb.size.x * (1f - hb.pivot.x) - hc.pos.x;
            if (upper) Put(choiceColumn, new Vector2(1f, 1f), new Vector2(x + boxW - inset, y + bottom - 16f), hc.size);
            else Put(choiceColumn, new Vector2(1f, 0f), new Vector2(x + boxW - inset, y + top + 16f), hc.size);
        }
    }
    void OnDestroy() { if (Current == this) Current = null; }

    // Put away at once (the fight whose lines these were was cancelled: the caller's coroutine stopped mid-line). A bark
    // runs on this box's own coroutine, so it is stopped here too.
    public void Abort()
    {
        StopAllCoroutines();
        ReleaseLeave();
        barks.Clear();
        Hide(true);
        if (stage != null) stage.Clear();
        Open = false; Barking = false;
        SlowReading(false);
    }

    // Plays the lines. The boss is the fight's boss (its look and, unless a line names someone, its name); firstShown
    // runs as the first box appears. Push the Dialogue state around it (BossStageEncounter does).
    // where: the box's placement (the speaker's BossDialogueData.placement).
    public IEnumerator Play(IReadOnlyList<PortraitLine> lines, Character boss, string bossName, Action firstShown = null, DialoguePlacement where = DialoguePlacement.Standard)
    {
        picked = -1;
        if (lines == null || lines.Count == 0) yield break;
        Open = true;
        Place(where);
        Raise(false);
        // The Dungeon Master's lines among them keep the conversation's pace (his feed's reading pause held the next
        // line a few seconds on a line already read). Put back by the end or Abort (a
        // coroutine stopped from outside never runs its finally).
        SlowReading(true);
        stage ??= new GameObject("Portrait stage").AddComponent<PortraitStage>();
        stage.resolution = portraitResolution; stage.turn = portraitTurn; stage.bodyBelow = portraitBodyBelow; stage.roomAbove = portraitRoomAbove;
        if (portrait != null) portrait.texture = null;
        shown = false; this.firstShown = firstShown;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line == null) continue;
            bool asks = line.choices != null && line.choices.Count > 0;
            yield return Line(line.speaker, line.name, line.model, line.text, boss, bossName, !asks, line.voice);
            if (!asks) continue;
            yield return Choose(line.choices);
            var choice = picked >= 0 && picked < line.choices.Count ? line.choices[picked] : null;
            if (choice?.reply == null) continue;
            foreach (var reply in choice.reply)
                if (reply != null) yield return Line(reply.speaker, reply.name, reply.model, reply.text, boss, bossName, true, reply.voice);
        }
        if (shown) yield return Fade(0f);
        Hide(false);
        stage.Clear();
        Open = false;
        SlowReading(false);
    }

    float readingBefore = -1f;

    void SlowReading(bool on)
    {
        if (!DungeonMaster.HasInstance) return;
        if (on && readingBefore < 0f) { readingBefore = DungeonMaster.Instance.ReadingScale; DungeonMaster.Instance.ReadingScale = readingBefore * readingScale; }
        else if (!on && readingBefore >= 0f) { DungeonMaster.Instance.ReadingScale = readingBefore; readingBefore = -1f; }
    }

    [Range(.2f, 1f), Tooltip("The Dungeon Master's reading pause on his lines in a conversation here, against his feed's own.")] public float readingScale = .55f;

    bool shown;
    Action firstShown;

    // One line said in the middle of play (a boss's taunt in its fight): the box with the speaker, typed out, held while
    // it is read (hold seconds plus a little a letter), then gone. It pushes no game state and can't be skipped, so the
    // fight goes on under it. Ignored while the box is up for anything else.
    // A bark said while another is up waits for it (one quick after another: the boss's crew broken up while its call
    // for them still shows).
    // where: Standard follows UIManager > Boss Bark Style as before; a corner says it in the box in that corner, out of
    // the middle of the view.
    // inBox: always in this box, whatever the Boss Bark Style (a conversation asset's speaker, LimbLoss).
    public void Bark(string text, Character speaker, string speakerName, float hold = 1.4f, DialoguePlacement where = DialoguePlacement.Standard, bool inBox = false)
    {
        if (string.IsNullOrWhiteSpace(text) || !isActiveAndEnabled) return;
        // Said without the box, so nothing covers the fight (UIManager > Boss Bark Style).
        // Simple: as a subtitle, never in the box.
        if (where == DialoguePlacement.Simple && BossBarkUI.Instance != null) { BossBarkUI.Instance.Say(text, speaker, speakerName, BossBarkStyle.Subtitle); return; }
        if (!inBox && where == DialoguePlacement.Standard && UIManager.BossBark != BossBarkStyle.Box && BossBarkUI.Instance != null) { BossBarkUI.Instance.Say(text, speaker, speakerName, UIManager.BossBark); return; }
        if (Barking) { barks.Enqueue((text, speaker, speakerName, hold)); return; }
        if (Open) return;
        StartCoroutine(BarkRun(text, speaker, speakerName, hold, where));
    }

    readonly Queue<(string text, Character speaker, string name, float hold)> barks = new();

    IEnumerator BarkRun(string text, Character speaker, string speakerName, float hold, DialoguePlacement where)
    {
        Open = true; Barking = true;
        Place(where);
        Raise(true);
        stage ??= new GameObject("Portrait stage").AddComponent<PortraitStage>();
        stage.resolution = portraitResolution; stage.turn = portraitTurn; stage.bodyBelow = portraitBodyBelow; stage.roomAbove = portraitRoomAbove;
        shown = false; firstShown = null;
        yield return Box(PortraitSpeaker.Boss, null, null, text, speaker, speakerName);
        if (arrow != null) arrow.enabled = false;
        yield return new WaitForSeconds(hold + text.Length * .03f);
        // The next waiting one follows on in the same box.
        while (barks.Count > 0)
        {
            var next = barks.Dequeue();
            yield return Box(PortraitSpeaker.Boss, null, null, next.text, next.speaker, next.name);
            if (arrow != null) arrow.enabled = false;
            yield return new WaitForSeconds(next.hold + next.text.Length * .03f);
        }
        yield return Fade(0f);
        Hide(false);
        stage.Clear();
        Open = false; Barking = false;
    }

    // One line: the Dungeon Master's in his own feed as always (the box steps aside while he
    // speaks), anyone else's in the box. Wait: hold until it is read and pressed on (a line with answers doesn't).
    // voice: a recorded voice for the line (the box's Voice), played instead of the blips or his mumble.
    IEnumerator Line(PortraitSpeaker speaker, string name, GameObject model, string text, Character boss, string bossName, bool wait, AudioData voice = null)
    {
        if (speaker == PortraitSpeaker.DungeonMaster)
        {
            if (shown) { yield return Fade(0f); shown = false; }
            Fire();
            DungeonMaster.Say(new DungeonMaster.Line { text = text, voice = voice, mumble = voice == null, inPerson = true });
            yield return null;
            while (DungeonMaster.HasInstance && DungeonMaster.Instance.Speaking) yield return null;
            yield break;
        }
        yield return Box(speaker, name, model, text, boss, bossName, voice);
        if (wait) yield return WaitForNext();
    }

    // The voice of the box on show, stopped when the next box comes or the box goes (only while it is still that
    // clip on that source: the pool hands finished sources to other sounds).
    AudioSource boxVoice;
    AudioClip boxVoiceClip;
    void StopBoxVoice()
    {
        if (boxVoice != null && boxVoice.isPlaying && boxVoice.clip == boxVoiceClip) boxVoice.Stop();
        boxVoice = null; boxVoiceClip = null;
    }

    void Fire() { firstShown?.Invoke(); firstShown = null; }

    IEnumerator Box(PortraitSpeaker speaker, string name, GameObject model, string text, Character boss, string bossName, AudioData voice = null)
    {
        StopBoxVoice();
        var source = speaker == PortraitSpeaker.Boss ? (boss != null ? boss.gameObject : null) : model;
        stage.Show(source);
        if (portrait != null) { portrait.texture = stage.Texture; portrait.enabled = source != null; }
        if (nameText != null) nameText.text = (!string.IsNullOrWhiteSpace(name) ? name
            : speaker == PortraitSpeaker.Boss ? (!string.IsNullOrWhiteSpace(bossName) ? bossName : boss != null ? boss.TheNameCap : "")
            : model != null ? model.name : "").ToUpperInvariant();
        if (arrow != null) arrow.enabled = false;
        body.text = text ?? "";
        body.maxVisibleCharacters = 0;
        if (!shown) { yield return Fade(1f); shown = true; Fire(); }
        float pitch = speaker == PortraitSpeaker.Boss ? bossPitch : otherPitch;
        // A recorded voice takes the blips' place.
        var clip = voice != null ? voice.GetClip() : null;
        if (clip != null && AudioManager.HasInstance) { boxVoice = AudioManager.Instance.PlayUI(clip, voice.volume, voice.RandomPitch()); boxVoiceClip = clip; }
        yield return Type(pitch, clip == null);
        if (arrow != null) arrow.enabled = true;
    }

    // Letter by letter, held after punctuation; a press shows the rest at once.
    IEnumerator Type(float pitch, bool blipping = true)
    {
        body.ForceMeshUpdate();
        int total = body.textInfo.characterCount, since = 0;
        float wait = 0f;
        yield return null;   // the press that opened this box doesn't skip it
        for (int shown = 0; shown < total; )
        {
            if (Pressed()) { body.maxVisibleCharacters = total; yield return null; yield break; }
            wait -= Time.unscaledDeltaTime;
            while (wait <= 0f && shown < total)
            {
                char c = body.textInfo.characterInfo[shown].character;
                shown++;
                body.maxVisibleCharacters = shown;
                wait += 1f / charactersPerSecond;
                if (c == '.' || c == '!' || c == '?') wait += punctuationPause;
                else if (c == ',' || c == ';' || c == ':') wait += punctuationPause * .5f;
                if (blipping && !char.IsWhiteSpace(c) && ++since % blipEvery == 1) Blip(pitch);
            }
            yield return null;
        }
    }

    void Blip(float pitch)
    {
        if (blips == null || blips.Length == 0 || !AudioManager.HasInstance) return;
        var clip = blips[UnityEngine.Random.Range(0, blips.Length)];
        if (clip != null) AudioManager.Instance.PlayUI(clip, blipVolume, pitch * (1f + UnityEngine.Random.Range(-blipPitchVariance, blipPitchVariance)));
    }

    IEnumerator WaitForNext()
    {
        while (!Pressed()) yield return null;
        yield return null;
    }

    // The answers as buttons: a click takes one; the mouse moving over one, up and down (or Tab) light it (so after the keys
    // moved the light, a nudge of the mouse takes it back), and Submit takes the one lit.
    IEnumerator Choose(List<PortraitChoice> choices)
    {
        picked = -1; selected = 0;
        if (arrow != null) arrow.enabled = false;
        foreach (var b in choiceButtons) if (b != null) Destroy(b.gameObject);
        choiceButtons.Clear();
        for (int i = 0; i < choices.Count; i++)
        {
            var button = Instantiate(choiceTemplate, choiceColumn);
            button.gameObject.SetActive(true);
            button.GetComponentInChildren<TMP_Text>().text = choices[i]?.label ?? "";
            int index = i;
            button.onClick.AddListener(() => picked = index);
            choiceButtons.Add(button);
        }
        choiceColumn.gameObject.SetActive(true);
        Light(0);
        bool navigated = false;
        void Step() { navigated = true; }
        if (InputManager.HasInstance) InputManager.Instance.NavigatePressed += Step;
        // A box with an answer that walks away (Never mind) is a menu: Escape picks that answer instead of pausing.
        int leave = choices.FindIndex(c => c != null && c.leaves);
        if (leave >= 0 && InputManager.HasInstance)
        {
            leaveHook = () => { if (picked < 0) picked = leave; };
            InputManager.Instance.WantCancel(this, true);
            InputManager.Instance.CancelPressed += leaveHook;
        }
        yield return null;
        Vector2 pointer = InputManager.HasInstance ? InputManager.Instance.PointerPosition : Vector2.zero;
        while (picked < 0)
        {
            if (navigated && InputManager.HasInstance) { navigated = false; Light((selected + InputManager.Instance.NavigateStep + choiceButtons.Count) % choiceButtons.Count); }
            if (InputManager.HasInstance && InputManager.Instance.PointerPosition != pointer)
            {
                pointer = InputManager.Instance.PointerPosition;
                int over = Under(pointer);
                if (over >= 0 && over != selected) Light(over);
            }
            if (KeyPressed()) picked = selected;
            yield return null;
        }
        if (InputManager.HasInstance) InputManager.Instance.NavigatePressed -= Step;
        ReleaseLeave();
        choiceColumn.gameObject.SetActive(false);
        foreach (var b in choiceButtons) if (b != null) Destroy(b.gameObject);
        choiceButtons.Clear();
        if (UIFeedbackSettings.Shared != null) UIFeedbackSettings.Shared.Hover();
        yield return null;
    }

    // Escape's claim while answers with a leave answer are up (Choose); also given back when the box is aborted mid-choice.
    Action leaveHook;
    void ReleaseLeave()
    {
        if (leaveHook == null) return;
        if (InputManager.HasInstance) { InputManager.Instance.WantCancel(this, false); InputManager.Instance.CancelPressed -= leaveHook; }
        leaveHook = null;
    }

    void OnDisable() => ReleaseLeave();

    // The answer under a screen point, -1 for none.
    int Under(Vector2 point)
    {
        var canvas = choiceColumn.GetComponentInParent<Canvas>();
        var cam = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        for (int i = 0; i < choiceButtons.Count; i++)
            if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)choiceButtons[i].transform, point, cam)) return i;
        return -1;
    }

    void Light(int index)
    {
        selected = index;
        for (int i = 0; i < choiceButtons.Count; i++)
        {
            var label = choiceButtons[i].GetComponentInChildren<TMP_Text>();
            if (label == null) continue;
            string text = label.text.TrimStart('>', ' ');
            label.text = i == index ? "> " + text : text;
            label.color = i == index ? new Color(.95f, .8f, .42f) : new Color(.62f, .58f, .52f);
        }
        if (index >= 0 && index < choiceButtons.Count) choiceButtons[index].Select();
    }

    bool Pressed()
    {
        if (Barking) return false;
        if (testPress) { testPress = false; return true; }
        return InputManager.HasInstance && InputManager.Instance.SkipLinePressed;
    }
    // Space, Enter or a pad's South (a click lands on a button instead).
    bool KeyPressed() => InputManager.HasInstance && InputManager.Instance.SkipLinePressed && !InputManager.Instance.PointerClicked;

    IEnumerator Fade(float to)
    {
        if (group == null) yield break;
        if (to > 0f) gameObject.SetActive(true);
        float from = group.alpha;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(.01f, fadeSeconds))
        {
            group.alpha = Mathf.Lerp(from, to, t);
            yield return null;
        }
        group.alpha = to;
    }

    void Hide(bool instant)
    {
        StopBoxVoice();
        if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; }
        if (choiceColumn != null) choiceColumn.gameObject.SetActive(false);
        if (choiceTemplate != null) choiceTemplate.gameObject.SetActive(false);
    }

    // Test hooks (evals): a press as a click would be, or an answer taken.
    bool testPress;
    public void TestPress() => testPress = true;
    public void TestPick(int index) => picked = index;
    public string TestState => Open ? $"{nameText.text}: {body.text} ({body.maxVisibleCharacters}/{body.textInfo.characterCount}){(choiceColumn.gameObject.activeSelf ? " [choices]" : "")}" : "closed";

    void LateUpdate()
    {
        if (group != null) group.blocksRaycasts = Open && choiceColumn != null && choiceColumn.gameObject.activeSelf;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A dialogue box at the foot of the screen with the speaker's head and shoulders idling at its left, their name on a
// plate above it and the line typed out with a soft blip a letter (an alternative to the Dialogue System's
// conversations, for the boss and the dungeon's people: BossFight > Intro). The Dungeon Master never speaks in it:
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
    [Header("Portrait")]
    [Tooltip("Pixels across the speaker's portrait (drawn point-filtered, so fewer is chunkier).")] public int portraitResolution = 256;
    [Tooltip("Degrees the speaker is turned from facing the camera.")] public float portraitTurn = 22f;
    [Tooltip("How much body shows below the head, in head-to-chest lengths (more: a smaller figure).")] public float portraitBodyBelow = 1.9f;
    [Tooltip("Room above the head, in head-to-chest lengths (horns and helmets get their own room on top).")] public float portraitRoomAbove = .55f;

    PortraitStage stage;
    readonly List<Button> choiceButtons = new();
    int picked = -1, selected;

    public bool Open { get; private set; }
    // While it is up the hotbar and vitals step aside (HudCanvas.BehindMenu).
    public static bool Covers => Current != null && Current.Open;

    void Awake() { Current = this; Hide(true); }
    void OnDestroy() { if (Current == this) Current = null; }

    // Plays the lines. The boss is the fight's boss (its look and, unless a line names someone, its name); firstShown
    // runs as the first box appears. Push the Dialogue state around it (BossStageEncounter does).
    public IEnumerator Play(IReadOnlyList<PortraitLine> lines, Character boss, string bossName, Action firstShown = null)
    {
        if (lines == null || lines.Count == 0) yield break;
        Open = true;
        stage ??= new GameObject("Portrait stage").AddComponent<PortraitStage>();
        stage.resolution = portraitResolution; stage.turn = portraitTurn; stage.bodyBelow = portraitBodyBelow; stage.roomAbove = portraitRoomAbove;
        if (portrait != null) portrait.texture = null;
        shown = false; this.firstShown = firstShown;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line == null) continue;
            bool asks = line.choices != null && line.choices.Count > 0;
            yield return Line(line.speaker, line.name, line.model, line.text, boss, bossName, !asks);
            if (!asks) continue;
            yield return Choose(line.choices);
            var choice = picked >= 0 && picked < line.choices.Count ? line.choices[picked] : null;
            if (choice?.reply == null) continue;
            foreach (var reply in choice.reply)
                if (reply != null) yield return Line(reply.speaker, reply.name, reply.model, reply.text, boss, bossName, true);
        }
        if (shown) yield return Fade(0f);
        Hide(false);
        stage.Clear();
        Open = false;
    }

    bool shown;
    Action firstShown;

    // One line: the Dungeon Master's in his own feed as always (the box steps aside while he
    // speaks), anyone else's in the box. Wait: hold until it is read and pressed on (a line with answers doesn't).
    IEnumerator Line(PortraitSpeaker speaker, string name, GameObject model, string text, Character boss, string bossName, bool wait)
    {
        if (speaker == PortraitSpeaker.DungeonMaster)
        {
            if (shown) { yield return Fade(0f); shown = false; }
            Fire();
            DungeonMaster.Say(new DungeonMaster.Line { text = text, mumble = true, inPerson = true });
            yield return null;
            while (DungeonMaster.HasInstance && DungeonMaster.Instance.Speaking) yield return null;
            yield break;
        }
        yield return Box(speaker, name, model, text, boss, bossName);
        if (wait) yield return WaitForNext();
    }

    void Fire() { firstShown?.Invoke(); firstShown = null; }

    IEnumerator Box(PortraitSpeaker speaker, string name, GameObject model, string text, Character boss, string bossName)
    {
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
        yield return Type(pitch);
        if (arrow != null) arrow.enabled = true;
    }

    // Letter by letter, held after punctuation; a press shows the rest at once.
    IEnumerator Type(float pitch)
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
                if (!char.IsWhiteSpace(c) && ++since % blipEvery == 1) Blip(pitch);
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

    // The answers as buttons: a click takes one; up and down (or Tab) move, and Submit takes the one lit.
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
        yield return null;
        while (picked < 0)
        {
            if (navigated && InputManager.HasInstance) { navigated = false; Light((selected + InputManager.Instance.NavigateStep + choiceButtons.Count) % choiceButtons.Count); }
            if (KeyPressed()) picked = selected;
            yield return null;
        }
        if (InputManager.HasInstance) InputManager.Instance.NavigatePressed -= Step;
        choiceColumn.gameObject.SetActive(false);
        foreach (var b in choiceButtons) if (b != null) Destroy(b.gameObject);
        choiceButtons.Clear();
        if (UIFeedbackSettings.Shared != null) UIFeedbackSettings.Shared.Hover();
        yield return null;
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

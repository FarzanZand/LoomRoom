using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

// A house rule given for the first time (the user, 2026-11-13: "Say 'New rule added' and below it in smaller text: type
// out the rule."; then, of a big notice at the top like the level up's: "it was a bit too much. Make the new rule added
// more discreet, just place it at the same place the DM text is"; and "no audio"). A small, silent notice where the
// Dungeon Master's lines are, in their own font: "NEW RULE ADDED" comes in, and under it, smaller, the rule is typed out
// letter by letter at his typing pace (a pause after a full stop, half of it after a comma). It stays while it is read,
// then fades. No rule number, no sound. It stands on his lines' foot, or on top of whatever he is saying just now, and
// is as wide as his lines are (TikTok mode's narrow column included), so it never covers them. When the line he is
// saying is the rule itself (his lines for giving a rule state it, typed out), the notice does not say it twice: only
// its title shows, on his line; it types the rule itself when he isn't saying it.
// RuleManager.Give shows it (NewRuleUI.Show) the moment a rule is first given; a rule given while one shows waits its turn.
// The prefab (Rules/Resources/New rule, made by Tools/new_rule_popup.eval.cs) has its own canvas, as his lines do: the
// HUD's is switched off in cutscenes, and the intro gives rule one in one.
public class NewRuleUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField, Tooltip("Holds the title and the rule; its foot stands on the Dungeon Master's lines.")] RectTransform notice;
    [SerializeField] TMP_Text title;
    [SerializeField, Tooltip("The rule's text, typed out under the title.")] TMP_Text ruleText;
    [SerializeField, Min(0f), Tooltip("Gap between the Dungeon Master's text and the notice, and between the rule and its title (canvas units).")] float gap = 10f, titleGap = 4f;
    [SerializeField, Min(0f), Tooltip("Where its foot stands when his lines can't be found (canvas units up from the screen's foot: his lines' own 250).")] float restingFoot = 250f;
    [SerializeField, Min(0f), Tooltip("From the title coming in to the first letter.")] float textDelay = .35f;
    [SerializeField, Min(1f), Tooltip("Letters a second (the Dungeon Master's typewriter: 40).")] float charactersPerSecond = 40f;
    [SerializeField, Min(0f), Tooltip("Pause after . ! ? (half of it after , ; :).")] float punctuationPause = .25f;
    [SerializeField, Min(0f), Tooltip("Once typed out it stays this long, plus Read Per Word for each of its words.")] float readBase = 1.2f;
    [SerializeField, Min(0f)] float readPerWord = .3f;
    [SerializeField, Min(0f)] float leastHold = 2.2f;
    [SerializeField, Min(.05f)] float fade = .8f;

    static NewRuleUI instance;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => instance = null;

    readonly Queue<string> waiting = new();
    readonly List<float> typedAt = new();   // when each letter shows, in seconds from the first
    Sequence show;
    string rule = "";   // the rule being shown
    bool saidByHim;       // he is saying it in his own line: the notice leaves its text out

    public static bool Showing => instance != null && (instance.show != null && instance.show.IsActive() || instance.waiting.Count > 0);

    // A rule was just given: its notice, after any that is showing.
    public static void Show(HouseRule rule)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.text) || !Application.isPlaying) return;
        if (instance == null)
        {
            var prefab = Resources.Load<NewRuleUI>("New rule");
            if (prefab == null) return;
            instance = Instantiate(prefab);
        }
        instance.waiting.Enqueue(rule.text);
    }

    void Awake() { if (group != null) group.alpha = 0f; }

    void OnDestroy()
    {
        show?.Kill(); show = null;
        if (instance == this) instance = null;
    }

    void Update()
    {
        if (waiting.Count > 0 && (show == null || !show.IsActive())) Play(waiting.Dequeue());
    }

    // After his lines have laid themselves out for the frame: it stands on them.
    void LateUpdate() { if (show != null && show.IsActive()) Place(); }

    // On the Dungeon Master's lines: its foot on the top of what he is saying (on his lines' own foot when he says
    // nothing), as wide as his lines.
    void Place()
    {
        if (notice == null || ruleText == null) return;
        float scale = Mathf.Max(transform.lossyScale.y, .0001f);
        if (DungeonMaster.FeedTop(out var top, out float width, out string saying))
        {
            notice.position = top + Vector3.up * gap * scale;
            // As wide as his lines at most; a rule on two lines is split evenly, not left with one word on the second.
            float most = width / scale, one = ruleText.GetPreferredValues(rule, 10000f, 0f).x;
            int rows = Mathf.Max(1, Mathf.CeilToInt(one / most));
            ruleText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rows == 1 ? most : Mathf.Min(most, one / rows * 1.12f));
            if (!saidByHim && rule.Length > 0 && saying.Contains(rule)) saidByHim = true;
        }
        else notice.anchoredPosition = new Vector2(0f, restingFoot);
        ruleText.alpha = saidByHim ? 0f : 1f;
        // The title sits on the rule's text, however many lines that takes; on his line when he says the rule himself.
        if (title != null) title.rectTransform.anchoredPosition = new Vector2(0f, saidByHim ? 0f : ruleText.GetPreferredValues(rule, ruleText.rectTransform.rect.width, 0f).y + titleGap);
    }

    void Play(string text)
    {
        if (group == null || title == null || ruleText == null) return;
        rule = text; saidByHim = false;
        ruleText.text = text;
        ruleText.alpha = 1f;
        ruleText.maxVisibleCharacters = 99999;
        Place();
        ruleText.ForceMeshUpdate();
        Place();
        var info = ruleText.textInfo;
        int count = info.characterCount;
        // When each letter is typed: the pace, and a pause after punctuation.
        typedAt.Clear();
        float at = 0f;
        for (int i = 0; i < count; i++)
        {
            typedAt.Add(at);
            char c = info.characterInfo[i].character;
            at += 1f / charactersPerSecond;
            if (c == '.' || c == '!' || c == '?') at += punctuationPause;
            else if (c == ',' || c == ';' || c == ':') at += punctuationPause * .5f;
        }
        float typing = count > 0 ? typedAt[count - 1] : 0f;
        ruleText.maxVisibleCharacters = 0;

        group.alpha = 1f;
        var titleRect = title.rectTransform;
        title.alpha = 0f; titleRect.localScale = Vector3.one * 1.15f;

        int words = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
        float hold = Mathf.Max(leastHold, readBase + readPerWord * words);
        show = DOTween.Sequence().SetUpdate(true);
        show.Append(title.DOFade(1f, .2f)).Join(titleRect.DOScale(1f, .25f).SetEase(Ease.OutQuad));
        if (typing > 0f) show.Insert(textDelay, DOTween.To(() => 0f, Type, typing, typing).SetEase(Ease.Linear));
        show.InsertCallback(textDelay + typing, () => ruleText.maxVisibleCharacters = count);
        show.Insert(textDelay + typing + hold, group.DOFade(0f, fade));
        show.OnComplete(() => { show = null; group.alpha = 0f; });
    }

    // Every letter whose time has come is shown.
    void Type(float seconds)
    {
        int shown = 0;
        while (shown < typedAt.Count && typedAt[shown] <= seconds) shown++;
        ruleText.maxVisibleCharacters = shown;
    }
}

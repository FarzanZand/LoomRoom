using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

// A boss's lines in the middle of its fight (PortraitDialogueUI.Bark), said without the dialogue box so nothing covers
// the fight: UIManager > Boss Bark Style picks where. Under Boss Bar: the line under the boss's health bar at the top of
// the screen. Over Head: over the boss's head, following it, held inside the screen's edge when it is out of view.
// Subtitle: in the lower middle like a subtitle, the boss's name in front. Typed out, held while it is read, then faded;
// a line said while another shows waits for it. Built on the boss bar's canvas by BossBarkSetup.Build().
public class BossBarkUI : Singleton<BossBarkUI>
{
    [SerializeField] CanvasGroup underBar, overHead, subtitle;
    [SerializeField] TMP_Text underBarText, overHeadText, subtitleText;
    [SerializeField] Color nameColour = new(1f, .55f, .35f);
    [SerializeField, Min(1f)] float charactersPerSecond = 40f;
    [SerializeField, Min(0f)] float hold = 1.4f, perLetter = .03f;
    [SerializeField, Min(.05f)] float fadeIn = .15f, fadeOut = .4f;
    [Tooltip("Metres over the speaker's head the Over Head line floats.")]
    [SerializeField] float overHeadLift = .5f;
    [Tooltip("Canvas units kept clear of the screen's edge by the Over Head line.")]
    [SerializeField] float edgeMargin = 140f;

    readonly Queue<(string text, Character speaker, string name)> waiting = new();
    Character speaker;
    BossBarkStyle style;
    public bool Saying { get; private set; }
    float subtitleWidth = -1f, subtitleBottom, subtitleTop;

    // The foot and top of the Subtitle line on its 1920 x 1080 canvas while it shows (top 0 when it doesn't): the
    // Dungeon Master's lines rise above it when they would run into it (a wrapped line, TikTok mode).
    public static float SubtitleBottom => HasInstance ? Instance.subtitleBottom : 0f;
    public static float SubtitleTop => HasInstance && Instance.Saying ? Instance.subtitleTop : 0f;

    protected override void Awake()
    {
        base.Awake();
        foreach (var g in new[] { underBar, overHead, subtitle }) if (g != null) g.alpha = 0f;
    }

    public void Say(string text, Character who, string name, BossBarkStyle how)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Saying) { waiting.Enqueue((text, who, name)); return; }
        StartCoroutine(Run(text, who, name, how));
    }

    IEnumerator Run(string text, Character who, string name, BossBarkStyle how)
    {
        Saying = true;
        style = how;
        while (true)
        {
            speaker = who;
            var (group, label) = Parts(style);
            if (group == null || label == null) break;
            bool named = style == BossBarkStyle.Subtitle && !string.IsNullOrWhiteSpace(name);
            if (style == BossBarkStyle.Subtitle) FitSubtitle();
            label.text = named ? $"<color=#{ColorUtility.ToHtmlStringRGB(nameColour)}>{name}:</color> {text}" : text;
            label.ForceMeshUpdate();
            if (style == BossBarkStyle.Subtitle)
            {
                subtitleBottom = ((RectTransform)label.transform).anchoredPosition.y;
                subtitleTop = subtitleBottom + label.preferredHeight;
            }
            int total = label.textInfo.characterCount, from = named ? name.Length + 2 : 0;
            label.maxVisibleCharacters = from;
            Follow();
            for (float a = group.alpha; a < 1f; a += Time.unscaledDeltaTime / fadeIn) { group.alpha = a; Follow(); yield return null; }
            group.alpha = 1f;
            for (float shown = from; shown < total; shown += charactersPerSecond * Time.unscaledDeltaTime)
            {
                label.maxVisibleCharacters = Mathf.FloorToInt(shown);
                Follow();
                yield return null;
            }
            label.maxVisibleCharacters = total;
            for (float t = 0f, read = hold + text.Length * perLetter; t < read; t += Time.unscaledDeltaTime) { Follow(); yield return null; }
            if (waiting.Count > 0) { (text, who, name) = waiting.Dequeue(); continue; }
            for (float a = group.alpha; a > 0f; a -= Time.unscaledDeltaTime / fadeOut) { group.alpha = a; Follow(); yield return null; }
            group.alpha = 0f;
            break;
        }
        speaker = null;
        subtitleTop = 0f;
        Saying = false;
    }

    // The Subtitle line wraps inside its width (in TikTok mode the Dungeon Master's narrow width, so it stays inside a
    // 9:16 crop), its first line at its foot and the rest above: a long line ran off both sides of the screen.
    void FitSubtitle()
    {
        var r = (RectTransform)subtitleText.transform;
        if (subtitleWidth < 0f) subtitleWidth = r.sizeDelta.x;
        r.sizeDelta = new Vector2(DungeonMaster.TikTokMode ? DungeonMaster.TikTokWidth : subtitleWidth, r.sizeDelta.y);
        subtitleText.textWrappingMode = TextWrappingModes.Normal;
        subtitleText.verticalAlignment = VerticalAlignmentOptions.Bottom;
    }

    (CanvasGroup, TMP_Text) Parts(BossBarkStyle how) => how switch
    {
        BossBarkStyle.UnderBossBar => (underBar, underBarText),
        BossBarkStyle.OverHead => (overHead, overHeadText),
        _ => (subtitle, subtitleText),
    };

    // The Over Head line sits over the speaker's head on screen, held inside the edge when the speaker is off it (behind
    // the view it goes to the edge on the speaker's side).
    void Follow()
    {
        if (style != BossBarkStyle.OverHead || overHead == null || speaker == null) return;
        var cam = PlayerManager.HasInstance ? PlayerManager.Instance.OutputCamera : Camera.main;
        var canvas = (RectTransform)overHead.transform.parent;
        if (cam == null || canvas == null) return;
        var col = speaker.GetComponent<Collider>();
        var top = col != null ? new Vector3(col.bounds.center.x, col.bounds.max.y + overHeadLift, col.bounds.center.z)
                              : speaker.transform.position + Vector3.up * (2f + overHeadLift);
        var screen = cam.WorldToScreenPoint(top);
        if (screen.z < 0f) { screen.x = Screen.width - screen.x; screen.y = 0f; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out var local);
        var half = canvas.rect.size * .5f - Vector2.one * edgeMargin;
        local.x = Mathf.Clamp(local.x, -half.x, half.x);
        local.y = Mathf.Clamp(local.y, -half.y, half.y);
        ((RectTransform)overHead.transform).anchoredPosition = local;
    }
}

// Where a boss's mid-fight lines show (UIManager.bossBarkStyle). Serialized by integer; append only.
public enum BossBarkStyle { Box = 0, UnderBossBar = 1, OverHead = 2, Subtitle = 3 }

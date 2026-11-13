using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The character sheet on the hobby table: your name, a portrait picked from the unlocked classes' pictures, and the
// starting levels of the skills and attributes, bought with spoils (ProgressionManager). Authored in the scene by
// Tools > LoomRoom > Meta > Build Character Sheet; this only binds and fills it. Putting it down hands over the trophies
// earned since it was opened.
public class CharacterSheetUI : Singleton<CharacterSheetUI>
{
    [Serializable]
    public class Row
    {
        public string trackId;
        public Image icon;
        public TMP_Text title, value, unit;
        public Image[] bubbles = new Image[0];
        [Tooltip("The unlock icon or \"?\" inside each milestone bubble (same order as the track's milestones).")]
        public Image[] milestoneIcons = new Image[0];
        public TMP_Text[] milestoneMarks = new TMP_Text[0];
        public Button buy;
        public TMP_Text buyLabel;
        public Image buyIcon;
        [System.NonSerialized] public float buyLabelSize, buyLabelSpacing;   // the label's own size and line spacing, kept while the lesson text auto-sizes
    }

    [SerializeField] GameObject root;
    [SerializeField] Image portrait;
    [SerializeField] Button previousPortrait, nextPortrait;
    [SerializeField] TMP_Text portraitLabel;
    [SerializeField] TMP_InputField nameField;
    [SerializeField] TMP_Text spoilsText;
    [SerializeField] Button refundButton, closeButton;
    [SerializeField] TMP_Text feedback;
    [SerializeField, Tooltip("The banner across the sheet when a milestone is bought (\"Unlocked: Shield bash\").")] RectTransform unlockBanner;
    [SerializeField] TMP_Text unlockTitle, unlockSub;
    [SerializeField] List<Row> rows = new();
    [Header("Hover card")]
    [SerializeField] RectTransform tip;
    [SerializeField] TMP_Text tipTitle, tipBody;
    [Header("Look")]
    [SerializeField] Sprite bubbleFilled, bubbleOutline, bubbleDashed, portraitPlaceholder;
    [SerializeField] Color ink = new(.17f, .15f, .14f), pen = new(.58f, .16f, .12f), faint = new(.47f, .45f, .42f), poor = new(.6f, .58f, .55f);
    [SerializeField, Min(.5f)] float feedbackSeconds = 3f;
    [Header("Quest")]
    [SerializeField, Tooltip("While the spend-spoils quest is open: its line on the paper (the HUD's quest note hides in menus).")]
    TMP_Text questNote;
    [SerializeField, Tooltip("A pen ring drawn round the Train button that quest asks for; it pulses.")]
    Image questRing;
    [SerializeField, Min(0)] Vector2 questRingPadding = new(26, 18);
    [Header("First lesson")]
    [SerializeField, Tooltip("The tutorial window shown while the first lesson holds the sheet (the lesson skill must be trained first): it sits by that skill's Train button and its tail points at it.")]
    RectTransform lessonTip;
    [SerializeField] TMP_Text lessonTitle, lessonBody;
    [SerializeField, Tooltip("The window's pointer; it is put on the window's edge nearest the Train button.")] RectTransform lessonTail;
    [SerializeField, Tooltip("A marker stroke behind the lesson skill's row; it breathes.")] Image lessonHighlight;
    [SerializeField, Tooltip("Washes over the rest of the sheet while the lesson is on, so its row stands out: the skill rows above it, those below it, and the attributes. Each is a rect mask whose first child is a half clear copy of the paper.")]
    RectTransform veilAbove, veilBelow, veilSide;
    [SerializeField, Tooltip("The window's title. {skill} is the lesson skill's name, {price} its next price.")] string lessonTitleText = "Train {skill} first";
    [SerializeField, TextArea, Tooltip("The window's text. {skill} is the lesson skill's name, {price} its next price.")]
    string lessonBodyText = "It costs {price} spoils and teaches Evade.\nThe rest of the sheet opens after.";
    [SerializeField, Min(0), Tooltip("Half the height of a skill row (the washes stop this far above and below the lesson's row).")] float lessonRowHalfHeight = 43f;
    [SerializeField, Tooltip("The lowest and highest the washes over the skill rows reach, in the paper's own units.")] Vector2 veilSpan = new(-365, 335);
    [SerializeField, Min(0), Tooltip("How far the window bobs toward the button and back.")] float lessonBob = 4f;

    bool open;
    float feedbackUntil;
    Sequence bannerSequence;
    public bool IsOpen => open;
    static ProgressionManager Meta => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;

    protected override void Awake()
    {
        base.Awake();
        if (root != null) root.SetActive(false);
        closeButton?.onClick.AddListener(Close);
        refundButton?.onClick.AddListener(Refund);
        previousPortrait?.onClick.AddListener(() => StepPortrait(-1));
        nextPortrait?.onClick.AddListener(() => StepPortrait(1));
        nameField?.onEndEdit.AddListener(SaveName);
        foreach (var row in rows)
        {
            var r = row;
            r.buy?.onClick.AddListener(() => Buy(r));
            Navigable(r.buy);
            foreach (var hover in HoverTargets(r)) AddHover(hover, r);
            for (int m = 0; m < r.milestoneIcons.Length; m++) if (r.milestoneIcons[m] != null) AddHover(r.milestoneIcons[m].transform.parent.gameObject, r, m);
            // The next bubble buys too.
            for (int i = 0; i < r.bubbles.Length; i++)
            {
                int index = i;
                if (r.bubbles[i] == null) continue;
                var trigger = r.bubbles[i].gameObject.GetComponent<EventTrigger>() ?? r.bubbles[i].gameObject.AddComponent<EventTrigger>();
                var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
                click.callback.AddListener(_ => { var t = Track(r); if (t != null && Meta != null && index == Meta.Levels(t)) Buy(r); });
                trigger.triggers.Add(click);
            }
        }
        if (tip != null) tip.gameObject.SetActive(false);
        Navigable(refundButton); Navigable(closeButton);
    }

    // A pad or the arrow keys move between the rows' buttons: a button authored with no navigation gets Unity's own.
    static void Navigable(Selectable s)
    {
        if (s != null && s.navigation.mode == Navigation.Mode.None) s.navigation = new Navigation { mode = Navigation.Mode.Automatic };
    }

    // Something is selected as the sheet opens, so Submit works on a pad: the Train button the quest asks for, else
    // the first row's that can be pressed, else Close. The mouse works as before.
    void SelectStart()
    {
        Selectable pick = questRow != null && questRow.buy != null && questRow.buy.IsInteractable() ? questRow.buy : null;
        if (pick == null)
            foreach (var r in rows)
                if (r.buy != null && r.buy.gameObject.activeInHierarchy && r.buy.IsInteractable()) { pick = r.buy; break; }
        if (pick == null) pick = closeButton;
        if (pick != null) pick.Select();
    }

    void Start() { if (InputManager.HasInstance) InputManager.Instance.CancelPressed += OnCancel; }
    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (InputManager.HasInstance) InputManager.Instance.CancelPressed -= OnCancel;
        if (Meta != null) Meta.MetaChanged -= Refresh;
    }
    void OnDisable() => Close();
    void OnCancel() { if (open && (nameField == null || !nameField.isFocused)) Close(); }

    IEnumerable<GameObject> HoverTargets(Row r)
    {
        if (r.icon != null) yield return r.icon.gameObject;
        if (r.title != null) yield return r.title.gameObject;
    }
    // A milestone bubble tells only about its own unlock; the row's icon and name tell what the track does.
    void AddHover(GameObject target, Row r, int milestone = -1)
    {
        var trigger = target.GetComponent<EventTrigger>() ?? target.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => { if (milestone >= 0) ShowMilestoneTip(r, milestone, target.transform as RectTransform); else ShowTip(r, target.transform as RectTransform); });
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => HideTip());
        trigger.triggers.Add(enter); trigger.triggers.Add(exit);
        if (target.GetComponent<Graphic>() is Graphic g) g.raycastTarget = true;
    }

    ProgressionManager.MetaTrack Track(Row r) => Meta != null ? Meta.Track(r.trackId) : null;

    public void Open()
    {
        if (root == null || open) return;
        if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        open = true;
        root.SetActive(true);
        if (Meta != null) Meta.MetaChanged += Refresh;
        if (nameField != null) nameField.SetTextWithoutNotify(Save != null ? Save.Saved.characterName : "");
        ShowFeedback("");
        HideTip();
        if (unlockBanner != null) unlockBanner.gameObject.SetActive(false);
        if (lessonTip != null) lessonTip.gameObject.SetActive(false);   // so the lesson's window pops in on every opening
        Refresh();
        // With a pad, at once; the arrow keys select on their first press (Update), and the mouse selects nothing, so
        // Space typed later never presses a Train button.
        if (InputManager.PadConnected) SelectStart();
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (nameField != null && nameField.isFocused) SaveName(nameField.text);
        if (Meta != null) Meta.MetaChanged -= Refresh;
        HideTip();
        // A button left selected would stay selected while hidden.
        var events = EventSystem.current;
        if (events != null && root != null && events.currentSelectedGameObject != null && events.currentSelectedGameObject.transform.IsChildOf(root.transform)
            && !events.alreadySelecting) events.SetSelectedGameObject(null);
        if (root != null) root.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        // Put down: the DM hands over what was earned.
        Meta?.HandOverTrophies();
    }

    void Update()
    {
        // A mouse click on nothing clears the selection: a pad's or an arrow key's next press selects again.
        if (open && InputManager.MenuInputStarted && InputManager.NothingSelected) SelectStart();
        if (open && feedback != null && feedback.text.Length > 0 && Time.unscaledTime > feedbackUntil) feedback.text = "";
        // The quest's ring breathes so the eye finds it.
        if (open && questRing != null && questRing.gameObject.activeSelf)
        {
            float k = .5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f);
            var c = pen; c.a = Mathf.Lerp(.55f, 1f, k); questRing.color = c;
            questRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.06f, k);
        }
        // The lesson's window bobs toward the button it points at, in step with the ring, and the marker breathes.
        if (open && questRow != null)
        {
            float k = .5f + .5f * Mathf.Sin(Time.unscaledTime * 3.2f);
            if (lessonTip != null && lessonTip.gameObject.activeSelf) lessonTip.anchoredPosition = lessonTipAt - lessonTipAway * (lessonBob * (1f - k));
            if (lessonHighlight != null) { var c = lessonHighlightColour; c.a *= Mathf.Lerp(.72f, 1f, k); lessonHighlight.color = c; }
        }
    }

    void ShowFeedback(string text) { if (feedback == null) return; feedback.text = text; feedbackUntil = Time.unscaledTime + feedbackSeconds; }

    static AdventureSave Save
    {
        get
        {
            var p = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
            return p != null ? p.GetComponent<AdventureSave>() : null;
        }
    }

    void SaveName(string text)
    {
        var save = Save;
        if (save == null) return;
        text = AdventureSave.CleanName(text);
        // The save is known by this name: a cleared field keeps the old one.
        if (text.Length == 0) { nameField?.SetTextWithoutNotify(save.Saved.characterName); return; }
        if (save.Saved.characterName == text) return;
        save.Saved.characterName = text;
        save.Write();
    }

    // The unlocked classes, in the rules' order: their pictures are the portraits on offer.
    static List<AdventurerClass> Portraits()
    {
        var list = new List<AdventurerClass>();
        var p = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var rules = p != null ? p.GetComponent<AdventurerProgress>()?.rules : null;
        if (rules != null && rules.classes != null) foreach (var c in rules.classes) if (c != null && c.Unlocked) list.Add(c);
        return list;
    }

    void StepPortrait(int step)
    {
        var list = Portraits();
        if (Meta == null || list.Count == 0) return;
        Meta.PortraitIndex = ((Meta.PortraitIndex + step) % list.Count + list.Count) % list.Count;
        Refresh();
    }

    void Buy(Row r)
    {
        var meta = Meta; var t = Track(r);
        if (meta == null || t == null) return;
        int price = meta.NextPrice(t);
        var before = new List<ProgressionManager.Milestone>();
        if (t.milestones != null) foreach (var m in t.milestones) if (meta.Reached(t, m)) before.Add(m);
        switch (meta.Buy(t))
        {
            case ProgressionManager.BuyResult.Locked: DungeonMaster.Say(meta.lessonSkillFirst); ShowFeedback($"\"{meta.lessonSkillFirst.text}\""); Shake(r); break;
            case ProgressionManager.BuyResult.Poor: ShowFeedback($"Not enough spoils. {t.displayName} needs {price}."); Shake(r); break;
            case ProgressionManager.BuyResult.Max: break;
            case ProgressionManager.BuyResult.Bought:
                PlayUi("shopTrade", "skillUp");
                if (t.milestones != null)
                    foreach (var m in t.milestones)
                        if (meta.Reached(t, m) && !before.Contains(m))
                        {
                            // What it does, not its trophy ("A trophy for the wall" under "Unlocked: Dash" read as if the
                            // dash were the trophy; the user, 2026-11-12).
                            ShowUnlock(m.Known ? $"Unlocked: {m.Name}" : $"{t.displayName} {m.value}", m.Known ? m.Description ?? "" : "");
                            if (AudioManager.HasInstance) AudioManager.Instance.PlayLevelUp();
                        }
                break;
        }
        Refresh();
    }

    void ShowUnlock(string title, string sub)
    {
        if (unlockBanner == null) return;
        if (unlockTitle != null) unlockTitle.text = title;
        if (unlockSub != null) unlockSub.text = sub;
        FitBanner();
        unlockBanner.gameObject.SetActive(true);
        // The hover card under the resting cursor would lie over it: it goes while the banner shows.
        HideTip();
        // A banner shown while another is up starts over: the old sequence would otherwise hide the new one early.
        bannerSequence?.Kill();
        unlockBanner.DOKill(true);
        unlockBanner.localScale = Vector3.one * .6f;
        bannerSequence = DOTween.Sequence().SetUpdate(true).SetLink(unlockBanner.gameObject)
            .Append(unlockBanner.DOScale(1f, .22f).SetEase(Ease.OutBack))
            .AppendInterval(3.2f)
            .AppendCallback(() => unlockBanner.gameObject.SetActive(false));
    }

    // The line under the title says what the unlock does (the user, 2026-11-12), which can be longer than the banner is
    // wide: it wraps inside the banner, and the banner grows downward and upward by the extra lines, title kept at its top.
    Vector2? bannerSize, fillSize, titlePos, subPos, subSize;
    void FitBanner()
    {
        if (unlockSub == null) return;
        var subRect = unlockSub.rectTransform;
        var fill = unlockBanner.Find("Fill") as RectTransform;
        bannerSize ??= unlockBanner.sizeDelta; subSize ??= subRect.sizeDelta; subPos ??= subRect.anchoredPosition;
        if (fill != null) fillSize ??= fill.sizeDelta;
        if (unlockTitle != null) titlePos ??= unlockTitle.rectTransform.anchoredPosition;
        unlockSub.textWrappingMode = TMPro.TextWrappingModes.Normal;
        float lineHeight = subSize.Value.y;
        float height = string.IsNullOrEmpty(unlockSub.text) ? lineHeight : Mathf.Max(lineHeight, Mathf.Ceil(unlockSub.GetPreferredValues(unlockSub.text, subSize.Value.x, 0f).y));
        float extra = height - lineHeight;
        unlockBanner.sizeDelta = bannerSize.Value + new Vector2(0f, extra);
        if (fill != null) fill.sizeDelta = fillSize.Value + new Vector2(0f, extra);
        if (unlockTitle != null) unlockTitle.rectTransform.anchoredPosition = titlePos.Value + new Vector2(0f, extra * .5f);
        subRect.sizeDelta = new Vector2(subSize.Value.x, height);
        // Its middle stays put: its top moves up with the banner's top, its bottom down with the banner's bottom.
        subRect.anchoredPosition = subPos.Value;
    }

    void Refund()
    {
        var meta = Meta;
        if (meta == null) return;
        int back = meta.RefundAll();
        ShowFeedback(back > 0 ? $"All back: {back} spoils. The trophies come down." : "Nothing to give back.");
        Refresh();
    }

    static void PlayUi(params string[] keys)
    {
        if (!AudioManager.HasInstance) return;
        foreach (var k in keys) if (AudioManager.Instance.HasUI(k)) { AudioManager.Instance.PlayUI(k); return; }
    }

    void Shake(Row r)
    {
        var target = r.buy != null ? r.buy.transform : null;
        if (target == null) return;
        target.DOKill(true);
        target.DOPunchPosition(new Vector3(8, 0, 0), .3f, 12).SetUpdate(true);
    }

    public void Refresh()
    {
        var meta = Meta;
        if (meta == null || !open) return;
        if (spoilsText != null) spoilsText.text = meta.Spoils.ToString();
        var portraits = Portraits();
        var cls = portraits.Count > 0 ? portraits[Mathf.Clamp(meta.PortraitIndex, 0, portraits.Count - 1)] : null;
        var picture = meta.SheetPortrait(cls);
        if (portrait != null) { portrait.sprite = picture != null ? picture : portraitPlaceholder; portrait.preserveAspect = true; }
        if (portraitLabel != null) portraitLabel.text = portraits.Count > 0 ? $"Portrait {Mathf.Clamp(meta.PortraitIndex, 0, portraits.Count - 1) + 1} of {portraits.Count}" : "";
        foreach (var r in rows) RefreshRow(meta, r);
        RefreshQuest(meta);
    }

    // The first lesson (ProgressionManager.LessonHoldsRuns: the lesson skill must be trained before anything else): its
    // quest's line at the foot of the paper, the lesson skill's row name in pen, a pen ring round its Train button, a
    // marker stroke behind its row, the rest of the sheet washed pale and a tutorial window pointing at the button,
    // until the skill's first milestone is bought.
    Row questRow;
    Vector2 lessonTipAt, lessonTipAway;
    Color lessonHighlightColour;
    bool lessonHighlightRead;
    void RefreshQuest(ProgressionManager meta)
    {
        var lesson = meta.Track(meta.lessonSkill);
        // By the lesson itself, not by its quest: the quest is only given the frame after the sheet is first opened,
        // so on that first look (the one that matters) nothing on the page pointed at the skill.
        bool open = meta.LessonHoldsRuns;
        questRow = open && lesson != null ? rows.Find(r => r.trackId == lesson.id) : null;
        RefreshLesson(meta, lesson);
        if (questNote != null)
        {
            questNote.gameObject.SetActive(questRow != null);
            if (questRow != null) questNote.text = "Quest: " + meta.QuestText(meta.FindQuest(ProgressionManager.SpendQuest));
        }
        foreach (var r in rows) if (r.title != null && Track(r)?.kind == MetaTrackKind.Skill) r.title.color = r == questRow ? pen : ink;
        if (questRing == null) return;
        questRing.gameObject.SetActive(questRow != null && questRow.buy != null);
        if (questRow == null || questRow.buy == null) return;
        var target = (RectTransform)questRow.buy.transform;
        var ring = questRing.rectTransform;
        ring.position = target.TransformPoint(target.rect.center);
        ring.sizeDelta = target.rect.size + questRingPadding;
        questRing.color = pen;
        questRing.raycastTarget = false;
    }

    // The lesson's highlight, washes and tutorial window, laid out against the lesson skill's row wherever it is on the
    // page (everything here is a child of the paper, anchored to its middle).
    void RefreshLesson(ProgressionManager meta, ProgressionManager.MetaTrack lesson)
    {
        bool on = questRow != null && questRow.buy != null;
        if (lessonHighlight != null)
        {
            if (!lessonHighlightRead) { lessonHighlightRead = true; lessonHighlightColour = lessonHighlight.color; }
            lessonHighlight.gameObject.SetActive(on);
        }
        bool shown = lessonTip != null && lessonTip.gameObject.activeSelf;
        if (lessonTip != null) lessonTip.gameObject.SetActive(on);
        if (!on)
        {
            foreach (var v in new[] { veilAbove, veilBelow, veilSide }) if (v != null) v.gameObject.SetActive(false);
            return;
        }
        var buy = (RectTransform)questRow.buy.transform;
        float y = buy.anchoredPosition.y;
        if (lessonHighlight != null) lessonHighlight.rectTransform.anchoredPosition = new Vector2(lessonHighlight.rectTransform.anchoredPosition.x, y);
        // The washes: from the row's upper edge up, from its lower edge down (pivots on the edge that meets the row).
        Wash(veilAbove, y + lessonRowHalfHeight, veilSpan.y - (y + lessonRowHalfHeight));
        Wash(veilBelow, y - lessonRowHalfHeight, (y - lessonRowHalfHeight) - veilSpan.x);
        if (veilSide != null) { veilSide.gameObject.SetActive(true); AlignPaper(veilSide); }
        if (lessonTip == null) return;
        string Fill(string text) => (text ?? "").Replace("{skill}", lesson.displayName).Replace("{price}", Mathf.Max(0, meta.NextPrice(lesson)).ToString());
        if (lessonTitle != null) lessonTitle.text = Fill(lessonTitleText);
        if (lessonBody != null) lessonBody.text = Fill(lessonBodyText);
        LayoutRebuilder.ForceRebuildLayoutImmediate(lessonTip);
        // Over the button with its tail pointing down at it (there it covers the right end of the two rows above from
        // edge to edge, nothing cut in half); under it when there is no room above (the first rows). The window's pivot
        // is its top right corner, and its tail stands outside the edge that faces the button. This is where it rests,
        // at its farthest: it bobs toward the button from here.
        float height = lessonTip.rect.height, tail = lessonTail != null ? lessonTail.rect.height : 0f;
        float clear = buy.rect.height / 2 + questRingPadding.y / 2 + 4f;   // past the ring round the button
        bool below = y + clear + tail + height > veilSpan.y;
        float right = buy.anchoredPosition.x + buy.rect.width / 2 + 10f;
        lessonTipAt = new Vector2(right, below ? y - clear - tail : y + clear + tail + height);
        lessonTipAway = below ? Vector2.down : Vector2.up;
        lessonTip.anchoredPosition = lessonTipAt;
        if (lessonTail != null)
        {
            lessonTail.anchorMin = lessonTail.anchorMax = new Vector2(1, below ? 1 : 0);
            lessonTail.localScale = new Vector3(1, below ? 1 : -1, 1);
            // Its base lies a few units inside the window, over the frame's line.
            lessonTail.anchoredPosition = new Vector2(buy.anchoredPosition.x - right, below ? -3f : 3f);
        }
        // It pops in when it first shows, from the corner by the button.
        if (!shown)
        {
            lessonTip.DOKill(true);
            lessonTip.localScale = Vector3.one * .8f;
            lessonTip.DOScale(1f, .26f).SetEase(Ease.OutBack).SetDelay(.25f).SetUpdate(true).SetLink(lessonTip.gameObject);
        }
    }

    static void Wash(RectTransform veil, float edge, float height)
    {
        if (veil == null) return;
        veil.gameObject.SetActive(height > 1f);
        veil.anchoredPosition = new Vector2(veil.anchoredPosition.x, edge);
        veil.sizeDelta = new Vector2(veil.sizeDelta.x, Mathf.Max(0f, height));
        AlignPaper(veil);
    }

    // A wash is a window (a rect mask) onto a copy of the paper itself, drawn half clear over the ink: the copy is kept
    // exactly on the paper, so the wash has no edge of its own and only what is drawn on the paper fades.
    static void AlignPaper(RectTransform veil)
    {
        if (veil.childCount > 0 && veil.parent != null) veil.GetChild(0).position = veil.parent.position;
    }

    void RefreshRow(ProgressionManager meta, Row r)
    {
        var t = Track(r);
        if (t == null) return;
        int levels = meta.Levels(t), bought = meta.Bought(t);
        bool locked = meta.LockedByLesson(t);
        if (r.title != null) r.title.text = t.displayName;
        if (r.icon != null && t.icon != null && t.kind == MetaTrackKind.Skill) r.icon.sprite = t.icon;
        if (r.value != null) { r.value.text = meta.Value(t).ToString(); r.value.color = bought > 0 ? pen : ink; }
        if (r.unit != null && t.kind == MetaTrackKind.Attribute)
        {
            r.unit.text = ProgressionManager.AttributeEffects(t, levels);
            r.unit.color = levels > 0 ? pen : faint;
        }
        for (int i = 0; i < r.bubbles.Length; i++)
        {
            var b = r.bubbles[i];
            if (b == null) continue;
            b.gameObject.SetActive(i < t.levels);
            bool given = i < t.givenLevels, have = i < levels, next = i == levels;
            b.sprite = have ? bubbleFilled : next ? bubbleDashed : bubbleOutline;
            b.color = have ? (given ? ink : pen) : next ? pen : ink;
        }
        if (t.milestones != null)
            for (int i = 0; i < t.milestones.Count; i++)
            {
                var m = t.milestones[i];
                bool reached = meta.Reached(t, m);
                if (i < r.milestoneIcons.Length && r.milestoneIcons[i] != null)
                {
                    r.milestoneIcons[i].enabled = m.Icon != null;
                    if (m.Icon != null) r.milestoneIcons[i].sprite = m.Icon;
                }
                if (i < r.milestoneMarks.Length && r.milestoneMarks[i] != null)
                {
                    r.milestoneMarks[i].text = m.Icon != null ? "" : "?";
                    r.milestoneMarks[i].color = reached ? Color.white : ink;
                }
            }
        int price = meta.NextPrice(t);
        if (r.buy != null) r.buy.interactable = price >= 0;
        if (r.buyLabel != null)
        {
            // Before the lesson every other row says which skill comes first on its button.
            r.buyLabel.text = price < 0 ? "MAX" : locked ? meta.LessonSkillName + "\n" + "first" : $"Train {price}";
            // The lesson text is two short lines, sized to fit inside the tag (the small attribute tags most of all).
            if (r.buyLabelSize <= 0f) { r.buyLabelSize = r.buyLabel.fontSize; r.buyLabelSpacing = r.buyLabel.lineSpacing; }
            // "Train 20" and longer prices shrink to fit the tag too (the attribute tags are small; review 2026-11-12).
            r.buyLabel.enableAutoSizing = true;
            // The label's rect runs to the tag's right edge and starts at its string hole: keep the text inside the border
            // and clear of the hole.
            float tagHeight = r.buyLabel.rectTransform.rect.height;
            r.buyLabel.margin = new Vector4(tagHeight * .15f, 0f, tagHeight * .25f, 0f);
            r.buyLabel.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            if (locked) { r.buyLabel.fontSizeMax = r.buyLabelSize * .8f; r.buyLabel.fontSizeMin = r.buyLabelSize * .4f; r.buyLabel.lineSpacing = -20f; }
            else { r.buyLabel.fontSizeMax = r.buyLabelSize; r.buyLabel.fontSizeMin = r.buyLabelSize * .5f; r.buyLabel.lineSpacing = r.buyLabelSpacing; }
            r.buyLabel.color = price < 0 || locked ? faint : meta.Spoils < price ? poor : pen;
        }
        if (r.buyIcon != null) r.buyIcon.enabled = price >= 0 && !locked;
    }

    // What the track gives now: a skill's bonus at its value, an attribute's bonus per point.
    void ShowTip(Row r, RectTransform near)
    {
        var meta = Meta; var t = Track(r);
        if (tip == null || meta == null || t == null) return;
        string body;
        if (t.kind == MetaTrackKind.Attribute)
        {
            string label = ColorUtility.ToHtmlStringRGB(faint);
            body = $"<color=#{label}>Each point</color>\n{ProgressionManager.AttributeEffects(t, 1, true)}";
            if (meta.Levels(t) > 0) body += $"\n<color=#{label}>Now</color>\n{ProgressionManager.AttributeEffects(t, meta.Levels(t), true)}";
        }
        else body = SkillBonus(t, meta.Value(t));
        ShowCard($"{t.displayName}  <color=#{ColorUtility.ToHtmlStringRGB(pen)}>{meta.Value(t)}</color>", body, near);
    }

    void ShowMilestoneTip(Row r, int index, RectTransform near)
    {
        var meta = Meta; var t = Track(r);
        if (tip == null || meta == null || t == null || t.milestones == null || index >= t.milestones.Count) return;
        var m = t.milestones[index];
        bool got = meta.Reached(t, m);
        // What it does and when, never its trophy (the user, 2026-11-12).
        string state = got ? "Learned." : $"At {t.displayName} {m.value}.";
        string name = m.Known ? m.Name : "?";
        string body = string.IsNullOrEmpty(m.Description) ? state : $"{m.Description}\n<color=#{ColorUtility.ToHtmlStringRGB(got ? pen : faint)}>{state}</color>";
        ShowCard(name, body, near);
    }

    // A skill's bonus at a value, in the words of its definition ("+5% damage").
    static string SkillBonus(ProgressionManager.MetaTrack t, int value)
    {
        var p = PlayerManager.HasInstance ? PlayerManager.Instance.GetPlayer(PlayerKind.Table) : null;
        var def = p != null ? p.GetComponent<AdventurerProgress>()?.Definition(t.skill) : null;
        if (def == null) return "";
        if (string.IsNullOrEmpty(def.bonusText) || def.bonusAt100 <= 0) return def.description;
        string line = string.Format(def.bonusText, (value * def.bonusAt100).ToString("0.#")) + ".";
        if (!string.IsNullOrEmpty(def.extraText) && def.extraAt100 > 0) line += " " + string.Format(def.extraText, def.ExtraShown(value)) + ".";
        return line;
    }

    void ShowCard(string title, string body, RectTransform near)
    {
        if (unlockBanner != null && unlockBanner.gameObject.activeSelf) return;
        tip.gameObject.SetActive(true);
        if (tipTitle != null) tipTitle.text = title;
        if (tipBody != null) tipBody.text = body;
        // Beside what is hovered, inside the sheet.
        var parent = tip.parent as RectTransform;
        if (near != null && parent != null)
        {
            Vector3[] corners = new Vector3[4];
            near.GetWorldCorners(corners);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, RectTransformUtility.WorldToScreenPoint(null, corners[2]), null, out local);
            LayoutRebuilder.ForceRebuildLayoutImmediate(tip);
            var size = tip.rect.size; var bounds = parent.rect;
            local.x = Mathf.Min(local.x + 12, bounds.xMax - size.x - 12);
            local.y = Mathf.Clamp(local.y, bounds.yMin + size.y + 12, bounds.yMax - 12);
            tip.anchoredPosition = local;
        }
    }
    void HideTip() { if (tip != null) tip.gameObject.SetActive(false); }
}

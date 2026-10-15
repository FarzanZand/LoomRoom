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
        Refresh();
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (nameField != null && nameField.isFocused) SaveName(nameField.text);
        if (Meta != null) Meta.MetaChanged -= Refresh;
        HideTip();
        if (root != null) root.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        // Put down: the DM hands over what was earned.
        Meta?.HandOverTrophies();
    }

    void Update()
    {
        if (open && feedback != null && feedback.text.Length > 0 && Time.unscaledTime > feedbackUntil) feedback.text = "";
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
            case ProgressionManager.BuyResult.Locked: DungeonMaster.Say(meta.lessonBlockingFirst); ShowFeedback($"\"{meta.lessonBlockingFirst.text}\""); Shake(r); break;
            case ProgressionManager.BuyResult.Poor: ShowFeedback($"Not enough spoils. {t.displayName} needs {price}."); Shake(r); break;
            case ProgressionManager.BuyResult.Max: break;
            case ProgressionManager.BuyResult.Bought:
                PlayUi("shopTrade", "skillUp");
                if (t.milestones != null)
                    foreach (var m in t.milestones)
                        if (meta.Reached(t, m) && !before.Contains(m))
                        {
                            ShowUnlock(m.unlock != "?" ? $"Unlocked: {m.unlock}" : $"{t.displayName} {m.value}", "A trophy for the wall");
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
        unlockBanner.gameObject.SetActive(true);
        // A banner shown while another is up starts over: the old sequence would otherwise hide the new one early.
        bannerSequence?.Kill();
        unlockBanner.DOKill(true);
        unlockBanner.localScale = Vector3.one * .6f;
        bannerSequence = DOTween.Sequence().SetUpdate(true).SetLink(unlockBanner.gameObject)
            .Append(unlockBanner.DOScale(1f, .22f).SetEase(Ease.OutBack))
            .AppendInterval(3.2f)
            .AppendCallback(() => unlockBanner.gameObject.SetActive(false));
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
        if (portrait != null) { portrait.sprite = cls != null && cls.sheetPortrait != null ? cls.sheetPortrait : portraitPlaceholder; portrait.preserveAspect = true; }
        if (portraitLabel != null) portraitLabel.text = portraits.Count > 0 ? $"Portrait {Mathf.Clamp(meta.PortraitIndex, 0, portraits.Count - 1) + 1} of {portraits.Count}" : "";
        foreach (var r in rows) RefreshRow(meta, r);
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
            float bonus = meta.AttributeBonus(t);
            r.unit.text = $"+{(Mathf.Approximately(bonus, Mathf.Round(bonus)) ? Mathf.RoundToInt(bonus).ToString() : bonus.ToString("0.#"))} {t.unit}";
            r.unit.color = bonus > 0 ? pen : faint;
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
                    r.milestoneIcons[i].enabled = m.icon != null;
                    if (m.icon != null) r.milestoneIcons[i].sprite = m.icon;
                }
                if (i < r.milestoneMarks.Length && r.milestoneMarks[i] != null)
                {
                    r.milestoneMarks[i].text = m.icon != null ? "" : "?";
                    r.milestoneMarks[i].color = reached ? Color.white : ink;
                }
            }
        int price = meta.NextPrice(t);
        if (r.buy != null) r.buy.interactable = price >= 0;
        if (r.buyLabel != null)
        {
            r.buyLabel.text = price < 0 ? "MAX" : $"Buy {price}";
            r.buyLabel.color = price < 0 ? faint : locked || meta.Spoils < price ? poor : pen;
        }
        if (r.buyIcon != null) r.buyIcon.enabled = price >= 0;
    }

    // What the track gives now: a skill's bonus at its value, an attribute's bonus per point.
    void ShowTip(Row r, RectTransform near)
    {
        var meta = Meta; var t = Track(r);
        if (tip == null || meta == null || t == null) return;
        string body;
        if (t.kind == MetaTrackKind.Attribute)
        {
            float bonus = meta.AttributeBonus(t);
            body = $"+{t.bonusPerPoint:0.#} {t.unit} per point.";
            if (bonus > 0) body += $"\nNow: +{bonus:0.#} {t.unit}.";
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
        string state = got ? (meta.Trophy(t, m) == ProgressionManager.TrophyState.Mounted ? "On the wall." : "A trophy to hang.") : $"At {t.displayName} {m.value}.";
        string name = string.IsNullOrEmpty(m.unlock) || m.unlock == "?" ? "?" : m.unlock;
        string body = string.IsNullOrEmpty(m.description) ? state : $"{m.description}\n<color=#{ColorUtility.ToHtmlStringRGB(got ? pen : faint)}>{state}</color>";
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

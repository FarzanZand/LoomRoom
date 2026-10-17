using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The run summary after death or victory: what killed you, how deep, kills, gold, time,
// seed, then a new run, the adventure menu or back to the room. With Wake In Bed it offers only
// "Wake up": back to bed for the next morning (after a win, WorldManager's ending).
public class RunRecapUI : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] CanvasGroup group;
    [SerializeField] TMP_Text title;
    [SerializeField] TMP_Text cause;
    [SerializeField] TMP_Text stats;
    [SerializeField] TMP_Text seed;
    [SerializeField] Button newRunButton;
    [SerializeField] Button adventuresButton;
    [SerializeField] Button returnButton;
    [SerializeField, Min(.05f)] float fadeSeconds = .8f;
    [SerializeField] Color deathColor = new Color(.85f, .22f, .18f);
    [SerializeField] Color victoryColor = new Color(1f, .8f, .35f);
    [SerializeField, Tooltip("Every run ends by waking in bed: the only button is Wake up.")] bool wakeInBed = true;
    [SerializeField, Tooltip("The return button's label after a win (the ending, waking in bed).")] string wakeLabel = "Wake up";
    [SerializeField, Tooltip("The return button's label after a death (back in the room, standing by the table).")] string standLabel = "Stand up";

    [Header("Tally (spoils, ProgressionManager)")]
    [SerializeField, Tooltip("The tally rows; hidden when a run counts no spoils (the intro's practice board).")] GameObject tallyRoot;
    [SerializeField] RectTransform spoilsStrip;
    [SerializeField, Tooltip("One spoils icon, cloned per spoil carried (it stays hidden).")] Image spoilsIconTemplate;
    [SerializeField] TMP_Text spoilsValue;
    [SerializeField] RectTransform killsStrip;
    [SerializeField, Tooltip("One skull, cloned per kill (it stays hidden).")] Image skullTemplate;
    [SerializeField] TMP_Text killsValue, healthLine, healthValue, totalValue, bankValue, helpLine;
    [SerializeField, Tooltip("The short line of floor, gold and time under the tally.")] TMP_Text summaryLine;
    [SerializeField, Min(0)] int maxSkulls = 64, maxSpoilIcons = 24;
    [SerializeField, Min(1), Tooltip("Rows of icons each strip holds (each icon its template's size); past that the icons of a row overlap so they all fit.")]
    int stripRows = 3;
    [SerializeField, Tooltip("Inside each strip: space from its left and right edges (x) and its top and bottom (y).")] Vector2 stripPadding = new(14, 10);
    [SerializeField, Min(0)] float stripGap = 4;

    [Header("Spoils showcase (built by Tools > LoomRoom > UI > Build Recap Spoils Showcase)")]
    [SerializeField, Tooltip("The big picture of the spoil being counted; it grows from Showcase Start Scale to 1 as the spoils come in.")] Image showcaseIcon;
    [SerializeField, Tooltip("Light rays behind it, turning slowly and flaring with each spoil.")] Image showcaseRays;
    [SerializeField, Range(.1f, 1f)] float showcaseStartScale = .65f;
    [SerializeField, Tooltip("Where flying icons and sparks are drawn, over the tally.")] RectTransform effects;
    [SerializeField, Tooltip("Shaken when the total lands.")] RectTransform shakeTarget;
    [SerializeField, Tooltip("Gaps between spoils: the first, how much each next one shrinks, the shortest.")] float spoilFirstGap = .2f;
    [SerializeField, Range(.5f, 1f)] float spoilGapShrink = .9f;
    [SerializeField, Min(.03f)] float spoilMinGap = .06f;
    [SerializeField, Tooltip("Gaps between skulls: the first, how much each next one shrinks, the shortest.")] float skullFirstGap = .07f;
    [SerializeField, Range(.5f, 1f)] float skullGapShrink = .95f;
    [SerializeField, Min(.02f)] float skullMinGap = .03f;
    [SerializeField, Min(.05f)] float flySeconds = .26f;
    [SerializeField, Tooltip("Each tick's pitch: the first, the rise per tick, the highest.")] float tickPitch = .9f;
    [SerializeField] float tickPitchStep = .035f, tickPitchMax = 2f;
    [SerializeField] Color sparkGold = new(1f, .82f, .35f), sparkWhite = new(1f, .97f, .85f);
    [SerializeField, Min(0)] int totalSparks = 36;
    [SerializeField, Tooltip("A spoil worth at least this gets a moment of its own: a pause before it, a big flare, sparks and a clink.")] int bigSpoilWorth = 10;
    [SerializeField, Min(8)] float worthTextSize = 34;

    const string TickKey = "tallyTick", SkullKey = "tallySkull", TotalKey = "tallyTotal", BankKey = "tallyBank";
    Color spoilsColour, killsColour, totalColour, bankColour;
    bool coloursRead;
    [SerializeField, Min(.01f), Tooltip("How long the total and the bank take to count up.")] float countSeconds = .9f;

    bool open, wakes, won;
    Coroutine counting;
    string returnLabel;
    Vector2? returnPosition;
    Tween fade;

    static TableLevelLoader Loader => TableManager.Loader;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        newRunButton?.onClick.AddListener(NewRun);
        adventuresButton?.onClick.AddListener(Adventures);
        returnButton?.onClick.AddListener(ReturnToRoom);
    }

    public void Show(RunSummary s)
    {
        if (root == null) return;
        if (!open && GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
        open = true;
        root.SetActive(true);
        if (title != null) { title.text = s.victory ? "VICTORY" : "YOU DIED"; title.color = s.victory ? victoryColor : deathColor; }
        if (cause != null)
            cause.text = (string.IsNullOrEmpty(s.adventurer) ? "" : $"<color=#A3A3A3>{s.adventurer}</color>\n") + (s.victory
                ? $"You conquered {s.levelName}."
                : $"Killed by {RunManager.KillerWithArticle(s.killer)} on floor {s.floor} of {s.levelName}.");
        bool tallied = s.tally.counted && tallyRoot != null;
        if (tallyRoot != null) tallyRoot.SetActive(tallied);
        if (summaryLine != null)
        {
            summaryLine.gameObject.SetActive(tallied);
            summaryLine.text = $"Floor {s.floor} / {s.floors}   ·   Gold {s.gold}   ·   {s.TimeText}" + (s.bosses > 0 ? $"   ·   Bosses {s.bosses}" : "");
        }
        if (stats != null) stats.gameObject.SetActive(!tallied);
        if (counting != null) StopCoroutine(counting);
        counting = tallied ? StartCoroutine(Count(s)) : null;
        if (stats != null)
            stats.text = $"Floor reached  <b>{s.floor}</b> / {s.floors}\n" +
                         $"Enemies slain  <b>{s.kills}</b>\n" +
                         (s.bosses > 0 ? $"Bosses slain  <b>{s.bosses}</b>\n" : "") +
                         $"Gold found  <b>{s.gold}</b>\n" +
                         $"Time  <b>{s.TimeText}</b>" +
                         (string.IsNullOrEmpty(s.skills) ? "" : $"\n\n<color=#8C8173>{s.skills}</color>");
        if (seed != null) seed.text = $"Seed {s.seed}";
        fade?.Kill();
        if (group != null)
        {
            group.alpha = 0f;
            fade = group.DOFade(1f, fadeSeconds).SetUpdate(true);
        }
        // With the morning loop, a run has one way on: waking up in bed.
        wakes = wakeInBed && WorldManager.HasInstance && WorldManager.Instance.wakeUpCutscene != null;
        won = s.victory;
        if (newRunButton != null) newRunButton.gameObject.SetActive(!wakes);
        if (adventuresButton != null) adventuresButton.gameObject.SetActive(!wakes);
        var label = returnButton != null ? returnButton.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null)
        {
            returnLabel ??= label.text;
            label.text = wakes ? (won ? wakeLabel : standLabel) : returnLabel;
        }
        // Alone, it takes the middle slot.
        if (returnButton != null && returnButton.transform is RectTransform rect)
        {
            returnPosition ??= rect.anchoredPosition;
            rect.anchoredPosition = wakes ? new Vector2(0f, returnPosition.Value.y) : returnPosition.Value;
        }
        (wakes ? returnButton : newRunButton)?.Select();
    }

    // The tally counts up row by row: the spoils pop into their strip, the kills fill theirs, their health turns into
    // spoils, then the total goes into what there is to spend.
    // Each spoil shows big in the showcase (which grows as they come in), then flies down into its place in the strip,
    // the count jumping and a tick rising in pitch with every one, faster and faster. The skulls follow the same way,
    // the total counts up and lands with a flash, sparks and a shake, then it rolls into the bank.
    IEnumerator Count(RunSummary s)
    {
        var t = s.tally;
        ReadColours();
        ClearEffects();
        Clear(spoilsStrip, spoilsIconTemplate); Clear(killsStrip, skullTemplate);
        SetText(spoilsValue, "0"); SetText(killsValue, "0"); SetText(healthValue, "0"); SetText(totalValue, "0");
        // The health line is gone from the recap (the user's call); the spoils it gives still count in the total.
        if (healthLine != null) healthLine.gameObject.SetActive(false);
        if (healthValue != null) healthValue.gameObject.SetActive(false);
        SetText(bankValue, t.before.ToString());
        SetText(helpLine, "");
        int spoilCount = 0;
        if (t.carried != null) foreach (var (_, count) in t.carried) spoilCount += count;
        ResetShowcase(spoilCount > 0 && t.carried.Count > 0 ? t.carried[0].item.icon : null);
        yield return Wait(.45f);
        int worth = 0, icons = 0, n = 0;
        float gap = spoilFirstGap;
        if (t.carried != null)
            foreach (var (item, count) in t.carried)
                for (int i = 0; i < count; i++)
                {
                    int each = Meta != null ? Meta.WorthOf(item) : 0;
                    bool big = each >= bigSpoilWorth;
                    if (big) yield return Wait(.3f);   // a breath before a valuable one
                    worth += each;
                    n++;
                    SetText(spoilsValue, worth.ToString());
                    Punch(spoilsValue, spoilsColour, big ? .6f : .3f);
                    Showcase(item.icon, spoilCount > 1 ? (n - 1f) / (spoilCount - 1f) : 1f, big);
                    FloatWorth(each, big);
                    if (icons++ < maxSpoilIcons) Fly(Pop(spoilsStrip, spoilsIconTemplate, item.icon, false), item.icon);
                    Sound(TickKey, Mathf.Min(tickPitchMax, tickPitch + tickPitchStep * (n - 1)));
                    if (big) Sound(BankKey, 1.15f);
                    yield return Wait(big ? Mathf.Max(gap, .4f) : gap);
                    gap = Mathf.Max(spoilMinGap, gap * spoilGapShrink);
                }
        SetText(spoilsValue, t.carriedWorth.ToString());
        yield return Wait(.35f);
        gap = skullFirstGap;
        for (int i = 0; i < t.kills; i++)
        {
            if (i < maxSkulls) Pop(killsStrip, skullTemplate, null, true);
            SetText(killsValue, (i + 1).ToString());
            Punch(killsValue, killsColour, .25f);
            if (i < maxSkulls)
            {
                Sound(SkullKey, Mathf.Min(tickPitchMax, .9f + .02f * i));
                yield return Wait(gap);
                gap = Mathf.Max(skullMinGap, gap * skullGapShrink);
            }
        }
        yield return Wait(.35f);
        yield return CountUp(totalValue, 0, t.total, v => v.ToString(), TickKey, 1.2f);
        Slam(totalValue, totalColour);
        if (t.toppedUp > 0) SetText(helpLine, $"+{t.toppedUp} from the Dungeon Master");
        yield return Wait(.6f);
        yield return CountUp(bankValue, t.before, t.after, v => v.ToString(), TickKey, 1.5f);
        Punch(bankValue, bankColour, .45f);
        if (bankValue != null) { Flash(bankValue.rectTransform.position, 1.8f); Sparks(bankValue.rectTransform.position, 16, 110f, 1.3f); }
        Sound(BankKey, 1f);
        counting = null;
    }
    static ProgressionManager Meta => ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
    static void SetText(TMP_Text text, string value) { if (text != null) text.text = value; }
    static object Wait(float seconds) => new WaitForSecondsRealtime(seconds);
    // Counts a number up over countSeconds, ticking (rising from startPitch) as it goes.
    IEnumerator CountUp(TMP_Text text, int from, int to, System.Func<int, string> format, string tick = null, float startPitch = 1f)
    {
        if (text == null) yield break;
        int shown = from, ticks = 0;
        for (float e = 0; e < countSeconds; e += Time.unscaledDeltaTime)
        {
            int v = Mathf.RoundToInt(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, e / countSeconds)));
            if (v != shown)
            {
                shown = v;
                text.text = format(v);
                if (tick != null && ticks++ % 2 == 0) Sound(tick, Mathf.Min(tickPitchMax + .3f, startPitch + .9f * e / countSeconds));
            }
            yield return null;
        }
        text.text = format(to);
    }

    // ── Feel ─────────────────────────────────────────────────────────

    void ReadColours()
    {
        if (coloursRead) return;
        coloursRead = true;
        spoilsColour = spoilsValue != null ? spoilsValue.color : Color.white;
        killsColour = killsValue != null ? killsValue.color : Color.white;
        totalColour = totalValue != null ? totalValue.color : Color.white;
        bankColour = bankValue != null ? bankValue.color : Color.white;
    }

    // A number jumps and flashes white, then settles back to its own colour.
    static void Punch(TMP_Text text, Color colour, float strength)
    {
        if (text == null) return;
        var t = text.transform;
        t.DOKill(true);
        t.localScale = Vector3.one;
        t.DOPunchScale(Vector3.one * strength, .2f, 5, .6f).SetUpdate(true).SetLink(text.gameObject);
        text.DOKill();
        text.color = Color.white;
        text.DOColor(colour, .25f).SetUpdate(true).SetLink(text.gameObject);
    }

    // The total lands: it slams down from big, flashes, throws sparks and shakes the panel.
    void Slam(TMP_Text text, Color colour)
    {
        Sound(TotalKey, 1f);
        if (text == null) return;
        var t = text.transform;
        t.DOKill(true);
        t.localScale = Vector3.one * 1.9f;
        t.DOScale(1f, .32f).SetEase(Ease.OutBack, 2.5f).SetUpdate(true).SetLink(text.gameObject);
        text.DOKill();
        text.color = Color.white;
        text.DOColor(colour, .6f).SetDelay(.1f).SetUpdate(true).SetLink(text.gameObject);
        Flash(text.rectTransform.position, 3.2f);
        Sparks(text.rectTransform.position, totalSparks, 240f, 1.7f);
        if (shakeTarget != null)
        {
            shakeTarget.DOKill(true);
            shakeTarget.DOShakeAnchorPos(.35f, 12f, 30, 90f, false, true).SetUpdate(true).SetLink(shakeTarget.gameObject);
        }
    }

    void ResetShowcase(Sprite first)
    {
        if (showcaseIcon != null)
        {
            showcaseIcon.gameObject.SetActive(first != null);
            if (first != null) showcaseIcon.sprite = first;
            showcaseIcon.preserveAspect = true;
            showcaseIcon.transform.DOKill();
            showcaseIcon.transform.localScale = Vector3.one * showcaseStartScale;
            showcaseIcon.transform.localRotation = Quaternion.identity;
        }
        if (showcaseRays != null)
        {
            showcaseRays.gameObject.SetActive(first != null);
            var rays = showcaseRays.transform;
            rays.DOKill();
            rays.localScale = Vector3.one * showcaseStartScale;
            var c = showcaseRays.color; c.a = .25f; showcaseRays.color = c;
            rays.DOLocalRotate(new Vector3(0, 0, -360), 14f, RotateMode.FastBeyond360).SetEase(Ease.Linear).SetLoops(-1).SetUpdate(true).SetLink(rays.gameObject);
        }
    }

    // The spoil being counted fills the showcase, which grows toward full size as the count goes on (share 0 to 1); a
    // big one flares harder and throws sparks.
    void Showcase(Sprite sprite, float share, bool big)
    {
        float size = Mathf.Lerp(showcaseStartScale, 1f, share);
        if (big && showcaseIcon != null) { Flash(showcaseIcon.rectTransform.position, 2.2f); Sparks(showcaseIcon.rectTransform.position, 22, 170f, 1.3f); }
        if (showcaseIcon != null)
        {
            if (sprite != null) showcaseIcon.sprite = sprite;
            var t = showcaseIcon.transform;
            t.DOKill(true);
            t.localScale = Vector3.one * size * (big ? 1.45f : 1.18f);
            t.localRotation = Quaternion.Euler(0, 0, Random.Range(-6f, 6f));
            t.DOScale(size, .2f).SetEase(Ease.OutBack, 3f).SetUpdate(true).SetLink(t.gameObject);
            t.DOLocalRotate(Vector3.zero, .25f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(t.gameObject);
        }
        if (showcaseRays != null)
        {
            var rays = showcaseRays.transform;
            rays.DOScale(size * (big ? 1.5f : 1.1f), .2f).SetUpdate(true).SetLink(showcaseRays.gameObject);
            if (big) rays.DOScale(size * 1.1f, .5f).SetDelay(.2f).SetUpdate(true).SetLink(showcaseRays.gameObject);
            showcaseRays.DOKill();
            var c = showcaseRays.color; c.a = big ? 1f : .85f; showcaseRays.color = c;
            showcaseRays.DOFade(.3f, big ? .7f : .35f).SetUpdate(true).SetLink(showcaseRays.gameObject);
        }
    }

    // "+N" rises out of the showcase and fades: what that spoil was worth.
    void FloatWorth(int worth, bool big)
    {
        if (effects == null || showcaseIcon == null || worth <= 0) return;
        var label = new GameObject("Worth", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        label.transform.SetParent(effects, false);
        label.raycastTarget = false;
        if (spoilsValue != null) { label.font = spoilsValue.font; label.fontSharedMaterial = spoilsValue.fontSharedMaterial; }
        label.fontSize = worthTextSize * (big ? 1.5f : 1f);
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.text = "+" + worth;
        label.color = big ? sparkGold : sparkWhite;
        label.rectTransform.sizeDelta = new Vector2(160, 60);
        float s = effects.lossyScale.y;
        // It stays over the showcase: from its middle up to just under its top edge.
        var start = showcaseIcon.rectTransform.position + new Vector3(Random.Range(-24f, 24f) * s, -10f * s, 0);
        label.rectTransform.position = start;
        label.transform.localScale = Vector3.one * .6f;
        var seq = DOTween.Sequence().SetUpdate(true).SetLink(label.gameObject);
        seq.Append(label.transform.DOScale(1f, .15f).SetEase(Ease.OutBack, 3f));
        seq.Join(label.rectTransform.DOMove(start + Vector3.up * (big ? 70f : 55f) * s, big ? .9f : .55f).SetEase(Ease.OutCubic));
        seq.Insert(big ? .5f : .25f, label.DOFade(0f, big ? .4f : .3f));
        seq.OnComplete(() => Destroy(label.gameObject));
    }

    // A copy of the spoil leaves the showcase and lands in its place in the strip, which then pops in with a few sparks.
    void Fly(Image slot, Sprite sprite)
    {
        if (slot == null) return;
        if (effects == null || showcaseIcon == null) { Reveal(slot, false); return; }
        var flyer = new GameObject("Flying spoil", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        flyer.transform.SetParent(effects, false);
        flyer.raycastTarget = false;
        flyer.sprite = sprite; flyer.preserveAspect = true;
        flyer.rectTransform.sizeDelta = slot.rectTransform.rect.size;
        var from = showcaseIcon.rectTransform.position;
        var to = slot.rectTransform.position;
        flyer.rectTransform.position = from;
        flyer.transform.localScale = Vector3.one * 2.2f;
        // A little hop up before it drops into place.
        var mid = Vector3.Lerp(from, to, .5f) + Vector3.up * 60f * effects.lossyScale.y;
        var seq = DOTween.Sequence().SetUpdate(true).SetLink(flyer.gameObject);
        seq.Append(flyer.rectTransform.DOPath(new[] { mid, to }, flySeconds, PathType.CatmullRom).SetEase(Ease.InQuad));
        seq.Join(flyer.transform.DOScale(1f, flySeconds).SetEase(Ease.InQuad));
        seq.OnComplete(() =>
        {
            Destroy(flyer.gameObject);
            Reveal(slot, true);
        });
    }

    void Reveal(Image slot, bool sparks)
    {
        if (slot == null) return;
        slot.enabled = true;
        slot.transform.localScale = Vector3.one * 1.5f;
        slot.transform.DOScale(1f, .16f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(slot.gameObject);
        if (sparks) Sparks(slot.rectTransform.position, 5, 45f);
    }

    // A burst of the showcase's rays behind a point, opening out and fading.
    void Flash(Vector3 at, float scale)
    {
        if (effects == null || showcaseRays == null) return;
        var flash = new GameObject("Flash", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        flash.transform.SetParent(effects, false);
        flash.transform.SetAsFirstSibling();   // under the sparks
        flash.raycastTarget = false;
        flash.sprite = showcaseRays.sprite;
        flash.color = sparkGold;
        flash.rectTransform.sizeDelta = new Vector2(160, 160);
        flash.rectTransform.position = at;
        flash.transform.localScale = Vector3.one * .4f;
        var seq = DOTween.Sequence().SetUpdate(true).SetLink(flash.gameObject);
        seq.Append(flash.transform.DOScale(scale, .55f).SetEase(Ease.OutCubic));
        seq.Join(flash.transform.DOLocalRotate(new Vector3(0, 0, -50), .55f));
        seq.Join(flash.DOFade(0f, .55f).SetEase(Ease.InQuad));
        seq.OnComplete(() => Destroy(flash.gameObject));
    }

    // Little square sparks thrown out from a point, falling a little as they fade.
    void Sparks(Vector3 at, int count, float reach, float sizeScale = 1f)
    {
        if (effects == null) return;
        float scale = effects.lossyScale.x;
        for (int i = 0; i < count; i++)
        {
            var spark = new GameObject("Spark", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            spark.transform.SetParent(effects, false);
            spark.raycastTarget = false;
            spark.color = Random.value < .6f ? sparkGold : sparkWhite;
            float size = Random.Range(4f, 9f) * sizeScale;
            spark.rectTransform.sizeDelta = new Vector2(size, size);
            spark.rectTransform.position = at;
            var dir = Random.insideUnitCircle.normalized;
            float distance = Random.Range(.35f, 1f) * reach * scale;
            float seconds = Random.Range(.45f, .8f);
            var end = at + (Vector3)(dir * distance) + Vector3.down * reach * .35f * scale;
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(spark.gameObject);
            seq.Append(spark.rectTransform.DOMove(end, seconds).SetEase(Ease.OutCubic));
            seq.Join(spark.DOFade(0f, seconds).SetEase(Ease.InQuad));
            seq.Join(spark.transform.DOScale(.3f, seconds));
            seq.OnComplete(() => Destroy(spark.gameObject));
        }
    }

    void ClearEffects()
    {
        if (effects == null) return;
        for (int i = effects.childCount - 1; i >= 0; i--) Destroy(effects.GetChild(i).gameObject);
    }

    static void Sound(string key, float pitch)
    {
        if (!AudioManager.HasInstance || !AudioManager.Instance.HasUI(key)) return;
        var source = AudioManager.Instance.PlayUI(key);
        if (source != null) source.pitch *= pitch;
    }
    static void Clear(RectTransform strip, Image template)
    {
        if (strip == null) return;
        for (int i = strip.childCount - 1; i >= 0; i--) { var c = strip.GetChild(i); if (template == null || c != template.transform) { c.gameObject.SetActive(false); Destroy(c.gameObject); } }
        if (template != null) template.gameObject.SetActive(false);
    }
    // A new icon in its strip; shown: pops in now, otherwise it waits hidden for whatever reveals it (Fly).
    Image Pop(RectTransform strip, Image template, Sprite sprite, bool shown)
    {
        if (strip == null || template == null) return null;
        var img = Instantiate(template, strip);
        img.gameObject.SetActive(true);
        if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
        Arrange(strip, template);
        img.enabled = shown;
        if (!shown) return img;
        img.transform.localScale = Vector3.zero;
        img.transform.DOScale(1f, .18f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(img.gameObject);
        return img;
    }

    // Lays a strip's icons out left to right in up to stripRows rows (the rows in use centred in the strip); once all
    // the rows are full, each row takes an equal share and its icons close up and overlap until they fit.
    void Arrange(RectTransform strip, Image template)
    {
        if (strip.TryGetComponent<LayoutGroup>(out var group)) group.enabled = false;   // placed here instead
        var icons = new System.Collections.Generic.List<RectTransform>();
        foreach (RectTransform child in strip) if (child != template.transform && child.gameObject.activeSelf) icons.Add(child);
        if (icons.Count == 0) return;
        var size = template.rectTransform.rect.size;
        var area = strip.rect.size - stripPadding * 2f;
        int fits = Mathf.Max(1, Mathf.FloorToInt((area.x + stripGap) / (size.x + stripGap)));
        int rows = Mathf.Clamp(Mathf.CeilToInt(icons.Count / (float)fits), 1, Mathf.Max(1, stripRows));
        int perRow = Mathf.Max(fits, Mathf.CeilToInt(icons.Count / (float)rows));   // rows fill one by one until all are full
        float step = perRow <= fits ? size.x + stripGap : (area.x - size.x) / Mathf.Max(1, perRow - 1);
        float rowStep = size.y + stripGap;
        float top = (rows * size.y + (rows - 1) * stripGap) * .5f;   // the rows in use, centred
        for (int i = 0; i < icons.Count; i++)
        {
            int row = i / perRow, column = i % perRow;
            var icon = icons[i];
            icon.anchorMin = icon.anchorMax = new Vector2(0f, .5f);
            icon.pivot = new Vector2(.5f, .5f);
            icon.sizeDelta = size;
            icon.anchoredPosition = new Vector2(stripPadding.x + size.x * .5f + column * step, top - size.y * .5f - row * rowStep);
        }
    }

    public void Hide()
    {
        if (!open) return;
        open = false;
        if (counting != null) { StopCoroutine(counting); counting = null; }
        ClearEffects();
        if (showcaseRays != null) showcaseRays.transform.DOKill();
        fade?.Kill();
        if (root != null) root.SetActive(false);
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        if (RunManager.HasInstance) RunManager.Instance.RecapClosed();
    }

    void NewRun()
    {
        var level = RunManager.HasInstance ? RunManager.Instance.Level : null;
        Hide();
        if (Loader != null && level != null) Loader.Load(level);
        else Loader?.ShowSelection();
    }

    void Adventures()
    {
        Hide();
        if (RunManager.HasInstance) RunManager.Instance.Abandon();
        Loader?.ShowSelection("Choose your adventure");
    }

    // A win plays the ending (a new morning); a death puts the player back in the room, standing by the table,
    // with no wake-up.
    static void WakeUp(bool victory)
    {
        var world = WorldManager.Instance;
        if (victory && world.ending != null) { world.ending.Play(); return; }
        Loader?.ReturnToRoom();
    }

    void ReturnToRoom()
    {
        bool wake = wakes, victory = won;
        Hide();
        if (RunManager.HasInstance) RunManager.Instance.Abandon();
        if (wake) WakeUp(victory);
        else Loader?.ReturnToRoom();
    }
}

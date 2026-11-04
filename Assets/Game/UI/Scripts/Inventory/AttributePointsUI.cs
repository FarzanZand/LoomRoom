using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The level's attribute points in the middle of the Tab screen, between the character panel and the bag: while points are
// left in a run (AdventurerProgress.UnspentPoints, Level up Reward on Points) the four attributes stand there to click
// (or 1-4), each with its score now and after a point and what a point gives (ProgressionManager.AttributeEffects); it
// comes in a moment after the side panels, and goes once the last point is spent or the bag closes. Four looks, one
// shown (look; the user picks): stone rows, a ledger page, stone slots, a strip of slots. Each is authored under its own
// root by AttributePointsSetup.Build() (eval); this only fills and moves them. Moves are short and quiet (eases with at
// most a small settle, no bounce, rays or confetti), and the sounds are tabletop ones (paper, a book, a knock, a pen). The points left are only ever said in the title (the user: no tray of tokens).
public class AttributePointsUI : MonoBehaviour
{
    public enum Look { StoneRows = 0, Ledger = 1, StoneSlots = 2, Strip = 3 }

    [Serializable] public class Choice
    {
        public MetaAttribute attribute;
        public RectTransform root;
        public Image hit, icon, edge, strike;
        public TMP_Text label, from, to, gains, key;
    }

    [Serializable] public class Design
    {
        public Look look;
        public RectTransform root;
        public CanvasGroup group;
        public TMP_Text title;
        public Choice[] choices = Array.Empty<Choice>();
        [Tooltip("Strip: one line under the slots saying what a point in the hovered one gives.")] public TMP_Text detail;
        [Tooltip("Stone slots: one ember, switched off; a few rise from the slot on a pick.")] public Image spark;
        public AudioData appear, hover, pick, leave, extra;
        public Color ink = Color.white, gold = Color.white, faded = Color.gray;
    }

    public Look look;
    public Design[] designs = Array.Empty<Design>();
    [Min(0f)] public float enterDelay = .12f, enterSeconds = .3f, choiceGap = .05f, leaveSeconds = .3f, holdAfterLast = .5f;
    [Min(0f)] public float pickGap = .2f;

    public static bool Showing { get; private set; }
    // The panel is in the scene and the bag is open: the character panel's + buttons stand down for it.
    public static bool Present { get; private set; }

    InventoryUI inventory;
    AdventurerProgress progress;
    Design d;
    enum State { Hidden, Shown, Leaving }
    State state;
    Sequence moving;
    int hovered = -1, shownPoints = -1;
    float lastPick = -10f, emptySince = -1f;
    Vector2 home;

    void Awake()
    {
        inventory = GetComponentInParent<InventoryUI>(true);
        foreach (var x in designs) if (x?.root != null) x.root.gameObject.SetActive(false);
        Pick();
    }

    void OnEnable() => Present = true;

    void OnDisable() { Present = false; moving?.Kill(); Showing = false; state = State.Hidden; if (d?.root != null) d.root.gameObject.SetActive(false); }

    // The look to show (switching it in Play mode takes effect the next time the panel comes in).
    void Pick()
    {
        d = Array.Find(designs, x => x != null && x.look == look && x.root != null);
        if (d != null) home = d.root.anchoredPosition;
    }

    bool Wanted()
    {
        if (inventory == null || !inventory.IsOpen || ProgressionManager.CardsOnLevelUp) return false;
        if (progress == null && PlayerManager.HasInstance)
        {
            var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if (table != null) progress = table.GetComponent<AdventurerProgress>();
        }
        return progress != null && progress.InRun && progress.UnspentPoints > 0;
    }

    void Update()
    {
        bool open = inventory != null && inventory.IsOpen;
        if (state == State.Hidden)
        {
            if (Wanted()) Enter();
            return;
        }
        if (!open) { Leave(true); return; }
        if (state != State.Shown) return;
        if (progress.UnspentPoints <= 0)
        {
            if (emptySince < 0f) emptySince = Time.unscaledTime;
            if (Time.unscaledTime - emptySince >= holdAfterLast) Leave(false);
            return;
        }
        emptySince = -1f;
        if (shownPoints != progress.UnspentPoints) FillTitle();
        Hover();
        Keys();
    }

    // ── In and out ──────────────────────────────────────────────

    void Enter()
    {
        Pick();
        if (d == null) return;
        state = State.Shown; Showing = true; emptySince = -1f; hovered = -1;
        moving?.Kill();
        d.root.gameObject.SetActive(true);
        Fill();
        d.group.alpha = 0f;
        moving = DOTween.Sequence().SetUpdate(true);
        float t = enterDelay;
        switch (d.look)
        {
            case Look.Ledger:
                // The page is slid up onto the table, turning straight as it lands.
                d.root.anchoredPosition = home + new Vector2(0f, -60f);
                d.root.localRotation = Quaternion.Euler(0f, 0f, -4f);
                moving.Insert(t, d.group.DOFade(1f, enterSeconds * .6f));
                moving.Insert(t, d.root.DOAnchorPos(home, enterSeconds).SetEase(Ease.OutCubic));
                moving.Insert(t, d.root.DOLocalRotate(new Vector3(0f, 0f, -1f), enterSeconds).SetEase(Ease.OutCubic));
                break;
            default:
                d.root.anchoredPosition = home + new Vector2(0f, -16f);
                moving.Insert(t, d.group.DOFade(1f, enterSeconds * .7f));
                moving.Insert(t, d.root.DOAnchorPos(home, enterSeconds).SetEase(Ease.OutCubic));
                break;
        }
        for (int i = 0; i < d.choices.Length; i++)
        {
            var c = d.choices[i];
            var cg = c.root.GetComponent<CanvasGroup>();
            Vector2 at = c.root.anchoredPosition;
            Vector2 from = d.look == Look.StoneRows ? new Vector2(-22f, 0f) : new Vector2(0f, -10f);
            float when = t + .06f + choiceGap * i;
            if (cg != null) { cg.alpha = 0f; moving.Insert(when, cg.DOFade(1f, enterSeconds * .8f)); }
            if (d.look == Look.StoneSlots || d.look == Look.Strip)
            {
                c.root.localScale = Vector3.one * .94f;
                moving.Insert(when, c.root.DOScale(1f, enterSeconds).SetEase(Ease.OutCubic));
            }
            else
            {
                c.root.anchoredPosition = at + from;
                moving.Insert(when, c.root.DOAnchorPos(at, enterSeconds).SetEase(Ease.OutCubic));
            }
        }
        Play(d.appear, 1f, t);
    }

    // instantly: the bag closed with it (its own panels slide away), so this only fades fast.
    void Leave(bool instantly)
    {
        if (state == State.Hidden || d == null) return;
        if (state == State.Leaving && !instantly) return;
        state = State.Leaving;
        moving?.Kill();
        moving = DOTween.Sequence().SetUpdate(true);
        float s = instantly ? .12f : leaveSeconds;
        moving.Join(d.group.DOFade(0f, s));
        if (!instantly)
        {
            moving.Join(d.root.DOAnchorPos(home + new Vector2(0f, d.look == Look.Ledger ? -50f : -12f), s).SetEase(Ease.InCubic));
            Play(d.leave, 1f);
        }
        moving.OnComplete(() => { d.root.gameObject.SetActive(false); d.root.anchoredPosition = home; state = State.Hidden; Showing = false; scripted = false; });
    }

    // ── Filling ─────────────────────────────────────────────────

    void Fill()
    {
        FillTitle();
        for (int i = 0; i < d.choices.Length; i++) FillChoice(d.choices[i], i);
        if (d.detail != null) d.detail.text = "";
    }

    void FillTitle()
    {
        int n = progress != null ? progress.UnspentPoints : 0;
        shownPoints = n;
        if (n <= 0) return;   // the last point spent: the title keeps its count while the panel goes
        if (d.title != null) d.title.text = d.look == Look.Ledger ? (n == 1 ? "1 point to spend" : $"{n} points to spend") : (n == 1 ? "1 POINT TO SPEND" : $"{n} POINTS TO SPEND");
    }

    void FillChoice(Choice c, int i)
    {
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var track = meta != null ? meta.Track(c.attribute) : null;
        int now = progress != null ? progress.Score(c.attribute) : 8;
        string name = track != null ? track.displayName : c.attribute.ToString();
        if (c.label != null) c.label.text = d.look == Look.Ledger ? name : name.ToUpperInvariant();
        if (c.icon != null && track != null && track.icon != null) c.icon.sprite = track.icon;
        if (c.from != null) { c.from.text = now.ToString(); c.from.color = d.ink; c.from.rectTransform.localScale = Vector3.one; }
        if (c.to != null)
        {
            c.to.text = d.look == Look.Ledger ? (now + 1).ToString() : $"<color=#{ColorUtility.ToHtmlStringRGBA(d.faded)}>></color> {now + 1}";
            c.to.color = d.gold;
            c.to.alpha = d.look == Look.Ledger ? 0f : 1f;
        }
        if (c.gains != null) c.gains.text = ProgressionManager.AttributeEffects(track, 1, d.look != Look.Strip);   // a bonus a line (the user)
        if (c.key != null) c.key.text = (i + 1).ToString();
        if (c.edge != null) c.edge.color = new Color(d.gold.r, d.gold.g, d.gold.b, 0f);
        if (c.strike != null) { c.strike.fillAmount = 0f; c.strike.color = new Color(d.ink.r, d.ink.g, d.ink.b, 1f); }
    }

    // ── Choosing ────────────────────────────────────────────────

    void Hover()
    {
        var mouse = Mouse.current;
        int over = scripted ? hovered : -1;
        if (mouse != null && !scripted)
        {
            Vector2 at = mouse.position.ReadValue();
            for (int i = 0; i < d.choices.Length; i++)
                if (d.choices[i].hit != null && RectTransformUtility.RectangleContainsScreenPoint(d.choices[i].hit.rectTransform, at, null)) { over = i; break; }
            if (over >= 0 && mouse.leftButton.wasPressedThisFrame) Choose(over);
        }
        if (over != hovered)
        {
            hovered = over;
            if (over >= 0) Play(d.hover, 1f);
            if (d.detail != null) d.detail.text = over >= 0 ? Detail(d.choices[over]) : "";
        }
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < d.choices.Length; i++)
        {
            var c = d.choices[i];
            if (c.edge == null) continue;
            var col = c.edge.color;
            float want = i == hovered ? .55f : 0f;
            if (col.a > want) col.a = Mathf.MoveTowards(col.a, want, dt * 1.6f); else col.a = Mathf.MoveTowards(col.a, want, dt * 6f);
            c.edge.color = col;
            if (d.look == Look.Ledger && c.label != null) c.label.fontStyle = i == hovered ? FontStyles.Underline : FontStyles.Normal;
        }
    }

    void Keys()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (k.digit1Key.wasPressedThisFrame) Choose(0);
        else if (k.digit2Key.wasPressedThisFrame) Choose(1);
        else if (k.digit3Key.wasPressedThisFrame) Choose(2);
        else if (k.digit4Key.wasPressedThisFrame) Choose(3);
    }

    // For the recordings: the same as a click on choice i.
    // A recording or a test drives it instead of the mouse (no cursor shows in a recording) until the panel goes.
    bool scripted;
    public void TestChoose(int i) { scripted = true; Choose(i); }
    public void TestHover(int i) { scripted = true; if (i != hovered && i >= 0) Play(d?.hover, 1f); hovered = i; if (d?.detail != null) d.detail.text = i >= 0 ? Detail(d.choices[i]) : ""; }

    // The Strip's line under the slots: what a point in this attribute gives.
    static string Detail(Choice c)
    {
        var track = ProgressionManager.HasInstance ? ProgressionManager.Instance.Track(c.attribute) : null;
        return ProgressionManager.AttributeEffects(track, 1);
    }
    public void TestLook(Look l) { look = l; if (state != State.Hidden) { moving?.Kill(); d.root.gameObject.SetActive(false); state = State.Hidden; Showing = false; } }

    void Choose(int i)
    {
        if (state != State.Shown || i < 0 || i >= d.choices.Length || Time.unscaledTime - lastPick < pickGap) return;
        var c = d.choices[i];
        int before = progress.Score(c.attribute);
        if (!progress.SpendPoint(c.attribute, false)) return;
        lastPick = Time.unscaledTime;
        Play(d.pick, 1f);
        var seq = DOTween.Sequence().SetUpdate(true);
        switch (d.look)
        {
            case Look.Ledger: Write(seq, c, i, before); break;
            case Look.StoneSlots: case Look.Strip: Press(seq, c, i); break;
            default: Flash(seq, c, i, 0f); break;
        }
        FillTitle();
        if (d.detail != null && i == hovered) d.detail.text = Detail(c);
    }

    // Stone rows: a gold edge flashes, the row is pushed a little to the side and settles, and the old score turns into
    // the new one as it grows a touch and shrinks back.
    void Flash(Sequence seq, Choice c, int i, float at)
    {
        if (c.edge != null) { var e = c.edge.color; e.a = 1f; c.edge.color = e; seq.Insert(at, c.edge.DOFade(0f, .55f).SetEase(Ease.OutQuad)); }
        var home = c.root.anchoredPosition;
        seq.Insert(at, c.root.DOAnchorPos(home + new Vector2(6f, 0f), .07f).SetEase(Ease.OutQuad));
        seq.Insert(at + .07f, c.root.DOAnchorPos(home, .22f).SetEase(Ease.OutCubic));
        Tick(seq, c, i, at + .05f);
    }

    void Tick(Sequence seq, Choice c, int i, float at)
    {
        if (c.from == null) return;
        var r = c.from.rectTransform;
        seq.InsertCallback(at, () => { FillChoice(c, i); c.from.color = d.gold; r.localScale = Vector3.one * 1.22f; });
        seq.Insert(at, r.DOScale(1f, .28f).SetEase(Ease.OutCubic));
        seq.Insert(at + .1f, c.from.DOColor(d.ink, .5f));
    }

    // Ledger: the old number is struck through with a line of ink, the new one written beside it with a pen scratch;
    // a moment later the row is written fresh (the new score, and the one after it waiting).
    void Write(Sequence seq, Choice c, int i, int before)
    {
        if (c.strike != null) seq.Insert(0f, c.strike.DOFillAmount(1f, .13f).SetEase(Ease.OutQuad));
        if (c.to != null)
        {
            c.to.text = (before + 1).ToString();
            var r = c.to.rectTransform; var at = r.anchoredPosition;
            r.anchoredPosition = at + new Vector2(-4f, 0f);
            seq.Insert(.12f, c.to.DOFade(1f, .2f));
            seq.Insert(.12f, r.DOAnchorPos(at, .2f).SetEase(Ease.OutQuad));
        }
        seq.InsertCallback(.1f, () => Play(d.extra, 1f));
        if (progress.UnspentPoints > 0)
        {
            seq.Insert(.85f, c.root.GetComponent<CanvasGroup>().DOFade(.0f, .12f));
            seq.InsertCallback(.97f, () => FillChoice(c, i));
            seq.Insert(.97f, c.root.GetComponent<CanvasGroup>().DOFade(1f, .18f));
        }
    }

    // Stone slots: the slot is pressed in and comes back, a few embers rise from it and a warm glow fades.
    void Press(Sequence seq, Choice c, int i)
    {
        Play(d.extra, 1f);
        seq.Insert(0f, c.root.DOScale(.94f, .06f).SetEase(Ease.OutQuad));
        seq.Insert(.06f, c.root.DOScale(1f, .24f).SetEase(Ease.OutCubic));
        if (c.edge != null) { var e = c.edge.color; e.a = 1f; c.edge.color = e; seq.Insert(0f, c.edge.DOFade(0f, .6f)); }
        Tick(seq, c, i, .05f);
        if (d.spark == null) return;
        for (int s = 0; s < 7; s++)
        {
            // Behind the words, from around the icon (over the text they read as clutter).
            var spark = Instantiate(d.spark, c.root);
            spark.gameObject.SetActive(true);
            spark.transform.SetSiblingIndex(c.hit != null ? c.hit.transform.GetSiblingIndex() + 1 : 0);
            var r = spark.rectTransform;
            Vector2 from = c.icon != null ? c.icon.rectTransform.anchoredPosition : Vector2.zero;
            float x = UnityEngine.Random.Range(-46f, 46f);
            r.anchoredPosition = from + new Vector2(x, UnityEngine.Random.Range(-14f, 10f));
            float rise = UnityEngine.Random.Range(28f, 52f), life = UnityEngine.Random.Range(.45f, .75f), delay = UnityEngine.Random.Range(0f, .12f);
            spark.color = new Color(1f, UnityEngine.Random.Range(.55f, .8f), .3f, 0f);
            seq.Insert(delay, spark.DOFade(1f, .06f));
            seq.Insert(delay, r.DOAnchorPos(r.anchoredPosition + new Vector2(x * .25f, rise), life).SetEase(Ease.OutQuad));
            seq.Insert(delay + life * .45f, spark.DOFade(0f, life * .55f));
            seq.InsertCallback(delay + life + .02f, () => { if (spark != null) Destroy(spark.gameObject); });
        }
    }

    // ── Sound ───────────────────────────────────────────────────

    static void Play(AudioData data, float pitch, float delay = 0f, float volume = 1f)
    {
        if (data == null || !AudioManager.HasInstance) return;
        if (delay > 0f) { DOVirtual.DelayedCall(delay, () => Play(data, pitch, 0f, volume), true); return; }
        var clip = data.GetClip();
        if (clip != null) AudioManager.Instance.PlayUI(clip, data.volume * volume, data.RandomPitch() * pitch);
    }
}

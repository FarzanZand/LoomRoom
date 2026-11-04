using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The level's attribute points in the middle of the Tab screen, between the character panel and the bag: while points are
// left in a run (AdventurerProgress.UnspentPoints, Level up Reward on Points) a stone panel stands there with a row per
// attribute to click (or 1-4), each with its score now and after a point and a line per bonus
// (ProgressionManager.AttributeEffects); the points left are only said in its title. It comes in a moment after the
// side panels, and goes once the last point is spent or the bag closes. Built by AttributePointsSetup.Build() (eval);
// this only fills and moves it. Moves are short and quiet: eases with no bounce; a pick flashes the row's gold edge,
// nudges it aside and ticks its score up, with the regular button click.
public class AttributePointsUI : MonoBehaviour
{
    [Serializable] public class Choice
    {
        public MetaAttribute attribute;
        public RectTransform root;
        public Image hit, icon, edge;
        public TMP_Text label, from, to, gains, key;
    }

    public RectTransform panel;
    public CanvasGroup group;
    public TMP_Text title;
    public Choice[] choices = Array.Empty<Choice>();
    public AudioData appear, hover, leave;
    [Tooltip("The UI Library key a pick plays (the regular button click, the user).")] public string pickKey = AttributeCellUI.ClickKey;
    public Color ink = Color.white, gold = Color.white, faded = Color.gray;
    [Min(0f)] public float enterDelay = .12f, enterSeconds = .3f, choiceGap = .05f, leaveSeconds = .3f, holdAfterLast = .5f;
    [Min(0f)] public float pickGap = .2f;

    public static bool Showing { get; private set; }
    // The panel is in the scene and the bag is open: the character panel's + buttons stand down for it.
    public static bool Present { get; private set; }

    InventoryUI inventory;
    AdventurerProgress progress;
    enum State { Hidden, Shown, Leaving }
    State state;
    Sequence moving;
    int hovered = -1, shownPoints = -1;
    float lastPick = -10f, emptySince = -1f;
    Vector2 home;
    // A recording or a test drives it instead of the mouse (no cursor shows in a recording) until the panel goes.
    bool scripted;

    void Awake()
    {
        inventory = GetComponentInParent<InventoryUI>(true);
        if (panel != null) { home = panel.anchoredPosition; panel.gameObject.SetActive(false); }
    }

    void OnEnable() => Present = true;

    void OnDisable() { Present = false; moving?.Kill(); Showing = false; state = State.Hidden; if (panel != null) panel.gameObject.SetActive(false); }

    bool Wanted()
    {
        if (panel == null || inventory == null || !inventory.IsOpen || ProgressionManager.CardsOnLevelUp) return false;
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

    // The panel fades in rising a little, and the rows come in one after another from the left.
    void Enter()
    {
        state = State.Shown; Showing = true; emptySince = -1f; hovered = -1;
        moving?.Kill();
        panel.gameObject.SetActive(true);
        Fill();
        group.alpha = 0f;
        panel.anchoredPosition = home + new Vector2(0f, -16f);
        moving = DOTween.Sequence().SetUpdate(true);
        float t = enterDelay;
        moving.Insert(t, group.DOFade(1f, enterSeconds * .7f));
        moving.Insert(t, panel.DOAnchorPos(home, enterSeconds).SetEase(Ease.OutCubic));
        for (int i = 0; i < choices.Length; i++)
        {
            var c = choices[i];
            var cg = c.root.GetComponent<CanvasGroup>();
            Vector2 at = c.root.anchoredPosition;
            float when = t + .06f + choiceGap * i;
            if (cg != null) { cg.alpha = 0f; moving.Insert(when, cg.DOFade(1f, enterSeconds * .8f)); }
            c.root.anchoredPosition = at + new Vector2(-22f, 0f);
            moving.Insert(when, c.root.DOAnchorPos(at, enterSeconds).SetEase(Ease.OutCubic));
        }
        Play(appear, t);
    }

    // instantly: the bag closed with it (its own panels slide away), so this only fades fast.
    void Leave(bool instantly)
    {
        if (state == State.Hidden) return;
        if (state == State.Leaving && !instantly) return;
        state = State.Leaving;
        moving?.Kill();
        moving = DOTween.Sequence().SetUpdate(true);
        float s = instantly ? .12f : leaveSeconds;
        moving.Join(group.DOFade(0f, s));
        if (!instantly)
        {
            moving.Join(panel.DOAnchorPos(home + new Vector2(0f, -12f), s).SetEase(Ease.InCubic));
            Play(leave);
        }
        moving.OnComplete(() => { panel.gameObject.SetActive(false); panel.anchoredPosition = home; state = State.Hidden; Showing = false; scripted = false; });
    }

    // ── Filling ─────────────────────────────────────────────────

    void Fill()
    {
        FillTitle();
        for (int i = 0; i < choices.Length; i++) FillChoice(choices[i], i);
    }

    void FillTitle()
    {
        int n = progress != null ? progress.UnspentPoints : 0;
        shownPoints = n;
        if (n <= 0) return;   // the last point spent: the title keeps its count while the panel goes
        if (title != null) title.text = n == 1 ? "1 POINT TO SPEND" : $"{n} POINTS TO SPEND";
    }

    void FillChoice(Choice c, int i)
    {
        var meta = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        var track = meta != null ? meta.Track(c.attribute) : null;
        int now = progress != null ? progress.Score(c.attribute) : 8;
        if (c.label != null) c.label.text = (track != null ? track.displayName : c.attribute.ToString()).ToUpperInvariant();
        if (c.icon != null && track != null && track.icon != null) c.icon.sprite = track.icon;
        if (c.from != null) { c.from.text = now.ToString(); c.from.color = ink; c.from.rectTransform.localScale = Vector3.one; }
        if (c.to != null) { c.to.text = $"<color=#{ColorUtility.ToHtmlStringRGBA(faded)}>></color> {now + 1}"; c.to.color = gold; }
        if (c.gains != null) c.gains.text = ProgressionManager.AttributeEffects(track, 1, true);   // a bonus a line (the user)
        if (c.key != null) c.key.text = (i + 1).ToString();
        if (c.edge != null) c.edge.color = new Color(gold.r, gold.g, gold.b, 0f);
    }

    // ── Choosing ────────────────────────────────────────────────

    void Hover()
    {
        var mouse = Mouse.current;
        int over = scripted ? hovered : -1;
        if (mouse != null && !scripted)
        {
            Vector2 at = mouse.position.ReadValue();
            for (int i = 0; i < choices.Length; i++)
                if (choices[i].hit != null && RectTransformUtility.RectangleContainsScreenPoint(choices[i].hit.rectTransform, at, null)) { over = i; break; }
            if (over >= 0 && mouse.leftButton.wasPressedThisFrame) Choose(over);
        }
        if (over != hovered)
        {
            hovered = over;
            if (over >= 0) Play(hover);
        }
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < choices.Length; i++)
        {
            var c = choices[i];
            if (c.edge == null) continue;
            var col = c.edge.color;
            float want = i == hovered ? .55f : 0f;
            col.a = Mathf.MoveTowards(col.a, want, dt * (col.a > want ? 1.6f : 6f));
            c.edge.color = col;
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

    public void TestChoose(int i) { scripted = true; Choose(i); }
    public void TestHover(int i) { scripted = true; if (i != hovered && i >= 0) Play(hover); hovered = i; }

    // A gold edge flashes, the row is pushed a little to the side and settles, and the old score turns into the new one
    // as it grows a touch and shrinks back.
    void Choose(int i)
    {
        if (state != State.Shown || i < 0 || i >= choices.Length || Time.unscaledTime - lastPick < pickGap) return;
        var c = choices[i];
        if (!progress.SpendPoint(c.attribute, false)) return;
        lastPick = Time.unscaledTime;
        if (!string.IsNullOrEmpty(pickKey) && AudioManager.HasInstance) AudioManager.Instance.PlayUI(pickKey);
        var seq = DOTween.Sequence().SetUpdate(true);
        if (c.edge != null) { var e = c.edge.color; e.a = 1f; c.edge.color = e; seq.Insert(0f, c.edge.DOFade(0f, .55f).SetEase(Ease.OutQuad)); }
        var at = c.root.anchoredPosition;
        seq.Insert(0f, c.root.DOAnchorPos(at + new Vector2(6f, 0f), .07f).SetEase(Ease.OutQuad));
        seq.Insert(.07f, c.root.DOAnchorPos(at, .22f).SetEase(Ease.OutCubic));
        if (c.from != null)
        {
            var r = c.from.rectTransform;
            seq.InsertCallback(.05f, () => { FillChoice(c, i); c.from.color = gold; r.localScale = Vector3.one * 1.22f; });
            seq.Insert(.05f, r.DOScale(1f, .28f).SetEase(Ease.OutCubic));
            seq.Insert(.15f, c.from.DOColor(ink, .5f));
        }
        FillTitle();
    }

    // ── Sound ───────────────────────────────────────────────────

    static void Play(AudioData data, float delay = 0f)
    {
        if (data == null || !AudioManager.HasInstance) return;
        if (delay > 0f) { DOVirtual.DelayedCall(delay, () => Play(data), true); return; }
        var clip = data.GetClip();
        if (clip != null) AudioManager.Instance.PlayUI(clip, data.volume, data.RandomPitch());
    }
}

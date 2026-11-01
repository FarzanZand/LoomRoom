using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The level-up cards (ProgressionManager > Level up Reward on Cards): while the table player has an attribute point
// left in a run (AdventurerProgress.UnspentPoints), no enemy is chasing or attacking them and no boss fight is on, the
// Dungeon Master puts up four panels, one per attribute, each with its score now and after and what a point there gives
// (ProgressionManager.AttributeEffects, so the panels say what the sheet and the character panel say). They pop up one
// after another (a fade, a small rise and a soft overshoot in size; no turning or spinning, the user, 2026-11-02) while
// he says one of his deal lines. With
// ProgressionManager's Auto Popup they come by themselves and the game waits meanwhile (Menu state, time stopped);
// without it a prompt says "Press R to level up" and R brings them up while the game goes on (Menu state for the
// cursor, time running; Esc puts them away, the point kept). Pick one with the mouse, 1-4, or the arrows and Enter; it
// takes the point with a small pulse and fades away rising, while the others shrink a little and fade. With two points
// left they come up again. A prefab (UI/Prefabs/Progression/Level up cards, made by LevelUpCardsSetup.Build())
// that ProgressionManager puts on the HUD canvas (ProgressionManager.levelUpCards).
public class LevelUpCardsUI : MonoBehaviour
{
    // The cards' art, colours and sounds (art from Tools/level_up_cards.py, sounds in Audio/Data/Level up cards).
    [Serializable]
    public class CardArt
    {
        public Sprite face, divider, keyFrame;
        public Color name = Color.white, ink = Color.white, gain = Color.white, faded = new(1f, 1f, 1f, .6f), glow = new(1f, .85f, .4f, .8f);
        public Material titleMaterial, bodyMaterial;
        [Range(0f, 1f), Tooltip("How dark the screen goes behind the cards.")] public float dim = .6f;
        [Tooltip("Played as each panel pops up, a little higher each time.")] public AudioData deal;
        public AudioData hover, pick, leave;
    }

    [SerializeField] CanvasGroup group;
    [Tooltip("\"Press R to level up\", shown while a point waits and Auto Popup is off.")]
    [SerializeField] CanvasGroup prompt;
    [SerializeField] TMPro.TMP_Text promptText;
    [SerializeField] string promptLine = "Press <color=#FFFFFF>R</color> to level up";
    [SerializeField] Image backdrop;
    [Tooltip("A line over the cards (none now: the Dungeon Master's deal line says it).")]
    [SerializeField] TMPro.TMP_Text title;
    [SerializeField] LevelUpCardUI[] cards = new LevelUpCardUI[4];
    [InlineProperty, HideLabel, FoldoutGroup("Art")] public CardArt art = new();
    [Tooltip("What the Dungeon Master says as he deals (one at random, each time the cards come up).")]
    public List<DungeonMaster.Line> dealLines = new();

    [FoldoutGroup("Timing"), Min(0f), Tooltip("Seconds after the point is earned before the cards come (the LEVEL UP banner lands first).")] public float afterLevelUp = .9f;
    [FoldoutGroup("Timing"), Min(.05f), Tooltip("Seconds each panel takes to pop up, and between one panel and the next.")] public float dealSeconds = .32f, dealGap = .07f;
    [FoldoutGroup("Timing"), Min(0f), Tooltip("Seconds after the last card is in before a pick counts, so a click meant for a swing doesn't take one.")] public float pickDelay = .25f;
    [FoldoutGroup("Timing"), Min(.05f), Tooltip("Seconds the panels take to go once one is picked.")] public float leaveSeconds = .45f;
    [FoldoutGroup("Hover"), Min(0f)] public float hoverLift = 26f, hoverScale = 1.07f;
    [FoldoutGroup("Hover"), Min(.1f)] public float hoverSpeed = 14f;

    static readonly MetaAttribute[] order = { MetaAttribute.Strength, MetaAttribute.Dexterity, MetaAttribute.Constitution, MetaAttribute.Intelligence };

    enum Phase { Closed, Dealing, Choosing, Leaving }
    Phase phase;
    float phaseStart, pointSince = -1f, timeScaleBefore = 1f, promptShown;
    bool paused;
    int hovered = -1, picked = -1, keyboard = -1;
    readonly float[] hover = new float[4], landed = new float[4];
    readonly Vector2[] slots = new Vector2[4];
    AdventurerProgress progress;

    public static bool Open { get; private set; }
    // The time scale while the cards hold the game. 0 stops it; a recording sets 1 (the Unity Recorder only takes a frame
    // while game time moves, so a stopped game films as one still frame).
    public static float HeldTimeScale = 0f;

    void Awake()
    {
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] != null) { cards[i].owner = this; cards[i].index = i; slots[i] = ((RectTransform)cards[i].transform).anchoredPosition; }
        Hide();
    }

    void OnEnable() { if (InputManager.HasInstance) { InputManager.Instance.NavigatePressed += Navigate; InputManager.Instance.SubmitPressed += Submit; InputManager.Instance.CancelPressed += PutAway; } }
    void OnDisable()
    {
        if (InputManager.HasInstance) { InputManager.Instance.NavigatePressed -= Navigate; InputManager.Instance.SubmitPressed -= Submit; InputManager.Instance.CancelPressed -= PutAway; }
        if (Open) Close();
    }

    void Hide()
    {
        if (group != null) { group.alpha = 0f; group.blocksRaycasts = false; group.interactable = false; }
        phase = Phase.Closed;
    }

    AdventurerProgress Progress()
    {
        if (progress == null && PlayerManager.HasInstance)
        {
            var table = PlayerManager.Instance.GetPlayer(PlayerKind.Table);
            if (table != null) progress = table.GetComponent<AdventurerProgress>();
        }
        return progress;
    }

    // A point waiting, the table player exploring and no boss fight on; with Auto Popup also no enemy after them (on R
    // the player decides when).
    bool Ready()
    {
        var p = Progress();
        if (p == null || !p.InRun || p.UnspentPoints <= 0 || !ProgressionManager.CardsOnLevelUp) return false;
        if (!PlayerManager.HasInstance || PlayerManager.Instance.ActiveKind != PlayerKind.Table) return false;
        var player = PlayerManager.Instance.Active;
        if (player == null || !player.IsAlive) return false;
        if (GameManager.HasInstance && !GameManager.Instance.GameplayActive) return false;
        // Never during a boss fight on the stage: they wait for it to end.
        if (BossStageEncounter.Current != null && BossStageEncounter.Current.Fighting) return false;
        if (!ProgressionManager.CardsAutoPopup) return true;
        foreach (var brain in EnemyBrain.Active)
            if (brain != null && brain.Character != null && brain.Character.IsAlive && brain.IsAlerted && brain.Perception != null && brain.Perception.Target == player) return false;
        return true;
    }

    void Update()
    {
        if (phase == Phase.Closed)
        {
            bool ready = Ready();
            if (!ready) pointSince = -1f;
            else if (pointSince < 0f) pointSince = Time.unscaledTime;
            bool waited = ready && Time.unscaledTime - pointSince >= afterLevelUp;
            bool auto = ProgressionManager.CardsAutoPopup;
            ShowPrompt(waited && !auto);
            if (waited && (auto || Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)) Deal();
            return;
        }
        ShowPrompt(false);
        float dt = Time.unscaledDeltaTime, t = Time.unscaledTime - phaseStart;
        if (phase == Phase.Dealing)
        {
            for (int i = 0; i < cards.Length; i++) PoseDealing(i, t - i * dealGap);
            if (t >= (cards.Length - 1) * dealGap + dealSeconds + pickDelay) { phase = Phase.Choosing; phaseStart = Time.unscaledTime; }
        }
        else if (phase == Phase.Choosing)
        {
            KeysPressed();
            for (int i = 0; i < cards.Length; i++)
            {
                bool on = i == hovered || i == keyboard && hovered < 0;
                hover[i] = Mathf.MoveTowards(hover[i], on ? 1f : 0f, dt * hoverSpeed * .5f);
                PoseResting(i, Ease.OutCubic(hover[i]), Time.unscaledTime);
            }
        }
        else if (phase == Phase.Leaving)
        {
            for (int i = 0; i < cards.Length; i++) PoseLeaving(i, t);
            if (t >= leaveSeconds + (cards.Length - 1) * .04f) Finish();
        }
    }

    // ── Opening and closing ───────────────────────────────────────────

    void Deal()
    {
        var p = Progress();
        var meta = ProgressionManager.Instance;
        for (int i = 0; i < cards.Length; i++) Fill(cards[i], order[i], p, meta, i);
        if (backdrop != null) backdrop.color = new Color(0f, 0f, 0f, 0f);
        if (title != null) { title.color = art.name; title.alpha = 0f; }
        if (group != null) { group.alpha = 1f; group.blocksRaycasts = true; group.interactable = true; }
        hovered = keyboard = picked = -1;
        Array.Clear(hover, 0, 4); Array.Clear(landed, 0, 4);
        for (int i = 0; i < cards.Length; i++) PoseDealing(i, -1f);
        if (!Open)
        {
            Open = true;
            if (GameManager.HasInstance) GameManager.Instance.Push(GameState.Menu);
            paused = ProgressionManager.CardsAutoPopup;
            timeScaleBefore = Time.timeScale;
            if (paused) Time.timeScale = HeldTimeScale;
            // He deals them: one of his lines as they come, shown over the cards (the feed hides behind menus otherwise).
            if (dealLines.Count > 0)
            {
                DungeonMaster.OverMenus = true;
                DungeonMaster.Say(dealLines[UnityEngine.Random.Range(0, dealLines.Count)]);
            }
        }
        phase = Phase.Dealing;
        phaseStart = Time.unscaledTime;
    }

    void Fill(LevelUpCardUI card, MetaAttribute attribute, AdventurerProgress p, ProgressionManager meta, int i)
    {
        if (card == null) return;
        var track = meta != null ? meta.Track(attribute) : null;
        if (card.face != null) { card.face.sprite = art.face; card.face.color = Color.white; }
        if (card.back != null) card.back.enabled = false;
        if (card.divider != null) { card.divider.sprite = art.divider; card.divider.enabled = art.divider != null; }
        if (card.keyFrame != null) { card.keyFrame.sprite = art.keyFrame; card.keyFrame.enabled = art.keyFrame != null; }
        // The glow is the card's own shape a little larger, in the glow colour.
        if (card.glow != null) { card.glow.sprite = art.face; card.glow.color = WithAlpha(art.glow, 0f); }
        if (card.icon != null) { card.icon.sprite = track != null ? track.icon : null; card.icon.enabled = card.icon.sprite != null; }
        int now = p != null ? p.Score(attribute) : 8;
        if (card.attributeName != null)
        {
            card.attributeName.text = (track != null ? track.displayName : attribute.ToString()).ToUpperInvariant();
            card.attributeName.color = art.name;
            card.attributeName.fontSharedMaterial = art.titleMaterial != null ? art.titleMaterial : card.attributeName.font.material;
        }
        if (card.score != null)
        {
            card.score.text = $"<color=#{ColorUtility.ToHtmlStringRGBA(art.faded)}>{now}  ></color>  <color=#{ColorUtility.ToHtmlStringRGBA(art.name)}>{now + 1}</color>";
            card.score.color = art.ink;
            card.score.fontSharedMaterial = art.titleMaterial != null ? art.titleMaterial : card.score.font.material;
        }
        if (card.effects != null)
        {
            card.effects.text = ProgressionManager.AttributeEffects(track, 1, true);
            card.effects.color = art.gain;
            card.effects.fontSharedMaterial = art.bodyMaterial != null ? art.bodyMaterial : card.effects.font.material;
        }
        if (card.key != null) { card.key.text = (i + 1).ToString(); card.key.color = art.ink; }
        card.FaceUp(true);
        if (card.group != null) card.group.alpha = 0f;
    }

    void Finish()
    {
        Hide();
        // Another point left (two levels at once): deal again straight away.
        var p = Progress();
        if (p != null && p.UnspentPoints > 0 && ProgressionManager.CardsOnLevelUp) { Deal(); return; }
        Close();
    }

    void Close()
    {
        Hide();
        if (!Open) return;
        Open = false;
        if (paused && Time.timeScale <= HeldTimeScale) Time.timeScale = timeScaleBefore;
        paused = false;
        DungeonMaster.OverMenus = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Menu);
        pointSince = -1f;
    }

    // ── Picking ───────────────────────────────────────────────────────

    public void PointerOver(int i)
    {
        if (phase != Phase.Choosing || hovered == i) return;
        hovered = i; keyboard = i;
        Play(art.hover, 1f + i * .04f);
        cards[i].transform.SetAsLastSibling();
    }
    public void PointerLeft(int i) { if (hovered == i) hovered = -1; }

    public void Pick(int i)
    {
        if (phase != Phase.Choosing || i < 0 || i >= cards.Length) return;
        var p = Progress();
        if (p == null || !p.SpendPoint(order[i])) return;
        picked = i;
        cards[i].transform.SetAsLastSibling();
        Play(art.pick, 1f);
        Play(art.leave, 1f);
        phase = Phase.Leaving;
        phaseStart = Time.unscaledTime;
    }

    void KeysPressed()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (k.digit1Key.wasPressedThisFrame || k.numpad1Key.wasPressedThisFrame) Pick(0);
        else if (k.digit2Key.wasPressedThisFrame || k.numpad2Key.wasPressedThisFrame) Pick(1);
        else if (k.digit3Key.wasPressedThisFrame || k.numpad3Key.wasPressedThisFrame) Pick(2);
        else if (k.digit4Key.wasPressedThisFrame || k.numpad4Key.wasPressedThisFrame) Pick(3);
    }

    void Navigate()
    {
        if (phase != Phase.Choosing) return;
        int step = InputManager.Instance.NavigateStep;
        keyboard = keyboard < 0 ? (step < 0 ? cards.Length - 1 : 0) : (keyboard + step + cards.Length) % cards.Length;
        hovered = -1;
        Play(art.hover, 1f + keyboard * .04f);
        cards[keyboard].transform.SetAsLastSibling();
    }
    void Submit() { if (phase == Phase.Choosing && keyboard >= 0) Pick(keyboard); }
    // Esc without Auto Popup: the cards go away and the point waits (the prompt comes back).
    void PutAway() { if (!paused && phase is Phase.Dealing or Phase.Choosing) { Close(); pointSince = Time.unscaledTime - afterLevelUp; } }

    // The prompt fades in and breathes while it shows.
    void ShowPrompt(bool show)
    {
        if (prompt == null) return;
        if (show && prompt.alpha <= 0f) { promptShown = Time.unscaledTime; if (promptText != null) promptText.text = promptLine; }
        float target = show ? .82f + .18f * Mathf.Sin((Time.unscaledTime - promptShown) * 3.2f) : 0f;
        prompt.alpha = Mathf.MoveTowards(prompt.alpha, target, Time.unscaledDeltaTime * (show ? 3f : 6f));
        float pop = show ? 1f + .08f * Mathf.Exp(-(Time.unscaledTime - promptShown) * 6f) : 1f;
        prompt.transform.localScale = new Vector3(pop, pop, 1f);
    }

    // Test hooks (from an eval in Play mode): the card a pointer would be over, and a pick.
    public static LevelUpCardsUI Find() => FindAnyObjectByType<LevelUpCardsUI>(FindObjectsInactive.Include);
    public void TestHover(int i) { if (i < 0) hovered = -1; else PointerOver(i); }
    public void TestPick(int i) => Pick(i);
    public bool Choosing => phase == Phase.Choosing;
    // Test hook for filming: once the cards are in, the pointer goes over each card in turn, back to one, and picks it.
    public void TestDemo(int pick, float dwell = .5f) => StartCoroutine(Demo(pick, dwell));
    System.Collections.IEnumerator Demo(int pick, float dwell)
    {
        while (phase != Phase.Choosing) yield return null;
        for (int i = 0; i < cards.Length; i++) { TestHover(i); yield return new WaitForSecondsRealtime(dwell); }
        TestHover(pick);
        yield return new WaitForSecondsRealtime(dwell * 1.6f);
        TestPick(pick);
    }

    // ── Poses ─────────────────────────────────────────────────────────

    // Panel i t seconds into its own pop-up: faded in, risen a little and grown to its size with a soft overshoot.
    void PoseDealing(int i, float t)
    {
        var card = cards[i];
        if (card == null || card.body == null) return;
        float k = Mathf.Clamp01(t / dealSeconds);
        var body = card.body;
        if (backdrop != null) backdrop.color = new Color(0f, 0f, 0f, art.dim * Ease.OutCubic(Mathf.Clamp01((Time.unscaledTime - phaseStart) / .25f)));
        if (title != null) title.alpha = Mathf.Clamp01((Time.unscaledTime - phaseStart - .2f) / .3f);
        if (t > 0f && landed[i] == 0f) { landed[i] = 1f; Play(art.deal, .95f + i * .06f); }
        if (card.group != null) card.group.alpha = t <= 0f ? 0f : Ease.OutCubic(Mathf.Clamp01(k / .7f));
        body.anchoredPosition = new Vector2(0f, Mathf.Lerp(-22f, 0f, Ease.OutCubic(k)));
        body.localRotation = Quaternion.identity;
        float size = Mathf.LerpUnclamped(.9f, 1f, Ease.OutBack(k, 1.4f));
        body.localScale = new Vector3(size, size, 1f);
        if (card.glow != null) card.glow.color = WithAlpha(art.glow, 0f);
    }

    // A panel waiting to be picked: lifted and grown a little toward the pointer or the keys, the others a shade darker.
    void PoseResting(int i, float h, float now)
    {
        var card = cards[i];
        if (card == null || card.body == null) return;
        card.body.anchoredPosition = new Vector2(0f, hoverLift * h);
        card.body.localRotation = Quaternion.identity;
        float sc = Mathf.Lerp(1f, hoverScale, h);
        card.body.localScale = new Vector3(sc, sc, 1f);
        if (card.glow != null) card.glow.color = WithAlpha(art.glow, art.glow.a * .6f * h);
        // The panels not pointed at darken a little (never see-through: the world would show through them).
        bool other = (hovered >= 0 || keyboard >= 0) && !(i == hovered || i == keyboard && hovered < 0);
        if (card.face != null)
        {
            float v = Mathf.MoveTowards(card.face.color.r, other ? .74f : 1f, Time.unscaledDeltaTime * 4f);
            card.face.color = new Color(v, v, v, 1f);
        }
    }

    // After the pick: the picked panel pulses once, then rises a little and fades; the others shrink a little and fade,
    // the ones nearer the pick first.
    void PoseLeaving(int i, float t)
    {
        var card = cards[i];
        if (card == null || card.body == null) return;
        var body = card.body;
        body.localRotation = Quaternion.identity;
        if (backdrop != null) backdrop.color = new Color(0f, 0f, 0f, art.dim * (1f - Ease.OutCubic(Mathf.Clamp01(t / leaveSeconds))));
        if (title != null) title.alpha = 1f - Mathf.Clamp01(t / .2f);
        if (i == picked)
        {
            float k = Mathf.Clamp01(t / leaveSeconds);
            float pulse = Mathf.Sin(Mathf.Clamp01(k / .35f) * Mathf.PI) * .05f;
            float go = Ease.OutCubic(Mathf.Clamp01((k - .3f) / .7f));
            body.anchoredPosition = new Vector2(0f, hoverLift + 30f * go);
            float size = hoverScale * (1f + pulse);
            body.localScale = new Vector3(size, size, 1f);
            if (card.glow != null) card.glow.color = WithAlpha(art.glow, art.glow.a * (1f - k));
            if (card.group != null) card.group.alpha = 1f - go;
            return;
        }
        float o = Ease.OutCubic(Mathf.Clamp01((t - Mathf.Abs(i - picked) * .04f) / (leaveSeconds * .75f)));
        body.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, -14f, o));
        float shrink = Mathf.Lerp(1f, .94f, o);
        body.localScale = new Vector3(shrink, shrink, 1f);
        if (card.face != null) card.face.color = Color.white;
        if (card.glow != null) card.glow.color = WithAlpha(art.glow, 0f);
        if (card.group != null) card.group.alpha = Mathf.Min(card.group.alpha, 1f - o);
    }

    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    static void Play(AudioData data, float pitch)
    {
        if (data == null || !AudioManager.HasInstance) return;
        var clip = data.GetClip();
        if (clip != null) AudioManager.Instance.PlayUI(clip, data.volume, data.RandomPitch() * pitch);
    }

    static class Ease
    {
        public static float OutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
        public static float InCubic(float x) => x * x * x;
        // Past 1 and back: a soft overshoot.
        public static float OutBack(float x, float s) { float c = s + 1f; return 1f + c * Mathf.Pow(x - 1f, 3f) + s * Mathf.Pow(x - 1f, 2f); }
    }
}

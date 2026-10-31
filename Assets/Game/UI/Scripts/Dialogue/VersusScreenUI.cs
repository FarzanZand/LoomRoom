using System;
using System.Collections;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The timing and sounds of a versus screen, kept on the boss fight it opens (BossFight > Intro > Versus).
[Serializable]
public class VersusSettings
{
    [FoldoutGroup("Versus timing"), Min(.05f), Tooltip("Seconds the player's side takes to come in from the left.")] public float leftIn = .26f;
    [FoldoutGroup("Versus timing"), Min(0), Tooltip("Seconds from the start until the boss's side sets off from the right.")] public float rightDelay = .5f;
    [FoldoutGroup("Versus timing"), Min(.05f), Tooltip("Seconds the boss's side takes to come in.")] public float rightIn = .26f;
    [FoldoutGroup("Versus timing"), Min(0), Tooltip("Seconds after the boss's side lands before the VS medallion slams in (the gold line draws out meanwhile).")] public float slamDelay = .22f;
    [FoldoutGroup("Versus timing"), Min(0), Tooltip("Seconds it holds after the slam.")] public float hold = 1.6f;
    [FoldoutGroup("Versus timing"), Min(.05f), Tooltip("Seconds everything takes to clear.")] public float throwOut = .3f;
    [FoldoutGroup("Versus timing"), Tooltip("Canvas units the screen shakes at the slam (a side landing shakes it 40% as much).")] public float shake = 18f;
    [FoldoutGroup("Versus sounds"), Tooltip("As each side sets off.")] public AudioClip[] bandWhoosh = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Tooltip("As each side lands.")] public AudioClip[] bandLand = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Tooltip("As the VS medallion slams in (all played together).")] public AudioClip[] slam = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Tooltip("As it clears.")] public AudioClip[] throwOff = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Range(0, 1)] public float volume = .9f;
}

// The you-versus-the-boss screen before a boss fight (BossFight > Intro), cinematic: black bars close in from the top
// and bottom and the screen darkens; the player's side (their class painting melting into a blue glow, their name
// and class in the bottom bar) comes in from the left, then the boss's (its live idling portrait in a red glow, its
// name and title) from the right, each landing with a thud and a jolt; a thin gold line draws out between them and the
// VS medallion slams down on it with a flash and a shake; a beat to read it, then it all clears. On real time. Its
// timing and sounds come from the fight (VersusSettings); its look is this prefab's (Dialogue/UI/Boss dialogue, under
// the Dialogue Manager).
public class VersusScreenUI : MonoBehaviour
{
    public static VersusScreenUI Current { get; private set; }

    [SerializeField] CanvasGroup group;
    [SerializeField] Image dim, flash;
    [SerializeField] RectTransform shake, topBar, bottomBar, leftSide, rightSide, line, medallion;
    [Tooltip("Each side's name and title: drawn over the bottom bar (the portraits draw under it), moving with its side.")]
    [SerializeField] RectTransform leftLabel, rightLabel;
    [SerializeField] Image playerPortrait;
    [SerializeField] RawImage bossPortrait;
    [SerializeField] TMP_Text playerName, playerTitle, bossName, bossTitle;

    [Header("Look")]
    [Tooltip("How far each side slides in from (canvas units).")] public float sideTravel = 420f;
    [Tooltip("Canvas units the sides drift toward each other while held.")] public float drift = 18f;
    [Range(0, 1), Tooltip("How dark the screen goes behind it.")] public float dimAlpha = .92f;
    [Header("Portrait")]
    [Tooltip("Pixels across the boss's portrait (drawn point-filtered, so fewer is chunkier).")] public int portraitResolution = 320;
    [Tooltip("Degrees the boss is turned from facing the camera.")] public float portraitTurn = -18f;
    [Tooltip("How much body shows below the head, in head-to-chest lengths (more: a smaller figure).")] public float portraitBodyBelow = 2.3f;
    [Tooltip("Room above the head, in head-to-chest lengths (horns and helmets get their own room on top).")] public float portraitRoomAbove = .55f;

    PortraitStage stage;
    bool playing;
    public static bool Covers => Current != null && Current.playing;
    Vector2 leftHome, rightHome, topHome, bottomHome, leftLabelHome, rightLabelHome;
    CanvasGroup leftGroup, rightGroup, medallionGroup, leftLabelGroup, rightLabelGroup;

    void Awake()
    {
        Current = this;
        leftHome = leftSide.anchoredPosition; rightHome = rightSide.anchoredPosition;
        topHome = topBar.anchoredPosition; bottomHome = bottomBar.anchoredPosition;
        leftLabelHome = leftLabel.anchoredPosition; rightLabelHome = rightLabel.anchoredPosition;
        leftGroup = Group(leftSide); rightGroup = Group(rightSide); medallionGroup = Group(medallion);
        leftLabelGroup = Group(leftLabel); rightLabelGroup = Group(rightLabel);
        group.alpha = 0f; group.blocksRaycasts = false;
    }

    static CanvasGroup Group(Component c) => c.TryGetComponent<CanvasGroup>(out var g) ? g : c.gameObject.AddComponent<CanvasGroup>();

    void OnDestroy() { if (Current == this) Current = null; if (stage != null) Destroy(stage.gameObject); }

    // Put away at once (the fight it opened was cancelled: its coroutine stopped mid-screen and would have left it up).
    public void Abort()
    {
        if (!playing && group.alpha <= 0f) return;
        playing = false;
        group.alpha = 0f; group.blocksRaycasts = false;
        if (stage != null) stage.Clear();
    }

    public IEnumerator Play(Player player, Character boss, string name, string title, VersusSettings s)
    {
        s ??= new VersusSettings();
        // Fill it.
        var progress = player != null ? player.GetComponent<AdventurerProgress>() : null;
        var cls = progress != null ? progress.selectedClass : null;
        playerPortrait.sprite = cls != null ? cls.portrait : null;
        playerPortrait.enabled = playerPortrait.sprite != null;
        var save = player != null ? player.GetComponent<AdventureSave>() : null;
        string hero = save != null ? save.Saved.characterName : "";
        playerName.text = (string.IsNullOrWhiteSpace(hero) ? "You" : hero).ToUpperInvariant();
        playerTitle.text = cls != null ? $"Level {(progress != null ? progress.Level : 1)} {cls.displayName}" : "";
        bossName.text = (name ?? "").ToUpperInvariant();
        bossTitle.text = title ?? "";
        stage ??= new GameObject("Versus portrait stage").AddComponent<PortraitStage>();
        stage.resolution = portraitResolution; stage.turn = portraitTurn; stage.bodyBelow = portraitBodyBelow; stage.roomAbove = portraitRoomAbove;
        stage.Show(boss != null ? boss.gameObject : null);
        bossPortrait.texture = stage.Texture;

        // In.
        playing = true;
        float barTravel = topBar.rect.height + 4f;
        float rightLands = s.rightDelay + s.rightIn, slamAt = rightLands + s.slamDelay;
        flash.color = new Color(1f, 1f, 1f, 0f);
        medallion.localScale = Vector3.zero; medallionGroup.alpha = 0f; line.localScale = new Vector3(0f, 1f, 1f);
        group.alpha = 1f;
        float t0 = Time.unscaledTime, joltAt = -1f, joltSize = 0f;
        bool leftOff = false, leftLanded = false, rightOff = false, rightLanded = false, slammed = false;
        while (true)
        {
            float t = Time.unscaledTime - t0;
            float bars = OutCubic(t / .22f);
            topBar.anchoredPosition = topHome + Vector2.up * barTravel * (1f - bars);
            bottomBar.anchoredPosition = bottomHome - Vector2.up * barTravel * (1f - bars);
            dim.color = new Color(0f, 0f, 0f, dimAlpha * Mathf.Clamp01(t / .2f));
            if (!leftOff) { leftOff = true; Sound(s.bandWhoosh, s.volume, 1f); }
            if (!leftLanded && t >= s.leftIn) { leftLanded = true; Sound(s.bandLand, s.volume, 1f); joltAt = t; joltSize = s.shake * .4f; }
            if (!rightOff && t >= s.rightDelay) { rightOff = true; Sound(s.bandWhoosh, s.volume, 1.1f); }
            if (!rightLanded && t >= rightLands) { rightLanded = true; Sound(s.bandLand, s.volume, .9f); joltAt = t; joltSize = s.shake * .4f; }
            if (!slammed && t >= slamAt) { slammed = true; Sound(s.slam, s.volume, 1f); joltAt = t; joltSize = s.shake; }

            float leftK = t / s.leftIn, rightK = (t - s.rightDelay) / s.rightIn;
            float leftDrift = Mathf.Clamp01((t - s.leftIn) / (slamAt + s.hold)) * drift, rightDrift = Mathf.Clamp01((t - rightLands) / s.hold) * drift;
            leftSide.anchoredPosition = leftHome - Vector2.right * (sideTravel * (1f - OutBack(leftK)) - leftDrift);
            rightSide.anchoredPosition = rightHome + Vector2.right * (sideTravel * (1f - OutBack(rightK)) - rightDrift);
            leftGroup.alpha = leftLabelGroup.alpha = Mathf.Clamp01(leftK * 2.5f);
            rightGroup.alpha = rightLabelGroup.alpha = Mathf.Clamp01(rightK * 2.5f);
            leftLabel.anchoredPosition = leftLabelHome + (leftSide.anchoredPosition - leftHome);
            rightLabel.anchoredPosition = rightLabelHome + (rightSide.anchoredPosition - rightHome);
            line.localScale = new Vector3(OutCubic((t - rightLands) / Mathf.Max(.05f, s.slamDelay)), 1f, 1f);
            if (slammed)
            {
                float k = t - slamAt;
                // From well over its size down past one and back: it lands, it doesn't fade in.
                medallion.localScale = Vector3.one * (k < .09f ? Mathf.Lerp(2.6f, .88f, k / .09f) : Mathf.Lerp(.88f, 1f, OutBack((k - .09f) / .14f)));
                medallionGroup.alpha = Mathf.Clamp01(k / .05f);
                flash.color = new Color(1f, .93f, .8f, Mathf.Clamp01(.28f - k * 4f));
            }
            float since = joltAt >= 0f ? t - joltAt : 99f;
            float left = Mathf.Clamp01(1f - since / .26f);
            shake.anchoredPosition = UnityEngine.Random.insideUnitCircle * joltSize * left * left;
            if (t >= slamAt + s.hold) break;
            yield return null;
        }

        // Out: the sides slide away and fade, the line and the medallion go, the bars open.
        shake.anchoredPosition = Vector2.zero;
        Sound(s.throwOff, s.volume, 1f);
        var leftFrom = leftSide.anchoredPosition; var rightFrom = rightSide.anchoredPosition;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / s.throwOut)
        {
            float e = k * k;
            leftSide.anchoredPosition = leftFrom - Vector2.right * sideTravel * e;
            rightSide.anchoredPosition = rightFrom + Vector2.right * sideTravel * e;
            leftGroup.alpha = rightGroup.alpha = leftLabelGroup.alpha = rightLabelGroup.alpha = 1f - e;
            leftLabel.anchoredPosition = leftLabelHome + (leftSide.anchoredPosition - leftHome);
            rightLabel.anchoredPosition = rightLabelHome + (rightSide.anchoredPosition - rightHome);
            medallion.localScale = Vector3.one * (1f + e * .6f); medallionGroup.alpha = 1f - e;
            line.localScale = new Vector3(1f - e, 1f, 1f);
            topBar.anchoredPosition = topHome + Vector2.up * barTravel * e;
            bottomBar.anchoredPosition = bottomHome - Vector2.up * barTravel * e;
            dim.color = new Color(0f, 0f, 0f, dimAlpha * (1f - e));
            yield return null;
        }
        group.alpha = 0f;
        playing = false;
        stage.Clear();
    }

    static void Sound(AudioClip[] clips, float volume, float pitch)
    {
        if (clips == null || !AudioManager.HasInstance) return;
        foreach (var c in clips) if (c != null) AudioManager.Instance.PlayUI(c, volume, pitch);
    }

    static float OutCubic(float k) { k = Mathf.Clamp01(k); float t = 1f - k; return 1f - t * t * t; }

    static float OutBack(float k)
    {
        k = Mathf.Clamp01(k);
        const float c = 1.7f;
        float t = k - 1f;
        return 1f + (c + 1f) * t * t * t + c * t * t;
    }
}

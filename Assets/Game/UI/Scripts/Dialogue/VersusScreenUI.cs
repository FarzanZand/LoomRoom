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
    [FoldoutGroup("Versus timing"), Min(.05f), Tooltip("Seconds the player's band takes to slam in from the left.")] public float leftIn = .22f;
    [FoldoutGroup("Versus timing"), Min(0), Tooltip("Seconds from the start until the boss's band sets off from the right.")] public float rightDelay = .45f;
    [FoldoutGroup("Versus timing"), Min(.05f), Tooltip("Seconds the boss's band takes to slam in.")] public float rightIn = .22f;
    [FoldoutGroup("Versus timing"), Min(0), Tooltip("Seconds after the boss's band lands before VS crashes down.")] public float slamDelay = .2f;
    [FoldoutGroup("Versus timing"), Min(0), Tooltip("Seconds it holds after the crash.")] public float hold = 1.5f;
    [FoldoutGroup("Versus timing"), Min(.05f), Tooltip("Seconds the bands take to be thrown off.")] public float throwOut = .22f;
    [FoldoutGroup("Versus timing"), Tooltip("Canvas units the screen shakes at the crash (a band landing shakes it 40% as much).")] public float shake = 24f;
    [FoldoutGroup("Versus sounds"), Tooltip("As each band sets off.")] public AudioClip[] bandWhoosh = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Tooltip("As each band lands.")] public AudioClip[] bandLand = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Tooltip("As VS crashes down (all played together).")] public AudioClip[] slam = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Tooltip("As the bands are thrown off.")] public AudioClip[] throwOff = new AudioClip[0];
    [FoldoutGroup("Versus sounds"), Range(0, 1)] public float volume = .9f;
}

// The you-versus-the-boss screen before a boss fight (BossFight > Intro): the screen darkens, the player's band (their
// class painting, name and class) slams in from the left, then the boss's band (its live idling portrait, name and
// title) from the right, each landing with a thud and a jolt, "VS" crashes down between them with a flash and a shake,
// a beat to read it, then both bands are thrown off. On real time. Its timing and sounds come from the fight
// (VersusSettings); its look is this prefab's (Dialogue/UI/Boss dialogue, under the Dialogue Manager).
public class VersusScreenUI : MonoBehaviour
{
    public static VersusScreenUI Current { get; private set; }

    [SerializeField] CanvasGroup group;
    [SerializeField] Image dim, flash;
    [SerializeField] RectTransform shake, leftBand, rightBand, versus, divider;
    [SerializeField] Image playerPortrait;
    [SerializeField] RawImage bossPortrait;
    [SerializeField] TMP_Text playerName, playerTitle, bossName, bossTitle;

    [Header("Look")]
    [Tooltip("How far the bands slide in from (canvas units past their resting place).")] public float bandTravel = 1500f;
    [Tooltip("Canvas units the bands drift toward each other while held.")] public float drift = 26f;
    [Range(0, 1), Tooltip("How dark the screen goes behind the bands.")] public float dimAlpha = .78f;
    [Header("Portrait")]
    [Tooltip("Pixels across the boss's portrait (drawn point-filtered, so fewer is chunkier).")] public int portraitResolution = 320;
    [Tooltip("Degrees the boss is turned from facing the camera.")] public float portraitTurn = -18f;
    [Tooltip("How much body shows below the head, in head-to-chest lengths (more: a smaller figure).")] public float portraitBodyBelow = 2.3f;
    [Tooltip("Room above the head, in head-to-chest lengths (horns and helmets get their own room on top).")] public float portraitRoomAbove = .55f;

    PortraitStage stage;
    bool playing;
    public static bool Covers => Current != null && Current.playing;
    Vector2 leftHome, rightHome, versusHome;

    void Awake()
    {
        Current = this;
        leftHome = leftBand.anchoredPosition; rightHome = rightBand.anchoredPosition; versusHome = versus.anchoredPosition;
        group.alpha = 0f; group.blocksRaycasts = false;
    }

    void OnDestroy() { if (Current == this) Current = null; if (stage != null) Destroy(stage.gameObject); }

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

        // In: left, then right, each landing with a jolt; then the crash.
        playing = true;
        leftBand.anchoredPosition = leftHome - Vector2.right * bandTravel;
        rightBand.anchoredPosition = rightHome + Vector2.right * bandTravel;
        versus.localScale = Vector3.zero; divider.localScale = new Vector3(1f, 0f, 1f);
        flash.color = new Color(1f, 1f, 1f, 0f); dim.color = new Color(0f, 0f, 0f, 0f);
        group.alpha = 1f;
        float rightLands = s.rightDelay + s.rightIn, slamAt = rightLands + s.slamDelay;
        float t0 = Time.unscaledTime, joltAt = -1f, joltSize = 0f;
        bool leftOff = false, leftLanded = false, rightOff = false, rightLanded = false, slammed = false;
        while (true)
        {
            float t = Time.unscaledTime - t0;
            dim.color = new Color(0f, 0f, 0f, dimAlpha * Mathf.Clamp01(t / .15f));
            if (!leftOff) { leftOff = true; Sound(s.bandWhoosh, s.volume, 1f); }
            if (!leftLanded && t >= s.leftIn) { leftLanded = true; Sound(s.bandLand, s.volume, 1f); joltAt = t; joltSize = s.shake * .4f; }
            if (!rightOff && t >= s.rightDelay) { rightOff = true; Sound(s.bandWhoosh, s.volume, 1.1f); }
            if (!rightLanded && t >= rightLands) { rightLanded = true; Sound(s.bandLand, s.volume, .9f); joltAt = t; joltSize = s.shake * .4f; }
            if (!slammed && t >= slamAt) { slammed = true; Sound(s.slam, s.volume, 1f); joltAt = t; joltSize = s.shake; }

            float leftK = t / s.leftIn, rightK = (t - s.rightDelay) / s.rightIn;
            float leftDrift = Mathf.Clamp01((t - s.leftIn) / (slamAt + s.hold)) * drift, rightDrift = Mathf.Clamp01((t - rightLands) / s.hold) * drift;
            leftBand.anchoredPosition = Vector2.LerpUnclamped(leftHome - Vector2.right * bandTravel, leftHome, OutBack(leftK)) + Vector2.right * leftDrift;
            rightBand.anchoredPosition = Vector2.LerpUnclamped(rightHome + Vector2.right * bandTravel, rightHome, OutBack(rightK)) - Vector2.right * rightDrift;
            divider.localScale = new Vector3(1f, Mathf.Clamp01((t - rightLands) / .12f), 1f);
            if (slammed)
            {
                float k = t - slamAt;
                // From three times its size down past one and back: a crash, not a fade.
                versus.localScale = Vector3.one * (k < .1f ? Mathf.Lerp(3f, .86f, k / .1f) : Mathf.Lerp(.86f, 1f, OutBack((k - .1f) / .12f)));
                flash.color = new Color(1f, .95f, .85f, Mathf.Clamp01(.85f - k * 4f));
            }
            float since = joltAt >= 0f ? t - joltAt : 99f;
            float left = Mathf.Clamp01(1f - since / .28f);
            shake.anchoredPosition = UnityEngine.Random.insideUnitCircle * joltSize * left * left;
            if (t >= slamAt + s.hold) break;
            yield return null;
        }

        // Out: thrown off the way they came, the VS blown up and gone.
        shake.anchoredPosition = Vector2.zero;
        Sound(s.throwOff, s.volume, 1f);
        var leftFrom = leftBand.anchoredPosition; var rightFrom = rightBand.anchoredPosition;
        for (float k = 0f; k < 1f; k += Time.unscaledDeltaTime / s.throwOut)
        {
            float e = k * k * k;
            leftBand.anchoredPosition = Vector2.Lerp(leftFrom, leftFrom - Vector2.right * bandTravel, e);
            rightBand.anchoredPosition = Vector2.Lerp(rightFrom, rightFrom + Vector2.right * bandTravel, e);
            versus.localScale = Vector3.one * (1f + e * 1.6f);
            group.alpha = 1f - e;
            yield return null;
        }
        group.alpha = 0f;
        playing = false;
        versus.anchoredPosition = versusHome;
        stage.Clear();
    }

    static void Sound(AudioClip[] clips, float volume, float pitch)
    {
        if (clips == null || !AudioManager.HasInstance) return;
        foreach (var c in clips) if (c != null) AudioManager.Instance.PlayUI(c, volume, pitch);
    }

    static float OutBack(float k)
    {
        k = Mathf.Clamp01(k);
        const float c = 1.9f;
        float t = k - 1f;
        return 1f + (c + 1f) * t * t * t + c * t * t;
    }
}

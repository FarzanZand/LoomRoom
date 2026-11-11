using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The stamina readout under the crosshair (HUD canvas > Stamina readout, built by StaminaReadoutSetup.Build()): a centred
// row of pale segments, one for every Per Segment stamina (a shorter last one for what is left over), that empty one by
// one, the current one shrinking. It shows while the active player's stamina is below full and fades out a moment after
// it fills again. A refused stamina use (a dash or a block with too little left) flashes the empty segments red and
// says Not enough stamina just above them for a moment (Refused text, the user, 2026-11-11; it took the place of the
// notification PlayerVitalsUI gave, which still shows when the readout is switched off). The
// segments are copies of the authored template (switched off); each one's first child is its fill. UIManager >
// Show Stamina Readout switches it off.
public class StaminaReadoutUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] RectTransform template;
    [SerializeField, Min(1f)] float perSegment = 10f;
    [SerializeField, Min(0f)] float gap = 5f;
    [Tooltip("Colour of an empty segment.")]
    [SerializeField] Color empty = new(.85f, .9f, .83f, .2f);
    [Tooltip("Colour an empty segment flashes when stamina is refused.")]
    [SerializeField] Color refused = new(.85f, .3f, .25f, .85f);
    [SerializeField, Min(.05f)] float refusedSeconds = .4f;
    [Tooltip("Shown just above the segments for a moment when stamina is refused (built by StaminaReadoutSetup.BuildRefusedText).")]
    [SerializeField] TMPro.TMP_Text refusedText;
    [SerializeField] string refusedWords = "Not enough stamina";
    [Tooltip("How long the words stay, the last part fading.")]
    [SerializeField, Min(.2f)] float refusedTextSeconds = 1.1f;
    [SerializeField, Min(.05f)] float refusedTextFade = .3f;

    // The readout says the refusal itself (PlayerVitalsUI then gives no notification).
    static StaminaReadoutUI live;
    public static bool SaysRefusal => live != null && live.refusedText != null && live.isActiveAndEnabled && UIManager.StaminaReadoutShown;
    [SerializeField, Min(1f)] float smoothSpeed = 14f;
    [Tooltip("Seconds it stays after stamina is full again, then fades over Fade Out.")]
    [SerializeField, Min(0f)] float holdWhenFull = .5f;
    [SerializeField, Min(.01f)] float fadeIn = .1f, fadeOut = .35f;

    readonly List<Image> cells = new();
    readonly List<RectTransform> fills = new();
    CharacterStats bound;
    float shown, laidOutFor = -1f, fullSince, refusedAt = -10f;

    void OnEnable() => live = this;
    void OnDisable() { if (live == this) live = null; }

    void Start()
    {
        if (template != null) template.gameObject.SetActive(false);
        if (refusedText != null) { refusedText.text = refusedWords; refusedText.alpha = 0f; }
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped += Bind;
        Bind(PlayerManager.HasInstance ? PlayerManager.Instance.Active : null);
        if (group != null) group.alpha = 0f;
    }

    void OnDestroy()
    {
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= Bind;
        if (bound != null) bound.StaminaUseDenied -= Refused;
    }

    void Bind(Player player)
    {
        if (bound != null) bound.StaminaUseDenied -= Refused;
        bound = player != null && player.Stats != null && player.Stats.HasStat(StatType.MaxStamina) ? player.Stats : null;
        if (bound != null) bound.StaminaUseDenied += Refused;
        shown = bound != null ? bound.CurrentStamina : 0f;
    }

    void Refused() => refusedAt = Time.unscaledTime;

    // The words pop in a little large, settle, hold and fade.
    void ShowRefusedText()
    {
        if (refusedText == null) return;
        float t = Time.unscaledTime - refusedAt;
        float a = t < 0f || t >= refusedTextSeconds ? 0f : Mathf.Clamp01((refusedTextSeconds - t) / refusedTextFade);
        if (!Mathf.Approximately(refusedText.alpha, a)) refusedText.alpha = a;
        float pop = t < .12f ? Mathf.Lerp(1.18f, 1f, t / .12f) : 1f;
        refusedText.rectTransform.localScale = new Vector3(pop, pop, 1f);
    }

    // One segment per Per Segment of the most stamina, the last one only as wide as what is left over; the row centred.
    void LayOut(float max)
    {
        laidOutFor = max;
        int count = Mathf.Max(1, Mathf.CeilToInt(max / perSegment - .001f));
        while (cells.Count < count)
        {
            var copy = Instantiate(template, template.parent);
            copy.name = $"Segment {cells.Count + 1}";
            copy.gameObject.SetActive(true);
            cells.Add(copy.GetComponent<Image>());
            fills.Add(copy.childCount > 0 ? (RectTransform)copy.GetChild(0) : null);
        }
        float width = template.sizeDelta.x;
        float last = max / perSegment - (count - 1);
        float total = (count - 1) * (width + gap) + width * Mathf.Clamp01(last);
        float x = -total * .5f;
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = (RectTransform)cells[i].transform;
            cell.gameObject.SetActive(i < count);
            if (i >= count) continue;
            float w = i == count - 1 ? width * Mathf.Clamp01(last) : width;
            cell.sizeDelta = new Vector2(w, template.sizeDelta.y);
            cell.anchoredPosition = new Vector2(x + w * .5f, template.anchoredPosition.y);
            x += w + gap;
        }
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float max = bound != null ? bound.MaxStamina : 0f;
        float current = bound != null ? Mathf.Clamp(bound.CurrentStamina, 0f, max) : 0f;
        bool refusing = Time.unscaledTime - refusedAt < Mathf.Max(refusedSeconds, refusedText != null ? refusedTextSeconds : 0f);
        ShowRefusedText();
        bool live = bound != null && max > 0f && UIManager.StaminaReadoutShown && (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        if (current < max - .05f || refusing) fullSince = Time.unscaledTime;
        bool show = live && Time.unscaledTime - fullSince < holdWhenFull;
        if (group != null)
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, dt / (show ? fadeIn : fadeOut));
        if (group != null && group.alpha <= 0f || template == null || max <= 0f) { shown = current; return; }
        if (!Mathf.Approximately(max, laidOutFor)) LayOut(max);

        // Falls fast (a dash takes 10 at once) and fills as it regenerates, eased either way.
        shown = Mathf.Lerp(shown, current, 1f - Mathf.Exp(-smoothSpeed * dt));
        // The flash fades out over its time, so it reads as a blink.
        float flash = refusing ? 1f - (Time.unscaledTime - refusedAt) / refusedSeconds : 0f;
        var colour = Color.Lerp(empty, refused, flash);
        for (int i = 0; i < cells.Count; i++)
        {
            if (!cells[i].gameObject.activeSelf) continue;
            float holds = Mathf.Min(perSegment, max - i * perSegment);
            if (fills[i] != null) fills[i].localScale = new Vector3(Mathf.Clamp01((shown - i * perSegment) / holds), 1f, 1f);
            if (cells[i].color != colour) cells[i].color = colour;
        }
    }
}

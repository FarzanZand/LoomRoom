using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The stamina readout under the crosshair (HUD canvas > Stamina readout, built by StaminaReadoutSetup.Build()): a row of
// segments over a thin bracket, the share left on the left and STAMINA on the right, all in one pale green. It shows
// while the active player's stamina is below full and fades out a moment after it fills again; the segments empty one
// by one, the current one shrinking. A refused stamina use (a dash or a block with too little left) flashes its empty
// segments red. The authored segments are the cells; each cell's first child is its fill.
public class StaminaReadoutUI : MonoBehaviour
{
    [SerializeField] CanvasGroup group;
    [SerializeField] RectTransform[] fills;
    [SerializeField] Image[] cells;
    [SerializeField] TMP_Text percent;
    [Tooltip("Colour of an empty cell.")]
    [SerializeField] Color empty = new(.04f, .06f, .07f, .5f);
    [Tooltip("Colour an empty cell flashes when stamina is refused.")]
    [SerializeField] Color refused = new(.85f, .3f, .25f, .85f);
    [SerializeField, Min(.05f)] float refusedSeconds = .4f;
    [SerializeField, Min(1f)] float smoothSpeed = 14f;
    [Tooltip("Seconds it stays after stamina is full again, then fades over Fade Out.")]
    [SerializeField, Min(0f)] float holdWhenFull = .5f;
    [SerializeField, Min(.01f)] float fadeIn = .1f, fadeOut = .35f;

    CharacterStats bound;
    float shown = 1f, fullSince, refusedAt = -10f;
    int written = -1;

    void Start()
    {
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
        shown = Share();
        written = -1;
    }

    void Refused() => refusedAt = Time.unscaledTime;

    float Share() => bound != null && bound.MaxStamina > 0f ? Mathf.Clamp01(bound.CurrentStamina / bound.MaxStamina) : 1f;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float share = Share();
        bool refusing = Time.unscaledTime - refusedAt < refusedSeconds;
        bool live = bound != null && (!GameManager.HasInstance || GameManager.Instance.GameplayActive);
        if (share < .995f || refusing) fullSince = Time.unscaledTime;
        bool show = live && Time.unscaledTime - fullSince < holdWhenFull;
        if (group != null)
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, dt / (show ? fadeIn : fadeOut));
        if (group != null && group.alpha <= 0f) { shown = share; return; }

        // Falls fast (a dash takes half at once) and fills as it regenerates, eased either way.
        shown = Mathf.Lerp(shown, share, 1f - Mathf.Exp(-smoothSpeed * dt));
        int count = fills != null ? fills.Length : 0;
        for (int i = 0; i < count; i++)
        {
            float part = Mathf.Clamp01(shown * count - i);
            if (fills[i] != null) fills[i].localScale = new Vector3(part, 1f, 1f);
        }
        if (cells != null)
        {
            // The flash fades out over its time, so it reads as a blink.
            float flash = refusing ? 1f - (Time.unscaledTime - refusedAt) / refusedSeconds : 0f;
            var colour = Color.Lerp(empty, refused, flash);
            foreach (var cell in cells) if (cell != null && cell.color != colour) cell.color = colour;
        }
        int value = Mathf.RoundToInt(share * 100f);
        if (percent != null && value != written) { written = value; percent.SetText("{0}%", value); }
    }
}

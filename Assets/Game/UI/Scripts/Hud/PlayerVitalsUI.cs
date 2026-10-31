using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

// Health, stamina and mana bars for the active player. Bars are filled Images.
public class PlayerVitalsUI : MonoBehaviour
{
    [SerializeField] Image healthFill;
    [Tooltip("Optional bar behind the health fill that lingers on lost health, then drains (UIColor Recent Damage).")]
    [SerializeField] Image healthTrail;
    [SerializeField, Min(0)] float trailDelay = .35f;
    [SerializeField, Min(.1f)] float trailSpeed = 2.5f;
    [FormerlySerializedAs("staminaFill"), SerializeField] Image manaFill;
    [SerializeField] Image staminaBarFill;
    [FormerlySerializedAs("staminaRoot"), SerializeField] GameObject manaRoot;
    [SerializeField] GameObject staminaBarRoot;
    [SerializeField] float smoothSpeed = 10f;

    [SerializeField] TMP_Text healthValue;
    [FormerlySerializedAs("staminaValue"), SerializeField] TMP_Text manaValue;
    [SerializeField] TMP_Text staminaBarValue;

    [Header("Stamina feedback")]
    [SerializeField] string staminaWarning = "Not enough stamina";
    [SerializeField, Min(.1f)] float staminaWarningInterval = 1.5f;
    float nextStaminaWarning;

    void StaminaDenied()
    {
        if (Time.unscaledTime < nextStaminaWarning || (GameManager.HasInstance && !GameManager.Instance.GameplayActive)) return;
        nextStaminaWarning = Time.unscaledTime + staminaWarningInterval;
        NotificationUI.Show(staminaWarning);
    }

    CharacterStats bound;
    float healthShownLast = 1f;
    float healthShown = 1f, staminaShown = 1f, manaShown = 1f, trailShown = 1f, trailWaitUntil;
    // The numbers last written, so a label is only set again when it changes (TextMeshPro rebuilds its mesh on every set).
    readonly int[] written = { -1, -1, -1, -1, -1, -1 };

    void OnEnable()
    {
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
            OnPlayerSwapped(PlayerManager.Instance.Active);
        }
    }

    void Start()
    {
        // Managers may awaken after this UI's OnEnable. Subscribe once they all exist.
        if (PlayerManager.HasInstance)
        {
            PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
            PlayerManager.Instance.PlayerSwapped += OnPlayerSwapped;
        }
        if (PlayerManager.HasInstance) OnPlayerSwapped(PlayerManager.Instance.Active);
    }

    void OnDisable()
    {
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= OnPlayerSwapped;
        if (bound != null) bound.StaminaUseDenied -= StaminaDenied;
        bound = null;
    }

    void OnPlayerSwapped(Player player)
    {
        if (bound != null) bound.StaminaUseDenied -= StaminaDenied;
        bound = player != null ? player.Stats : null;
        for (int i = 0; i < written.Length; i++) written[i] = -1;   // a new player: write the labels again
        if (bound != null) bound.StaminaUseDenied += StaminaDenied;
        nextStaminaWarning = 0;
        if (manaRoot != null) manaRoot.SetActive(bound != null && bound.HasStat(StatType.MaxMana));
        if (staminaBarRoot != null) staminaBarRoot.SetActive(bound != null && bound.HasStat(StatType.MaxStamina));
        healthShown = bound != null && bound.MaxHealth > 0 ? bound.CurrentHealth / bound.MaxHealth : 0f;
        trailShown = healthShown;
        manaShown = bound != null && bound.MaxMana > 0 ? bound.CurrentMana / bound.MaxMana : 0f;
        staminaShown = bound != null && bound.MaxStamina > 0 ? bound.CurrentStamina / bound.MaxStamina : 0f;
    }

    void Update()
    {
        if (bound == null) return;
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime);

        float h = bound.MaxHealth > 0f ? bound.CurrentHealth / bound.MaxHealth : 0f;
        healthShown = Mathf.Lerp(healthShown, h, t);
        UIBar.Set(healthFill, healthShown);
        // The trail holds where health was for a moment after a hit, then drains down to it.
        if (h >= trailShown) trailShown = h;
        else if (Time.unscaledTime >= trailWaitUntil) trailShown = Mathf.MoveTowards(trailShown, h, trailSpeed * .25f * Time.unscaledDeltaTime);
        if (h < healthShownLast - .0001f) trailWaitUntil = Time.unscaledTime + trailDelay;
        healthShownLast = h;
        UIBar.Set(healthTrail, trailShown);
        Write(healthValue, 0, bound.CurrentHealth, bound.MaxHealth);

        float m = bound.MaxMana > 0f ? bound.CurrentMana / bound.MaxMana : 0f;
        manaShown = Mathf.Lerp(manaShown, m, t);
        UIBar.Set(manaFill, manaShown);
        Write(manaValue, 2, bound.CurrentMana, bound.MaxMana);
        float s = bound.MaxStamina > 0 ? bound.CurrentStamina / bound.MaxStamina : 0;
        staminaShown = Mathf.Lerp(staminaShown, s, t);
        UIBar.Set(staminaBarFill, staminaShown);
        Write(staminaBarValue, 4, bound.CurrentStamina, bound.MaxStamina);
    }

    void Write(TMPro.TMP_Text label, int at, float current, float max)
    {
        if (label == null) return;
        int c = Mathf.CeilToInt(current), m = Mathf.CeilToInt(max);
        if (written[at] == c && written[at + 1] == m) return;
        written[at] = c; written[at + 1] = m;
        label.SetText("{0}/{1}", c, m);
    }
}

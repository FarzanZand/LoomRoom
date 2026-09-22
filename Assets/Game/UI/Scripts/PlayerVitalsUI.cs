using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

// Health, stamina and mana bars for the active player. Bars are filled Images.
public class PlayerVitalsUI : MonoBehaviour
{
    [SerializeField] Image healthFill;
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
    float healthShown = 1f, staminaShown = 1f, manaShown = 1f;

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
        if (bound != null) bound.StaminaUseDenied += StaminaDenied;
        nextStaminaWarning = 0;
        if (manaRoot != null) manaRoot.SetActive(bound != null && bound.HasStat(StatType.MaxMana));
        if (staminaBarRoot != null) staminaBarRoot.SetActive(bound != null && bound.HasStat(StatType.MaxStamina));
        healthShown = bound != null && bound.MaxHealth > 0 ? bound.CurrentHealth / bound.MaxHealth : 0f;
        manaShown = bound != null && bound.MaxMana > 0 ? bound.CurrentMana / bound.MaxMana : 0f;
        staminaShown = bound != null && bound.MaxStamina > 0 ? bound.CurrentStamina / bound.MaxStamina : 0f;
    }

    void Update()
    {
        if (bound == null) return;
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime);

        float h = bound.MaxHealth > 0f ? bound.CurrentHealth / bound.MaxHealth : 0f;
        healthShown = Mathf.Lerp(healthShown, h, t);
        if (healthFill != null) healthFill.fillAmount = healthShown;
        if (healthValue != null) healthValue.SetText("{0} / {1}", Mathf.CeilToInt(bound.CurrentHealth), Mathf.CeilToInt(bound.MaxHealth));

        float m = bound.MaxMana > 0f ? bound.CurrentMana / bound.MaxMana : 0f;
        manaShown = Mathf.Lerp(manaShown, m, t);
        if (manaFill != null) manaFill.fillAmount = manaShown;
        if (manaValue != null) manaValue.SetText("{0} / {1}", Mathf.CeilToInt(bound.CurrentMana), Mathf.CeilToInt(bound.MaxMana));
        float s = bound.MaxStamina > 0 ? bound.CurrentStamina / bound.MaxStamina : 0;
        staminaShown = Mathf.Lerp(staminaShown, s, t);
        if (staminaBarFill != null) staminaBarFill.fillAmount = staminaShown;
        if (staminaBarValue != null) staminaBarValue.SetText("{0} / {1}", Mathf.CeilToInt(bound.CurrentStamina), Mathf.CeilToInt(bound.MaxStamina));
    }
}

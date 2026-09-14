using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Health and stamina bars for the active player. Bars are filled Images.
public class PlayerVitalsUI : MonoBehaviour
{
    [SerializeField] Image healthFill;
    [SerializeField] Image staminaFill;
    [SerializeField] GameObject staminaRoot;
    [SerializeField] float smoothSpeed = 10f;

    [SerializeField] TMP_Text healthValue;
    [SerializeField] TMP_Text staminaValue;

    RectTransform panelRect, healthRoot;
    Vector2 fullSize, healthPosition;

    void Awake()
    {
        panelRect = transform as RectTransform;
        healthRoot = healthFill != null ? healthFill.transform.parent as RectTransform : null;
        if (panelRect != null) fullSize = panelRect.sizeDelta;
        if (healthRoot != null) healthPosition = healthRoot.anchoredPosition;
    }

    CharacterStats bound;
    float healthShown = 1f, staminaShown = 1f;

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
        bound = null;
    }

    void OnPlayerSwapped(Player player)
    {
        bound = player != null ? player.Stats : null;
        bool hasStamina = bound != null && bound.HasStat(StatType.MaxStamina);
        if (staminaRoot != null) staminaRoot.SetActive(hasStamina);
        if (panelRect != null && healthRoot != null && staminaRoot != null)
        {
            var staminaRect = staminaRoot.transform as RectTransform;
            float rowHeight = staminaRect != null ? healthPosition.y - staminaRect.anchoredPosition.y : 0f;
            panelRect.sizeDelta = fullSize - new Vector2(0, hasStamina ? 0 : rowHeight);
            healthRoot.anchoredPosition = healthPosition - new Vector2(0, hasStamina ? 0 : rowHeight);
        }
        healthShown = bound != null && bound.MaxHealth > 0 ? bound.CurrentHealth / bound.MaxHealth : 0f;
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

        float s = bound.MaxStamina > 0f ? bound.CurrentStamina / bound.MaxStamina : 0f;
        staminaShown = Mathf.Lerp(staminaShown, s, t);
        if (staminaFill != null) staminaFill.fillAmount = staminaShown;
        if (staminaValue != null) staminaValue.SetText("{0} / {1}", Mathf.CeilToInt(bound.CurrentStamina), Mathf.CeilToInt(bound.MaxStamina));
    }
}

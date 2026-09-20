using UnityEngine;

// Hides the HUD canvas when GameManager says so (cutscenes).
public class HudCanvas : MonoBehaviour
{
    [SerializeField] Canvas canvas;
    HotbarUI hotbar;
    PlayerVitalsUI vitals;

    void Awake()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        hotbar = GetComponentInChildren<HotbarUI>(true);
        vitals = GetComponentInChildren<PlayerVitalsUI>(true);
    }

    void OnEnable()
    {
        if (GameManager.HasInstance) GameManager.Instance.HudVisibilityChanged += SetVisible;
        BindPlayer();
    }

    void Start()
    {
        BindPlayer();
        if (GameManager.HasInstance)
        {
            GameManager.Instance.HudVisibilityChanged -= SetVisible;
            GameManager.Instance.HudVisibilityChanged += SetVisible;
        }
    }

    void OnDisable()
    {
        if (GameManager.HasInstance) GameManager.Instance.HudVisibilityChanged -= SetVisible;
        if (PlayerManager.HasInstance) PlayerManager.Instance.PlayerSwapped -= SetPlayer;
    }

    void BindPlayer()
    {
        if (!PlayerManager.HasInstance) return;
        PlayerManager.Instance.PlayerSwapped -= SetPlayer;
        PlayerManager.Instance.PlayerSwapped += SetPlayer;
        SetPlayer(PlayerManager.Instance.Active);
    }

    void SetPlayer(Player player)
    {
        bool showCombatHud = player != null && player.kind == PlayerKind.Table;
        if (hotbar != null) hotbar.gameObject.SetActive(showCombatHud);
        if (vitals != null) vitals.gameObject.SetActive(showCombatHud);
    }

    void SetVisible(bool visible)
    {
        if (canvas != null) canvas.enabled = visible;
    }
}

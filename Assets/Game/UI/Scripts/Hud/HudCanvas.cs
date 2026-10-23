using UnityEngine;

// Hides the HUD canvas when GameManager says so (cutscenes). Full-screen menus share the canvas, so behind them (the
// shop, character sheet, run recap: the Menu state) only the hotbar and the vitals fade out. The pause menu shares it
// too, so pausing a cutscene shows the canvas again: the hotbar and vitals stay out of sight behind it then as well.
public class HudCanvas : MonoBehaviour
{
    [SerializeField] Canvas canvas;
    HotbarUI hotbar;
    PlayerVitalsUI vitals;
    CanvasGroup hotbarGroup, vitalsGroup;
    public static bool BehindMenu => GameManager.HasInstance && GameManager.Instance.State == GameState.Menu || PauseMenuUI.OverCutscene;

    void Awake()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        hotbar = GetComponentInChildren<HotbarUI>(true);
        vitals = GetComponentInChildren<PlayerVitalsUI>(true);
        hotbarGroup = Group(hotbar);
        vitalsGroup = Group(vitals);
    }

    static CanvasGroup Group(Component c) => c == null ? null : c.TryGetComponent<CanvasGroup>(out var g) ? g : c.gameObject.AddComponent<CanvasGroup>();

    void Update()
    {
        float alpha = BehindMenu ? 0f : 1f;
        if (hotbarGroup != null) hotbarGroup.alpha = alpha;
        if (vitalsGroup != null) vitalsGroup.alpha = alpha;
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

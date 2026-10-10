using UnityEngine;
using UnityEngine.UI;

// Escape during gameplay: time stops, the cursor frees. Resume, settings, the adventure
// menu (table player only) or quit. Escape again, or Resume, returns to the game.
public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] Button resumeButton;
    [SerializeField] Button settingsButton;
    [SerializeField] Button adventuresButton;
    [SerializeField] Button quitButton;
    Vector2? quitHome;
    [SerializeField] SettingsUI settings;
    [Tooltip("Hidden while settings are open so the two panels never overlap.")]
    [SerializeField] GameObject pausePanel;

    bool open;
    // The time scale when the menu opened (a hit stop may be slowing time); restored on close.
    // CombatManager's hit stop holds still while the game is Paused, so nothing else writes it meanwhile.
    float resumeTimeScale = 1f;
    public bool IsOpen => open;

    static TableLevelLoader Loader => TableManager.HasInstance ? TableManager.Instance.GetComponent<TableLevelLoader>() : null;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        resumeButton?.onClick.AddListener(Close);
        settingsButton?.onClick.AddListener(OpenSettings);
        adventuresButton?.onClick.AddListener(Adventures);
        quitButton?.onClick.AddListener(Quit);
    }

    void Start()
    {
        if (!InputManager.HasInstance) return;
        InputManager.Instance.PausePressed += Open;
        InputManager.Instance.CancelPressed += OnCancel;
    }

    // Disabled or destroyed while open: restore time and give back the Paused state.
    void OnDisable() => Close();

    void OnDestroy()
    {
        Close();
        if (InputManager.HasInstance)
        {
            InputManager.Instance.PausePressed -= Open;
            InputManager.Instance.CancelPressed -= OnCancel;
        }
    }

    public void Open()
    {
        if (open || root == null || !GameManager.HasInstance || GameManager.Instance.State != GameState.Explore) return;
        open = true;
        GameManager.Instance.Push(GameState.Paused);
        resumeTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        root.SetActive(true);
        settings?.Close();
        if (pausePanel != null) pausePanel.SetActive(true);
        bool table = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        if (adventuresButton != null) adventuresButton.gameObject.SetActive(table && Loader != null && !Loader.Busy && !(RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended));
        // Quit moves up into the adventures slot when that button is hidden, so the list has no gap.
        if (adventuresButton != null && quitButton != null && quitButton.transform is RectTransform quitRect && adventuresButton.transform is RectTransform advRect)
        {
            quitHome ??= quitRect.anchoredPosition;
            quitRect.anchoredPosition = adventuresButton.gameObject.activeSelf ? quitHome.Value : advRect.anchoredPosition;
        }
        resumeButton?.Select();
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (settings != null) settings.Close();
        if (root != null) root.SetActive(false);
        Time.timeScale = resumeTimeScale;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Paused);
    }

    void OnCancel()
    {
        if (!open) return;
        if (InputManager.HasInstance && InputManager.Instance.IsRebinding) return;
        if (settings != null && settings.IsOpen) { CloseSettings(); return; }
        Close();
    }

    void OpenSettings()
    {
        if (settings == null) return;
        if (pausePanel != null) pausePanel.SetActive(false);
        settings.Open();
    }

    // Settings' own Back button closes it; bring the pause panel back either way.
    void Update()
    {
        if (open && pausePanel != null && !pausePanel.activeSelf && (settings == null || !settings.IsOpen)) CloseSettings();
    }

    void CloseSettings()
    {
        settings?.Close();
        if (pausePanel != null) pausePanel.SetActive(true);
        settingsButton?.Select();
    }

    void Adventures()
    {
        Close();
        if (RunManager.HasInstance) RunManager.Instance.Abandon();
        Loader?.ShowSelection("Choose your next adventure");
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

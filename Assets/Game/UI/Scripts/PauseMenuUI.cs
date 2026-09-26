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
    [SerializeField] SettingsUI settings;
    [Tooltip("Hidden while settings are open so the two panels never overlap.")]
    [SerializeField] GameObject pausePanel;

    bool open;
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

    void OnDestroy()
    {
        if (InputManager.HasInstance)
        {
            InputManager.Instance.PausePressed -= Open;
            InputManager.Instance.CancelPressed -= OnCancel;
        }
        if (open) Time.timeScale = 1f;
    }

    public void Open()
    {
        if (open || root == null || !GameManager.HasInstance || GameManager.Instance.State != GameState.Explore) return;
        open = true;
        GameManager.Instance.Push(GameState.Paused);
        Time.timeScale = 0f;
        root.SetActive(true);
        settings?.Close();
        if (pausePanel != null) pausePanel.SetActive(true);
        bool table = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        if (adventuresButton != null) adventuresButton.gameObject.SetActive(table && Loader != null && !Loader.Busy);
        resumeButton?.Select();
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        settings?.Close();
        if (root != null) root.SetActive(false);
        Time.timeScale = 1f;
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

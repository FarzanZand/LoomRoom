using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Escape during gameplay: time stops, the cursor frees. Resume, settings, the adventure
// menu (table player only), the main menu or quit; leaving and quitting ask first, saying what a run loses.
// Escape again, or Resume, returns to the game.
public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] Button resumeButton;
    [SerializeField] Button settingsButton;
    [SerializeField] Button adventuresButton;
    [SerializeField] Button mainMenuButton;
    [SerializeField] Button quitButton;
    [SerializeField] SettingsUI settings;
    [Tooltip("Hidden while settings are open so the two panels never overlap.")]
    [SerializeField] GameObject pausePanel;
    [Header("Are you sure? (Main menu, Quit)")]
    [SerializeField] GameObject confirmPanel;
    [SerializeField] TMP_Text confirmText;
    [SerializeField] Button confirmYes, confirmNo;
    [SerializeField] string mainMenuScene = "Main Menu";
    [SerializeField, Tooltip("Said under the question while a run is on: what leaving costs.")]
    string runLoss = "Your run picks up at the start of this floor.";
    System.Action confirmed;

    bool open;
    // The time scale when the menu opened (a hit stop may be slowing time); restored on close.
    // CombatManager's hit stop holds still while the game is Paused, so nothing else writes it meanwhile.
    float resumeTimeScale = 1f;
    public bool IsOpen => open;

    static TableLevelLoader Loader => TableManager.Loader;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        resumeButton?.onClick.AddListener(Close);
        settingsButton?.onClick.AddListener(OpenSettings);
        adventuresButton?.onClick.AddListener(Adventures);
        mainMenuButton?.onClick.AddListener(() => Ask("Leave for the main menu?", ToMainMenu));
        quitButton?.onClick.AddListener(() => Ask("Quit the game?", Quit));
        confirmYes?.onClick.AddListener(() => { var go = confirmed; CloseConfirm(); go?.Invoke(); });
        confirmNo?.onClick.AddListener(CloseConfirm);
        if (confirmPanel != null) confirmPanel.SetActive(false);
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
        CloseConfirm();
        if (pausePanel != null) pausePanel.SetActive(true);
        bool table = PlayerManager.HasInstance && PlayerManager.Instance.ActiveKind == PlayerKind.Table;
        // The buttons sit in a vertical layout, so a hidden one leaves no gap.
        if (adventuresButton != null) adventuresButton.gameObject.SetActive(table && Loader != null && !Loader.Busy && !InRun);
        resumeButton?.Select();
    }

    static bool InRun => RunManager.HasInstance && RunManager.Instance.Running && !RunManager.Instance.Ended;

    // The question over the pause buttons, with what a run loses under it; Yes runs the action.
    void Ask(string question, System.Action action)
    {
        if (confirmPanel == null) { action?.Invoke(); return; }
        confirmed = action;
        if (confirmText != null) confirmText.text = InRun && !string.IsNullOrEmpty(runLoss) ? $"{question}\n<size=75%><color=#A3A3A3>{runLoss}</color></size>" : question;
        if (pausePanel != null) pausePanel.SetActive(false);
        confirmPanel.SetActive(true);
        confirmNo?.Select();
    }

    void CloseConfirm()
    {
        confirmed = null;
        if (confirmPanel == null || !confirmPanel.activeSelf) return;
        confirmPanel.SetActive(false);
        if (open && pausePanel != null) pausePanel.SetActive(true);
    }

    void ToMainMenu()
    {
        Close();
        StartCoroutine(LoadMainMenu());
    }

    IEnumerator LoadMainMenu()
    {
        Time.timeScale = 1f;
        if (ScreenManager.HasInstance) { ScreenManager.Instance.FadeToBlack(.4f); yield return new WaitForSecondsRealtime(.45f); }
        UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuScene);
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
        if (confirmPanel != null && confirmPanel.activeSelf) { CloseConfirm(); return; }
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
        if (open && pausePanel != null && !pausePanel.activeSelf && (settings == null || !settings.IsOpen) && (confirmPanel == null || !confirmPanel.activeSelf)) CloseSettings();
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

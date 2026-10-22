using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Escape during gameplay: time stops, the cursor frees. Resume, settings, the adventure
// menu (table player only), the main menu or quit; leaving and quitting ask first, saying what a run loses.
// Escape again, or Resume, returns to the game. It also opens over a cutscene or a conversation (the UI map's Pause, a
// pad's Start): time stops with it (timelines, a conversation's own clock), and the state underneath comes back on close.
public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] GameObject root;
    [SerializeField] Button resumeButton;
    [SerializeField] Button settingsButton;
    [SerializeField] Button adventuresButton;
    [SerializeField] Button mainMenuButton;
    [SerializeField] Button quitButton;
    [Tooltip("Opens the co-op screen (CoopMenuUI): host, join, leave.")]
    [SerializeField] Button coopButton;
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
    // The open conversation was paused by this menu (DialogueManager), so closing gives it its time back.
    bool pausedConversation;
    // Its choices under the menu: their canvas's clicks switched off, and the panels' own focus keeping (which would pull
    // a pad's or the keyboard's selection off Resume onto a choice) held while the menu is open.
    GraphicRaycaster conversationRaycaster;
    bool heldFocusKeeping;
    public bool IsOpen => open;
    // Open over a cutscene: the HUD it hid stays out of sight behind the menu (HudCanvas).
    public static bool OverCutscene { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => OverCutscene = false;

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
        coopButton?.onClick.AddListener(OpenCoop);
        // Settings and keys reach the disk when the settings screen closes.
        if (settings != null) settings.Closed += UserSettings.Save;
    }

    // The co-op screen in place of the pause menu.
    void OpenCoop()
    {
        Close();
        if (CoopMenuUI.Instance != null) CoopMenuUI.Instance.Open(CoopMenuUI.Mode.Pause);
    }

    void Start()
    {
        if (!InputManager.HasInstance) return;
        InputManager.Instance.PausePressed += OnPause;
        // While paused, Cancel comes only to this menu (InputManager).
        InputManager.Instance.PausedCancelPressed += OnCancel;
    }

    // Disabled or destroyed while open: restore time and give back the Paused state.
    void OnDisable() => Close();

    void OnDestroy()
    {
        Close();
        if (settings != null) settings.Closed -= UserSettings.Save;
        if (InputManager.HasInstance)
        {
            InputManager.Instance.PausePressed -= OnPause;
            InputManager.Instance.PausedCancelPressed -= OnCancel;
        }
    }

    // Pause again (a pad's Start) steps back like Cancel.
    void OnPause() { if (open) OnCancel(); else Open(); }

    // Over plain play, a cutscene or a conversation; not over another menu, the inventory or death.
    static bool CanPauseIn(GameState state) => state == GameState.Explore || state == GameState.Cutscene || state == GameState.Dialogue;

    public void Open()
    {
        if (open || root == null || !GameManager.HasInstance || !CanPauseIn(GameManager.Instance.State)) return;
        open = true;
        OverCutscene = GameManager.Instance.State == GameState.Cutscene;
        GameManager.Instance.Push(GameState.Paused);
        resumeTimeScale = Time.timeScale;
        // In co-op the game goes on for the others: the menu opens over it.
        if (!Coop.Running) Time.timeScale = 0f;
        // A conversation runs on its own clock (DialogueTime): stop it too.
        pausedConversation = PixelCrushers.DialogueSystem.DialogueManager.isConversationActive && !PixelCrushers.DialogueSystem.DialogueTime.isPaused;
        if (pausedConversation) PixelCrushers.DialogueSystem.DialogueManager.Pause();
        if (PixelCrushers.DialogueSystem.DialogueManager.isConversationActive) HoldConversationUI(true);
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

    // A conversation's choices stay where they are under the menu: no click reaches them, and the Dialogue System's
    // panels stop taking the selection back (with the Input Device Manager's Always Auto Focus they would, every fifth
    // of a second, so Space, Enter or a pad's South picked a choice behind the menu).
    void HoldConversationUI(bool hold)
    {
        if (hold)
        {
            if (!heldFocusKeeping) { heldFocusKeeping = PixelCrushers.UIPanel.monitorSelection; PixelCrushers.UIPanel.monitorSelection = false; }
            var ui = PixelCrushers.DialogueSystem.DialogueManager.dialogueUI as Component;
            var raycaster = ui != null ? ui.GetComponentInParent<GraphicRaycaster>() : null;
            if (raycaster != null && raycaster.enabled && !transform.IsChildOf(raycaster.transform)) { raycaster.enabled = false; conversationRaycaster = raycaster; }
            return;
        }
        if (heldFocusKeeping) { heldFocusKeeping = false; PixelCrushers.UIPanel.monitorSelection = true; }
        if (conversationRaycaster != null) conversationRaycaster.enabled = true;
        conversationRaycaster = null;
    }

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
        // Through the co-op manager: it leaves a session and drops the Network Manager (kept across scene loads).
        if (MultiplayerManager.HasInstance) MultiplayerManager.Instance.ToMainMenu();
        else UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuScene);
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (settings != null) settings.Close();
        if (root != null) root.SetActive(false);
        if (!Coop.Running) Time.timeScale = resumeTimeScale;
        if (pausedConversation) { pausedConversation = false; PixelCrushers.DialogueSystem.DialogueManager.Unpause(); }
        HoldConversationUI(false);
        OverCutscene = false;
        if (GameManager.HasInstance) GameManager.Instance.Pop(GameState.Paused);
        // The press that closed the menu doesn't also click or skip what is under it.
        if (InputManager.HasInstance) InputManager.Instance.HoldPresses();
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

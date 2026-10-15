using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The main menu (Scenes/Main Menu, the first scene of a build): the Dungeon Master leaning over the table in a teal
// haze, his eyes burning, one hand held out over a little ruin with a pewter figure in it, fog rolling across the table
// (UI/Main Menu: pose clip, ruins, fog shader, look profile; Tools > LoomRoom > UI > Rebuild Main Menu Ruins), the
// painted title, and Continue / New game / Load game / Quit. There are three save slots (AdventureSave.Slot):
// Continue plays the newest save, New game asks whether to play the intro or skip it (starting on the first morning,
// right after the intro dungeon) and starts in a free slot (or asks which save to replace when all are taken),
// Load game lists the saves to play or delete. Playing fades to black and loads the game's scene. The set, buttons,
// save list, title and fade are authored in the scene (Tools > LoomRoom > UI > Build Main Menu Saves); this binds them.
// Co-op asks which save to play together with (an empty slot starts a new game there) and opens the game on the co-op
// screen. The menu is drawn smooth, not pixel-lit, and the
// scene has no managers, so this switches pixel lighting off, frees the cursor and reads the music volume itself.
public class MainMenuUI : MonoBehaviour
{
    [System.Serializable]
    public class SlotRow
    {
        public Button button;
        public TMP_Text title, details, time;
        public Button delete;
    }

    [Tooltip("Continue: the save played last. Hidden while there is no save.")]
    [SerializeField] Button playButton;
    [SerializeField] TMP_Text playLabel;
    [SerializeField] Button newGameButton;
    [Tooltip("Opens the save list. Hidden while there is no save.")]
    [SerializeField] Button loadButton;
    [SerializeField] Button quitButton;
    [Tooltip("Co-op: asks which save to play together with, then the game opens with the co-op screen (host or join).")]
    [SerializeField] Button coopButton;
    [Tooltip("The column of menu buttons, hidden while the save list is open.")]
    [SerializeField] GameObject buttons;

    [Header("New game")]
    [Tooltip("Asked when New game is pressed: play the intro, or skip it.")]
    [SerializeField] GameObject newGamePanel;
    [SerializeField] Button playIntroButton;
    [Tooltip("Starts on the first morning, as if the intro dungeon had just been played (ProgressionManager.SkipIntroRequested).")]
    [SerializeField] Button skipIntroButton;
    [SerializeField] Button newGameBack;

    [Header("Saves")]
    [SerializeField] GameObject savesPanel;
    [SerializeField] TMP_Text savesTitle;
    [Tooltip("One row per save slot, in slot order.")]
    [SerializeField] SlotRow[] slotRows = new SlotRow[0];
    [SerializeField] Button savesBack;
    [Tooltip("Asks before a save is deleted or replaced.")]
    [SerializeField] GameObject confirmPanel;
    [SerializeField] TMP_Text confirmText;
    [SerializeField] Button confirmYes, confirmNo;
    [Tooltip("Black over everything: fades out when the menu opens and in before the game loads.")]
    [SerializeField] CanvasGroup fade;
    [SerializeField, Min(.05f)] float fadeSeconds = 1f;
    [Tooltip("The scene Play loads (it must be in the build's scene list).")]
    [SerializeField] string gameScene = "Room";

    [Header("Set")]
    [Tooltip("The Pixel Look library: its materials are switched back to their own shaders, so the menu is drawn smooth.")]
    [SerializeField] PixelLookLibrary pixelLook;
    [SerializeField] Animator dungeonMaster;
    [Tooltip("The Dungeon Master's state while he waits (UI/Main Menu/Menu DM.controller: reaching over the table).")]
    [SerializeField] string dungeonMasterState = "Reach";
    [Tooltip("The Dungeon Master's eyes: burning slits (UI/Main Menu/Menu Eye.shader; colours on their materials).")]
    [SerializeField] Renderer[] eyes = new Renderer[0];
    [Tooltip("How much the eyes' glow swells and fades, slowly and unevenly.")]
    [SerializeField, Range(0f, .6f)] float eyePulse = .25f;
    [SerializeField] AudioSource music;

    bool leaving;
    enum SavesMode { Load, Replace, Coop }
    SavesMode mode;
    bool replaceSkipsIntro;
    bool coop;            // the save list and the new game question are for co-op
    int newGameSlot;      // co-op: the empty slot picked for the new game (0: the first free one)
    System.Action confirmed;

    void Start()
    {
        // No GameManager here (it owns the cursor in the game's scene): the menu needs a free pointer.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (pixelLook != null) pixelLook.SetPixelLighting(false);   // the game's ScreenManager switches it back on
        if (dungeonMaster != null && dungeonMaster.runtimeAnimatorController != null)
        {
            dungeonMaster.applyRootMotion = false;
            dungeonMaster.Play(dungeonMasterState, 0, Random.value);
        }
        SetEyes(1f);
        if (music != null)
        {
            music.volume *= PlayerPrefs.GetFloat(AudioManager.K_MASTER, 1f) * PlayerPrefs.GetFloat(AudioManager.K_MUSIC, .8f);
            if (!music.isPlaying) music.Play();
        }
        if (playLabel != null) playLabel.text = "Continue";
        playButton?.onClick.AddListener(Continue);
        newGameButton?.onClick.AddListener(OpenNewGame);
        loadButton?.onClick.AddListener(() => OpenSaves(SavesMode.Load));
        playIntroButton?.onClick.AddListener(() => NewGame(false));
        skipIntroButton?.onClick.AddListener(() => NewGame(true));
        newGameBack?.onClick.AddListener(CloseNewGame);
        quitButton?.onClick.AddListener(Quit);
        savesBack?.onClick.AddListener(CloseSaves);
        confirmYes?.onClick.AddListener(() => { var then = confirmed; CloseConfirm(); then?.Invoke(); });
        confirmNo?.onClick.AddListener(CloseConfirm);
        for (int i = 0; i < slotRows.Length; i++)
        {
            int slot = i + 1; var row = slotRows[i];
            row.button?.onClick.AddListener(() => PickSlot(slot));
            row.delete?.onClick.AddListener(() => AskDelete(slot));
        }
        if (savesPanel != null) savesPanel.SetActive(false);
        if (newGamePanel != null) newGamePanel.SetActive(false);
        if (confirmPanel != null) confirmPanel.SetActive(false);
        RefreshButtons();
        coopButton?.onClick.AddListener(PlayCoop);
        if (fade != null) StartCoroutine(Fade(1f, 0f, null));
        // Started with -host or -join (co-op from the command line): straight into the game.
        foreach (var arg in System.Environment.GetCommandLineArgs())
            if (arg.ToLowerInvariant() == "-host" || arg.ToLowerInvariant() == "-join") { leaving = true; SceneManager.LoadScene(gameScene); break; }
    }

    void Update()
    {
        if (eyePulse > 0f) SetEyes(1f + (Mathf.PerlinNoise(Time.unscaledTime * .45f, 7.1f) - .5f) * 2f * eyePulse);
        // A pad or the keyboard can always reach the buttons, even after a mouse click took the selection away.
        if (!leaving && UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == null
            && (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame || Gamepad.current != null && Gamepad.current.dpad.ReadValue() != Vector2.zero))
            SelectFirst();
        // Escape (or a pad's east button) steps back: the question first, then the save list.
        bool back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame || Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        if (back && !leaving)
        {
            if (confirmPanel != null && confirmPanel.activeSelf) CloseConfirm();
            else if (savesPanel != null && savesPanel.activeSelf) CloseSaves();
            else if (newGamePanel != null && newGamePanel.activeSelf) CloseNewGame();
        }
    }

    void SelectFirst()
    {
        if (confirmPanel != null && confirmPanel.activeSelf) { confirmNo?.Select(); return; }
        if (savesPanel != null && savesPanel.activeSelf)
        {
            foreach (var row in slotRows) if (row.button != null && row.button.interactable) { row.button.Select(); return; }
            savesBack?.Select(); return;
        }
        if (newGamePanel != null && newGamePanel.activeSelf) { playIntroButton?.Select(); return; }
        if (playButton != null && playButton.gameObject.activeInHierarchy) playButton.Select();
        else newGameButton?.Select();
    }

    // Continue and Load game only while a save exists.
    void RefreshButtons()
    {
        bool any = AdventureSave.SaveExists;
        if (playButton != null) playButton.gameObject.SetActive(any);
        if (loadButton != null) loadButton.gameObject.SetActive(any);
        SelectFirst();
    }

    void Play(int slot)
    {
        if (leaving) return;
        leaving = true;
        AdventureSave.Slot = slot;
        MultiplayerManager.CoopFromMenu = coop;
        StartCoroutine(Fade(0f, 1f, () => SceneManager.LoadScene(gameScene)));
    }

    void Continue()
    {
        int slot = AdventureSave.LatestSlot;
        if (slot > 0) Play(slot); else OpenNewGame();
    }

    // Co-op: the save to play together with (a guest's own spoils, sheet and trophies go to it too); with no save yet,
    // straight to the new game question. The game then opens on the co-op screen.
    void PlayCoop()
    {
        if (leaving) return;
        coop = true;
        if (AdventureSave.SaveExists) OpenSaves(SavesMode.Coop); else OpenNewGame();
    }

    void OpenNewGame()
    {
        if (leaving) return;
        if (buttons != null) buttons.SetActive(false);
        if (savesPanel != null) savesPanel.SetActive(false);
        if (newGamePanel != null) newGamePanel.SetActive(true);
        else { NewGame(false); return; }
        SelectFirst();
    }

    void CloseNewGame()
    {
        coop = false; newGameSlot = 0;
        if (newGamePanel != null) newGamePanel.SetActive(false);
        if (buttons != null) buttons.SetActive(true);
        RefreshButtons();
    }

    // A new game goes in the first free slot; with every slot taken the player picks one to replace.
    void NewGame(bool skipIntro)
    {
        if (leaving) return;
        int slot = newGameSlot > 0 ? newGameSlot : AdventureSave.FreeSlot;
        if (slot == 0) { replaceSkipsIntro = skipIntro; if (newGamePanel != null) newGamePanel.SetActive(false); OpenSaves(SavesMode.Replace); return; }
        StartNew(slot, skipIntro);
    }

    void StartNew(int slot, bool skipIntro)
    {
        AdventureSave.DeleteSlot(slot);   // nothing there, or the save the player chose to replace
        ProgressionManager.SkipIntroRequested = skipIntro;
        Play(slot);
    }

    // ── The save list ────────────────────────────────────────────────

    void OpenSaves(SavesMode m)
    {
        mode = m;
        if (savesTitle != null) savesTitle.text = mode == SavesMode.Load ? "Load game" : mode == SavesMode.Coop ? "Play together with which save?" : "All saves are taken. Pick one to replace.";
        if (buttons != null) buttons.SetActive(false);
        if (savesPanel != null) savesPanel.SetActive(true);
        FillSaves();
        SelectFirst();
    }

    void CloseSaves()
    {
        coop = false;
        CloseConfirm();
        if (savesPanel != null) savesPanel.SetActive(false);
        if (buttons != null) buttons.SetActive(true);
        RefreshButtons();
    }

    void FillSaves()
    {
        for (int i = 0; i < slotRows.Length; i++)
        {
            var row = slotRows[i]; int slot = i + 1;
            var save = AdventureSave.Summarize(slot);
            bool startHere = !save.exists && mode == SavesMode.Coop;   // co-op: an empty slot starts a new game
            if (row.title != null) row.title.text = !save.exists ? "Empty" : string.IsNullOrWhiteSpace(save.name) ? $"Save {slot}" : save.name;
            if (row.details != null) row.details.text = startHere ? "Start a new game here" : !save.exists ? "" : !save.readable ? "This save can't be read" : Details(save);
            if (row.time != null) row.time.text = save.exists ? save.lastPlayed.ToString("d MMM, HH:mm") : "";
            if (row.button != null)
            {
                row.button.interactable = startHere || save.exists && (mode == SavesMode.Replace || save.readable);
                if (row.button.GetComponent<CanvasGroup>() is CanvasGroup group) group.alpha = save.exists || startHere ? 1f : .45f;
            }
            if (row.delete != null) row.delete.gameObject.SetActive(save.exists && mode == SavesMode.Load);
        }
    }

    static string Details(AdventureSave.Summary save)
    {
        var parts = new System.Collections.Generic.List<string>
        {
            save.runs == 1 ? "1 run" : $"{save.runs} runs",
            save.trophies == 1 ? "1 trophy" : $"{save.trophies} trophies",
            $"{save.spoils} spoils",
        };
        if (save.runFloor > 0) parts.Add($"in a run, floor {save.runFloor}");
        return string.Join("   ", parts);
    }

    void PickSlot(int slot)
    {
        if (leaving) return;
        if (mode == SavesMode.Load) { Play(slot); return; }
        if (mode == SavesMode.Coop)
        {
            if (AdventureSave.SlotExists(slot)) Play(slot);
            else { newGameSlot = slot; OpenNewGame(); }
            return;
        }
        Ask($"Replace {SaveName(slot)}? It will be gone for good.", () => StartNew(slot, replaceSkipsIntro));
    }

    void AskDelete(int slot) => Ask($"Delete {SaveName(slot)}? It will be gone for good.", () =>
    {
        AdventureSave.DeleteSlot(slot);
        if (AdventureSave.SaveExists) { FillSaves(); SelectFirst(); }
        else CloseSaves();
    });

    static string SaveName(int slot)
    {
        var save = AdventureSave.Summarize(slot);
        return string.IsNullOrWhiteSpace(save.name) ? $"Save {slot}" : save.name;
    }

    void Ask(string question, System.Action yes)
    {
        confirmed = yes;
        if (confirmText != null) confirmText.text = question;
        if (confirmPanel != null) confirmPanel.SetActive(true);
        SelectFirst();
    }

    void CloseConfirm()
    {
        confirmed = null;
        if (confirmPanel != null && confirmPanel.activeSelf) { confirmPanel.SetActive(false); SelectFirst(); }
    }

    void Quit()
    {
        if (leaving) return;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    MaterialPropertyBlock eyeBlock;

    void SetEyes(float strength)
    {
        eyeBlock ??= new MaterialPropertyBlock();
        foreach (var eye in eyes)
        {
            if (eye == null) continue;
            eye.GetPropertyBlock(eyeBlock);
            eyeBlock.SetFloat("_Intensity", strength);
            eye.SetPropertyBlock(eyeBlock);
        }
    }

    IEnumerator Fade(float from, float to, System.Action then)
    {
        fade.gameObject.SetActive(true);
        fade.blocksRaycasts = true;
        float startMusic = music != null ? music.volume : 0f;
        for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / fadeSeconds);
            fade.alpha = Mathf.Lerp(from, to, k);
            if (music != null && to > from) music.volume = startMusic * (1f - k);   // the music goes with the light
            yield return null;
        }
        fade.alpha = to;
        fade.blocksRaycasts = to > 0f;
        if (to <= 0f) fade.gameObject.SetActive(false);
        then?.Invoke();
    }
}

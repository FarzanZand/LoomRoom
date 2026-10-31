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
// Continue plays the newest save, New game asks for the player's name (the save is known by it) and starts with the
// intro in a free slot (or asks which save to replace when all are taken),
// Load game lists the saves to play or delete, Settings opens the game's settings screen (its InputManager is here
// only for rebinding keys). Playing fades to black and loads the game's scene. The set, buttons,
// save list, title and fade are authored in the scene (Tools > LoomRoom > UI > Build Main Menu Saves); this binds them. The menu is drawn smooth, not pixel-lit, and the
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
    [Tooltip("Opens the game's settings screen (the same prefab as the pause menu's).")]
    [SerializeField] Button settingsButton;
    [SerializeField] SettingsUI settings;
    [Tooltip("The column of menu buttons, hidden while the save list is open.")]
    [SerializeField] GameObject buttons;

    [Header("New game")]
    [Tooltip("Asked when New game is pressed: the player's name, then Begin (the intro).")]
    [SerializeField] GameObject newGamePanel;
    [Tooltip("The player's name: the new save is known by it (AdventureSave.NewCharacterName).")]
    [SerializeField] TMP_InputField nameField;
    [SerializeField] Button playIntroButton;
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
    enum SavesMode { Load, Replace }
    SavesMode mode;
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
            musicBase = music.volume;
            SetMusicVolume();
            AudioManager.VolumeStored += SetMusicVolume;   // the settings screen's sliders, live
            if (!music.isPlaying) music.Play();
        }
        settingsButton?.onClick.AddListener(OpenSettings);
        if (settings != null) settings.Closed += CloseSettings;
        AdventureSave.NewCharacterName = null;   // a name typed earlier but never played
        ProgressionManager.SkipIntroRequested = false;
        AdventureSave.MigrateOldSaves();   // saves from before the rename, brought over once
        if (playLabel != null) playLabel.text = "Continue";
        playButton?.onClick.AddListener(Continue);
        newGameButton?.onClick.AddListener(OpenNewGame);
        loadButton?.onClick.AddListener(() => OpenSaves(SavesMode.Load));
        playIntroButton?.onClick.AddListener(NewGame);
        newGameBack?.onClick.AddListener(CloseNewGame);
        if (nameField != null)
        {
            nameField.characterLimit = AdventureSave.NameLength;
            nameField.onValidateInput = (text, index, c) => Typeable(c) ? c : '\0';
            nameField.onSubmit.AddListener(_ => playIntroButton?.Select());
            if (nameField.placeholder is TMP_Text hint) hint.text = AdventureSave.DefaultName;
        }
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
        if (fade != null) StartCoroutine(Fade(1f, 0f, null));
    }

    float musicBase = 1f;
    void SetMusicVolume()
    {
        if (music != null) music.volume = musicBase * AudioManager.StoredVolume(AudioManager.Channel.Master) * AudioManager.StoredVolume(AudioManager.Channel.Music);
    }

    void OnDestroy()
    {
        AudioManager.VolumeStored -= SetMusicVolume;
        if (settings != null) settings.Closed -= CloseSettings;
    }

    void OpenSettings()
    {
        if (settings == null || leaving) return;
        if (buttons != null) buttons.SetActive(false);
        settings.Open();
    }

    void CloseSettings()
    {
        UserSettings.Save();   // settings and keys reach the disk
        if (buttons != null) buttons.SetActive(true);
        settingsButton?.Select();
    }

    void Update()
    {
        if (eyePulse > 0f) SetEyes(1f + (Mathf.PerlinNoise(Time.unscaledTime * .45f, 7.1f) - .5f) * 2f * eyePulse);
        // A pad or the keyboard can always reach the buttons, even after a mouse click took the selection away.
        if (!leaving && UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == null
            && (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame || Gamepad.current != null && Gamepad.current.dpad.ReadValue() != Vector2.zero))
            SelectFirst();
        // A pad can't type, and a focused name field keeps the pad's moves: its first move or press leaves the field for the
        // start buttons (an empty name becomes the default one).
        if (!leaving && nameField != null && nameField.isFocused && Gamepad.current != null
            && (Gamepad.current.dpad.ReadValue() != Vector2.zero || Gamepad.current.leftStick.ReadValue().sqrMagnitude > .25f || Gamepad.current.buttonSouth.wasPressedThisFrame))
        {
            nameField.DeactivateInputField();
            playIntroButton?.Select();
        }
        // Escape (or a pad's east button) steps back: the question first, then the save list.
        bool back = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame || Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        if (back && !leaving)
        {
            // Escape that ended a rebind this frame (it finishes before Update) only cancels the rebind.
            if (settings != null && settings.IsOpen) { if (!InputManager.HasInstance || !InputManager.Instance.IsRebinding && !InputManager.Instance.RebindEndedThisFrame) settings.Close(); }
            else if (confirmPanel != null && confirmPanel.activeSelf) CloseConfirm();
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
        if (newGamePanel != null && newGamePanel.activeSelf) { if (nameField != null) nameField.Select(); else playIntroButton?.Select(); return; }
        if (playButton != null && playButton.gameObject.activeInHierarchy) playButton.Select();
        else newGameButton?.Select();
    }

    // Continue and Load game only while a save exists.
    void RefreshButtons()
    {
        bool any = AdventureSave.SaveExists;
        if (playButton != null) playButton.gameObject.SetActive(any);
        if (loadButton != null) loadButton.gameObject.SetActive(any);
        // Keys and a pad walk the column top to bottom: the scene's own links sent every button to Quit, so New game,
        // Load game and Settings couldn't be reached.
        var column = new System.Collections.Generic.List<Selectable>();
        var parent = newGameButton != null ? newGameButton.transform.parent : null;
        if (parent != null) foreach (Transform child in parent) if (child.TryGetComponent<Selectable>(out var button)) column.Add(button);
        MenuFocus.Chain(column);
        SelectFirst();
    }

    void Play(int slot)
    {
        if (leaving) return;
        leaving = true;
        AdventureSave.Slot = slot;
        StartCoroutine(Fade(0f, 1f, () => SceneManager.LoadScene(gameScene)));
    }

    void Continue()
    {
        int slot = AdventureSave.LatestSlot;
        if (slot > 0) Play(slot); else OpenNewGame();
    }

    void OpenNewGame()
    {
        if (leaving) return;
        if (buttons != null) buttons.SetActive(false);
        if (newGamePanel != null) newGamePanel.SetActive(true);
        else { NewGame(); return; }
        nameField?.SetTextWithoutNotify("");
        SelectFirst();
    }

    // The typed name, or the default one when the field is left empty (the placeholder shows it).
    string ChosenName
    {
        get
        {
            var typed = AdventureSave.CleanName(nameField != null ? nameField.text : "");
            return typed.Length > 0 ? typed : AdventureSave.DefaultName;
        }
    }

    // Only letters the name's font can draw (the save list and the character sheet show it too); no control characters.
    // Checked against the font file, so asking adds nothing to the font asset.
    bool Typeable(char c)
    {
        if (char.IsControl(c) || char.IsSurrogate(c)) return false;
        var font = nameField.textComponent != null ? nameField.textComponent.font : null;
        return c == ' ' || font == null || font.HasCharacter(c, true, false) || font.sourceFontFile != null && font.sourceFontFile.HasCharacter(c);
    }

    void CloseNewGame()
    {
        if (newGamePanel != null) newGamePanel.SetActive(false);
        if (buttons != null) buttons.SetActive(true);
        RefreshButtons();
    }

    // A new game goes in the first free slot; with every slot taken the player picks one to replace.
    void NewGame()
    {
        if (leaving) return;
        int slot = AdventureSave.FreeSlot;
        if (slot == 0) { if (newGamePanel != null) newGamePanel.SetActive(false); OpenSaves(SavesMode.Replace); return; }
        StartNew(slot);
    }

    void StartNew(int slot)
    {
        AdventureSave.DeleteSlot(slot);   // nothing there, or the save the player chose to replace
        AdventureSave.NewCharacterName = nameField != null ? ChosenName : null;
        Play(slot);
    }

    // ── The save list ────────────────────────────────────────────────

    void OpenSaves(SavesMode m)
    {
        mode = m;
        if (savesTitle != null) savesTitle.text = mode == SavesMode.Load ? "Load game" : "All saves are taken. Pick one to replace.";
        if (buttons != null) buttons.SetActive(false);
        if (savesPanel != null) savesPanel.SetActive(true);
        FillSaves();
        SelectFirst();
    }

    void CloseSaves()
    {
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
            if (row.title != null) row.title.text = !save.exists ? "Empty" : Name(save, slot);
            if (row.details != null) row.details.text = !save.exists ? "" : !save.readable ? "This save can't be read" : Details(save);
            if (row.time != null) row.time.text = save.exists ? save.lastPlayed.ToString("d MMM, HH:mm") : "";
            if (row.button != null)
            {
                row.button.interactable = save.exists && (mode == SavesMode.Replace || save.readable);
                if (row.button.GetComponent<CanvasGroup>() is CanvasGroup group) group.alpha = save.exists ? 1f : .45f;
            }
            if (row.delete != null) row.delete.gameObject.SetActive(save.exists && mode == SavesMode.Load);
        }
        // Keys and a pad move between the saves, their Delete buttons and Back by where they sit (Down stayed on the
        // first save).
        if (savesPanel != null) { Canvas.ForceUpdateCanvases(); MenuFocus.LinkNearest(savesPanel.GetComponentsInChildren<Selectable>()); }
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
        Ask($"Replace {SaveName(slot)}? It will be gone for good.", () => StartNew(slot));
    }

    void AskDelete(int slot) => Ask($"Delete {SaveName(slot)}? It will be gone for good.", () =>
    {
        AdventureSave.DeleteSlot(slot);
        if (AdventureSave.SaveExists) { FillSaves(); SelectFirst(); }
        else CloseSaves();
    });

    static string SaveName(int slot) => Name(AdventureSave.Summarize(slot), slot);

    // A save is known by the player's name; saves from before names were asked for have none and go by their slot.
    static string Name(AdventureSave.Summary save, int slot) => string.IsNullOrWhiteSpace(save.name) ? $"Save {slot}" : save.name;

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

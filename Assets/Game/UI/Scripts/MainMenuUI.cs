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
// save list, title and fade are authored in the scene (Tools > LoomRoom > UI > Build Main Menu Saves); this binds them.
// Co-op opens the co-op menu: your name, Host a game, or a friend's code and Join; then which save to play with (an
// empty slot starts a new game there: a host is asked about the intro, a guest plays the host's game), and the game
// opens already hosting (the lobby, with the code to send) or joining (MultiplayerManager.StartFromMenu). The menu is
// drawn smooth, not pixel-lit, and the
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
    [Tooltip("Opens the co-op menu.")]
    [SerializeField] Button coopButton;
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
    [Header("Co-op")]
    [Tooltip("The co-op menu: your name, Host a game, or a friend's code and Join.")]
    [SerializeField] GameObject coopPanel;
    [SerializeField] TMP_InputField coopName, coopCode;
    [SerializeField] Button coopHost, coopJoin, coopBack;
    [Tooltip("What to do, or what went wrong (no code typed).")]
    [SerializeField] TMP_Text coopStatus;

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
    static bool skippedForCommandLine;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics() => skippedForCommandLine = false;
    enum SavesMode { Load, Replace, Coop }
    SavesMode mode;
    bool coop;            // the save list and the new game question are for co-op
    MultiplayerManager.MenuStart coopStart;   // host, or join coopJoinCode
    string coopJoinCode;
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
        coopButton?.onClick.AddListener(OpenCoop);
        coopHost?.onClick.AddListener(CoopHost);
        coopJoin?.onClick.AddListener(CoopJoin);
        coopBack?.onClick.AddListener(CloseCoop);
        coopCode?.onSubmit.AddListener(_ => CoopJoin());
        if (coopPanel != null) coopPanel.SetActive(false);
        if (fade != null) StartCoroutine(Fade(1f, 0f, null));
        // Started with -host or -join (co-op from the command line): straight into the game, once (coming back to the
        // menu later stays here).
        if (!skippedForCommandLine)
        {
            var args = System.Environment.GetCommandLineArgs();
            foreach (var arg in args)
                if (arg.ToLowerInvariant() == "-host" || arg.ToLowerInvariant() == "-join")
                {
                    // A new save made this way is named after the player (-name).
                    for (int i = 0; i + 1 < args.Length; i++) if (args[i].ToLowerInvariant() == "-name") AdventureSave.NewCharacterName = AdventureSave.CleanName(args[i + 1]);
                    skippedForCommandLine = leaving = true; SceneManager.LoadScene(gameScene); break;
                }
        }
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
            if (settings != null && settings.IsOpen) { if (!InputManager.HasInstance || !InputManager.Instance.IsRebinding) settings.Close(); }
            else if (confirmPanel != null && confirmPanel.activeSelf) CloseConfirm();
            else if (savesPanel != null && savesPanel.activeSelf) CloseSaves();
            else if (newGamePanel != null && newGamePanel.activeSelf) CloseNewGame();
            else if (coopPanel != null && coopPanel.activeSelf) CloseCoop();
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
        if (coopPanel != null && coopPanel.activeSelf) { coopHost?.Select(); return; }
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
        MultiplayerManager.StartFromMenu = coop ? coopStart : MultiplayerManager.MenuStart.None;
        MultiplayerManager.MenuJoinCode = coop && coopStart == MultiplayerManager.MenuStart.Join ? coopJoinCode : null;
        StartCoroutine(Fade(0f, 1f, () => SceneManager.LoadScene(gameScene)));
    }

    void Continue()
    {
        int slot = AdventureSave.LatestSlot;
        if (slot > 0) Play(slot); else OpenNewGame();
    }

    // ── The co-op menu ───────────────────────────────────────────────

    void OpenCoop()
    {
        if (leaving) return;
        coop = false; newGameSlot = 0;
        if (coopPanel == null) return;
        if (buttons != null) buttons.SetActive(false);
        if (savesPanel != null) savesPanel.SetActive(false);
        if (newGamePanel != null) newGamePanel.SetActive(false);
        coopPanel.SetActive(true);
        if (coopName != null) coopName.text = PlayerPrefs.GetString(MultiplayerManager.NameKey, "");
        // A code copied from a chat fills itself in.
        if (coopCode != null && string.IsNullOrWhiteSpace(coopCode.text) && ClipboardCode(out var copied)) coopCode.text = copied;
        CoopStatus("Host a game, or join a friend's with their code.", false);
        SelectFirst();
    }

    void CloseCoop()
    {
        if (coopPanel != null) coopPanel.SetActive(false);
        if (buttons != null) buttons.SetActive(true);
        RefreshButtons();
    }

    string CoopName => AdventureSave.CleanName(coopName != null ? coopName.text : "");

    void KeepName()
    {
        if (coopName == null) return;
        PlayerPrefs.SetString(MultiplayerManager.NameKey, coopName.text.Trim());
        PlayerPrefs.Save();
    }

    // Hosting: the save to play (a new game asks about the intro); the game opens with the lobby and its code.
    void CoopHost()
    {
        if (leaving) return;
        KeepName();
        coop = true; coopStart = MultiplayerManager.MenuStart.Host;
        if (coopPanel != null) coopPanel.SetActive(false);
        if (AdventureSave.SaveExists) OpenSaves(SavesMode.Coop); else OpenNewGame();
    }

    // Joining: the save whose spoils, sheet and trophies are this player's (an empty slot makes a new one); the game
    // opens joining the friend's, which decides what is played.
    void CoopJoin()
    {
        if (leaving) return;
        KeepName();
        string code = coopCode != null ? coopCode.text.Trim().ToUpperInvariant() : "";
        if (code.Length == 0 && ClipboardCode(out var copied)) code = copied;
        if (code.Length == 0) { CoopStatus("Type your friend's code first.", true); coopCode?.Select(); return; }
        coop = true; coopStart = MultiplayerManager.MenuStart.Join; coopJoinCode = code;
        if (coopPanel != null) coopPanel.SetActive(false);
        if (AdventureSave.SaveExists) OpenSaves(SavesMode.Coop);
        else StartNew(AdventureSave.FreeSlot);
    }

    void CoopStatus(string text, bool error)
    {
        if (coopStatus == null) return;
        coopStatus.text = text;
        coopStatus.color = error ? new Color(.92f, .45f, .4f) : (playLabel != null ? playLabel.color : Color.white);
    }

    // A join code on the clipboard (six letters and digits, as the host's Copy gives).
    static bool ClipboardCode(out string code)
    {
        code = (GUIUtility.systemCopyBuffer ?? "").Trim().ToUpperInvariant();
        if (code.Length != 6) return false;
        foreach (var c in code) if (!char.IsLetterOrDigit(c)) return false;
        return true;
    }

    void OpenNewGame()
    {
        if (leaving) return;
        if (buttons != null) buttons.SetActive(false);
        if (savesPanel != null) savesPanel.SetActive(false);
        if (newGamePanel != null) newGamePanel.SetActive(true);
        else { NewGame(); return; }
        // Co-op: the name typed in the co-op menu is the new save's too, unless changed here.
        nameField?.SetTextWithoutNotify(coop ? CoopName : "");
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
        if (coop) { OpenCoop(); return; }   // back to the co-op menu
        newGameSlot = 0;
        if (buttons != null) buttons.SetActive(true);
        RefreshButtons();
    }

    // A new game goes in the first free slot; with every slot taken the player picks one to replace.
    void NewGame()
    {
        if (leaving) return;
        int slot = newGameSlot > 0 ? newGameSlot : AdventureSave.FreeSlot;
        if (slot == 0) { if (newGamePanel != null) newGamePanel.SetActive(false); OpenSaves(SavesMode.Replace); return; }
        StartNew(slot);
    }

    void StartNew(int slot)
    {
        AdventureSave.DeleteSlot(slot);   // nothing there, or the save the player chose to replace
        // A guest's new save (no New game question) is named after the player.
        bool joining = coop && coopStart == MultiplayerManager.MenuStart.Join;
        AdventureSave.NewCharacterName = joining ? (CoopName.Length > 0 ? CoopName : AdventureSave.DefaultName) : nameField != null ? ChosenName : null;
        Play(slot);
    }

    // ── The save list ────────────────────────────────────────────────

    void OpenSaves(SavesMode m)
    {
        mode = m;
        if (savesTitle != null) savesTitle.text = mode == SavesMode.Load ? "Load game" : mode == SavesMode.Coop ? (coopStart == MultiplayerManager.MenuStart.Join ? "Join with which save?" : "Host with which save?") : "All saves are taken. Pick one to replace.";
        if (buttons != null) buttons.SetActive(false);
        if (savesPanel != null) savesPanel.SetActive(true);
        FillSaves();
        SelectFirst();
    }

    void CloseSaves()
    {
        CloseConfirm();
        if (savesPanel != null) savesPanel.SetActive(false);
        if (coop) { OpenCoop(); return; }   // back to the co-op menu
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
            if (row.title != null) row.title.text = !save.exists ? "Empty" : Name(save, slot);
            if (row.details != null) row.details.text = startHere ? (coopStart == MultiplayerManager.MenuStart.Join ? "Start a new save here" : "Start a new game here") : !save.exists ? "" : !save.readable ? "This save can't be read" : Details(save);
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
            else if (coopStart == MultiplayerManager.MenuStart.Join) StartNew(slot);   // the host's game decides what is played
            else { newGameSlot = slot; OpenNewGame(); }
            return;
        }
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

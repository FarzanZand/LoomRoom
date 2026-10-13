using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The main menu (Scenes/Main Menu, the first scene of a build): the table in the dark from the player's seat, the
// Dungeon Master waiting behind it with his eyes lit, the title, and Continue (New game) / Skip intro / Quit. Play fades to black and
// loads the game's scene. The set, buttons, title and fade are authored in the scene; this binds them. The menu scene
// has no managers, so this sets the Pixel Lit shader's globals, frees the cursor and reads the music volume itself.
public class MainMenuUI : MonoBehaviour
{
    [SerializeField] Button playButton;
    [SerializeField] TMP_Text playLabel;
    [Tooltip("Plays like New game / Continue, but straight to the room: the intro and the character sheet count as done.")]
    [SerializeField] Button skipIntroButton;
    [SerializeField] Button quitButton;
    [Tooltip("Co-op: the game opens with the co-op screen (host or join; a new game asks before its intro).")]
    [SerializeField] Button coopButton;
    [Tooltip("Black over everything: fades out when the menu opens and in before the game loads.")]
    [SerializeField] CanvasGroup fade;
    [SerializeField, Min(.05f)] float fadeSeconds = 1f;
    [Tooltip("The scene Play loads (it must be in the build's scene list).")]
    [SerializeField] string gameScene = "Room";

    [Header("Set")]
    [Tooltip("The Pixel Look library: the menu's set is drawn with the room's texels and brightness.")]
    [SerializeField] PixelLookLibrary pixelLook;
    [SerializeField, Range(1f, 8f)] float pixelSize = 1f;
    [SerializeField] Animator dungeonMaster;
    [Tooltip("The Dungeon Master's state while he waits (his animator's seated idle).")]
    [SerializeField] string dungeonMasterState = "Sitting Idle";
    [Tooltip("The Dungeon Master's eyes, coloured like the intro's look (IntroController > Looks).")]
    [SerializeField] Renderer[] eyes = new Renderer[0];
    [SerializeField, ColorUsage(false)] Color eyeColour = new(1f, .78f, .4f);
    [Tooltip("The lamp over the table; it breathes a little, like a flame.")]
    [SerializeField] Light lamp;
    [SerializeField, Range(0f, .3f)] float lampFlicker = .06f;
    [SerializeField] AudioSource music;

    float lampBase;
    bool leaving;

    void Start()
    {
        // No GameManager here (it owns the cursor in the game's scene): the menu needs a free pointer.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (pixelLook != null) { pixelLook.SetPixelLighting(true); pixelLook.ApplyGlobals(false, pixelSize); }   // as ScreenManager does in the game
        if (dungeonMaster != null && dungeonMaster.runtimeAnimatorController != null)
        {
            dungeonMaster.applyRootMotion = false;
            dungeonMaster.Play(dungeonMasterState, 0, Random.value);
        }
        var block = new MaterialPropertyBlock();
        foreach (var eye in eyes) { if (eye == null) continue; eye.GetPropertyBlock(block); block.SetColor("_Color", eyeColour); eye.SetPropertyBlock(block); }
        if (lamp != null) lampBase = lamp.intensity;
        if (music != null)
        {
            music.volume *= PlayerPrefs.GetFloat(AudioManager.K_MASTER, 1f) * PlayerPrefs.GetFloat(AudioManager.K_MUSIC, .8f);
            if (!music.isPlaying) music.Play();
        }
        if (playLabel != null) playLabel.text = AdventureSave.SaveExists ? "Continue" : "New game";
        if (playButton != null) { playButton.onClick.AddListener(Play); playButton.Select(); }
        if (skipIntroButton != null) skipIntroButton.onClick.AddListener(SkipIntro);
        if (quitButton != null) quitButton.onClick.AddListener(Quit);
        if (coopButton != null) coopButton.onClick.AddListener(PlayCoop);
        if (fade != null) StartCoroutine(Fade(1f, 0f, null));
        // Started with -host or -join (co-op from the command line): straight into the game.
        foreach (var arg in System.Environment.GetCommandLineArgs())
            if (arg.ToLowerInvariant() == "-host" || arg.ToLowerInvariant() == "-join") { leaving = true; SceneManager.LoadScene(gameScene); break; }
    }

    void Update()
    {
        if (lamp != null && lampFlicker > 0f)
            lamp.intensity = lampBase * (1f + (Mathf.PerlinNoise(Time.unscaledTime * 1.7f, .3f) - .5f) * 2f * lampFlicker);
        // A pad or the keyboard can always reach the buttons, even after a mouse click took the selection away.
        if (!leaving && UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject == null
            && (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame || Gamepad.current != null && Gamepad.current.dpad.ReadValue() != Vector2.zero))
            playButton?.Select();
    }

    void Play()
    {
        if (leaving) return;
        leaving = true;
        StartCoroutine(Fade(0f, 1f, () => SceneManager.LoadScene(gameScene)));
    }

    void PlayCoop()
    {
        if (leaving) return;
        MultiplayerManager.CoopFromMenu = true;
        Play();
    }

    void SkipIntro()
    {
        if (leaving) return;
        ProgressionManager.SkipIntroRequested = true;
        Play();
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

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Player-chosen options from the settings menu, kept in PlayerPrefs. Authored tuning stays
// on the assets (PlayerSettings, camera lenses); these multiply or replace it at runtime.
// Display: window mode and resolution (Screen, which the player also remembers itself), vsync, and brightness, a
// global volume of its own (lift, gamma, gain) over every scene's look, made when a scene loads, so it works in the
// main menu as well as the game.
public static class UserSettings
{
    const string SensitivityKey = "mouse_sensitivity";
    const string FovKey = "field_of_view";
    const string VSyncKey = "vsync";
    const string BrightnessKey = "brightness";

    static bool loaded;
    static float sensitivity = 1f, fov, brightness;
    static bool vsync = true;
    static LiftGammaGain brightnessGrade;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = false; Changed = null; brightnessGrade = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ApplyDisplay()
    {
        Load();
        QualitySettings.vSyncCount = vsync ? 1 : 0;
        ApplyBrightness();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        sensitivity = PlayerPrefs.GetFloat(SensitivityKey, 1f);
        fov = PlayerPrefs.GetFloat(FovKey, 0f);
        vsync = PlayerPrefs.GetInt(VSyncKey, 1) != 0;
        brightness = PlayerPrefs.GetFloat(BrightnessKey, 0f);
    }

    public static bool VSync
    {
        get { Load(); return vsync; }
        set
        {
            Load();
            vsync = value;
            PlayerPrefs.SetInt(VSyncKey, value ? 1 : 0);
            QualitySettings.vSyncCount = value ? 1 : 0;
            Changed?.Invoke();
        }
    }

    // -1 to 1, 0 = as authored: lifts or lowers the mid-tones of the whole picture.
    public static float Brightness
    {
        get { Load(); return brightness; }
        set
        {
            Load();
            brightness = Mathf.Clamp(value, -1f, 1f);
            PlayerPrefs.SetFloat(BrightnessKey, brightness);
            ApplyBrightness();
            Changed?.Invoke();
        }
    }

    static void ApplyBrightness()
    {
        if (brightnessGrade == null)
        {
            var go = new GameObject("User brightness");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.DontSave;
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20000;   // over every look, the death effect included
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            volume.sharedProfile = profile;
            brightnessGrade = profile.Add<LiftGammaGain>();
        }
        // Gamma's w moves the mid-tones: up for brighter, so a dark dungeon reads without washing out its blacks.
        brightnessGrade.gamma.overrideState = !Mathf.Approximately(brightness, 0f);
        brightnessGrade.gamma.value = new Vector4(1f, 1f, 1f, brightness * .4f);
    }

    // Fullscreen (borderless, the desktop's size) or a window.
    public static bool Fullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

    public static void SetDisplay(bool fullscreen, int width, int height)
    {
        Screen.SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
        Changed?.Invoke();
    }

    // Multiplier on PlayerSettings.mouseSensitivity. 1 = as authored.
    public static float MouseSensitivity
    {
        get { Load(); return sensitivity; }
        set
        {
            Load();
            sensitivity = Mathf.Clamp(value, .1f, 4f);
            PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
            Changed?.Invoke();
        }
    }

    // Vertical field of view in degrees. 0 = each camera's authored lens.
    public static float FieldOfView
    {
        get { Load(); return fov; }
        set
        {
            Load();
            fov = value <= 0f ? 0f : Mathf.Clamp(value, 50f, 110f);
            PlayerPrefs.SetFloat(FovKey, fov);
            Changed?.Invoke();
        }
    }
}

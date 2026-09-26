using System;
using UnityEngine;

// Player-chosen options from the settings menu, kept in PlayerPrefs. Authored tuning stays
// on the assets (PlayerSettings, camera lenses); these multiply or replace it at runtime.
public static class UserSettings
{
    const string SensitivityKey = "mouse_sensitivity";
    const string FovKey = "field_of_view";

    static bool loaded;
    static float sensitivity = 1f, fov;

    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { loaded = false; Changed = null; }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        sensitivity = PlayerPrefs.GetFloat(SensitivityKey, 1f);
        fov = PlayerPrefs.GetFloat(FovKey, 0f);
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

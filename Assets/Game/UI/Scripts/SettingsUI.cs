using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Volume, mouse, field of view, pixelation and key bindings. Values apply immediately
// and persist in PlayerPrefs through AudioManager, UserSettings, ScreenManager and InputManager.
public class SettingsUI : MonoBehaviour
{
    [SerializeField] GameObject root;
    [Header("Audio")]
    [SerializeField] Slider masterVolume;
    [SerializeField] Slider musicVolume;
    [SerializeField] Slider sfxVolume;
    [Header("Controls")]
    [SerializeField] Slider sensitivity;
    [SerializeField] TMP_Text sensitivityValue;
    [SerializeField] Slider fieldOfView;
    [SerializeField] TMP_Text fieldOfViewValue;
    [SerializeField] RebindRowUI[] bindings;
    [SerializeField] Button resetBindings;
    [Header("Display")]
    [SerializeField] Slider pixelation;
    [SerializeField] TMP_Text pixelationValue;
    [SerializeField] Button backButton;

    // Slider index -> ScreenManager pixel lines (-1 authored, 0 off). Left is sharpest.
    static readonly int[] PixelSteps = { 0, 720, 480, 360, 320, 240, 180, -1 };
    const float FovDefaultSlot = 49f; // leftmost slider position means "authored lens"

    bool loading, loaded;
    public bool IsOpen => root != null && root.activeSelf;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        if (pixelation != null) { pixelation.wholeNumbers = true; pixelation.minValue = 0; pixelation.maxValue = PixelSteps.Length - 1; }
        if (fieldOfView != null) { fieldOfView.wholeNumbers = true; fieldOfView.minValue = FovDefaultSlot; fieldOfView.maxValue = 110; }
        if (sensitivity != null) { sensitivity.minValue = .1f; sensitivity.maxValue = 4f; }
        Hook(masterVolume, v => SetVolume(AudioManager.Channel.Master, v));
        Hook(musicVolume, v => SetVolume(AudioManager.Channel.Music, v));
        Hook(sfxVolume, v => SetVolume(AudioManager.Channel.Sfx, v));
        Hook(sensitivity, v => { UserSettings.MouseSensitivity = v; Labels(); });
        Hook(fieldOfView, v => { UserSettings.FieldOfView = v <= FovDefaultSlot ? 0f : v; Labels(); });
        Hook(pixelation, v =>
        {
            if (ScreenManager.HasInstance) ScreenManager.Instance.SetUserPixelLines(PixelStep(v));
            Labels();
        });
        resetBindings?.onClick.AddListener(() =>
        {
            if (InputManager.HasInstance) InputManager.Instance.ResetBindings();
            RefreshBindings();
        });
        backButton?.onClick.AddListener(Close);
    }

    void Hook(Slider slider, System.Action<float> apply)
    {
        // Only a player moving a slider on the open screen applies it. Unity also invokes the
        // callback while enabling or rebuilding sliders, before Open has loaded the real values.
        if (slider != null) slider.onValueChanged.AddListener(v => { if (!loading && loaded && IsOpen) apply(v); });
    }

    static int PixelStep(float v) => PixelSteps[Mathf.Clamp(Mathf.RoundToInt(v), 0, PixelSteps.Length - 1)];

    static void SetVolume(AudioManager.Channel channel, float v)
    {
        if (AudioManager.HasInstance) AudioManager.Instance.SetVolume(channel, v);
    }

    public void Open()
    {
        if (root == null) return;
        root.SetActive(true);
        loading = true;
        if (AudioManager.HasInstance)
        {
            if (masterVolume != null) masterVolume.value = AudioManager.Instance.GetVolume(AudioManager.Channel.Master);
            if (musicVolume != null) musicVolume.value = AudioManager.Instance.GetVolume(AudioManager.Channel.Music);
            if (sfxVolume != null) sfxVolume.value = AudioManager.Instance.GetVolume(AudioManager.Channel.Sfx);
        }
        if (sensitivity != null) sensitivity.value = UserSettings.MouseSensitivity;
        if (fieldOfView != null) fieldOfView.value = UserSettings.FieldOfView <= 0f ? FovDefaultSlot : UserSettings.FieldOfView;
        if (pixelation != null)
        {
            int current = ScreenManager.HasInstance ? ScreenManager.Instance.UserPixelLines : -1;
            int index = System.Array.IndexOf(PixelSteps, current);
            pixelation.value = index < 0 ? PixelSteps.Length - 1 : index;
        }
        loading = false;
        loaded = true;
        Labels();
        RefreshBindings();
        masterVolume?.Select();
    }

    public void Close()
    {
        if (InputManager.HasInstance) InputManager.Instance.CancelRebind();
        loaded = false;
        if (root != null) root.SetActive(false);
    }

    void Labels()
    {
        if (sensitivityValue != null && sensitivity != null) sensitivityValue.text = $"{sensitivity.value:0.00}x";
        if (fieldOfViewValue != null && fieldOfView != null) fieldOfViewValue.text = fieldOfView.value <= FovDefaultSlot ? "Default" : $"{fieldOfView.value:0} deg";
        if (pixelationValue != null && pixelation != null)
        {
            int lines = PixelStep(pixelation.value);
            pixelationValue.text = lines < 0 ? "Default" : lines == 0 ? "Off" : $"{lines} lines";
        }
    }

    void RefreshBindings()
    {
        if (bindings == null) return;
        foreach (var row in bindings) row?.Refresh();
    }
}

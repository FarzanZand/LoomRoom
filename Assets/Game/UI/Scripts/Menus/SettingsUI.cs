using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Volume, mouse, field of view, display and key bindings. Values apply immediately and persist in PlayerPrefs through
// AudioManager, UserSettings, ScreenManager and InputManager. One prefab (UI/Prefabs/Settings screen) serves the pause
// menu and the main menu; the main menu has no AudioManager or ScreenManager, so volumes and pixelation are then read
// and written straight to PlayerPrefs (the game picks them up when it loads).
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
    [Tooltip("Click to switch between Fullscreen and Windowed.")]
    [SerializeField] Button windowMode;
    [Tooltip("Click for the next resolution the screen offers.")]
    [SerializeField] Button resolution;
    [SerializeField] Button vsync;
    [SerializeField] Slider brightness;
    [SerializeField] TMP_Text brightnessValue;
    [SerializeField] Button backButton;

    // Slider index -> ScreenManager pixel lines (-1 authored, 0 off). The leftmost position is the default; then from the
    // sharpest picture to the chunkiest.
    static readonly int[] PixelSteps = { -1, 0, 720, 480, 360, 320, 240, 180 };
    // Field of view runs 50-110; its Default is where the table camera's own lens is (85): there each camera keeps its
    // authored lens, and moving off it goes on smoothly from there.
    const float FovDefaultSlot = 85f;

    bool loading, loaded;
    public bool IsOpen => root != null && root.activeSelf;
    public event System.Action Closed;

    void Awake()
    {
        if (root != null) root.SetActive(false);
        if (pixelation != null) { pixelation.wholeNumbers = true; pixelation.minValue = 0; pixelation.maxValue = PixelSteps.Length - 1; }
        if (fieldOfView != null) { fieldOfView.wholeNumbers = true; fieldOfView.minValue = 50; fieldOfView.maxValue = 110; }
        if (sensitivity != null) { sensitivity.minValue = .1f; sensitivity.maxValue = 4f; }
        if (brightness != null) { brightness.minValue = -1f; brightness.maxValue = 1f; }
        Hook(masterVolume, v => SetVolume(AudioManager.Channel.Master, v));
        Hook(musicVolume, v => SetVolume(AudioManager.Channel.Music, v));
        Hook(sfxVolume, v => SetVolume(AudioManager.Channel.Sfx, v));
        Hook(sensitivity, v => { UserSettings.MouseSensitivity = v; Labels(); });
        Hook(fieldOfView, v => { UserSettings.FieldOfView = Mathf.Approximately(v, FovDefaultSlot) ? 0f : v; Labels(); });
        Hook(pixelation, v =>
        {
            int lines = PixelStep(v);
            if (ScreenManager.HasInstance) ScreenManager.Instance.SetUserPixelLines(lines);
            else PlayerPrefs.SetInt(ScreenManager.PixelLinesPref, lines);
            Labels();
        });
        Hook(brightness, v => { UserSettings.Brightness = v; Labels(); });
        windowMode?.onClick.AddListener(() =>
        {
            // Fullscreen fills the desktop at its own size; a window keeps the size it has now.
            bool full = !UserSettings.Fullscreen;
            var size = full ? new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height) : new Vector2Int(Screen.width, Screen.height);
            UserSettings.SetDisplay(full, size.x, size.y);
            Labels();
        });
        resolution?.onClick.AddListener(NextResolution);
        vsync?.onClick.AddListener(() => { UserSettings.VSync = !UserSettings.VSync; Labels(); });
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
        else AudioManager.StoreVolume(channel, v);
    }

    static float GetVolume(AudioManager.Channel channel) =>
        AudioManager.HasInstance ? AudioManager.Instance.GetVolume(channel) : AudioManager.StoredVolume(channel);

    // The screen's sizes, each once, smallest first.
    static List<Vector2Int> Resolutions()
    {
        var list = new List<Vector2Int>();
        foreach (var r in Screen.resolutions)
        {
            var size = new Vector2Int(r.width, r.height);
            if (!list.Contains(size)) list.Add(size);
        }
        if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    void NextResolution()
    {
        var list = Resolutions();
        var now = new Vector2Int(Screen.width, Screen.height);
        int current = list.IndexOf(now);
        // A window dragged to a size that isn't listed goes to the next listed size above it.
        if (current < 0) current = list.FindLastIndex(r => r.x < now.x || (r.x == now.x && r.y < now.y));
        var next = list[(current + 1) % list.Count];
        UserSettings.SetDisplay(UserSettings.Fullscreen, next.x, next.y);
        Labels(next);
    }

    public void Open()
    {
        if (root == null) return;
        root.SetActive(true);
        loading = true;
        if (masterVolume != null) masterVolume.value = GetVolume(AudioManager.Channel.Master);
        if (musicVolume != null) musicVolume.value = GetVolume(AudioManager.Channel.Music);
        if (sfxVolume != null) sfxVolume.value = GetVolume(AudioManager.Channel.Sfx);
        if (sensitivity != null) sensitivity.value = UserSettings.MouseSensitivity;
        if (fieldOfView != null) fieldOfView.value = UserSettings.FieldOfView <= 0f ? FovDefaultSlot : UserSettings.FieldOfView;
        if (pixelation != null)
        {
            int current = ScreenManager.HasInstance ? ScreenManager.Instance.UserPixelLines : PlayerPrefs.GetInt(ScreenManager.PixelLinesPref, -1);
            int index = System.Array.IndexOf(PixelSteps, current);
            pixelation.value = index < 0 ? 0 : index;
        }
        if (brightness != null) brightness.value = UserSettings.Brightness;
        loading = false;
        loaded = true;
        Labels();
        RefreshBindings();
        // The key list starts at its top (Move forward).
        var list = root.GetComponentInChildren<ScrollRect>(true);
        if (list != null) { Canvas.ForceUpdateCanvases(); list.verticalNormalizedPosition = 1f; }
        keyList = list;
        // Keys and a pad walk the screen top to bottom: the left column, then the key list, then its reset, then Back.
        order.Clear();
        order.AddRange(new Selectable[] { masterVolume, musicVolume, sfxVolume, sensitivity, fieldOfView, pixelation, windowMode, resolution, vsync, brightness });
        if (bindings != null) foreach (var row in bindings) if (row != null) order.Add(row.Button);
        order.Add(resetBindings);
        order.Add(backButton);
        MenuFocus.Chain(order);
        masterVolume?.Select();
    }

    readonly List<Selectable> order = new();
    ScrollRect keyList;

    void Update()
    {
        if (!IsOpen) return;
        MenuFocus.Tint(order);
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null) MenuFocus.Follow(keyList, events.currentSelectedGameObject);
    }

    public void Close()
    {
        if (InputManager.HasInstance) InputManager.Instance.CancelRebind();
        bool was = IsOpen;
        loaded = false;
        if (root != null) root.SetActive(false);
        if (was) Closed?.Invoke();
    }

    void Labels() => Labels(new Vector2Int(Screen.width, Screen.height));

    // shownSize: the resolution just asked for (Screen.width only changes on the next frame).
    void Labels(Vector2Int shownSize)
    {
        if (sensitivityValue != null && sensitivity != null) sensitivityValue.text = $"{sensitivity.value:0.00}x";
        if (fieldOfViewValue != null && fieldOfView != null) fieldOfViewValue.text = Mathf.Approximately(fieldOfView.value, FovDefaultSlot) ? "Default" : $"{fieldOfView.value:0} deg";
        if (pixelationValue != null && pixelation != null)
        {
            int lines = PixelStep(pixelation.value);
            pixelationValue.text = lines < 0 ? "Default" : lines == 0 ? "Off" : $"{lines} lines";
        }
        if (brightnessValue != null && brightness != null)
            brightnessValue.text = Mathf.Abs(brightness.value) < .01f ? "Default" : $"{brightness.value * 100f:+0;-0}%";
        SetLabel(windowMode, UserSettings.Fullscreen ? "Fullscreen" : "Windowed");
        SetLabel(resolution, $"{shownSize.x} x {shownSize.y}");
        SetLabel(vsync, UserSettings.VSync ? "On" : "Off");
    }

    static void SetLabel(Button button, string text)
    {
        var label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label != null) label.text = text;
    }

    void RefreshBindings()
    {
        if (bindings == null) return;
        foreach (var row in bindings) row?.Refresh();
    }
}

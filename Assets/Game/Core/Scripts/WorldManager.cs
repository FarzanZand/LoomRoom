using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum RoomExitMode
{
    LoopRooms   = 0, // both doors lead into corridors that bring you back into the room
    LockDoors   = 1, // both doors locked
    LockOneDoor = 2, // the apartment door works, the other door is locked
}

// World-level helpers: the directional light, the room's exits and the opening sequence hook.
// Cutscene logic itself lives on CutsceneController subclasses.
public class WorldManager : Singleton<WorldManager>
{
    [Header("Room exits")]
    [OnValueChanged(nameof(OnRoomExitChanged))]
    public RoomExitMode roomExit = RoomExitMode.LoopRooms;
    public HingedDoor apartmentDoor;
    [Tooltip("The room's door that does not lead to the apartment.")]
    public HingedDoor otherDoor;
    [Tooltip("The rest of the apartment. Only active in Lock One Door.")]
    public List<GameObject> apartment = new();
    [Tooltip("Loop corridors and their RoomLoopPortals. Only active in Loop Rooms.")]
    public GameObject loopCorridors;

    [Header("Table level reveal")]
    [InlineEditor, Tooltip("Shared assembly and camera settings for entering dungeon levels. Clear this to use the standard transition.")]
    public TableLevelRevealSettings tableLevelReveal;
    [Tooltip("Designer-editable adventure selection menu prefab.")]
    public TableAdventureMenuView tableAdventureMenu;

    [Header("Opening")]
    public WakeUpCutsceneController wakeUpCutscene;
    public DinnerCutsceneController dinnerCutscene;

    [Header("Lighting")]
    public Light directionalLight;
    [Tooltip("Light colour used while the game starts in the room (night).")]
    public Color roomStartLightColor = Color.black;
    public float roomStartLightIntensity = 0.7f;

    protected override void Awake()
    {
        base.Awake();
        ApplyRoomExit();
        if (ProgressionManager.HasInstance &&
            ProgressionManager.Instance.startingPlayer == PlayerKind.Room &&
            EnsureDirectionalLight())
        {
            directionalLight.color     = roomStartLightColor;
            directionalLight.intensity = roomStartLightIntensity;
        }
    }

    void Start()
    {
        var progression = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        bool skip = progression != null &&
            (progression.skipWakeUp || progression.startingPlayer != PlayerKind.Room);
        if (!skip && wakeUpCutscene != null)
            wakeUpCutscene.Play();
    }

    [Button]
    public void PlayDinnerCutscene()
    {
        if (dinnerCutscene != null) dinnerCutscene.Play();
    }

    // ── Room exits ────────────────────────────────────────────────────

    public void SetRoomExit(RoomExitMode mode)
    {
        roomExit = mode;
        ApplyRoomExit();
    }

    void OnRoomExitChanged()
    {
        if (Application.isPlaying) ApplyRoomExit();
    }

    void ApplyRoomExit()
    {
        if (loopCorridors != null) loopCorridors.SetActive(roomExit == RoomExitMode.LoopRooms);
        foreach (var part in apartment)
            if (part != null) part.SetActive(roomExit == RoomExitMode.LockOneDoor);
        if (apartmentDoor != null) apartmentDoor.Locked = roomExit == RoomExitMode.LockDoors;
        if (otherDoor != null)     otherDoor.Locked = roomExit != RoomExitMode.LoopRooms;
    }

    // ── Directional light fades ───────────────────────────────────────

    Coroutine lightFadeRoutine;
    Coroutine lightColorRoutine;

    bool EnsureDirectionalLight()
    {
        if (directionalLight != null) return true;
        directionalLight = RenderSettings.sun;
        if (directionalLight == null)
            foreach (var light in FindObjectsByType<Light>())
                if (light.type == LightType.Directional) { directionalLight = light; break; }
        if (directionalLight == null)
            Debug.LogWarning("WorldManager: no directional light assigned or found in scene.", this);
        return directionalLight != null;
    }

    public void FadeDirectionalLight(float to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
        FadeDirectionalLight(directionalLight.intensity, to, duration);
    }

    public void FadeDirectionalLight(float from, float to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
        if (lightFadeRoutine != null) StopCoroutine(lightFadeRoutine);
        lightFadeRoutine = StartCoroutine(FadeIntensity(from, to, duration));
    }

    public void FadeDirectionalLightColor(Color to, float duration)
    {
        if (!EnsureDirectionalLight()) return;
        if (lightColorRoutine != null) StopCoroutine(lightColorRoutine);
        lightColorRoutine = StartCoroutine(FadeColor(to, duration));
    }

    IEnumerator FadeIntensity(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            directionalLight.intensity = Mathf.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        directionalLight.intensity = to;
        lightFadeRoutine = null;
    }

    IEnumerator FadeColor(Color to, float duration)
    {
        Color from = directionalLight.color;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            directionalLight.color = Color.Lerp(from, to, elapsed / duration);
            yield return null;
        }
        directionalLight.color = to;
        lightColorRoutine = null;
    }
}

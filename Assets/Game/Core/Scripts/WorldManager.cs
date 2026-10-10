using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public enum RoomExitMode
{
    LoopRooms   = 0, // both doors lead into corridors that bring you back into the room
    LockDoors   = 1, // both doors locked
    LockOneDoor = 2, // the apartment door works, the other door is locked
}

// World-level settings: the room's exits, head bob, the table reveal and menu, and which opening plays
// (intro, wake-up). The sun and moods belong to LightingManager; cutscene logic lives on
// CutsceneController subclasses.
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
    [Tooltip("Winning a run: the last morning.")]
    public EndingController ending;

    [FoldoutGroup("Head Bob"), HideLabel, InlineProperty]
    [Tooltip("Camera bob while walking, running and crouch-walking. Read live by HeadBob on each player's camera.")]
    public HeadBobSettings headBob = new();

    [Header("Testing")]
    [Tooltip("The level the editor's Dungeon button generates and plays straight away, skipping the room.")]
    public TableLevelData debugDungeon;

    protected override void Awake()
    {
        base.Awake();
        ApplyRoomExit();
    }

    void Start()
    {
        var progression = ProgressionManager.HasInstance ? ProgressionManager.Instance : null;
        // The editor's Debug and Dungeon buttons go straight to the table.
        bool skip = DebugDungeonSession.Active || progression != null &&
            (progression.skipWakeUp || progression.startingPlayer != PlayerKind.Room);
        // The first launch opens at the table in the dark instead (IntroController).
        bool intro = !skip && IntroController.HasInstance && IntroController.Instance.TryPlay();
        if (!skip && !intro && wakeUpCutscene != null) wakeUpCutscene.Play(brief: false);
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
}

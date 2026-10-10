using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Where an editor Play session picks up the story. Serialized by integer: append only.
public enum ProgressionStart
{
    NewGame = 0,             // the intro in the dark, then the tutorial dungeon
    AfterTutorial = 1,       // the first morning: the next seat at the table plays the character sheet and dice
    AfterCharacterSheet = 2, // after the dice run: ordinary mornings, straight to the figures
}

// Story flags. Items, interactions, dialogue and cutscenes set and read named flags
// here; nothing else needs a bespoke bool. AdventureSave stores them as a list of
// (key, value) with the adventure and puts them back on load.
public class ProgressionManager : Singleton<ProgressionManager>
{
    [Serializable]
    public class FlagEntry
    {
        public string key;
        public int    value = 1;
    }

    [Header("Start")]
    public PlayerKind startingPlayer = PlayerKind.Room;
    [Tooltip("Skip the wake-up cutscene when entering play mode.")]
    public bool skipWakeUp = false;
    [Tooltip("Editor only: fast-forward the story by setting the flags that point leaves behind (on top of any save).")]
    public ProgressionStart startAt = ProgressionStart.NewGame;

    [Header("Flags")]
    [Tooltip("Flags set before play starts. Runtime changes go to the dictionary, not this list.")]
    [ListDrawerSettings(ShowFoldout = true)]
    [SerializeField] List<FlagEntry> initialFlags = new();

    [ShowInInspector, ReadOnly]
    readonly Dictionary<string, int> flags = new();

    public event Action<string, int> FlagChanged;

    public const string TableEnteredFlag = "tableEntered";

    // Kept as a property so existing scripts read naturally.
    public bool tableEntered
    {
        get => HasFlag(TableEnteredFlag);
        set => SetFlag(TableEnteredFlag, value ? 1 : 0);
    }

    protected override void Awake()
    {
        base.Awake();
        foreach (var f in initialFlags)
            if (!string.IsNullOrEmpty(f.key)) flags[f.key] = f.value;
        if (Application.isEditor) FastForward(startAt);
    }

    void FastForward(ProgressionStart point)
    {
        if (point >= ProgressionStart.AfterTutorial)
        {
            flags[IntroController.IntroFlag] = 1;
            flags[IntroController.RoomOpenFlag] = 1;
            flags[DungeonMasterRemarks.SatFlag] = 1;
            flags[TableEnteredFlag] = 1;
        }
        if (point >= ProgressionStart.AfterCharacterSheet)
        {
            flags[IntroController.SheetFlag] = 1;
            if (GetFlag("adventure.deepestFloor") < 1) flags["adventure.deepestFloor"] = 1;
        }
    }

    public int  GetFlag(string key) => flags.TryGetValue(key, out var v) ? v : 0;
    public bool HasFlag(string key) => GetFlag(key) != 0;

    public void SetFlag(string key, int value = 1)
    {
        if (string.IsNullOrEmpty(key)) return;
        flags.TryGetValue(key, out var old);
        if (old == value && flags.ContainsKey(key)) return;
        flags[key] = value;
        FlagChanged?.Invoke(key, value);
    }

    public void AddToFlag(string key, int delta) => SetFlag(key, GetFlag(key) + delta);

    public IReadOnlyDictionary<string, int> AllFlags => flags;
}

using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Story flags. Items, interactions, dialogue and cutscenes set and read named flags
// here; nothing else needs a bespoke bool. Designed so the whole thing serialises to
// a list of (key, value) for the save system later.
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

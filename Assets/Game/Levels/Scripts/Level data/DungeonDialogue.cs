using System;
using Sirenix.OdinInspector;
using UnityEngine;

// What makes the Dungeon Master speak during a run on a level. Serialized by integer: append only.
public enum DungeonDialogueTrigger
{
    RunStart = 0, Kill = 1, Backstab = 2, CloseCall = 3, LevelUp = 4, BetterGear = 5,
    NewDeepestFloor = 6, EnterRoom = 7, Hurt = 8, PickUp = 9,
}

// One thing the Dungeon Master may say on a level: when, how likely, and the lines to pick from.
// Held in TableLevelData's Dialogue tab; DungeonMasterRemarks listens to the run and says them.
[Serializable]
public class DungeonDialogue
{
    [HorizontalGroup("When"), HideLabel] public DungeonDialogueTrigger trigger;
    [HorizontalGroup("When", 80), LabelWidth(40), ShowIf(nameof(IsRoom)), Tooltip("The room's number on an authored floor plan (0 is the entrance).")]
    public int room;
    [HorizontalGroup("When", 130), LabelWidth(50), Range(0, 1), Tooltip("Chance it is said when the trigger happens.")]
    public float chance = 1f;
    [HorizontalGroup("When", 110), LabelWidth(90), Tooltip("Said at most once a run.")]
    public bool oncePerRun = true;
    [TextArea(1, 3), Tooltip("One is picked at random each time.")]
    public string[] lines = { "" };
    [Tooltip("Said across the table, without the \"voice inside your head\" lead-in.")]
    public bool inPerson;

    bool IsRoom => trigger == DungeonDialogueTrigger.EnterRoom;

    public DungeonMaster.Line Pick()
    {
        if (lines == null || lines.Length == 0) return null;
        var text = lines[UnityEngine.Random.Range(0, lines.Length)];
        return string.IsNullOrWhiteSpace(text) ? null : new DungeonMaster.Line { text = text, inPerson = inPerson, mumble = true };
    }
}

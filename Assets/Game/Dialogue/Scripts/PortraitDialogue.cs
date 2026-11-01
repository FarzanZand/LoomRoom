using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Who speaks in a portrait dialogue (PortraitDialogueUI): the boss of the fight it belongs to, any other model (a
// dungeon NPC), or the Dungeon Master, whose lines go to his own feed as always, never into the box. Serialized by
// number: append only.
public enum PortraitSpeaker { Boss = 0, DungeonMaster = 1, Other = 2 }

// One box of a portrait dialogue: the speaker's upper body idles at its left, their name on a plate above it, the text
// typed out. A line can end with answers for the player to pick; each answer has its own reply boxes, and the
// dialogue goes on with the next line after them.
[Serializable]
public class PortraitLine
{
    [HorizontalGroup("Who"), HideLabel] public PortraitSpeaker speaker;
    [HorizontalGroup("Who"), HideLabel, Tooltip("The name on the box. Empty: the speaker's own (the boss's name on the fight, or Dungeon Master).")]
    public string name;
    [ShowIf("@speaker == PortraitSpeaker.Other"), Tooltip("The model whose upper body shows (a character prefab or anything with an Animator).")]
    public GameObject model;
    [TextArea(2, 4), HideLabel] public string text;
    [Tooltip("Answers the player picks from after this box. None: a click moves on.")]
    public List<PortraitChoice> choices = new();
}

[Serializable]
public class PortraitChoice
{
    [Tooltip("What the answer button says.")] public string label;
    [Tooltip("The boxes said after this answer, before the dialogue goes on.")] public List<PortraitReply> reply = new();
}

// A box said after an answer (as a line, without answers of its own).
[Serializable]
public class PortraitReply
{
    [HorizontalGroup("Who"), HideLabel] public PortraitSpeaker speaker;
    [HorizontalGroup("Who"), HideLabel, Tooltip("The name on the box. Empty: the speaker's own.")]
    public string name;
    [ShowIf("@speaker == PortraitSpeaker.Other")] public GameObject model;
    [TextArea(2, 4), HideLabel] public string text;
}

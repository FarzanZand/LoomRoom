using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Who speaks in a portrait dialogue (PortraitDialogueUI): the boss of the fight it belongs to, any other model (a
// dungeon NPC), or the Dungeon Master, whose lines go to his own feed as always, never into the box. Serialized by
// number: append only.
public enum PortraitSpeaker { Boss = 0, DungeonMaster = 1, Other = 2 }

// Where the dialogue box sits (PortraitDialogueUI). Standard: the wide box along the foot of the screen with the big
// portrait at its left. The corners are a box half as wide with a portrait half as big (the text keeps its size), out
// of the middle of the view, for lines said while the fight goes on: Bottom Left above the health bars, Bottom Right
// above the hotbar's right end, Upper Left and Upper Right below the top bars (Upper Right left of the minimap and the quest log). Serialized by
// number: append only. Simple: no box, the line as a subtitle in the lower middle with the speaker's name in front (a
// line said in the middle of a fight; a conversation with answers set Simple still uses the Standard box).
public enum DialoguePlacement { Standard = 0, BottomLeft = 1, BottomRight = 2, UpperLeft = 3, UpperRight = 4, Simple = 5 }

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
    [Tooltip("A recorded voice played as the box shows, in place of the typing blips (on a Dungeon Master box, in place of his mumble). Empty: the blips.")]
    public AudioData voice;
    [Tooltip("Answers the player picks from after this box. None: a click moves on.")]
    public List<PortraitChoice> choices = new();
}

[Serializable]
public class PortraitChoice
{
    [Tooltip("What the answer button says.")] public string label;
    [Tooltip("Escape picks this answer (one that walks away, such as Never mind). With none, Escape opens the pause menu.")]
    public bool leaves;
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
    [Tooltip("A recorded voice played as the box shows, in place of the typing blips (on a Dungeon Master box, in place of his mumble). Empty: the blips.")]
    public AudioData voice;
}
